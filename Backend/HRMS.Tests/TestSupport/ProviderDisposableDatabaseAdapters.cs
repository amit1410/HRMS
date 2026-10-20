using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MySql.Data.MySqlClient;
using HRMS.Infrastructure.Persistence;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// Resolves provider credentials only from the explicitly named test environment variables.
/// There is deliberately no configured/development/shared-database fallback.
/// </summary>
public static class ProviderAcceptanceConnectionResolver
{
    public const string SqlServerConnectionVariable = "HRMS_PHASE1E_SQLSERVER_CONNECTION";
    public const string MySqlConnectionVariable = "HRMS_PHASE1E_MYSQL_CONNECTION";

    public static string Resolve(DisposableDatabaseProvider provider)
    {
        var variable = provider == DisposableDatabaseProvider.SqlServer
            ? SqlServerConnectionVariable
            : MySqlConnectionVariable;
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"The approved provider connection must be supplied through {variable}.");
        return value;
    }
}

/// <summary>Common gate used by real provider acceptance tests before any provider connection opens.</summary>
public static class ProviderAcceptanceExecutionGate
{
    public static void Validate(DisposableDatabaseTarget target, DisposableDatabaseApproval approval)
    {
        if (!approval.CreateApproved || !approval.DeleteApproved)
            throw new InvalidOperationException("Provider acceptance requires explicit creation and deletion approval.");
        if (!approval.ConnectionApproved)
            throw new InvalidOperationException("Provider acceptance requires explicit connection approval.");
        if (!approval.MigrationApproved)
            throw new InvalidOperationException("Provider acceptance requires explicit migration approval.");
        if (!string.Equals(target.EnvironmentDesignation, "isolated-provider-acceptance", StringComparison.Ordinal))
            throw new InvalidOperationException("Provider acceptance requires the isolated-provider-acceptance environment.");
        if (!Path.IsPathFullyQualified(target.ManifestPath))
            throw new InvalidOperationException("Provider acceptance requires an absolute ownership manifest path.");
        if (target.RunId.Length < 12 || target.RunId.Contains(' ', StringComparison.Ordinal))
            throw new InvalidOperationException("Provider acceptance requires a unique, non-empty run ID.");
        if (target.ExpectedDatabaseNames.Count == 0 || target.ExpectedDatabaseNames.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Provider acceptance requires exact database names.");
        if (approval.ApprovedProvider is null || !string.Equals(approval.ApprovedProvider, target.Provider.ToString(), StringComparison.Ordinal))
            throw new InvalidOperationException("The explicit approved provider does not match the target.");
        if (approval.ApprovedServerIdentity is null || !string.Equals(approval.ApprovedServerIdentity, target.ServerIdentity, StringComparison.Ordinal))
            throw new InvalidOperationException("The explicit approved server identity does not match the target.");
        if (approval.ApprovedInstanceOrPort is null || !string.Equals(approval.ApprovedInstanceOrPort, target.InstanceOrPort, StringComparison.Ordinal))
            throw new InvalidOperationException("The explicit approved instance/port does not match the target.");
        if (approval.ApprovedRunId is null || !string.Equals(approval.ApprovedRunId, target.RunId, StringComparison.Ordinal))
            throw new InvalidOperationException("The explicit approved run ID does not match the target.");
        if (approval.ApprovedManifestPath is null || !string.Equals(approval.ApprovedManifestPath, target.ManifestPath, StringComparison.Ordinal))
            throw new InvalidOperationException("The explicit approved manifest path does not match the target.");
        if (approval.ApprovedDatabaseNames is null || !approval.ApprovedDatabaseNames.SequenceEqual(target.ExpectedDatabaseNames, StringComparer.Ordinal))
            throw new InvalidOperationException("The explicit approved database allowlist does not match the target.");
        if (approval.ApprovedEnvironmentDesignation is null || !string.Equals(approval.ApprovedEnvironmentDesignation, target.EnvironmentDesignation, StringComparison.Ordinal))
            throw new InvalidOperationException("The explicit test-only environment designation does not match the target.");

        // This check intentionally happens before resolving credentials or opening a connection.
        _ = ProviderAcceptanceConnectionResolver.Resolve(target.Provider);
        if (target.TestOnlyConnectionConfiguration.Contains("Password=", StringComparison.OrdinalIgnoreCase) ||
            target.TestOnlyConnectionConfiguration.Contains("Pwd=", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Credentials must not be embedded in the acceptance target configuration.");
    }
}

public sealed class SqlServerDisposableDatabaseAdapter : IDisposableDatabaseAdapter, IDisposableDatabaseMigrationDownAdapter
{
    public DisposableDatabaseProvider Provider => DisposableDatabaseProvider.SqlServer;

    public async Task<string> VerifyServerIdentityAsync(DisposableDatabaseTarget target, CancellationToken cancellationToken)
    {
        await using var connection = await OpenMasterAsync(target, cancellationToken);
        return Convert.ToString(await ScalarAsync(connection, "SELECT CONVERT(nvarchar(256), SERVERPROPERTY('ServerName'))", cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public async Task<string> VerifyInstanceOrPortAsync(DisposableDatabaseTarget target, CancellationToken cancellationToken)
    {
        await using var connection = await OpenMasterAsync(target, cancellationToken);
        return Convert.ToString(await ScalarAsync(connection,
            "SELECT COALESCE(CONVERT(nvarchar(128), SERVERPROPERTY('InstanceName')), CONVERT(nvarchar(128), SERVERPROPERTY('MachineName')))", cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public async Task<bool> DatabaseExistsAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken)
    {
        ValidateIdentifier(databaseName);
        await using var connection = await OpenMasterAsync(target, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CASE WHEN DB_ID(@name) IS NULL THEN 0 ELSE 1 END";
        command.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = databaseName });
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<bool> TryClaimDatabaseAsync(DisposableDatabaseTarget target, string databaseName, string runId, string ownershipMarker, CancellationToken cancellationToken)
    {
        // Creation is the cross-process atomic claim: SQL Server rejects a second CREATE DATABASE.
        // Never use a development database or a process-local claim to authorize cleanup.
        return !await DatabaseExistsAsync(target, databaseName, cancellationToken);
    }

    public async Task CreateDatabaseAsync(DisposableDatabaseTarget target, string databaseName, string ownershipMarker, CancellationToken cancellationToken)
    {
        ValidateIdentifier(databaseName);
        await using var master = await OpenMasterAsync(target, cancellationToken);
        await using (var create = master.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)}";
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using var database = await OpenDatabaseAsync(target, databaseName, cancellationToken);
            await EnsureMarkerAsync(database, target, databaseName, ownershipMarker, cancellationToken);
        }
        catch
        {
            // The harness leaves the durable manifest ineligible. No automatic delete is attempted here.
            throw;
        }
    }

    public async Task MigrateUpAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken)
    {
        await using var context = CreateContext(target, databaseName);
        await context.Database.MigrateAsync(cancellationToken);
    }

    public async Task MigrateDownAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken)
    {
        await using var context = CreateContext(target, databaseName);
        var migrations = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        if (migrations.Length > 0)
            await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(
                migrations.Length > 1 ? migrations[^2] : null, cancellationToken);
    }

    public HrmsDbContext CreateDbContext(DisposableDatabaseTarget target, string databaseName) => CreateContext(target, databaseName);

    public async Task<DisposableDatabaseOwnershipMarker?> ReadOwnershipMarkerAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken)
    {
        ValidateIdentifier(databaseName);
        await using var connection = await OpenDatabaseAsync(target, databaseName, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RunId, Provider, ServerIdentity, DatabaseName, OwnershipMarker FROM [__HRMS_DisposableOwnership] WHERE Id = 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(reader.GetString(0), Enum.Parse<DisposableDatabaseProvider>(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4));
    }

    public async Task DeleteDatabaseAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken)
    {
        ValidateIdentifier(databaseName);
        await using var connection = await OpenMasterAsync(target, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE {QuoteIdentifier(databaseName)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {QuoteIdentifier(databaseName)}";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<bool> DatabaseIsAbsentAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken) =>
        DatabaseExistsAsync(target, databaseName, cancellationToken).ContinueWith(t => !t.Result, cancellationToken);

    private static async Task EnsureMarkerAsync(SqlConnection connection, DisposableDatabaseTarget target, string databaseName, string marker, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "IF OBJECT_ID(N'[__HRMS_DisposableOwnership]', N'U') IS NULL " +
            "BEGIN CREATE TABLE [__HRMS_DisposableOwnership] (Id int NOT NULL CONSTRAINT PK___HRMS_DisposableOwnership PRIMARY KEY, RunId nvarchar(128) NOT NULL, Provider nvarchar(32) NOT NULL, ServerIdentity nvarchar(256) NOT NULL, DatabaseName nvarchar(128) NOT NULL, OwnershipMarker nvarchar(512) NOT NULL); END; " +
            "IF NOT EXISTS (SELECT 1 FROM [__HRMS_DisposableOwnership] WHERE Id = 1) INSERT INTO [__HRMS_DisposableOwnership] (Id, RunId, Provider, ServerIdentity, DatabaseName, OwnershipMarker) VALUES (1, @runId, @provider, @server, @database, @marker)";
        AddMarkerParameters(command, target, databaseName, marker);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddMarkerParameters(SqlCommand command, DisposableDatabaseTarget target, string databaseName, string marker)
    {
        command.Parameters.AddWithValue("@runId", target.RunId);
        command.Parameters.AddWithValue("@provider", target.Provider.ToString());
        command.Parameters.AddWithValue("@server", target.ServerIdentity);
        command.Parameters.AddWithValue("@database", databaseName);
        command.Parameters.AddWithValue("@marker", marker);
    }

    private static HrmsDbContext CreateContext(DisposableDatabaseTarget target, string databaseName) =>
        new(new DbContextOptionsBuilder<HrmsDbContext>().UseSqlServer(Connection(target, databaseName), sql => sql.MigrationsAssembly(typeof(HrmsDbContext).Assembly.FullName)).Options, new TestTenantContext());

    private static async Task<SqlConnection> OpenMasterAsync(DisposableDatabaseTarget target, CancellationToken token) => await OpenAsync(Connection(target, "master"), token);
    private static async Task<SqlConnection> OpenDatabaseAsync(DisposableDatabaseTarget target, string name, CancellationToken token) => await OpenAsync(Connection(target, name), token);
    private static async Task<SqlConnection> OpenAsync(string connectionString, CancellationToken token) { var connection = new SqlConnection(connectionString); await connection.OpenAsync(token); return connection; }
    private static async Task<object?> ScalarAsync(SqlConnection connection, string sql, CancellationToken token) { await using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(token); }
    private static string Connection(DisposableDatabaseTarget target, string database) { var builder = new SqlConnectionStringBuilder(ProviderAcceptanceConnectionResolver.Resolve(DisposableDatabaseProvider.SqlServer)); builder.InitialCatalog = database; builder.ApplicationName = $"HRMS disposable acceptance {target.RunId}"; return builder.ConnectionString; }
    private static void ValidateIdentifier(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Contains('\0')) throw new InvalidOperationException("Invalid SQL Server database identifier."); }
    internal static string QuoteIdentifier(string value) { ValidateIdentifier(value); return "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]"; }
}

public sealed class MySqlDisposableDatabaseAdapter : IDisposableDatabaseAdapter, IDisposableDatabaseMigrationDownAdapter
{
    public DisposableDatabaseProvider Provider => DisposableDatabaseProvider.MySql;

    public async Task<string> VerifyServerIdentityAsync(DisposableDatabaseTarget target, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(Connection(target, string.Empty), cancellationToken);
        return Convert.ToString(await ScalarAsync(connection, "SELECT @@hostname", cancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public async Task<string> VerifyInstanceOrPortAsync(DisposableDatabaseTarget target, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(Connection(target, string.Empty), cancellationToken);
        return Convert.ToString(await ScalarAsync(connection, "SELECT CAST(@@port AS CHAR)", cancellationToken), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public async Task<bool> DatabaseExistsAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken)
    {
        ValidateIdentifier(databaseName);
        await using var connection = await OpenAsync(Connection(target, string.Empty), cancellationToken);
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @name"; command.Parameters.AddWithValue("@name", databaseName);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }

    public async Task<bool> TryClaimDatabaseAsync(DisposableDatabaseTarget target, string databaseName, string runId, string ownershipMarker, CancellationToken cancellationToken) => !await DatabaseExistsAsync(target, databaseName, cancellationToken);

    public async Task CreateDatabaseAsync(DisposableDatabaseTarget target, string databaseName, string ownershipMarker, CancellationToken cancellationToken)
    {
        ValidateIdentifier(databaseName);
        await using var server = await OpenAsync(Connection(target, string.Empty), cancellationToken);
        await using (var create = server.CreateCommand()) { create.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)}"; await create.ExecuteNonQueryAsync(cancellationToken); }
        await using var database = await OpenAsync(Connection(target, databaseName), cancellationToken);
        await using var marker = database.CreateCommand(); marker.CommandText = "CREATE TABLE IF NOT EXISTS `__HRMS_DisposableOwnership` (Id INT NOT NULL PRIMARY KEY, RunId VARCHAR(128) NOT NULL, Provider VARCHAR(32) NOT NULL, ServerIdentity VARCHAR(256) NOT NULL, DatabaseName VARCHAR(128) NOT NULL, OwnershipMarker VARCHAR(512) NOT NULL); INSERT INTO `__HRMS_DisposableOwnership` (Id, RunId, Provider, ServerIdentity, DatabaseName, OwnershipMarker) SELECT 1, @runId, @provider, @server, @database, @marker WHERE NOT EXISTS (SELECT 1 FROM `__HRMS_DisposableOwnership` WHERE Id = 1);"; AddMarkerParameters(marker, target, databaseName, ownershipMarker); await marker.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MigrateUpAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken) { await using var context = CreateContext(target, databaseName); await context.Database.MigrateAsync(cancellationToken); }
    public async Task MigrateDownAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken) { await using var context = CreateContext(target, databaseName); var migrations = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray(); if (migrations.Length > 0) await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(migrations.Length > 1 ? migrations[^2] : null, cancellationToken); }

    public HrmsDbContext CreateDbContext(DisposableDatabaseTarget target, string databaseName) => CreateContext(target, databaseName);

    public async Task<DisposableDatabaseOwnershipMarker?> ReadOwnershipMarkerAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken)
    {
        ValidateIdentifier(databaseName); await using var connection = await OpenAsync(Connection(target, databaseName), cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "SELECT RunId, Provider, ServerIdentity, DatabaseName, OwnershipMarker FROM `__HRMS_DisposableOwnership` WHERE Id = 1"; await using var reader = await command.ExecuteReaderAsync(cancellationToken); if (!await reader.ReadAsync(cancellationToken)) return null; return new(reader.GetString(0), Enum.Parse<DisposableDatabaseProvider>(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4));
    }

    public async Task DeleteDatabaseAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken) { ValidateIdentifier(databaseName); await using var connection = await OpenAsync(Connection(target, string.Empty), cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = $"DROP DATABASE {QuoteIdentifier(databaseName)}"; await command.ExecuteNonQueryAsync(cancellationToken); }
    public async Task<bool> DatabaseIsAbsentAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken) => !await DatabaseExistsAsync(target, databaseName, cancellationToken);

    private static void AddMarkerParameters(MySqlCommand command, DisposableDatabaseTarget target, string databaseName, string marker) { command.Parameters.AddWithValue("@runId", target.RunId); command.Parameters.AddWithValue("@provider", target.Provider.ToString()); command.Parameters.AddWithValue("@server", target.ServerIdentity); command.Parameters.AddWithValue("@database", databaseName); command.Parameters.AddWithValue("@marker", marker); }
    private static HrmsDbContext CreateContext(DisposableDatabaseTarget target, string databaseName) => new(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(Connection(target, databaseName), x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, new TestTenantContext());
    private static async Task<MySqlConnection> OpenAsync(string connectionString, CancellationToken token) { var connection = new MySqlConnection(connectionString); await connection.OpenAsync(token); return connection; }
    private static async Task<object?> ScalarAsync(MySqlConnection connection, string sql, CancellationToken token) { await using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(token); }
    private static string Connection(DisposableDatabaseTarget target, string database) { var builder = new MySqlConnectionStringBuilder(ProviderAcceptanceConnectionResolver.Resolve(DisposableDatabaseProvider.MySql)) { Database = database }; return builder.ConnectionString; }
    private static void ValidateIdentifier(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Contains('\0')) throw new InvalidOperationException("Invalid MySQL database identifier."); }
    internal static string QuoteIdentifier(string value) { ValidateIdentifier(value); return "`" + value.Replace("`", "``", StringComparison.Ordinal) + "`"; }
}

public interface IDisposableDatabaseMigrationDownAdapter
{
    Task MigrateDownAsync(DisposableDatabaseTarget target, string databaseName, CancellationToken cancellationToken);
}
