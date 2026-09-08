using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Sharding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace HRMS.Infrastructure.Persistence;

/// <inheritdoc cref="ITenantProvisioningService"/>
public sealed class TenantProvisioningService : ITenantProvisioningService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(
        IServiceScopeFactory scopeFactory,
        ILogger<TenantProvisioningService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ProvisionAsync(ShardDescriptor shard, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shard);

        // Its own scope, and that is the whole mechanism rather than tidiness. HrmsDbContext's options are
        // built once per scope from whatever IShardContext holds at that moment, so the shard has to be
        // selected before the context is first resolved — and a scope is write-once, so one scope can only
        // ever provision one organization. Calling this in a loop therefore cannot leak the previous
        // organization's connection into the next one's seeding.
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;

        provider.GetRequiredService<IShardContext>().Use(shard);

        await PrepareMySqlDatabaseAsync(provider, shard, cancellationToken);

        var tenant = await LoadCatalogRowAsync(provider, shard, cancellationToken);

        var db = provider.GetRequiredService<HrmsDbContext>();
        try
        {
            await SchemaPreparer.PrepareAsync(db, $"'{shard.TenantCode}' tenant", _logger, cancellationToken);
        }
        catch (MySqlException ex)
        {
            throw new TenantProvisioningException("MySqlMigrationFailed", "The MySQL tenant migration failed.", ex);
        }

        if (shard.DatabaseProvider == DatabaseProviderType.MySql)
        {
            try
            {
                await VerifyMySqlDatabaseIsCurrentAsync(db, databaseName: GetMySqlDatabaseName(db.Database.GetDbConnection().ConnectionString), cancellationToken);
            }
            catch (MySqlException ex)
            {
                throw new TenantProvisioningException("MySqlMigrationFailed", "The MySQL tenant migration failed.", ex);
            }
        }

        _logger.LogInformation(
            "Seeding organization {TenantCode} on shard {ShardKey}.", shard.TenantCode, shard.ShardKey);

        await DatabaseSeeder.SeedShardAsync(
            db,
            provider.GetRequiredService<IPasswordHasher>(),
            tenant,
            cancellationToken);
    }

    public async Task SynchronizeTenantIdentityAsync(
        ShardDescriptor shard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shard);
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<IShardContext>().Use(shard);
        var db = provider.GetRequiredService<HrmsDbContext>();
        var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == shard.TenantId, cancellationToken);
        if (tenant is null)
            return;

        tenant.TenantCode = shard.TenantCode;
        tenant.Host = shard.Host;
        tenant.ShardKey = shard.ShardKey;
        tenant.Status = shard.Status;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task PrepareMySqlDatabaseAsync(
        IServiceProvider provider,
        ShardDescriptor shard,
        CancellationToken cancellationToken)
    {
        if (shard.DatabaseProvider != DatabaseProviderType.MySql)
            return;

        HrmsDbContext db;
        string databaseConnectionString;
        string databaseName;
        try
        {
            db = provider.GetRequiredService<HrmsDbContext>();
            databaseConnectionString = db.Database.GetDbConnection().ConnectionString;
            databaseName = GetMySqlDatabaseName(databaseConnectionString);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(nameof(ShardingOptions.MySqlConnectionStringTemplate), StringComparison.Ordinal))
        {
            throw new TenantProvisioningException(
                "MySqlServerConfigurationMissing",
                "The trusted MySQL tenant connection template is not configured.",
                ex);
        }

        if (string.IsNullOrWhiteSpace(databaseName) || databaseName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new TenantProvisioningException(
                "MySqlDatabaseNameInvalid",
                "The configured MySQL tenant database name is not a safe identifier.");

        await using (var serverConnection = new MySqlConnection(CreateMySqlServerConnectionString(databaseConnectionString)))
        {
            try
            {
                await serverConnection.OpenAsync(cancellationToken);
            }
            catch (MySqlException ex)
            {
                throw new TenantProvisioningException("MySqlServerConnectionFailed", "The trusted MySQL server connection failed.", ex);
            }
            await using var existsCommand = serverConnection.CreateCommand();
            existsCommand.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @name";
            existsCommand.Parameters.AddWithValue("@name", databaseName);
            bool exists;
            try
            {
                exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync(cancellationToken)) > 0;
            }
            catch (MySqlException ex)
            {
                throw new TenantProvisioningException(
                    "MySqlDatabaseStateUnexpected",
                    "The MySQL server did not return a usable state for the tenant database.",
                    ex);
            }
            if (!exists)
            {
                await using var createCommand = serverConnection.CreateCommand();
                createCommand.CommandText = $"CREATE DATABASE `{databaseName}`";
                try
                {
                    await createCommand.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (MySqlException ex)
                {
                    throw new TenantProvisioningException("MySqlDatabaseCreateFailed", "The MySQL tenant database could not be created.", ex);
                }
                return;
            }
        }

        await using var existingConnection = new MySqlConnection(databaseConnectionString);
        try
        {
            await existingConnection.OpenAsync(cancellationToken);
        }
        catch (MySqlException ex)
        {
            throw new TenantProvisioningException("MySqlTenantConnectionFailed", "The MySQL tenant database connection failed.", ex);
        }
        await using var historyCommand = existingConnection.CreateCommand();
        historyCommand.CommandText = "SELECT MigrationId FROM `__EFMigrationsHistory` ORDER BY MigrationId";
        var applied = new List<string>();
        try
        {
            await using var reader = await historyCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                applied.Add(reader.GetString(0));
        }
        catch (MySqlException ex) when (ex.Number == 1146)
        {
            throw new TenantProvisioningException(
                "MySqlDatabaseStateUnexpected",
                $"MySQL tenant database '{databaseName}' exists without migration history; retry is stopped for operator diagnosis.", ex);
        }
        catch (MySqlException ex)
        {
            throw new TenantProvisioningException(
                "MySqlDatabaseStateUnexpected",
                $"MySQL tenant database '{databaseName}' has unreadable migration history; retry is stopped for operator diagnosis.", ex);
        }

        ValidateMySqlMigrationHistory(applied, db.Database.GetMigrations().ToArray(), databaseName);
    }

    private static async Task VerifyMySqlDatabaseIsCurrentAsync(
        HrmsDbContext db,
        string databaseName,
        CancellationToken cancellationToken)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        ValidateMySqlMigrationHistory(applied, db.Database.GetMigrations().ToArray(), databaseName);

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        if (pending.Length > 0)
            throw new TenantProvisioningException(
                "MySqlMigrationFailed",
                $"MySQL tenant database '{databaseName}' still has pending migrations after preparation.");
    }

    internal static void ValidateMySqlMigrationHistory(
        IReadOnlyList<string> applied,
        IReadOnlyList<string> known,
        string databaseName)
    {
        if (known.Count == 0 || applied.Count == 0 || applied.Count > known.Count)
            throw UnexpectedMigrationHistory(databaseName);

        for (var index = 0; index < applied.Count; index++)
        {
            if (!string.Equals(applied[index], known[index], StringComparison.Ordinal))
                throw UnexpectedMigrationHistory(databaseName);
        }
    }

    private static TenantProvisioningException UnexpectedMigrationHistory(string databaseName) =>
        new(
            "MySqlDatabaseStateUnexpected",
            $"MySQL tenant database '{databaseName}' has incomplete or unexpected migration history; retry is stopped.");

    internal static string GetMySqlDatabaseName(string connectionString) =>
        new MySqlConnectionStringBuilder(connectionString).Database;

    internal static string CreateMySqlServerConnectionString(string tenantConnectionString)
    {
        var builder = new MySqlConnectionStringBuilder(tenantConnectionString)
        {
            Database = string.Empty
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// The organization's row as the catalog holds it, detached.
    /// <para>
    /// Read from the catalog rather than rebuilt from the descriptor or from <see cref="SeedData"/>: the
    /// tenant row a shard carries exists to satisfy its own foreign keys and must say the same thing the
    /// catalog says, and the catalog is the authority. Reading it here is also what lets this provision an
    /// organization created through onboarding, which has no seed data to be rebuilt from at all.
    /// </para>
    /// <para>
    /// <c>AsNoTracking</c> matters beyond performance: the instance is handed to a <em>different</em>
    /// context to insert, and an entity still tracked by the catalog cannot be.
    /// </para>
    /// </summary>
    private static async Task<Tenant> LoadCatalogRowAsync(
        IServiceProvider provider,
        ShardDescriptor shard,
        CancellationToken cancellationToken)
    {
        var catalog = provider.GetRequiredService<HrmsCatalogDbContext>();

        return await catalog.Tenants
                   .AsNoTracking()
                   .SingleOrDefaultAsync(tenant => tenant.Id == shard.TenantId, cancellationToken)
               ?? throw new InvalidOperationException(
                   $"Organization '{shard.TenantCode}' ({shard.TenantId}) has no row in the catalog, so its "
                   + "database cannot be provisioned. The catalog row has to be written first: it is what "
                   + "decides which database the organization's data belongs in.");
    }
}
