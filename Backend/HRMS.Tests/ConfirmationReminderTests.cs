using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests;

/// <summary>
/// Phase 9b — Confirmation &amp; Probation reminder scheduler: window/date calculation, policy-driven
/// candidacy, claim + deduplication, retry/backoff, recipient resolution (employee / manager / HRBP),
/// multi-tenant isolation and missing-recipient handling. SQLite proves the logic; provider locking parity
/// is covered by the opt-in MySQL/SQL Server suites.
/// </summary>
public sealed class ConfirmationReminderTests
{
    private static readonly DateOnly Doj = new(2026, 1, 15);
    private static readonly DateOnly ProbationEnd = new(2026, 7, 15);   // Doj + 6 months
    private static readonly DateOnly WithinWindow = new(2026, 6, 20);   // after trigger (2026-06-15), before end

    // ── Window / candidacy ──────────────────────────────────────────────────────

    [Fact]
    public async Task Raises_reminder_within_window_and_records_sent_delivery()
    {
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAsync(ctx, tenant);
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddPublishedPolicyAsync(ctx, tenant, probationMonths: 6);

        var stub = new StubNotifications { Recipients = { Recipient(employee) } };
        var result = await Processor(ctx, tenant, stub, WithinWindow).ProcessAsync();

        Assert.Equal(1, result.Cases);
        Assert.Equal(1, result.Sent);
        var row = await ctx.ConfirmationReminderDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal("Sent", row.Status);
        Assert.Equal(ProbationEnd, row.ProbationEndDate);
        Assert.Equal(1, row.AttemptCount);
        Assert.NotNull(row.SentAtUtc);
    }

    [Fact]
    public async Task Skips_employee_before_trigger_window()
    {
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAsync(ctx, tenant);
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddPublishedPolicyAsync(ctx, tenant, probationMonths: 6);

        var stub = new StubNotifications { Recipients = { Recipient(employee) } };
        var result = await Processor(ctx, tenant, stub, new DateOnly(2026, 2, 1)).ProcessAsync();

        Assert.Equal(0, result.Cases);
        Assert.Equal(0, stub.SendCalls);
        Assert.False(await ctx.ConfirmationReminderDeliveries.AnyAsync());
    }

    [Fact]
    public async Task Skips_employee_with_no_applicable_policy()
    {
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAsync(ctx, tenant);
        await AddEmployeeAsync(ctx, tenant, employee);

        var stub = new StubNotifications { Recipients = { Recipient(employee) } };
        var result = await Processor(ctx, tenant, stub, WithinWindow).ProcessAsync();

        Assert.Equal(0, result.Cases);
        Assert.Equal(0, stub.SendCalls);
    }

    [Fact]
    public async Task Excludes_confirmed_and_inactive_employees()
    {
        var tenant = Guid.NewGuid(); var confirmed = Guid.NewGuid(); var inactive = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAsync(ctx, tenant);
        await AddEmployeeAsync(ctx, tenant, confirmed, confirmationDate: new DateOnly(2026, 6, 1));
        await AddEmployeeAsync(ctx, tenant, inactive, status: EmployeeStatus.Resigned);
        await AddPublishedPolicyAsync(ctx, tenant, probationMonths: 6);

        var stub = new StubNotifications { Recipients = { Recipient(confirmed), Recipient(inactive) } };
        var result = await Processor(ctx, tenant, stub, WithinWindow).ProcessAsync();

        Assert.Equal(0, result.Cases);
        Assert.Equal(0, stub.SendCalls);
    }

    // ── Deduplication ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Does_not_resend_an_already_sent_occurrence()
    {
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAsync(ctx, tenant);
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddPublishedPolicyAsync(ctx, tenant, probationMonths: 6);
        var stub = new StubNotifications { Recipients = { Recipient(employee) } };

        var first = await Processor(ctx, tenant, stub, WithinWindow).ProcessAsync();
        var second = await Processor(ctx, tenant, stub, new DateOnly(2026, 6, 21)).ProcessAsync();

        Assert.Equal(1, first.Sent);
        Assert.Equal(0, second.Sent);
        Assert.Equal(1, stub.SendCalls);
        Assert.Equal(1, await ctx.ConfirmationReminderDeliveries.CountAsync());
    }

