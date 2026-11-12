using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

[Collection("Provider acceptance environment")]
public sealed class EmployeeImportMySqlAcceptanceGateTests
{
    [Fact]
    public void Disabled_harness_fails_closed_before_parsing_connection_or_touching_resources()
    {
        var values = ValidValues();
        values[EmployeeImportMySqlAcceptanceEnvironment.Execute] = "false";
        values[EmployeeImportMySqlAcceptanceEnvironment.Connection] = "not a connection string";
        var error = Assert.Throws<InvalidOperationException>(() => EmployeeImportMySqlAcceptanceConfiguration.Read(values));
        Assert.Contains("disabled", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Adopt_mode_requires_approval_and_stops_before_any_file_or_provider_mutation()
    {
        var values = ValidValues();
        values[EmployeeImportMySqlAcceptanceEnvironment.DatabaseMode] = "AdoptExistingEmpty";
        Assert.Contains("separate explicit approval", Assert.Throws<InvalidOperationException>(
            () => EmployeeImportMySqlAcceptanceConfiguration.Read(values)).Message);
        values[EmployeeImportMySqlAcceptanceEnvironment.ApproveAdoption] = "true";
        var attempts = 0;
        var runner = new EmployeeImportMySqlAcceptanceRunner(() => attempts++);
        await Assert.ThrowsAsync<NotImplementedException>(() => runner.RunAsync(values));
        Assert.Equal(0, attempts);
        Assert.False(File.Exists(values[EmployeeImportMySqlAcceptanceEnvironment.ManifestPath]));
    }

    [Fact]
    public void Omitted_database_mode_preserves_CreateNew_default()
    {
        Assert.Equal(EmployeeImportDatabaseMode.CreateNew,
            EmployeeImportMySqlAcceptanceConfiguration.Read(ValidValues()).DatabaseMode);
    }

    [Fact]
    public async Task Runner_never_reaches_connection_boundary_when_gate_rejects_configuration()
    {
        var values = ValidValues();
        values[EmployeeImportMySqlAcceptanceEnvironment.ApproveConnection] = "false";
        var connectionAttempts = 0;
        var runner = new EmployeeImportMySqlAcceptanceRunner(() => connectionAttempts++);
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(values));
        Assert.Equal(0, connectionAttempts);
        Assert.False(File.Exists(values[EmployeeImportMySqlAcceptanceEnvironment.ManifestPath]));
    }

    [Fact]
    public void Dedicated_configuration_does_not_read_legacy_or_existing_tenant_connection_settings()
    {
        var snapshot = EmployeeImportMySqlAcceptanceEnvironment.Snapshot();
        Assert.DoesNotContain("HRMS_PHASE1E_MYSQL_CONNECTION", snapshot.Keys);
        Assert.DoesNotContain("ConnectionStrings:Catalog", snapshot.Keys);
        Assert.DoesNotContain("Sharding:MySqlConnectionStringTemplate", snapshot.Keys);
    }

    [Theory]
    [InlineData(EmployeeImportMySqlAcceptanceEnvironment.ApproveConnection)]
    [InlineData(EmployeeImportMySqlAcceptanceEnvironment.ApproveCreate)]
    [InlineData(EmployeeImportMySqlAcceptanceEnvironment.ApproveMigration)]
    [InlineData(EmployeeImportMySqlAcceptanceEnvironment.ApproveWorker)]
    [InlineData(EmployeeImportMySqlAcceptanceEnvironment.ApproveDelete)]
    [InlineData(EmployeeImportMySqlAcceptanceEnvironment.ApproveMinioRestart)]
    public void Every_mutating_or_connecting_action_requires_its_own_approval(string approvalName)
    {
        var values = ValidValues();
        values[approvalName] = "false";
        var error = Assert.Throws<InvalidOperationException>(() => EmployeeImportMySqlAcceptanceConfiguration.Read(values));
        Assert.Contains(approvalName, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hrms_catalog")]
    [InlineData("hrms_web1")]
    [InlineData("web1_archive")]
    [InlineData("mysql")]
    public void Protected_or_ambiguous_database_names_are_rejected(string forbidden)
    {
        var values = ValidValues();
        values[EmployeeImportMySqlAcceptanceEnvironment.CatalogDatabase] = forbidden;
        values[EmployeeImportMySqlAcceptanceEnvironment.DatabaseAllowlist] =
            $"{forbidden},hrms_ei1_run_20261028_shard,hrms_ei1_run_20261028_down";
        var error = Assert.Throws<InvalidOperationException>(() => EmployeeImportMySqlAcceptanceConfiguration.Read(values));
        Assert.Contains("Protected", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("mysql.example.invalid", "13307")]
    [InlineData("127.0.0.1", "3306")]
    public void Non_loopback_or_mismatched_mysql_endpoint_is_rejected(string host, string port)
    {
        var values = ValidValues();
        values[EmployeeImportMySqlAcceptanceEnvironment.HostPort] = "13307";
        values[EmployeeImportMySqlAcceptanceEnvironment.Connection] = $"Server={host};Port={port};Database=mysql;User ID=f1;Password=synthetic-test-only;Pooling=false";
        var error = Assert.Throws<InvalidOperationException>(() => EmployeeImportMySqlAcceptanceConfiguration.Read(values));
        Assert.Contains("loopback", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wrong_default_database_and_unpooled_guard_are_rejected_without_connection()
    {
        var wrongDatabase = ValidValues();
        wrongDatabase[EmployeeImportMySqlAcceptanceEnvironment.Connection] = "Server=127.0.0.1;Port=13307;Database=hrms_catalog;User ID=f1;Password=test;Pooling=false";
        Assert.Contains("system database", Assert.Throws<InvalidOperationException>(() => EmployeeImportMySqlAcceptanceConfiguration.Read(wrongDatabase)).Message);

        var pooled = ValidValues();
        pooled[EmployeeImportMySqlAcceptanceEnvironment.Connection] = "Server=127.0.0.1;Port=13307;Database=mysql;User ID=f1;Password=test;Pooling=true";
        Assert.Contains("pooling", Assert.Throws<InvalidOperationException>(() => EmployeeImportMySqlAcceptanceConfiguration.Read(pooled)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Allowlist_must_match_exactly_three_unique_run_owned_database_names()
    {
        var values = ValidValues();
        values[EmployeeImportMySqlAcceptanceEnvironment.DatabaseAllowlist] =
            "hrms_ei1_run202610280_meta,hrms_ei1_run202610280_meta,hrms_ei1_run202610280_down";
        Assert.Contains("allowlist", Assert.Throws<InvalidOperationException>(() => EmployeeImportMySqlAcceptanceConfiguration.Read(values)).Message);
    }

    [Fact]
    public void Tenant_database_name_is_lowercase_and_safe_for_the_production_shard_router()
    {
        var accepted = EmployeeImportMySqlAcceptanceConfiguration.Read(ValidValues());
        Assert.Equal(accepted.TenantDatabase.ToLowerInvariant(), accepted.TenantDatabase);
        Assert.DoesNotContain(accepted.TenantDatabase, character => char.IsUpper(character));

        var uppercase = ValidValues();
        uppercase[EmployeeImportMySqlAcceptanceEnvironment.TenantDatabase] = accepted.TenantDatabase.ToUpperInvariant();
        uppercase[EmployeeImportMySqlAcceptanceEnvironment.DatabaseAllowlist] =
            $"{accepted.CatalogDatabase},{accepted.TenantDatabase.ToUpperInvariant()},{accepted.MigrationDownDatabase}";
        Assert.Contains("run-specific prefix", Assert.Throws<InvalidOperationException>(
            () => EmployeeImportMySqlAcceptanceConfiguration.Read(uppercase)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Credentials_are_not_included_in_configuration_validation_errors()
    {
        var values = ValidValues();
        values[EmployeeImportMySqlAcceptanceEnvironment.Connection] = "not valid; Password=do-not-leak";
        var error = Assert.Throws<InvalidOperationException>(() => EmployeeImportMySqlAcceptanceConfiguration.Read(values));
        Assert.DoesNotContain("do-not-leak", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cleanup_manifest_requires_delete_approval_exact_names_and_matching_database_markers()
    {
        var values = ValidValues();
        var approved = EmployeeImportMySqlAcceptanceConfiguration.Read(values);
        var databases = new[] { approved.CatalogDatabase, approved.TenantDatabase, approved.MigrationDownDatabase }
            .Select(name => new EmployeeImportOwnedDatabase(name, true, true,
                $"HRMS-EMPLOYEE-IMPORT-F1|{approved.ServerIdentity}|{approved.RunId}|{name}")).ToArray();
        var manifest = new EmployeeImportMySqlOwnershipManifest(1, approved.RunId, approved.ServerIdentity,
            approved.HostPort, approved.ServerPort, databases, DateTimeOffset.UtcNow);
        var markers = databases.ToDictionary(x => x.Name, x => x.OwnershipMarker, StringComparer.Ordinal);

        Assert.Throws<InvalidOperationException>(() => manifest.ValidateCleanup(approved, false, markers));
        manifest.ValidateCleanup(approved, true, markers);
        markers[approved.TenantDatabase] = "foreign-owner";
        Assert.Throws<InvalidOperationException>(() => manifest.ValidateCleanup(approved, true, markers));
    }

    private static Dictionary<string, string?> ValidValues()
    {
        var root = Path.Combine(Path.GetTempPath(), "hrms-employee-import-f1-guard-tests", Guid.NewGuid().ToString("N"));
        const string run = "run202610280";
        var catalog = $"hrms_ei1_{run}_meta";
        var tenant = $"hrms_ei1_{run}_shard";
        var down = $"hrms_ei1_{run}_down";
        return new(StringComparer.Ordinal)
        {
            [EmployeeImportMySqlAcceptanceEnvironment.Execute] = "true",
            [EmployeeImportMySqlAcceptanceEnvironment.Environment] = "isolated-employee-import-f1",
            [EmployeeImportMySqlAcceptanceEnvironment.RunId] = run,
            [EmployeeImportMySqlAcceptanceEnvironment.HostPort] = "13307",
            [EmployeeImportMySqlAcceptanceEnvironment.ServerPort] = "3306",
            [EmployeeImportMySqlAcceptanceEnvironment.ServerIdentity] = "hrmsf1mysqlrun202610280",
            [EmployeeImportMySqlAcceptanceEnvironment.Connection] = "Server=127.0.0.1;Port=13307;Database=mysql;User ID=f1;Password=test-only;Pooling=false",
            [EmployeeImportMySqlAcceptanceEnvironment.CatalogDatabase] = catalog,
            [EmployeeImportMySqlAcceptanceEnvironment.TenantDatabase] = tenant,
            [EmployeeImportMySqlAcceptanceEnvironment.MigrationDownDatabase] = down,
            [EmployeeImportMySqlAcceptanceEnvironment.DatabaseAllowlist] = $"{catalog},{tenant},{down}",
            [EmployeeImportMySqlAcceptanceEnvironment.ManifestPath] = Path.Combine(root, "owner.json"),
            [EmployeeImportMySqlAcceptanceEnvironment.ResultPath] = Path.Combine(root, "result.json"),
            [EmployeeImportMySqlAcceptanceEnvironment.StorageConfiguration] = Path.Combine(root, "s3-test-secrets.json"),
            [EmployeeImportMySqlAcceptanceEnvironment.MinioPort] = "19000",
            [EmployeeImportMySqlAcceptanceEnvironment.ComposeProject] = $"hrms-f1-{run}",
            [EmployeeImportMySqlAcceptanceEnvironment.ComposeFile] = Path.Combine(root, "docker-compose.f1.yml"),
            [EmployeeImportMySqlAcceptanceEnvironment.ApproveConnection] = "true",
            [EmployeeImportMySqlAcceptanceEnvironment.ApproveCreate] = "true",
            [EmployeeImportMySqlAcceptanceEnvironment.ApproveMigration] = "true",
            [EmployeeImportMySqlAcceptanceEnvironment.ApproveWorker] = "true",
            [EmployeeImportMySqlAcceptanceEnvironment.ApproveDelete] = "true",
            [EmployeeImportMySqlAcceptanceEnvironment.ApproveMinioRestart] = "true"
        };
    }
}

/// <summary>Provider acceptance entry points are skipped unless every explicit F1 gate is present.</summary>
public sealed class EmployeeImportMySqlProviderAcceptanceEntryPoint
{
    [EmployeeImportMySqlProviderFact]
    [Trait("Category", "EmployeeImportMySqlAcceptance")]
    public Task MySql_schema_and_minio_provider_acceptance() => new EmployeeImportMySqlAcceptanceRunner().RunAsync();

    [EmployeeImportMySqlProviderFact(allowExistingManifest: true)]
    [Trait("Category", "EmployeeImportMySqlAcceptance")]
    public Task MySql_cleanup_current_owned_resources() => new EmployeeImportMySqlAcceptanceRunner().CleanupAsync();
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class EmployeeImportMySqlProviderFactAttribute : FactAttribute
{
    public EmployeeImportMySqlProviderFactAttribute(bool allowExistingManifest = false)
    {
        try { _ = EmployeeImportMySqlAcceptanceConfiguration.Read(EmployeeImportMySqlAcceptanceEnvironment.Snapshot(), allowExistingManifest); }
        catch (Exception) { Skip = "Employee-import MySQL/MinIO acceptance is safely skipped until its isolated configuration and all explicit approvals are present."; }
    }
}
