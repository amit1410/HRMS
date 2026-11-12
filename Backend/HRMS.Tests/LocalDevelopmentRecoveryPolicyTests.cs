using System.Security.Cryptography;
using System.Runtime.Versioning;

public sealed class LocalDevelopmentRecoveryPolicyTests
{
    [Fact]
    public void Local_policy_accepts_fresh_signed_WEB1_backup_and_isolated_restore_evidence()
    {
        using var fixture = new EvidenceFixture();
        var validated = LocalDevelopmentRecoveryPolicy.ReadAndValidateEvidence(fixture.EvidencePath, fixture.PublicKey, fixture.Expectation);
        Assert.Equal("backup-test-1", validated.BackupId);
        Assert.Equal(LocalDevelopmentRecoveryPolicy.PolicyId, validated.PolicyId);
    }

    [Theory]
    [InlineData("Staging", "WEB1", "hrms_web1", "127.0.0.1", "mysql-local-instance")]
    [InlineData("Production", "WEB1", "hrms_web1", "127.0.0.1", "mysql-local-instance")]
    [InlineData("Development", "OTHER", "hrms_web1", "127.0.0.1", "mysql-local-instance")]
    [InlineData("Development", "WEB1", "hrms_catalog", "127.0.0.1", "mysql-local-instance")]
    [InlineData("Development", "WEB1", "hrms_web1", "localhost", "mysql-local-instance")]
    [InlineData("Development", "WEB1", "hrms_web1", "127.0.0.1", "wrong-instance")]
    public void Local_policy_rejects_wrong_environment_target_endpoint_or_instance(
        string environment, string tenant, string database, string endpoint, string serverUuid)
    {
        using var fixture = new EvidenceFixture();
        var expectation = fixture.Expectation with
        {
            Environment = environment,
            TenantCode = tenant,
            Database = database,
            EndpointHost = endpoint,
            ActualServerUuid = serverUuid
        };
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ValidateContext(expectation));
    }

    [Fact]
    public void Local_policy_rejects_missing_or_nonallowlisted_approval_and_unexpected_migration_set()
    {
        using var fixture = new EvidenceFixture();
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ValidateContext(
            fixture.Expectation with { ApprovalReference = "" }));
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ValidateContext(
            fixture.Expectation with { ApprovalReference = "UNAPPROVED" }));
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ValidateContext(
            fixture.Expectation with { PendingMigrations = [LocalDevelopmentRecoveryPolicy.RecoveryMigration, "other"] }));
    }

    [Fact]
    public void Local_policy_rejects_wrong_tenant_id_provider_shard_catalog_or_machine()
    {
        using var fixture = new EvidenceFixture();
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ValidateContext(
            fixture.Expectation with { TenantId = Guid.NewGuid() }));
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ValidateContext(
            fixture.Expectation with { Provider = "SqlServer" }));
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ValidateContext(
            fixture.Expectation with { Shard = "other" }));
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ValidateContext(
            fixture.Expectation with { Catalog = "other_catalog" }));
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ValidateContext(
            fixture.Expectation with { MachineName = "OTHER-MACHINE" }));
    }

    [Theory]
    [InlineData("Disabled")]
    [InlineData("Preferred")]
    [InlineData("Required")]
    public void Local_policy_rejects_unverified_or_optional_tls(string tlsMode)
    {
        using var fixture = new EvidenceFixture();
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ValidateContext(
            fixture.Expectation with { TlsMode = tlsMode }));
    }

    [Fact]
    public void Local_policy_rejects_backup_roots_with_inherited_windows_permissions()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<InvalidOperationException>(() => VerifyInheritedRootIsRejected());
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyInheritedRootIsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "hrms-local-policy-acl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { LocalDevelopmentRecoveryPolicy.VerifyRestrictedBackupRoot(root); }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Local_policy_requires_present_signed_evidence_and_bound_operator_retention_and_approval()
    {
        using var fixture = new EvidenceFixture();
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ReadAndValidateEvidence(
            Path.Combine(fixture.Root, "missing.json"), fixture.PublicKey, fixture.Expectation));

        foreach (var invalid in new[]
        {
            fixture.Payload with { OperatorIdentity = "someone-else" },
            fixture.Payload with { ApprovalReference = "UNAPPROVED" },
            fixture.Payload with { RetentionPolicyReference = "OTHER-POLICY" },
            fixture.Payload with { RestoreTestedUtc = fixture.Now.AddHours(-25) }
        })
        {
            fixture.WriteEvidence(invalid);
            Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ReadAndValidateEvidence(
                fixture.EvidencePath, fixture.PublicKey, fixture.Expectation));
        }
    }

    [Fact]
    public void Local_policy_rejects_unsigned_tampered_or_wrong_artifact_evidence()
    {
        using var fixture = new EvidenceFixture();
        var bytes = File.ReadAllBytes(fixture.EvidencePath);
        bytes[^1] ^= 1;
        File.WriteAllBytes(fixture.EvidencePath, bytes);
        Assert.ThrowsAny<Exception>(() => LocalDevelopmentRecoveryPolicy.ReadAndValidateEvidence(
            fixture.EvidencePath, fixture.PublicKey, fixture.Expectation));
    }

    [Theory]
    [InlineData(false, true, "hrms_restore_test", "mysql-restore-instance", "AES-256-GCM", "Windows-DPAPI-CurrentUser")]
    [InlineData(true, false, "hrms_restore_test", "mysql-restore-instance", "AES-256-GCM", "Windows-DPAPI-CurrentUser")]
    [InlineData(true, true, "hrms_web1", "mysql-restore-instance", "AES-256-GCM", "Windows-DPAPI-CurrentUser")]
    [InlineData(true, true, "hrms_catalog", "mysql-restore-instance", "AES-256-GCM", "Windows-DPAPI-CurrentUser")]
    [InlineData(true, true, "hrms_restore_test", "mysql-restore-instance", "AES-256-CBC", "Windows-DPAPI-CurrentUser")]
    [InlineData(true, true, "hrms_restore_test", "mysql-restore-instance", "AES-256-GCM", "plaintext-key")]
    public void Local_policy_rejects_failed_restore_or_unapproved_crypto_evidence(
        bool decryptPassed, bool restorePassed, string restoreDatabase, string restoreServerUuid, string algorithm, string keyProtection)
    {
        using var fixture = new EvidenceFixture();
        fixture.WriteEvidence(fixture.Payload with
        {
            AuthenticatedDecryptionPassed = decryptPassed,
            IsolatedRestorePassed = restorePassed,
            RestoreDatabase = restoreDatabase,
            RestoreServerUuid = restoreServerUuid,
            EncryptionAlgorithm = algorithm,
            KeyProtection = keyProtection
        });
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ReadAndValidateEvidence(
            fixture.EvidencePath, fixture.PublicKey, fixture.Expectation));
    }

    [Fact]
    public void Local_policy_rejects_stale_evidence_and_artifact_hash_mismatch()
    {
        using var fixture = new EvidenceFixture();
        fixture.WriteEvidence(fixture.Payload with { BackupCreatedUtc = fixture.Now.AddHours(-25) });
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ReadAndValidateEvidence(
            fixture.EvidencePath, fixture.PublicKey, fixture.Expectation));

        fixture.WriteEvidence(fixture.Payload);
        File.AppendAllText(fixture.ArtifactPath, "tamper");
        Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ReadAndValidateEvidence(
            fixture.EvidencePath, fixture.PublicKey, fixture.Expectation));
    }

    [Fact]
    public void Local_policy_requires_verified_schema_migration_and_sanitized_import_aggregates()
    {
        using var fixture = new EvidenceFixture();
        foreach (var invalid in new[]
        {
            fixture.Payload with { RestoreTableCount = 298 },
            fixture.Payload with { RestoreSchemaSha256 = new string('0', 64) },
            fixture.Payload with { RestoreAppliedMigrations = [] },
            fixture.Payload with { ImportOperationId = Guid.NewGuid().ToString() },
            fixture.Payload with { RestoreImportPendingRows = 7154 },
            fixture.Payload with { SourceTlsMode = "Required" },
            fixture.Payload with { RestoreEndpointHost = "localhost" },
            fixture.Payload with { RestoreServerUuid = "mysql-local-instance" },
            fixture.Payload with { RestoreDatabase = "hrms_other_tenant" },
            fixture.Payload with { RestoreAllBaseTablesInnoDb = false },
            fixture.Payload with { ImportLeaseStatus = "active" }
        })
        {
            fixture.WriteEvidence(invalid);
            Assert.Throws<InvalidOperationException>(() => LocalDevelopmentRecoveryPolicy.ReadAndValidateEvidence(
                fixture.EvidencePath, fixture.PublicKey, fixture.Expectation));
        }
    }

    private sealed class EvidenceFixture : IDisposable
    {
        private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "hrms-local-recovery-policy-" + Guid.NewGuid().ToString("N"));
        public string ArtifactPath => Path.Combine(Root, "backup.hreb");
        public string EvidencePath => Path.Combine(Root, "evidence.json");
        public string PublicKey => _signer.ExportSubjectPublicKeyInfoPem();
        public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
        public LocalRecoveryPolicyExpectation Expectation { get; }
        public LocalRecoveryEvidencePayload Payload { get; }

        public EvidenceFixture()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllBytes(ArtifactPath, [1, 2, 3, 4, 5]);
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ArtifactPath)));
            Expectation = new("Development", Environment.MachineName, Environment.MachineName, "127.0.0.1", "VerifyCA",
                "mysql-local-instance", "mysql-local-instance", LocalDevelopmentRecoveryPolicy.ExpectedTenantId,
                "WEB1", "web1", "hrms_catalog", "hrms_web1", "MySql",
                [LocalDevelopmentRecoveryPolicy.RecoveryMigration], "LOCAL-APPROVAL-1", "MIGRATION-APPROVAL-1",
                "backup-test-1", "restore-test-1", "test-operator",
                new HashSet<string>(["LOCAL-APPROVAL-1"], StringComparer.Ordinal), Root, 30,
                "LOCAL-WEB1-30DAY-V1", Now);
            Payload = new(2, LocalDevelopmentRecoveryPolicy.PolicyId, "backup-test-1", "Development", Environment.MachineName,
                LocalDevelopmentRecoveryPolicy.ExpectedTenantId, "WEB1", "web1", "hrms_catalog", "hrms_web1", "MySql",
                "mysql-local-instance", ArtifactPath, sha, "AES-256-GCM", "Windows-DPAPI-CurrentUser", true,
                Now.AddMinutes(-20), Now.AddMinutes(-20).AddDays(30), "hrms_restore_test", "mysql-restore-instance", true,
                Now.AddMinutes(-10), "restore-test-1", [LocalDevelopmentRecoveryPolicy.RecoveryMigration], "test-operator",
                "LOCAL-APPROVAL-1", "MIGRATION-APPROVAL-1", "LOCAL-WEB1-30DAY-V1")
            {
                SourceTableCount = 299,
                RestoreTableCount = 299,
                SchemaSha256 = Convert.ToHexString(SHA256.HashData("synthetic-schema"u8.ToArray())),
                RestoreSchemaSha256 = Convert.ToHexString(SHA256.HashData("synthetic-schema"u8.ToArray())),
                MigrationHistorySha256 = Convert.ToHexString(SHA256.HashData("synthetic-migration-history"u8.ToArray())),
                RestoreMigrationHistorySha256 = Convert.ToHexString(SHA256.HashData("synthetic-migration-history"u8.ToArray())),
                AppliedMigrations = ["synthetic-migration-001", "synthetic-migration-002"],
                RestoreAppliedMigrations = ["synthetic-migration-001", "synthetic-migration-002"],
                ImportOperationId = "b89d04aa-1419-4c85-a7e3-48ab1d92d111",
                ImportState = "Processing",
                ImportTotalRows = 10_000,
                ImportSucceededRows = 0,
                ImportFailedRows = 1,
                ImportSkippedRows = 2_844,
                ImportPendingRows = 7_155,
                RestoreImportState = "Processing",
                RestoreImportTotalRows = 10_000,
                RestoreImportSucceededRows = 0,
                RestoreImportFailedRows = 1,
                RestoreImportSkippedRows = 2_844,
                RestoreImportPendingRows = 7_155,
                SourceEndpointHost = "127.0.0.1",
                SourcePort = 3306,
                SourceTlsMode = "VerifyCA",
                RestoreEndpointHost = "127.0.0.1",
                RestorePort = 3306,
                RestoreTlsMode = "VerifyCA",
                AllBaseTablesInnoDb = true,
                RestoreAllBaseTablesInnoDb = true,
                ImportLeaseStatus = "expired",
                RestoreImportLeaseStatus = "expired"
            };
            WriteEvidence(Payload);
        }

        public void WriteEvidence(LocalRecoveryEvidencePayload payload)
            => File.WriteAllBytes(EvidencePath, LocalDevelopmentRecoveryPolicy.SerializeAndSignEvidence(payload, _signer));

        public void Dispose()
        {
            _signer.Dispose();
            try { Directory.Delete(Root, recursive: true); }
            catch (DirectoryNotFoundException) { }
        }
    }
}
