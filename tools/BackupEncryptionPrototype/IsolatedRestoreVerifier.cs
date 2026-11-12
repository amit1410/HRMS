using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.Versioning;
using System.Text.Json;

namespace BackupEncryptionPrototype;

public sealed record ImportAggregate(string OperationId, string State, int Total, int Succeeded, int Failed, int Skipped, int Pending,
    string LeaseStatus = "unknown");
public sealed record DatabaseSnapshot(int TableCount, string SchemaSha256, IReadOnlyList<string> AppliedMigrations,
    ImportAggregate Import, bool AllBaseTablesInnoDb = false);
public sealed record RestoreTargetIdentity(string Host, int Port, string ServerUuid, string Database,
    bool IsDisposableInstance, bool GrantsRestrictedToTargetDatabase, bool EventSchedulerDisabled, string TlsMode);
public sealed record RestoreSourceIdentity(string ServerUuid, string TenantCode, string Database, string Host,
    int Port, string Catalog, string Provider, string TlsMode);
public sealed class RestoreVerificationResult
{
    internal RestoreVerificationResult(string restoreReference, RestoreTargetIdentity target, DatabaseSnapshot snapshot,
        string artifactSha256, DateTimeOffset verifiedUtc, DatabaseSnapshot sourceSnapshot, string sourceServerUuid)
        => (RestoreReference, Target, Snapshot, ArtifactSha256, VerifiedUtc, SourceSnapshot, SourceServerUuid) =
            (restoreReference, target, snapshot, artifactSha256, verifiedUtc, sourceSnapshot, sourceServerUuid);
    public string RestoreReference { get; }
    public RestoreTargetIdentity Target { get; }
    public DatabaseSnapshot Snapshot { get; }
    public string ArtifactSha256 { get; }
    public DateTimeOffset VerifiedUtc { get; }
    public DatabaseSnapshot SourceSnapshot { get; }
    public string SourceServerUuid { get; }
}

public interface IIsolatedRestoreTarget
{
    RestoreTargetIdentity InspectIdentity();
    Stream OpenImportStream();
    void CompleteImport();
    void AbortImport();
    DatabaseSnapshot InspectRestoredSnapshot();
}

public interface IBackupDataKeyUnwrapper
{
    byte[] Unprotect(ReadOnlySpan<byte> protectedKey, string backupId);
}

[SupportedOSPlatform("windows")]
public sealed class CurrentUserDpapiKeyUnwrapper : IBackupDataKeyUnwrapper
{
    public byte[] Unprotect(ReadOnlySpan<byte> protectedKey, string backupId)
        => WindowsDpapiKeyProtector.Unprotect(protectedKey, backupId);
}

/// <summary>Restore adapter must be preconfigured for a disposable target with a database-scoped account.</summary>
[SupportedOSPlatform("windows")]
public static class IsolatedRestoreVerifier
{
    private const string ExpectedOperationId = "b89d04aa-1419-4c85-a7e3-48ab1d92d111";

    public static RestoreVerificationResult VerifyAndRestore(string artifactPath, string wrappedKeyPath, string backupId,
        string expectedArtifactSha256, RestoreSourceIdentity source, DatabaseSnapshot sourceSnapshot,
        RestoreTargetIdentity approvedTarget, IIsolatedRestoreTarget target, IBackupDataKeyUnwrapper? keyUnwrapper = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceSnapshot);
        ArgumentNullException.ThrowIfNull(approvedTarget);
        ArgumentNullException.ThrowIfNull(target);
        if (source.TenantCode != "WEB1" || source.Database != "hrms_web1" || string.IsNullOrWhiteSpace(source.ServerUuid))
            throw new InvalidOperationException("Backup source identity is not the approved WEB1 tenant.");
        if (sourceSnapshot.Import.OperationId != ExpectedOperationId || sourceSnapshot.Import.Total != 10_000 ||
            sourceSnapshot.Import.Succeeded + sourceSnapshot.Import.Failed + sourceSnapshot.Import.Skipped + sourceSnapshot.Import.Pending != 10_000)
            throw new InvalidOperationException("Sanitized import aggregate is not consistent with the expected operation.");

