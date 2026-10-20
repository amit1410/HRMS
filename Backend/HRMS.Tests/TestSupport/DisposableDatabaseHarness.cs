using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HRMS.Tests.TestSupport;

public enum DisposableDatabaseProvider
{
    SqlServer,
    MySql
}

public sealed record DisposableDatabaseTarget(
    DisposableDatabaseProvider Provider,
    string ServerIdentity,
    string InstanceOrPort,
    IReadOnlyList<string> ExpectedDatabaseNames,
    string RunId,
    string EnvironmentDesignation,
    string TestOnlyConnectionConfiguration,
    string ManifestPath);

public sealed record DisposableDatabaseApproval(
    bool CreateApproved,
    bool DeleteApproved,
    bool ConnectionApproved = false,
    bool MigrationApproved = false,
    string? ApprovedProvider = null,
    string? ApprovedServerIdentity = null,
    string? ApprovedInstanceOrPort = null,
    string? ApprovedRunId = null,
    string? ApprovedManifestPath = null,
    IReadOnlyList<string>? ApprovedDatabaseNames = null,
    string? ApprovedEnvironmentDesignation = null);

public sealed record DisposableDatabaseOwnershipMarker(
    string RunId,
    DisposableDatabaseProvider Provider,
    string ServerIdentity,
    string DatabaseName,
    string OwnershipMarker);

public sealed record DisposableDatabaseManifest(
    string RunId,
    DisposableDatabaseProvider Provider,
    string ServerIdentity,
    string DatabaseName,
    DateTimeOffset CreationTimestampUtc,
    string OwnershipMarker,
    bool CreationSucceeded,
    bool MigrationSucceeded,
    bool CleanupEligible);

public interface IDisposableDatabaseAdapter
{
    DisposableDatabaseProvider Provider { get; }
    Task<string> VerifyServerIdentityAsync(DisposableDatabaseTarget target, CancellationToken cancellationToken);
    Task<string> VerifyInstanceOrPortAsync(DisposableDatabaseTarget target, CancellationToken cancellationToken);
    Task<bool> DatabaseExistsAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken);
    Task<bool> TryClaimDatabaseAsync(DisposableDatabaseTarget target, string databaseName, string runId, string ownershipMarker, CancellationToken cancellationToken);
    Task CreateDatabaseAsync(DisposableDatabaseTarget target, string databaseName, string ownershipMarker, CancellationToken cancellationToken);
    Task MigrateUpAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken);
    Task<DisposableDatabaseOwnershipMarker?> ReadOwnershipMarkerAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken);
    Task DeleteDatabaseAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken);
    Task<bool> DatabaseIsAbsentAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken);
}

public interface IDisposableDatabaseManifestStore
{
    Task WriteAsync(DisposableDatabaseManifest manifest, string path, CancellationToken cancellationToken);
    Task<DisposableDatabaseManifest?> ReadAsync(string path, CancellationToken cancellationToken);
}

public sealed class JsonDisposableDatabaseManifestStore : IDisposableDatabaseManifestStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task WriteAsync(DisposableDatabaseManifest manifest, string path, CancellationToken cancellationToken)
    {
        ValidatePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, Options, cancellationToken);
    }

    public async Task<DisposableDatabaseManifest?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        ValidatePath(path);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DisposableDatabaseManifest>(stream, Options, cancellationToken);
    }

    private static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || string.IsNullOrWhiteSpace(Path.GetDirectoryName(path)))
            throw new InvalidOperationException("Disposable database manifests require an absolute artifact path.");
    }
}

