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

    [Fact]
    public void CI_required_mode_fails_closed_instead_of_allowing_provider_acceptance_to_skip()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EmployeeImportMySqlAcceptanceEnvironment.CiRequired), "true", StringComparison.OrdinalIgnoreCase))
            return;

        _ = EmployeeImportMySqlAcceptanceConfiguration.Read(EmployeeImportMySqlAcceptanceEnvironment.Snapshot());
    }

    [Fact]
    public void Failure_diagnostics_are_structured_and_do_not_copy_exception_messages()
    {
        const string secret = "Password=synthetic-secret-must-not-appear";
        var diagnostic = EmployeeImportMySqlFailureDiagnostics.Create(
            "catalog migration", new InvalidOperationException(secret), "catalog_migration_sql");
        var json = System.Text.Json.JsonSerializer.Serialize(diagnostic);

        Assert.Equal(EmployeeImportMySqlFailureDiagnostics.ProviderAcceptanceTestName, diagnostic.TestName);
        Assert.Equal("catalog migration", diagnostic.Stage);
        Assert.Equal("Migration", diagnostic.Category);
        Assert.Equal("INVALID_OPERATION", diagnostic.ErrorCode);
        Assert.Equal("catalog_migration_sql", diagnostic.FailingCheckpoint);
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic-secret", json, StringComparison.Ordinal);
        Assert.Contains("private test log", diagnostic.Remediation, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("synthetic_tenant_creation")]
    [InlineData("catalog_tenant_insert")]
    [InlineData("shard_seeding")]
    [InlineData("employee_code_configuration")]
    [InlineData("template_creation")]
    [InlineData("workbook_preparation")]
    [InlineData("http_confirmation")]
    [InlineData("queued_state_verification")]
    [InlineData("worker_startup")]
    [InlineData("row2_commit")]
    [InlineData("row3_pause")]
    [InlineData("host_shutdown")]
    [InlineData("cancellation")]
    [InlineData("lease_retention")]
    [InlineData("recovery_claim")]
    [InlineData("final_completion")]
    public async Task Every_acceptance_checkpoint_preserves_a_nonempty_failure_identifier(string checkpoint)
    {
        var resultPath = Path.Combine(Path.GetTempPath(), $"hrms-f1-checkpoint-{Guid.NewGuid():N}.json");
        try
        {
            var tracker = new EmployeeImportMySqlAcceptanceRunner.AcceptanceCheckpointTracker(resultPath, "run-test", []);
            await Assert.ThrowsAsync<InvalidOperationException>(() => tracker.RunAsync(checkpoint,
                () => throw new InvalidOperationException("secret employee detail")));

            Assert.Equal(checkpoint, tracker.ActiveCheckpoint);
            Assert.False(string.IsNullOrWhiteSpace(tracker.ActiveCheckpoint));
        }
        finally
        {
            if (File.Exists(resultPath)) File.Delete(resultPath);
        }
    }

    [Fact]
    public void Login_path_mode_requires_an_exclusive_administrator_source()
    {
        var values = ValidValues();
        values[EmployeeImportMySqlAcceptanceEnvironment.Connection] =
            "Server=127.0.0.1;Port=13307;Database=mysql;User ID=f1_admin;Pooling=false";
        values[EmployeeImportMySqlAcceptanceEnvironment.ApplicationConnection] =
            "Server=127.0.0.1;Port=13307;Database=mysql;User ID=hrms_f1_app;Pooling=false";
        values[EmployeeImportMySqlAcceptanceEnvironment.LoginPath] = "hrms_f1_admin";
        values[EmployeeImportMySqlAcceptanceEnvironment.ConfigEditor] = "mysql_config_editor.exe";

        var accepted = EmployeeImportMySqlAcceptanceConfiguration.Read(values);

        Assert.Equal("hrms_f1_admin", accepted.AdminLoginPath);
        Assert.DoesNotContain("Password", accepted.ConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("mysql_config_editor.exe", accepted.MySqlConfigEditorPath);
    }

    [Fact]
    public void Login_path_mode_rejects_persistent_password_fallback()
    {
        var values = ValidValues();
        values[EmployeeImportMySqlAcceptanceEnvironment.LoginPath] = "hrms_f1_admin";

        var error = Assert.Throws<InvalidOperationException>(() =>
            EmployeeImportMySqlAcceptanceConfiguration.Read(values));

        Assert.Contains("password-free", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Login_path_output_requires_complete_approved_loopback_credentials()
    {
        var missing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = "root", ["host"] = "127.0.0.1", ["port"] = "3306"
        };
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMySqlCredentialAdapter.BuildConnectionString(missing, 3306));

        var wrongHost = new Dictionary<string, string>(missing) { ["password"] = "secret", ["host"] = "localhost" };
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMySqlCredentialAdapter.BuildConnectionString(wrongHost, 3306));
    }

    [Fact]
    public void Masked_login_path_output_is_rejected_before_provider_connection_creation()
    {
        var error = Assert.Throws<InvalidOperationException>(() => EmployeeImportMySqlCredentialAdapter.ParseOutput(
            "[hrms_f1_admin]\nuser = \"root\"\npassword = *****\nhost = \"127.0.0.1\"\nport = 3306\n"));

        Assert.Contains("masked", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Administrator_connection_rejects_restricted_application_account()
    {
        var values = ValidValues();
        values[EmployeeImportMySqlAcceptanceEnvironment.Connection] =
            "Server=127.0.0.1;Port=13307;Database=mysql;User ID=hrms_f1_app;Password=test;Pooling=false";

        var error = Assert.Throws<InvalidOperationException>(() =>
            EmployeeImportMySqlAcceptanceConfiguration.Read(values));

        Assert.Contains("administrator", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(EmployeeImportMySqlAcceptanceEnvironment.ApproveConnection)]
    [InlineData(EmployeeImportMySqlAcceptanceEnvironment.ApproveCreate)]
    [InlineData(EmployeeImportMySqlAcceptanceEnvironment.ApproveMigration)]
    [InlineData(EmployeeImportMySqlAcceptanceEnvironment.ApproveWorker)]
    public void Every_mutating_or_connecting_action_requires_its_own_approval(string approvalName)
    {
        var values = ValidValues();
        values[approvalName] = "false";
        var error = Assert.Throws<InvalidOperationException>(() => EmployeeImportMySqlAcceptanceConfiguration.Read(values));
        Assert.Contains(approvalName, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_configuration_does_not_require_delete_approval_when_run_does_not_delete_resources()
    {
        var values = ValidValues();
        values[EmployeeImportMySqlAcceptanceEnvironment.ApproveDelete] = "false";
        var accepted = EmployeeImportMySqlAcceptanceConfiguration.Read(values);
        Assert.Equal(EmployeeImportDatabaseMode.CreateNew, accepted.DatabaseMode);
    }

    [Fact]
    public void S3_storage_requires_its_own_restart_approval()
    {
        var values = ValidValues();
        values[EmployeeImportMySqlAcceptanceEnvironment.ApproveMinioRestart] = "false";
        var error = Assert.Throws<InvalidOperationException>(() => EmployeeImportMySqlAcceptanceConfiguration.Read(values));
        Assert.Contains(EmployeeImportMySqlAcceptanceEnvironment.ApproveMinioRestart, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Filesystem_existing_loopback_mode_requires_explicit_opt_ins_and_preserves_run_root()
    {
        var values = ValidValues();
        var root = Path.Combine(Path.GetTempPath(), "hrms-employee-import-f1", "run202610280", "workbooks");
        values[EmployeeImportMySqlAcceptanceEnvironment.MySqlMode] = "ExistingLoopback";
        values[EmployeeImportMySqlAcceptanceEnvironment.ServerIdentity] = "local-mysql-host";
        values[EmployeeImportMySqlAcceptanceEnvironment.HostPort] = "3306";
        values[EmployeeImportMySqlAcceptanceEnvironment.Connection] = "Server=127.0.0.1;Port=3306;Database=mysql;User ID=f1_admin;Password=test-only;Pooling=false";
        values[EmployeeImportMySqlAcceptanceEnvironment.ApplicationConnection] = "Server=127.0.0.1;Port=3306;Database=mysql;User ID=hrms_f1_app;Password=test-only;Pooling=false";
        values[EmployeeImportMySqlAcceptanceEnvironment.StorageMode] = "FileSystem";
        values[EmployeeImportMySqlAcceptanceEnvironment.FileSystemRoot] = root;
        values.Remove(EmployeeImportMySqlAcceptanceEnvironment.MinioPort);
        values[EmployeeImportMySqlAcceptanceEnvironment.ApproveExistingLoopback] = "true";
        values[EmployeeImportMySqlAcceptanceEnvironment.ApproveFileSystemStorage] = "true";

        var accepted = EmployeeImportMySqlAcceptanceConfiguration.Read(values);

        Assert.Equal(EmployeeImportMySqlMode.ExistingLoopback, accepted.MySqlMode);
        Assert.Equal(EmployeeImportStorageMode.FileSystem, accepted.StorageMode);
        Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(accepted.FileSystemRoot!));
    }

    [Fact]
    public void Filesystem_root_must_contain_the_exact_run_directory()
    {
        var values = ValidValues();
        values[EmployeeImportMySqlAcceptanceEnvironment.StorageMode] = "FileSystem";
        values[EmployeeImportMySqlAcceptanceEnvironment.ApproveFileSystemStorage] = "true";
        values[EmployeeImportMySqlAcceptanceEnvironment.FileSystemRoot] =
            Path.Combine(Path.GetTempPath(), "hrms-employee-import-f1", "workbooks");

        var error = Assert.Throws<InvalidOperationException>(() =>
            EmployeeImportMySqlAcceptanceConfiguration.Read(values));

        Assert.Contains("run", error.Message, StringComparison.OrdinalIgnoreCase);
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
            [EmployeeImportMySqlAcceptanceEnvironment.ApplicationConnection] = "Server=127.0.0.1;Port=13307;Database=mysql;User ID=hrms_f1_app;Password=test-only;Pooling=false",
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
        catch (Exception) when (!string.Equals(
            Environment.GetEnvironmentVariable(EmployeeImportMySqlAcceptanceEnvironment.CiRequired),
            "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Employee-import MySQL/MinIO acceptance is safely skipped until its isolated configuration and all explicit approvals are present.";
        }
    }
}

