using System.Net;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

internal sealed record LocalRecoveryPolicyExpectation(
    string Environment,
    string MachineName,
    string ExpectedMachineName,
    string EndpointHost,
    string TlsMode,
    string ExpectedServerUuid,
    string ActualServerUuid,
    Guid TenantId,
    string TenantCode,
    string Shard,
    string Catalog,
    string Database,
    string Provider,
    IReadOnlyList<string> PendingMigrations,
    string ApprovalReference,
    string MigrationApprovalReference,
    string BackupReference,
    string RestoreTestReference,
    string OperatorIdentity,
    IReadOnlySet<string> AllowedApprovals,
    string BackupRoot,
    int RetentionDays,
    string RetentionPolicyReference,
    DateTimeOffset Now,
    int Port = 3306);

internal sealed record LocalRecoveryEvidencePayload(
    int SchemaVersion,
    string PolicyId,
    string BackupId,
    string Environment,
    string MachineName,
    Guid TenantId,
    string TenantCode,
    string Shard,
    string Catalog,
    string Database,
    string Provider,
    string MySqlServerUuid,
    string ArtifactPath,
    string ArtifactSha256,
    string EncryptionAlgorithm,
    string KeyProtection,
    bool AuthenticatedDecryptionPassed,
    DateTimeOffset BackupCreatedUtc,
    DateTimeOffset RetainUntilUtc,
    string RestoreDatabase,
    string RestoreServerUuid,
    bool IsolatedRestorePassed,
    DateTimeOffset RestoreTestedUtc,
    string RestoreTestReference,
    IReadOnlyList<string> PendingMigrations,
    string OperatorIdentity,
    string ApprovalReference,
    string MigrationApprovalReference,
    string RetentionPolicyReference,
    int SourceTableCount = 0,
    int RestoreTableCount = 0,
    string SchemaSha256 = "",
    string RestoreSchemaSha256 = "",
    string MigrationHistorySha256 = "",
    string RestoreMigrationHistorySha256 = "",
    IReadOnlyList<string>? AppliedMigrations = null,
    IReadOnlyList<string>? RestoreAppliedMigrations = null,
    string ImportOperationId = "",
    string ImportState = "",
    int ImportTotalRows = 0,
    int ImportSucceededRows = 0,
    int ImportFailedRows = 0,
    int ImportSkippedRows = 0,
    int ImportPendingRows = 0,
    string RestoreImportState = "",
    int RestoreImportTotalRows = 0,
    int RestoreImportSucceededRows = 0,
    int RestoreImportFailedRows = 0,
    int RestoreImportSkippedRows = 0,
    int RestoreImportPendingRows = 0,
    string SourceEndpointHost = "",
    int SourcePort = 0,
    string SourceTlsMode = "",
    string RestoreEndpointHost = "",
    int RestorePort = 0,
    string RestoreTlsMode = "",
    bool AllBaseTablesInnoDb = false,
    bool RestoreAllBaseTablesInnoDb = false,
    string ImportLeaseStatus = "",
    string RestoreImportLeaseStatus = "");

internal sealed record SignedLocalRecoveryEvidence(string PayloadBase64, string SignatureBase64);

internal static class LocalDevelopmentRecoveryPolicy
{
    public const string PolicyId = "hrms-local-development-web1-recovery-v2";
    public const string ExpectedProvider = "MySql";
    public const string ExpectedCatalog = "hrms_catalog";
    public const string ExpectedDatabase = "hrms_web1";
    public const string ExpectedTenantCode = "WEB1";
    public const string ExpectedShard = "web1";
    public const int MaximumEvidenceAgeHours = 24;
    public static readonly Guid ExpectedTenantId = Guid.Parse("16313e68-2e72-4a6a-b689-79b271cec295");
    public const string RecoveryMigration = "20261104173339_AddEmployeeImportRecoveryAudit";