public sealed class DisposableDatabaseHarness
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BAD1", "MH01", "HRMS_Phase4_Leave_MySql_Test", "hrms_tenant", "master", "model", "msdb", "tempdb", "HRMS"
    };

    private readonly IDisposableDatabaseAdapter _adapter;
    private readonly IDisposableDatabaseManifestStore _manifests;
    private readonly Action<string> _audit;

    public DisposableDatabaseHarness(IDisposableDatabaseAdapter adapter, IDisposableDatabaseManifestStore manifests, Action<string>? audit = null)
    {
        _adapter = adapter;
        _manifests = manifests;
        _audit = audit ?? (_ => { });
    }

    public async Task<DisposableDatabaseManifest> CreateAsync(
        DisposableDatabaseTarget target,
        DisposableDatabaseApproval approval,
        CancellationToken cancellationToken = default)
    {
        ValidateTarget(target, approval, requireCreateApproval: true);
        if (_adapter.Provider != target.Provider) throw new InvalidOperationException("The provider adapter does not match the approved target.");
        if (_adapter is SqlServerDisposableDatabaseAdapter or MySqlDisposableDatabaseAdapter)
            ProviderAcceptanceExecutionGate.Validate(target, approval);
        if ((_adapter is SqlServerDisposableDatabaseAdapter or MySqlDisposableDatabaseAdapter) &&
            await _manifests.ReadAsync(target.ManifestPath, cancellationToken) is not null)
            throw new InvalidOperationException("The ownership manifest path already contains a prior run; use a new unique run ID and manifest path.");

        var verifiedServer = await _adapter.VerifyServerIdentityAsync(target, cancellationToken);
        RequireExact("server identity", target.ServerIdentity, verifiedServer);
        var verifiedInstanceOrPort = await _adapter.VerifyInstanceOrPortAsync(target, cancellationToken);
        RequireExact("instance/port", target.InstanceOrPort, verifiedInstanceOrPort);

        foreach (var databaseName in target.ExpectedDatabaseNames)
            ValidateDatabaseName(databaseName, target);

        foreach (var databaseName in target.ExpectedDatabaseNames)
        {
            if (await _adapter.DatabaseExistsAsync(target, databaseName, cancellationToken))
                throw new InvalidOperationException($"Refusing existing disposable database '{databaseName}'.");

            var marker = CreateOwnershipMarker(target, databaseName);
            if (!await _adapter.TryClaimDatabaseAsync(target, databaseName, target.RunId, marker, cancellationToken))
                throw new InvalidOperationException($"Database '{databaseName}' is already claimed by another run.");

            var manifest = new DisposableDatabaseManifest(target.RunId, target.Provider, target.ServerIdentity, databaseName,
                DateTimeOffset.UtcNow, marker, false, false, false);
            await _manifests.WriteAsync(manifest, target.ManifestPath, cancellationToken);
            try
            {
                await _adapter.CreateDatabaseAsync(target, databaseName, marker, cancellationToken);
                manifest = manifest with { CreationSucceeded = true };
                await _manifests.WriteAsync(manifest, target.ManifestPath, cancellationToken);
                await _adapter.MigrateUpAsync(target, databaseName, cancellationToken);
                manifest = manifest with { MigrationSucceeded = true, CleanupEligible = true };
                await _manifests.WriteAsync(manifest, target.ManifestPath, cancellationToken);
            }
            catch
            {
                // The manifest stays durable and ineligible for cleanup when creation or migration is incomplete.
                _audit($"Disposable database setup failed for provider {target.Provider}, database {databaseName}, run {target.RunId}.");
                throw;
            }
        }

        return (await _manifests.ReadAsync(target.ManifestPath, cancellationToken))!
            ?? throw new InvalidOperationException("The disposable database manifest could not be read after creation.");
    }

    public async Task CleanupAsync(
        DisposableDatabaseTarget target,
        DisposableDatabaseApproval approval,
        DisposableDatabaseManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ValidateTarget(target, approval, requireCreateApproval: false);
        if (!approval.DeleteApproved) throw new InvalidOperationException("Explicit deletion approval is required.");
        if (_adapter.Provider != target.Provider) throw new InvalidOperationException("The provider adapter does not match the approved target.");
        RequireExact("manifest path", target.ManifestPath, target.ManifestPath);
        RequireExact("manifest run id", target.RunId, manifest.RunId);
        RequireExact("manifest server identity", target.ServerIdentity, manifest.ServerIdentity);
        if (manifest.Provider != target.Provider)
            throw new InvalidOperationException("The manifest provider does not match the approved target.");
        if (!target.ExpectedDatabaseNames.Contains(manifest.DatabaseName, StringComparer.Ordinal))
            throw new InvalidOperationException("The manifest database is outside the exact approved disposable allowlist.");
        if (_adapter is SqlServerDisposableDatabaseAdapter or MySqlDisposableDatabaseAdapter)
            ProviderAcceptanceExecutionGate.Validate(target, approval);
        var storedManifest = await _manifests.ReadAsync(target.ManifestPath, cancellationToken)
            ?? throw new InvalidOperationException("The durable ownership manifest is missing.");
        if (storedManifest != manifest)
            throw new InvalidOperationException("The supplied manifest does not match the durable ownership manifest.");
        if (!manifest.CreationSucceeded || !manifest.CleanupEligible)
            throw new InvalidOperationException("The database was not successfully created and migrated by this run.");

        var verifiedServer = await _adapter.VerifyServerIdentityAsync(target, cancellationToken);
        RequireExact("server identity", target.ServerIdentity, verifiedServer);
        var verifiedInstanceOrPort = await _adapter.VerifyInstanceOrPortAsync(target, cancellationToken);
        RequireExact("instance/port", target.InstanceOrPort, verifiedInstanceOrPort);
        ValidateDatabaseName(manifest.DatabaseName, target);
        var marker = await _adapter.ReadOwnershipMarkerAsync(target, manifest.DatabaseName, cancellationToken)
            ?? throw new InvalidOperationException("The database-side ownership marker is missing.");
        if (marker != new DisposableDatabaseOwnershipMarker(target.RunId, target.Provider, target.ServerIdentity, manifest.DatabaseName, manifest.OwnershipMarker))
            throw new InvalidOperationException("The database-side ownership marker does not match the manifest and approved target.");

        try
        {
            await _adapter.DeleteDatabaseAsync(target, manifest.DatabaseName, cancellationToken);
            if (!await _adapter.DatabaseIsAbsentAsync(target, manifest.DatabaseName, cancellationToken))
                throw new InvalidOperationException("Disposable database cleanup could not be verified.");
        }
        catch (Exception exception)
        {
            _audit($"Disposable database cleanup failed for provider {target.Provider}, database {manifest.DatabaseName}, run {target.RunId}: {exception.GetType().Name}.");
            throw new InvalidOperationException("Disposable database cleanup failed; the database was not confirmed absent.", exception);
        }
    }

    public async Task MigrateDownAsync(
        DisposableDatabaseTarget target,
        DisposableDatabaseApproval approval,
        DisposableDatabaseManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ValidateTarget(target, approval, requireCreateApproval: false);
        if (!approval.MigrationApproved) throw new InvalidOperationException("Explicit migration approval is required.");
        if (_adapter.Provider != target.Provider) throw new InvalidOperationException("The provider adapter does not match the approved target.");
        if (_adapter is not IDisposableDatabaseMigrationDownAdapter migrationDown)
            throw new InvalidOperationException("The provider adapter does not support migration-down.");

        ValidateManifestIdentity(target, manifest);
        var storedManifest = await _manifests.ReadAsync(target.ManifestPath, cancellationToken)
            ?? throw new InvalidOperationException("The durable ownership manifest is missing.");
        if (storedManifest != manifest)
            throw new InvalidOperationException("The supplied manifest does not match the durable ownership manifest.");
        if (!manifest.CreationSucceeded || !manifest.MigrationSucceeded || !manifest.CleanupEligible)
            throw new InvalidOperationException("Migration-down requires a successfully created and migrated disposable database.");

        var marker = await _adapter.ReadOwnershipMarkerAsync(target, manifest.DatabaseName, cancellationToken)
            ?? throw new InvalidOperationException("The database-side ownership marker is missing.");
        if (marker != new DisposableDatabaseOwnershipMarker(target.RunId, target.Provider, target.ServerIdentity, manifest.DatabaseName, manifest.OwnershipMarker))
            throw new InvalidOperationException("The database-side ownership marker does not match the manifest and approved target.");

        await migrationDown.MigrateDownAsync(target, manifest.DatabaseName, cancellationToken);
    }

    private static void ValidateTarget(DisposableDatabaseTarget target, DisposableDatabaseApproval approval, bool requireCreateApproval)
    {
        if (requireCreateApproval && !approval.CreateApproved) throw new InvalidOperationException("Explicit creation approval is required.");
        if (string.IsNullOrWhiteSpace(target.ServerIdentity) || string.IsNullOrWhiteSpace(target.InstanceOrPort)) throw new InvalidOperationException("Exact server identity and instance/port are required.");
        if (string.IsNullOrWhiteSpace(target.RunId) || string.IsNullOrWhiteSpace(target.EnvironmentDesignation)) throw new InvalidOperationException("A unique run ID and explicit environment designation are required.");
        if (target.ExpectedDatabaseNames.Count == 0) throw new InvalidOperationException("At least one approved disposable database name is required.");
        if (string.IsNullOrWhiteSpace(target.TestOnlyConnectionConfiguration)) throw new InvalidOperationException("A test-only connection configuration is required.");
        if (!Path.IsPathFullyQualified(target.ManifestPath)) throw new InvalidOperationException("The ownership manifest path must be absolute.");
        if (target.ExpectedDatabaseNames.Distinct(StringComparer.Ordinal).Count() != target.ExpectedDatabaseNames.Count)
            throw new InvalidOperationException("Approved disposable database names must be unique.");
    }

    private static void ValidateManifestIdentity(DisposableDatabaseTarget target, DisposableDatabaseManifest manifest)
    {
        RequireExact("manifest run id", target.RunId, manifest.RunId);
        RequireExact("manifest server identity", target.ServerIdentity, manifest.ServerIdentity);
        if (manifest.Provider != target.Provider)
            throw new InvalidOperationException("The manifest provider does not match the approved target.");
        if (!target.ExpectedDatabaseNames.Contains(manifest.DatabaseName, StringComparer.Ordinal))
            throw new InvalidOperationException("The manifest database is outside the exact approved disposable allowlist.");
        ValidateDatabaseName(manifest.DatabaseName, target);
    }

    private static void ValidateDatabaseName(string databaseName, DisposableDatabaseTarget target)
    {
        if (ProtectedNames.Contains(databaseName)) throw new InvalidOperationException($"Protected fixture/database name '{databaseName}' is not allowed.");
        if (!target.ExpectedDatabaseNames.Contains(databaseName, StringComparer.Ordinal)) throw new InvalidOperationException("Database is outside the exact approved disposable allowlist.");
        if (!databaseName.Contains(target.RunId, StringComparison.Ordinal)) throw new InvalidOperationException("Database name must contain the unique run ID.");
        if (databaseName.Contains("catalog", StringComparison.OrdinalIgnoreCase) || databaseName.Contains("tenant", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Shared tenant catalogs and tenant database names are not valid disposable targets.");
    }

    private static string CreateOwnershipMarker(DisposableDatabaseTarget target, string databaseName) => $"HRMS-DISPOSABLE|{target.Provider}|{target.ServerIdentity}|{target.RunId}|{databaseName}";
    private static void RequireExact(string field, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal)) throw new InvalidOperationException($"Approved {field} does not match the observed value.");
    }
}