    // ── Retry / backoff ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Failed_delivery_is_retried_only_after_backoff()
    {
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAsync(ctx, tenant);
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddPublishedPolicyAsync(ctx, tenant, probationMonths: 6);

        var fail = true;
        var stub = new StubNotifications
        {
            Recipients = { Recipient(employee) },
            OnSend = (_, _) => fail ? ConfirmationNotificationDeliveryResult.Failed : ConfirmationNotificationDeliveryResult.Sent
        };
        var clock = new MutableClock(WithinWindow);

        var first = await Processor(ctx, tenant, stub, clock).ProcessAsync();
        Assert.Equal(1, first.Failed);
        var failedRow = await ctx.ConfirmationReminderDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal("Failed", failedRow.Status);
        Assert.NotNull(failedRow.NextAttemptAtUtc);

        // Still inside the backoff window: no retry.
        clock.Advance(TimeSpan.FromMinutes(5));
        var tooSoon = await Processor(ctx, tenant, stub, clock).ProcessAsync();
        Assert.Equal(0, tooSoon.Sent);
        Assert.Equal(0, tooSoon.Failed);
        Assert.Equal(1, stub.SendCalls);

        // After the backoff the occurrence is reclaimed and now succeeds.
        fail = false;
        clock.Advance(TimeSpan.FromMinutes(90));
        var retry = await Processor(ctx, tenant, stub, clock).ProcessAsync();
        Assert.Equal(1, retry.Sent);
        var sentRow = await ctx.ConfirmationReminderDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal("Sent", sentRow.Status);
        Assert.Equal(2, sentRow.AttemptCount);
    }

    [Fact]
    public async Task Abandons_occurrence_after_max_attempts()
    {
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAsync(ctx, tenant);
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddPublishedPolicyAsync(ctx, tenant, probationMonths: 6);

        var stub = new StubNotifications
        {
            Recipients = { Recipient(employee) },
            OnSend = (_, _) => ConfirmationNotificationDeliveryResult.Failed
        };
        var clock = new MutableClock(WithinWindow);
        var options = new ConfirmationReminderOptions { Enabled = true, MaxAttempts = 2, RetryBackoffMinutes = 60 };

        await Processor(ctx, tenant, stub, clock, options).ProcessAsync();   // attempt 1
        clock.Advance(TimeSpan.FromMinutes(90));
        await Processor(ctx, tenant, stub, clock, options).ProcessAsync();   // attempt 2 → reaches max
        clock.Advance(TimeSpan.FromMinutes(90));
        await Processor(ctx, tenant, stub, clock, options).ProcessAsync();   // blocked

        Assert.Equal(2, stub.SendCalls);
        var row = await ctx.ConfirmationReminderDeliveries.AsNoTracking().SingleAsync();
        Assert.Equal(2, row.AttemptCount);
        Assert.Equal("Failed", row.Status);
    }

    // ── Recipients / counts ─────────────────────────────────────────────────────

    [Fact]
    public async Task Counts_case_but_skips_when_no_recipient_resolves()
    {
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAsync(ctx, tenant);
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddPublishedPolicyAsync(ctx, tenant, probationMonths: 6);

        var stub = new StubNotifications();   // resolves no recipients
        var result = await Processor(ctx, tenant, stub, WithinWindow).ProcessAsync();

        Assert.Equal(1, result.Cases);
        Assert.Equal(0, result.Sent);
        Assert.Equal(1, result.Skipped);
        Assert.False(await ctx.ConfirmationReminderDeliveries.AnyAsync());
    }

    [Fact]
    public async Task Notifies_each_distinct_recipient_once()
    {
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var manager = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAsync(ctx, tenant);
        // The manager is a confirmed employee (a valid FK target) so only the probationary employee is a case.
        await AddEmployeeAsync(ctx, tenant, manager, confirmationDate: new DateOnly(2025, 1, 1));
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddPublishedPolicyAsync(ctx, tenant, probationMonths: 6);

        // RecipientEmployeeId is a real FK, so employee/manager point at seeded employees; an HRBP without a
        // linked employee is modelled as a null-backed address.
        var stub = new StubNotifications
        {
            Recipients =
            {
                new(ConfirmationReminderRecipientType.Employee, "e@test.local", employee),
                new(ConfirmationReminderRecipientType.Manager, "m@test.local", manager),
                new(ConfirmationReminderRecipientType.Hrbp, "hr@test.local", null)
            }
        };
        var result = await Processor(ctx, tenant, stub, WithinWindow).ProcessAsync();

        Assert.Equal(1, result.Cases);
        Assert.Equal(3, result.Sent);
        Assert.Equal(3, await ctx.ConfirmationReminderDeliveries.CountAsync());
        Assert.Equal(3, await ctx.ConfirmationReminderDeliveries.Select(x => x.OccurrenceKey).Distinct().CountAsync());
    }