        var actualTarget = target.InspectIdentity();
        ValidateTarget(actualTarget, approvedTarget, source.ServerUuid);
        var artifactFullPath = Path.GetFullPath(artifactPath);
        var keyFullPath = Path.GetFullPath(wrappedKeyPath);
        if ((File.GetAttributes(artifactFullPath) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(keyFullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Artifact or wrapped-key file cannot be a reparse point.");
        using var artifact = new FileStream(artifactFullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        var actualHash = SHA256.HashData(artifact);
        if (!Convert.ToHexString(actualHash).Equals(expectedArtifactSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Encrypted artifact hash differs from the approved reference.");
        artifact.Position = 0;

        if (new FileInfo(keyFullPath).Length > 16 * 1024) throw new InvalidDataException("Wrapped-key metadata exceeds its size limit.");
        var wrapped = JsonSerializer.Deserialize<WrappedKeyMetadata>(File.ReadAllBytes(keyFullPath))
            ?? throw new InvalidDataException("Wrapped-key metadata is malformed.");
        if (wrapped.SchemaVersion != 1 || wrapped.BackupId != backupId || wrapped.Protection != "Windows-DPAPI-CurrentUser")
            throw new InvalidDataException("Wrapped-key context does not match this backup.");
        var wrappedBytes = Convert.FromBase64String(wrapped.ProtectedKeyBase64);
        byte[] dataKey = (keyUnwrapper ?? new CurrentUserDpapiKeyUnwrapper()).Unprotect(wrappedBytes, backupId);
        try
        {
            var metadata = StreamingBackupArchive.Verify(artifact, dataKey, backupId);
            if (metadata.SourceServerUuid != source.ServerUuid || metadata.TenantCode != source.TenantCode || metadata.Database != source.Database ||
                metadata.Host != source.Host || metadata.Port != source.Port || metadata.Catalog != source.Catalog ||
                metadata.Provider != source.Provider || metadata.TlsMode != source.TlsMode)
                throw new InvalidDataException("Authenticated archive source metadata does not match the verified source.");
            artifact.Position = 0;
            var restoreStarted = false;
            try
            {
                restoreStarted = true;
                using (var import = target.OpenImportStream())
                {
                    _ = StreamingBackupArchive.VerifyThenDecrypt(artifact, import, dataKey, backupId);
                }
                target.CompleteImport();
                var restored = target.InspectRestoredSnapshot();
                CompareSnapshots(sourceSnapshot, restored);
                var reference = "restore-" + Guid.NewGuid().ToString("N");
                return new(reference, actualTarget, restored, Convert.ToHexString(actualHash), DateTimeOffset.UtcNow, sourceSnapshot, source.ServerUuid);
            }
            catch (Exception failure) when (restoreStarted)
            {
                try { target.AbortImport(); }
                catch (Exception cleanup) { throw new IOException("Isolated restore failed and disposable-target cleanup failed.", new AggregateException(failure, cleanup)); }
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dataKey);
            CryptographicOperations.ZeroMemory(wrappedBytes);
            CryptographicOperations.ZeroMemory(actualHash);
        }
    }

    private static void ValidateTarget(RestoreTargetIdentity actual, RestoreTargetIdentity approved, string sourceUuid)
    {
        if (actual != approved || !IPAddress.TryParse(actual.Host, out var ip) || !IPAddress.IsLoopback(ip) || actual.Port is < 1 or > 65535 ||
            actual.ServerUuid.Equals(sourceUuid, StringComparison.OrdinalIgnoreCase) || actual.Database.Equals("hrms_web1", StringComparison.OrdinalIgnoreCase) ||
            actual.Database.Equals("hrms_catalog", StringComparison.OrdinalIgnoreCase) ||
            !actual.Database.StartsWith("hrms_restore_", StringComparison.OrdinalIgnoreCase) || !actual.IsDisposableInstance ||
            !actual.GrantsRestrictedToTargetDatabase || !actual.EventSchedulerDisabled || actual.TlsMode is not ("VerifyCA" or "VerifyFull"))
            throw new InvalidOperationException("Restore target identity, separation, privileges, or scheduler state is not approved.");
    }

    private static void CompareSnapshots(DatabaseSnapshot source, DatabaseSnapshot restored)
    {
        if (source.TableCount != restored.TableCount || !source.AllBaseTablesInnoDb || !restored.AllBaseTablesInnoDb ||
            !source.SchemaSha256.Equals(restored.SchemaSha256, StringComparison.OrdinalIgnoreCase) ||
            !source.AppliedMigrations.SequenceEqual(restored.AppliedMigrations, StringComparer.Ordinal) || source.Import != restored.Import)
            throw new InvalidDataException("Restored schema, migration history, or sanitized import aggregates differ from the source snapshot.");
    }

    private sealed record WrappedKeyMetadata(int SchemaVersion, string BackupId, string ProtectedKeyBase64, string Protection);
}
