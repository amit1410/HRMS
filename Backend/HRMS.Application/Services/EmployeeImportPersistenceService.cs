using System.Text.RegularExpressions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRMS.Application.Services;

/// <summary>Durable employee-import state primitives. This service never accepts or stores workbook rows.</summary>
public sealed class EmployeeImportPersistenceService(
    IHrmsDbContext db,
    ITenantContext tenant,
    TimeProvider clock,
    EmployeeBulkUploadOptions? uploadOptions = null) : IEmployeeImportPersistenceService
{
    private readonly int _maximumEmployeeRows = uploadOptions?.MaximumEmployeeRows ?? EmployeeBulkUploadOptions.MaximumAllowedEmployeeRows;
    private static readonly Regex HexHash = new("\\A[0-9a-fA-F]{64}\\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Aadhaar = new("(?<![0-9])[0-9]{12}(?![0-9])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Pan = new("(?i)(?<![A-Z0-9])[A-Z]{5}[0-9]{4}[A-Z](?![A-Z0-9])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Email = new("[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex LongNumber = new("(?<![A-Z0-9])\\+?[0-9][0-9\\s().-]{6,}[0-9](?![A-Z0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task<Result<EmployeeImportOperation>> BeginPreparationAsync(
        string workbookSha256, string confirmationTokenHash, int totalRows, Guid preparationLeaseId, string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty || tenant.UserId is not Guid userId || userId == Guid.Empty)
            return Result<EmployeeImportOperation>.Unauthorized("An authenticated tenant and user are required.");
        if (!IsHash(workbookSha256) || !IsHash(confirmationTokenHash))
            return Result<EmployeeImportOperation>.Invalid("hash", "Workbook and confirmation token hashes must be SHA-256 hex values.");
        if (totalRows < 1 || totalRows > _maximumEmployeeRows || preparationLeaseId == Guid.Empty)
            return Result<EmployeeImportOperation>.Invalid("totalRows", $"Import row count must be between 1 and {_maximumEmployeeRows:N0}.");

        var now = clock.GetUtcNow().UtcDateTime;
        var operationId = Guid.NewGuid();
        var batch = new ImportBatch
        {
            Id = operationId,
            TenantId = tenantId,
            FileName = NormalizeFileName(fileName),
            ImportedBy = userId.ToString("D"),
            TotalRows = totalRows,
            Status = EmployeeImportOperationState.ArtifactPending.ToString(),
            StartedAtUtc = null,
            Message = null
        };
        var operation = new EmployeeImportOperation
        {
            Id = operationId,
            ImportBatchId = batch.Id,
            TenantId = tenantId,
            WorkbookSha256 = workbookSha256.ToLowerInvariant(),
            ConfirmationTokenHash = confirmationTokenHash.ToLowerInvariant(),
            InitiatedByUserId = userId,
            PreparedAtUtc = now,
            TotalRows = totalRows,
            Rows = Enumerable.Range(2, totalRows).Select(row => new EmployeeImportRow
            {
                Id = Guid.NewGuid(), TenantId = tenantId, OperationId = operationId, ExcelRowNumber = row
            }).ToList()
        };
        if (!operation.BeginArtifactPreparation(preparationLeaseId, now, TimeSpan.FromMinutes(10)))
            return Result<EmployeeImportOperation>.Conflict("Import artifact preparation could not be started.");
        db.ImportBatches.Add(batch);
        db.EmployeeImportOperations.Add(operation);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result<EmployeeImportOperation>.Success(operation, "Employee import artifact preparation reserved.");
        }
        catch (DbUpdateException)
        {
            db.ClearChangeTracker();
            return Result<EmployeeImportOperation>.Conflict("An import operation already exists for this workbook or confirmation claim.");
        }
    }

    public async Task<Result<EmployeeImportOperation>> ResumePreparationAsync(
        string workbookSha256, string confirmationTokenHash, Guid preparationLeaseId, CancellationToken cancellationToken = default)
    {
        if (!TryTenant(out var tenantId) || tenant.UserId is not Guid userId || userId == Guid.Empty)
            return Result<EmployeeImportOperation>.Unauthorized("An authenticated tenant and user are required.");
        if (!IsHash(workbookSha256) || !IsHash(confirmationTokenHash) || preparationLeaseId == Guid.Empty)
            return Result<EmployeeImportOperation>.Invalid("preparation", "Workbook identity or preparation claim is invalid.");
        var operation = await db.EmployeeImportOperations.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.WorkbookSha256 == workbookSha256.ToLowerInvariant(), cancellationToken);
        if (operation is null) return Result<EmployeeImportOperation>.NotFound("An interrupted preparation was not found.");
        if (operation.InitiatedByUserId != userId ||
            !operation.AcquireArtifactPreparationLease(preparationLeaseId, confirmationTokenHash.ToLowerInvariant(),
                clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(10)))
            return Result<EmployeeImportOperation>.Conflict(
                "This workbook already has an active or accepted import operation.",
                [new ValidationError("existingOperationId", operation.Id.ToString("D"))]);
        return await SaveTransitionAsync(operation, operation.State.ToString(), cancellationToken);
    }

    public async Task<Result<EmployeeImportOperation>> CompletePreparationAsync(
        Guid operationId, Guid preparationLeaseId, string? sanitizedSummary = null, CancellationToken cancellationToken = default)
    {
        if (!TryTenant(out var tenantId)) return Result<EmployeeImportOperation>.Unauthorized("No authenticated tenant.");
        var operation = await db.EmployeeImportOperations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == operationId, cancellationToken);
        if (operation is null) return Result<EmployeeImportOperation>.NotFound("Import operation was not found.");
        if (!operation.CompleteArtifactPreparation(preparationLeaseId, clock.GetUtcNow().UtcDateTime, Sanitize(sanitizedSummary)))
            return Result<EmployeeImportOperation>.Conflict("The artifact preparation claim is no longer owned by this request.");
        return await SaveTransitionAsync(operation, operation.State.ToString(), cancellationToken);
    }

    public async Task<Result<EmployeeImportOperation>> ReleasePreparationLeaseAsync(
        Guid operationId, Guid preparationLeaseId, CancellationToken cancellationToken = default)
    {
        if (!TryTenant(out var tenantId)) return Result<EmployeeImportOperation>.Unauthorized("No authenticated tenant.");
        var operation = await db.EmployeeImportOperations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == operationId, cancellationToken);
        if (operation is null) return Result<EmployeeImportOperation>.NotFound("Import operation was not found.");
        if (!operation.ReleaseArtifactPreparationLease(preparationLeaseId))
            return Result<EmployeeImportOperation>.Conflict("The artifact preparation claim is no longer owned by this request.");
        return await SaveTransitionAsync(operation, operation.State.ToString(), cancellationToken);
    }

    public async Task<Result<EmployeeImportOperation>> GetAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (!TryTenant(out var tenantId)) return Result<EmployeeImportOperation>.Unauthorized("No authenticated tenant.");
        var operation = await db.EmployeeImportOperations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == operationId, cancellationToken);
        return operation is null
            ? Result<EmployeeImportOperation>.NotFound("Import operation was not found.")
            : Result<EmployeeImportOperation>.Success(operation);
    }

    public async Task<Result<EmployeeImportOperation>> EnqueueAsync(
        Guid operationId, string confirmationTokenHash, CancellationToken cancellationToken = default)
    {
        if (!TryTenant(out var tenantId)) return Result<EmployeeImportOperation>.Unauthorized("No authenticated tenant.");
        if (!IsHash(confirmationTokenHash)) return Result<EmployeeImportOperation>.Invalid("confirmation", "Confirmation claim is invalid.");
        var operation = await db.EmployeeImportOperations.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == operationId, cancellationToken);
        if (operation is null) return Result<EmployeeImportOperation>.NotFound("Import operation was not found.");
        if (!operation.Enqueue(confirmationTokenHash, clock.GetUtcNow().UtcDateTime))
            return Result<EmployeeImportOperation>.Conflict("Import operation is already accepted or the confirmation claim is invalid.");
        return await SaveTransitionAsync(operation, EmployeeImportOperationState.Queued.ToString(), cancellationToken);
    }

    public async Task<Result<EmployeeImportOperation?>> ClaimQueuedOperationAsync(
        Guid operationId, Guid leaseId, CancellationToken cancellationToken = default)
    {
        if (!TryTenant(out var tenantId)) return Result<EmployeeImportOperation?>.Unauthorized("No authenticated tenant.");
        if (operationId == Guid.Empty) return Result<EmployeeImportOperation?>.Invalid("operation", "A valid operation ID is required.");
        if (leaseId == Guid.Empty) return Result<EmployeeImportOperation?>.Invalid("lease", "A worker lease is required.");

        var now = clock.GetUtcNow().UtcDateTime;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            db.ClearChangeTracker();
            var operation = await db.EmployeeImportOperations.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && x.Id == operationId &&
                    (x.State == EmployeeImportOperationState.Queued ||
                     ((x.State == EmployeeImportOperationState.Claimed || x.State == EmployeeImportOperationState.Processing) &&
                      x.ClaimLeaseExpiresAtUtc != null && x.ClaimLeaseExpiresAtUtc <= now)))
                .FirstOrDefaultAsync(cancellationToken);
            if (operation is null)
                return Result<EmployeeImportOperation?>.Success(null, "The targeted operation is not available for processing.");
            if (!operation.AcquireWorkerClaim(leaseId, now, TimeSpan.FromMinutes(10))) continue;

            var saved = await SaveTransitionAsync(operation, operation.State.ToString(), cancellationToken);
            if (saved.Succeeded) return Result<EmployeeImportOperation?>.Success(saved.Value);
            if (saved.Status != ResultStatus.Conflict)
                return Result<EmployeeImportOperation?>.Failure(saved.Status, saved.Message, saved.Errors);
        }

        return Result<EmployeeImportOperation?>.Conflict("Targeted operation was concurrently claimed; retry the next worker poll.");
    }

    public async Task<Result<EmployeeImportOperation>> ClaimAsync(Guid operationId, string confirmationTokenHash, Guid leaseId = default, CancellationToken cancellationToken = default)
    {
        if (leaseId == Guid.Empty) leaseId = Guid.NewGuid();
        if (!TryTenant(out var tenantId)) return Result<EmployeeImportOperation>.Unauthorized("No authenticated tenant.");
        if (!IsHash(confirmationTokenHash)) return Result<EmployeeImportOperation>.Invalid("confirmation", "Confirmation claim is invalid.");
        var operation = await db.EmployeeImportOperations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == operationId, cancellationToken);
        if (operation is null) return Result<EmployeeImportOperation>.NotFound("Import operation was not found.");
        if (!FixedHashEquals(operation.ConfirmationTokenHash, confirmationTokenHash) ||
            !operation.AcquireOrRecoverClaim(leaseId, clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(10)))
            return Result<EmployeeImportOperation>.Conflict("Import operation is already claimed or the confirmation claim is invalid.");
        return await SaveTransitionAsync(operation, operation.State.ToString(), cancellationToken);
    }

    public async Task<Result<EmployeeImportOperation>> RenewClaimAsync(Guid operationId, Guid leaseId, CancellationToken cancellationToken = default)
    {
        if (!TryTenant(out var tenantId)) return Result<EmployeeImportOperation>.Unauthorized("No authenticated tenant.");
        var operation = await db.EmployeeImportOperations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == operationId, cancellationToken);
        if (operation is null) return Result<EmployeeImportOperation>.NotFound("Import operation was not found.");
        if (!operation.RenewClaim(leaseId, clock.GetUtcNow().UtcDateTime, TimeSpan.FromMinutes(10)))
            return Result<EmployeeImportOperation>.Conflict("Import claim is no longer owned by this request.");
        return await SaveTransitionAsync(operation, operation.State.ToString(), cancellationToken);
    }

    public async Task<Result<EmployeeImportOperation>> TransitionAsync(Guid operationId, EmployeeImportOperationState next, string? sanitizedSummary = null, CancellationToken cancellationToken = default)
    {
        if (!TryTenant(out var tenantId)) return Result<EmployeeImportOperation>.Unauthorized("No authenticated tenant.");
        var operation = await db.EmployeeImportOperations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == operationId, cancellationToken);
        if (operation is null) return Result<EmployeeImportOperation>.NotFound("Import operation was not found.");
        if (!operation.TransitionTo(next, clock.GetUtcNow().UtcDateTime, Sanitize(sanitizedSummary)))
            return Result<EmployeeImportOperation>.Conflict("The requested import operation state transition is not allowed.");
        return await SaveTransitionAsync(operation, next.ToString(), cancellationToken);
    }

    public async Task<Result<EmployeeImportOperation>> TransitionClaimedAsync(Guid operationId, Guid leaseId,
        EmployeeImportOperationState next, string? sanitizedSummary = null, CancellationToken cancellationToken = default)
    {
        if (!TryTenant(out var tenantId)) return Result<EmployeeImportOperation>.Unauthorized("No authenticated tenant.");
        var operation = await db.EmployeeImportOperations.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.Id == operationId, cancellationToken);
        if (operation is null) return Result<EmployeeImportOperation>.NotFound("Import operation was not found.");
        if (leaseId == Guid.Empty || operation.ClaimLeaseId != leaseId ||
            operation.ClaimLeaseExpiresAtUtc <= clock.GetUtcNow().UtcDateTime ||
            !operation.TransitionTo(next, clock.GetUtcNow().UtcDateTime, Sanitize(sanitizedSummary)))
            return Result<EmployeeImportOperation>.Conflict("The worker lease is no longer owned or the state transition is invalid.");
        return await SaveTransitionAsync(operation, next.ToString(), cancellationToken);
    }

    public async Task<Result<EmployeeImportRow>> StartRowAsync(Guid operationId, int excelRowNumber, CancellationToken cancellationToken = default)
        => await StartRowCoreAsync(operationId, excelRowNumber, null, cancellationToken);

    public async Task<Result<EmployeeImportRow>> StartClaimedRowAsync(Guid operationId, int excelRowNumber, Guid leaseId, CancellationToken cancellationToken = default)
        => await StartRowCoreAsync(operationId, excelRowNumber, leaseId, cancellationToken);

    private async Task<Result<EmployeeImportRow>> StartRowCoreAsync(Guid operationId, int excelRowNumber, Guid? leaseId, CancellationToken cancellationToken)
    {
        var found = await FindRowAsync(operationId, excelRowNumber, cancellationToken);
        if (found.Error is not null) return Result<EmployeeImportRow>.Failure(found.Error.Status, found.Error.Message);
        var (operation, row) = found.Value;
        if (operation.State != EmployeeImportOperationState.Processing || !OwnsLiveClaim(operation, leaseId) || !row.Start(clock.GetUtcNow().UtcDateTime))
            return Result<EmployeeImportRow>.Conflict("Import row cannot be started from its current state.");
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result<EmployeeImportRow>.Success(row);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ClearChangeTracker();
            return Result<EmployeeImportRow>.Conflict("Import row was concurrently claimed.");
        }
    }

    public async Task<Result<EmployeeImportRow>> RecoverAbandonedRowAsync(Guid operationId, int excelRowNumber, CancellationToken cancellationToken = default)
        => await RecoverAbandonedRowCoreAsync(operationId, excelRowNumber, null, cancellationToken);

    public async Task<Result<EmployeeImportRow>> RecoverAbandonedClaimedRowAsync(Guid operationId, int excelRowNumber, Guid leaseId, CancellationToken cancellationToken = default)
        => await RecoverAbandonedRowCoreAsync(operationId, excelRowNumber, leaseId, cancellationToken);

    private async Task<Result<EmployeeImportRow>> RecoverAbandonedRowCoreAsync(Guid operationId, int excelRowNumber, Guid? leaseId, CancellationToken cancellationToken)
    {
        var found = await FindRowAsync(operationId, excelRowNumber, cancellationToken);
        if (found.Error is not null) return Result<EmployeeImportRow>.Failure(found.Error.Status, found.Error.Message);
        var (operation, row) = found.Value;
        if (operation.State != EmployeeImportOperationState.Processing || !OwnsLiveClaim(operation, leaseId) || !row.ResetAbandonedProcessing())
            return Result<EmployeeImportRow>.Conflict("Only an uncommitted in-flight row can be recovered.");
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result<EmployeeImportRow>.Success(row, "The abandoned row is ready for revalidation and retry.");
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ClearChangeTracker();
            return Result<EmployeeImportRow>.Conflict("Import row changed while recovery was being applied.");
        }
    }

    public Task<Result<EmployeeImportRow>> FailRowAsync(Guid operationId, int excelRowNumber, string errorCode, string sanitizedMessage, string? employeeCode = null, CancellationToken cancellationToken = default) =>
        FinishRowAsync(operationId, excelRowNumber, EmployeeImportRowStatus.Failed, errorCode, sanitizedMessage, null, employeeCode, cancellationToken);

    public Task<Result<EmployeeImportRow>> FailClaimedRowAsync(Guid operationId, int excelRowNumber, Guid leaseId, string errorCode, string sanitizedMessage, string? employeeCode = null, CancellationToken cancellationToken = default) =>
        FinishRowAsync(operationId, excelRowNumber, EmployeeImportRowStatus.Failed, errorCode, sanitizedMessage, leaseId, employeeCode, cancellationToken);

    public Task<Result<EmployeeImportRow>> SkipRowAsync(Guid operationId, int excelRowNumber, string errorCode, string sanitizedMessage, CancellationToken cancellationToken = default) =>
        FinishRowAsync(operationId, excelRowNumber, EmployeeImportRowStatus.Skipped, errorCode, sanitizedMessage, null, null, cancellationToken);

    public Task<Result<EmployeeImportRow>> SkipClaimedRowAsync(Guid operationId, int excelRowNumber, Guid leaseId, string errorCode, string sanitizedMessage, CancellationToken cancellationToken = default) =>
        FinishRowAsync(operationId, excelRowNumber, EmployeeImportRowStatus.Skipped, errorCode, sanitizedMessage, leaseId, null, cancellationToken);

    public async Task<Result<EmployeeImportRow>> CompleteRowWithinTransactionAsync(
        Guid operationId, int excelRowNumber, Guid employeeId, string? employeeCode, Guid leaseId,
        IDbContextTransaction callerTransaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callerTransaction);
        if (!IsCallerTransactionActive(callerTransaction)) return Result<EmployeeImportRow>.Conflict("The supplied import transaction is not active on this DbContext.");
        var found = await FindRowAsync(operationId, excelRowNumber, cancellationToken);
        if (found.Error is not null) return Result<EmployeeImportRow>.Failure(found.Error.Status, found.Error.Message);
        var (operation, row) = found.Value;
        if (leaseId == Guid.Empty || operation.ClaimLeaseId != leaseId || operation.ClaimLeaseExpiresAtUtc <= clock.GetUtcNow().UtcDateTime)
            return Result<EmployeeImportRow>.Conflict("Import claim is no longer owned by this request.");
        var actualCode = await db.Employees.AsNoTracking()
            .Where(x => x.TenantId == operation.TenantId && x.Id == employeeId)
            .Select(x => x.EmployeeCode)
            .FirstOrDefaultAsync(cancellationToken);
        if (actualCode is null || !string.Equals(actualCode, employeeCode, StringComparison.Ordinal))
            return Result<EmployeeImportRow>.Conflict("Created employee reference is unavailable or inconsistent in this tenant.");
        if (operation.State != EmployeeImportOperationState.Processing || !row.Complete(employeeId, NormalizeEmployeeCode(employeeCode), clock.GetUtcNow().UtcDateTime))
            return Result<EmployeeImportRow>.Conflict("Import row cannot be completed from its current state.");
        operation.RecordRow(EmployeeImportRowStatus.Succeeded);
        var batch = await db.ImportBatches.FirstOrDefaultAsync(x => x.Id == operation.ImportBatchId && x.TenantId == operation.TenantId, cancellationToken);
        if (batch is null) return Result<EmployeeImportRow>.Conflict("Import batch is unavailable.");
        batch.SuccessfulRows = operation.SucceededRows;
        batch.Status = operation.State.ToString();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result<EmployeeImportRow>.Success(row);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ClearChangeTracker();
            return Result<EmployeeImportRow>.Conflict("Import row was concurrently completed.");
        }
    }

    private async Task<Result<EmployeeImportRow>> FinishRowAsync(
        Guid operationId, int excelRowNumber, EmployeeImportRowStatus status, string errorCode, string sanitizedMessage, Guid? leaseId, string? employeeCode,
        CancellationToken cancellationToken)
    {
        var found = await FindRowAsync(operationId, excelRowNumber, cancellationToken);
        if (found.Error is not null) return Result<EmployeeImportRow>.Failure(found.Error.Status, found.Error.Message);
        var (operation, row) = found.Value;
        var safeCode = NormalizeErrorCode(errorCode);
        var safeMessage = Sanitize(sanitizedMessage) ?? "The row could not be processed.";
        var changed = status == EmployeeImportRowStatus.Failed
            ? row.Fail(safeCode, safeMessage, clock.GetUtcNow().UtcDateTime, NormalizeEmployeeCode(employeeCode))
            : row.Skip(safeCode, safeMessage, clock.GetUtcNow().UtcDateTime);
        if (operation.State != EmployeeImportOperationState.Processing || !OwnsLiveClaim(operation, leaseId) || !changed)
            return Result<EmployeeImportRow>.Conflict("Import row cannot be finalized from its current state.");
        operation.RecordRow(status);
        var batch = await db.ImportBatches.FirstOrDefaultAsync(x => x.Id == operation.ImportBatchId && x.TenantId == operation.TenantId, cancellationToken);
        if (batch is not null)
        {
            batch.FailedRows = operation.FailedRows;
            batch.SkippedRows = operation.SkippedRows;
            batch.Status = operation.State.ToString();
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result<EmployeeImportRow>.Success(row);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ClearChangeTracker();
            return Result<EmployeeImportRow>.Conflict("Import row was concurrently finalized.");
        }
    }

    private async Task<Result<EmployeeImportOperation>> SaveTransitionAsync(EmployeeImportOperation operation, string batchStatus, CancellationToken cancellationToken)
    {
        var batch = await db.ImportBatches.FirstOrDefaultAsync(x => x.Id == operation.ImportBatchId && x.TenantId == operation.TenantId, cancellationToken);
        if (batch is null) return Result<EmployeeImportOperation>.Conflict("Import batch is unavailable.");
        batch.Status = batchStatus;
        batch.StartedAtUtc = operation.StartedAtUtc;
        batch.CompletedAtUtc = operation.CompletedAtUtc;
        batch.TotalRows = operation.TotalRows;
        batch.SuccessfulRows = operation.SucceededRows;
        batch.FailedRows = operation.FailedRows;
        batch.SkippedRows = operation.SkippedRows;
        batch.Message = operation.SanitizedSummary;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result<EmployeeImportOperation>.Success(operation);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ClearChangeTracker();
            return Result<EmployeeImportOperation>.Conflict("Import operation was concurrently updated.");
        }
    }

    private async Task<(Result<EmployeeImportOperation>? Error, (EmployeeImportOperation Operation, EmployeeImportRow Row) Value)> FindRowAsync(Guid operationId, int rowNumber, CancellationToken cancellationToken)
    {
        if (!TryTenant(out var tenantId)) return (Result<EmployeeImportOperation>.Unauthorized("No authenticated tenant."), default);
        var operation = await db.EmployeeImportOperations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == operationId, cancellationToken);
        if (operation is null) return (Result<EmployeeImportOperation>.NotFound("Import operation was not found."), default);
        var row = await db.EmployeeImportRows.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.OperationId == operationId && x.ExcelRowNumber == rowNumber, cancellationToken);
        return row is null
            ? (Result<EmployeeImportOperation>.NotFound("Import row was not found."), default)
            : (null, (operation, row));
    }

    private bool IsCallerTransactionActive(IDbContextTransaction caller)
    {
        var active = db.CurrentTransaction;
        return active is not null && ReferenceEquals(active.GetDbTransaction(), caller.GetDbTransaction());
    }

    private bool TryTenant(out Guid tenantId)
    {
        tenantId = tenant.TenantId.GetValueOrDefault();
        return tenantId != Guid.Empty;
    }

    private bool OwnsLiveClaim(EmployeeImportOperation operation, Guid? leaseId) =>
        leaseId is null || (leaseId != Guid.Empty && operation.ClaimLeaseId == leaseId &&
                             operation.ClaimLeaseExpiresAtUtc > clock.GetUtcNow().UtcDateTime);

    private static bool IsHash(string? value) => value is not null && HexHash.IsMatch(value);
    private static bool FixedHashEquals(string expected, string supplied)
    {
        if (!IsHash(supplied)) return false;
        try
        {
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected), Convert.FromHexString(supplied));
        }
        catch (FormatException) { return false; }
    }
    private static string? NormalizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var safeName = Path.GetFileName(value.Trim());
        return safeName.Length <= 500 ? safeName : safeName[..500];
    }
    private static string? NormalizeEmployeeCode(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 100)];
    private static string NormalizeErrorCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "import.row_failed";
        var code = Regex.Replace(value.Trim(), "[^A-Za-z0-9._-]", "_");
        return code.Length <= 100 ? code : code[..100];
    }
    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var safe = Email.Replace(Pan.Replace(LongNumber.Replace(Aadhaar.Replace(value, "[masked]"), "[masked]"), "[masked]"), "[masked]");
        safe = Regex.Replace(safe, "[\\r\\n\\t]+", " ").Trim();
        return safe.Length <= 500 ? safe : safe[..500];
    }
}