    public static void ValidateContext(LocalRecoveryPolicyExpectation expected)
    {
        Require(string.Equals(expected.Environment, "Development", StringComparison.Ordinal), "Local policy is restricted to the Development environment.");
        Require(!string.IsNullOrWhiteSpace(expected.MachineName) &&
            string.Equals(expected.MachineName, expected.ExpectedMachineName, StringComparison.OrdinalIgnoreCase),
            "Local machine identity does not match the approved machine.");
        Require(IPAddress.TryParse(expected.EndpointHost, out var endpoint) && IPAddress.IsLoopback(endpoint),
            "Local policy requires a literal loopback MySQL endpoint.");
        Require(expected.TlsMode is "VerifyCA" or "VerifyFull", "Local policy requires TLS encryption and CA verification; identity verification remains preferred.");
        Require(!string.IsNullOrWhiteSpace(expected.ExpectedServerUuid) &&
            string.Equals(expected.ActualServerUuid, expected.ExpectedServerUuid, StringComparison.OrdinalIgnoreCase),
            "MySQL server UUID does not match the approved local instance.");
        Require(expected.TenantId == ExpectedTenantId && expected.TenantCode == ExpectedTenantCode && expected.Shard == ExpectedShard &&
            expected.Catalog.Equals(ExpectedCatalog, StringComparison.OrdinalIgnoreCase) &&
            expected.Database.Equals(ExpectedDatabase, StringComparison.OrdinalIgnoreCase) &&
            expected.Provider.Equals(ExpectedProvider, StringComparison.OrdinalIgnoreCase),
            "Target is not the exact approved WEB1 MySQL tenant.");
        Require(expected.PendingMigrations.SequenceEqual([RecoveryMigration], StringComparer.Ordinal),
            "Complete pending migration set must contain only the recovery audit migration.");
        Require(!string.IsNullOrWhiteSpace(expected.ApprovalReference) && expected.AllowedApprovals.Contains(expected.ApprovalReference),
            "Local policy approval is missing or not allowlisted.");
        Require(!string.IsNullOrWhiteSpace(expected.OperatorIdentity), "Local policy requires an explicit operator identity.");
        Require(!string.IsNullOrWhiteSpace(expected.MigrationApprovalReference) &&
            !string.IsNullOrWhiteSpace(expected.BackupReference) && !string.IsNullOrWhiteSpace(expected.RestoreTestReference),
            "Migration approval, backup reference, and restore-test reference are required.");
        Require(Path.IsPathFullyQualified(expected.BackupRoot) && !expected.BackupRoot.StartsWith("\\\\", StringComparison.Ordinal),
            "Backup root must be a fully qualified local path.");
        Require(expected.RetentionDays == 30, "The approved local WEB1 retention period is exactly thirty days.");
    }

    public static LocalRecoveryEvidencePayload ReadAndValidateEvidence(
        string evidencePath,
        string trustedPublicKeyPem,
        LocalRecoveryPolicyExpectation expected)
    {
        ValidateContext(expected);
        Require(!string.IsNullOrWhiteSpace(evidencePath) && File.Exists(evidencePath), "Signed local backup evidence file is unavailable.");
        var root = Path.GetFullPath(expected.BackupRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var evidenceFullPath = Path.GetFullPath(evidencePath);
        Require(evidenceFullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase), "Signed evidence must be stored under the approved backup root.");
        Require(new FileInfo(evidencePath).Length <= 256 * 1024, "Signed local backup evidence exceeds the size limit.");
        var evidenceAttributes = File.GetAttributes(evidencePath);
        Require((evidenceAttributes & FileAttributes.ReparsePoint) == 0, "Evidence file cannot be a reparse point.");
        var envelope = JsonSerializer.Deserialize<SignedLocalRecoveryEvidence>(File.ReadAllBytes(evidencePath))
            ?? throw new InvalidOperationException("Signed local backup evidence is malformed.");
        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = Convert.FromBase64String(envelope.PayloadBase64);
            signature = Convert.FromBase64String(envelope.SignatureBase64);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentNullException) { throw new InvalidOperationException("Signed local backup evidence encoding is invalid.", ex); }

