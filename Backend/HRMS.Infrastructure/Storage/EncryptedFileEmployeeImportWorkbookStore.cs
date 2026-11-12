using System.Security.Cryptography;
using HRMS.Application.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HRMS.Infrastructure.Storage;

/// <summary>
/// Private local-development store. Files are AES-256-GCM authenticated and use opaque IDs for paths.
/// Configure a durable root and externally managed key ring before this service is used.
/// </summary>
public sealed class EncryptedFileEmployeeImportWorkbookStore(
    IConfiguration configuration,
    IHostEnvironment environment,
    ITenantContext tenant,
    TimeProvider clock,
    IEmployeeImportWorkbookCipher cipher) : IEmployeeImportWorkbookStore
{
    private const int MaximumWorkbookBytes = 10 * 1024 * 1024;

    public async Task SaveAsync(Guid operationId, Stream workbook, string expectedSha256, CancellationToken cancellationToken = default)
    {
        var scope = ResolveScope(operationId);
        ValidateHash(expectedSha256);
        if (!workbook.CanRead) throw new ArgumentException("Workbook stream is not readable.", nameof(workbook));
        var plain = await ReadBoundedAsync(workbook, cancellationToken);
        var actualHash = SHA256.HashData(plain);
        if (!FixedHashEquals(expectedSha256, actualHash))
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new InvalidDataException("Workbook hash does not match the expected identity.");
        }

        var encrypted = Array.Empty<byte>();
        try
        {
            encrypted = cipher.Encrypt(scope.TenantId, operationId, plain);
            var directory = TenantDirectory(scope.RootPath, scope.TenantId);
            Directory.CreateDirectory(directory);
            var destination = ArtifactPath(directory, operationId);
            var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await output.WriteAsync(encrypted, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                }
                File.Move(temporary, destination, overwrite: false);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    public async Task<Stream?> OpenReadAsync(Guid operationId, string expectedSha256, CancellationToken cancellationToken = default)
    {
        var scope = ResolveScope(operationId);
        ValidateHash(expectedSha256);
        var path = ArtifactPath(TenantDirectory(scope.RootPath, scope.TenantId), operationId);
        if (!File.Exists(path)) return null;
        var encryptedLength = new FileInfo(path).Length;
        if (encryptedLength > MaximumWorkbookBytes + 128) throw new InvalidDataException("Stored workbook artifact exceeds the configured size limit.");
        var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
        byte[] plaintext;
        try
        {
            plaintext = cipher.Decrypt(scope.TenantId, operationId, encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
        }

        if (plaintext.Length > MaximumWorkbookBytes || !FixedHashEquals(expectedSha256, SHA256.HashData(plaintext)))
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidDataException("Stored workbook hash does not match the operation identity.");
        }
        return new ZeroingMemoryStream(plaintext);
    }

    public Task<bool> IsAvailableAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ResolveScope(operationId);
        return Task.FromResult(File.Exists(ArtifactPath(TenantDirectory(scope.RootPath, scope.TenantId), operationId)));
    }

    public Task<bool> DeleteExpiredAsync(Guid operationId, DateTime completedAtUtc, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = ResolveScope(operationId);
        var retentionDays = configuration.GetValue<int?>("EmployeeImportStorage:RetentionDays");
        if (retentionDays is null or < 1) throw new InvalidOperationException("EmployeeImportStorage:RetentionDays must be configured as a positive value.");
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-retentionDays.Value);
        if (DateTime.SpecifyKind(completedAtUtc, DateTimeKind.Utc) > cutoff) return Task.FromResult(false);
        var path = ArtifactPath(TenantDirectory(scope.RootPath, scope.TenantId), operationId);
        if (!File.Exists(path)) return Task.FromResult(false);

        // Best-effort overwrite before unlink. Filesystem/SSD snapshots may retain physical blocks;
        // production retention must use provider-native object deletion and lifecycle controls.
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            var zeros = new byte[64 * 1024];
            while (file.Position < file.Length)
                file.Write(zeros, 0, (int)Math.Min(zeros.Length, file.Length - file.Position));
            file.Flush(flushToDisk: true);
        }
        File.Delete(path);
        return Task.FromResult(true);
    }

    private (Guid TenantId, string RootPath) ResolveScope(Guid operationId)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("A valid import operation ID is required.", nameof(operationId));
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty)
            throw new InvalidOperationException("Authenticated tenant context is required for workbook storage.");
        var root = configuration["EmployeeImportStorage:RootPath"];
        if (string.IsNullOrWhiteSpace(root)) throw new InvalidOperationException("EmployeeImportStorage:RootPath must be configured.");
        if (!Path.IsPathFullyQualified(root)) throw new InvalidOperationException("EmployeeImportStorage:RootPath must be an absolute path.");
        var fullRoot = Path.GetFullPath(root);
        var webRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (IsWithin(fullRoot, webRoot)) throw new InvalidOperationException("Employee import workbook storage must be outside the web root.");
        return (tenantId, fullRoot);
    }

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

    private static string TenantDirectory(string root, Guid tenantId) => Path.Combine(root, tenantId.ToString("N"));
    private static string ArtifactPath(string directory, Guid operationId) => Path.Combine(directory, operationId.ToString("N") + ".hrw");
    private static void ValidateHash(string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new ArgumentException("Expected workbook identity must be a SHA-256 hex value.", nameof(hash));
    }

    private static bool FixedHashEquals(string expectedHex, byte[] actualHash) =>
        CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expectedHex), actualHash);

    private static bool IsWithin(string path, string parent)
    {
        var relative = Path.GetRelativePath(parent, path);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

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
