namespace HRMS.Tests;

public sealed class EmployeeImportMigrationSafetyTests
{
    private static readonly Guid TenantId = Guid.Parse("16313e68-2e72-4a6a-b689-79b271cec295");
    private static readonly EmployeeImportMigrationExpectation Expected = new(TenantId, "127.0.0.1", 3306, "mysql-host", "server-uuid");

    [Fact]
    public void Employee_import_plan_is_separate_and_requires_exact_ordered_migration_ids()
    {
        Assert.Equal("employee-import-web1", EmployeeImportMigrationPlan.Name);
        Assert.Equal(
            [
                "20261028043743_AddEmployeeImportPersistence",
                "20261028052420_AddEmployeeImportClaimLease"
            ],
            EmployeeImportMigrationPlan.OrderedMigrationIds);
        Assert.Contains("20261013132338_AddWelcomeInvitationCenter", MigrationHost.TenantOnlyApprovedMigrations);
        Assert.Contains("20261013133934_AddWelcomeInvitationOutboxProcessing", MigrationHost.TenantOnlyApprovedMigrations);
    }

    [Fact]
    public void Employee_import_plan_arguments_preserve_approval_order_and_require_explicit_selection()
    {
        var args = Arguments.Parse([
            "migrate", "--tenant-only", "--tenant-plan", "employee-import-web1",
            "--approved-migrations", "20261028043743_AddEmployeeImportPersistence,20261028052420_AddEmployeeImportClaimLease",
            "--expected-endpoint-host", "127.0.0.1", "--expected-endpoint-port", "13307",
            "--expected-server-hostname", "mysql-host", "--expected-server-uuid", "server-uuid",
            "--backup-reference", "backup-record", "--restore-test-reference", "restore-record",
            "--maintenance-window", "--workers-paused"]);

        Assert.True(args.TenantOnly);
        Assert.Equal(EmployeeImportMigrationPlan.Name, args.TenantPlan);
        Assert.Equal(EmployeeImportMigrationPlan.OrderedMigrationIds, args.ApprovedMigrationOrder);
        Assert.Equal("127.0.0.1", args.ExpectedEndpointHost);
        Assert.Equal("13307", args.ExpectedEndpointPort);
        Assert.Equal("mysql-host", args.ExpectedServerHostname);
        Assert.Equal("server-uuid", args.ExpectedServerUuid);
        Assert.Equal("backup-record", args.BackupReference);
        Assert.Equal("restore-record", args.RestoreTestReference);
        Assert.True(args.MaintenanceWindow);
        Assert.True(args.WorkersPaused);
        Assert.Null(Arguments.Parse(["migrate", "--tenant-only"]).TenantPlan);
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidatePlanSelection(
            tenantOnly: false, EmployeeImportMigrationPlan.Name, EmployeeImportMigrationPlan.OrderedMigrationIds));
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidatePlanSelection(
            tenantOnly: false, tenantPlan: null, EmployeeImportMigrationPlan.OrderedMigrationIds));
        EmployeeImportMigrationGuard.ValidatePlanSelection(
            tenantOnly: true, EmployeeImportMigrationPlan.Name, EmployeeImportMigrationPlan.OrderedMigrationIds);
    }

    [Fact]
    public void Complete_WEB1_baseline_is_accepted()
        => EmployeeImportMigrationGuard.Validate(ValidEvidence(), Expected);

    [Theory]
    [InlineData("id")]
    [InlineData("code")]
    [InlineData("host")]
    [InlineData("shard")]
    [InlineData("provider")]
    [InlineData("status")]
    public void Catalog_route_identity_mismatches_fail_closed(string field)
    {
        var route = ValidEvidence().Route;
        route = field switch
        {
            "id" => route with { Id = Guid.NewGuid() },
            "code" => route with { TenantCode = "OTHER" },
            "host" => route with { Host = "other.localhost" },
            "shard" => route with { ShardKey = "other" },
            "provider" => route with { Provider = "SqlServer" },
            "status" => route with { Status = 2 },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.Validate(ValidEvidence() with { Route = route }, Expected));
    }

    [Theory]
    [InlineData("database")]
    [InlineData("version")]
    [InlineData("hostname")]
    [InlineData("port")]
    [InlineData("uuid")]
    [InlineData("configured-host")]
    [InlineData("configured-port")]
    public void Runtime_or_configured_endpoint_identity_mismatches_fail_closed(string field)
    {
        var evidence = ValidEvidence();
        evidence = field switch
        {
            "database" => evidence with { TenantRuntime = evidence.TenantRuntime with { Database = "other_db" } },
            "version" => evidence with { TenantRuntime = evidence.TenantRuntime with { Version = "8.0.45" } },
            "hostname" => evidence with { TenantRuntime = evidence.TenantRuntime with { Hostname = "other-host" } },
            "port" => evidence with { TenantRuntime = evidence.TenantRuntime with { Port = 3307 } },
            "uuid" => evidence with { CatalogRuntime = evidence.CatalogRuntime with { ServerUuid = "other-uuid" } },
            "configured-host" => evidence with { TenantConfiguredServer = "other-host" },
            "configured-port" => evidence with { CatalogConfiguredPort = 3307 },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.Validate(evidence, Expected));
    }

    [Fact]
    public void Unknown_applied_migration_fails_history_prefix_validation()
    {
        var known = KnownMigrations();
        var applied = new[] { known[0], "20261099999999_Unknown" };
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidateHistoryPrefix(known, applied));
    }

    [Fact]
    public void Missing_middle_applied_migration_fails_history_prefix_validation()
    {
        var known = KnownMigrations();
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidateHistoryPrefix(known, [known[0], known[2]]));
    }

    [Fact]
    public void Reordered_or_duplicate_approval_ids_fail_closed()
    {
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidateRequestedMigrationOrder(
            EmployeeImportMigrationPlan.OrderedMigrationIds.Reverse().ToArray()));
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidateRequestedMigrationOrder(
            [EmployeeImportMigrationPlan.OrderedMigrationIds[0], EmployeeImportMigrationPlan.OrderedMigrationIds[0]]));
    }

    [Fact]
    public void Release_inventory_requires_both_employee_migrations_and_snapshot_objects()
    {
        const string complete = "20261028043743_AddEmployeeImportPersistence 20261028052420_AddEmployeeImportClaimLease HrmsDbContextModelSnapshot EmployeeImportOperations EmployeeImportRows";
        EmployeeImportMigrationGuard.ValidateReleaseMigrationInventory(complete);

        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidateReleaseMigrationInventory(
            complete.Replace("20261028052420_AddEmployeeImportClaimLease", "", StringComparison.Ordinal)));
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidateReleaseMigrationInventory(
            complete.Replace("HrmsDbContextModelSnapshot", "", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("unrelated")]
    [InlineData("none")]
    [InlineData("first-only")]
    [InlineData("second-only")]
    [InlineData("reversed")]
    public void Unrelated_or_partial_pending_migration_states_fail_closed(string state)
    {
        var first = EmployeeImportMigrationPlan.OrderedMigrationIds[0];
        var second = EmployeeImportMigrationPlan.OrderedMigrationIds[1];
        var pending = state switch
        {
            "unrelated" => new[] { first, second, "20261099999999_Unexpected" },
            "none" => [],
            "first-only" => [first],
            "second-only" => [second],
            "reversed" => [second, first],
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidateExactPendingMigrations(pending));
    }

    [Fact]
    public void Missing_prerequisite_or_existing_employee_object_fails_schema_baseline()
    {
        var baseline = ValidEvidence().Schema;
        var missingUsers = baseline with
        {
            Tables = baseline.Tables.Where(x => x.Key != "users").ToDictionary(x => x.Key, x => x.Value)
        };
        var partialTables = baseline.Tables.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        partialTables["employeeimportoperations"] = "BASE TABLE";
        var partial = baseline with { Tables = partialTables };

        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidateSchemaBaseline(missingUsers));
        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidateSchemaBaseline(partial));
    }

    [Fact]
    public void Existing_import_batch_composite_key_fails_schema_baseline()
    {
        var baseline = ValidEvidence().Schema;
        var withExistingKey = baseline with
        {
            Indexes = baseline.Indexes.Append(new MigrationIndex("importbatches", "AK_ImportBatches_TenantId_Id", true, ["TenantId", "Id"])).ToArray(),
            Constraints = new HashSet<string>(baseline.Constraints) { "importbatches|AK_ImportBatches_TenantId_Id|UNIQUE" }
        };

        Assert.Throws<InvalidOperationException>(() => EmployeeImportMigrationGuard.ValidateSchemaBaseline(withExistingKey));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("history")]
    [InlineData("schema")]
    [InlineData("pending")]
    public async Task Lock_time_revalidation_failure_never_invokes_executor(string failure)
    {
        var inspections = 0;
        var lockAcquired = false;
        var executorInvoked = false;
        var valid = ValidEvidence();
        var invalidAfterLock = failure switch
        {
            "identity" => valid with { TenantRuntime = valid.TenantRuntime with { ServerUuid = "wrong-uuid" } },
            "history" => valid with { AppliedMigrations = ["20261099999999_Unknown"] },
            "schema" => valid with { Schema = valid.Schema with { HistoryTablePresent = false } },
            "pending" => valid with { PendingMigrations = [EmployeeImportMigrationPlan.OrderedMigrationIds[0]] },
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => EmployeeImportMigrationWorkflow.ExecuteAsync(
            _ => Task.FromResult(++inspections == 1 ? valid : invalidAfterLock),
            _ =>
            {
                lockAcquired = true;
                return Task.FromResult<IAsyncDisposable>(new TestLock());
            },
            _ =>
            {
                executorInvoked = true;
                return Task.CompletedTask;
            },
            Expected,
            CancellationToken.None));

        Assert.Equal(2, inspections);
        Assert.True(lockAcquired);
        Assert.False(executorInvoked);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("history")]
    [InlineData("schema")]
    [InlineData("pending")]
    public async Task Initial_preflight_failure_does_not_acquire_lock_or_invoke_executor(string failure)
    {
        var lockAcquired = false;
        var executorInvoked = false;
        var valid = ValidEvidence();
        var invalid = failure switch
        {
            "identity" => valid with { TenantRuntime = new MySqlRuntimeIdentity("wrong_db", "8.0.46", "mysql-host", 3306, "server-uuid") },
            "history" => valid with { AppliedMigrations = ["20261099999999_Unknown"] },
            "schema" => valid with { Schema = valid.Schema with { HistoryTablePresent = false } },
            "pending" => valid with { PendingMigrations = [EmployeeImportMigrationPlan.OrderedMigrationIds[1]] },
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => EmployeeImportMigrationWorkflow.ExecuteAsync(
            _ => Task.FromResult(invalid),
            _ =>
            {
                lockAcquired = true;
                return Task.FromResult<IAsyncDisposable>(new TestLock());
            },
            _ =>
            {
                executorInvoked = true;
                return Task.CompletedTask;
            },
            Expected,
            CancellationToken.None));

        Assert.False(lockAcquired);
        Assert.False(executorInvoked);
    }

    [Fact]
    public async Task Execution_occurs_only_after_both_preflight_passes()
    {
        var inspections = 0;
        var executorInvoked = false;

        await EmployeeImportMigrationWorkflow.ExecuteAsync(
            _ =>
            {
                inspections++;
                return Task.FromResult(ValidEvidence());
            },
            _ => Task.FromResult<IAsyncDisposable>(new TestLock()),
            _ =>
            {
                Assert.Equal(2, inspections);
                executorInvoked = true;
                return Task.CompletedTask;
            },
            Expected,
            CancellationToken.None);

        Assert.True(executorInvoked);
    }

    private static string[] KnownMigrations() =>
    [
        "20261013133934_AddWelcomeInvitationOutboxProcessing",
        .. EmployeeImportMigrationPlan.OrderedMigrationIds
    ];

    private static EmployeeImportMigrationEvidence ValidEvidence()
    {
        var indexes = new MigrationIndex[]
        {
            new("importbatches", "PRIMARY", true, ["Id"]),
            new("importbatches", "IX_ImportBatches_TenantId_CreatedDate", false, ["TenantId", "CreatedDate"]),
            new("tenants", "PRIMARY", true, ["Id"]),
            new("tenants", "IX_Tenants_Host", true, ["Host"]),
            new("tenants", "IX_Tenants_ShardKey", true, ["ShardKey"]),
            new("tenants", "IX_Tenants_TenantCode", true, ["TenantCode"]),
            new("users", "PRIMARY", true, ["Id"]),
            new("users", "AK_Users_TenantId_Id", true, ["TenantId", "Id"]),
            new("users", "IX_Users_TenantId_Email", true, ["TenantId", "Email"])
        };
        var constraints = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "importbatches|PRIMARY|PRIMARY KEY",
            "tenants|PRIMARY|PRIMARY KEY",
            "users|PRIMARY|PRIMARY KEY",
            "users|AK_Users_TenantId_Id|UNIQUE"
        };
        var schema = new EmployeeImportSchemaBaseline(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["__efmigrationshistory"] = "BASE TABLE",
                ["importbatches"] = "BASE TABLE",
                ["tenants"] = "BASE TABLE",
                ["users"] = "BASE TABLE"
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["__efmigrationshistory"] = "InnoDB",
                ["importbatches"] = "InnoDB",
                ["tenants"] = "InnoDB",
                ["users"] = "InnoDB"
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["importbatches|id"] = "char(36)|NO",
                ["importbatches|tenantid"] = "char(36)|NO",
                ["tenants|id"] = "char(36)|NO",
                ["users|id"] = "char(36)|NO",
                ["users|tenantid"] = "char(36)|NO"
            },
            indexes,
            constraints,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "importbatches|FK_ImportBatches_Tenants_TenantId|TenantId|tenants|Id"
            },
            HistoryTablePresent: true);
        return new EmployeeImportMigrationEvidence(
            new EmployeeImportTenantRoute(TenantId, "WEB1", "web1.localhost", "web1", "MySql", 1),
            "hrms_catalog", "127.0.0.1", 3306, "127.0.0.1", 3306,
            new MySqlRuntimeIdentity("hrms_catalog", "8.0.46", "mysql-host", 3306, "server-uuid"),
            new MySqlRuntimeIdentity("hrms_web1", "8.0.46", "mysql-host", 3306, "server-uuid"),
            KnownMigrations(), ["20261013133934_AddWelcomeInvitationOutboxProcessing"],
            EmployeeImportMigrationPlan.OrderedMigrationIds, schema);
    }

    private sealed class TestLock : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
