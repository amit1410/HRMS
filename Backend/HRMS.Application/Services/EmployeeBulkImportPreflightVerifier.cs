using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.Extensions.Options;

namespace HRMS.Application.Services;

/// <summary>
/// Read-only verifier for the single approved local operation. It intentionally has no dependency on
/// the execution service or persistence mutation methods.
/// </summary>
public sealed class EmployeeBulkImportPreflightVerifier(
    IEmployeeImportPreflightReadStore readStore,
    ITenantContext tenant,
    ICurrentAuthorizationContext authorization,
    IEmployeeImportWorkbookReader workbookStore,
    IEmployeeBulkUploadValidationService validation,
    IOptions<EmployeeBulkImportWorkerOptions> workerOptions,
    TimeProvider clock,
    int expectedRows = 10_000) : IEmployeeBulkImportPreflightVerifier
{
    public static readonly Guid TargetOperationId = Guid.Parse("b89d04aa-1419-4c85-a7e3-48ab1d92d111");
    public const string TargetTenantCode = "WEB1";
    public const int ExpectedRows = 10_000;

    public async Task<EmployeeBulkImportPreflightReport> VerifyAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var checks = new List<EmployeeBulkImportPreflightCheck>();
        var emptyCategories = new Dictionary<string, int>();
        var tenantId = tenant.TenantId;

        var operationIdMatches = operationId == TargetOperationId;
        checks.Add(new("operation_allowlist", operationIdMatches,
            operationIdMatches ? "operation.allowed" : "operation.not_allowlisted"));
        if (!operationIdMatches)
            return Empty(operationId, tenantId, checks, emptyCategories);

        var authorized = tenant.UserId is Guid userId && userId != Guid.Empty &&
                         tenantId is Guid authenticatedTenantId && authenticatedTenantId != Guid.Empty &&
                         authorization.HasAnyPermission(Permissions.Employee.Import);
        checks.Add(new("authorization", authorized, authorized ? "authorized" : "authorization.denied"));
        checks.Add(new("worker_disabled", !workerOptions.Value.Enabled,
            workerOptions.Value.Enabled ? "worker.enabled" : "worker.disabled"));
        if (!authorized || workerOptions.Value.Enabled)
            return Empty(operationId, tenantId, checks, emptyCategories);

        // The caller resolves and compares the catalog shard before constructing this tenant-scoped service.
        // Require the signed token identity to match that resolved shard; never accept a caller-supplied ID.
        checks.Add(new("tenant_identity", tenantId.HasValue, tenantId.HasValue ? "tenant.authenticated" : "tenant.missing"));
        if (!tenantId.HasValue) return Empty(operationId, null, checks, emptyCategories);

        var operation = await readStore.FindOperationAsync(tenantId.Value, operationId, cancellationToken);
        if (operation is null)
        {
            checks.Add(new("operation_identity", false, "operation.not_found_in_authenticated_tenant"));
            return Empty(operationId, tenantId, checks, emptyCategories);
        }

        checks.Add(new("operation_identity", true, "operation.matched"));
        var confirmedAndQueued = operation.State == EmployeeImportOperationState.Queued && operation.ConfirmedAtUtc.HasValue;
        checks.Add(new("confirmed_queued", confirmedAndQueued,
            confirmedAndQueued ? "operation.confirmed_queued" : "operation.state_or_confirmation_mismatch"));

        var now = clock.GetUtcNow().UtcDateTime;
        var hasLeaseId = operation.ClaimLeaseId.HasValue;
        var hasLeaseExpiry = operation.ClaimLeaseExpiresAtUtc.HasValue;
        var leaseMetadataConsistent = hasLeaseId == hasLeaseExpiry;
        var activeLease = hasLeaseId && hasLeaseExpiry && operation.ClaimLeaseExpiresAtUtc!.Value > now;
        checks.Add(new("no_active_lease", leaseMetadataConsistent && !activeLease,
            !leaseMetadataConsistent ? "lease.metadata_inconsistent" : activeLease ? "lease.active" : "lease.clear"));

        var rowCounts = await readStore.CountRowsAsync(tenantId.Value, operationId, cancellationToken);
        var pending = rowCounts.Pending;
        var processing = rowCounts.Processing;
        var succeeded = rowCounts.Succeeded;
        var failed = rowCounts.Failed;
        var skipped = rowCounts.Skipped;
        var countsMatch = operation.TotalRows == expectedRows && rowCounts.Total == expectedRows &&
                          pending == expectedRows && processing == 0 && succeeded == 0 && failed == 0 && skipped == 0 &&
                          operation.SucceededRows == 0 && operation.FailedRows == 0 && operation.SkippedRows == 0;
        checks.Add(new("row_counts", countsMatch, countsMatch ? "rows.pending_expected" : "rows.count_mismatch"));

        var report = new EmployeeBulkImportPreflightReport(
            operationId, tenantId, TargetTenantCode, operation.State.ToString(), operation.ConfirmedAtUtc,
            operation.TotalRows, pending, processing, succeeded, failed, skipped, activeLease,
            null, null, emptyCategories, checks);

        // Avoid touching the artifact or current master data if the operation is not safely in its expected
        // pre-execution state. This service has no claim, enqueue, confirmation, or execution dependency.
        if (!confirmedAndQueued || !leaseMetadataConsistent || activeLease || !countsMatch)
            return report;

        var categories = new Dictionary<string, int>(StringComparer.Ordinal);
        Stream? workbook;
        try
        {
            workbook = await workbookStore.OpenReadAsync(operationId, operation.WorkbookSha256, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            checks.Add(new("artifact_integrity", false, "artifact.verification_failed"));
            return report with { Checks = checks };
        }

        if (workbook is null)
        {
            checks.Add(new("artifact_integrity", false, "artifact.unavailable"));
            return report with { Checks = checks };
        }

        await using (workbook)
        {
            // OpenReadAsync authenticates AES-GCM and compares the plaintext SHA-256 with the operation record.
            checks.Add(new("artifact_integrity", true, "artifact.authenticated_hash_match"));
            try
            {
                var result = await validation.ValidateForExecutionAsync(workbook, cancellationToken);
                var validationResult = result.Succeeded ? result.Value : null;
                if (validationResult is not null)
                {
                    foreach (var issue in validationResult.Preview.Rows.SelectMany(row => row.Issues))
                    {
                        var category = SanitizeCategory(issue.ErrorCode);
                        categories[category] = categories.GetValueOrDefault(category) + 1;
                    }
                }
                var validRows = validationResult?.Preview.ValidRows;
                var invalidRows = validationResult?.Preview.InvalidRows;
                var validationPassed = result.Succeeded && validationResult!.Preview.TotalRows == expectedRows &&
                                       validRows == expectedRows && invalidRows == 0 &&
                                       validationResult!.ValidatedRows.Count == expectedRows;
                checks.Add(new("current_workbook_validation", validationPassed,
                    validationPassed ? "validation.current_data_valid" : result.Succeeded ? "validation.current_data_invalid" : "validation.failed"));

                return report with
                {
                    ValidatedRows = validationResult?.ValidatedRows.Count,
                    InvalidRows = invalidRows,
                    SanitizedErrorCategories = categories,
                    Checks = checks
                };
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                checks.Add(new("current_workbook_validation", false, "validation.failed"));
                return report with { SanitizedErrorCategories = categories, Checks = checks };
            }
        }
    }

    private EmployeeBulkImportPreflightReport Empty(
        Guid operationId,
        Guid? tenantId,
        IReadOnlyList<EmployeeBulkImportPreflightCheck> checks,
        IReadOnlyDictionary<string, int> categories) =>
        new(operationId, tenantId, TargetTenantCode, null, null, null, null, null, null, null, null,
            null, null, null, categories, checks);

    private static string SanitizeCategory(string? category) =>
        !string.IsNullOrWhiteSpace(category) && category.Length <= 64 &&
        category.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')
            ? category
            : "validation.other";
}