    // ── Multi-tenant isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task Does_not_cross_tenant_boundaries()
    {
        var tenantA = Guid.NewGuid(); var employeeA = Guid.NewGuid();
        var tenantB = Guid.NewGuid(); var employeeB = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();

        using (var seed = db.CreateContext(new TestTenantContext(tenantA)))
        {
            await SeedTenantAsync(seed, tenantA);
            await AddEmployeeAsync(seed, tenantA, employeeA);
            await AddPublishedPolicyAsync(seed, tenantA, probationMonths: 6);
        }
        using (var seed = db.CreateContext(new TestTenantContext(tenantB)))
        {
            await SeedTenantAsync(seed, tenantB);
            await AddEmployeeAsync(seed, tenantB, employeeB);
            await AddPublishedPolicyAsync(seed, tenantB, probationMonths: 6);
        }

        using var ctx = db.CreateContext(new TestTenantContext(tenantA));
        var stub = new StubNotifications { Recipients = { Recipient(employeeA), Recipient(employeeB) } };
        var result = await Processor(ctx, tenantA, stub, WithinWindow).ProcessAsync();

        Assert.Equal(1, result.Cases);
        var rows = await ctx.ConfirmationReminderDeliveries.IgnoreQueryFilters().ToListAsync();
        Assert.All(rows, r => Assert.Equal(tenantA, r.TenantId));
        Assert.All(rows, r => Assert.Equal(employeeA, r.EmployeeId));
    }

    // ── Tenant configuration gate ──────────────────────────────────────────────────

    [Fact]
    public async Task Disabled_tenant_setting_suppresses_scanning()
    {
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAsync(ctx, tenant);
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddPublishedPolicyAsync(ctx, tenant, probationMonths: 6);
        ctx.ConfirmationSchedulerSettings.Add(new ConfirmationSchedulerSetting { Id = Guid.NewGuid(), TenantId = tenant, Enabled = false });
        await ctx.SaveChangesAsync();

        var stub = new StubNotifications { Recipients = { Recipient(employee) } };
        var result = await Processor(ctx, tenant, stub, WithinWindow).ProcessAsync();

        Assert.Equal(0, result.Cases);
        Assert.Equal(0, stub.SendCalls);
    }

    [Fact]
    public async Task Frequency_gate_defers_until_due_then_runs()
    {
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAsync(ctx, tenant);
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddPublishedPolicyAsync(ctx, tenant, probationMonths: 6);
        var clock = new MutableClock(WithinWindow);
        ctx.ConfirmationSchedulerSettings.Add(new ConfirmationSchedulerSetting
        {
            Id = Guid.NewGuid(), TenantId = tenant, Enabled = true, FrequencyMinutes = 60,
            LastRunAtUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-10)
        });
        await ctx.SaveChangesAsync();
        var stub = new StubNotifications { Recipients = { Recipient(employee) } };

        var tooSoon = await Processor(ctx, tenant, stub, clock).ProcessAsync();
        Assert.Equal(0, tooSoon.Cases);
        Assert.Equal(0, stub.SendCalls);

