using System.Data;
using System.Security.Cryptography;
using System.Text;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public sealed class EmployeeBulkImportExecutionService(
    IHrmsDbContext db,
    ITenantContext tenant,
    IEmployeeBulkUploadValidationService validation,
    IEmployeeImportPersistenceService persistence,
    IEmployeeImportWorkbookStore workbookStore,
    IEmployeeService employees,
    IEmployeeImportWorkerExecutionContext workerIdentity,
    ILogger<EmployeeBulkImportExecutionService>? logger = null) : IEmployeeBulkImportExecutionService
{
    private const int MaximumWorkbookBytes = IEmployeeBulkUploadValidationService.MaximumFileBytes;

    public async Task<Result<EmployeeBulkImportPreparationDto>> PrepareAsync(Stream workbook, string? fileName, CancellationToken cancellationToken = default)
    {
        if (!HasTenantUser()) return Result<EmployeeBulkImportPreparationDto>.Unauthorized("Authenticated tenant context is required.");
        var bytes = await ReadBoundedAsync(workbook, cancellationToken);
        if (bytes is null) return Result<EmployeeBulkImportPreparationDto>.Invalid("file", "Workbook is empty or exceeds the 10 MB limit.");
        int totalRows;
        {
            var validationResult = await ValidateAsync(bytes, cancellationToken);
            if (!validationResult.Succeeded) return Result<EmployeeBulkImportPreparationDto>.Failure(validationResult.Status, validationResult.Message, validationResult.Errors);
            var validated = validationResult.Value!;
            if (validated.Preview.InvalidRows != 0 || validated.ValidatedRows.Count != validated.Preview.TotalRows)
                return Result<EmployeeBulkImportPreparationDto>.Invalid("workbook", "The workbook has validation errors and cannot be prepared.");
            totalRows = validated.Preview.TotalRows;
        }

        var confirmationToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var workbookHash = Convert.ToHexString(SHA256.HashData(bytes));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(confirmationToken)));
        var preparationLeaseId = Guid.NewGuid();
        var prepared = await persistence.BeginPreparationAsync(workbookHash, tokenHash, totalRows,
            preparationLeaseId, fileName, cancellationToken);
        if (!prepared.Succeeded && prepared.Status == ResultStatus.Conflict)
            prepared = await persistence.ResumePreparationAsync(workbookHash, tokenHash, preparationLeaseId, cancellationToken);
        if (!prepared.Succeeded) return Result<EmployeeBulkImportPreparationDto>.Failure(prepared.Status, prepared.Message, prepared.Errors);
        var operationId = prepared.Value!.Id;
        var failurePhase = "availability-check";
        try
        {
            var available = await workbookStore.IsAvailableAsync(operationId, cancellationToken);
            if (!available)
            {
                failurePhase = "artifact-write";
                await using var source = new MemoryStream(bytes, writable: false);
                await workbookStore.SaveAsync(operationId, source, workbookHash, cancellationToken);
            }
            failurePhase = "artifact-read-back";
            await using var stored = await workbookStore.OpenReadAsync(operationId, workbookHash, cancellationToken)
                ?? throw new InvalidDataException("Prepared workbook artifact is unavailable.");
            var verifiedBytes = await ReadBoundedAsync(stored, cancellationToken)
                ?? throw new InvalidDataException("Prepared workbook artifact is empty or exceeds the size limit.");
            failurePhase = "artifact-hash-verification";
            if (!FixedTextEquals(workbookHash, Convert.ToHexString(SHA256.HashData(verifiedBytes))))
                throw new InvalidDataException("Prepared workbook artifact verification failed.");
            failurePhase = "preparation-completion";
            var completed = await persistence.CompletePreparationAsync(operationId, preparationLeaseId,
                $"Prepared {totalRows} valid employee rows.", cancellationToken);
            if (!completed.Succeeded)
            {
                logger?.LogWarning("Employee import artifact is verified, but preparation completion was rejected with {CompletionStatus}; operation {OperationId} remains recoverable.",
                    completed.Status, operationId);
                await TryReleasePreparationLeaseAsync(operationId, preparationLeaseId);
                return Result<EmployeeBulkImportPreparationDto>.Failure(completed.Status, completed.Message, completed.Errors);
            }
        }
        catch (OperationCanceledException)
        {
            await TryReleasePreparationLeaseAsync(operationId, preparationLeaseId);
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Employee import preparation failed during {FailurePhase}; operation {OperationId} remains recoverable.",
                failurePhase, operationId);
            await TryReleasePreparationLeaseAsync(operationId, preparationLeaseId);
            return Result<EmployeeBulkImportPreparationDto>.Failure(ResultStatus.Conflict,
                "Workbook storage or verification failed. Retry preparation with the same workbook after service recovery.");
        }
        return Result<EmployeeBulkImportPreparationDto>.Success(new(operationId, confirmationToken, totalRows,
            $"Prepared {totalRows} valid employee rows. Confirm using the original workbook and one-use token."));
    }

    public async Task<Result<EmployeeBulkImportAcceptedDto>> ConfirmAsync(Guid operationId, string confirmationToken, CancellationToken cancellationToken = default)
    {
        if (!HasTenantUser()) return Result<EmployeeBulkImportAcceptedDto>.Unauthorized("Authenticated tenant context is required.");
        if (operationId == Guid.Empty || string.IsNullOrWhiteSpace(confirmationToken))
            return Result<EmployeeBulkImportAcceptedDto>.Invalid("confirmation", "Operation ID and confirmation token are required.");
        var operationResult = await persistence.GetAsync(operationId, cancellationToken);
        if (!operationResult.Succeeded) return Result<EmployeeBulkImportAcceptedDto>.Failure(operationResult.Status, operationResult.Message, operationResult.Errors);
        var operation = operationResult.Value!;
        if (operation.InitiatedByUserId != tenant.UserId)
            return Result<EmployeeBulkImportAcceptedDto>.Forbidden("Only the user who prepared this import may confirm it.");
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(confirmationToken)));
        if (!FixedTextEquals(operation.ConfirmationTokenHash, tokenHash))
            return Result<EmployeeBulkImportAcceptedDto>.Conflict("Import operation is already accepted or the confirmation claim is invalid.");

        try
        {
            await using var stored = await workbookStore.OpenReadAsync(operationId, operation.WorkbookSha256, cancellationToken);
            if (stored is null) return Result<EmployeeBulkImportAcceptedDto>.Conflict("The prepared workbook artifact is unavailable.");
            var bytes = await ReadBoundedAsync(stored, cancellationToken);
            if (bytes is null || !FixedTextEquals(operation.WorkbookSha256, Convert.ToHexString(SHA256.HashData(bytes))))
                return Result<EmployeeBulkImportAcceptedDto>.Conflict("The prepared workbook artifact could not be verified.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return Result<EmployeeBulkImportAcceptedDto>.Conflict("The prepared workbook artifact is unavailable or could not be verified.");
        }

        var queued = await persistence.EnqueueAsync(operationId, tokenHash, cancellationToken);
        if (!queued.Succeeded)
            return Result<EmployeeBulkImportAcceptedDto>.Failure(queued.Status, queued.Message, queued.Errors);
        return Result<EmployeeBulkImportAcceptedDto>.Success(new(operationId, EmployeeImportOperationState.Queued.ToString(),
            $"/api/employees/bulk-upload/{operationId:D}"), "Import accepted for background processing.");
    }

    public async Task<Result<EmployeeBulkImportStatusDto>> GetStatusAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (!HasTenantUser()) return Result<EmployeeBulkImportStatusDto>.Unauthorized("Authenticated tenant context is required.");
        var found = await persistence.GetAsync(operationId, cancellationToken);
        if (!found.Succeeded) return Result<EmployeeBulkImportStatusDto>.Failure(found.Status, found.Message, found.Errors);
        var operation = found.Value!;
        var rowEntities = await db.EmployeeImportRows.AsNoTracking()
            .Where(x => x.TenantId == tenant.TenantId && x.OperationId == operationId)
            .OrderBy(x => x.ExcelRowNumber)
            .ToListAsync(cancellationToken);
        var rows = rowEntities.Select(x => new EmployeeBulkImportRowResultDto(x.ExcelRowNumber, x.Status.ToString(), x.EmployeeCode,
            x.ErrorCode, x.SanitizedErrorMessage)).ToList();
        var lastUpdatedAtUtc = new DateTime?[] { operation.PreparedAtUtc, operation.ConfirmedAtUtc, operation.StartedAtUtc, operation.CompletedAtUtc }
            .Concat(rowEntities.SelectMany(x => new[] { x.ProcessingStartedAtUtc, x.ProcessingCompletedAtUtc }))
            .Max()!.Value;
        return Result<EmployeeBulkImportStatusDto>.Success(new(operationId, operation.State.ToString(), operation.TotalRows,
            operation.SucceededRows, operation.FailedRows, operation.SkippedRows, operation.StartedAtUtc,
            operation.CompletedAtUtc, lastUpdatedAtUtc, operation.SanitizedSummary, rows));
    }

    public async Task<Result<EmployeeBulkImportHistoryDto>> GetHistoryAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!HasTenantUser()) return Result<EmployeeBulkImportHistoryDto>.Unauthorized("Authenticated tenant context is required.");
        if (page < 1 || pageSize is < 1 or > 100)
            return Result<EmployeeBulkImportHistoryDto>.Invalid("pagination", "Page must be positive and page size must be between 1 and 100.");

        var query = db.EmployeeImportOperations.AsNoTracking().Where(x => x.TenantId == tenant.TenantId);
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.PreparedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new EmployeeBulkImportHistoryItemDto(x.Id, x.State.ToString(), x.TotalRows,
                x.SucceededRows, x.FailedRows, x.SkippedRows, x.PreparedAtUtc, x.CompletedAtUtc, x.SanitizedSummary))
            .ToListAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        return Result<EmployeeBulkImportHistoryDto>.Success(new(items, page, pageSize, totalCount, totalPages));
    }

    public async Task<bool> ProcessQueuedOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty || tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty) return false;
        var leaseId = Guid.NewGuid();
        var claim = await persistence.ClaimQueuedOperationAsync(operationId, leaseId, cancellationToken);
        if (!claim.Succeeded || claim.Value is null) return false;
        await ProcessClaimedOperationAsync(claim.Value, leaseId, cancellationToken);
        return true;
    }

    private async Task ProcessClaimedOperationAsync(HRMS.Domain.Entities.EmployeeImportOperation claimed, Guid leaseId, CancellationToken cancellationToken)
    {
        workerIdentity.Activate(claimed.InitiatedByUserId);
        try
        {
            byte[] bytes;
            try
            {
                await using var artifact = await workbookStore.OpenReadAsync(claimed.Id, claimed.WorkbookSha256, cancellationToken);
                if (artifact is null) throw new InvalidDataException("The stored workbook is unavailable.");
                bytes = await ReadBoundedAsync(artifact, cancellationToken)
                    ?? throw new InvalidDataException("The stored workbook is empty or exceeds the configured limit.");
                if (!FixedTextEquals(claimed.WorkbookSha256, Convert.ToHexString(SHA256.HashData(bytes))))
                    throw new InvalidDataException("The stored workbook identity does not match the operation.");
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidDataException)
            {
                await FinalizeUnprocessableOperationAsync(claimed.Id, leaseId, "workbook.unavailable",
                    "The stored workbook could not be verified; no employees were created.", cancellationToken);
                return;
            }

            var current = await persistence.GetAsync(claimed.Id, cancellationToken);
            if (!current.Succeeded) return;
            if (current.Value!.State == EmployeeImportOperationState.Claimed)
            {
                var processing = await persistence.TransitionClaimedAsync(claimed.Id, leaseId, EmployeeImportOperationState.Processing,
                    "Import processing started.", cancellationToken);
                if (!processing.Succeeded) return;
            }

            var rows = await db.EmployeeImportRows.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenant.TenantId && x.OperationId == claimed.Id)
                .OrderBy(x => x.ExcelRowNumber).ToListAsync(cancellationToken);
            var rowsToProcess = rows.Where(x => x.Status is EmployeeImportRowStatus.Pending or EmployeeImportRowStatus.Processing)
                .Select(x => x.ExcelRowNumber).ToHashSet();
            var validationResult = await ValidateAsync(bytes, cancellationToken);
            var currentValidationInvalid = validationResult.Succeeded &&
                (validationResult.Value!.Preview.Rows.Any(x => rowsToProcess.Contains(x.RowNumber) && x.Status == "Invalid") ||
                 validationResult.Value.ValidatedRows.Count(x => rowsToProcess.Contains(x.ExcelRowNumber)) != rowsToProcess.Count);
            if (!validationResult.Succeeded || currentValidationInvalid)
            {
                await FinalizeUnprocessableOperationAsync(claimed.Id, leaseId, "validation.changed",
                    "Current tenant data no longer validates this workbook; no employees were created.", cancellationToken);
                return;
            }

            var currentValidation = validationResult.Value!;
            var rowsByNumber = currentValidation.ValidatedRows.ToDictionary(x => x.ExcelRowNumber);
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row.Status is EmployeeImportRowStatus.Succeeded or EmployeeImportRowStatus.Failed or EmployeeImportRowStatus.Skipped) continue;
                db.ClearChangeTracker();
                var renewed = await persistence.RenewClaimAsync(claimed.Id, leaseId, cancellationToken);
                if (!renewed.Succeeded) return;
                var freshRowStatus = await db.EmployeeImportRows.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.TenantId == tenant.TenantId && x.OperationId == claimed.Id && x.ExcelRowNumber == row.ExcelRowNumber)
                    .Select(x => x.Status).SingleAsync(cancellationToken);
                if (freshRowStatus == EmployeeImportRowStatus.Processing)
                {
                    var recovered = await persistence.RecoverAbandonedClaimedRowAsync(claimed.Id, row.ExcelRowNumber, leaseId, cancellationToken);
                    if (!recovered.Succeeded) return;
                }
                var started = await persistence.StartClaimedRowAsync(claimed.Id, row.ExcelRowNumber, leaseId, cancellationToken);
                if (!started.Succeeded) return;

                Result<EmployeeDto>? creation = null;
                var runtimeFailed = false;
                var ownershipLost = false;
                Exception? onboardingException = null;
                var failureStage = "transaction_begin";
                try
                {
                    await db.CreateExecutionStrategy().ExecuteAsync(async operationToken =>
                    {
                        db.ClearChangeTracker();
                        creation = null;
                        runtimeFailed = false;
                        ownershipLost = false;
                        onboardingException = null;
                        failureStage = "transaction_begin";
                        await using var transaction = await db.BeginTransactionAsync(IsolationLevel.Serializable, operationToken);
                        failureStage = "commit_state_check";
                        var alreadyCommitted = await db.EmployeeImportRows.IgnoreQueryFilters().AsNoTracking()
                            .AnyAsync(x => x.TenantId == tenant.TenantId && x.OperationId == claimed.Id &&
                                           x.ExcelRowNumber == row.ExcelRowNumber && x.Status == EmployeeImportRowStatus.Succeeded,
                                operationToken);
                        if (alreadyCommitted)
                        {
                            await transaction.RollbackAsync(CancellationToken.None);
                            return;
                        }

                        failureStage = "employee_onboarding";
                        creation = await employees.CreateOnboardingWithinTransactionAsync(rowsByNumber[row.ExcelRowNumber].Request,
                            claimed.InitiatedByUserId.ToString("D"), claimed.Id, transaction, operationToken, canEditSensitive: true);
                        if (!creation.Succeeded)
                        {
                            await transaction.RollbackAsync(CancellationToken.None);
                            runtimeFailed = true;
                            return;
                        }
                        failureStage = "claim_renewal";
                        var renewedInTransaction = await persistence.RenewClaimAsync(claimed.Id, leaseId, operationToken);
                        if (!renewedInTransaction.Succeeded)
                        {
                            await transaction.RollbackAsync(CancellationToken.None);
                            ownershipLost = true;
                            return;
                        }
                        failureStage = "row_completion";
                        var completed = await persistence.CompleteRowWithinTransactionAsync(claimed.Id, row.ExcelRowNumber,
                            creation.Value!.Id, creation.Value.EmployeeCode, leaseId, transaction, operationToken);
                        if (!completed.Succeeded)
                        {
                            await transaction.RollbackAsync(CancellationToken.None);
                            ownershipLost = true;
                            return;
                        }
                        failureStage = "transaction_commit";
                        await transaction.CommitAsync(operationToken);
                    }, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    onboardingException = exception;
                    runtimeFailed = true;
                }

                if (ownershipLost) return;
                if (runtimeFailed)
                {
                    var errorCategory = onboardingException is null
                        ? $"result_{creation?.Status.ToString().ToLowerInvariant() ?? "unknown"}"
                        : GetSafeErrorCategory(onboardingException);
                    var failureStatus = onboardingException is null
                        ? creation?.Status.ToString() ?? "Unknown"
                        : "Exception";
                    var failureFields = GetSafeFailureFields(creation?.Errors);
                    logger?.LogError(
                        "Employee import row onboarding failed. Operation {OperationId}, Excel row {ExcelRowNumber}, stage {FailureStage}, failure status {FailureStatus}, failure fields {FailureFields}, exception type {ExceptionType}, error category {ErrorCategory}.",
                        claimed.Id,
                        row.ExcelRowNumber,
                        failureStage,
                        failureStatus,
                        failureFields,
                        onboardingException?.GetType().Name ?? "None",
                        errorCategory);
                    db.ClearChangeTracker();
                    var failed = await persistence.FailClaimedRowAsync(claimed.Id, row.ExcelRowNumber, leaseId,
                        "employee.create_failed", "Employee onboarding failed and was rolled back. Correct the row or current tenant data before retrying.",
                        CancellationToken.None);
                    if (!failed.Succeeded) return;
                    continue;
                }
            }

            await FinalizeFromRowOutcomesAsync(claimed.Id, leaseId, cancellationToken);
        }
        finally
        {
            workerIdentity.Deactivate();
        }
    }

    private static string GetSafeErrorCategory(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => "database_concurrency",
        DbUpdateException => "database_update",
        TimeoutException => "timeout",
        InvalidDataException => "invalid_data",
        ArgumentException => "invalid_argument",
        InvalidOperationException => "invalid_operation",
        _ => "unhandled_exception"
    };

    private static string GetSafeFailureFields(IReadOnlyList<ValidationError>? errors)
    {
        if (errors is null || errors.Count == 0) return "none";
        var fields = errors
            .Select(error => error.Field)
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Select(field => new string(field.Where(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.')
                .Take(64).ToArray()))
            .Where(field => field.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(field => field, StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        return fields.Length == 0 ? "none" : string.Join(',', fields);
    }

    private async Task FinalizeUnprocessableOperationAsync(Guid operationId, Guid leaseId, string errorCode, string message, CancellationToken cancellationToken)
    {
        var current = await persistence.GetAsync(operationId, cancellationToken);
        if (!current.Succeeded) return;
        if (current.Value!.State == EmployeeImportOperationState.Claimed)
        {
            var processing = await persistence.TransitionClaimedAsync(operationId, leaseId, EmployeeImportOperationState.Processing, cancellationToken: cancellationToken);
            if (!processing.Succeeded) return;
        }
        var pending = await db.EmployeeImportRows.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant.TenantId && x.OperationId == operationId &&
                        (x.Status == EmployeeImportRowStatus.Pending || x.Status == EmployeeImportRowStatus.Processing))
            .OrderBy(x => x.ExcelRowNumber).Select(x => new { x.ExcelRowNumber, x.Status }).ToListAsync(cancellationToken);
        foreach (var row in pending)
        {
            if (row.Status == EmployeeImportRowStatus.Processing)
                await persistence.RecoverAbandonedClaimedRowAsync(operationId, row.ExcelRowNumber, leaseId, cancellationToken);
            await persistence.SkipClaimedRowAsync(operationId, row.ExcelRowNumber, leaseId, errorCode, message, cancellationToken);
        }
        await persistence.TransitionClaimedAsync(operationId, leaseId, EmployeeImportOperationState.Failed, message, cancellationToken);
    }

    private async Task FinalizeFromRowOutcomesAsync(Guid operationId, Guid leaseId, CancellationToken cancellationToken)
    {
        var finalRows = await db.EmployeeImportRows.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant.TenantId && x.OperationId == operationId)
            .OrderBy(x => x.ExcelRowNumber).ToListAsync(cancellationToken);
        var current = await persistence.GetAsync(operationId, cancellationToken);
        if (!current.Succeeded || current.Value!.State != EmployeeImportOperationState.Processing) return;
        var succeeded = finalRows.Count(x => x.Status == EmployeeImportRowStatus.Succeeded);
        var failed = finalRows.Count(x => x.Status == EmployeeImportRowStatus.Failed);
        var skipped = finalRows.Count(x => x.Status == EmployeeImportRowStatus.Skipped);
        var state = succeeded == finalRows.Count ? EmployeeImportOperationState.Completed
            : succeeded > 0 ? EmployeeImportOperationState.PartiallyCompleted : EmployeeImportOperationState.Failed;
        await persistence.TransitionClaimedAsync(operationId, leaseId, state, "Import processing finished.", cancellationToken);
    }

    private async Task<Result<EmployeeBulkUploadValidationResult>> ValidateAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(bytes, writable: false);
        return await validation.ValidateForExecutionAsync(stream, cancellationToken);
    }

    private async Task MarkPreExecutionFailureAsync(Guid operationId, EmployeeBulkUploadValidationResult? validationResult, CancellationToken ct)
    {
        var status = await persistence.GetAsync(operationId, ct);
        if (!status.Succeeded) return;
        if (status.Value!.State == EmployeeImportOperationState.Claimed)
            await persistence.TransitionAsync(operationId, EmployeeImportOperationState.Processing, cancellationToken: ct);
        var pending = await db.EmployeeImportRows.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenant.TenantId && x.OperationId == operationId &&
                        (x.Status == EmployeeImportRowStatus.Pending || x.Status == EmployeeImportRowStatus.Processing))
            .OrderBy(x => x.ExcelRowNumber).Select(x => x.ExcelRowNumber).ToListAsync(ct);
        foreach (var row in pending)
        {
            var rowState = await db.EmployeeImportRows.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenant.TenantId && x.OperationId == operationId && x.ExcelRowNumber == row)
                .Select(x => x.Status).SingleAsync(ct);
            if (rowState == EmployeeImportRowStatus.Processing)
                await persistence.RecoverAbandonedRowAsync(operationId, row, ct);
            await persistence.SkipRowAsync(operationId, row, "validation.changed", "Current tenant data or workbook validation changed; no employee was created for this row.", ct);
        }
        await persistence.TransitionAsync(operationId, EmployeeImportOperationState.Failed,
            validationResult is null ? "Revalidation failed." : "Workbook is no longer valid.", ct);
    }

    private async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (!stream.CanRead) return null;
        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + read > MaximumWorkbookBytes) return null;
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return memory.Length == 0 ? null : memory.ToArray();
    }

    private bool HasTenantUser() => tenant.TenantId is Guid tenantId && tenantId != Guid.Empty && tenant.UserId is Guid userId && userId != Guid.Empty;

    private async Task TryReleasePreparationLeaseAsync(Guid operationId, Guid leaseId)
    {
        try { await persistence.ReleasePreparationLeaseAsync(operationId, leaseId, CancellationToken.None); }
        catch (Exception) { }
    }

    private static bool FixedTextEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(left.ToUpperInvariant()), Encoding.ASCII.GetBytes(right.ToUpperInvariant()));
}
