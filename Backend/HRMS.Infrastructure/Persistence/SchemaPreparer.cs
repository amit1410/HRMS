using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using MySql.EntityFrameworkCore.Extensions;
using System.Reflection;

namespace HRMS.Infrastructure.Persistence;

/// <summary>
/// Brings one database's schema up to date: EF Core migrations on SQL Server (the production target), and
/// create-from-model on the SQLite development fallback, since SQL Server migrations are provider-specific.
/// <para>
/// Shared by the catalog and by every tenant database, which is the point — "one codebase, one code path"
/// has to hold for provisioning too, or a tenant database created during onboarding would differ from one
/// created at startup.
/// </para>
/// </summary>
internal static class SchemaPreparer
{
    /// <summary>
    /// Performs the same compatibility checks as preparation without creating a database, history table,
    /// schema object, or seed row. This is the production API startup path and the migration host's validate
    /// primitive.
    /// </summary>
    public static async Task<SchemaValidationResult> ValidateAsync(
        DbContext db,
        string label,
        CancellationToken cancellationToken)
    {
        if (!await db.Database.CanConnectAsync(cancellationToken))
            throw new InvalidOperationException($"The {label} database is unavailable.");

        if (db.Database.IsSqlServer() || db.Database.IsMySql())
        {
            var historyRepository = db.GetService<IHistoryRepository>();
            var historyTable = historyRepository.GetType()
                .GetProperty("TableName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?.GetValue(historyRepository) as string
                ?? "__EFMigrationsHistory";
            if (!await HasTableAsync(db, historyTable, cancellationToken))
                throw new InvalidOperationException($"The {label} database is not initialized: migration-history table '{historyTable}' is missing. No changes were made.");

            var known = db.Database.GetMigrations().ToArray();
            var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            var unexpected = applied.Where((migration, index) => index >= known.Length || !string.Equals(migration, known[index], StringComparison.Ordinal)).ToArray();
            if (unexpected.Length > 0)
                throw new InvalidOperationException($"The {label} database has unexpected migration history: {string.Join(", ", unexpected)}.");

            var pending = known.Skip(applied.Length).ToArray();
            if (pending.Length > 0)
                throw new InvalidOperationException($"The {label} database has pending required migrations: {string.Join(", ", pending)}. No changes were made.");

            var relationalMissing = await MissingRelationalSchemaAsync(db, cancellationToken);
            if (relationalMissing.Count > 0)
                throw new InvalidOperationException($"The {label} database is missing required schema objects ({string.Join(", ", relationalMissing)}). No changes were made.");

            var modelChanges = db.Database.HasPendingModelChanges();
            if (modelChanges)
                throw new InvalidOperationException($"The {label} database schema is incompatible with the current model. No changes were made.");
            return new SchemaValidationResult(label, db.Database.ProviderName ?? "unknown", applied, pending, [], modelChanges);
        }

        if (!db.Database.IsSqlite())
            throw new InvalidOperationException($"The {label} database uses unsupported provider '{db.Database.ProviderName}'.");

        var missing = await MissingSchemaAsync(db, cancellationToken);
        if (missing.Count > 0)
            throw new InvalidOperationException($"The SQLite {label} database is missing schema objects ({string.Join(", ", missing)}). No changes were made.");

        return new SchemaValidationResult(label, db.Database.ProviderName ?? "unknown", [], [], missing, false);
    }

    /// <summary>
    /// Prepares <paramref name="db"/>'s schema. <paramref name="label"/> names the database in the log —
    /// with one catalog and one database per organization, "the database" is no longer unambiguous.
    /// </summary>
    public static async Task PrepareAsync(
        DbContext db,
        string label,
        ILogger logger,
        bool allowSqliteDestructiveRebuild,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsSqlServer() || db.Database.IsMySql())
        {
            logger.LogInformation(
                "Applying EF Core migrations to the {Label} database ({Provider}).",
                label,
                db.Database.ProviderName);
            await db.Database.MigrateAsync(cancellationToken);
            return;
        }

        logger.LogWarning(
            "Provider '{Provider}' is not SQL Server; creating the {Label} schema from the model with EnsureCreated (development fallback).",
            db.Database.ProviderName,
            label);

        // EnsureCreated does nothing when the database already exists — it cannot add tables or columns the
        // model has gained since. A development database left over from an earlier phase would therefore be
        // missing schema and fail at the first read, so rebuild it rather than run half-provisioned.
        if (!await db.Database.EnsureCreatedAsync(cancellationToken))
        {
            await RebuildIfSchemaIsStaleAsync(db, label, logger, allowSqliteDestructiveRebuild, cancellationToken);
        }
    }

