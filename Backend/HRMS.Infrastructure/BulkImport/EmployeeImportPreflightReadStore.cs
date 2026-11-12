using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Infrastructure.BulkImport;

/// <summary>Read-only, explicit-tenant projections used only by the local preflight verifier.</summary>
public sealed class EmployeeImportPreflightReadStore(IHrmsDbContext db) : IEmployeeImportPreflightReadStore
{
    public Task<EmployeeImportPreflightOperationSnapshot?> FindOperationAsync(
        Guid tenantId,
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        db.EmployeeImportOperations.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == operationId)
            .Select(x => new EmployeeImportPreflightOperationSnapshot(
                x.TenantId, x.Id, x.WorkbookSha256, x.State, x.ConfirmedAtUtc, x.TotalRows,
                x.SucceededRows, x.FailedRows, x.SkippedRows, x.ClaimLeaseId, x.ClaimLeaseExpiresAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<EmployeeImportPreflightRowCounts> CountRowsAsync(
        Guid tenantId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var groups = await db.EmployeeImportRows.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.OperationId == operationId)
            .GroupBy(x => x.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        int Count(EmployeeImportRowStatus status) => groups.FirstOrDefault(group => group.Status == status)?.Count ?? 0;
        return new(Count(EmployeeImportRowStatus.Pending), Count(EmployeeImportRowStatus.Processing),
            Count(EmployeeImportRowStatus.Succeeded), Count(EmployeeImportRowStatus.Failed), Count(EmployeeImportRowStatus.Skipped));
    }
}
