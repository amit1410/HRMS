using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRMS.Application.Abstractions;

/// <summary>
/// Centralized, tenant-scoped Job Status mutation boundary. Phase 1 exposes this boundary to workflows
/// without changing any existing controller or production write path.
/// </summary>
public interface IJobStatusService
{
    /// <summary>
    /// Initializes the Job Status on a new, not-yet-persisted employee. This method only mutates the
    /// supplied entity and queues an initial audit when a non-blank value was supplied; the caller owns
    /// the transaction and SaveChanges boundary.
    /// </summary>
    Task<Result<string?>> InitializeForCreationAsync(
        Employee employee,
        string? jobStatus,
        string? reason = null,
        CancellationToken cancellationToken = default);

    Task<Result<JobStatusChangeResult>> ChangeAsync(
        JobStatusChangeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a Job Status change inside a transaction owned by the caller. The supplied transaction must
    /// belong to the same DbContext used by this service; this method never begins, commits, or rolls it back.
    /// </summary>
    Task<Result<JobStatusChangeResult>> ChangeWithinTransactionAsync(
        JobStatusChangeRequest request,
        IDbContextTransaction callerTransaction,
        CancellationToken cancellationToken = default);
}
