using System.Data.Common;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRMS.Tests;

public sealed class JobStatusServiceTests
{
    [Fact]
    public async Task Creation_requires_its_own_permission()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employeeId = await SeedAsync(database, tenantId, "On Probation");
        var context = new TestTenantContext(tenantId, Guid.NewGuid());
        await using var db = database.CreateContext(context);
        var service = new JobStatusService(db, context, new TestAuthorization(), TimeProvider.System);

        var result = await service.ChangeAsync(new(employeeId, "Confirmed", JobStatusMutationSource.Creation, "initial status"));

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal("On Probation", await db.Employees.Where(x => x.Id == employeeId).Select(x => x.JobStatus).SingleAsync());
        Assert.Empty(await db.EmployeeAuditLogs.Where(x => x.EmployeeId == employeeId).ToListAsync());
    }

    [Fact]
    public async Task Authorized_creation_assigns_an_initial_status()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employeeId = await SeedAsync(database, tenantId, null);
        var context = new TestTenantContext(tenantId, Guid.NewGuid());
        await using var db = database.CreateContext(context);
        var service = new JobStatusService(db, context, new TestAuthorization(Permissions.Employee.Create), TimeProvider.System);

        var result = await service.ChangeAsync(new(employeeId, "On Probation", JobStatusMutationSource.Creation));

        Assert.Equal(ResultStatus.Success, result.Status);
        Assert.Equal("On Probation", await db.Employees.Where(x => x.Id == employeeId).Select(x => x.JobStatus).SingleAsync());
        Assert.Equal("On Probation", await db.EmployeeEmployments.Where(x => x.EmployeeId == employeeId).Select(x => x.JobStatus).SingleAsync());
    }

    [Fact]
    public async Task Cross_tenant_employee_is_not_visible_to_the_service()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var employeeId = await SeedAsync(database, tenantA, "Probation");
        var context = new TestTenantContext(tenantB, Guid.NewGuid());
        await using var db = database.CreateContext(context);
        var service = new JobStatusService(db, context, new TestAuthorization(Permissions.Confirmation.Decide), TimeProvider.System);

        var result = await service.ChangeAsync(new(employeeId, "Confirmed", JobStatusMutationSource.Confirmation));

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Empty(await db.EmployeeAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Confirmation_updates_both_existing_status_columns_and_audits_the_transition()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employeeId = await SeedAsync(database, tenantId, "Probation", withEmployment: true);
        var actor = Guid.NewGuid();
        var context = new TestTenantContext(tenantId, actor);
        await using var db = database.CreateContext(context);
        var service = new JobStatusService(db, context, new TestAuthorization(Permissions.Confirmation.Decide), TimeProvider.System);

        var result = await service.ChangeAsync(new(employeeId, "Confirmed", JobStatusMutationSource.Confirmation, "decision approved"));

        Assert.Equal(ResultStatus.Success, result.Status);
        Assert.Equal("Confirmed", await db.Employees.Where(x => x.Id == employeeId).Select(x => x.JobStatus).SingleAsync());
        Assert.Equal("Confirmed", await db.EmployeeEmployments.Where(x => x.EmployeeId == employeeId).Select(x => x.JobStatus).SingleAsync());
        var audit = await db.EmployeeAuditLogs.SingleAsync(x => x.EmployeeId == employeeId);
        Assert.Equal("Probation", audit.OldValue);
        Assert.Equal("Confirmed", audit.NewValue);
        Assert.Equal(actor.ToString(), audit.ChangedBy);
        Assert.Equal("Confirmation", audit.Source);
    }

    [Fact]
    public async Task Caller_owned_transaction_is_not_committed_by_job_status_service()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employeeId = await SeedAsync(database, tenantId, "Probation", withEmployment: true);
        var context = new TestTenantContext(tenantId, Guid.NewGuid());
        await using var db = database.CreateContext(context);
        var service = new JobStatusService(db, context, new TestAuthorization(Permissions.Confirmation.Decide), TimeProvider.System);

        await using var transaction = await db.BeginTransactionAsync();
        var result = await service.ChangeWithinTransactionAsync(
            new(employeeId, "Confirmed", JobStatusMutationSource.Confirmation), transaction);

        Assert.Equal(ResultStatus.Success, result.Status);
        Assert.Equal("Confirmed", await db.Employees.Where(x => x.Id == employeeId).Select(x => x.JobStatus).SingleAsync());
        await transaction.RollbackAsync();

        await AssertUnchangedAsync(database, tenantId, employeeId, "Probation");
    }

    [Fact]
    public async Task Separation_rejects_non_terminal_target_and_only_allows_active_employees()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employeeId = await SeedAsync(database, tenantId, "Confirmed");
        var context = new TestTenantContext(tenantId, Guid.NewGuid());
        await using var db = database.CreateContext(context);
        var service = new JobStatusService(db, context, new TestAuthorization(Permissions.Separation.Manage), TimeProvider.System);

        var invalidTarget = await service.ChangeAsync(new(employeeId, "Terminated", JobStatusMutationSource.Separation));
        var valid = await service.ChangeAsync(new(employeeId, "Separated", JobStatusMutationSource.Separation));

        Assert.Equal(ResultStatus.Conflict, invalidTarget.Status);
        Assert.Equal(ResultStatus.Success, valid.Status);
        Assert.Equal("Separated", await db.Employees.Where(x => x.Id == employeeId).Select(x => x.JobStatus).SingleAsync());
    }

    [Fact]
    public async Task Save_changes_failure_rolls_back_both_status_columns_and_audit()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employeeId = await SeedAsync(database, tenantId, "Probation", withEmployment: true);
        var context = new TestTenantContext(tenantId, Guid.NewGuid());
        await using (var db = database.CreateIsolatedContext(context, new FailOnceSaveChangesInterceptor()))
        {
            var service = new JobStatusService(db, context, new TestAuthorization(Permissions.Confirmation.Decide), TimeProvider.System);
            var result = await service.ChangeAsync(new(employeeId, "Confirmed", JobStatusMutationSource.Confirmation));

            Assert.Equal(ResultStatus.ServiceUnavailable, result.Status);
        }

        await AssertUnchangedAsync(database, tenantId, employeeId, "Probation");
    }

    [Fact]
    public async Task Audit_persistence_failure_rolls_back_both_status_columns_and_audit()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employeeId = await SeedAsync(database, tenantId, "Probation", withEmployment: true);
        var context = new TestTenantContext(tenantId, Guid.NewGuid());
        await using (var db = database.CreateIsolatedContext(context, new FailAuditInsertInterceptor()))
        {
            var service = new JobStatusService(db, context, new TestAuthorization(Permissions.Confirmation.Decide), TimeProvider.System);
            var result = await service.ChangeAsync(new(employeeId, "Confirmed", JobStatusMutationSource.Confirmation));

            Assert.Equal(ResultStatus.ServiceUnavailable, result.Status);
        }

        await AssertUnchangedAsync(database, tenantId, employeeId, "Probation");
    }

    [Fact]
    public async Task Transaction_commit_failure_rolls_back_both_status_columns_and_audit()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var employeeId = await SeedAsync(database, tenantId, "Probation", withEmployment: true);
        var context = new TestTenantContext(tenantId, Guid.NewGuid());
        await using (var db = database.CreateIsolatedContext(context, new FailTransactionCommitInterceptor()))
        {
            var service = new JobStatusService(db, context, new TestAuthorization(Permissions.Confirmation.Decide), TimeProvider.System);

            var result = await service.ChangeAsync(
                new(employeeId, "Confirmed", JobStatusMutationSource.Confirmation));
            Assert.Equal(ResultStatus.ServiceUnavailable, result.Status);
        }

        await AssertUnchangedAsync(database, tenantId, employeeId, "Probation");
    }

    [Fact]
    public async Task Employee_status_model_has_no_optimistic_concurrency_token_for_phase_one()
    {
        using var database = new SqliteInMemoryDatabase();
        await using var db = database.CreateContext(new TestTenantContext());

        var employeeStatus = db.Entry(new Employee()).Metadata.FindProperty(nameof(Employee.JobStatus));
        var employmentStatus = db.Entry(new EmployeeEmployment()).Metadata.FindProperty(nameof(EmployeeEmployment.JobStatus));

        Assert.False(employeeStatus?.IsConcurrencyToken ?? false);
        Assert.False(employmentStatus?.IsConcurrencyToken ?? false);
    }

    private static async Task AssertUnchangedAsync(
        SqliteInMemoryDatabase database,
        Guid tenantId,
        Guid employeeId,
        string expectedStatus)
    {
        await using var verify = database.CreateIsolatedContext(new TestTenantContext(tenantId));
        Assert.Equal(expectedStatus, await verify.Employees.AsNoTracking().Where(x => x.Id == employeeId).Select(x => x.JobStatus).SingleAsync());
        Assert.Equal(expectedStatus, await verify.EmployeeEmployments.AsNoTracking().Where(x => x.EmployeeId == employeeId).Select(x => x.JobStatus).SingleAsync());
        Assert.Empty(await verify.EmployeeAuditLogs.AsNoTracking().Where(x => x.EmployeeId == employeeId).ToListAsync());
    }

    private static async Task<Guid> SeedAsync(SqliteInMemoryDatabase database, Guid tenantId, string? status, bool withEmployment = false)
    {
        var employeeId = Guid.NewGuid();
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"JS{tenantId:N}"[..10], TenantName = "Job Status Test", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "JS-001", FirstName = "Job", LastName = "Status", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active, JobStatus = status });
        if (withEmployment)
            db.EmployeeEmployments.Add(new EmployeeEmployment { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, JobStatus = status });
        await db.SaveChangesAsync();
        return employeeId;
    }

    private sealed class TestAuthorization(params string[] permissions) : ICurrentAuthorizationContext
    {
        public bool HasAnyPermission(params string[] requested) => requested.Any(permissions.Contains);
    }

    private sealed class FailOnceSaveChangesInterceptor : SaveChangesInterceptor
    {
        private int failed;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref failed, 1) == 0)
                throw new DbUpdateException("Injected SaveChangesAsync failure.");

            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailAuditInsertInterceptor : DbCommandInterceptor
    {
        private int failed;

        private void MaybeFail(DbCommand command)
        {
            if (command.CommandText.Contains("EmployeeAuditLogs", StringComparison.Ordinal) &&
                Interlocked.Exchange(ref failed, 1) == 0)
            {
                throw new DbUpdateException("Injected audit persistence failure.");
            }
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            MaybeFail(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            MaybeFail(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            MaybeFail(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            MaybeFail(command);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailTransactionCommitInterceptor : DbTransactionInterceptor
    {
        private static void Fail() => throw new DbUpdateException("Injected transaction commit failure.");

        public override InterceptionResult TransactionCommitting(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result)
        {
            Fail();
            return result;
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            Fail();
            return ValueTask.FromResult(result);
        }
    }
}
