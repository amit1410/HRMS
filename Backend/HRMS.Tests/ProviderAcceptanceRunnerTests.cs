using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

[Collection("Provider acceptance environment")]
public sealed class ProviderAcceptanceRunnerTests
{
    [Fact]
    public void Missing_explicit_runner_enablement_blocks_before_configuration()
    {
        using var env = new EnvironmentScope((ProviderAcceptanceRunner.ExecuteVariable, null));
        var exception = Assert.Throws<InvalidOperationException>(() => ProviderAcceptanceRunner.ReadConfiguration(DisposableDatabaseProvider.SqlServer));
        Assert.Contains("dedicated provider acceptance command", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_connection_variable_is_ignored()
    {
        using var env = new EnvironmentScope(
            (ProviderAcceptanceRunner.ExecuteVariable, "true"),
            (ProviderAcceptanceRunner.ProviderVariable, "SqlServer"),
            (ProviderAcceptanceRunner.ServerVariable, null),
            ("HRMS_SQLSERVER_TEST_CONNECTION", "Server=legacy;Password=must-not-be-used"));
        var exception = Assert.Throws<InvalidOperationException>(() => ProviderAcceptanceRunner.ReadConfiguration(DisposableDatabaseProvider.SqlServer));
        Assert.DoesNotContain("legacy", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_exact_server_identity_blocks()
    {
        using var env = ValidEnvironment((ProviderAcceptanceRunner.ServerVariable, null));
        var exception = Assert.Throws<InvalidOperationException>(() => ProviderAcceptanceRunner.ReadConfiguration(DisposableDatabaseProvider.SqlServer));
        Assert.Contains(ProviderAcceptanceRunner.ServerVariable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_migration_approval_blocks_gate()
    {
        var target = Target();
        var approval = Approval() with { MigrationApproved = false };
        var exception = Assert.Throws<InvalidOperationException>(() => ProviderAcceptanceExecutionGate.Validate(target, approval));
        Assert.Contains("migration approval", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wrong_provider_and_run_id_are_rejected()
    {
        var target = Target();
        var wrongProvider = Approval() with { ApprovedProvider = "MySql" };
        Assert.Contains("provider", Assert.Throws<InvalidOperationException>(() => ProviderAcceptanceExecutionGate.Validate(target, wrongProvider)).Message, StringComparison.OrdinalIgnoreCase);
        var wrongRun = Approval() with { ApprovedRunId = "other-run-20261013" };
        Assert.Contains("run ID", Assert.Throws<InvalidOperationException>(() => ProviderAcceptanceExecutionGate.Validate(target, wrongRun)).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Existing_database_blocks_creation_and_manifest_or_marker_mismatch_blocks_cleanup()
    {
        var adapter = new InMemoryDisposableDatabaseAdapter { Provider = DisposableDatabaseProvider.SqlServer, ObservedServerIdentity = "sql.test", ObservedInstanceOrPort = "1433" };
        var target = Target();
        await adapter.TryClaimDatabaseAsync(target, target.ExpectedDatabaseNames[0], target.RunId, "existing", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DisposableDatabaseHarness(adapter, new MemoryManifestStore()).CreateAsync(target, new(true, false)));

        var cleanAdapter = new InMemoryDisposableDatabaseAdapter { Provider = DisposableDatabaseProvider.SqlServer, ObservedServerIdentity = "sql.test", ObservedInstanceOrPort = "1433" };
        var store = new MemoryManifestStore();
        var harness = new DisposableDatabaseHarness(cleanAdapter, store);
        var manifest = await harness.CreateAsync(target, new(true, false));
        cleanAdapter.HideOwnershipMarker = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.CleanupAsync(target, new(true, true), manifest));
    }

    [Fact]
    public async Task Missing_deletion_approval_does_not_delete()
    {
        var adapter = new InMemoryDisposableDatabaseAdapter { Provider = DisposableDatabaseProvider.SqlServer, ObservedServerIdentity = "sql.test", ObservedInstanceOrPort = "1433" };
        var target = Target();
        var harness = new DisposableDatabaseHarness(adapter, new MemoryManifestStore());
        var manifest = await harness.CreateAsync(target, new(true, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.CleanupAsync(target, new(true, false), manifest));
        Assert.Equal(0, adapter.DeleteCalls);
    }

    [Fact]
    public void Approval_manifest_does_not_accept_credentials_or_unknown_targets()
    {
        var target = Target() with { TestOnlyConnectionConfiguration = "Server=test;Password=not-a-secret-store" };
        var approval = Approval();
        Assert.Throws<InvalidOperationException>(() => ProviderAcceptanceExecutionGate.Validate(target, approval));
        Assert.Throws<InvalidOperationException>(() => ProviderAcceptanceExecutionGate.Validate(target with { ExpectedDatabaseNames = ["BAD1"] }, approval));
    }

    private static DisposableDatabaseTarget Target() => new(DisposableDatabaseProvider.SqlServer, "sql.test", "1433", ["HRMS_Phase1E_20261013T195000Z_acceptance", "HRMS_Phase1E_20261013T195000Z_rollback"], "20261013T195000Z", "isolated-provider-acceptance", "test-only-connection", Path.Combine(Path.GetTempPath(), "phase1e", "manifest.json"));
    private static DisposableDatabaseApproval Approval() => new(true, true, true, true, "SqlServer", "sql.test", "1433", "20261013T195000Z", Target().ManifestPath, Target().ExpectedDatabaseNames, Target().EnvironmentDesignation);
    private static EnvironmentScope ValidEnvironment((string Name, string? Value) overrideValue) => new(
        (ProviderAcceptanceRunner.ExecuteVariable, "true"), (ProviderAcceptanceRunner.ProviderVariable, "SqlServer"), (ProviderAcceptanceRunner.ServerVariable, "sql.test"), (ProviderAcceptanceRunner.InstanceVariable, "1433"), (ProviderAcceptanceRunner.DatabaseNamesVariable, "HRMS_Phase1E_20261013T195000Z_a,HRMS_Phase1E_20261013T195000Z_b"), (ProviderAcceptanceRunner.RunIdVariable, "20261013T195000Z"), (ProviderAcceptanceRunner.ManifestPathVariable, Path.Combine(Path.GetTempPath(), "phase1e", "manifest.json")), (ProviderAcceptanceRunner.EnvironmentVariable, "isolated-provider-acceptance"), (ProviderAcceptanceRunner.TestConfigurationVariable, "test-only"), (ProviderAcceptanceRunner.ConnectionApprovalVariable, "true"), (ProviderAcceptanceRunner.CreateApprovalVariable, "true"), (ProviderAcceptanceRunner.MigrationApprovalVariable, "true"), (ProviderAcceptanceRunner.DeleteApprovalVariable, "true"), (ProviderAcceptanceRunner.ResultPathVariable, Path.Combine(Path.GetTempPath(), "phase1e", "result.json")), overrideValue);

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> previous = new(StringComparer.Ordinal);
        public EnvironmentScope(params (string Name, string? Value)[] values) { foreach (var (name, value) in values) { previous[name] = Environment.GetEnvironmentVariable(name); Environment.SetEnvironmentVariable(name, value); } }
        public void Dispose() { foreach (var (name, value) in previous) Environment.SetEnvironmentVariable(name, value); }
    }

    private sealed class MemoryManifestStore : IDisposableDatabaseManifestStore
    {
        private readonly Dictionary<string, DisposableDatabaseManifest> values = new(StringComparer.Ordinal);
        public Task WriteAsync(DisposableDatabaseManifest manifest, string path, CancellationToken cancellationToken) { values[path] = manifest; return Task.CompletedTask; }
        public Task<DisposableDatabaseManifest?> ReadAsync(string path, CancellationToken cancellationToken) => Task.FromResult(values.TryGetValue(path, out var value) ? value : null);
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class Phase1EProviderFactAttribute : FactAttribute
{
    public Phase1EProviderFactAttribute(DisposableDatabaseProvider provider)
    {
        Skip = !ProviderAcceptanceRunner.IsExplicitlyEnabled ? $"Set {ProviderAcceptanceRunner.ExecuteVariable}=true through the dedicated provider acceptance command." :
            !string.Equals(Environment.GetEnvironmentVariable(ProviderAcceptanceRunner.ProviderVariable), provider.ToString(), StringComparison.Ordinal) ? "The explicit provider selection does not match this entry point." : null;
    }
}

public sealed class ProviderAcceptanceRunnerEntryPoints
{
    [Phase1EProviderFact(DisposableDatabaseProvider.SqlServer)]
    [Trait("Category", "Phase1EProviderAcceptance")]
    public Task SqlServer_WelcomeInvitation_acceptance() => new ProviderAcceptanceRunner().RunAsync(DisposableDatabaseProvider.SqlServer);

    [Phase1EProviderFact(DisposableDatabaseProvider.MySql)]
    [Trait("Category", "Phase1EProviderAcceptance")]
    public Task MySql_WelcomeInvitation_acceptance() => new ProviderAcceptanceRunner().RunAsync(DisposableDatabaseProvider.MySql);
}
