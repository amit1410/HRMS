namespace HRMS.Infrastructure.Storage;

/// <summary>Minimal object-store surface used by the encrypted workbook provider and mockable in tests.</summary>
public interface IEmployeeImportObjectClient
{
    Task PutIfAbsentAsync(string bucket, string key, byte[] content, CancellationToken cancellationToken);
    Task<byte[]?> GetAsync(string bucket, string key, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string bucket, string key, CancellationToken cancellationToken);
    Task DeleteAsync(string bucket, string key, CancellationToken cancellationToken);
}
