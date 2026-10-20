using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Communication;
using HRMS.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

public sealed class WelcomeInvitationProcessorTests
{
    [Fact]
    public async Task Worker_processing_uses_selected_tenant_context_to_claim_queued_attempt()
    {
        using var dbStore = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var attempt = Guid.NewGuid();
        await SeedAsync(dbStore, tenant, employee, attempt, WelcomeInvitationStatus.Queued);
        await using var db = dbStore.CreateContext(new TestTenantContext(tenant));
        var email = new RecordingAccountService();

        await Processor(db, email, new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)), tenant: tenant).ProcessAsync();

        var row = await db.WelcomeInvitationAttempts.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == attempt);
        Assert.Equal(WelcomeInvitationStatus.Sent, row.DeliveryStatus);
        Assert.Equal(1, email.Calls);
    }

    [Fact]
    public async Task Processing_claims_queued_attempt_and_records_success_without_plaintext_token()
    {
        using var dbStore = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var attempt = Guid.NewGuid();
        await SeedAsync(dbStore, tenant, employee, attempt, WelcomeInvitationStatus.Queued);
        await using var db = dbStore.CreateContext(new TestTenantContext(tenant));
        var email = new RecordingAccountService();
        await Processor(db, email, new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)), tenant: tenant).ProcessAttemptAsync(attempt);
        var row = await db.WelcomeInvitationAttempts.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == attempt);
        Assert.Equal(WelcomeInvitationStatus.Sent, row.DeliveryStatus);
        Assert.Equal(1, email.Calls);
        Assert.Null(row.ErrorSummary);
        Assert.DoesNotContain("token", row.RecipientEmailSnapshot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Expired_lease_is_recoverable_and_stale_lease_cannot_overwrite_newer_result()
    {
        using var dbStore = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var attempt = Guid.NewGuid();
        await SeedAsync(dbStore, tenant, employee, attempt, WelcomeInvitationStatus.Processing, leaseId: "old", leaseExpires: new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc));
        await using var db = dbStore.CreateContext(new TestTenantContext(tenant));
        var processor = Processor(db, new RecordingAccountService(), new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)), tenant: tenant);
        await processor.ProcessAttemptAsync(attempt);
        var recovered = await db.WelcomeInvitationAttempts.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == attempt);
        Assert.Equal(WelcomeInvitationStatus.Sent, recovered.DeliveryStatus);
        Assert.Equal(0, await db.WelcomeInvitationAttempts.Where(x => x.Id == attempt && x.LeaseId == "old").ExecuteUpdateAsync(s => s.SetProperty(x => x.DeliveryStatus, WelcomeInvitationStatus.Failed)));
    }

    [Fact]
    public async Task Transient_failure_uses_bounded_backoff_and_max_attempts_stop_processing()
    {
        using var dbStore = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var attempt = Guid.NewGuid();
        await SeedAsync(dbStore, tenant, employee, attempt, WelcomeInvitationStatus.Queued, maxAttempts: 1);
        await using var db = dbStore.CreateContext(new TestTenantContext(tenant));
        var processor = Processor(db, new RecordingAccountService(ResultStatus.ServiceUnavailable), new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)), new WelcomeInvitationOptions { RetryBaseSeconds = 30, MaxAttempts = 1 }, tenant);
        await processor.ProcessAttemptAsync(attempt);
        var row = await db.WelcomeInvitationAttempts.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == attempt);
        Assert.Equal(WelcomeInvitationStatus.Failed, row.DeliveryStatus);
        Assert.Equal("TransientDeliveryFailure", row.ErrorCategory);
        Assert.Equal(new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc), row.CompletedAtUtc);
        Assert.Null(row.NextAttemptAtUtc);
    }

    [Fact]
    public async Task Two_workers_attempting_same_invitation_only_one_claims_it()
    {
        using var dbStore = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var attempt = Guid.NewGuid();
        await SeedAsync(dbStore, tenant, employee, attempt, WelcomeInvitationStatus.Queued);

        await using var dbA = dbStore.CreateIsolatedContext(new TestTenantContext(tenant));
        await using var dbB = dbStore.CreateIsolatedContext(new TestTenantContext(tenant));
        var accountA = new RecordingAccountService();
        var accountB = new RecordingAccountService();
        var now = new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero));
        var processorA = Processor(dbA, accountA, now, tenant: tenant);
        var processorB = Processor(dbB, accountB, now, tenant: tenant);

        await Task.WhenAll(processorA.ProcessAttemptAsync(attempt), processorB.ProcessAttemptAsync(attempt));

        Assert.Equal(1, accountA.Calls + accountB.Calls);
        var row = await dbA.WelcomeInvitationAttempts.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == attempt);
        Assert.Equal(WelcomeInvitationStatus.Sent, row.DeliveryStatus);
        Assert.Equal(1, row.RetryCount);
    }

    [Fact]
    public async Task Restarting_worker_does_not_redeliver_a_sent_invitation()
    {
        using var dbStore = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var attempt = Guid.NewGuid();
        await SeedAsync(dbStore, tenant, employee, attempt, WelcomeInvitationStatus.Queued);
        await using var db = dbStore.CreateContext(new TestTenantContext(tenant));
        var account = new RecordingAccountService();
        var processor = Processor(db, account, new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)), tenant: tenant);

        await processor.ProcessAttemptAsync(attempt);
        await processor.ProcessAttemptAsync(attempt);

        Assert.Equal(1, account.Calls);
    }

    [Fact]
    public async Task A_worker_scoped_to_another_tenant_cannot_process_the_attempt()
    {
        using var dbStore = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var otherTenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var attempt = Guid.NewGuid();
        await SeedAsync(dbStore, tenant, employee, attempt, WelcomeInvitationStatus.Queued);
        await using var db = dbStore.CreateContext(new TestTenantContext(otherTenant));
        var account = new RecordingAccountService();

        await Processor(db, account, new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)), tenant: otherTenant).ProcessAttemptAsync(attempt);

        Assert.Equal(0, account.Calls);
    }

    [Fact]
    public async Task Single_attempt_processing_selects_only_the_exact_attempt()
    {
        using var store = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var batch = Guid.NewGuid(); var wanted = Guid.NewGuid(); var sibling = Guid.NewGuid();
        await SeedAsync(store, tenant, Guid.NewGuid(), wanted, WelcomeInvitationStatus.Queued, batchId: batch);
        await SeedAsync(store, tenant, Guid.NewGuid(), sibling, WelcomeInvitationStatus.Queued, batchId: batch);
        await using var db = store.CreateContext(new TestTenantContext(tenant));
        var account = new RecordingAccountService();

        await Processor(db, account, new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)), tenant: tenant)
            .ProcessSingleAttemptAsync(tenant, batch, wanted, true);

        Assert.Equal(1, account.Calls);
        Assert.Equal(WelcomeInvitationStatus.Sent, (await db.WelcomeInvitationAttempts.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == wanted)).DeliveryStatus);
        Assert.Equal(WelcomeInvitationStatus.Queued, (await db.WelcomeInvitationAttempts.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == sibling)).DeliveryStatus);
    }

    [Fact]
    public async Task Single_attempt_fails_closed_for_mismatched_batch_or_ineligible_attempt()
    {
        using var store = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var batch = Guid.NewGuid(); var attempt = Guid.NewGuid();
        await SeedAsync(store, tenant, Guid.NewGuid(), attempt, WelcomeInvitationStatus.Sent, batchId: batch);
        await using var db = store.CreateContext(new TestTenantContext(tenant));
        var account = new RecordingAccountService();
        var processor = Processor(db, account, new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)), tenant: tenant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessSingleAttemptAsync(tenant, Guid.NewGuid(), attempt, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessSingleAttemptAsync(tenant, batch, attempt, true));
        Assert.Equal(0, account.Calls);
    }

    [Fact]
    public async Task Concurrent_single_attempt_claims_process_only_once()
    {
        using var store = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var batch = Guid.NewGuid(); var attempt = Guid.NewGuid();
        await SeedAsync(store, tenant, Guid.NewGuid(), attempt, WelcomeInvitationStatus.Queued, batchId: batch);
        await using var dbA = store.CreateIsolatedContext(new TestTenantContext(tenant));
        await using var dbB = store.CreateIsolatedContext(new TestTenantContext(tenant));
        var a = new RecordingAccountService(); var b = new RecordingAccountService();
        var pA = Processor(dbA, a, new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)), tenant: tenant);
        var pB = Processor(dbB, b, new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)), tenant: tenant);
        var outcomes = await Task.WhenAll(Capture(() => pA.ProcessSingleAttemptAsync(tenant, batch, attempt, true)), Capture(() => pB.ProcessSingleAttemptAsync(tenant, batch, attempt, true)));
        Assert.Equal(1, a.Calls + b.Calls);
        Assert.Single(outcomes.Where(x => x is null));
    }

    [Fact]
    public async Task Single_attempt_claim_cannot_cross_tenant_boundary()
    {
        using var store = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var other = Guid.NewGuid(); var batch = Guid.NewGuid(); var attempt = Guid.NewGuid();
        await SeedAsync(store, tenant, Guid.NewGuid(), attempt, WelcomeInvitationStatus.Queued, batchId: batch);
        await using var db = store.CreateContext(new TestTenantContext(other));
        var account = new RecordingAccountService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Processor(db, account, new FixedClock(DateTimeOffset.UtcNow), tenant: other).ProcessSingleAttemptAsync(other, batch, attempt, true));
        Assert.Equal(0, account.Calls);
    }

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try { await action(); return null; } catch (Exception ex) { return ex; }
    }

    [Fact]
    public async Task Batch_preview_is_tenant_batch_and_eligibility_scoped_without_mutations()
    {
        using var store = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var otherTenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var batch = Guid.NewGuid();
        await SeedAsync(store, tenant, employee, Guid.NewGuid(), WelcomeInvitationStatus.Queued, batchId: batch);
        await SeedAsync(store, tenant, Guid.NewGuid(), Guid.NewGuid(), WelcomeInvitationStatus.Sent, batchId: batch);
        await SeedAsync(store, tenant, Guid.NewGuid(), Guid.NewGuid(), WelcomeInvitationStatus.Queued, batchId: Guid.NewGuid());
        await SeedAsync(store, otherTenant, Guid.NewGuid(), Guid.NewGuid(), WelcomeInvitationStatus.Queued, batchId: batch);
        await using var db = store.CreateContext(new TestTenantContext(tenant));
        var before = await db.WelcomeInvitationAttempts.IgnoreQueryFilters().AsNoTracking().Select(x => new { x.Id, x.RetryCount, x.DeliveryStatus }).ToListAsync();
        var rows = await Processor(db, new RecordingAccountService(), new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)), tenant: tenant)
            .PreviewBatchAsync(tenant, batch);
        Assert.Single(rows.Eligible);
        Assert.Single(rows.Excluded);
        Assert.Equal("Status Sent is not eligible", rows.Excluded[0].Reason);
        Assert.Equal("t***@example.test", rows.Eligible[0].MaskedRecipient);
        Assert.Equal(before.Select(x => (x.Id, x.RetryCount, x.DeliveryStatus)),
            (await db.WelcomeInvitationAttempts.IgnoreQueryFilters().AsNoTracking().Select(x => new { x.Id, x.RetryCount, x.DeliveryStatus }).ToListAsync()).Select(x => (x.Id, x.RetryCount, x.DeliveryStatus)));
    }

    [Fact]
    public async Task Batch_processing_requires_confirmation_and_rejects_mismatched_tenant()
    {
        using var store = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var attempt = Guid.NewGuid(); var batch = Guid.NewGuid();
        await SeedAsync(store, tenant, employee, attempt, WelcomeInvitationStatus.Queued, batchId: batch);
        await using var db = store.CreateContext(new TestTenantContext(tenant));
        var account = new RecordingAccountService();
        var processor = Processor(db, account, new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)), tenant: tenant);
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessBatchAsync(tenant, batch, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessBatchAsync(Guid.NewGuid(), batch, true));
        Assert.Equal(0, account.Calls);
    }

    [Fact]
    public async Task Batch_status_is_completed_with_failures_after_mixed_delivery_results()
    {
        using var dbStore = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var attempt = Guid.NewGuid(); var batch = Guid.NewGuid();
        await SeedAsync(dbStore, tenant, employee, attempt, WelcomeInvitationStatus.Queued, batchId: batch);
        await using var db = dbStore.CreateContext(new TestTenantContext(tenant));
        var processor = Processor(db, new RecordingAccountService(ResultStatus.ServiceUnavailable), new FixedClock(new DateTimeOffset(2026, 10, 13, 10, 0, 0, TimeSpan.Zero)), new WelcomeInvitationOptions { MaxAttempts = 1 }, tenant);

        await processor.ProcessAttemptAsync(attempt);

        var row = await db.WelcomeInvitationBatches.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == batch);
        Assert.Equal(WelcomeInvitationBatchStatus.CompletedWithFailures, row.Status);
        Assert.NotNull(row.CompletedAtUtc);
    }

    private static WelcomeInvitationProcessor Processor(HRMS.Infrastructure.Persistence.HrmsDbContext db, IEmployeePortalAccountService account, TimeProvider clock, WelcomeInvitationOptions? options = null, Guid? tenant = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(account);
        return new(db, services.BuildServiceProvider(), options ?? new(), null!, clock, NullLogger<WelcomeInvitationProcessor>.Instance, tenant is Guid id ? new TestTenantContext(id) : null);
    }

    private static async Task SeedAsync(SqliteInMemoryDatabase store, Guid tenant, Guid employee, Guid attempt, WelcomeInvitationStatus status, string? leaseId = null, DateTime? leaseExpires = null, int maxAttempts = 3, Guid? batchId = null)
    {
        await using var db = store.CreateContext(new TestTenantContext(tenant));
        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(x => x.Id == tenant))
            db.Tenants.Add(new Tenant { Id = tenant, TenantCode = $"INV{tenant:N}", TenantName = "Inv", Host = $"{tenant:N}.inv.local", ShardKey = tenant.ToString("N"), Status = TenantStatus.Active });
        if (!await db.Employees.IgnoreQueryFilters().AnyAsync(x => x.Id == employee))
            db.Employees.Add(new Employee { Id = employee, TenantId = tenant, EmployeeCode = $"E{employee:N}", FirstName = "Test", LastName = "Employee", Email = $"{employee:N}@example.test", DateOfJoining = new DateOnly(2026, 1, 1) });
        if (batchId is Guid batch && !await db.WelcomeInvitationBatches.IgnoreQueryFilters().AnyAsync(x => x.Id == batch))
            db.WelcomeInvitationBatches.Add(new WelcomeInvitationBatch { Id = batch, TenantId = tenant, RequestedByUserId = Guid.NewGuid(), RequestedAtUtc = new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc), TotalRequested = 1, TotalAccepted = 1, CorrelationId = $"batch-{batch:N}", IdempotencyKey = $"batch-{batch:N}" });
        db.WelcomeInvitationAttempts.Add(new WelcomeInvitationAttempt { Id = attempt, TenantId = tenant, EmployeeId = employee, BatchId = batchId, RequestedByUserId = Guid.NewGuid(), RequestedAtUtc = new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc), RecipientEmailSnapshot = "test@example.test", CorrelationId = "c", IdempotencyKey = attempt.ToString("N"), DeliveryStatus = status, LeaseId = leaseId, LeaseExpiresAtUtc = leaseExpires, MaxAttempts = maxAttempts });
        await db.SaveChangesAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class RecordingAccountService(ResultStatus? status = null) : IEmployeePortalAccountService
    {
        public int Calls { get; private set; }
        public Task<Result<CreatePortalAccountResponse>> ProcessWelcomeInvitationAsync(Guid employeeId, Guid requestedByUserId, bool resend, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult(status is null ? Result<CreatePortalAccountResponse>.Success(new(new(employeeId, "InvitationPending", Guid.NewGuid(), "test@example.test", null, null), null, "ok")) : Result<CreatePortalAccountResponse>.Failure(status.Value, "temporary")); }
        public Task<Result<PortalAccountDto>> GetAsync(Guid e, CancellationToken c = default) => throw new NotImplementedException(); public Task<Result<CreatePortalAccountResponse>> CreateAsync(Guid e, CreatePortalAccountRequest r, CancellationToken c = default) => throw new NotImplementedException(); public Task<Result<CreatePortalAccountResponse>> ResendAsync(Guid e, CancellationToken c = default) => throw new NotImplementedException(); public Task<Result<PortalAccountDto>> RevokeAsync(Guid e, CancellationToken c = default) => throw new NotImplementedException(); public Task<Result<bool>> SetPasswordAsync(SetPasswordRequest r, CancellationToken c = default) => throw new NotImplementedException();
    }
}
