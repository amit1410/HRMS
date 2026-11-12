using System.Data.Common;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

internal static class EmployeeImportMigrationPlan
{
    public const string Name = "employee-import-web1";
    public const string Catalog = "hrms_catalog";
    public const string TenantCode = "WEB1";
    public const string TenantHost = "web1.localhost";
    public const string ShardKey = "web1";
    public const string Database = "hrms_web1";
    public const string MySqlProvider = "MySql";
    public const string MySqlVersion = "8.0.46";

    public static IReadOnlyList<string> OrderedMigrationIds { get; } = Array.AsReadOnly(new[]
    {
        "20261028043743_AddEmployeeImportPersistence",
        "20261028052420_AddEmployeeImportClaimLease"
    });
}

internal static class EmployeeCodeAuditMigrationPlan
{
    public const string Name = "employee-code-audit-web1";
    public const string Catalog = "hrms_catalog";
    public const string TenantCode = "WEB1";
    public const string TenantHost = "web1.localhost";
    public const string ShardKey = "web1";
    public const string Database = "hrms_web1";
    public const string Provider = "MySql";
    public const string MigrationId = "20261030034652_AddEmployeeCodeConfigurationAudit";

    public static readonly Guid TenantId = Guid.Parse("16313e68-2e72-4a6a-b689-79b271cec295");
}

internal static class EmployeeImportRecoveryAuditMigrationPlan
{
    public const string Name = "employee-import-recovery-audit-web1";
    public const string Catalog = "hrms_catalog";
    public const string TenantCode = "WEB1";
    public const string TenantHost = "web1.localhost";
    public const string ShardKey = "web1";
    public const string Database = "hrms_web1";
    public const string Provider = "MySql";
    public const string MigrationId = "20261104173339_AddEmployeeImportRecoveryAudit";
    public static readonly Guid TenantId = Guid.Parse("16313e68-2e72-4a6a-b689-79b271cec295");
}

internal static class EmployeeImportRecoveryAuditMigrationGuard
{
    public static void ValidateTarget(Guid tenantId, string catalog, string tenantCode, string shard, string database, string provider)
    {
        Require(tenantId == EmployeeImportRecoveryAuditMigrationPlan.TenantId, "Tenant ID does not match the approved WEB1 identity.");
        Require(catalog.Equals(EmployeeImportRecoveryAuditMigrationPlan.Catalog, StringComparison.OrdinalIgnoreCase), "Catalog must be hrms_catalog.");
        Require(tenantCode == EmployeeImportRecoveryAuditMigrationPlan.TenantCode && shard == EmployeeImportRecoveryAuditMigrationPlan.ShardKey, "Tenant code or shard differs from the approved WEB1 identity.");
        Require(database.Equals(EmployeeImportRecoveryAuditMigrationPlan.Database, StringComparison.OrdinalIgnoreCase), "Physical database must be hrms_web1.");
        Require(provider.Equals(EmployeeImportRecoveryAuditMigrationPlan.Provider, StringComparison.OrdinalIgnoreCase), "Provider must be MySql.");
    }

    public static void ValidatePending(IReadOnlyList<string> pending)
        => Require(pending.SequenceEqual([EmployeeImportRecoveryAuditMigrationPlan.MigrationId], StringComparer.Ordinal),
            "Complete pending tenant migration set must contain exactly the EmployeeImportRecoveryAudit migration.");

    public static bool ValidatePendingOrApplied(IReadOnlyList<string> pending, IReadOnlyList<string> applied)
    {
        if (pending.Count == 0 && applied.Contains(EmployeeImportRecoveryAuditMigrationPlan.MigrationId, StringComparer.Ordinal)) return false;
        ValidatePending(pending);
        return true;
    }

