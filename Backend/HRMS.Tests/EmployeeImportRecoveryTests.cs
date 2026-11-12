using System.Security.Cryptography;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests;

public sealed class EmployeeImportRecoveryTests
{
    private static readonly Guid TenantId = SeedData.TenantIds.Demo01;
    private static readonly Guid UserId = SeedData.Users.First(x => x.TenantId == TenantId).Id;
    private static readonly byte[] Workbook = "synthetic recovery workbook"u8.ToArray();

    [Fact]
    public async Task Dry_run_is_read_only_and_apply_requeues_failed_skipped_preserving_pending_and_audits()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var clock = new FixedClock(DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        var tenant = new TestTenantContext(TenantId, UserId);
        var operationId = await CreateOperationAsync(database, tenant, clock, failed: 1, skipped: 1, pending: 1);
        clock.Now = clock.Now.AddMinutes(11);

        using (var db = database.CreateContext(tenant))
        {
            var service = CreateService(db, tenant, clock);
            var dryRun = await service.DryRunAsync(operationId, TenantId);
            Assert.True(dryRun.Passed, string.Join(',', dryRun.Blockers));
            Assert.False(dryRun.Applied);
            Assert.Equal(2, dryRun.RowsToRequeue);
            Assert.Empty(await db.EmployeeImportRecoveryAudits.ToListAsync());

            var applied = await service.RecoverAsync(operationId, TenantId, "operator.test", "approved historical date fix", true);
            Assert.True(applied.Passed, string.Join(',', applied.Blockers));
            Assert.True(applied.Applied);
            Assert.Equal(2, applied.RowsToRequeue);
            Assert.Equal(3, applied.PendingRows);
            Assert.Equal(0, applied.FailedRows);
            Assert.Equal(0, applied.SkippedRows);
        }

        using var verify = database.CreateContext(tenant);
        var rows = await verify.EmployeeImportRows.Where(x => x.OperationId == operationId).ToListAsync();
        Assert.All(rows, row => Assert.Equal(EmployeeImportRowStatus.Pending, row.Status));
        var op = await verify.EmployeeImportOperations.SingleAsync(x => x.Id == operationId);
        Assert.Equal(0, op.FailedRows);
        Assert.Equal(0, op.SkippedRows);
        Assert.Equal(3, op.TotalRows);
        var audit = await verify.EmployeeImportRecoveryAudits.SingleAsync(x => x.OperationId == operationId);
        Assert.Equal("operator.test", audit.Operator);
        Assert.Equal("approved historical date fix", audit.Reason);
        Assert.Equal("Applied", audit.Outcome);
        Assert.Equal(1, audit.BeforeFailedRows);
        Assert.Equal(1, audit.BeforeSkippedRows);
        Assert.Equal(3, audit.AfterPendingRows);
    }

    [Fact]
    public async Task Dry_run_reports_only_safe_validator_failure_diagnostics()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var clock = new FixedClock(DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        var tenant = new TestTenantContext(TenantId, UserId);
        var operationId = await CreateOperationAsync(database, tenant, clock, failed: 1, skipped: 0, pending: 2);
        clock.Now = clock.Now.AddMinutes(11);
        using var db = database.CreateContext(tenant);
        var validator = new FailedWorkbookValidator();
        var report = await CreateService(db, tenant, clock, validator: validator).DryRunAsync(operationId, TenantId);

        Assert.Contains("workbook_validation_failed", report.Blockers);
        var diagnostic = Assert.Single(report.ValidationDiagnostics);
        Assert.Equal("workbook.structure.parser_load", diagnostic.Stage);
        Assert.Equal("workbook.access_denied", diagnostic.ErrorCode);
        Assert.Equal("Workbook", diagnostic.ColumnName);
        Assert.Null(diagnostic.AffectedRows);
        Assert.Empty(diagnostic.RowNumbers);
        Assert.Equal("excel_parser.load_stream", diagnostic.OperationCategory);
        Assert.Equal("memory_stream", diagnostic.ResourceType);
        Assert.Equal("UnauthorizedAccessException", diagnostic.ExceptionType);
        Assert.Equal("0x80070005", diagnostic.SafeHResult);
        Assert.False(diagnostic.ResourceInsideApprovedWorkspace);
        var serialized = System.Text.Json.JsonSerializer.Serialize(report);
        Assert.DoesNotContain("employee.private@example.test", serialized);
        Assert.DoesNotContain("raw database detail", serialized);
    }

