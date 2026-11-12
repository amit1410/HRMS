using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace BackupEncryptionPrototype;

public sealed class VerifiedWeb1BackupSource
{
    internal VerifiedWeb1BackupSource(MySqlCliProfile catalogProfile, MySqlCliProfile tenantProfile, StreamingBackupArchive.ArchiveScope scope)
        => (CatalogProfile, TenantProfile, Scope) = (catalogProfile, tenantProfile, scope);
    internal MySqlCliProfile CatalogProfile { get; }
    internal MySqlCliProfile TenantProfile { get; }
    public StreamingBackupArchive.ArchiveScope Scope { get; }
}

[SupportedOSPlatform("windows")]
public static class MySqlWeb1BackupSource
{
    public const string ExpectedTenantId = "16313e68-2e72-4a6a-b689-79b271cec295";
    public const string ExpectedOperationId = "b89d04aa-1419-4c85-a7e3-48ab1d92d111";

    public static async Task<VerifiedWeb1BackupSource> VerifyRoutingAsync(MySqlCliAdapter cli, MySqlCliProfile catalogProfile,
        MySqlCliProfile tenantProfile, string expectedServerUuid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cli);
        if (catalogProfile.Host != tenantProfile.Host || catalogProfile.Port != tenantProfile.Port ||
            catalogProfile.TlsMode != tenantProfile.TlsMode || !Path.GetFullPath(catalogProfile.CaPemPath).Equals(
                Path.GetFullPath(tenantProfile.CaPemPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Catalog and tenant routing profiles must use one endpoint, CA, and TLS mode.");
        if (string.IsNullOrWhiteSpace(expectedServerUuid)) throw new InvalidOperationException("Approved local MySQL UUID is required.");

        var catalogIdentity = await ReadIdentityAsync(cli, catalogProfile, "hrms_catalog", cancellationToken).ConfigureAwait(false);
        var tenantIdentity = await ReadIdentityAsync(cli, tenantProfile, "hrms_web1", cancellationToken).ConfigureAwait(false);
        if (!catalogIdentity.ServerUuid.Equals(expectedServerUuid, StringComparison.OrdinalIgnoreCase) ||
            !tenantIdentity.ServerUuid.Equals(expectedServerUuid, StringComparison.OrdinalIgnoreCase) ||
            !catalogIdentity.ServerUuid.Equals(tenantIdentity.ServerUuid, StringComparison.OrdinalIgnoreCase) ||
            catalogIdentity.Port != catalogProfile.Port || tenantIdentity.Port != tenantProfile.Port ||
            catalogIdentity.Database != "hrms_catalog" || tenantIdentity.Database != "hrms_web1")
            throw new InvalidOperationException("Catalog and tenant server identity do not match the approved local WEB1 instance.");

        var routeRows = await cli.QueryAsync(catalogProfile, "hrms_catalog",
            "SELECT CAST(Id AS CHAR),TenantCode,Host,ShardKey,DatabaseProvider,Status FROM Tenants WHERE TenantCode='WEB1'",
            cancellationToken).ConfigureAwait(false);
        if (routeRows.Count != 1) throw new InvalidOperationException("Catalog did not return exactly one WEB1 route.");
        var route = routeRows[0].Split('\t');
        if (route.Length != 6 || !Guid.TryParse(route[0], out var tenantId) || tenantId.ToString("D") != ExpectedTenantId ||
            route[1] != "WEB1" || route[2] != "web1.localhost" || route[3] != "web1" || route[4] != "MySql" || route[5] != "1")
            throw new InvalidOperationException("Catalog route differs from exact WEB1 host, shard, provider, status, or tenant identity.");

        return new(catalogProfile, tenantProfile, new StreamingBackupArchive.ArchiveScope(
            tenantIdentity.ServerUuid, "WEB1", "hrms_web1", tenantProfile.Host, tenantProfile.Port,
            "hrms_catalog", "MySql", tenantProfile.TlsMode));
    }

    public static async Task<DatabaseSnapshot> ReadSanitizedSnapshotAsync(MySqlCliAdapter cli, VerifiedWeb1BackupSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cli);
        ArgumentNullException.ThrowIfNull(source);
        var engineRows = await cli.QueryAsync(source.TenantProfile, "hrms_web1",
            "SELECT COUNT(*),COALESCE(SUM(ENGINE='InnoDB'),0) FROM information_schema.tables WHERE TABLE_SCHEMA=DATABASE() AND TABLE_TYPE='BASE TABLE'",
            cancellationToken).ConfigureAwait(false);
        var engines = ParseSingleRow(engineRows, 2, "base table engine inventory");
        var tableCount = ParseInt(engines[0], "base table count");
        var innodbCount = ParseInt(engines[1], "InnoDB table count");
        if (tableCount <= 0 || innodbCount != tableCount) throw new InvalidOperationException("Every WEB1 base table must use InnoDB for a consistent snapshot.");

        var migrationRows = await cli.QueryAsync(source.TenantProfile, "hrms_web1",
            "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId", cancellationToken).ConfigureAwait(false);
        if (migrationRows.Count == 0 || migrationRows.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Tenant migration history is empty or malformed.");

        var operationRows = await cli.QueryAsync(source.TenantProfile, "hrms_web1",
            $"SELECT State,TotalRows,SucceededRows,FailedRows,SkippedRows,CASE WHEN ClaimLeaseId IS NULL THEN 'none' WHEN ClaimLeaseExpiresAtUtc<=UTC_TIMESTAMP(6) THEN 'expired' ELSE 'active' END FROM EmployeeImportOperations WHERE TenantId='{ExpectedTenantId}' AND Id='{ExpectedOperationId}'",
            cancellationToken).ConfigureAwait(false);
        var operation = ParseSingleRow(operationRows, 6, "employee import aggregate");
        var state = ParseOperationState(ParseInt(operation[0], "operation state"));
        var total = ParseInt(operation[1], "total rows");
        var succeeded = ParseInt(operation[2], "succeeded rows");
        var failed = ParseInt(operation[3], "failed rows");
        var skipped = ParseInt(operation[4], "skipped rows");
        var lease = operation[5];

        var statusRows = await cli.QueryAsync(source.TenantProfile, "hrms_web1",
            $"SELECT Status,COUNT(*) FROM EmployeeImportRows WHERE TenantId='{ExpectedTenantId}' AND OperationId='{ExpectedOperationId}' GROUP BY Status ORDER BY Status",
            cancellationToken).ConfigureAwait(false);
        var rowCounts = new int[5];
        foreach (var line in statusRows)
        {
            var columns = line.Split('\t');
            if (columns.Length != 2 || !int.TryParse(columns[0], NumberStyles.None, CultureInfo.InvariantCulture, out var status) || status is < 0 or > 4)
                throw new InvalidOperationException("Import row status aggregate is malformed.");
            rowCounts[status] = ParseInt(columns[1], "row status count");
        }
        var pending = rowCounts[0];
        if (rowCounts[1] != 0 || rowCounts[2] != succeeded || rowCounts[3] != failed || rowCounts[4] != skipped ||
            pending + succeeded + failed + skipped != total || total != 10_000 || state != "Processing" || lease != "expired")
            throw new InvalidOperationException("Operation counters, row statuses, state, or lease do not meet the expected safe-backup precondition.");

        using var schemaDump = cli.StartDump(source.TenantProfile, "hrms_web1", schemaOnly: true);
        var schemaDigest = SHA256.HashData(schemaDump.Output);
        var dumpExit = schemaDump.CompleteAndGetExitCode();
        if (dumpExit != 0) throw new MySqlCliException("mysqldump.schema", dumpExit, "schema_dump_failed");
        try
        {
            var schemaHash = Convert.ToHexString(schemaDigest);
            return new DatabaseSnapshot(tableCount, schemaHash, migrationRows,
                new ImportAggregate(ExpectedOperationId, state, total, succeeded, failed, skipped, pending, lease), AllBaseTablesInnoDb: true);
        }
        finally { CryptographicOperations.ZeroMemory(schemaDigest); }
    }

    public static MySqlDumpProcess StartTenantDump(MySqlCliAdapter cli, VerifiedWeb1BackupSource source)
    {
        ArgumentNullException.ThrowIfNull(cli);
        ArgumentNullException.ThrowIfNull(source);
        return cli.StartDump(source.TenantProfile, "hrms_web1");
    }

    private static async Task<(string ServerUuid, string Database, int Port)> ReadIdentityAsync(MySqlCliAdapter cli,
        MySqlCliProfile profile, string database, CancellationToken cancellationToken)
    {
        var identityRows = await cli.QueryAsync(profile, database,
            "SELECT @@server_uuid, DATABASE(), @@port", cancellationToken).ConfigureAwait(false);
        var identity = ParseSingleRow(identityRows, 3, "server identity");
        var sslRows = await cli.QueryAsync(profile, database, "SHOW SESSION STATUS LIKE 'Ssl_cipher'", cancellationToken).ConfigureAwait(false);
        if (sslRows.Count != 1 || sslRows[0].Split('\t') is not [_, var cipher] || string.IsNullOrWhiteSpace(cipher))
            throw new InvalidOperationException("MySQL session did not establish verified TLS.");
        return (identity[0], identity[1], ParseInt(identity[2], "server port"));
    }

    private static string[] ParseSingleRow(IReadOnlyList<string> rows, int columns, string label)
    {
        if (rows.Count != 1) throw new InvalidOperationException($"Expected exactly one {label} row; received {rows.Count}.");
        var values = rows[0].Split('\t');
        if (values.Length != columns) throw new InvalidOperationException($"{label} has an unexpected column count.");
        return values;
    }

    private static int ParseInt(string value, string label)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) && result >= 0
            ? result : throw new InvalidOperationException($"{label} is not a nonnegative integer.");

    private static string ParseOperationState(int value) => value switch
    {
        0 => "Prepared", 1 => "Claimed", 2 => "Processing", 3 => "Completed", 4 => "Failed",
        5 => "PartiallyCompleted", 6 => "Cancelled", 7 => "ArtifactPending", 8 => "Queued",
        _ => throw new InvalidOperationException("Operation state code is invalid.")
    };
}
