using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class DisposableDatabaseHarnessTests
{
    [Fact]
    public async Task Missing_creation_approval_rejects_without_adapter_calls()
    {
        var adapter = NewAdapter();
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewHarness(adapter).CreateAsync(Target(), new(false, false)));
        Assert.Equal(0, adapter.DeleteCalls);
    }

    [Fact]
    public async Task Missing_or_wrong_server_identity_rejects()
    {
        var adapter = NewAdapter();
        adapter.FailVerification = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewHarness(adapter).CreateAsync(Target(), new(true, false)));

        adapter.FailVerification = false;
        adapter.ObservedServerIdentity = "other-server:3306";
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewHarness(adapter).CreateAsync(Target(), new(true, false)));
    }

    [Fact]
    public async Task Existing_database_rejects_creation()
    {
        var adapter = NewAdapter();
        await adapter.TryClaimDatabaseAsync(Target(), DatabaseName, RunId, "existing", default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewHarness(adapter).CreateAsync(Target(), new(true, false)));
    }

    [Fact]
    public async Task Protected_fixture_names_are_rejected()
    {
        var target = Target("BAD1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewHarness(NewAdapter()).CreateAsync(target, new(true, false)));
    }

    [Fact]
    public async Task Successful_creation_records_run_owned_manifest()
    {
        var manifest = new MemoryManifestStore();
        var result = await NewHarness(NewAdapter(), manifest).CreateAsync(Target(), new(true, false));

        Assert.Equal(RunId, result.RunId);
        Assert.Equal(DisposableDatabaseProvider.MySql, result.Provider);
        Assert.True(result.CreationSucceeded);
        Assert.True(result.MigrationSucceeded);
        Assert.True(result.CleanupEligible);
        Assert.NotNull(await manifest.ReadAsync(ManifestPath, default));
    }

    [Fact]
    public async Task Missing_marker_manifest_mismatch_and_wrong_run_id_reject_cleanup()
    {
        var adapter = NewAdapter();
        var manifest = new MemoryManifestStore();
        var harness = NewHarness(adapter, manifest);
        var created = await harness.CreateAsync(Target(), new(true, false));

        adapter.HideOwnershipMarker = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.CleanupAsync(Target(), new(true, true), created));

        adapter.HideOwnershipMarker = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.CleanupAsync(Target(), new(true, true), created with { ServerIdentity = "wrong" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.CleanupAsync(Target(), new(true, true), created with { RunId = "other-run" }));
        Assert.Equal(0, adapter.DeleteCalls);
    }

    [Fact]
    public async Task Prefix_match_alone_never_authorizes_cleanup()
    {
        var target = Target("HRMS_Phase1E2_20261013T195000Z_sql");
        var manifest = new DisposableDatabaseManifest(RunId, DisposableDatabaseProvider.MySql, target.ServerIdentity, target.ExpectedDatabaseNames[0], DateTimeOffset.UtcNow, "marker", true, true, true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewHarness(NewAdapter()).CleanupAsync(target, new(true, true), manifest));
    }

    [Fact]
    public async Task Cleanup_failure_is_surfaced_and_not_reported_as_success()
    {
        var adapter = NewAdapter();
        adapter.FailDelete = true;
        var harness = NewHarness(adapter);
        var manifest = await harness.CreateAsync(Target(), new(true, false));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.CleanupAsync(Target(), new(true, true), manifest));
        Assert.Contains("cleanup failed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Partial_creation_failure_remains_ineligible_for_cleanup()
    {
        var adapter = NewAdapter();
        adapter.FailCreate = true;
        var manifests = new MemoryManifestStore();
        var harness = NewHarness(adapter, manifests);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.CreateAsync(Target(), new(true, false)));
        var manifest = await manifests.ReadAsync(ManifestPath, default);
        Assert.NotNull(manifest);
        Assert.False(manifest!.CreationSucceeded);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.CleanupAsync(Target(), new(true, true), manifest));
    }

    [Fact]
    public async Task Concurrent_runs_cannot_claim_the_same_database()
    {
        var adapter = NewAdapter();
        var target = Target();
        var harness = NewHarness(adapter);
        var results = await Task.WhenAll(
            Task.Run(async () => await RecordAsync(harness.CreateAsync(target, new(true, false)))),
            Task.Run(async () => await RecordAsync(harness.CreateAsync(target, new(true, false)))));

        Assert.Equal(1, results.Count(x => x is null));
        Assert.Equal(1, results.Count(x => x is not null));
    }

    [Fact]
    public async Task Credentials_are_redacted_from_audit_logs()
    {
        var messages = new List<string>();
        var adapter = NewAdapter();
        adapter.FailCreate = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new DisposableDatabaseHarness(adapter, new MemoryManifestStore(), messages.Add)
            .CreateAsync(Target("HRMS_Phase1E2_20261013T195000Z_sql", "Server=exact;User ID=test;Password=do-not-log"), new(true, false)));

        Assert.DoesNotContain(messages, message => message.Contains("do-not-log", StringComparison.Ordinal));
    }

    private static async Task<DisposableDatabaseManifest?> RecordAsync(Task<DisposableDatabaseManifest> operation)
    {
        try { return await operation; } catch (InvalidOperationException) { return null; }
    }

    private static DisposableDatabaseHarness NewHarness(InMemoryDisposableDatabaseAdapter adapter, MemoryManifestStore? manifests = null) => new(adapter, manifests ?? new MemoryManifestStore());
    private static InMemoryDisposableDatabaseAdapter NewAdapter() => new() { Provider = DisposableDatabaseProvider.MySql, ObservedServerIdentity = "mysql.test:3306", ObservedInstanceOrPort = "3306" };
    private static string RunId => "20261013T195000Z";
    private static string DatabaseName => $"HRMS_Phase1E2_{RunId}_db";
    private static string ManifestPath => Path.Combine(Path.GetTempPath(), "hrms-phase1e2", $"{RunId}.json");
    private static DisposableDatabaseTarget Target(string? databaseName = null, string connection = "test-only-connection") => new(
        DisposableDatabaseProvider.MySql, "mysql.test:3306", "3306", [databaseName ?? DatabaseName], RunId, "isolated-provider-acceptance", connection, ManifestPath);

    private sealed class MemoryManifestStore : IDisposableDatabaseManifestStore
    {
        private readonly Dictionary<string, DisposableDatabaseManifest> _values = new(StringComparer.Ordinal);
        public Task WriteAsync(DisposableDatabaseManifest manifest, string path, CancellationToken cancellationToken) { _values[path] = manifest; return Task.CompletedTask; }
        public Task<DisposableDatabaseManifest?> ReadAsync(string path, CancellationToken cancellationToken) => Task.FromResult(_values.TryGetValue(path, out var manifest) ? manifest : null);
    }
}