    [Fact]
    public async Task Dry_run_aggregates_row_findings_with_allowlisted_columns_and_exact_rows()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var clock = new FixedClock(DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        var tenant = new TestTenantContext(TenantId, UserId);
        var operationId = await CreateOperationAsync(database, tenant, clock, failed: 1, skipped: 0, pending: 2);
        clock.Now = clock.Now.AddMinutes(11);
        using var db = database.CreateContext(tenant);
        var validator = new WorkbookWithSensitiveIssueValidator();
        var report = await CreateService(db, tenant, clock, validator: validator).DryRunAsync(operationId, TenantId);

        var diagnostic = Assert.Single(report.ValidationDiagnostics);
        Assert.Equal("row.validation", diagnostic.Stage);
        Assert.Equal("master.invalid", diagnostic.ErrorCode);
        Assert.Equal("Unknown", diagnostic.ColumnName);
        Assert.Equal(2, diagnostic.AffectedRows);
        Assert.Equal([2, 3], diagnostic.RowNumbers);
        var serialized = System.Text.Json.JsonSerializer.Serialize(report);
        Assert.DoesNotContain("private header", serialized);
        Assert.DoesNotContain("employee.private@example.test", serialized);
    }

    [Fact]
    public async Task Active_lease_wrong_tenant_and_missing_explicit_approval_are_rejected()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var clock = new FixedClock(DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        var tenant = new TestTenantContext(TenantId, UserId);
        var operationId = await CreateOperationAsync(database, tenant, clock, failed: 1, skipped: 0, pending: 2);
        using var db = database.CreateContext(tenant);
        var service = CreateService(db, tenant, clock);
        Assert.Contains("lease_not_expired_or_not_owned", (await service.DryRunAsync(operationId, TenantId)).Blockers);
        Assert.Contains("tenant_mismatch", (await service.DryRunAsync(operationId, SeedData.TenantIds.Demo02)).Blockers);
        var noApproval = await service.RecoverAsync(operationId, TenantId, "operator.test", "approved retry reason", false);
        Assert.Contains("approval_required", noApproval.Blockers);
    }

    [Fact]
    public async Task Repeated_recovery_is_rejected_by_active_recovery_lease()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var clock = new FixedClock(DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        var tenant = new TestTenantContext(TenantId, UserId);
        var operationId = await CreateOperationAsync(database, tenant, clock, failed: 1, skipped: 0, pending: 2);
        clock.Now = clock.Now.AddMinutes(11);
        using var db = database.CreateContext(tenant);
        var service = CreateService(db, tenant, clock);
        Assert.True((await service.RecoverAsync(operationId, TenantId, "operator.test", "approved retry reason", true)).Applied);
        var repeat = await service.RecoverAsync(operationId, TenantId, "operator.test", "approved retry reason", true);
        Assert.False(repeat.Applied);
        Assert.Contains("lease_not_expired_or_not_owned", repeat.Blockers);
        clock.Now = clock.Now.AddMinutes(11);
        var afterLeaseExpiry = await service.RecoverAsync(operationId, TenantId, "operator.test", "approved retry reason", true);
        Assert.False(afterLeaseExpiry.Applied);
        Assert.Contains("no_recoverable_rows", afterLeaseExpiry.Blockers);
        var audits = await db.EmployeeImportRecoveryAudits.Where(x => x.OperationId == operationId).ToListAsync();
        Assert.Equal(3, audits.Count);
        Assert.Equal("Rejected", audits.OrderBy(x => x.RecoveryAtUtc).Last().Outcome);
    }