        clock.Advance(TimeSpan.FromMinutes(120));
        var due = await Processor(ctx, tenant, stub, clock).ProcessAsync();
        Assert.Equal(1, due.Sent);
    }

    // ── Recipient resolution (real notification service) ───────────────────────────

    [Fact]
    public async Task Notification_service_resolves_employee_manager_and_hrbp()
    {
        var tenant = Guid.NewGuid();
        var employee = Guid.NewGuid(); var manager = Guid.NewGuid(); var hrbpEmp = Guid.NewGuid();
        var employeeUser = Guid.NewGuid(); var managerUser = Guid.NewGuid(); var hrbpUser = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAsync(ctx, tenant);
        await AddEmployeeAsync(ctx, tenant, manager);
        await AddEmployeeAsync(ctx, tenant, hrbpEmp);
        await AddEmployeeAsync(ctx, tenant, employee, managerId: manager);
        await LinkUserAsync(ctx, tenant, employeeUser, employee, "employee@test.local");
        await LinkUserAsync(ctx, tenant, managerUser, manager, "manager@test.local");
        await LinkUserAsync(ctx, tenant, hrbpUser, hrbpEmp, "hrbp@test.local");
        await GrantHrbpAsync(ctx, tenant, hrbpUser);

        var service = NotificationService(ctx, tenant);
        var reviewCase = new ConfirmationReminderCase(tenant, employee, "Test Employee", Guid.NewGuid(), Guid.NewGuid(), "Standard", ProbationEnd, WithinWindow);
        var recipients = await service.ResolveRecipientsAsync(reviewCase, ConfirmationRecipientToggles.All);

        Assert.Contains(recipients, r => r.Type == ConfirmationReminderRecipientType.Employee && r.Email == "employee@test.local");
        Assert.Contains(recipients, r => r.Type == ConfirmationReminderRecipientType.Manager && r.Email == "manager@test.local");
        Assert.Contains(recipients, r => r.Type == ConfirmationReminderRecipientType.Hrbp && r.Email == "hrbp@test.local");
    }

    [Fact]
    public async Task Notification_service_omits_missing_recipients()
    {
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var db = new SqliteInMemoryDatabase();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await SeedTenantAsync(ctx, tenant);
        await AddEmployeeAsync(ctx, tenant, employee);   // no account link, no manager, no HRBP

        var service = NotificationService(ctx, tenant);
        var reviewCase = new ConfirmationReminderCase(tenant, employee, "Test Employee", Guid.NewGuid(), Guid.NewGuid(), "Standard", ProbationEnd, WithinWindow);
        var recipients = await service.ResolveRecipientsAsync(reviewCase, ConfirmationRecipientToggles.All);

        Assert.Empty(recipients);
    }

    // ── Builders ──────────────────────────────────────────────────────────────────

    private static ConfirmationReminderProcessor Processor(HrmsDbContext ctx, Guid tenant, StubNotifications stub, DateOnly today, ConfirmationReminderOptions? options = null)
        => Processor(ctx, tenant, stub, new MutableClock(today), options);

    private static ConfirmationReminderProcessor Processor(HrmsDbContext ctx, Guid tenant, StubNotifications stub, TimeProvider clock, ConfirmationReminderOptions? options = null)
        => new(ctx, new ConfirmationPolicyResolver(ctx, tenantContext: new TestTenantContext(tenant)), stub,
            options ?? new ConfirmationReminderOptions { Enabled = true }, new TestTenantContext(tenant), clock,
            NullLogger<ConfirmationReminderProcessor>.Instance);

    private static ConfirmationReminderNotificationService NotificationService(HrmsDbContext ctx, Guid tenant)
        => new(ctx, new NoopEmailSender(), new EmployeeManagerResolver(ctx, new TestTenantContext(tenant)),
            new ConfirmationHrbpResolver(ctx, tenantContext: new TestTenantContext(tenant)),
            NullLogger<ConfirmationReminderNotificationService>.Instance);

    private static ConfirmationReminderRecipient Recipient(Guid employeeId)
        => new(ConfirmationReminderRecipientType.Employee, employeeId.ToString("N")[..8] + "@test.local", employeeId);

    // ── Seeding ───────────────────────────────────────────────────────────────────

    private static async Task SeedTenantAsync(HrmsDbContext db, Guid tenant)
    {
        db.Tenants.Add(new Tenant
        {
            Id = tenant, TenantCode = Guid.NewGuid().ToString("N")[..8], Host = Guid.NewGuid().ToString("N") + ".local",
            ShardKey = Guid.NewGuid().ToString("N"), TenantName = "Test"
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddEmployeeAsync(HrmsDbContext db, Guid tenant, Guid employee, Guid? managerId = null, DateOnly? confirmationDate = null, EmployeeStatus status = EmployeeStatus.Active)
    {
        db.Employees.Add(new Employee { Id = employee, TenantId = tenant, FirstName = "Test", LastName = "Employee", Email = Guid.NewGuid() + "@test.local", DateOfJoining = Doj, Gender = Gender.Unspecified, Status = status });
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, EffectiveFrom = Doj, ManagerId = managerId, EmploymentStatus = EmployeeStatus.Active, IsSuperseded = false });
        if (confirmationDate is DateOnly confirmed)
            db.EmployeeEmployments.Add(new EmployeeEmployment { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, ConfirmationDate = confirmed, JobStatus = "Confirmed" });
        await db.SaveChangesAsync();
    }

    private static async Task AddPublishedPolicyAsync(HrmsDbContext db, Guid tenant, int probationMonths, int triggerDays = 30)
    {
        var policy = new ConfirmationPolicy { Id = Guid.NewGuid(), TenantId = tenant, Code = Guid.NewGuid().ToString("N")[..8], Name = "Standard", IsActive = true };
        var version = new ConfirmationPolicyVersion { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyId = policy.Id, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = ConfirmationPolicyVersionStatus.Published, Priority = 1 };
        var rule = new ConfirmationPolicyRule { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyVersionId = version.Id, ProbationDuration = probationMonths, ProbationDurationUnit = ProbationDurationUnit.Months, TriggerDaysBeforeEnd = triggerDays };
        db.ConfirmationPolicies.Add(policy);
        db.ConfirmationPolicyVersions.Add(version);
        db.ConfirmationPolicyRules.Add(rule);
        await db.SaveChangesAsync();
    }

    private static async Task LinkUserAsync(HrmsDbContext db, Guid tenant, Guid userId, Guid employeeId, string email)
    {
        var linkId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, TenantId = tenant, Email = email, PasswordHash = "x", FirstName = "U", LastName = "ser" });
        await db.SaveChangesAsync();
        // The current-link row carries an FK to its creation event, so the event is seeded first.
        db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent
        {
            Id = linkId, TenantId = tenant, SubjectUserId = userId, ActorUserId = userId, Sequence = 1,
            Operation = "Link", NewLinkId = linkId, AfterEmployeeId = employeeId, OccurredAtUtc = DateTime.UtcNow,
            Reason = "seed", CorrelationId = Guid.NewGuid().ToString("N")
        });
        db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = tenant, UserId = userId, EmployeeId = employeeId });
        await db.SaveChangesAsync();
    }

    private static async Task GrantHrbpAsync(HrmsDbContext db, Guid tenant, Guid userId)
    {
        var roleId = SeedData.RoleId(RoleNames.HRBP);
        if (!await db.Roles.AnyAsync(r => r.Id == roleId))
            db.Roles.Add(new Role { Id = roleId, Name = RoleNames.HRBP });
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), TenantId = tenant, UserId = userId, RoleId = roleId, EffectiveFrom = DateOnly.MinValue });
        await db.SaveChangesAsync();
    }

    // ── Doubles ───────────────────────────────────────────────────────────────────

    private sealed class StubNotifications : IConfirmationReminderNotificationService
    {
        public List<ConfirmationReminderRecipient> Recipients { get; } = [];
        public Func<ConfirmationReminderCase, ConfirmationReminderRecipient, ConfirmationNotificationDeliveryResult> OnSend { get; set; }
            = (_, _) => ConfirmationNotificationDeliveryResult.Sent;
        public int SendCalls { get; private set; }

        public Task<IReadOnlyList<ConfirmationReminderRecipient>> ResolveRecipientsAsync(ConfirmationReminderCase reviewCase, ConfirmationRecipientToggles toggles, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ConfirmationReminderRecipient>>(Recipients);

        public Task<ConfirmationNotificationDeliveryResult> SendAsync(ConfirmationReminderCase reviewCase, ConfirmationReminderRecipient recipient, CancellationToken cancellationToken = default)
        {
            SendCalls++;
            return Task.FromResult(OnSend(reviewCase, recipient));
        }
    }

    private sealed class NoopEmailSender : IEmailSender
    {
        public Task SendLeaveNotificationAsync(LeaveNotificationEmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendWelcomeInviteAsync(WelcomeEmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> SendPasswordResetOtpAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SendPlatformUsernameReminderAsync(PlatformUsernameReminderMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendPlatformPasswordResetAsync(PlatformPasswordResetEmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendPlatformPasswordChangedAsync(PlatformPasswordChangedEmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MutableClock(DateOnly today) : TimeProvider
    {
        private DateTimeOffset _now = new(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