public sealed class InMemoryDisposableDatabaseAdapter : IDisposableDatabaseAdapter
{
    private readonly ConcurrentDictionary<string, DisposableDatabaseOwnershipMarker> _claims = new(StringComparer.Ordinal);
    public DisposableDatabaseProvider Provider { get; init; }
    public string? ObservedServerIdentity { get; set; }
    public string? ObservedInstanceOrPort { get; set; }
    public bool FailCreate { get; set; }
    public bool FailDelete { get; set; }
    public bool FailVerification { get; set; }
    public bool HideOwnershipMarker { get; set; }
    public int DeleteCalls { get; private set; }

    public Task<string> VerifyServerIdentityAsync(DisposableDatabaseTarget target, CancellationToken cancellationToken) => Task.FromResult(FailVerification ? "wrong-server" : ObservedServerIdentity ?? target.ServerIdentity);
    public Task<string> VerifyInstanceOrPortAsync(DisposableDatabaseTarget target, CancellationToken cancellationToken) => Task.FromResult(FailVerification ? "wrong-port" : ObservedInstanceOrPort ?? target.InstanceOrPort);
    public Task<bool> DatabaseExistsAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken) => Task.FromResult(_claims.ContainsKey(databaseName));
    public Task<bool> TryClaimDatabaseAsync(DisposableDatabaseTarget target, string databaseName, string runId, string ownershipMarker, CancellationToken cancellationToken) => Task.FromResult(_claims.TryAdd(databaseName, new(runId, Provider, target.ServerIdentity, databaseName, ownershipMarker)));
    public Task CreateDatabaseAsync(DisposableDatabaseTarget target, string databaseName, string ownershipMarker, CancellationToken cancellationToken) { if (FailCreate) throw new InvalidOperationException("fake create failure"); return Task.CompletedTask; }
    public Task MigrateUpAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<DisposableDatabaseOwnershipMarker?> ReadOwnershipMarkerAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken) => Task.FromResult(!HideOwnershipMarker && _claims.TryGetValue(databaseName, out var value) ? value : null);
    public Task DeleteDatabaseAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken) { DeleteCalls++; if (FailDelete) throw new InvalidOperationException("fake delete failure"); _claims.TryRemove(databaseName, out _); return Task.CompletedTask; }
    public Task<bool> DatabaseIsAbsentAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken) => Task.FromResult(!_claims.ContainsKey(databaseName));
}