    [Fact]
    public async Task Counter_mismatch_and_invalid_operation_state_block_recovery_without_audit()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var clock = new FixedClock(DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        var tenant = new TestTenantContext(TenantId, UserId);
        var operationId = await CreateOperationAsync(database, tenant, clock, failed: 1, skipped: 0, pending: 2);
        using (var db = database.CreateContext(tenant))
        {
            var batch = await db.ImportBatches.SingleAsync(x => x.Id == operationId);
            batch.FailedRows = 0;
            await db.SaveChangesAsync();
        }
        clock.Now = clock.Now.AddMinutes(11);
        using (var db = database.CreateContext(tenant))
        {
            var result = await CreateService(db, tenant, clock).DryRunAsync(operationId, TenantId);
            Assert.Contains("batch_counter_mismatch", result.Blockers);
        }
        using (var db = database.CreateContext(tenant))
        {
            var batch = await db.ImportBatches.SingleAsync(x => x.Id == operationId);
            var op = await db.EmployeeImportOperations.SingleAsync(x => x.Id == operationId);
            batch.FailedRows = 1;
            op.RecordRow(EmployeeImportRowStatus.Failed);
            await db.SaveChangesAsync();
        }
        using (var db = database.CreateContext(tenant))
        {
            var result = await CreateService(db, tenant, clock).DryRunAsync(operationId, TenantId);
            Assert.Contains("operation_row_counter_mismatch", result.Blockers);
        }
        using (var db = database.CreateContext(tenant))
        {
            var op = await db.EmployeeImportOperations.SingleAsync(x => x.Id == operationId);
            op.RecordRow(EmployeeImportRowStatus.Failed);
            Assert.True(op.TransitionTo(EmployeeImportOperationState.Failed, clock.GetUtcNow().UtcDateTime, "test"));
            await db.SaveChangesAsync();
        }
        using (var db = database.CreateContext(tenant))
        {
            var result = await CreateService(db, tenant, clock).RecoverAsync(operationId, TenantId, "operator.test", "approved retry reason", true);
            Assert.Contains("operation_state_invalid", result.Blockers);
            Assert.Equal("Rejected", (await db.EmployeeImportRecoveryAudits.SingleAsync()).Outcome);
        }
        using (var db = database.CreateContext(tenant))
        {
            var unknown = await CreateService(db, tenant, clock).DryRunAsync(Guid.NewGuid(), TenantId);
            Assert.Contains("operation_not_found_for_tenant", unknown.Blockers);
        }
    }

