using System.Security.Cryptography;
using System.Text.Json;

namespace BackupEncryptionPrototype;

public sealed class PublishedArtifact
{
    internal PublishedArtifact(string backupId, string artifactPath, string protectedKeyPath, string sha256, long length,
        StreamingBackupArchive.ArchiveScope sourceScope, DatabaseSnapshot sourceSnapshot, DateTimeOffset createdUtc)
        => (BackupId, ArtifactPath, ProtectedKeyPath, Sha256, Length, SourceScope, SourceSnapshot, CreatedUtc) =
            (backupId, artifactPath, protectedKeyPath, sha256, length, sourceScope, sourceSnapshot, createdUtc);
    public string BackupId { get; }
    public string ArtifactPath { get; }
    public string ProtectedKeyPath { get; }
    public string Sha256 { get; }
    public long Length { get; }
    public StreamingBackupArchive.ArchiveScope SourceScope { get; }
    public DatabaseSnapshot SourceSnapshot { get; }
    public DateTimeOffset CreatedUtc { get; }
}

/// <summary>Encrypts a producer stream into a staged directory and publishes by same-volume directory rename.</summary>
public static class StreamingArtifactPublisher
{
    public static void InitializeNewProtectedRoot(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Restricted backup root creation is Windows-only.");
        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath) || File.Exists(fullPath)) throw new IOException("Backup root already exists; initialization will not change existing permissions.");
        var parent = Directory.GetParent(fullPath) ?? throw new InvalidOperationException("Backup root must have an explicit parent directory.");
        if (!parent.Exists || (parent.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Backup root parent must already exist and cannot be a reparse point.");
        var staging = Path.Combine(parent.FullName, ".hrms-backup-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var published = false;
        try
        {
            RestrictedBackupAcl.ProtectDirectory(staging);
            Directory.Move(staging, fullPath);
            published = true;
            RestrictedBackupAcl.VerifyDirectory(fullPath);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (published && Directory.Exists(fullPath)) Directory.Delete(fullPath, recursive: true);
            throw;
        }
    }

    public static PublishedArtifact Publish(Stream producerOutput, string protectedBackupRoot, string backupId,
        StreamingBackupArchive.ArchiveScope sourceScope, DatabaseSnapshot sourceSnapshot, Func<int> completeProducerAndGetExitCode,
        Action validateProtectedRoot, int chunkSize = StreamingBackupArchive.DefaultChunkSize)
    {
        ArgumentNullException.ThrowIfNull(producerOutput);
        ArgumentNullException.ThrowIfNull(validateProtectedRoot);
        ArgumentNullException.ThrowIfNull(completeProducerAndGetExitCode);
        ArgumentNullException.ThrowIfNull(sourceScope);
        ArgumentNullException.ThrowIfNull(sourceSnapshot);
        if (sourceSnapshot.Import.OperationId != "b89d04aa-1419-4c85-a7e3-48ab1d92d111" || sourceSnapshot.Import.Total != 10_000 ||
            sourceSnapshot.Import.Succeeded + sourceSnapshot.Import.Failed + sourceSnapshot.Import.Skipped + sourceSnapshot.Import.Pending != 10_000 ||
            sourceSnapshot.Import.State != "Processing" || sourceSnapshot.Import.LeaseStatus != "expired" || !sourceSnapshot.AllBaseTablesInnoDb)
            throw new ArgumentException("Sanitized import aggregate does not match the expected operation.", nameof(sourceSnapshot));
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Local backup key protection requires Windows DPAPI CurrentUser.");
        var root = Path.GetFullPath(protectedBackupRoot);
        validateProtectedRoot();
        RestrictedBackupAcl.VerifyDirectory(root);
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Approved backup root is unavailable or reparse-linked.");
        if (string.IsNullOrWhiteSpace(backupId) || backupId.Length > 128 || backupId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Backup identifier is invalid.", nameof(backupId));
        var finalDirectory = Path.Combine(root, backupId);
        if (Directory.Exists(finalDirectory) || File.Exists(finalDirectory)) throw new IOException("Backup identifier already exists; overwrite is prohibited.");
        var staging = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
        var committedByUs = false;
        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[]? wrapped = null;
        try
        {
            Directory.CreateDirectory(staging);
            RestrictedBackupAcl.ProtectDirectory(staging);
            var artifact = Path.Combine(staging, "backup.hrb");
            using (var output = new FileStream(artifact, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024,
                FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                StreamingBackupArchive.EncryptWithKey(producerOutput, output, backupId, key, chunkSize, sourceScope);
                if (completeProducerAndGetExitCode() != 0) throw new InvalidOperationException("Backup producer exited unsuccessfully; publication was cancelled.");
                output.Flush(flushToDisk: true);
            }
            RestrictedBackupAcl.VerifyFile(artifact);
            using (var verify = new FileStream(artifact, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
                _ = StreamingBackupArchive.Verify(verify, key, backupId);
            wrapped = WindowsDpapiKeyProtector.Protect(key, backupId);
            var keyMetadata = new WrappedKeyMetadata(1, backupId, Convert.ToBase64String(wrapped), "Windows-DPAPI-CurrentUser");
            var keyPath = Path.Combine(staging, "key.dpapi.json");
            using (var keyFile = new FileStream(keyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(keyFile, keyMetadata);
                keyFile.Flush(flushToDisk: true);
            }
            RestrictedBackupAcl.VerifyFile(keyPath);
            validateProtectedRoot();
            RestrictedBackupAcl.VerifyDirectory(root);
            Directory.Move(staging, finalDirectory);
            committedByUs = true;
            var committedArtifact = Path.Combine(finalDirectory, "backup.hrb");
            using var committed = new FileStream(committedArtifact, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
            var sha = Convert.ToHexString(SHA256.HashData(committed));
            return new(backupId, committedArtifact, Path.Combine(finalDirectory, "key.dpapi.json"), sha, committed.Length,
                sourceScope, sourceSnapshot, DateTimeOffset.UtcNow);
        }
        catch (Exception failure)
        {
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                if (committedByUs && Directory.Exists(finalDirectory)) Directory.Delete(finalDirectory, recursive: true);
            }
            catch (Exception cleanup) { throw new IOException("Artifact publication failed; encrypted staging cleanup also failed.", new AggregateException(failure, cleanup)); }
            throw new IOException("Artifact publication failed; no artifact was committed.", failure);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (wrapped is not null) CryptographicOperations.ZeroMemory(wrapped);
        }
    }

    private sealed record WrappedKeyMetadata(int SchemaVersion, string BackupId, string ProtectedKeyBase64, string Protection);
}
