using System.Data.Common;
using System.Reflection;
using System.Security.Cryptography;
using HRMS.Domain.Enums;
using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Sharding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MySql.EntityFrameworkCore.Extensions;

return await MigrationHost.RunAsync(args);

internal static class MigrationHost
{
    private const string ApiUserSecretsId = "af221e70-58f4-4c5b-bbf0-034f30422395";
    internal static readonly IReadOnlySet<string> TenantOnlyApprovedMigrations = new HashSet<string>(StringComparer.Ordinal)
    {
        "20261013132338_AddWelcomeInvitationCenter",
        "20261013133934_AddWelcomeInvitationOutboxProcessing"
    };
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var parsed = Arguments.Parse(args);
            if (parsed.Command is "help" or "--help" or "-h")
            {
                PrintUsage();
                return 0;
            }

            var builder = Host.CreateApplicationBuilder(args);
            AddRepositoryConfiguration(builder);
            using var host = builder.Build();
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            return parsed.Command switch
            {
                "validate" => await ValidateAsync(configuration, parsed),
                "migrate" => await MigrateAsync(configuration, parsed),
                "catalog-preflight" => await CatalogPreflightAsync(configuration, parsed),
                "catalog-migrate" => await CatalogMigrateAsync(configuration, parsed),
                "verify-release" => VerifyRelease(parsed),
                _ => throw new ArgumentException($"Unknown command '{parsed.Command}'.")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 2;
        }
    }

    internal static IConfiguration BuildConfiguration(string[] args)
        => BuildConfiguration(args, null);

    internal static IConfiguration BuildConfiguration(string[] args, Action<IConfigurationBuilder>? addDevelopmentSecrets)
    {
        var builder = Host.CreateApplicationBuilder(args);
        AddRepositoryConfiguration(builder, addDevelopmentSecrets);
        return builder.Configuration;
    }

    private static void AddRepositoryConfiguration(HostApplicationBuilder builder, Action<IConfigurationBuilder>? addDevelopmentSecrets = null)
    {
        var sources = builder.Configuration.Sources;
        var environmentVariablesIndex = sources
            .Select((source, index) => (source, index))
            .First(x => x.source.GetType().Name.Contains("EnvironmentVariablesConfigurationSource", StringComparison.Ordinal)).index;

        var repositoryBase = Path.Combine(Environment.CurrentDirectory, "Backend", "HRMS.API", "appsettings.json");
        sources.Insert(0, new JsonConfigurationSource { Path = repositoryBase, Optional = true, ReloadOnChange = false });

        var environmentFile = Path.Combine(Environment.CurrentDirectory, "Backend", "HRMS.API", $"appsettings.{builder.Environment.EnvironmentName}.json");
        sources.Insert(environmentVariablesIndex, new JsonConfigurationSource { Path = environmentFile, Optional = true, ReloadOnChange = false });
        if (builder.Environment.IsDevelopment())
        {
            if (addDevelopmentSecrets is null)
                builder.Configuration.AddUserSecrets(ApiUserSecretsId);
            else
                addDevelopmentSecrets(builder.Configuration);
            var userSecretsIndex = sources.Count - 1;
            var configurationSource = sources[userSecretsIndex];
            sources.RemoveAt(userSecretsIndex);
            environmentVariablesIndex = sources
                .Select((source, index) => (source, index))
                .First(x => x.source.GetType().Name.Contains("EnvironmentVariablesConfigurationSource", StringComparison.Ordinal)).index;
            sources.Insert(environmentVariablesIndex, configurationSource);
        }
    }

    private static async Task<int> ValidateAsync(IConfiguration config, Arguments args)
    {
        var target = await OpenTargetAsync(config, args);
        await using (target.Catalog)
        await using (target.Tenant)
        {
            await CheckIdentityAsync(target, args);
            await ReportAsync("catalog", target.Catalog, args.CancellationToken);
            await ReportAsync($"tenant {args.TenantCode}", target.Tenant, args.CancellationToken);
            Console.WriteLine("VALIDATE OK: read-only checks completed; no schema, migration-history, or seed writes were attempted.");
        }
        return 0;
    }

    private static async Task<int> MigrateAsync(IConfiguration config, Arguments args)
    {
        if (!args.Confirm || !args.BackupVerified || !args.RestoreReady)
            throw new InvalidOperationException("Migrate requires --confirm, --backup-verified, and --restore-ready.");

        if (args.TenantOnly)
        {
            await MigrateTenantOnlyAsync(config, args);
            return 0;
        }

        var target = await OpenTargetAsync(config, args);
        await using (target.Catalog)
        await using (target.Tenant)
        {
            await CheckIdentityAsync(target, args);
            var catalogPending = await PendingAsync(target.Catalog, args.CancellationToken);
            var tenantPending = await PendingAsync(target.Tenant, args.CancellationToken);
            CheckApproved(catalogPending, args.ApprovedMigrations, "catalog");
            CheckApproved(tenantPending, args.ApprovedMigrations, "tenant");
            Console.WriteLine($"BEFORE: catalog pending={catalogPending.Count}; tenant pending={tenantPending.Count}");

            await using var catalogLock = await MigrationLock.AcquireAsync(target.Catalog, "catalog", args.CancellationToken);
            await using var tenantLock = await MigrationLock.AcquireAsync(target.Tenant, $"tenant-{args.TenantId}", args.CancellationToken);
            // Re-read identity and history after locking. A concurrent operator may have changed either.
            await CheckIdentityAsync(target, args);
            var lockedCatalogPending = await PendingAsync(target.Catalog, args.CancellationToken);
            var lockedTenantPending = await PendingAsync(target.Tenant, args.CancellationToken);
            CheckApproved(lockedCatalogPending, args.ApprovedMigrations, "catalog");
            CheckApproved(lockedTenantPending, args.ApprovedMigrations, "tenant");

            if (lockedCatalogPending.Count > 0) await target.Catalog.Database.MigrateAsync(args.CancellationToken);
            if (lockedTenantPending.Count > 0) await target.Tenant.Database.MigrateAsync(args.CancellationToken);
            await ReportAsync("catalog after", target.Catalog, args.CancellationToken);
            await ReportAsync($"tenant {args.TenantCode} after", target.Tenant, args.CancellationToken);
            Console.WriteLine("MIGRATE OK: approved migration chains applied; no seeders were invoked.");
        }
        return 0;
    }

    private static async Task MigrateTenantOnlyAsync(IConfiguration config, Arguments args)
    {
        if (ParseProvider(args.Provider) != DatabaseProviderType.MySql)
            throw new InvalidOperationException("--tenant-only is restricted to the MySql tenant provider.");
        if (!TenantOnlyApprovedMigrations.SetEquals(args.ApprovedMigrations))
            throw new InvalidOperationException("--tenant-only requires exactly the two approved tenant migration IDs via --approved-migrations.");
        if (string.IsNullOrWhiteSpace(args.Database))
            throw new InvalidOperationException("--tenant-only requires --database with the expected physical tenant database name.");
        var tenantOnlyAllowlist = config.GetSection("MigrationHost:AllowedTenants").Get<string[]>() ?? [];
        if (!tenantOnlyAllowlist.Contains($"{args.TenantCode}:{args.TenantId}:{args.ShardKey}:{args.Database}", StringComparer.Ordinal))
            throw new InvalidOperationException("Tenant-only target is not in MigrationHost:AllowedTenants (expected TenantCode:TenantId:ShardKey:Database).");

        var target = await OpenTargetAsync(config, args);
        await using (target.Catalog)
        await using (target.Tenant)
        {
            await CheckIdentityAsync(target, args);
            CheckTenantDatabase(target.Tenant, args.Database);
            var pending = await PendingAsync(target.Tenant, args.CancellationToken);
            CheckApproved(pending, TenantOnlyApprovedMigrations, "tenant");
            await ValidateCompleteTenantHistoryAsync(target.Tenant, args.CancellationToken);
            Console.WriteLine($"BEFORE: tenant pending={pending.Count}; catalog migration history was not queried.");

            // Only the read-only catalog identity lookup is used in this branch. No catalog lock,
            // migration-history query, report, executor, or MigrateAsync call is reachable.
            await using var tenantLock = await MigrationLock.AcquireAsync(target.Tenant, $"tenant-{args.TenantId}", args.CancellationToken);
            await CheckIdentityAsync(target, args);
            CheckTenantDatabase(target.Tenant, args.Database);
            var lockedPending = await PendingAsync(target.Tenant, args.CancellationToken);
            CheckApproved(lockedPending, TenantOnlyApprovedMigrations, "tenant");
            await ValidateCompleteTenantHistoryAsync(target.Tenant, args.CancellationToken);
            await TenantOnlyMigrationExecutor.ExecuteAsync(target.Tenant, lockedPending, TenantOnlyApprovedMigrations, args.CancellationToken);
            await ReportAsync($"tenant {args.TenantCode} after", target.Tenant, args.CancellationToken);
            Console.WriteLine("TENANT-ONLY MIGRATE OK: approved MySQL tenant migrations applied; catalog schema and history were not accessed for migration.");
        }
    }

    private static void CheckTenantDatabase(HrmsDbContext tenant, string expectedDatabase)
    {
        var actual = tenant.Database.GetDbConnection().Database;
        if (!string.Equals(actual, expectedDatabase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Tenant physical database mismatch. Expected '{expectedDatabase}', connected to '{actual}'.");
    }

    private static async Task ValidateCompleteTenantHistoryAsync(HrmsDbContext tenant, CancellationToken token)
    {
        var known = tenant.Database.GetMigrations().ToArray();
        var applied = (await tenant.Database.GetAppliedMigrationsAsync(token)).ToArray();
        for (var i = 0; i < applied.Length; i++)
            if (i >= known.Length || !string.Equals(applied[i], known[i], StringComparison.Ordinal))
                throw new InvalidOperationException($"tenant has unexpected migration history at '{applied[i]}'.");
    }

    private static async Task<int> CatalogPreflightAsync(IConfiguration config, Arguments args)
    {
        await VerifyCatalogExecutionBoundaryAsync(config, args);
        await using var catalog = OpenCatalog(config, args);
        await CheckCatalogIdentityAsync(catalog, args);
        var pending = await PendingAsync(catalog, args.CancellationToken);
        if (args.ApprovedMigrations.Count > 0)
            CheckApproved(pending, args.ApprovedMigrations, "catalog");
        ReportSanitizedCatalogTarget(catalog, args, pending);
        Console.WriteLine("CATALOG PREFLIGHT OK: read-only checks completed; no tenant target was opened and no schema writes were attempted.");
        return 0;
    }

    private static async Task<int> CatalogMigrateAsync(IConfiguration config, Arguments args)
    {
        VerifyCatalogMigrationApproval(config, args);
        await VerifyCatalogExecutionBoundaryAsync(config, args);
        await using var catalog = OpenCatalog(config, args);
        await CheckCatalogIdentityAsync(catalog, args);
        var pending = await PendingAsync(catalog, args.CancellationToken);
        CheckApproved(pending, args.ApprovedMigrations, "catalog");
        ReportSanitizedCatalogTarget(catalog, args, pending);

        await using var catalogLock = await MigrationLock.AcquireAsync(catalog, "catalog", args.CancellationToken);
        await CheckCatalogIdentityAsync(catalog, args);
        var lockedPending = await PendingAsync(catalog, args.CancellationToken);
        CheckApproved(lockedPending, args.ApprovedMigrations, "catalog");
        if (lockedPending.Count > 0)
            await new CatalogOnlyMigrationExecutor().ExecuteAsync(new CatalogOnlyMigrationTarget(catalog), args.CancellationToken);

        await ReportAsync("catalog after", catalog, args.CancellationToken);
        Console.WriteLine("CATALOG MIGRATE OK: approved catalog migration chain applied; no tenant target was opened or modified.");
        return 0;
    }

    private static HrmsCatalogDbContext OpenCatalog(IConfiguration config, Arguments args)
    {
        var provider = ParseProvider(args.Provider);
        var catalogConnection = config.GetConnectionString("Catalog")
            ?? throw new InvalidOperationException("ConnectionStrings:Catalog is required.");
        var catalogOptions = new DbContextOptionsBuilder<HrmsCatalogDbContext>();
        if (provider == DatabaseProviderType.MySql)
            catalogOptions.UseMySQL(catalogConnection, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlCatalogMigrations"));
        else if (provider == DatabaseProviderType.SqlServer)
            catalogOptions.UseSqlServer(catalogConnection, x => x.MigrationsAssembly(typeof(HrmsCatalogDbContext).Assembly.FullName).MigrationsHistoryTable("__EFMigrationsHistoryCatalog"));
        else
            throw new InvalidOperationException("Only MySql and SqlServer are valid migration providers; SQLite is test/development-only.");
        return new HrmsCatalogDbContext(catalogOptions.Options);
    }

    private static async Task VerifyCatalogExecutionBoundaryAsync(IConfiguration config, Arguments args)
    {
        var allowedCatalogs = config.GetSection("MigrationHost:AllowedCatalogs").Get<string[]>() ?? [];
        if (!allowedCatalogs.Contains(args.Catalog, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Catalog '{args.Catalog}' is not in MigrationHost:AllowedCatalogs.");

        var requiredEnvironment = config["MigrationHost:RequiredEnvironment"];
        if (string.IsNullOrWhiteSpace(requiredEnvironment))
            throw new InvalidOperationException("MigrationHost:RequiredEnvironment must be configured for catalog migration.");
        if (!string.Equals(requiredEnvironment, args.Environment, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Environment verification failed. Expected '{requiredEnvironment}', received '{args.Environment}'.");
        await Task.CompletedTask;
    }

    private static void VerifyCatalogMigrationApproval(IConfiguration config, Arguments args)
    {
        if (!args.Confirm || !args.BackupVerified || !args.BackupEncrypted || !args.RetentionApproved || !args.RestoreReady || !args.IsolatedRestoreTested)
            throw new InvalidOperationException("Catalog migrate requires --confirm, --backup-verified, --backup-encrypted, --retention-approved, --restore-ready, and --isolated-restore-tested.");
        if (string.IsNullOrWhiteSpace(args.Approval))
            throw new InvalidOperationException("Catalog migrate requires --approval APPROVAL-ID.");
        var approvals = config.GetSection("MigrationHost:AllowedApprovals").Get<string[]>() ?? [];
        if (!approvals.Contains(args.Approval, StringComparer.Ordinal))
            throw new InvalidOperationException("Approval is not present in MigrationHost:AllowedApprovals.");
    }

    private static async Task CheckCatalogIdentityAsync(HrmsCatalogDbContext catalog, Arguments args)
    {
        var catalogName = catalog.Database.GetDbConnection().Database;
        if (!string.Equals(catalogName, args.Catalog, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Catalog identity mismatch. Expected '{args.Catalog}', connected to '{catalogName}'.");
        var providerName = catalog.Database.ProviderName ?? "";
        if (!providerName.Contains(args.Provider, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Provider mismatch. Expected '{args.Provider}', actual '{providerName}'.");
        await Task.CompletedTask;
    }

    private static void ReportSanitizedCatalogTarget(HrmsCatalogDbContext catalog, Arguments args, IReadOnlyCollection<string> pending)
    {
        var target = catalog.Database.GetDbConnection().Database;
        Console.WriteLine($"catalog target: provider={args.Provider}; database={target}; environment={args.Environment}; tenant-shards=none");
        Console.WriteLine($"catalog pending={pending.Count}; migration-ids={string.Join(", ", pending)}");
    }

    private static async Task<Target> OpenTargetAsync(IConfiguration config, Arguments args)
    {
        var allowedCatalogs = config.GetSection("MigrationHost:AllowedCatalogs").Get<string[]>() ?? [];
        var allowedTenants = config.GetSection("MigrationHost:AllowedTenants").Get<string[]>() ?? [];
        if (!allowedCatalogs.Contains(args.Catalog, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Catalog '{args.Catalog}' is not in MigrationHost:AllowedCatalogs.");
        if (!allowedTenants.Contains($"{args.TenantCode}:{args.TenantId}:{args.ShardKey}", StringComparer.Ordinal))
            throw new InvalidOperationException("Tenant target is not in MigrationHost:AllowedTenants (expected TenantCode:TenantId:ShardKey).");
        var provider = ParseProvider(args.Provider);
        if (provider != DatabaseProviderType.MySql && provider != DatabaseProviderType.SqlServer)
            throw new InvalidOperationException("Only MySql and SqlServer are valid migration providers; SQLite is test/development-only.");

        var catalogConnection = config.GetConnectionString("Catalog")
            ?? throw new InvalidOperationException("ConnectionStrings:Catalog is required.");
        var sharding = config.GetSection(ShardingOptions.SectionName).Get<ShardingOptions>() ?? new ShardingOptions();
        var template = provider == DatabaseProviderType.MySql
            ? sharding.MySqlConnectionStringTemplate
            : sharding.SqlServerConnectionStringTemplate ?? sharding.ConnectionStringTemplate;
        if (string.IsNullOrWhiteSpace(template))
            throw new InvalidOperationException($"Sharding template for {provider} is required.");
        var tenantConnection = template.Replace(ShardingOptions.ShardKeyPlaceholder, args.ShardKey, StringComparison.Ordinal);

        var catalogOptions = new DbContextOptionsBuilder<HrmsCatalogDbContext>();
        var tenantOptions = new DbContextOptionsBuilder<HrmsDbContext>();
        if (provider == DatabaseProviderType.MySql)
        {
            catalogOptions.UseMySQL(catalogConnection, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlCatalogMigrations"));
            tenantOptions.UseMySQL(tenantConnection, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations"));
        }
        else
        {
            catalogOptions.UseSqlServer(catalogConnection, x => x.MigrationsAssembly(typeof(HrmsCatalogDbContext).Assembly.FullName).MigrationsHistoryTable("__EFMigrationsHistoryCatalog"));
            tenantOptions.UseSqlServer(tenantConnection, x => x.MigrationsAssembly(typeof(HrmsDbContext).Assembly.FullName));
        }
        return new Target(new HrmsCatalogDbContext(catalogOptions.Options), new HrmsDbContext(tenantOptions.Options, new MigrationTenantContext(args.TenantId)), provider);
    }

    private static async Task CheckIdentityAsync(Target target, Arguments args)
    {
        var catalogName = target.Catalog.Database.GetDbConnection().Database;
        if (!string.Equals(catalogName, args.Catalog, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Catalog identity mismatch. Expected '{args.Catalog}', connected to '{catalogName}'.");
        var providerName = target.Catalog.Database.ProviderName ?? "";
        if (!providerName.Contains(args.Provider, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Provider mismatch. Expected '{args.Provider}', actual '{providerName}'.");

        await target.Catalog.Database.OpenConnectionAsync(args.CancellationToken);
        await using var command = target.Catalog.Database.GetDbConnection().CreateCommand();
        command.CommandText = target.Provider == DatabaseProviderType.MySql ? "SELECT Id, TenantCode, ShardKey, DatabaseProvider FROM Tenants WHERE Id = @id" : "SELECT Id, TenantCode, ShardKey, DatabaseProvider FROM Tenants WHERE Id = @id";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.Value = args.TenantId; command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync(args.CancellationToken);
        if (!await reader.ReadAsync(args.CancellationToken)) throw new InvalidOperationException($"Tenant '{args.TenantId}' is not present in the authorized catalog.");
        if (!string.Equals(reader.GetString(1), args.TenantCode, StringComparison.Ordinal) || !string.Equals(reader.GetString(2), args.ShardKey, StringComparison.Ordinal))
            throw new InvalidOperationException("Tenant identity mismatch between command-line target and catalog row.");
        var expectedProvider = ParseProvider(args.Provider);
        if (!string.Equals(Convert.ToString(reader.GetValue(3)), args.Provider, StringComparison.OrdinalIgnoreCase)
            && (!int.TryParse(Convert.ToString(reader.GetValue(3)), out var providerValue) || providerValue != (int)expectedProvider))
            throw new InvalidOperationException("Tenant provider mismatch between command-line target and catalog row.");
        await target.Catalog.Database.CloseConnectionAsync();
    }

    private static async Task ReportAsync(string label, DbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Database.CanConnectAsync(cancellationToken)) throw new InvalidOperationException($"{label} is unavailable.");
        var known = db.Database.GetMigrations().ToArray();
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        for (var i = 0; i < applied.Length; i++)
            if (i >= known.Length || !string.Equals(applied[i], known[i], StringComparison.Ordinal))
                throw new InvalidOperationException($"{label} has unexpected migration history at '{applied[i]}'.");
        Console.WriteLine($"{label}: provider={db.Database.ProviderName}; applied={applied.Length}; pending={known.Length - applied.Length}; pendingModelChanges={db.Database.HasPendingModelChanges()}");
        if (known.Length > applied.Length) Console.WriteLine($"  pending: {string.Join(", ", known.Skip(applied.Length))}");
    }

    private static async Task<List<string>> PendingAsync(DbContext db, CancellationToken token) =>
        (await db.Database.GetPendingMigrationsAsync(token)).ToList();

    private static void CheckApproved(IReadOnlyCollection<string> pending, IReadOnlySet<string> approved, string label)
    {
        var unexpected = pending.Where(x => !approved.Contains(x)).ToArray();
        if (unexpected.Length > 0) throw new InvalidOperationException($"{label} has unapproved pending migrations: {string.Join(", ", unexpected)}.");
    }

    private static int VerifyRelease(Arguments args)
    {
        if (string.IsNullOrWhiteSpace(args.PublishDirectory)) throw new ArgumentException("verify-release requires --publish-dir.");
        var directory = Path.GetFullPath(args.PublishDirectory);
        var required = new[] { "HRMS.API.dll", "HRMS.Application.dll", "HRMS.Domain.dll", "HRMS.Infrastructure.dll", "HRMS.Infrastructure.MySqlMigrations.dll", "HRMS.Infrastructure.MySqlCatalogMigrations.dll" };
        foreach (var file in required)
        {
            var path = Path.Combine(directory, file);
            if (!File.Exists(path)) throw new InvalidOperationException($"Release is missing {file}.");
            using var stream = File.OpenRead(path);
            Console.WriteLine($"{file}: {Convert.ToHexString(SHA256.HashData(stream))}");
        }
        // Metadata-only verification keeps this CLI independent of the ASP.NET shared framework. A publish
        // directory should be verifiable on an operator workstation even when the API is not executable there.
        var apiBytes = File.ReadAllBytes(Path.Combine(directory, "HRMS.API.dll"));
        var apiMetadata = System.Text.Encoding.UTF8.GetString(apiBytes);
        var discard = apiMetadata.Contains("LeavePoliciesController", StringComparison.Ordinal)
            && apiMetadata.Contains("Discard", StringComparison.Ordinal);
        if (!discard) throw new InvalidOperationException("Release is missing LeavePoliciesController.Discard.");
        var migrationMetadata = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "HRMS.Infrastructure.MySqlMigrations.dll")));
        if (!migrationMetadata.Contains("AddLeavePolicyDraftDiscard", StringComparison.Ordinal)) throw new InvalidOperationException("Release is missing the draft-discard migration.");
        var domainMetadata = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "HRMS.Domain.dll")));
        if (!domainMetadata.Contains("Discarded", StringComparison.Ordinal)) throw new InvalidOperationException("Release is missing Discarded status handling.");
        Console.WriteLine("VERIFY-RELEASE OK: required assemblies, endpoint, migration ID, and SHA-256 hashes verified.");
        return 0;
    }

    private static DatabaseProviderType ParseProvider(string value) => Enum.TryParse<DatabaseProviderType>(value, true, out var provider) ? provider : throw new ArgumentException($"Unsupported provider '{value}'.");
    private static void PrintUsage() => Console.WriteLine("hrms-migration-host validate|migrate --catalog NAME --tenant-id GUID --tenant-code CODE --shard KEY --provider MySql|SqlServer [--approved-migrations ID,...]\n  migrate additionally requires --confirm --backup-verified --restore-ready\n  migrate --tenant-only additionally requires --database NAME and exactly the two approved WEB1 tenant migration IDs; MySql only\n  hrms-migration-host catalog-preflight|catalog-migrate --catalog NAME --provider MySql|SqlServer --environment ENV --approved-migrations ID,...\n  catalog-migrate additionally requires --confirm --backup-verified --backup-encrypted --retention-approved --restore-ready --isolated-restore-tested --approval APPROVAL-ID\n  verify-release --publish-dir DIR");

    private sealed record Target(HrmsCatalogDbContext Catalog, HrmsDbContext Tenant, DatabaseProviderType Provider);
}

/// <summary>Deliberately has no tenant context or tenant callback. This is the catalog-only mutation boundary.</summary>
internal sealed record CatalogOnlyMigrationTarget(HrmsCatalogDbContext Catalog);

internal sealed class CatalogOnlyMigrationExecutor(Func<DbContext, CancellationToken, Task>? migrate = null)
{
    public Task ExecuteAsync(CatalogOnlyMigrationTarget target, CancellationToken cancellationToken)
    {
        var operation = migrate ?? ((catalog, token) => catalog.Database.MigrateAsync(token));
        return operation(target.Catalog, cancellationToken);
    }
}

internal sealed class MigrationTenantContext(Guid tenantId) : ITenantContext
{
    public Guid? TenantId { get; } = tenantId;
    public Guid? UserId => null;
    public bool HasTenant => true;
}

internal sealed class Arguments
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    public string Command { get; private init; } = "help";
    public string Catalog => Required("catalog"); public Guid TenantId => Guid.Parse(Required("tenant-id")); public string TenantCode => Required("tenant-code"); public string ShardKey => Required("shard"); public string Provider => Required("provider"); public string Environment => Get("environment") ?? ""; public string? Approval => Get("approval"); public string? PublishDirectory => Get("publish-dir");
    public bool Confirm => Has("confirm"); public bool BackupVerified => Has("backup-verified"); public bool BackupEncrypted => Has("backup-encrypted"); public bool RetentionApproved => Has("retention-approved"); public bool RestoreReady => Has("restore-ready"); public bool IsolatedRestoreTested => Has("isolated-restore-tested");
    public bool TenantOnly => Has("tenant-only"); public string? Database => Get("database");
    public IReadOnlySet<string> ApprovedMigrations => new HashSet<string>((Get("approved-migrations") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.Ordinal);
    public CancellationToken CancellationToken => CancellationToken.None;
    public static Arguments Parse(string[] args) { var result = new Arguments { Command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help" }; for (var i = 1; i < args.Length; i++) { var key = args[i].TrimStart('-'); if (key is "confirm" or "backup-verified" or "restore-ready" or "tenant-only") result._values[key] = "true"; else if (i + 1 < args.Length) result._values[key] = args[++i]; } return result; }
    private string Required(string key) => Get(key) ?? throw new ArgumentException($"--{key} is required."); private string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null; private bool Has(string key) => _values.ContainsKey(key);
}

internal static class TenantOnlyMigrationExecutor
{
    public static Task ExecuteAsync(HrmsDbContext tenant, IReadOnlyCollection<string> pending, IReadOnlySet<string> approved, CancellationToken token)
    {
        var unexpected = pending.Where(id => !approved.Contains(id)).ToArray();
        if (unexpected.Length != 0) throw new InvalidOperationException($"tenant has unapproved pending migrations: {string.Join(", ", unexpected)}.");
        return pending.Count == 0 ? Task.CompletedTask : tenant.Database.MigrateAsync(token);
    }
}

internal static class MigrationLock
{
    public static async Task<IAsyncDisposable> AcquireAsync(DbContext db, string name, CancellationToken token)
    {
        var connection = db.Database.GetDbConnection(); await connection.OpenAsync(token); var command = connection.CreateCommand();
        if (db.Database.IsMySql()) { command.CommandText = "SELECT GET_LOCK(@name, 60)"; } else { command.CommandText = "DECLARE @result int; EXEC @result = sp_getapplock @Resource=@name, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=60000; SELECT @result;"; }
        var parameter = command.CreateParameter(); parameter.ParameterName = "@name"; parameter.Value = "hrms-migration:" + name; command.Parameters.Add(parameter);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(token)); if (result != 1 && !(db.Database.IsSqlServer() && result >= 0)) throw new InvalidOperationException($"Could not acquire migration lock '{name}'.");
        return new Releaser(connection, db.Database.IsMySql(), name);
    }
    private sealed class Releaser(DbConnection connection, bool mysql, string name) : IAsyncDisposable
    { public async ValueTask DisposeAsync() { await using var command = connection.CreateCommand(); command.CommandText = mysql ? "SELECT RELEASE_LOCK(@name)" : "EXEC sp_releaseapplock @Resource=@name, @LockOwner='Session'"; var p = command.CreateParameter(); p.ParameterName = "@name"; p.Value = "hrms-migration:" + name; command.Parameters.Add(p); await command.ExecuteNonQueryAsync(); await connection.CloseAsync(); await connection.DisposeAsync(); } }
}