    /// <summary>
    /// Drops and recreates a SQLite development database whose schema no longer matches the model. Only
    /// ever reached on the development fallback: SQL Server goes through migrations and is never rebuilt.
    /// The data is seeded demo data, so losing it costs nothing — but it is logged as a warning so the
    /// rebuild is never a surprise.
    /// <para>
    /// This is why each context's model must map exactly the tables its own database holds. A context that
    /// maps a table living in a <em>different</em> database reports it missing here, and the repair for a
    /// missing table is to drop the database — so a stray configuration turns into unconditional data loss
    /// on every startup.
    /// </para>
    /// </summary>
    private static async Task RebuildIfSchemaIsStaleAsync(
        DbContext db,
        string label,
        ILogger logger,
        bool allowSqliteDestructiveRebuild,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlite())
        {
            return;
        }

        var missing = await MissingSchemaAsync(db, cancellationToken);
        if (missing.Count == 0)
        {
            return;
        }

        if (!allowSqliteDestructiveRebuild)
        {
            throw new InvalidOperationException(
                $"The SQLite {label} database is missing schema objects ({string.Join(", ", missing)}). "
                + "It was not modified. Apply a migration or replace only a disposable development database; "
                + "set Database:AllowSqliteDestructiveRebuild to true only for a disposable store.");
        }

        logger.LogWarning(
            "The development {Label} database is missing {Count} table(s)/column(s) ({Missing}) and predates the "
            + "current model. Recreating it from scratch; seeded development data will be rebuilt.",
            label,
            missing.Count,
            string.Join(", ", missing));

        await db.Database.EnsureDeletedAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }

    /// <summary>
    /// The <c>Table.Column</c> pairs the model expects and the database does not have.
    /// <para>
    /// Columns and not just tables, which matters: a new table is obvious the moment anything reads it, but
    /// a new <em>column</em> on a table that already exists produces a "no such column" error from inside a
    /// query that used to work — far from the change that caused it. Comparing at column granularity means
    /// adding a property to an existing entity triggers the same rebuild that adding an entity does.
    /// </para>
    /// </summary>
    private static async Task<List<string>> MissingSchemaAsync(DbContext db, CancellationToken cancellationToken)
    {
        var expected = db.Model.GetEntityTypes()
            .Where(entityType => !string.IsNullOrEmpty(entityType.GetTableName()))
            .SelectMany(entityType => entityType.GetProperties()
                .Select(property => $"{entityType.GetTableName()}.{property.GetColumnName()}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // pragma_table_info as a table-valued function, so one round trip covers every table's columns.
        // A table that is missing entirely contributes no rows here and therefore reports all of its
        // columns as missing, which is the same rebuild by a different route.
        var present = await db.Database
            .SqlQuery<string>(
                $"""
                SELECT m.name || '.' || p.name AS "Value"
                FROM sqlite_master AS m
                JOIN pragma_table_xinfo(m.name) AS p
                WHERE m.type = 'table'
                """)
            .ToListAsync(cancellationToken);

        return expected.Except(present, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<List<string>> MissingRelationalSchemaAsync(DbContext db, CancellationToken cancellationToken)
    {
        var expected = db.Model.GetEntityTypes()
            .Where(entityType => !string.IsNullOrWhiteSpace(entityType.GetTableName()))
            .SelectMany(entityType => entityType.GetProperties()
                .Select(property => $"{entityType.GetTableName()}.{property.GetColumnName()}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = db.Database.IsMySql()
            ? "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = DATABASE()"
            : "SELECT t.name, c.name FROM sys.tables t JOIN sys.columns c ON c.object_id = t.object_id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            present.Add($"{reader.GetString(0)}.{reader.GetString(1)}");
        return expected.Except(present, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<bool> HasTableAsync(DbContext db, string table, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = db.Database.IsMySql()
            ? "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @table"
            : "SELECT COUNT(*) FROM sys.tables WHERE name = @table";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@table";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }
}

public sealed record SchemaValidationResult(
    string Label,
    string Provider,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    IReadOnlyList<string> MissingSchemaObjects,
    bool HasPendingModelChanges);
