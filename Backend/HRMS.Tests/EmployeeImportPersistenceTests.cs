using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests;

public sealed class EmployeeImportPersistenceTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TokenHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly Guid TenantId = SeedData.TenantIds.Demo01;
    private static readonly Guid UserId = SeedData.Users.First(x => x.TenantId == TenantId).Id;

    [Fact]
    public async Task Preparation_and_completion_use_the_same_tenant_scoped_context()
    {
        using var database = await CreateDatabaseAsync();
        var tenant = new TestTenantContext(TenantId, UserId);
        using var db = database.CreateContext(tenant);
        var service = Service(db, tenant);

        var started = await service.BeginPreparationAsync(Hash, TokenHash, 1, Guid.NewGuid());
        Assert.True(started.Succeeded, started.Message);

        var completed = await service.CompletePreparationAsync(started.Value!.Id, started.Value.ClaimLeaseId!.Value);

        Assert.True(completed.Succeeded, completed.Message);
        Assert.Equal(started.Value.Id, completed.Value!.Id);
        Assert.Equal(EmployeeImportOperationState.Prepared,
            await db.EmployeeImportOperations.Where(x => x.Id == started.Value.Id).Select(x => x.State).SingleAsync());
    }

    [Fact]
    public async Task Mismatched_service_and_db_tenant_context_reproduces_prepare_not_found_without_bypassing_filters()
    {
        using var database = await CreateDatabaseAsync();
        var serviceTenant = new TestTenantContext(TenantId, UserId);
        var emptyDbTenant = new TestTenantContext();
        using var db = database.CreateContext(emptyDbTenant);
        var service = Service(db, serviceTenant);

        var started = await service.BeginPreparationAsync(Hash, TokenHash, 1, Guid.NewGuid());
        Assert.True(started.Succeeded, started.Message);

        var completed = await service.CompletePreparationAsync(started.Value!.Id, started.Value.ClaimLeaseId!.Value);

        Assert.Equal(HRMS.Application.Common.ResultStatus.NotFound, completed.Status);
        Assert.Contains("not found", completed.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await db.EmployeeImportOperations.IgnoreQueryFilters()
            .Where(x => x.Id == started.Value.Id && x.TenantId == TenantId).ToListAsync());
    }

    [Fact]
    public async Task Matching_context_does_not_allow_cross_tenant_operation_access()
    {
        using var database = await CreateDatabaseAsync();
        var tenant = new TestTenantContext(TenantId, UserId);
        using var db = database.CreateContext(tenant);
        var operation = (await PrepareAsync(Service(db, tenant), Hash, TokenHash, 1)).Value!;

        var otherTenantId = SeedData.TenantIds.Demo02;
        var otherUserId = SeedData.Users.First(x => x.TenantId == otherTenantId).Id;
        var otherTenant = new TestTenantContext(otherTenantId, otherUserId);
        using var otherDb = database.CreateIsolatedContext(otherTenant);
        var crossTenant = await Service(otherDb, otherTenant).GetAsync(operation.Id);

        Assert.Equal(HRMS.Application.Common.ResultStatus.NotFound, crossTenant.Status);
    }

    [Fact]
    public async Task Workbook_identity_is_unique_per_tenant_but_shared_hash_is_allowed_across_tenants()
    {
        using var database = await CreateDatabaseAsync();
        using var db = database.CreateContext(new TestTenantContext(TenantId, UserId));
        var service = Service(db, new TestTenantContext(TenantId, UserId));
        var created = await PrepareAsync(service, Hash, TokenHash, 1);
        Assert.True(created.Succeeded, created.Message);
        var duplicate = await service.BeginPreparationAsync(Hash, "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", 1, Guid.NewGuid());
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, duplicate.Status);

        var foreignUserId = SeedData.Users.First(x => x.TenantId == SeedData.TenantIds.Demo02).Id;
        using var foreignContext = database.CreateIsolatedContext(new TestTenantContext(SeedData.TenantIds.Demo02, foreignUserId));
        var crossTenant = await PrepareAsync(Service(foreignContext, new TestTenantContext(SeedData.TenantIds.Demo02, foreignUserId)), Hash, TokenHash, 1);
        Assert.True(crossTenant.Succeeded, crossTenant.Message);
    }

    [Fact]
    public async Task Confirmation_claim_is_one_use_and_transition_graph_rejects_invalid_moves()
    {
        using var database = await CreateDatabaseAsync();
        using var db = database.CreateContext(new TestTenantContext(TenantId, UserId));
        var service = Service(db, new TestTenantContext(TenantId, UserId));
        var operation = (await PrepareAsync(service, Hash, TokenHash, 1)).Value!;
        var claim = await service.ClaimAsync(operation.Id, TokenHash);
        Assert.True(claim.Succeeded, claim.Message);
        Assert.Equal(EmployeeImportOperationState.Claimed, claim.Value!.State);
        var repeated = await service.ClaimAsync(operation.Id, TokenHash);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, repeated.Status);
        Assert.False(operation.TransitionTo(EmployeeImportOperationState.Completed, DateTime.UtcNow));
        Assert.True((await service.TransitionAsync(operation.Id, EmployeeImportOperationState.Processing)).Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict,
            (await service.TransitionAsync(operation.Id, EmployeeImportOperationState.Claimed)).Status);
    }

    [Fact]
    public async Task Concurrent_claim_requests_have_exactly_one_winner()
    {
        using var database = await CreateDatabaseAsync();
        using (var setup = database.CreateContext(new TestTenantContext(TenantId, UserId)))
            await PrepareAsync(Service(setup, new TestTenantContext(TenantId, UserId)), Hash, TokenHash, 1);
        using var left = database.CreateIsolatedContext(new TestTenantContext(TenantId, UserId));
        using var right = database.CreateIsolatedContext(new TestTenantContext(TenantId, UserId));
        var operationId = await left.EmployeeImportOperations.Select(x => x.Id).SingleAsync();
        var results = await Task.WhenAll(
            Service(left, new TestTenantContext(TenantId, UserId)).ClaimAsync(operationId, TokenHash),
            Service(right, new TestTenantContext(TenantId, UserId)).ClaimAsync(operationId, TokenHash));
        Assert.Single(results.Where(x => x.Succeeded));
        Assert.Single(results.Where(x => !x.Succeeded && x.Status == HRMS.Application.Common.ResultStatus.Conflict));
    }

    [Fact]
    public async Task Durable_queue_claim_has_one_winner_and_expired_lease_fences_stale_worker()
    {
        using var database = await CreateDatabaseAsync();
        var tenant = new TestTenantContext(TenantId, UserId);
        Guid operationId;
        var clock = new AdjustableClock(DateTimeOffset.Parse("2026-10-28T00:00:00Z"));
        using (var setup = database.CreateContext(tenant))
        {
            var service = new EmployeeImportPersistenceService(setup, tenant, clock);
            var operation = (await service.BeginPreparationAsync(Hash, TokenHash, 1, Guid.NewGuid())).Value!;
            operationId = operation.Id;
            Assert.True((await service.CompletePreparationAsync(operationId, operation.ClaimLeaseId!.Value)).Succeeded);
            Assert.True((await service.EnqueueAsync(operationId, TokenHash)).Succeeded);
        }

        using var left = database.CreateIsolatedContext(tenant);
        using var right = database.CreateIsolatedContext(tenant);
        var results = await Task.WhenAll(
            new EmployeeImportPersistenceService(left, tenant, clock).ClaimQueuedOperationAsync(operationId, Guid.NewGuid()),
            new EmployeeImportPersistenceService(right, tenant, clock).ClaimQueuedOperationAsync(operationId, Guid.NewGuid()));
        Assert.Single(results.Where(x => x.Succeeded && x.Value is not null));
        var winner = results.Single(x => x.Succeeded && x.Value is not null).Value!;
        var staleLease = winner.ClaimLeaseId!.Value;
        clock.Advance(TimeSpan.FromMinutes(11));

        using var recovery = database.CreateIsolatedContext(tenant);
        var newLease = Guid.NewGuid();
        var recovered = await new EmployeeImportPersistenceService(recovery, tenant, clock)
            .ClaimQueuedOperationAsync(operationId, newLease);
        Assert.True(recovered.Succeeded, recovered.Message);
        Assert.Equal(newLease, recovered.Value!.ClaimLeaseId);
        var staleTransition = await new EmployeeImportPersistenceService(left, tenant, clock)
            .TransitionClaimedAsync(operationId, staleLease, EmployeeImportOperationState.Processing);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, staleTransition.Status);
        Assert.Equal(EmployeeImportOperationState.Claimed,
            (await recovery.EmployeeImportOperations.AsNoTracking().SingleAsync(x => x.Id == operationId)).State);
    }

    [Fact]
    public async Task Targeted_worker_claim_is_tenant_and_operation_scoped_and_recovers_only_that_lease()
    {
        using var database = await CreateDatabaseAsync();
        var clock = new AdjustableClock(DateTimeOffset.Parse("2026-10-28T00:00:00Z"));
        var web1 = new TestTenantContext(TenantId, UserId);
        var otherTenantId = SeedData.TenantIds.Demo02;
        var otherUserId = SeedData.Users.First(x => x.TenantId == otherTenantId).Id;
        var otherTenant = new TestTenantContext(otherTenantId, otherUserId);
        Guid targetOperationId;
        Guid otherWeb1OperationId;
        Guid otherTenantOperationId;

        using (var setup = database.CreateContext(web1))
        {
            var persistence = new EmployeeImportPersistenceService(setup, web1, clock);
            targetOperationId = await CreateQueuedOperationAsync(persistence, 'c', 'd');
            otherWeb1OperationId = await CreateQueuedOperationAsync(persistence, 'e', 'f');
        }
        using (var setup = database.CreateIsolatedContext(otherTenant))
        {
            otherTenantOperationId = await CreateQueuedOperationAsync(
                new EmployeeImportPersistenceService(setup, otherTenant, clock), '1', '2');
        }

        Guid oldLease;
        using (var claimContext = database.CreateIsolatedContext(web1))
        {
            var lease = Guid.NewGuid();
            var claimed = await new EmployeeImportPersistenceService(claimContext, web1, clock)
                .ClaimQueuedOperationAsync(targetOperationId, lease);
            Assert.True(claimed.Succeeded, claimed.Message);
            Assert.Equal(targetOperationId, claimed.Value!.Id);
            Assert.Equal(lease, claimed.Value.ClaimLeaseId);
            oldLease = lease;
        }

        using (var wrongTenantContext = database.CreateIsolatedContext(otherTenant))
        {
            var crossTenantClaim = await new EmployeeImportPersistenceService(wrongTenantContext, otherTenant, clock)
                .ClaimQueuedOperationAsync(targetOperationId, Guid.NewGuid());
            Assert.True(crossTenantClaim.Succeeded);
            Assert.Null(crossTenantClaim.Value);
        }

        clock.Advance(TimeSpan.FromMinutes(11));
        var recoveryLease = Guid.NewGuid();
        using (var recoveryContext = database.CreateIsolatedContext(web1))
        {
            var recovered = await new EmployeeImportPersistenceService(recoveryContext, web1, clock)
                .ClaimQueuedOperationAsync(targetOperationId, recoveryLease);
            Assert.True(recovered.Succeeded, recovered.Message);
            Assert.Equal(targetOperationId, recovered.Value!.Id);
            Assert.Equal(recoveryLease, recovered.Value.ClaimLeaseId);

            var staleTransition = await new EmployeeImportPersistenceService(recoveryContext, web1, clock)
                .TransitionClaimedAsync(targetOperationId, oldLease, EmployeeImportOperationState.Processing);
            Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, staleTransition.Status);
        }

        using (var verify = database.CreateIsolatedContext(web1))
        {
            var target = await verify.EmployeeImportOperations.AsNoTracking().SingleAsync(x => x.Id == targetOperationId);
            var sibling = await verify.EmployeeImportOperations.AsNoTracking().SingleAsync(x => x.Id == otherWeb1OperationId);
            Assert.Equal(EmployeeImportOperationState.Claimed, target.State);
            Assert.Equal(EmployeeImportOperationState.Queued, sibling.State);
            Assert.Null(sibling.ClaimLeaseId);
        }
        using (var verifyOtherTenant = database.CreateIsolatedContext(otherTenant))
        {
            var other = await verifyOtherTenant.EmployeeImportOperations.AsNoTracking()
                .SingleAsync(x => x.Id == otherTenantOperationId);
            Assert.Equal(EmployeeImportOperationState.Queued, other.State);
            Assert.Null(other.ClaimLeaseId);
        }
    }

    [Fact]
    public async Task Queued_state_consumes_confirmation_claim_once_and_rejects_terminal_reclaim()
    {
        using var database = await CreateDatabaseAsync();
        var tenant = new TestTenantContext(TenantId, UserId);
        using var db = database.CreateContext(tenant);
        var service = Service(db, tenant);
        var operation = (await service.BeginPreparationAsync(Hash, TokenHash, 1, Guid.NewGuid())).Value!;
        Assert.True((await service.CompletePreparationAsync(operation.Id, operation.ClaimLeaseId!.Value)).Succeeded);
        Assert.True((await service.EnqueueAsync(operation.Id, TokenHash)).Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, (await service.EnqueueAsync(operation.Id, TokenHash)).Status);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict,
            (await service.TransitionAsync(operation.Id, EmployeeImportOperationState.Completed)).Status);
    }

    [Fact]
    public async Task Row_identity_is_unique_and_sensitive_values_are_sanitized_from_persisted_errors()
    {
        using var database = await CreateDatabaseAsync();
        using var db = database.CreateContext(new TestTenantContext(TenantId, UserId));
        var service = Service(db, new TestTenantContext(TenantId, UserId));
        var operation = (await PrepareAsync(service, Hash, TokenHash, 1)).Value!;
        Assert.True((await service.ClaimAsync(operation.Id, TokenHash)).Succeeded);
        Assert.True((await service.TransitionAsync(operation.Id, EmployeeImportOperationState.Processing)).Succeeded);
        Assert.True((await service.StartRowAsync(operation.Id, 2)).Succeeded);
        var failed = await service.FailRowAsync(operation.Id, 2, "validation.failed", "Aadhaar 999988887777 PAN ABCDE1234F email person@example.test is invalid.");
        Assert.True(failed.Succeeded, failed.Message);
        db.EmployeeImportRows.Add(new EmployeeImportRow { Id = Guid.NewGuid(), TenantId = TenantId, OperationId = operation.Id, ExcelRowNumber = 2 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ClearChangeTracker();
        var stored = await db.EmployeeImportRows.AsNoTracking().SingleAsync(x => x.OperationId == operation.Id);
        Assert.DoesNotContain("999988887777", stored.SanitizedErrorMessage);
        Assert.DoesNotContain("ABCDE1234F", stored.SanitizedErrorMessage);
        Assert.DoesNotContain("person@example.test", stored.SanitizedErrorMessage);
    }

    [Fact]
    public async Task Employee_and_successful_row_are_atomic_and_abandoned_rows_can_be_recovered_safely()
    {
        using var database = await CreateDatabaseAsync();
        var tenant = new TestTenantContext(TenantId, UserId);
        Guid operationId;
        var employeeId = Guid.NewGuid();
        using (var db = database.CreateContext(tenant))
        {
            var service = Service(db, tenant);
            var operation = (await PrepareAsync(service, Hash, TokenHash, 1)).Value!;
            operationId = operation.Id;
            var leaseId = Guid.NewGuid();
            Assert.True((await service.ClaimAsync(operationId, TokenHash, leaseId)).Succeeded);
            Assert.True((await service.TransitionAsync(operationId, EmployeeImportOperationState.Processing)).Succeeded);
            Assert.True((await service.StartRowAsync(operationId, 2)).Succeeded);
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Employees.Add(Employee(employeeId));
            await db.SaveChangesAsync();
            var completed = await service.CompleteRowWithinTransactionAsync(operationId, 2, employeeId, "EMP-ROLLBACK", leaseId, transaction);
            Assert.True(completed.Succeeded, completed.Message);
            await transaction.RollbackAsync();
        }

        using (var verify = database.CreateContext(tenant))
        {
            Assert.False(await verify.Employees.AnyAsync(x => x.Id == employeeId));
            var row = await verify.EmployeeImportRows.SingleAsync(x => x.OperationId == operationId);
            Assert.Equal(EmployeeImportRowStatus.Processing, row.Status);
            Assert.Null(row.EmployeeId);
            var recovered = await Service(verify, tenant).RecoverAbandonedRowAsync(operationId, 2);
            Assert.True(recovered.Succeeded, recovered.Message);
            Assert.Equal(EmployeeImportRowStatus.Pending, recovered.Value!.Status);
        }
    }

    [Fact]
    public async Task Generic_import_batch_history_remains_available_and_operation_prevents_batch_deletion()
    {
        using var database = await CreateDatabaseAsync();
        var tenant = new TestTenantContext(TenantId, UserId);
        using var db = database.CreateContext(tenant);
        var operation = (await PrepareAsync(Service(db, tenant), Hash, TokenHash, 1, "employees.xlsx")).Value!;
        var batch = await db.ImportBatches.SingleAsync(x => x.Id == operation.ImportBatchId);
        Assert.Equal("Prepared", batch.Status);
        Assert.Equal(1, batch.TotalRows);
        using var fresh = database.CreateContext(tenant);
        var importBatchService = new ImportBatchService(fresh, tenant, NullLogger<ImportBatchService>.Instance);
        Assert.Single((await importBatchService.GetAsync()).Value!);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, (await importBatchService.DeleteAsync(batch.Id)).Status);
    }

    private static EmployeeImportPersistenceService Service(HrmsDbContext db, ITenantContext tenant) => new(db, tenant, TimeProvider.System);

    private static async Task<HRMS.Application.Common.Result<EmployeeImportOperation>> PrepareAsync(
        EmployeeImportPersistenceService service, string hash, string tokenHash, int rows, string? fileName = null)
    {
        var lease = Guid.NewGuid();
        var started = await service.BeginPreparationAsync(hash, tokenHash, rows, lease, fileName);
        if (!started.Succeeded) return started;
        return await service.CompletePreparationAsync(started.Value!.Id, lease);
    }

    private static async Task<Guid> CreateQueuedOperationAsync(
        EmployeeImportPersistenceService service, char hashCharacter, char tokenCharacter)
    {
        var prepared = await service.BeginPreparationAsync(
            new string(hashCharacter, 64), new string(tokenCharacter, 64), 1, Guid.NewGuid());
        Assert.True(prepared.Succeeded, prepared.Message);
        Assert.True((await service.CompletePreparationAsync(prepared.Value!.Id, prepared.Value.ClaimLeaseId!.Value)).Succeeded);
        Assert.True((await service.EnqueueAsync(prepared.Value.Id, new string(tokenCharacter, 64))).Succeeded);
        return prepared.Value.Id;
    }

    private static async Task<SqliteInMemoryDatabase> CreateDatabaseAsync()
    {
        var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        return database;
    }

    private static Employee Employee(Guid id) => new()
    {
        Id = id, TenantId = TenantId, EmployeeCode = "EMP-ROLLBACK", FirstName = "Test", LastName = "Rollback",
        Email = $"{id:N}@test.local", DateOfJoining = new DateOnly(2026, 3, 4), Status = EmployeeStatus.Active
    };

    private sealed class AdjustableClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan amount) => current += amount;
    }
}