    public static void ValidateApproval(bool confirm, bool backup, bool encrypted, bool retention, bool restore, bool maintenance,
        string? approval, bool approvalAllowlisted, string? backupReference, string? restoreReference)
        => Require(confirm && backup && encrypted && retention && restore && maintenance && !string.IsNullOrWhiteSpace(approval) && approvalAllowlisted
            && !string.IsNullOrWhiteSpace(backupReference) && !string.IsNullOrWhiteSpace(restoreReference),
            "Plan requires allowlisted explicit approval, encrypted retained backup, isolated restore evidence, and a maintenance window.");

    public static void ValidateAuditTableAbsent(bool exists)
        => Require(!exists, "EmployeeImportRecoveryAudits already exists; refusing migration.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class EmployeeCodeAuditMigrationGuard
{
    public static void ValidateTarget(Guid tenantId, string catalog, string tenantCode, string shard, string database, string provider)
    {
        Require(tenantId == EmployeeCodeAuditMigrationPlan.TenantId, "Tenant ID does not match the approved WEB1 identity.");
        Require(catalog.Equals(EmployeeCodeAuditMigrationPlan.Catalog, StringComparison.OrdinalIgnoreCase), "Catalog must be hrms_catalog.");
        Require(tenantCode == EmployeeCodeAuditMigrationPlan.TenantCode && shard == EmployeeCodeAuditMigrationPlan.ShardKey, "Tenant code or shard differs from the approved WEB1 identity.");
        Require(database.Equals(EmployeeCodeAuditMigrationPlan.Database, StringComparison.OrdinalIgnoreCase), "Physical database must be hrms_web1.");
        Require(provider.Equals(EmployeeCodeAuditMigrationPlan.Provider, StringComparison.OrdinalIgnoreCase), "Provider must be MySql.");
    }

    public static void ValidatePending(IReadOnlyList<string> pending)
        => Require(pending.SequenceEqual([EmployeeCodeAuditMigrationPlan.MigrationId], StringComparer.Ordinal),
            "Complete pending tenant migration set must contain exactly 20261030034652_AddEmployeeCodeConfigurationAudit.");

    public static bool ValidatePendingOrApplied(IReadOnlyList<string> pending, IReadOnlyList<string> applied)
    {
        if (pending.Count == 0 && applied.Contains(EmployeeCodeAuditMigrationPlan.MigrationId, StringComparer.Ordinal)) return false;
        ValidatePending(pending);
        return true;
    }

    public static void ValidateApproval(bool confirm, bool backup, bool restore, bool maintenance, string? approval, bool approvalAllowlisted, string? backupReference, string? restoreReference)
        => Require(confirm && backup && restore && maintenance && !string.IsNullOrWhiteSpace(approval) && approvalAllowlisted
            && !string.IsNullOrWhiteSpace(backupReference) && !string.IsNullOrWhiteSpace(restoreReference),
            "Plan requires allowlisted explicit approval, verified backup, isolated restore evidence, and maintenance-window confirmation.");

    public static void ValidateAuditTableAbsent(bool exists)
        => Require(!exists, "EmployeeCodeConfigurationAuditEvents already exists; refusing migration.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class EmployeeCodeAuditMigrationWorkflow
{
    public sealed record State(IReadOnlyList<string> Pending, bool ShouldExecute);

    public static async Task ExecuteAsync(
        Func<CancellationToken, Task<(IReadOnlyList<string> Pending, bool ShouldExecute)>> inspect,
        Func<CancellationToken, Task<IAsyncDisposable>> acquireLock,
        Func<State, CancellationToken, Task> execute,
        CancellationToken token)
    {
        _ = await inspect(token);
        await using var migrationLock = await acquireLock(token);
        var locked = await inspect(token);
        await execute(new State(locked.Pending, locked.ShouldExecute), token);
    }

    public static async Task<bool> AuditTableExistsAsync(HrmsDbContext tenant, CancellationToken token)
    {
        var connection = tenant.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(token);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'EmployeeCodeConfigurationAuditEvents'";
            return Convert.ToInt32(await command.ExecuteScalarAsync(token)) != 0;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}

internal sealed record EmployeeImportTenantRoute(
    Guid Id,
    string TenantCode,
    string Host,
    string ShardKey,
    string Provider,
    int Status);

internal sealed record MySqlRuntimeIdentity(string Database, string Version, string Hostname, uint Port, string ServerUuid);

internal sealed record MigrationIndex(string Table, string Name, bool Unique, IReadOnlyList<string> Columns);

internal sealed record EmployeeImportSchemaBaseline(
    IReadOnlyDictionary<string, string> Tables,
    IReadOnlyDictionary<string, string> Engines,
    IReadOnlyDictionary<string, string> ColumnTypes,
    IReadOnlyList<MigrationIndex> Indexes,
    IReadOnlySet<string> Constraints,
    IReadOnlySet<string> ForeignKeys,
    bool HistoryTablePresent);

internal sealed record EmployeeImportMigrationEvidence(
    EmployeeImportTenantRoute Route,
    string CatalogDatabase,
    string CatalogConfiguredServer,
    uint CatalogConfiguredPort,
    string TenantConfiguredServer,
    uint TenantConfiguredPort,
    MySqlRuntimeIdentity CatalogRuntime,
    MySqlRuntimeIdentity TenantRuntime,
    IReadOnlyList<string> KnownMigrations,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    EmployeeImportSchemaBaseline Schema);

internal sealed record EmployeeImportMigrationExpectation(
    Guid TenantId,
    string EndpointHost,
    uint EndpointPort,
    string RuntimeHostname,
    string RuntimeServerUuid);

internal static class EmployeeImportMigrationGuard
{
    public static void ValidatePlanSelection(bool tenantOnly, string? tenantPlan, IReadOnlyList<string> approvedInOrder)
    {
        var auditSelected = string.Equals(tenantPlan, EmployeeCodeAuditMigrationPlan.Name, StringComparison.Ordinal);
        var auditRequested = approvedInOrder.Contains(EmployeeCodeAuditMigrationPlan.MigrationId, StringComparer.Ordinal);
        if (auditRequested && (!tenantOnly || !auditSelected))
            throw new InvalidOperationException("Employee Code Audit migration requires the explicitly selected WEB1 tenant-only plan.");
        if (auditSelected && (!tenantOnly || !approvedInOrder.SequenceEqual([EmployeeCodeAuditMigrationPlan.MigrationId], StringComparer.Ordinal)))
            throw new InvalidOperationException("Employee Code Audit plan requires exactly its one approved migration ID.");
        var recoveryAuditSelected = string.Equals(tenantPlan, EmployeeImportRecoveryAuditMigrationPlan.Name, StringComparison.Ordinal);
        var recoveryAuditRequested = approvedInOrder.Contains(EmployeeImportRecoveryAuditMigrationPlan.MigrationId, StringComparer.Ordinal);
        if (recoveryAuditRequested && (!tenantOnly || !recoveryAuditSelected))
            throw new InvalidOperationException("Employee Import Recovery Audit migration requires its explicitly selected WEB1 tenant-only plan.");
        if (recoveryAuditSelected && (!tenantOnly || !approvedInOrder.SequenceEqual([EmployeeImportRecoveryAuditMigrationPlan.MigrationId], StringComparer.Ordinal)))
            throw new InvalidOperationException("Employee Import Recovery Audit plan requires exactly its one approved migration ID.");
        var employeeImportSelected = string.Equals(tenantPlan, EmployeeImportMigrationPlan.Name, StringComparison.Ordinal);
        var employeeImportMigrationRequested = approvedInOrder.Any(id =>
            EmployeeImportMigrationPlan.OrderedMigrationIds.Contains(id, StringComparer.Ordinal));
        if (employeeImportMigrationRequested && (!tenantOnly || !employeeImportSelected))
            throw new InvalidOperationException("Employee Import migrations require the explicitly selected WEB1 tenant-only plan.");
        if (tenantPlan is not null && !auditSelected && !recoveryAuditSelected && (!tenantOnly || !employeeImportSelected))
            throw new InvalidOperationException($"Unsupported tenant migration plan '{tenantPlan}'.");
    }

    public static void Validate(EmployeeImportMigrationEvidence evidence, EmployeeImportMigrationExpectation expected)
    {
        ValidateRoute(evidence.Route, expected.TenantId);

        Require(evidence.CatalogDatabase.Equals(EmployeeImportMigrationPlan.Catalog, StringComparison.OrdinalIgnoreCase), "Configured catalog database is not hrms_catalog.");
        Require(evidence.CatalogConfiguredServer.Equals(expected.EndpointHost, StringComparison.OrdinalIgnoreCase), "Catalog endpoint host differs from the approved endpoint.");
        Require(evidence.TenantConfiguredServer.Equals(expected.EndpointHost, StringComparison.OrdinalIgnoreCase), "Tenant endpoint host differs from the approved endpoint.");
        Require(evidence.CatalogConfiguredPort == expected.EndpointPort, "Catalog endpoint port differs from the approved endpoint.");
        Require(evidence.TenantConfiguredPort == expected.EndpointPort, "Tenant endpoint port differs from the approved endpoint.");
        ValidateRuntimeIdentity(evidence.CatalogRuntime, EmployeeImportMigrationPlan.Catalog, expected);
        ValidateRuntimeIdentity(evidence.TenantRuntime, EmployeeImportMigrationPlan.Database, expected);

        ValidateHistoryPrefix(evidence.KnownMigrations, evidence.AppliedMigrations);
        ValidateExactPendingMigrations(evidence.PendingMigrations);
        ValidateSchemaBaseline(evidence.Schema);
    }

    public static void ValidateRoute(EmployeeImportTenantRoute route, Guid expectedTenantId)
    {
        Require(route.Id == expectedTenantId, "Catalog tenant ID does not match the approved WEB1 identity.");
        Require(route.TenantCode == EmployeeImportMigrationPlan.TenantCode, "Catalog tenant code is not WEB1.");
        Require(route.Host.Equals(EmployeeImportMigrationPlan.TenantHost, StringComparison.OrdinalIgnoreCase), "Catalog host is not the approved WEB1 host.");
        Require(route.ShardKey == EmployeeImportMigrationPlan.ShardKey, "Catalog shard key is not the approved WEB1 shard.");
        Require(route.Provider.Equals(EmployeeImportMigrationPlan.MySqlProvider, StringComparison.OrdinalIgnoreCase), "Catalog tenant provider is not MySQL.");
        Require(route.Status == 1, "WEB1 is not active in the catalog.");
    }

    public static void ValidateHistoryPrefix(IReadOnlyList<string> known, IReadOnlyList<string> applied)
    {
        Require(known.Distinct(StringComparer.Ordinal).Count() == known.Count, "Migration assembly contains duplicate migration IDs.");
        Require(applied.Distinct(StringComparer.Ordinal).Count() == applied.Count, "Migration history contains duplicate migration IDs.");
        Require(applied.Count <= known.Count, "Migration history contains more entries than the selected provider assembly.");
        for (var i = 0; i < applied.Count; i++)
            Require(string.Equals(applied[i], known[i], StringComparison.Ordinal), $"Migration history is not a known prefix at '{applied[i]}'.");
    }

    public static void ValidateExactPendingMigrations(IReadOnlyList<string> pending)
    {
        Require(pending.SequenceEqual(EmployeeImportMigrationPlan.OrderedMigrationIds, StringComparer.Ordinal),
            $"Pending migrations must equal the ordered Employee Import set: {string.Join(", ", EmployeeImportMigrationPlan.OrderedMigrationIds)}.");
    }

    public static void ValidateRequestedMigrationOrder(IReadOnlyList<string> requested)
    {
        Require(requested.SequenceEqual(EmployeeImportMigrationPlan.OrderedMigrationIds, StringComparer.Ordinal),
            "The Employee Import plan requires exactly the two approved migration IDs in the required order.");
    }

    public static void ValidateReleaseMigrationInventory(string migrationAssemblyMetadata)
    {
        foreach (var migrationId in EmployeeImportMigrationPlan.OrderedMigrationIds)
            Require(migrationAssemblyMetadata.Contains(migrationId, StringComparison.Ordinal), $"Release MySQL migration assembly is missing '{migrationId}'.");
        Require(migrationAssemblyMetadata.Contains("HrmsDbContextModelSnapshot", StringComparison.Ordinal),
            "Release MySQL migration assembly is missing the model snapshot type.");
    }

    public static void ValidateSchemaBaseline(EmployeeImportSchemaBaseline schema)
    {
        Require(schema.HistoryTablePresent, "EF migration history table is absent.");
        RequireTable(schema, "__efmigrationshistory", "BASE TABLE");
        RequireTable(schema, "importbatches", "BASE TABLE");
        RequireTable(schema, "tenants", "BASE TABLE");
        RequireTable(schema, "users", "BASE TABLE");
        Require(!schema.Tables.ContainsKey("employeeimportoperations"), "EmployeeImportOperations already exists; baseline is partial or unexpected.");
        Require(!schema.Tables.ContainsKey("employeeimportrows"), "EmployeeImportRows already exists; baseline is partial or unexpected.");

        RequireIndex(schema, "importbatches", "PRIMARY", unique: true, "Id");
        RequireIndex(schema, "importbatches", "IX_ImportBatches_TenantId_CreatedDate", unique: false, "TenantId", "CreatedDate");
        Require(!schema.Indexes.Any(x => x.Table == "importbatches" && x.Name == "AK_ImportBatches_TenantId_Id"),
            "ImportBatches composite key already exists; baseline does not match the approved pre-migration state.");
        Require(schema.Constraints.Contains("importbatches|PRIMARY|PRIMARY KEY"), "ImportBatches primary-key constraint is absent.");
        Require(!schema.Constraints.Contains("importbatches|AK_ImportBatches_TenantId_Id|UNIQUE"), "ImportBatches composite unique constraint already exists.");

        RequireIndex(schema, "tenants", "PRIMARY", unique: true, "Id");
        RequireIndex(schema, "tenants", "IX_Tenants_Host", unique: true, "Host");
        RequireIndex(schema, "tenants", "IX_Tenants_ShardKey", unique: true, "ShardKey");
        RequireIndex(schema, "tenants", "IX_Tenants_TenantCode", unique: true, "TenantCode");

        RequireIndex(schema, "users", "PRIMARY", unique: true, "Id");
        RequireIndex(schema, "users", "AK_Users_TenantId_Id", unique: true, "TenantId", "Id");
        RequireIndex(schema, "users", "IX_Users_TenantId_Email", unique: true, "TenantId", "Email");
        Require(schema.Constraints.Contains("tenants|PRIMARY|PRIMARY KEY"), "Tenants primary-key constraint is absent.");
        Require(schema.Constraints.Contains("users|PRIMARY|PRIMARY KEY"), "Users primary-key constraint is absent.");
        Require(schema.Constraints.Contains("users|AK_Users_TenantId_Id|UNIQUE"), "Users composite unique constraint is absent.");
        var expectedIndexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "importbatches|PRIMARY|unique|Id",
            "importbatches|IX_ImportBatches_TenantId_CreatedDate|nonunique|TenantId,CreatedDate",
            "tenants|PRIMARY|unique|Id",
            "tenants|IX_Tenants_Host|unique|Host",
            "tenants|IX_Tenants_ShardKey|unique|ShardKey",
            "tenants|IX_Tenants_TenantCode|unique|TenantCode",
            "users|PRIMARY|unique|Id",
            "users|AK_Users_TenantId_Id|unique|TenantId,Id",
            "users|IX_Users_TenantId_Email|unique|TenantId,Email"
        };
        var actualIndexes = schema.Indexes
            .Where(x => x.Table is "importbatches" or "tenants" or "users")
            .Select(FormatIndex)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Require(actualIndexes.SetEquals(expectedIndexes), "Prerequisite index inventory differs from the approved WEB1 baseline.");

        var expectedForeignKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "importbatches|FK_ImportBatches_Tenants_TenantId|TenantId|tenants|Id"
        };
        Require(schema.ForeignKeys.SetEquals(expectedForeignKeys), "Prerequisite foreign-key inventory differs from the approved WEB1 baseline.");

        RequireColumn(schema, "importbatches", "Id", "char(36)");
        RequireColumn(schema, "importbatches", "TenantId", "char(36)");
        RequireColumn(schema, "tenants", "Id", "char(36)");
        RequireColumn(schema, "users", "Id", "char(36)");
        RequireColumn(schema, "users", "TenantId", "char(36)");
    }

    public static void ValidateRuntimeIdentity(MySqlRuntimeIdentity identity, string expectedDatabase, EmployeeImportMigrationExpectation expected)
    {
        Require(identity.Database.Equals(expectedDatabase, StringComparison.OrdinalIgnoreCase), $"Runtime DATABASE() is not '{expectedDatabase}'.");
        Require(identity.Version == EmployeeImportMigrationPlan.MySqlVersion, "Runtime MySQL version differs from the approved WEB1 baseline.");
        Require(identity.Hostname.Equals(expected.RuntimeHostname, StringComparison.OrdinalIgnoreCase), "Runtime MySQL hostname differs from the explicitly approved server identity.");
        Require(identity.Port == expected.EndpointPort, "Runtime MySQL port differs from the explicitly approved endpoint.");
        Require(identity.ServerUuid.Equals(expected.RuntimeServerUuid, StringComparison.OrdinalIgnoreCase), "Runtime MySQL server UUID differs from the explicitly approved server identity.");
    }

    private static void RequireTable(EmployeeImportSchemaBaseline schema, string name, string expectedType)
    {
        Require(schema.Tables.TryGetValue(name, out var type) && type == expectedType, $"Required prerequisite table '{name}' is absent or is not a base table.");
        Require(schema.Engines.TryGetValue(name, out var engine) && engine.Equals("InnoDB", StringComparison.OrdinalIgnoreCase), $"Required prerequisite table '{name}' is not using InnoDB.");
    }

    private static void RequireColumn(EmployeeImportSchemaBaseline schema, string table, string column, string expectedType)
        => Require(schema.ColumnTypes.TryGetValue($"{table}|{column}", out var type) && type == $"{expectedType}|NO",
            $"Required key column '{table}.{column}' is absent or has an unexpected type/nullability.");

    private static void RequireIndex(EmployeeImportSchemaBaseline schema, string table, string name, bool unique, params string[] columns)
    {
        var index = schema.Indexes.SingleOrDefault(x => x.Table == table && x.Name == name);
        Require(index is not null && index.Unique == unique && index.Columns.SequenceEqual(columns, StringComparer.Ordinal),
            $"Required index '{table}.{name}' is absent or differs from the expected baseline.");
    }

    private static string FormatIndex(MigrationIndex index)
        => $"{index.Table}|{index.Name}|{(index.Unique ? "unique" : "nonunique")}|{string.Join(",", index.Columns)}";

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal static class EmployeeImportMigrationWorkflow
{
    public static async Task ExecuteAsync(
        Func<CancellationToken, Task<EmployeeImportMigrationEvidence>> inspect,
        Func<CancellationToken, Task<IAsyncDisposable>> acquireLock,
        Func<CancellationToken, Task> execute,
        EmployeeImportMigrationExpectation expected,
        CancellationToken cancellationToken)
    {
        EmployeeImportMigrationGuard.Validate(await inspect(cancellationToken), expected);
        await using var migrationLock = await acquireLock(cancellationToken);
        EmployeeImportMigrationGuard.Validate(await inspect(cancellationToken), expected);
        await execute(cancellationToken);
    }
}

internal static class EmployeeImportWeb1MySqlMetadataInspector
{
    public static async Task<MySqlRuntimeIdentity> ReadIdentityAsync(DbContext context, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT DATABASE(), VERSION(), @@hostname, @@port, @@server_uuid";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("MySQL identity query returned no row.");
            return new MySqlRuntimeIdentity(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), Convert.ToUInt32(reader.GetValue(3)), reader.GetString(4));
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    public static async Task<EmployeeImportTenantRoute> ReadRouteAsync(HrmsCatalogDbContext catalog, Guid tenantId, CancellationToken cancellationToken)
    {
        var connection = catalog.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, TenantCode, Host, ShardKey, DatabaseProvider, Status FROM Tenants WHERE Id = @id";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@id";
            parameter.Value = tenantId;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("WEB1 routing row was not found in the catalog.");
            var route = new EmployeeImportTenantRoute(
                Guid.Parse(Convert.ToString(reader.GetValue(0))!),
                Convert.ToString(reader.GetValue(1))!,
                Convert.ToString(reader.GetValue(2))!,
                Convert.ToString(reader.GetValue(3))!,
                Convert.ToString(reader.GetValue(4))!,
                Convert.ToInt32(reader.GetValue(5)));
            if (await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("WEB1 routing identity is ambiguous in the catalog.");
            return route;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    public static async Task<EmployeeImportSchemaBaseline> ReadSchemaAsync(HrmsDbContext tenant, CancellationToken cancellationToken)
    {
        var connection = tenant.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            var tables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var engines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT LOWER(TABLE_NAME), TABLE_TYPE, ENGINE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) IN ('__efmigrationshistory','importbatches','tenants','users','employeeimportoperations','employeeimportrows')";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var table = reader.GetString(0);
                    tables.Add(table, reader.GetString(1));
                    if (!reader.IsDBNull(2)) engines.Add(table, reader.GetString(2));
                }
            }

            var columnTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT LOWER(TABLE_NAME), LOWER(COLUMN_NAME), LOWER(COLUMN_TYPE), IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) IN ('importbatches','tenants','users')";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    columnTypes.Add($"{reader.GetString(0)}|{reader.GetString(1)}", $"{reader.GetString(2)}|{reader.GetString(3)}");
            }

            var indexes = new List<MigrationIndex>();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT LOWER(TABLE_NAME), INDEX_NAME, NON_UNIQUE, SEQ_IN_INDEX, COLUMN_NAME FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) IN ('importbatches','tenants','users','employeeimportoperations','employeeimportrows') ORDER BY TABLE_NAME, INDEX_NAME, SEQ_IN_INDEX";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var table = reader.GetString(0);
                    var name = reader.GetString(1);
                    var nonUnique = Convert.ToInt32(reader.GetValue(2)) != 0;
                    var prior = indexes.LastOrDefault(x => x.Table == table && x.Name == name);
                    if (prior is null) indexes.Add(new MigrationIndex(table, name, !nonUnique, [reader.GetString(4)]));
                    else indexes[^1] = prior with { Columns = prior.Columns.Append(reader.GetString(4)).ToArray() };
                }
            }

            var constraints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT LOWER(TABLE_NAME), CONSTRAINT_NAME, CONSTRAINT_TYPE FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) IN ('importbatches','tenants','users','employeeimportoperations','employeeimportrows')";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) constraints.Add($"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}");
            }

            var foreignKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT LOWER(TABLE_NAME), CONSTRAINT_NAME, COLUMN_NAME, LOWER(REFERENCED_TABLE_NAME), REFERENCED_COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME IS NOT NULL AND LOWER(TABLE_NAME) IN ('importbatches','employeeimportoperations','employeeimportrows') ORDER BY TABLE_NAME, CONSTRAINT_NAME, ORDINAL_POSITION";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    foreignKeys.Add($"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}|{reader.GetString(3)}|{reader.GetString(4)}");
            }

            return new EmployeeImportSchemaBaseline(
                tables,
                engines,
                columnTypes,
                indexes,
                constraints,
                foreignKeys,
                tables.TryGetValue("__efmigrationshistory", out var historyType) && historyType == "BASE TABLE");
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}
