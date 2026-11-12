namespace HRMS.Application.Abstractions;

/// <summary>Private tenant-scoped storage for encrypted employee-import workbooks.</summary>
public interface IEmployeeImportWorkbookReader
{
    Task<Stream?> OpenReadAsync(Guid operationId, string expectedSha256, CancellationToken cancellationToken = default);
}

/// <summary>Write-capable storage contract used by the import preparation/retention workflows.</summary>
public interface IEmployeeImportWorkbookStore : IEmployeeImportWorkbookReader
{
    Task SaveAsync(Guid operationId, Stream workbook, string expectedSha256, CancellationToken cancellationToken = default);
    Task<bool> IsAvailableAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<bool> DeleteExpiredAsync(Guid operationId, DateTime completedAtUtc, CancellationToken cancellationToken = default);
}
