using System.Runtime.Versioning;
using System.Security.Cryptography;
using BackupEncryptionPrototype;

public sealed class StreamingRecoveryEvidenceContractTests
{
    [Fact]
    public void Synthetic_publish_restore_evidence_matches_MigrationHost_v2_contract()
    {
        if (!OperatingSystem.IsWindows()) return;
        RunOnWindows();
    }

    [SupportedOSPlatform("windows")]
    private static void RunOnWindows()
    {
        var root = Path.Combine(Path.GetTempPath(), "hrms-evidence-contract-" + Guid.NewGuid().ToString("N"));
        StreamingArtifactPublisher.InitializeNewProtectedRoot(root);
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            const string sourceUuid = "synthetic-source-server";
            var snapshot = new DatabaseSnapshot(299, new string('A', 64), ["migration-a", "migration-b"],
                new ImportAggregate("b89d04aa-1419-4c85-a7e3-48ab1d92d111", "Processing", 10_000, 0, 1, 2_844, 7_155, "expired"), true);
            var restoreIdentity = new RestoreTargetIdentity("127.0.0.1", 43307, "synthetic-restore-server", "hrms_restore_evidence", true, true, true, "VerifyCA");
            var fakeTarget = new FakeRestoreTarget(restoreIdentity, snapshot);
            var scope = new StreamingBackupArchive.ArchiveScope(sourceUuid, "WEB1", "hrms_web1", "127.0.0.1", 3306, "hrms_catalog", "MySql", "VerifyCA");
            var approval = "SYNTHETIC-LOCAL-APPROVAL";
            var migrationApproval = "SYNTHETIC-MIGRATION-APPROVAL";
            var retentionRef = "SYNTHETIC-30DAY-POLICY";

            using var dump = new MemoryStream("synthetic dump bytes"u8.ToArray());
            var backup = StreamingArtifactPublisher.Publish(dump, root, "synthetic-evidence-backup", scope, snapshot,
                () => 0, () => { }, 4096);
            var restore = IsolatedRestoreVerifier.VerifyAndRestore(backup.ArtifactPath, backup.ProtectedKeyPath, backup.BackupId,
                backup.Sha256, new RestoreSourceIdentity(sourceUuid, "WEB1", "hrms_web1", "127.0.0.1", 3306, "hrms_catalog", "MySql", "VerifyCA"), snapshot, restoreIdentity, fakeTarget);
            var payloadBytes = LocalRecoveryEvidenceProducer.CreatePayloadBytes(backup, restore, "synthetic-operator", approval,
                migrationApproval, retentionRef, [LocalDevelopmentRecoveryPolicy.RecoveryMigration]);
            var payload = System.Text.Json.JsonSerializer.Deserialize<LocalRecoveryEvidencePayload>(payloadBytes)
                ?? throw new InvalidOperationException("Evidence producer payload did not deserialize into MigrationHost contract.");

            var now = DateTimeOffset.UtcNow.AddSeconds(1);
            var expectation = new LocalRecoveryPolicyExpectation("Development", Environment.MachineName, Environment.MachineName,
                "127.0.0.1", "VerifyCA", sourceUuid, sourceUuid, LocalDevelopmentRecoveryPolicy.ExpectedTenantId, "WEB1", "web1",
                "hrms_catalog", "hrms_web1", "MySql", [LocalDevelopmentRecoveryPolicy.RecoveryMigration], approval,
                migrationApproval, backup.BackupId, restore.RestoreReference, "synthetic-operator",
                new HashSet<string>([approval], StringComparer.Ordinal), root, 30, retentionRef, now);
            var evidencePath = Path.Combine(root, "synthetic-evidence.json");
            File.WriteAllBytes(evidencePath, LocalDevelopmentRecoveryPolicy.SerializeAndSignEvidence(payload, signer));
            var verified = LocalDevelopmentRecoveryPolicy.ReadAndValidateEvidence(evidencePath, signer.ExportSubjectPublicKeyInfoPem(), expectation);
            Assert.Equal(10_000, verified.ImportTotalRows);
            Assert.Equal(299, verified.SourceTableCount);
            Assert.Equal(restore.RestoreReference, verified.RestoreTestReference);
            CryptographicOperations.ZeroMemory(payloadBytes);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class FakeRestoreTarget(RestoreTargetIdentity identity, DatabaseSnapshot snapshot) : IIsolatedRestoreTarget
    {
        private readonly MemoryStream _restoreInput = new();
        public RestoreTargetIdentity InspectIdentity() => identity;
        public Stream OpenImportStream() => _restoreInput;
        public void CompleteImport() { }
        public void AbortImport() { }
        public DatabaseSnapshot InspectRestoredSnapshot() => snapshot;
    }
}
