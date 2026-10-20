using HRMS.Application.DTOs.Communication;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using HRMS.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace HRMS.Tests;

public sealed class WelcomeInvitationServiceTests
{
    [Fact]
    public async Task List_enriches_invitation_and_employee_data_and_calculates_statuses()
    {
        using var store = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "E1", "Ada", "Lovelace");
        var user = new User { Id = Guid.NewGuid(), TenantId = tenantId, Email = employee.Email, FirstName = employee.FirstName, LastName = employee.LastName, IsActive = false };
        var attempt = NewAttempt(tenantId, employee.Id, WelcomeInvitationStatus.Sent, new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc));
        var invitation = new UserInvitation { Id = Guid.NewGuid(), TenantId = tenantId, UserId = user.Id, TokenHash = "hash", Purpose = UserInvitationPurpose.SetInitialPassword, CreatedAtUtc = attempt.RequestedAtUtc, ExpiresAtUtc = attempt.RequestedAtUtc.AddDays(30), LastSentAtUtc = attempt.SentAtUtc, CreatedByUserId = user.Id };
        await SeedAsync(store, tenantId, [employee], [user], [new AccountEmployeeCurrentLink { LinkId = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, UserId = user.Id }], [invitation], [attempt]);
        await using var db = store.CreateContext(new TestTenantContext(tenantId));

        var result = await Service(db, tenantId).ListAsync(new WelcomeInvitationQuery());

        Assert.True(result.Succeeded);
        var row = Assert.Single(result.Value!.Page.Items);
        Assert.Equal(employee.Id, row.EmployeeId);
        Assert.Equal("Ada Lovelace", row.EmployeeName);
        Assert.Equal("InvitationPending", row.PortalAccountStatus);
        Assert.Equal("Pending", row.InvitationStatus);
        Assert.Equal(invitation.LastSentAtUtc, row.LastInvitationDateUtc);
        Assert.True(row.CanResend);
        Assert.Equal(new WelcomeInvitationSummary(0, 0, 1, 0, 0), result.Value.Summary);
    }

    [Fact]
    public async Task List_paginates_and_handles_empty_and_single_employee_results()
    {
        using var store = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employees = new[] { NewEmployee(tenantId, "E1", "A", "One"), NewEmployee(tenantId, "E2", "B", "Two"), NewEmployee(tenantId, "E3", "C", "Three") };
        await SeedAsync(store, tenantId, employees, [], [], [], []);
        await using var db = store.CreateContext(new TestTenantContext(tenantId));
        var service = Service(db, tenantId);

        var page = await service.ListAsync(new WelcomeInvitationQuery { Page = 2, PageSize = 1 });
        var single = await service.ListAsync(new WelcomeInvitationQuery { EmployeeCode = "E2" });
        var empty = await service.ListAsync(new WelcomeInvitationQuery { EmployeeCode = "missing" });

        Assert.True(page.Succeeded);
        Assert.Equal(3, page.Value!.Page.TotalCount);
        Assert.Equal(2, page.Value.Page.Page);
        Assert.Equal("E2", Assert.Single(page.Value.Page.Items).EmployeeCode);
        Assert.Equal("E2", Assert.Single(single.Value!.Page.Items).EmployeeCode);
        Assert.Empty(empty.Value!.Page.Items);
        Assert.Equal(0, empty.Value.Page.TotalCount);
    }

    [Fact]
    public async Task List_returns_employees_without_portal_accounts_and_empty_enrichment_collections()
    {
        using var store = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "NO-PORTAL", "No", "Portal");
        await SeedAsync(store, tenantId, [employee], [], [], [], []);
        var commands = new RecordingCommandInterceptor();
        await using var db = store.CreateContext(new TestTenantContext(tenantId), commands);

        var result = await Service(db, tenantId).ListAsync(new WelcomeInvitationQuery());

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Value!.Page.TotalCount);
        var row = Assert.Single(result.Value.Page.Items);
        Assert.Equal(employee.Id, row.EmployeeId);
        Assert.Equal("NotCreated", row.PortalAccountStatus);
        Assert.Equal("NotSent", row.InvitationStatus);
        Assert.True(row.CanSend);
        Assert.Equal(new WelcomeInvitationSummary(1, 0, 0, 0, 0), result.Value.Summary);
        Assert.DoesNotContain(commands.Commands, sql =>
            sql.Contains("Users", StringComparison.OrdinalIgnoreCase) && sql.Contains("IN ()", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task List_enriches_normal_non_empty_portal_user_ids()
    {
        using var store = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "PORTAL", "Portal", "User");
        var user = new User { Id = Guid.NewGuid(), TenantId = tenantId, Email = employee.Email, FirstName = employee.FirstName, LastName = employee.LastName, IsActive = true };
        var link = new AccountEmployeeCurrentLink { LinkId = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employee.Id, UserId = user.Id };
        await SeedAsync(store, tenantId, [employee], [user], [link], [], []);
        await using var db = store.CreateContext(new TestTenantContext(tenantId));

        var result = await Service(db, tenantId).ListAsync(new WelcomeInvitationQuery());

        Assert.True(result.Succeeded);
        var row = Assert.Single(result.Value!.Page.Items);
        Assert.Equal("Active", row.PortalAccountStatus);
        Assert.Equal("Activated", row.InvitationStatus);
        Assert.Equal(new WelcomeInvitationSummary(1, 0, 0, 0, 1), result.Value.Summary);
    }

    [Fact]
    public async Task History_returns_attempt_snapshots_in_pages_and_hides_other_tenants()
    {
        using var store = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var otherTenantId = Guid.NewGuid();
        var employee = NewEmployee(tenantId, "H1", "History", "Employee");
        var otherEmployee = NewEmployee(otherTenantId, "H2", "Other", "Employee");
        var older = NewAttempt(tenantId, employee.Id, WelcomeInvitationStatus.Failed, new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc));
        older.ErrorSummary = "delivery failed";
        var newer = NewAttempt(tenantId, employee.Id, WelcomeInvitationStatus.Sent, new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc));
        await SeedAsync(store, tenantId, [employee], [], [], [], [older, newer]);
        await SeedAsync(store, otherTenantId, [otherEmployee], [], [], [], []);
        await using var db = store.CreateContext(new TestTenantContext(tenantId));
        var service = Service(db, tenantId);

        var history = await service.HistoryAsync(employee.Id, new WelcomeInvitationQuery { Page = 1, PageSize = 1 });
        var otherTenantHistory = await service.HistoryAsync(otherEmployee.Id, new WelcomeInvitationQuery());

        Assert.Equal(HRMS.Application.Common.ResultStatus.Success, history.Status);
        Assert.Equal(2, history.Value!.TotalCount);
        Assert.Equal(1, history.Value.Page);
        var row = Assert.Single(history.Value.Items);
        Assert.Equal(newer.Id, row.AttemptId);
        Assert.Equal("Sent", row.DeliveryStatus);
        Assert.Equal(newer.RecipientEmailSnapshot, row.RecipientEmail);
        Assert.Equal(HRMS.Application.Common.ResultStatus.NotFound, otherTenantHistory.Status);
    }
    [Fact]
    public async Task List_is_tenant_isolated_for_employee_and_invitation_enrichment()
    {
        using var store = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var otherTenantId = Guid.NewGuid();
        var own = NewEmployee(tenantId, "OWN", "Own", "Tenant");
        var other = NewEmployee(otherTenantId, "OTHER", "Other", "Tenant");
        var ownAttempt = NewAttempt(tenantId, own.Id, WelcomeInvitationStatus.Failed, DateTime.UtcNow);
        var otherAttempt = NewAttempt(otherTenantId, other.Id, WelcomeInvitationStatus.Sent, DateTime.UtcNow);
        await SeedAsync(store, tenantId, [own], [], [], [], [ownAttempt]);
        await SeedAsync(store, otherTenantId, [other], [], [], [], [otherAttempt]);
        await using var db = store.CreateContext(new TestTenantContext(tenantId));

        var result = await Service(db, tenantId).ListAsync(new WelcomeInvitationQuery());

        Assert.True(result.Succeeded);
        var row = Assert.Single(result.Value!.Page.Items);
        Assert.Equal(own.Id, row.EmployeeId);
        Assert.Equal("Failed", row.InvitationStatus);
        Assert.Equal(1, result.Value.Page.TotalCount);
    }

    [Fact]
    public async Task List_logs_failed_stage_without_filter_values()
    {
        using var store = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await SeedAsync(store, tenantId, [NewEmployee(tenantId, "PRIVATE-CODE", "Private", "Name")], [], [], [], []);
        await using var db = store.CreateContext(new TestTenantContext(tenantId));
        var logger = new RecordingLogger();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WelcomeInvitationService(db, new TestTenantContext(tenantId), null!, TimeProvider.System, logger)
            .ListAsync(new WelcomeInvitationQuery { EmployeeCode = "PRIVATE-CODE" }, cancelled.Token));

        var message = Assert.Single(logger.Messages);
        Assert.Contains("Employee count", message);
        Assert.Contains(nameof(OperationCanceledException), message);
        Assert.DoesNotContain("PRIVATE-CODE", message);
        Assert.Null(Assert.Single(logger.Exceptions));
    }

    private sealed class RecordingLogger : ILogger<WelcomeInvitationService>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception) + " " + exception?.GetType().Name + " " + exception?.StackTrace);
            Exceptions.Add(exception);
        }
    }

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private static WelcomeInvitationService Service(HrmsDbContext db, Guid tenantId)
        => new(db, new TestTenantContext(tenantId), null!, TimeProvider.System);

    private static Employee NewEmployee(Guid tenantId, string code, string first, string last)
        => new() { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCode = code, FirstName = first, LastName = last, Email = $"{code.ToLowerInvariant()}@example.test", DateOfJoining = new DateOnly(2026, 1, 1), Status = EmployeeStatus.Active };

    private static WelcomeInvitationAttempt NewAttempt(Guid tenantId, Guid employeeId, WelcomeInvitationStatus status, DateTime requestedAt)
        => new() { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, RequestedByUserId = Guid.NewGuid(), RequestedAtUtc = requestedAt, RecipientEmailSnapshot = "employee@example.test", AttemptType = WelcomeInvitationAttemptType.Initial, DeliveryStatus = status, CorrelationId = Guid.NewGuid().ToString("N"), IdempotencyKey = Guid.NewGuid().ToString("N"), SentAtUtc = status == WelcomeInvitationStatus.Sent ? requestedAt : null };

    private static async Task SeedAsync(SqliteInMemoryDatabase store, Guid tenantId, Employee[] employees, User[] users, AccountEmployeeCurrentLink[] links, UserInvitation[] invitations, WelcomeInvitationAttempt[] attempts)
    {
        await using var db = store.CreateContext(new TestTenantContext(tenantId));
        var tenantIds = employees.Select(x => x.TenantId).Append(tenantId).Distinct().ToArray();
        db.Tenants.AddRange(tenantIds.Select((id, index) => new Tenant { Id = id, TenantCode = id.ToString("N")[..8].ToUpperInvariant(), TenantName = $"Tenant {index}", Host = $"{id:N}.local", ShardKey = id.ToString("N"), Status = TenantStatus.Active }));
        foreach (var link in links)
        {
            link.CreationEvent = new AccountEmployeeLinkEvent { Id = link.LinkId, TenantId = link.TenantId, SubjectUserId = link.UserId, ActorUserId = link.UserId, Sequence = 1, Operation = "Link", NewLinkId = link.LinkId, AfterEmployeeId = link.EmployeeId, OccurredAtUtc = DateTime.UtcNow, Reason = "test", CorrelationId = Guid.NewGuid().ToString("N") };
        }
        db.Employees.AddRange(employees); db.Users.AddRange(users); db.AccountEmployeeCurrentLinks.AddRange(links); db.UserInvitations.AddRange(invitations); db.WelcomeInvitationAttempts.AddRange(attempts);
        await db.SaveChangesAsync();
    }
}
