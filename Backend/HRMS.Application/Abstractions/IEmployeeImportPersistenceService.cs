using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRMS.Application.Abstractions;

/// <summary>Durable lifecycle and sanitized row-outcome persistence; does not parse files or execute imports.</summary>
public interface IEmployeeImportPersistenceService
{
    Task<Result<EmployeeImportOperation>> BeginPreparationAsync(string workbookSha256, string confirmationTokenHash, int totalRows, Guid preparationLeaseId, string? fileName = null, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportOperation>> ResumePreparationAsync(string workbookSha256, string confirmationTokenHash, Guid preparationLeaseId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportOperation>> CompletePreparationAsync(Guid operationId, Guid preparationLeaseId, string? sanitizedSummary = null, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportOperation>> ReleasePreparationLeaseAsync(Guid operationId, Guid preparationLeaseId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportOperation>> GetAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportOperation>> EnqueueAsync(Guid operationId, string confirmationTokenHash, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportOperation?>> ClaimQueuedOperationAsync(Guid operationId, Guid leaseId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportOperation>> ClaimAsync(Guid operationId, string confirmationTokenHash, Guid leaseId = default, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportOperation>> RenewClaimAsync(Guid operationId, Guid leaseId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportOperation>> TransitionAsync(Guid operationId, EmployeeImportOperationState next, string? sanitizedSummary = null, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportOperation>> TransitionClaimedAsync(Guid operationId, Guid leaseId, EmployeeImportOperationState next, string? sanitizedSummary = null, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportRow>> StartRowAsync(Guid operationId, int excelRowNumber, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportRow>> StartClaimedRowAsync(Guid operationId, int excelRowNumber, Guid leaseId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportRow>> RecoverAbandonedRowAsync(Guid operationId, int excelRowNumber, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportRow>> RecoverAbandonedClaimedRowAsync(Guid operationId, int excelRowNumber, Guid leaseId, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportRow>> FailRowAsync(Guid operationId, int excelRowNumber, string errorCode, string sanitizedMessage, string? employeeCode = null, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportRow>> SkipRowAsync(Guid operationId, int excelRowNumber, string errorCode, string sanitizedMessage, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportRow>> FailClaimedRowAsync(Guid operationId, int excelRowNumber, Guid leaseId, string errorCode, string sanitizedMessage, string? employeeCode = null, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportRow>> SkipClaimedRowAsync(Guid operationId, int excelRowNumber, Guid leaseId, string errorCode, string sanitizedMessage, CancellationToken cancellationToken = default);
    Task<Result<EmployeeImportRow>> CompleteRowWithinTransactionAsync(Guid operationId, int excelRowNumber, Guid employeeId, string? employeeCode, Guid leaseId, IDbContextTransaction callerTransaction, CancellationToken cancellationToken = default);
}
