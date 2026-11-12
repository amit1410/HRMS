using System.Security.Cryptography;
using HRMS.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HRMS.Infrastructure.Storage;

/// <summary>Encrypted tenant-scoped workbook artifacts in a private S3-compatible bucket.</summary>
public sealed class S3EmployeeImportWorkbookStore(
    IConfiguration configuration,
    IHostEnvironment environment,
    ITenantContext tenant,
    TimeProvider clock,
    IEmployeeImportWorkbookCipher cipher,
    IEmployeeImportObjectClient objects) : IEmployeeImportWorkbookStore
{
    private const int MaximumWorkbookBytes = 10 * 1024 * 1024;

    public async Task SaveAsync(Guid operationId, Stream workbook, string expectedSha256, CancellationToken cancellationToken = default)
    {
        var (tenantId, options) = Resolve(operationId);
        ValidateHash(expectedSha256);
        if (!workbook.CanRead) throw new ArgumentException("Workbook stream is not readable.", nameof(workbook));
        var plaintext = await ReadBoundedAsync(workbook, cancellationToken);
        var encrypted = Array.Empty<byte>();
        try
        {
            if (!HashEquals(expectedSha256, SHA256.HashData(plaintext)))
                throw new InvalidDataException("Workbook hash does not match the expected identity.");
            encrypted = cipher.Encrypt(tenantId, operationId, plaintext);
            await objects.PutIfAbsentAsync(options.Bucket!, ObjectKey(tenantId, operationId), encrypted, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    public async Task<Stream?> OpenReadAsync(Guid operationId, string expectedSha256, CancellationToken cancellationToken = default)
    {
        var (tenantId, options) = Resolve(operationId);
        ValidateHash(expectedSha256);
        var encrypted = await objects.GetAsync(options.Bucket!, ObjectKey(tenantId, operationId), cancellationToken);
        if (encrypted is null) return null;
        byte[] plaintext;
        try { plaintext = cipher.Decrypt(tenantId, operationId, encrypted); }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
        if (plaintext.Length > MaximumWorkbookBytes || !HashEquals(expectedSha256, SHA256.HashData(plaintext)))
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidDataException("Stored workbook hash does not match the operation identity.");
        }
        return new ZeroingMemoryStream(plaintext);
    }

    public Task<bool> IsAvailableAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var (tenantId, options) = Resolve(operationId);
        return objects.ExistsAsync(options.Bucket!, ObjectKey(tenantId, operationId), cancellationToken);
    }

    public async Task<bool> DeleteExpiredAsync(Guid operationId, DateTime completedAtUtc, CancellationToken cancellationToken = default)
    {
        var (tenantId, options) = Resolve(operationId);
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-options.RetentionDays);
        if (DateTime.SpecifyKind(completedAtUtc, DateTimeKind.Utc) > cutoff) return false;
        var key = ObjectKey(tenantId, operationId);
        if (!await objects.ExistsAsync(options.Bucket!, key, cancellationToken)) return false;
        await objects.DeleteAsync(options.Bucket!, key, cancellationToken);
        return true;
    }

    private (Guid TenantId, EmployeeImportStorageOptions Options) Resolve(Guid operationId)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("A valid import operation ID is required.", nameof(operationId));
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty)
            throw new InvalidOperationException("Authenticated tenant context is required for workbook storage.");
        var options = EmployeeImportStorageOptions.LoadAndValidate(configuration, environment);
        if (options.Provider != EmployeeImportStorageProvider.S3)
            throw new InvalidOperationException("Employee import storage provider is not configured for S3.");
        return (tenantId, options);
    }

    private static string ObjectKey(Guid tenantId, Guid operationId) =>
        $"employee-import-workbooks/{tenantId:N}/{operationId:N}.hrw";

    private static async Task<byte[]> ReadBoundedAsync(Stream input, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + count > MaximumWorkbookBytes) throw new InvalidDataException("Workbook exceeds the 10 MB storage limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        if (output.Length == 0) throw new InvalidDataException("Workbook is empty.");
        return output.ToArray();
    }

    private static void ValidateHash(string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new ArgumentException("Expected workbook identity must be a SHA-256 hex value.", nameof(hash));
    }

    private static bool HashEquals(string expectedHex, byte[] actualHash) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expectedHex), actualHash);

    private sealed class ZeroingMemoryStream(byte[] content) : MemoryStream(content, writable: false)
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing && TryGetBuffer(out var segment) && segment.Array is not null)
                CryptographicOperations.ZeroMemory(segment.Array.AsSpan(segment.Offset, segment.Count));
            base.Dispose(disposing);
        }
    }
}
