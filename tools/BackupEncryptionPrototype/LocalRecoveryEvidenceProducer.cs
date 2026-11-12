using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace BackupEncryptionPrototype;

/// <summary>Writes only evidence derived from successful publisher and isolated restore results.</summary>
[SupportedOSPlatform("windows")]
public static class LocalRecoveryEvidenceProducer
{
    private const string Policy = "hrms-local-development-web1-recovery-v2";
    private const string TenantCode = "WEB1";
    private const string Shard = "web1";
    private const string Catalog = "hrms_catalog";
    private const string Database = "hrms_web1";
    private static readonly Guid TenantId = Guid.Parse("16313e68-2e72-4a6a-b689-79b271cec295");

    public static string WriteSigned(PublishedArtifact backup, RestoreVerificationResult restore, string backupRoot,
        string operatorIdentity, string localApprovalReference, string migrationApprovalReference,
        string retentionPolicyReference, IReadOnlyList<string> pendingMigrations, string certificateThumbprint,
        Action validateProtectedRoot)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(restore);
        ArgumentNullException.ThrowIfNull(validateProtectedRoot);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Local evidence signing requires the Windows certificate store.");
        var payloadBytes = CreatePayloadBytes(backup, restore, operatorIdentity, localApprovalReference,
            migrationApprovalReference, retentionPolicyReference, pendingMigrations);
        validateProtectedRoot();
        var root = Path.GetFullPath(backupRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        RestrictedBackupAcl.VerifyDirectory(backupRoot);
        var directory = Path.GetDirectoryName(Path.GetFullPath(backup.ArtifactPath))!;
        if (!Path.GetFullPath(backup.ArtifactPath).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Backup artifact is outside the approved evidence root.");
        var destination = Path.Combine(directory, "recovery-evidence.json");
        var temporary = Path.Combine(directory, ".evidence-" + Guid.NewGuid().ToString("N") + ".tmp");
        if (File.Exists(destination)) throw new IOException("Evidence already exists; overwrite is prohibited.");
        var publishedByUs = false;
        try
        {
            using var signer = OpenNonExportableCurrentUserSigner(certificateThumbprint);
            var signature = signer.SignData(payloadBytes, HashAlgorithmName.SHA256);
            if (!signer.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256))
                throw new CryptographicException("Evidence signature self-verification failed.");
            var envelope = JsonSerializer.SerializeToUtf8Bytes(new EvidenceEnvelope(Convert.ToBase64String(payloadBytes), Convert.ToBase64String(signature)));
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.WriteThrough))
            {
                output.Write(envelope);
                output.Flush(flushToDisk: true);
            }
            RestrictedBackupAcl.VerifyFile(temporary);
            validateProtectedRoot();
            RestrictedBackupAcl.VerifyDirectory(backupRoot);
            File.Move(temporary, destination, overwrite: false);
            publishedByUs = true;
            RestrictedBackupAcl.VerifyFile(destination);
            return destination;
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (publishedByUs && File.Exists(destination)) File.Delete(destination);
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(payloadBytes); }
    }

    public static byte[] CreatePayloadBytes(PublishedArtifact backup, RestoreVerificationResult restore,
        string operatorIdentity, string localApprovalReference, string migrationApprovalReference,
        string retentionPolicyReference, IReadOnlyList<string> pendingMigrations)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(restore);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Local evidence is restricted to the Windows local policy.");
        if (backup.BackupId != Path.GetFileName(Path.GetDirectoryName(backup.ArtifactPath)) ||
            backup.Sha256 != restore.ArtifactSha256 || backup.SourceScope.SourceServerUuid != restore.SourceServerUuid)
            throw new InvalidOperationException("Backup and restore attestations are not bound to the same artifact.");
        if (!File.Exists(backup.ArtifactPath) || (File.GetAttributes(backup.ArtifactPath) & FileAttributes.ReparsePoint) != 0 ||
            !File.Exists(backup.ProtectedKeyPath) || (File.GetAttributes(backup.ProtectedKeyPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Published encrypted artifact or wrapped-key metadata is unavailable or reparse-linked.");
        RestrictedBackupAcl.VerifyFile(backup.ArtifactPath);
        RestrictedBackupAcl.VerifyFile(backup.ProtectedKeyPath);
        using (var artifact = new FileStream(backup.ArtifactPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            if (!Convert.ToHexString(SHA256.HashData(artifact)).Equals(backup.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Encrypted artifact changed after isolated restore verification.");
        ValidateSnapshots(backup.SourceSnapshot, restore.Snapshot);
        if (restore.Target.ServerUuid == backup.SourceScope.SourceServerUuid || restore.Target.Database is "hrms_web1" or "hrms_catalog")
            throw new InvalidOperationException("Restore target is not separate from the backup source.");
        if (backup.SourceScope.TenantCode != TenantCode || backup.SourceScope.Database != Database ||
            backup.SourceSnapshot.Import.OperationId != "b89d04aa-1419-4c85-a7e3-48ab1d92d111")
            throw new InvalidOperationException("Evidence source scope is outside the approved WEB1 recovery operation.");
        var now = DateTimeOffset.UtcNow;
        var source = backup.SourceSnapshot;
        var payload = new EvidencePayload(
            2, Policy, backup.BackupId, "Development", Environment.MachineName, TenantId, TenantCode, Shard, Catalog, Database, "MySql",
            backup.SourceScope.SourceServerUuid, backup.ArtifactPath, backup.Sha256, "AES-256-GCM", "Windows-DPAPI-CurrentUser", true,
            backup.CreatedUtc, backup.CreatedUtc.AddDays(30), restore.Target.Database, restore.Target.ServerUuid, true, restore.VerifiedUtc,
            restore.RestoreReference, pendingMigrations, operatorIdentity, localApprovalReference, migrationApprovalReference, retentionPolicyReference,
            source.TableCount, restore.Snapshot.TableCount, source.SchemaSha256, restore.Snapshot.SchemaSha256,
            HashMigrations(source.AppliedMigrations), HashMigrations(restore.Snapshot.AppliedMigrations), source.AppliedMigrations,
            restore.Snapshot.AppliedMigrations, source.Import.OperationId, source.Import.State, source.Import.Total,
            source.Import.Succeeded, source.Import.Failed, source.Import.Skipped, source.Import.Pending, restore.Snapshot.Import.State,
            restore.Snapshot.Import.Total, restore.Snapshot.Import.Succeeded, restore.Snapshot.Import.Failed,
            restore.Snapshot.Import.Skipped, restore.Snapshot.Import.Pending, backup.SourceScope.Host, backup.SourceScope.Port,
            backup.SourceScope.TlsMode, restore.Target.Host, restore.Target.Port, restore.Target.TlsMode,
            source.AllBaseTablesInnoDb, restore.Snapshot.AllBaseTablesInnoDb, source.Import.LeaseStatus, restore.Snapshot.Import.LeaseStatus);
        if (string.IsNullOrWhiteSpace(operatorIdentity) || string.IsNullOrWhiteSpace(localApprovalReference) ||
            string.IsNullOrWhiteSpace(migrationApprovalReference) || string.IsNullOrWhiteSpace(retentionPolicyReference) || pendingMigrations is null)
            throw new InvalidOperationException("Evidence requires explicit operator, approval, retention, and migration references.");
        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    private static ECDsa OpenNonExportableCurrentUserSigner(string thumbprint)
    {
        var normalized = new string((thumbprint ?? string.Empty).Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        if (normalized.Length != 40) throw new InvalidOperationException("A SHA-1 certificate thumbprint is required to select the approved ECDSA signing certificate.");
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var certificate = store.Certificates.Cast<X509Certificate2>()
            .SingleOrDefault(c => string.Equals(c.Thumbprint?.Replace(" ", "", StringComparison.Ordinal), normalized, StringComparison.OrdinalIgnoreCase));
        if (certificate is null || !certificate.HasPrivateKey || DateTime.UtcNow < certificate.NotBefore.ToUniversalTime() || DateTime.UtcNow > certificate.NotAfter.ToUniversalTime())
            throw new InvalidOperationException("Approved current-user evidence-signing certificate is unavailable, expired, or lacks a private key.");
        ECDsa? key;
        using (certificate)
        {
            var keyUsage = certificate.Extensions.OfType<X509KeyUsageExtension>().SingleOrDefault();
            if (keyUsage is not null && !keyUsage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature))
                throw new InvalidOperationException("Evidence-signing certificate does not permit digital signatures.");
            key = certificate.GetECDsaPrivateKey();
        }
        if (key is not ECDsaCng cng || cng.Key.ExportPolicy != CngExportPolicies.None)
        {
            key?.Dispose();
            throw new InvalidOperationException("Signing key must be a non-exportable CNG ECDSA key.");
        }
        var uniqueName = cng.Key.UniqueName ?? throw new InvalidOperationException("Signing key has no persistent identifier.");
        var keyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Crypto", "Keys", uniqueName);
        RestrictedBackupAcl.VerifyFile(keyFile);
        return key;
    }

    private static void ValidateSnapshots(DatabaseSnapshot source, DatabaseSnapshot restored)
    {
        if (source.TableCount <= 0 || source.TableCount != restored.TableCount || !IsSha(source.SchemaSha256) ||
            !source.SchemaSha256.Equals(restored.SchemaSha256, StringComparison.OrdinalIgnoreCase) ||
            !source.AppliedMigrations.SequenceEqual(restored.AppliedMigrations, StringComparer.Ordinal) || source.Import != restored.Import ||
            !IsSha(HashMigrations(source.AppliedMigrations)))
            throw new InvalidOperationException("Source and isolated restore snapshots are incomplete or inconsistent.");
    }

    private static string HashMigrations(IReadOnlyList<string> migrations)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(migrations)));
    private static bool IsSha(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    // Property names and ordering-compatible types intentionally mirror MigrationHost's signed v2 contract.
    private sealed record EvidencePayload(int SchemaVersion, string PolicyId, string BackupId, string Environment, string MachineName,
        Guid TenantId, string TenantCode, string Shard, string Catalog, string Database, string Provider, string MySqlServerUuid,
        string ArtifactPath, string ArtifactSha256, string EncryptionAlgorithm, string KeyProtection, bool AuthenticatedDecryptionPassed,
        DateTimeOffset BackupCreatedUtc, DateTimeOffset RetainUntilUtc, string RestoreDatabase, string RestoreServerUuid,
        bool IsolatedRestorePassed, DateTimeOffset RestoreTestedUtc, string RestoreTestReference, IReadOnlyList<string> PendingMigrations,
        string OperatorIdentity, string ApprovalReference, string MigrationApprovalReference, string RetentionPolicyReference,
        int SourceTableCount, int RestoreTableCount, string SchemaSha256, string RestoreSchemaSha256, string MigrationHistorySha256,
        string RestoreMigrationHistorySha256, IReadOnlyList<string> AppliedMigrations, IReadOnlyList<string> RestoreAppliedMigrations,
        string ImportOperationId, string ImportState, int ImportTotalRows, int ImportSucceededRows, int ImportFailedRows,
        int ImportSkippedRows, int ImportPendingRows, string RestoreImportState, int RestoreImportTotalRows,
        int RestoreImportSucceededRows, int RestoreImportFailedRows, int RestoreImportSkippedRows, int RestoreImportPendingRows,
        string SourceEndpointHost, int SourcePort, string SourceTlsMode, string RestoreEndpointHost, int RestorePort, string RestoreTlsMode,
        bool AllBaseTablesInnoDb, bool RestoreAllBaseTablesInnoDb, string ImportLeaseStatus, string RestoreImportLeaseStatus);
    private sealed record EvidenceEnvelope(string PayloadBase64, string SignatureBase64);
}
