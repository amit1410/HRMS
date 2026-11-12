using System.Net;
using System.Text;
using System.Text.Json;
using System.Runtime.Versioning;

namespace BackupEncryptionPrototype;

/// <summary>MySQL CLI restore target bound to one protected, pre-provisioned disposable instance and schema.</summary>
[SupportedOSPlatform("windows")]
public sealed class MySqlIsolatedRestoreTarget : IIsolatedRestoreTarget
{
    private readonly MySqlCliAdapter _cli;
    private readonly MySqlCliProfile _profile;
    private readonly string _database;
    private readonly string _sourceUuid;
    private readonly string _expectedUuid;
    private readonly string _expectedDataDirectory;
    private readonly string _markerPath;
    private MySqlSqlInputProcess? _restoreProcess;
    private RestoreTargetIdentity? _verifiedIdentity;

    public MySqlIsolatedRestoreTarget(MySqlCliAdapter cli, MySqlCliProfile profile, string database,
        string sourceServerUuid, string expectedRestoreServerUuid, string expectedDataDirectory, string disposableMarkerPath)
    {
        _cli = cli ?? throw new ArgumentNullException(nameof(cli));
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        if (!database.StartsWith("hrms_restore_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Restore database must use the isolated hrms_restore_ prefix.");
        _database = database;
        _sourceUuid = sourceServerUuid;
        _expectedUuid = expectedRestoreServerUuid;
        _expectedDataDirectory = Path.GetFullPath(expectedDataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _markerPath = Path.GetFullPath(disposableMarkerPath);
    }

    public RestoreTargetIdentity InspectIdentity()
    {
        if (!File.Exists(_markerPath) || (File.GetAttributes(_markerPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Disposable MySQL instance marker is missing or reparse-linked.");
        RestrictedBackupAcl.VerifyFile(_markerPath);
        if (new FileInfo(_markerPath).Length > 4096) throw new InvalidOperationException("Disposable marker exceeds its size limit.");
        var marker = JsonSerializer.Deserialize<DisposableMarker>(File.ReadAllBytes(_markerPath))
            ?? throw new InvalidDataException("Disposable MySQL marker is malformed.");
        if (!marker.IsDisposable || marker.ServerUuid != _expectedUuid || marker.Database != _database ||
            !NormalizeDirectory(marker.DataDirectory).Equals(_expectedDataDirectory, StringComparison.OrdinalIgnoreCase) ||
            marker.Port != _profile.Port)
            throw new InvalidOperationException("Disposable marker does not match the approved target identity.");

        var identityRows = _cli.QueryAsync(_profile, _database,
            "SELECT @@server_uuid,DATABASE(),@@port,@@datadir,@@event_scheduler").GetAwaiter().GetResult();
        if (identityRows.Count != 1) throw new InvalidOperationException("Restore server identity query did not return one row.");
        var fields = identityRows[0].Split('\t');
        if (fields.Length != 5 || fields[0] != _expectedUuid || fields[1] != _database || fields[4] != "OFF" ||
            !int.TryParse(fields[2], out var port) || port != _profile.Port ||
            !NormalizeDirectory(fields[3]).Equals(_expectedDataDirectory, StringComparison.OrdinalIgnoreCase) ||
            fields[0].Equals(_sourceUuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Restore server UUID, database, data directory, port, scheduler, or source separation check failed.");
        VerifyTlsAndGrants();
        _verifiedIdentity = new RestoreTargetIdentity(_profile.Host, _profile.Port, fields[0], fields[1], true, true, true, _profile.TlsMode);
        return _verifiedIdentity;
    }

    public Stream OpenImportStream()
    {
        var verified = _verifiedIdentity ?? throw new InvalidOperationException("Restore target identity must be verified before import.");
        if (verified.ServerUuid != _expectedUuid || verified.Database != _database) throw new InvalidOperationException("Restore target verification is stale.");
        if (_restoreProcess is not null) throw new InvalidOperationException("A restore process is already active.");
        _restoreProcess = _cli.StartSqlInput(_profile, _database);
        return _restoreProcess.Input;
    }

    public void CompleteImport()
    {
        var process = _restoreProcess ?? throw new InvalidOperationException("No isolated restore process is active.");
        _restoreProcess = null;
        try { process.Complete(); }
        finally { process.Dispose(); }
    }

    public void AbortImport()
    {
        _restoreProcess?.Abort();
        _restoreProcess?.Dispose();
        _restoreProcess = null;
        var identity = InspectIdentity();
        if (identity.ServerUuid != _expectedUuid || identity.Database != _database || identity.ServerUuid == _sourceUuid)
            throw new InvalidOperationException("Refusing cleanup because the disposable target identity changed.");

        var tables = _cli.QueryAsync(_profile, _database, "SHOW FULL TABLES").GetAwaiter().GetResult()
            .Select(ParseObjectRow).ToArray();
        var routines = _cli.QueryAsync(_profile, _database,
            "SELECT ROUTINE_NAME,ROUTINE_TYPE FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=DATABASE() ORDER BY ROUTINE_TYPE,ROUTINE_NAME")
            .GetAwaiter().GetResult().Select(ParseObjectRow).ToArray();
        var events = _cli.QueryAsync(_profile, _database,
            "SELECT EVENT_NAME,'EVENT' FROM information_schema.EVENTS WHERE EVENT_SCHEMA=DATABASE() ORDER BY EVENT_NAME")
            .GetAwaiter().GetResult().Select(ParseObjectRow).ToArray();
        var script = new StringBuilder("SET FOREIGN_KEY_CHECKS=0;\n");
        foreach (var item in tables)
            script.Append(item.Type.Equals("VIEW", StringComparison.OrdinalIgnoreCase) ? "DROP VIEW IF EXISTS " : "DROP TABLE IF EXISTS ")
                .Append(Quote(_database)).Append('.').Append(Quote(item.Name)).Append(";\n");
        foreach (var item in routines)
            script.Append(item.Type.Equals("PROCEDURE", StringComparison.OrdinalIgnoreCase) ? "DROP PROCEDURE IF EXISTS " : "DROP FUNCTION IF EXISTS ")
                .Append(Quote(_database)).Append('.').Append(Quote(item.Name)).Append(";\n");
        foreach (var item in events)
            script.Append("DROP EVENT IF EXISTS ").Append(Quote(_database)).Append('.').Append(Quote(item.Name)).Append(";\n");
        using (var cleanup = _cli.StartSqlInput(_profile, _database))
        {
            using (var writer = new StreamWriter(cleanup.Input, new System.Text.UTF8Encoding(false), 4096, leaveOpen: true))
                writer.Write(script.ToString());
            cleanup.Input.Dispose();
            cleanup.Complete();
        }
        var remaining = _cli.QueryAsync(_profile, _database, "SELECT (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()),(SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema=DATABASE()),(SELECT COUNT(*) FROM information_schema.events WHERE event_schema=DATABASE())")
            .GetAwaiter().GetResult();
        if (remaining.Count != 1 || remaining[0] != "0\t0\t0") throw new InvalidOperationException("Disposable restore schema cleanup did not remove all test objects.");
    }

    public DatabaseSnapshot InspectRestoredSnapshot()
    {
        var identity = InspectIdentity();
        if (identity.ServerUuid != _expectedUuid || identity.Database != _database) throw new InvalidOperationException("Restore target identity changed before snapshot verification.");
        var sourceLike = new VerifiedWeb1BackupSourceForRestore(_profile, _database);
        return ReadSnapshot(sourceLike);
    }

    private DatabaseSnapshot ReadSnapshot(VerifiedWeb1BackupSourceForRestore target)
    {
        var engineRows = _cli.QueryAsync(_profile, target.Database,
            "SELECT COUNT(*),COALESCE(SUM(ENGINE='InnoDB'),0) FROM information_schema.tables WHERE TABLE_SCHEMA=DATABASE() AND TABLE_TYPE='BASE TABLE'").GetAwaiter().GetResult();
        var engine = SingleRow(engineRows, 2);
        var tables = Int(engine[0]);
        var innodb = Int(engine[1]);
        if (tables <= 0 || innodb != tables) throw new InvalidOperationException("Restored tables are not all InnoDB.");
        var migrations = _cli.QueryAsync(_profile, target.Database, "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId").GetAwaiter().GetResult();
        var opRows = _cli.QueryAsync(_profile, target.Database,
            $"SELECT State,TotalRows,SucceededRows,FailedRows,SkippedRows,CASE WHEN ClaimLeaseId IS NULL THEN 'none' WHEN ClaimLeaseExpiresAtUtc<=UTC_TIMESTAMP(6) THEN 'expired' ELSE 'active' END FROM EmployeeImportOperations WHERE TenantId='{MySqlWeb1BackupSource.ExpectedTenantId}' AND Id='{MySqlWeb1BackupSource.ExpectedOperationId}'").GetAwaiter().GetResult();
        var op = SingleRow(opRows, 6);
        var state = ParseState(Int(op[0]));
        var rowCounts = new int[5];
        foreach (var line in _cli.QueryAsync(_profile, target.Database,
                     $"SELECT Status,COUNT(*) FROM EmployeeImportRows WHERE TenantId='{MySqlWeb1BackupSource.ExpectedTenantId}' AND OperationId='{MySqlWeb1BackupSource.ExpectedOperationId}' GROUP BY Status ORDER BY Status").GetAwaiter().GetResult())
        {
            var c = line.Split('\t');
            if (c.Length != 2) throw new InvalidOperationException("Restored import row aggregate is malformed.");
            var i = Int(c[0]);
            if (i is < 0 or > 4) throw new InvalidOperationException("Restored import row status is unknown.");
            rowCounts[i] = Int(c[1]);
        }
        var total = Int(op[1]); var succeeded = Int(op[2]); var failed = Int(op[3]); var skipped = Int(op[4]); var pending = rowCounts[0];
        var lease = op[5];
        if (rowCounts[1] != 0 || rowCounts[2] != succeeded || rowCounts[3] != failed || rowCounts[4] != skipped ||
            pending + succeeded + failed + skipped != total || total != 10_000 || state != "Processing" || lease != "expired")
            throw new InvalidOperationException("Restored import aggregate is inconsistent with row-level outcomes.");
        using var schema = _cli.StartDump(_profile, _database, schemaOnly: true);
        var digest = System.Security.Cryptography.SHA256.HashData(schema.Output);
        var dumpExit = schema.CompleteAndGetExitCode();
        if (dumpExit != 0) throw new MySqlCliException("mysqldump.restore_schema", dumpExit, "schema_dump_failed");
        try { return new(tables, Convert.ToHexString(digest), migrations, new(MySqlWeb1BackupSource.ExpectedOperationId, state, total, succeeded, failed, skipped, pending, lease), true); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(digest); }
    }

    private void VerifyTlsAndGrants()
    {
        var tls = _cli.QueryAsync(_profile, _database, "SHOW SESSION STATUS LIKE 'Ssl_cipher'").GetAwaiter().GetResult();
        if (tls.Count != 1 || tls[0].Split('\t') is not [_, var cipher] || string.IsNullOrWhiteSpace(cipher))
            throw new InvalidOperationException("Restore target did not establish verified TLS.");
        var grants = _cli.QueryAsync(_profile, _database, "SHOW GRANTS FOR CURRENT_USER()").GetAwaiter().GetResult();
        if (grants.Count == 0) throw new InvalidOperationException("Restore account grants are unavailable.");
        var privileges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var grant in grants)
        {
            if (grant.Contains("WITH GRANT OPTION", StringComparison.OrdinalIgnoreCase) || !grant.StartsWith("GRANT ", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Restore account has unsupported or delegable grants.");
            var onIndex = grant.IndexOf(" ON ", StringComparison.OrdinalIgnoreCase);
            if (onIndex < 0) throw new InvalidOperationException("Restore account has a grant without an explicit scope.");
            var scope = grant[(onIndex + 4)..].Split(" TO ", 2, StringSplitOptions.None)[0].Trim();
            if (scope.Equals("*.*", StringComparison.Ordinal))
            {
                if (!grant.StartsWith("GRANT USAGE ON *.*", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Restore account has a global privilege.");
                continue;
            }
            var expectedScope = $"`{_database}`.*";
            if (!scope.Equals(expectedScope, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Restore account can access a database outside the disposable restore target.");
            var list = grant[6..onIndex].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (list.Any(x => x.Equals("ALL PRIVILEGES", StringComparison.OrdinalIgnoreCase)))
            {
                privileges.Add("ALL PRIVILEGES");
                continue;
            }
            foreach (var item in list) privileges.Add(item);
        }
        var required = new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "CREATE", "DROP", "ALTER", "INDEX", "REFERENCES", "TRIGGER", "EVENT", "CREATE ROUTINE", "ALTER ROUTINE", "EXECUTE", "CREATE VIEW", "SHOW VIEW" };
        if (!privileges.Contains("ALL PRIVILEGES") && required.Any(x => !privileges.Contains(x)))
            throw new InvalidOperationException("Restore account lacks the target-scoped privileges required for dump restoration and cleanup.");
    }

    private static (string Name, string Type) ParseObjectRow(string line)
    {
        var fields = line.Split('\t');
        if (fields.Length != 2 || string.IsNullOrWhiteSpace(fields[0]) || fields[0].Length > 64 || string.IsNullOrWhiteSpace(fields[1]))
            throw new InvalidOperationException("Disposable object inventory is malformed.");
        return (fields[0], fields[1]);
    }

    private static string Quote(string identifier) => "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";
    private static string NormalizeDirectory(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private static string[] SingleRow(IReadOnlyList<string> rows, int columns)
    {
        if (rows.Count != 1) throw new InvalidOperationException("Expected one sanitized restore snapshot row.");
        var fields = rows[0].Split('\t');
        if (fields.Length != columns) throw new InvalidOperationException("Sanitized restore snapshot schema is malformed.");
        return fields;
    }
    private static int Int(string value) => int.TryParse(value, out var number) && number >= 0 ? number : throw new InvalidOperationException("Sanitized restore count is malformed.");
    private static string ParseState(int state) => state switch
    {
        0 => "Prepared", 1 => "Claimed", 2 => "Processing", 3 => "Completed", 4 => "Failed", 5 => "PartiallyCompleted", 6 => "Cancelled", 7 => "ArtifactPending", 8 => "Queued",
        _ => throw new InvalidOperationException("Restore operation state is unknown.")
    };
    private sealed record DisposableMarker(bool IsDisposable, string ServerUuid, string Database, string DataDirectory, int Port);
    private sealed record VerifiedWeb1BackupSourceForRestore(MySqlCliProfile Profile, string Database);
}
