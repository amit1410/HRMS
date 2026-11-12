using System.Security.Cryptography;
using System.Data.Common;
using System.Text.RegularExpressions;
using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

/// <summary>Transactional, tenant-scoped recovery for terminal failed/skipped employee-import rows.</summary>
public sealed class EmployeeImportRecoveryService(
    IHrmsDbContext db,
    ITenantContext tenant,
    IEmployeeImportWorkbookReader workbookReader,
    IEmployeeBulkUploadValidationService validation,
    TimeProvider clock,
    ILogger<EmployeeImportRecoveryService> logger) : IEmployeeImportRecoveryService
{
    private static readonly Regex SafeOperator = new(@"\A[A-Za-z0-9\\._@-]{1,256}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SafeReason = new(@"\A[A-Za-z0-9 .,_:/#()\-]{8,500}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<EmployeeImportRecoveryReport> DryRunAsync(Guid operationId, Guid expectedTenantId,
        CancellationToken cancellationToken = default)
    {
        if (!TenantMatches(expectedTenantId)) return Blocked(operationId, expectedTenantId, "tenant_mismatch");
        var snapshot = await InspectAsync(operationId, expectedTenantId, cancellationToken);
        return snapshot.Report;
    }

    public async Task<EmployeeImportRecoveryReport> RecoverAsync(Guid operationId, Guid expectedTenantId,
        string operatorName, string reason, bool explicitlyApproved, CancellationToken cancellationToken = default)
    {
        var actor = operatorName ?? string.Empty;
        var safeReason = reason ?? string.Empty;
        var now = clock.GetUtcNow().UtcDateTime;
        if (!explicitlyApproved || !TenantMatches(expectedTenantId) || !SafeOperator.IsMatch(actor) ||
            !SafeReason.IsMatch(safeReason) || ContainsSensitivePattern(safeReason))
            return Blocked(operationId, expectedTenantId, !explicitlyApproved ? "approval_required" : "request_invalid");

        await using var tx = await db.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            var before = await InspectAsync(operationId, expectedTenantId, cancellationToken);
            if (!before.Report.Passed)
            {
                await tx.RollbackAsync(cancellationToken);
                await tx.DisposeAsync();
                await PersistOutcomeAuditAsync(before.Report, actor, safeReason, "Rejected", before.Report.Blockers.FirstOrDefault(), cancellationToken);
                return before.Report;
            }

            // Claim the expired operation inside this transaction. The worker uses the same concurrency stamp
            // and lease protocol, so only this transaction or a worker can win the claim.
            var operation = await db.EmployeeImportOperations.IgnoreQueryFilters()
                .SingleAsync(x => x.TenantId == expectedTenantId && x.Id == operationId, cancellationToken);
            var leaseId = Guid.NewGuid();
            if (!operation.AcquireWorkerClaim(leaseId, now, TimeSpan.FromMinutes(10)))
            {
                await tx.RollbackAsync(cancellationToken);
                await tx.DisposeAsync();
                var blocked = Blocked(operationId, expectedTenantId, "lease_claim_conflict");
                await PersistOutcomeAuditAsync(before.Report, actor, safeReason, "Rejected", "lease_claim_conflict", cancellationToken);
                return blocked;
            }
            await db.SaveChangesAsync(cancellationToken);

            db.ClearChangeTracker();
            var locked = await InspectAsync(operationId, expectedTenantId, cancellationToken, ownLeaseId: leaseId);
            if (!locked.Report.Passed)
            {
                await tx.RollbackAsync(cancellationToken);
                await tx.DisposeAsync();
                await PersistOutcomeAuditAsync(locked.Report, actor, safeReason, "Rejected", locked.Report.Blockers.FirstOrDefault(), cancellationToken);
                return locked.Report;
            }

            var freshOperation = await db.EmployeeImportOperations.IgnoreQueryFilters()
                .SingleAsync(x => x.TenantId == expectedTenantId && x.Id == operationId, cancellationToken);
            var rows = await db.EmployeeImportRows.IgnoreQueryFilters()
                .Where(x => x.TenantId == expectedTenantId && x.OperationId == operationId)
                .OrderBy(x => x.ExcelRowNumber).ToListAsync(cancellationToken);
            var rowSucceeded = rows.Count(x => x.Status == EmployeeImportRowStatus.Succeeded);
            var rowFailed = rows.Count(x => x.Status == EmployeeImportRowStatus.Failed);
            var rowSkipped = rows.Count(x => x.Status == EmployeeImportRowStatus.Skipped);
            var requeued = 0;
            foreach (var row in rows.Where(x => x.Status is EmployeeImportRowStatus.Failed or EmployeeImportRowStatus.Skipped))
            {
                if (!row.RequeueForRecovery()) throw new InvalidOperationException("row_requeue_conflict");
                requeued++;
            }
            foreach (var row in rows.Where(x => x.Status == EmployeeImportRowStatus.Processing))
            {
                if (!row.ResetAbandonedProcessing()) throw new InvalidOperationException("row_requeue_conflict");
                requeued++;
            }

            var summary = $"Recovery {now:O} by {actor}; reason={safeReason}; requeued={requeued}.";
            if (!freshOperation.ReconcileForRecovery(rowSucceeded, rowFailed, rowSkipped, now, summary))
                throw new InvalidOperationException("counter_reconciliation_conflict");
            var batch = await db.ImportBatches.IgnoreQueryFilters()
                .SingleAsync(x => x.TenantId == expectedTenantId && x.Id == freshOperation.ImportBatchId, cancellationToken);
            batch.SuccessfulRows = rowSucceeded;
            batch.FailedRows = 0;
            batch.SkippedRows = 0;
            batch.Status = EmployeeImportOperationState.Processing.ToString();
            batch.CompletedAtUtc = null;
            batch.Message = "Authorized recovery requeued eligible rows; processing awaits the worker.";

            var audit = new EmployeeImportRecoveryAudit
            {
                Id = Guid.NewGuid(),
                TenantId = expectedTenantId,
                OperationId = operationId,
                RecoveryAtUtc = now,
                Operator = actor,
                Reason = safeReason,
                Outcome = "Applied",
                BeforeTotalRows = before.Report.TotalRows,
                BeforeSucceededRows = before.Report.SucceededRows,
                BeforeFailedRows = before.Report.FailedRows,
                BeforeSkippedRows = before.Report.SkippedRows,
                BeforePendingRows = before.Report.PendingRows,
                AfterTotalRows = freshOperation.TotalRows,
                AfterSucceededRows = rowSucceeded,
                AfterFailedRows = 0,
                AfterSkippedRows = 0,
                AfterPendingRows = rows.Count(x => x.Status == EmployeeImportRowStatus.Pending)
            };
            db.EmployeeImportRecoveryAudits.Add(audit);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            logger.LogInformation("Employee import recovery applied for operation {OperationId}; rows requeued {RowsRequeued}; audit {AuditId}.",
                operationId, requeued, audit.Id);
            return new(true, true, operationId, expectedTenantId, freshOperation.State.ToString(), freshOperation.TotalRows,
                rowSucceeded, 0, 0, audit.AfterPendingRows, requeued, freshOperation.ClaimLeaseExpiresAtUtc, [], "recovery_applied");
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(CancellationToken.None);
            await tx.DisposeAsync();
            await PersistOutcomeAuditAsync(operationId, expectedTenantId, actor, safeReason, "Rejected", "concurrent_change", CancellationToken.None);
            return Blocked(operationId, expectedTenantId, "concurrent_change");
        }
        catch (DbException)
        {
            await tx.RollbackAsync(CancellationToken.None);
            await tx.DisposeAsync();
            await PersistOutcomeAuditAsync(operationId, expectedTenantId, actor, safeReason, "Rejected", "concurrent_storage_conflict", CancellationToken.None);
            return Blocked(operationId, expectedTenantId, "concurrent_storage_conflict");
        }
        catch (InvalidOperationException exception) when (exception.Message is "row_requeue_conflict" or "counter_reconciliation_conflict")
        {
            await tx.RollbackAsync(CancellationToken.None);
            await tx.DisposeAsync();
            await PersistOutcomeAuditAsync(operationId, expectedTenantId, actor, safeReason, "Rejected", exception.Message, CancellationToken.None);
            return Blocked(operationId, expectedTenantId, exception.Message);
        }
        catch (OperationCanceledException)
        {
            await tx.RollbackAsync(CancellationToken.None);
            await tx.DisposeAsync();
            await PersistOutcomeAuditAsync(operationId, expectedTenantId, actor, safeReason, "Failed", "recovery_cancelled", CancellationToken.None);
            throw;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            await tx.DisposeAsync();
            logger.LogError("Employee import recovery failed for operation {OperationId}; stage {FailureStage}.", operationId, "transaction");
            await PersistOutcomeAuditAsync(operationId, expectedTenantId, actor, safeReason, "Failed", "recovery_unexpected_error", CancellationToken.None);
            return Blocked(operationId, expectedTenantId, "recovery_unexpected_error");
        }
    }

    private Task PersistOutcomeAuditAsync(EmployeeImportRecoveryReport report, string operatorName, string reason,
        string outcome, string? errorCode, CancellationToken cancellationToken) =>
        PersistOutcomeAuditAsync(report.OperationId, report.TenantId, operatorName, reason, outcome, errorCode,
            cancellationToken, report);

    private async Task PersistOutcomeAuditAsync(Guid operationId, Guid tenantId, string operatorName, string reason,
        string outcome, string? errorCode, CancellationToken cancellationToken, EmployeeImportRecoveryReport? snapshot = null)
    {
        if (!TenantMatches(tenantId) || operationId == Guid.Empty) return;
        try
        {
            db.ClearChangeTracker();
            var current = snapshot;
            if (current is null)
            {
                var operation = await db.EmployeeImportOperations.IgnoreQueryFilters().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == operationId, cancellationToken);
                if (operation is null) return;
                var rows = await db.EmployeeImportRows.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.TenantId == tenantId && x.OperationId == operationId).ToListAsync(cancellationToken);
                var pending = rows.Count(x => x.Status == EmployeeImportRowStatus.Pending);
                current = new(false, false, operationId, tenantId, operation.State.ToString(), operation.TotalRows,
                    operation.SucceededRows, operation.FailedRows, operation.SkippedRows, pending,
                    rows.Count(x => x.Status is EmployeeImportRowStatus.Failed or EmployeeImportRowStatus.Skipped or EmployeeImportRowStatus.Processing),
                    operation.ClaimLeaseExpiresAtUtc, [], "blocked");
            }
            var audit = new EmployeeImportRecoveryAudit
            {
                Id = Guid.NewGuid(), TenantId = tenantId, OperationId = operationId,
                RecoveryAtUtc = clock.GetUtcNow().UtcDateTime, Operator = operatorName, Reason = reason,
                Outcome = outcome, BeforeTotalRows = current.TotalRows, BeforeSucceededRows = current.SucceededRows,
                BeforeFailedRows = current.FailedRows, BeforeSkippedRows = current.SkippedRows, BeforePendingRows = current.PendingRows,
                AfterTotalRows = current.TotalRows, AfterSucceededRows = current.SucceededRows,
                AfterFailedRows = current.FailedRows, AfterSkippedRows = current.SkippedRows, AfterPendingRows = current.PendingRows,
                SanitizedErrorCode = errorCode
            };
            db.EmployeeImportRecoveryAudits.Add(audit);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            logger.LogError("Employee import recovery outcome audit could not be persisted for operation {OperationId}.", operationId);
        }
    }

    private async Task<(EmployeeImportRecoveryReport Report, List<EmployeeImportRow> Rows)> InspectAsync(
        Guid operationId, Guid expectedTenantId, CancellationToken cancellationToken, Guid? ownLeaseId = null)
    {
        var blockers = new List<string>();
        var validationFindings = new List<EmployeeImportValidationDiagnostic>();
        if (operationId == Guid.Empty) return (Blocked(operationId, expectedTenantId, "operation_id_invalid"), []);
        var operation = await db.EmployeeImportOperations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == expectedTenantId && x.Id == operationId, cancellationToken);
        if (operation is null) return (Blocked(operationId, expectedTenantId, "operation_not_found_for_tenant"), []);
        var now = clock.GetUtcNow().UtcDateTime;
        if (operation.State != EmployeeImportOperationState.Processing) blockers.Add("operation_state_invalid");
        if (operation.ClaimLeaseExpiresAtUtc is null ||
            (ownLeaseId is null ? operation.ClaimLeaseExpiresAtUtc > now : operation.ClaimLeaseId != ownLeaseId || operation.ClaimLeaseExpiresAtUtc <= now))
            blockers.Add("lease_not_expired_or_not_owned");

        var rows = await db.EmployeeImportRows.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == expectedTenantId && x.OperationId == operationId)
            .OrderBy(x => x.ExcelRowNumber).ToListAsync(cancellationToken);
        var pending = rows.Count(x => x.Status == EmployeeImportRowStatus.Pending);
        var processing = rows.Count(x => x.Status == EmployeeImportRowStatus.Processing);
        var succeeded = rows.Count(x => x.Status == EmployeeImportRowStatus.Succeeded);
        var failed = rows.Count(x => x.Status == EmployeeImportRowStatus.Failed);
        var skipped = rows.Count(x => x.Status == EmployeeImportRowStatus.Skipped);
        if (failed + skipped + processing == 0) blockers.Add("no_recoverable_rows");
        if (rows.Count != operation.TotalRows || succeeded != operation.SucceededRows || failed != operation.FailedRows || skipped != operation.SkippedRows ||
            pending + processing + succeeded + failed + skipped != operation.TotalRows) blockers.Add("operation_row_counter_mismatch");
        if (rows.Any(x => x.Status == EmployeeImportRowStatus.Succeeded && !x.EmployeeId.HasValue) ||
            rows.Any(x => x.Status != EmployeeImportRowStatus.Succeeded && x.EmployeeId.HasValue)) blockers.Add("row_employee_link_mismatch");

        var batch = await db.ImportBatches.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == expectedTenantId && x.Id == operation.ImportBatchId, cancellationToken);
        if (batch is null || batch.TotalRows != operation.TotalRows || batch.SuccessfulRows != succeeded ||
            batch.FailedRows != failed || batch.SkippedRows != skipped) blockers.Add("batch_counter_mismatch");

        var succeededEmployeeIds = rows.Where(x => x.Status == EmployeeImportRowStatus.Succeeded && x.EmployeeId.HasValue)
            .Select(x => x.EmployeeId!.Value).ToHashSet();
        var auditEmployeeIds = await db.EmployeeAuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == expectedTenantId && x.ImportBatchId == operation.ImportBatchId)
            .Select(x => x.EmployeeId).Distinct().ToListAsync(cancellationToken);
        if (auditEmployeeIds.Count != succeededEmployeeIds.Count || auditEmployeeIds.Any(x => !succeededEmployeeIds.Contains(x)))
            blockers.Add("committed_employee_audit_mismatch");
        if (succeededEmployeeIds.Count > 0)
        {
            var existingIds = await db.Employees.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == expectedTenantId && succeededEmployeeIds.Contains(x.Id))
                .Select(x => x.Id).ToListAsync(cancellationToken);
            if (existingIds.Count != succeededEmployeeIds.Count) blockers.Add("committed_employee_missing");
        }

        try
        {
            await using var artifact = await workbookReader.OpenReadAsync(operationId, operation.WorkbookSha256, cancellationToken);
            if (artifact is null) blockers.Add("workbook_missing");
            else
            {
                using var buffer = new MemoryStream();
                var chunk = new byte[64 * 1024];
                int bytesRead;
                while ((bytesRead = await artifact.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    if (buffer.Length + bytesRead > IEmployeeBulkUploadValidationService.MaximumFileBytes)
                    {
                        blockers.Add("workbook_size_invalid");
                        break;
                    }
                    await buffer.WriteAsync(chunk.AsMemory(0, bytesRead), cancellationToken);
                }
                if (blockers.Contains("workbook_size_invalid"))
                {
                    var sizeReport = BuildReport(operation, succeeded, failed, skipped, pending, blockers);
                    return (sizeReport, rows);
                }
                var bytes = buffer.ToArray();
                var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
                if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(actualHash),
                        System.Text.Encoding.ASCII.GetBytes(operation.WorkbookSha256.ToUpperInvariant()))) blockers.Add("workbook_integrity_mismatch");
                else
                {
                    await using var workbook = new MemoryStream(bytes, writable: false);
                    var validationResult = await validation.ValidateForExecutionAsync(workbook, cancellationToken);
                    if (!validationResult.Succeeded)
                    {
                        blockers.Add("workbook_validation_failed");
                        validationFindings.Add(new(
                            IsSafeDiagnosticToken(validationResult.DiagnosticStage ?? string.Empty) ? validationResult.DiagnosticStage! : "validation.result",
                            IsSafeDiagnosticToken(validationResult.DiagnosticCode ?? string.Empty) ? validationResult.DiagnosticCode! : "workbook.validation_failed",
                            "Workbook",
                            validationResult.DiagnosticRowNumber.HasValue ? 1 : null,
                            validationResult.DiagnosticRowNumber is int row ? [row] : [],
                            IsSafeDiagnosticToken(validationResult.DiagnosticOperation ?? string.Empty) ? validationResult.DiagnosticOperation : "validation.operation_unknown",
                            IsSafeResourceType(validationResult.DiagnosticResourceType) ? validationResult.DiagnosticResourceType : "unknown",
                            IsSafeExceptionType(validationResult.DiagnosticExceptionType) ? validationResult.DiagnosticExceptionType : "unknown",
                            IsSafeHResult(validationResult.DiagnosticHResult) ? validationResult.DiagnosticHResult : null,
                            validationResult.DiagnosticResourceInsideWorkspace));
                    }
                    else
                    {
                        validationFindings.AddRange(BuildValidationDiagnostics(validationResult.Value!));
                        var eligible = rows.Where(x => x.Status is EmployeeImportRowStatus.Pending or EmployeeImportRowStatus.Processing or EmployeeImportRowStatus.Failed or EmployeeImportRowStatus.Skipped)
                            .Select(x => x.ExcelRowNumber).ToHashSet();
                        var validatedRows = validationResult.Value!.ValidatedRows.Select(x => x.ExcelRowNumber).ToHashSet();
                        var invalidRows = validationResult.Value.Preview.Rows.Where(x => x.Status == "Invalid").Select(x => x.RowNumber).ToHashSet();
                        if (validationResult.Value.Preview.TotalRows != operation.TotalRows || eligible.Any(x => !validatedRows.Contains(x)) || eligible.Any(invalidRows.Contains))
                            blockers.Add("current_validation_mismatch");
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { blockers.Add("workbook_verification_error"); }

        var report = BuildReport(operation, succeeded, failed, skipped, pending, blockers, processing) with
        {
            ValidationDiagnostics = validationFindings
        };
        return (report, rows);
    }

    private static IReadOnlyList<EmployeeImportValidationDiagnostic> BuildValidationDiagnostics(EmployeeBulkUploadValidationResult result)
    {
        var allowedColumns = new HashSet<string>([
            "Salutation", "First Name *", "Middle Name", "Last Name *", "Date of Birth", "Gender", "Blood Group", "Marital Status",
            "Birth Country Code", "Birth State Code", "Birth City Code", "Religion", "Caste", "Citizenship", "ESIC Applicable", "ESIC Number",
            "PF Number", "Mediclaim Number", "UAN Number", "Gratuity", "Pension", "Aadhaar Number *", "PAN *", "Date of Joining *",
            "Job Status *", "Official Email", "Personal Email", "Alternate Email", "Official Phone", "Personal Phone", "Emergency Number",
            "Same As Current Address", "Manual Employee Code", "Employment Effective From *", "Holding Company Code", "LOB Code", "Organization Code",
            "Department Code *", "Sub Department Code", "Section Code", "Sub Section Code", "Function Code", "Sub Function Code", "Grade Code",
            "Designation Code *", "Employee Type Code", "Country Location Code", "Work Location Code", "Cost Center Code", "Manager Employee Code",
            "Position Change Reason Code", "Employment Change Reason *", "Change Reason Description", "Business Role", "Grade Level", "Career Group",
            "Employment Type *", "Employment Status", "Employees"
        ], StringComparer.Ordinal);

        return result.Preview.Rows.SelectMany(row => row.Issues.Select(issue => new
            {
                RowNumber = row.RowNumber,
                ColumnName = allowedColumns.Contains(issue.ColumnName) ? issue.ColumnName : "Unknown",
                ErrorCode = IsSafeDiagnosticToken(issue.ErrorCode) ? issue.ErrorCode : "validation.other"
            }))
            .GroupBy(x => (x.ColumnName, x.ErrorCode))
            .Select(group =>
            {
                var rowNumbers = group.Select(x => x.RowNumber).Distinct().Order().ToArray();
                return new EmployeeImportValidationDiagnostic("row.validation", group.Key.ErrorCode,
                    group.Key.ColumnName, rowNumbers.Length, rowNumbers,
                    "row.validation", "memory_stream", null, null, false);
            })
            .OrderBy(x => x.ColumnName, StringComparer.Ordinal).ThenBy(x => x.ErrorCode, StringComparer.Ordinal).ToArray();
    }

    private static bool IsSafeDiagnosticToken(string value) => value.Length is > 0 and <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool IsSafeResourceType(string? value) => value is "memory_stream" or "stream" or "file" or "directory";

    private static bool IsSafeExceptionType(string? value) => value is "UnauthorizedAccessException" or "DbException" or
        "TimeoutException" or "InvalidDataException" or "IOException" or "ArgumentException" or "OtherException";

    private static bool IsSafeHResult(string? value) => value is { Length: 10 } && value.StartsWith("0x", StringComparison.Ordinal) &&
        value.AsSpan(2).ToString().All(Uri.IsHexDigit);

    private static EmployeeImportRecoveryReport BuildReport(EmployeeImportOperation operation, int succeeded, int failed,
        int skipped, int pending, IReadOnlyList<string> blockers, int processing = 0) =>
        new(blockers.Count == 0, false, operation.Id, operation.TenantId, operation.State.ToString(), operation.TotalRows,
            succeeded, failed, skipped, pending, failed + skipped + processing, operation.ClaimLeaseExpiresAtUtc,
            blockers, blockers.Count == 0 ? "dry_run_passed" : "blocked");

    private bool TenantMatches(Guid expectedTenantId) => expectedTenantId != Guid.Empty && tenant.TenantId == expectedTenantId;

    private static EmployeeImportRecoveryReport Blocked(Guid operationId, Guid tenantId, string blocker) =>
        new(false, false, operationId, tenantId, "Unknown", 0, 0, 0, 0, 0, 0, null, [blocker], "blocked");

    private static bool ContainsSensitivePattern(string value) =>
        Regex.IsMatch(value, @"\b[A-Z]{5}[0-9]{4}[A-Z]\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
        Regex.IsMatch(value, @"(?<![0-9])[0-9]{12}(?![0-9])", RegexOptions.CultureInvariant) ||
        Regex.IsMatch(value, @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
        Regex.IsMatch(value, @"\d{7,}", RegexOptions.CultureInvariant) ||
        Regex.IsMatch(value, @"\beyJ[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{8,}\.", RegexOptions.CultureInvariant);
}