        using var verifier = ECDsa.Create();
        verifier.ImportFromPem(trustedPublicKeyPem);
        Require(verifier.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256), "Local backup evidence signature is invalid.");
        var payload = JsonSerializer.Deserialize<LocalRecoveryEvidencePayload>(payloadBytes)
            ?? throw new InvalidOperationException("Signed local backup evidence payload is malformed.");
        ValidateEvidence(payload, expected);
        return payload;
    }

    public static byte[] SerializeAndSignEvidence(LocalRecoveryEvidencePayload payload, ECDsa signer)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(signer);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var envelope = new SignedLocalRecoveryEvidence(Convert.ToBase64String(bytes), Convert.ToBase64String(signer.SignData(bytes, HashAlgorithmName.SHA256)));
        return JsonSerializer.SerializeToUtf8Bytes(envelope);
    }

    private static void ValidateEvidence(LocalRecoveryEvidencePayload e, LocalRecoveryPolicyExpectation x)
    {
        Require(e.SchemaVersion == 2 && e.PolicyId == PolicyId && !string.IsNullOrWhiteSpace(e.BackupId), "Evidence schema or local policy identifier is invalid.");
        Require(new[] { e.Environment, e.MachineName, e.TenantCode, e.Shard, e.Catalog, e.Database, e.Provider,
            e.MySqlServerUuid, e.ArtifactPath, e.ArtifactSha256, e.EncryptionAlgorithm, e.KeyProtection,
            e.RestoreDatabase, e.RestoreServerUuid, e.RestoreTestReference, e.OperatorIdentity, e.ApprovalReference,
            e.SourceEndpointHost, e.SourceTlsMode, e.RestoreEndpointHost, e.RestoreTlsMode,
            e.MigrationApprovalReference, e.RetentionPolicyReference }.All(x => !string.IsNullOrWhiteSpace(x)) &&
            e.PendingMigrations is not null, "Evidence contains missing required fields.");
        Require(e.Environment == "Development" && e.MachineName.Equals(x.MachineName, StringComparison.OrdinalIgnoreCase), "Evidence environment or machine identity does not match.");
        Require(e.TenantId == x.TenantId && e.TenantCode == x.TenantCode && e.Shard == x.Shard && e.Catalog == x.Catalog && e.Database == x.Database && e.Provider == x.Provider,
            "Evidence tenant identity does not match the approved WEB1 target.");
        Require(e.MySqlServerUuid.Equals(x.ActualServerUuid, StringComparison.OrdinalIgnoreCase), "Evidence MySQL instance identity does not match.");
        Require(e.EncryptionAlgorithm == "AES-256-GCM" && e.KeyProtection == "Windows-DPAPI-CurrentUser", "Evidence encryption or key protection is not approved for this local policy.");
        Require(e.AuthenticatedDecryptionPassed && e.IsolatedRestorePassed, "Authenticated decryption or isolated restore evidence is missing or failed.");
        Require(e.SourceTableCount > 0 && e.SourceTableCount == e.RestoreTableCount &&
            IsSha256(e.SchemaSha256) && e.SchemaSha256.Equals(e.RestoreSchemaSha256, StringComparison.OrdinalIgnoreCase) &&
            IsSha256(e.MigrationHistorySha256) && e.MigrationHistorySha256.Equals(e.RestoreMigrationHistorySha256, StringComparison.OrdinalIgnoreCase) &&
            e.AppliedMigrations is not null && e.RestoreAppliedMigrations is not null &&
            e.AppliedMigrations.SequenceEqual(e.RestoreAppliedMigrations, StringComparer.Ordinal),
            "Restored table, schema, or complete migration-history evidence is missing or mismatched.");
        Require(e.ImportOperationId == "b89d04aa-1419-4c85-a7e3-48ab1d92d111" &&
            !string.IsNullOrWhiteSpace(e.ImportState) && e.ImportState == e.RestoreImportState &&
            e.ImportLeaseStatus == "expired" && e.ImportLeaseStatus == e.RestoreImportLeaseStatus &&
            e.ImportTotalRows == 10_000 && e.ImportTotalRows == e.RestoreImportTotalRows &&
            e.ImportSucceededRows == e.RestoreImportSucceededRows && e.ImportFailedRows == e.RestoreImportFailedRows &&
            e.ImportSkippedRows == e.RestoreImportSkippedRows && e.ImportPendingRows == e.RestoreImportPendingRows &&
            e.ImportSucceededRows + e.ImportFailedRows + e.ImportSkippedRows + e.ImportPendingRows == e.ImportTotalRows,
            "Sanitized operation aggregate evidence is missing, inconsistent, or differs from the isolated restore.");
        Require(e.AllBaseTablesInnoDb && e.RestoreAllBaseTablesInnoDb,
            "Snapshot consistency requires every base table on both source and restore to use InnoDB.");
        Require(IPAddress.TryParse(e.SourceEndpointHost, out var sourceIp) && IPAddress.IsLoopback(sourceIp) &&
            e.SourceEndpointHost == x.EndpointHost && e.SourcePort == x.Port && e.SourceTlsMode == x.TlsMode &&
            IPAddress.TryParse(e.RestoreEndpointHost, out var restoreIp) && IPAddress.IsLoopback(restoreIp) &&
            e.RestorePort is >= 1 and <= 65535 && (e.RestoreTlsMode is "VerifyCA" or "VerifyFull"),
            "Signed source or isolated-restore endpoint/TLS evidence is invalid or mismatched.");
        Require(!e.RestoreDatabase.Equals(x.Database, StringComparison.OrdinalIgnoreCase) &&
            !e.RestoreDatabase.Equals(x.Catalog, StringComparison.OrdinalIgnoreCase) && e.RestoreServerUuid.Length > 0 &&
            e.RestoreDatabase.StartsWith("hrms_restore_", StringComparison.OrdinalIgnoreCase) &&
            !e.RestoreServerUuid.Equals(e.MySqlServerUuid, StringComparison.OrdinalIgnoreCase),
            "Restore target is not proven to be a separate isolated database.");
        Require((e.PendingMigrations ?? throw new InvalidOperationException("Evidence migration inventory is missing."))
            .SequenceEqual(x.PendingMigrations, StringComparer.Ordinal), "Evidence migration inventory differs from the exact approved pending set.");
        Require(e.BackupId == x.BackupReference && e.RestoreTestReference == x.RestoreTestReference &&
            e.ApprovalReference == x.ApprovalReference && e.MigrationApprovalReference == x.MigrationApprovalReference &&
            e.OperatorIdentity == x.OperatorIdentity && !string.IsNullOrWhiteSpace(e.OperatorIdentity) &&
            e.RetentionPolicyReference == x.RetentionPolicyReference && !string.IsNullOrWhiteSpace(x.RetentionPolicyReference),
            "Evidence operator, approval, or retention-policy reference is missing or mismatched.");
        Require(e.ArtifactSha256 is { Length: 64 } && e.ArtifactSha256.All(Uri.IsHexDigit), "Encrypted artifact SHA-256 is malformed.");
        var root = Path.GetFullPath(x.BackupRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var artifact = Path.GetFullPath(e.ArtifactPath);
        Require(artifact.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(artifact), "Encrypted artifact is missing or outside the approved backup root.");
        Require((File.GetAttributes(artifact) & FileAttributes.ReparsePoint) == 0, "Encrypted artifact cannot be a reparse point.");
        using (var stream = File.OpenRead(artifact))
            Require(Convert.ToHexString(SHA256.HashData(stream)).Equals(e.ArtifactSha256, StringComparison.OrdinalIgnoreCase), "Encrypted artifact SHA-256 does not match signed evidence.");
        var now = x.Now.ToUniversalTime();
        Require(e.BackupCreatedUtc <= now && e.RestoreTestedUtc <= now && e.RetainUntilUtc > now &&
            e.BackupCreatedUtc >= now.AddHours(-MaximumEvidenceAgeHours) && e.RestoreTestedUtc >= now.AddHours(-MaximumEvidenceAgeHours) &&
            e.RestoreTestedUtc >= e.BackupCreatedUtc && e.RetainUntilUtc == e.BackupCreatedUtc.AddDays(x.RetentionDays),
            "Backup, restore-test, or retention timestamps are stale or inconsistent.");
    }

    [SupportedOSPlatform("windows")]
    public static void VerifyRestrictedBackupRoot(string backupRoot)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Local backup storage policy is Windows-only.");
        var directory = new DirectoryInfo(Path.GetFullPath(backupRoot));
        Require(directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) == 0,
            "Protected local backup root is missing or is a reparse point.");
        var security = directory.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        Require(security.AreAccessRulesProtected, "Backup root must not inherit access rules.");
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier
            ?? throw new InvalidOperationException("Backup root owner identity is unavailable.");
        var identity = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows user identity is unavailable.");
        var system = new SecurityIdentifier("S-1-5-18");
        var admins = new SecurityIdentifier("S-1-5-32-544");
        Require(owner.Equals(identity) || owner.Equals(admins), "Backup root owner is not the operator or local Administrators.");
        var allowed = new[] { identity, system, admins };
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        Require(rules.Length >= allowed.Length && rules.All(rule => !rule.IsInherited && rule.AccessControlType == AccessControlType.Allow &&
            allowed.Any(sid => sid.Equals(rule.IdentityReference)) &&
            (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl),
            "Backup root ACL contains inherited, broad, denied, or insufficient access.");
        Require(allowed.All(sid => rules.Any(rule => sid.Equals(rule.IdentityReference))), "Backup root ACL is missing an approved principal.");
    }

    [SupportedOSPlatform("windows")]
    public static void VerifyRestrictedBackupFile(string filePath)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Local backup storage policy is Windows-only.");
        var file = new FileInfo(Path.GetFullPath(filePath));
        Require(file.Exists && (file.Attributes & FileAttributes.ReparsePoint) == 0, "Backup evidence or artifact file is missing or is a reparse point.");
        var security = file.GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier
            ?? throw new InvalidOperationException("Backup file owner identity is unavailable.");
        using var currentIdentity = WindowsIdentity.GetCurrent();
        var identity = currentIdentity.User ?? throw new InvalidOperationException("Current Windows user identity is unavailable.");
        var system = new SecurityIdentifier("S-1-5-18");
        var admins = new SecurityIdentifier("S-1-5-32-544");
        var allowed = new[] { identity, system, admins };
        Require(owner.Equals(identity) || owner.Equals(admins), "Backup file owner is not the operator or local Administrators.");
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        Require(rules.Length >= allowed.Length && rules.All(rule => rule.AccessControlType == AccessControlType.Allow &&
            allowed.Any(sid => sid.Equals(rule.IdentityReference)) &&
            (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl),
            "Backup file ACL contains broad, denied, or insufficient access.");
        Require(allowed.All(sid => rules.Any(rule => sid.Equals(rule.IdentityReference))), "Backup file ACL is missing an approved principal.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static bool IsSha256(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