    [Fact]
    public async Task Existing_succeeded_employee_is_preserved_and_extra_batch_employee_blocks_duplicates()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var clock = new FixedClock(DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        var tenant = new TestTenantContext(TenantId, UserId);
        var employeeId = SeedData.Employees.First(x => x.TenantId == TenantId).Id;
        var operationId = await CreateOperationAsync(database, tenant, clock, succeededEmployeeId: employeeId, failed: 1, skipped: 1, pending: 1);
        clock.Now = clock.Now.AddMinutes(11);
        using (var db = database.CreateContext(tenant))
        {
            var result = await CreateService(db, tenant, clock, totalRows: 4).RecoverAsync(operationId, TenantId, "operator.test", "approved retry reason", true);
            Assert.True(result.Applied, string.Join(',', result.Blockers));
            Assert.Equal(1, result.SucceededRows);
            Assert.Equal(3, result.PendingRows);
            Assert.Equal(employeeId, (await db.EmployeeImportRows.SingleAsync(x => x.OperationId == operationId && x.Status == EmployeeImportRowStatus.Succeeded)).EmployeeId);
        }

        // An employee audit link for this batch without a corresponding succeeded row is evidence of a partial commit.
        using (var db = database.CreateContext(tenant))
        {
            var secondEmployee = SeedData.Employees.First(x => x.TenantId == TenantId && x.Id != employeeId);
            db.EmployeeAuditLogs.Add(new EmployeeAuditLog
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = secondEmployee.Id,
                Module = "Employee", ChangedBy = "test", Source = "Import", ImportBatchId = operationId
            });
            await db.SaveChangesAsync();
        }
        clock.Now = clock.Now.AddMinutes(11);
        using (var db = database.CreateContext(tenant))
        {
            var result = await CreateService(db, tenant, clock).DryRunAsync(operationId, TenantId);
            Assert.Contains("committed_employee_audit_mismatch", result.Blockers);
        }
    }

    [Fact]
    public async Task Recovery_rolls_back_rows_counters_and_success_audit_when_commit_fails()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var clock = new FixedClock(DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        var tenant = new TestTenantContext(TenantId, UserId);
        var operationId = await CreateOperationAsync(database, tenant, clock, failed: 1, skipped: 1, pending: 1);
        clock.Now = clock.Now.AddMinutes(11);
        using (var db = database.CreateContext(tenant, new FailRecoveryAuditInsertInterceptor()))
        {
            var result = await CreateService(db, tenant, clock).RecoverAsync(operationId, TenantId, "operator.test", "approved retry reason", true);
            Assert.False(result.Applied);
            Assert.Contains("recovery_unexpected_error", result.Blockers);
        }
        using var verify = database.CreateContext(tenant);
        var auditCount = await verify.EmployeeImportRecoveryAudits.CountAsync(x => x.OperationId == operationId);
        Assert.Equal(0, auditCount);
        var rows = await verify.EmployeeImportRows.Where(x => x.OperationId == operationId).ToListAsync();
        Assert.Contains(rows, x => x.Status == EmployeeImportRowStatus.Failed);
        Assert.Contains(rows, x => x.Status == EmployeeImportRowStatus.Skipped);
        var operation = await verify.EmployeeImportOperations.SingleAsync(x => x.Id == operationId);
        Assert.Equal(1, operation.FailedRows);
        Assert.Equal(1, operation.SkippedRows);

        var audit = new EmployeeImportRecoveryAudit
        {
            Id = Guid.NewGuid(), TenantId = TenantId, OperationId = operationId,
            RecoveryAtUtc = clock.GetUtcNow().UtcDateTime, Operator = "operator.test", Reason = "audit immutability test", Outcome = "Test"
        };
        verify.EmployeeImportRecoveryAudits.Add(audit);
        await verify.SaveChangesAsync();
        audit.Outcome = "Tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => verify.SaveChangesAsync());
    }

    [Fact]
    public async Task Concurrent_recovery_attempts_have_at_most_one_winner()
    {
        using var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        var clock = new FixedClock(DateTimeOffset.Parse("2026-11-01T00:00:00Z"));
        var tenant = new TestTenantContext(TenantId, UserId);
        var operationId = await CreateOperationAsync(database, tenant, clock, failed: 1, skipped: 1, pending: 1);
        clock.Now = clock.Now.AddMinutes(11);
        using var left = database.CreateIsolatedContext(tenant);
        using var right = database.CreateIsolatedContext(tenant);
        var results = await Task.WhenAll(
            CreateService(left, tenant, clock).RecoverAsync(operationId, TenantId, "operator.test", "approved retry reason", true),
            CreateService(right, tenant, clock).RecoverAsync(operationId, TenantId, "operator.test", "approved retry reason", true));
        Assert.Single(results.Where(x => x.Applied));
        Assert.Single(results.Where(x => !x.Applied));
        using var verify = database.CreateContext(tenant);
        Assert.Equal(2, await verify.EmployeeImportRecoveryAudits.CountAsync(x => x.OperationId == operationId));
    }

    private static EmployeeImportRecoveryService CreateService(HRMS.Infrastructure.Persistence.HrmsDbContext db,
        TestTenantContext tenant, TimeProvider clock, int totalRows = 3, IEmployeeBulkUploadValidationService? validator = null) => new(db, tenant, new MemoryWorkbookReader(), validator ?? new ValidWorkbookValidator(totalRows),
        clock, NullLogger<EmployeeImportRecoveryService>.Instance);

    private static async Task<Guid> CreateOperationAsync(SqliteInMemoryDatabase database, TestTenantContext tenant,
        FixedClock clock, int failed, int skipped, int pending, Guid? succeededEmployeeId = null)
    {
        using var db = database.CreateContext(tenant);
        var total = failed + skipped + pending + (succeededEmployeeId.HasValue ? 1 : 0);
        var operationId = Guid.NewGuid();
        var hash = Convert.ToHexString(SHA256.HashData(Workbook));
        var batch = new ImportBatch
        {
            Id = operationId, TenantId = TenantId, ImportedBy = "test", TotalRows = total,
            Status = "Processing", StartedAtUtc = clock.GetUtcNow().UtcDateTime,
            SuccessfulRows = succeededEmployeeId.HasValue ? 1 : 0, FailedRows = failed, SkippedRows = skipped
        };
        var operation = new EmployeeImportOperation
        {
            Id = operationId, TenantId = TenantId, ImportBatchId = operationId,
            WorkbookSha256 = hash, ConfirmationTokenHash = new string('b', 64),
            InitiatedByUserId = UserId, PreparedAtUtc = clock.GetUtcNow().UtcDateTime, TotalRows = total
        };
        var prepLease = Guid.NewGuid();
        Assert.True(operation.BeginArtifactPreparation(prepLease, clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(10)));
        Assert.True(operation.CompleteArtifactPreparation(prepLease, clock.GetUtcNow().UtcDateTime));
        Assert.True(operation.Enqueue(new string('b', 64), clock.GetUtcNow().UtcDateTime));
        Assert.True(operation.AcquireWorkerClaim(Guid.NewGuid(), clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(10)));
        Assert.True(operation.TransitionTo(EmployeeImportOperationState.Processing, clock.GetUtcNow().UtcDateTime));
        var rowNumber = 2;
        if (succeededEmployeeId is Guid employeeId)
        {
            var row = new EmployeeImportRow { Id = Guid.NewGuid(), TenantId = TenantId, OperationId = operationId, ExcelRowNumber = rowNumber++ };
            Assert.True(row.Start(clock.GetUtcNow().UtcDateTime));
            Assert.True(row.Complete(employeeId, "SYNTHETIC", clock.GetUtcNow().UtcDateTime));
            operation.RecordRow(EmployeeImportRowStatus.Succeeded);
            db.EmployeeAuditLogs.Add(new EmployeeAuditLog
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, Module = "Employee",
                ChangedBy = "test", Source = "Import", ImportBatchId = operationId
            });
            operation.Rows.Add(row);
        }
        for (var i = 0; i < failed; i++)
        {
            var row = NewRow(operationId, rowNumber++, EmployeeImportRowStatus.Failed, clock);
            operation.RecordRow(EmployeeImportRowStatus.Failed);
            operation.Rows.Add(row);
        }
        for (var i = 0; i < skipped; i++)
        {
            var row = NewRow(operationId, rowNumber++, EmployeeImportRowStatus.Skipped, clock);
            operation.RecordRow(EmployeeImportRowStatus.Skipped);
            operation.Rows.Add(row);
        }
        for (var i = 0; i < pending; i++)
            operation.Rows.Add(new EmployeeImportRow { Id = Guid.NewGuid(), TenantId = TenantId, OperationId = operationId, ExcelRowNumber = rowNumber++ });
        db.ImportBatches.Add(batch);
        db.EmployeeImportOperations.Add(operation);
        await db.SaveChangesAsync();
        return operationId;
    }

    private static EmployeeImportRow NewRow(Guid operationId, int rowNumber, EmployeeImportRowStatus status, FixedClock clock)
    {
        var row = new EmployeeImportRow { Id = Guid.NewGuid(), TenantId = TenantId, OperationId = operationId, ExcelRowNumber = rowNumber };
        if (status == EmployeeImportRowStatus.Failed)
        {
            Assert.True(row.Start(clock.GetUtcNow().UtcDateTime));
            Assert.True(row.Fail("employee.create_failed", "Sanitized test failure.", clock.GetUtcNow().UtcDateTime));
        }
        else Assert.True(row.Skip("import.stopped", "Sanitized test skip.", clock.GetUtcNow().UtcDateTime));
        return row;
    }

    private sealed class MemoryWorkbookReader : IEmployeeImportWorkbookReader
    {
        public Task<Stream?> OpenReadAsync(Guid operationId, string expectedSha256, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(new MemoryStream(Workbook, writable: false));
    }

    private sealed class ValidWorkbookValidator(int totalRows) : IEmployeeBulkUploadValidationService
    {
        public Task<Result<EmployeeBulkUploadPreviewDto>> ValidateAsync(Stream workbook, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<EmployeeBulkUploadPreviewDto>.Success(new(totalRows, totalRows, 0, 0, [])));

        public Task<Result<EmployeeBulkUploadValidationResult>> ValidateForExecutionAsync(Stream workbook, CancellationToken cancellationToken = default)
        {
            var rows = Enumerable.Range(2, totalRows).Select(row => new ValidatedEmployeeOnboardingRow(row, new EmployeeOnboardingRequest())).ToArray();
            return Task.FromResult(Result<EmployeeBulkUploadValidationResult>.Success(new(new(totalRows, totalRows, 0, 0, []), rows)));
        }
    }

    private sealed class FailedWorkbookValidator : IEmployeeBulkUploadValidationService
    {
        public Task<Result<EmployeeBulkUploadPreviewDto>> ValidateAsync(Stream workbook, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<EmployeeBulkUploadPreviewDto>.Failure(ResultStatus.ValidationFailed, "raw database detail with employee.private@example.test"));

        public Task<Result<EmployeeBulkUploadValidationResult>> ValidateForExecutionAsync(Stream workbook, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<EmployeeBulkUploadValidationResult>.Failure(ResultStatus.ValidationFailed,
                "raw database detail with employee.private@example.test",
                [new ValidationError("private header", "raw database detail")],
                "workbook.structure.parser_load", "workbook.access_denied", null,
                "excel_parser.load_stream", "memory_stream", "UnauthorizedAccessException", "0x80070005", false));
    }

    private sealed class WorkbookWithSensitiveIssueValidator : IEmployeeBulkUploadValidationService
    {
        public Task<Result<EmployeeBulkUploadPreviewDto>> ValidateAsync(Stream workbook, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<EmployeeBulkUploadPreviewDto>.Invalid("test", "not used"));

        public Task<Result<EmployeeBulkUploadValidationResult>> ValidateForExecutionAsync(Stream workbook, CancellationToken cancellationToken = default)
        {
            var issue = new EmployeeBulkUploadIssue("private header", "private header employee.private@example.test", 55,
                "master.invalid", "contains employee.private@example.test", "contains raw database detail");
            var preview = new EmployeeBulkUploadPreviewDto(3, 1, 2, 0,
            [
                new EmployeeBulkUploadPreviewRow(2, "Invalid", [issue]),
                new EmployeeBulkUploadPreviewRow(3, "Invalid", [issue])
            ]);
            var rows = Enumerable.Range(2, 3).Select(row => new ValidatedEmployeeOnboardingRow(row, new EmployeeOnboardingRequest())).ToArray();
            return Task.FromResult(Result<EmployeeBulkUploadValidationResult>.Success(new(preview, rows)));
        }
    }

    private sealed class FailRecoveryAuditInsertInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<EmployeeImportRecoveryAudit>()
                    .Any(x => x.State == EntityState.Added) == true)
                throw new InvalidOperationException("synthetic audit insert failure");
            return ValueTask.FromResult(result);
        }
    }
}
