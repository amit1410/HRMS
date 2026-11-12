using MySql.Data.MySqlClient;
using System.Text.Json;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// Dedicated, fail-closed configuration gate for Employee Bulk Import MySQL acceptance.
/// This is deliberately independent of the Phase 1E welcome-invitation runner.
/// It performs no I/O and must be evaluated before constructing any provider adapter.
/// </summary>
public sealed record EmployeeImportMySqlAcceptanceConfiguration(
    string RunId,
    string ServerIdentity,
    int HostPort,
    int ServerPort,
    string CatalogDatabase,
    string TenantDatabase,
    string MigrationDownDatabase,
    string ManifestPath,
    string ResultPath,
    string StorageConfigurationPath,
    int MinioPort,
    string ComposeProject,
    string ComposeFile,
    string ConnectionString)
{
    public EmployeeImportDatabaseMode DatabaseMode { get; init; } = EmployeeImportDatabaseMode.CreateNew;
    public static EmployeeImportMySqlAcceptanceConfiguration Read(
        IReadOnlyDictionary<string, string?> values,
        bool allowExistingManifest = false)
    {
        static string Required(IReadOnlyDictionary<string, string?> source, string key) =>
            source.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : throw new InvalidOperationException($"Required isolated employee-import setting {key} is missing.");

        static void RequireApproval(IReadOnlyDictionary<string, string?> source, string key)
        {
            if (!source.TryGetValue(key, out var value) || !string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Explicit approval {key}=true is required before provider acceptance can execute.");
        }

        if (!string.Equals(Required(values, "HRMS_EMPLOYEE_IMPORT_F1_EXECUTE"), "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The dedicated employee-import provider acceptance entry point is disabled.");
        if (!string.Equals(Required(values, "HRMS_EMPLOYEE_IMPORT_F1_ENVIRONMENT"), "isolated-employee-import-f1", StringComparison.Ordinal))
            throw new InvalidOperationException("The employee-import acceptance environment designation is invalid.");

        var modeText = values.TryGetValue("HRMS_EMPLOYEE_IMPORT_F1_DATABASE_MODE", out var configuredMode) && !string.IsNullOrWhiteSpace(configuredMode)
            ? configuredMode.Trim() : nameof(EmployeeImportDatabaseMode.CreateNew);
        if (!Enum.TryParse<EmployeeImportDatabaseMode>(modeText, ignoreCase: false, out var mode) || !Enum.IsDefined(mode))
            throw new InvalidOperationException("Database mode must be exactly CreateNew or AdoptExistingEmpty.");
        if (mode == EmployeeImportDatabaseMode.AdoptExistingEmpty &&
            (!values.TryGetValue("HRMS_EMPLOYEE_IMPORT_F1_APPROVE_ADOPTION", out var adoptionApproval) ||
             !string.Equals(adoptionApproval, "true", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("AdoptExistingEmpty requires separate explicit approval HRMS_EMPLOYEE_IMPORT_F1_APPROVE_ADOPTION=true.");

        foreach (var approval in new[]
                 {
                     "HRMS_EMPLOYEE_IMPORT_F1_APPROVE_CONNECTION",
                     "HRMS_EMPLOYEE_IMPORT_F1_APPROVE_CREATE",
                     "HRMS_EMPLOYEE_IMPORT_F1_APPROVE_MIGRATION",
                     "HRMS_EMPLOYEE_IMPORT_F1_APPROVE_WORKER",
                     "HRMS_EMPLOYEE_IMPORT_F1_APPROVE_DELETE",
                     "HRMS_EMPLOYEE_IMPORT_F1_APPROVE_MINIO_RESTART"
                 })
            RequireApproval(values, approval);

        var runId = Required(values, "HRMS_EMPLOYEE_IMPORT_F1_RUN_ID");
        if (runId.Length is < 12 or > 41 || runId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new InvalidOperationException("The employee-import run ID must be unique, 12-41 characters, and use only ASCII letters, digits, or hyphens.");
        var serverIdentity = Required(values, "HRMS_EMPLOYEE_IMPORT_F1_SERVER_IDENTITY");
        var requiredContainerHost = $"hrmsf1mysql{runId.Replace("-", string.Empty, StringComparison.Ordinal)}";
        if (!string.Equals(serverIdentity, requiredContainerHost, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The expected MySQL server identity must be the unique run-derived F1 container hostname.");

        // MySQL tenant shard routing rejects uppercase shard keys. Keep database identities
        // run-scoped while making the shard name directly routable by the production factory.
        var expectedPrefix = $"hrms_ei1_{runId.Replace('-', '_').ToLowerInvariant()}_";
        var catalog = ValidateDatabaseName(Required(values, "HRMS_EMPLOYEE_IMPORT_F1_CATALOG_DATABASE"), expectedPrefix);
        var tenant = ValidateDatabaseName(Required(values, "HRMS_EMPLOYEE_IMPORT_F1_TENANT_DATABASE"), expectedPrefix);
        var migrationDown = ValidateDatabaseName(Required(values, "HRMS_EMPLOYEE_IMPORT_F1_MIGRATION_DOWN_DATABASE"), expectedPrefix);
        var allowlist = Required(values, "HRMS_EMPLOYEE_IMPORT_F1_DATABASE_ALLOWLIST")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (allowlist.Length != 3 || !allowlist.ToHashSet(StringComparer.Ordinal).SetEquals([catalog, tenant, migrationDown]) ||
            allowlist.Distinct(StringComparer.Ordinal).Count() != 3)
            throw new InvalidOperationException("The exact disposable catalog, tenant, and migration-down names must match the three-database allowlist.");

        if (!int.TryParse(Required(values, "HRMS_EMPLOYEE_IMPORT_F1_HOST_PORT"), out var port) || port is < 1024 or > 65535)
            throw new InvalidOperationException("The approved loopback MySQL host port is invalid.");
        if (!int.TryParse(Required(values, "HRMS_EMPLOYEE_IMPORT_F1_SERVER_PORT"), out var serverPort) || serverPort != 3306)
            throw new InvalidOperationException("The disposable MySQL container port must be explicitly verified as 3306.");

        var connectionString = Required(values, "HRMS_EMPLOYEE_IMPORT_F1_MYSQL_CONNECTION");
        MySqlConnectionStringBuilder connection;
        try { connection = new MySqlConnectionStringBuilder(connectionString); }
        catch (ArgumentException) { throw new InvalidOperationException("The isolated MySQL connection configuration is malformed."); }

        if (!string.Equals(connection.Server, "127.0.0.1", StringComparison.Ordinal) || connection.Port != (uint)port)
            throw new InvalidOperationException("Employee-import acceptance is restricted to the explicitly approved loopback MySQL endpoint.");
        if (!string.IsNullOrEmpty(connection.Database) && !string.Equals(connection.Database, "mysql", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The administrator connection must select only the MySQL system database or no default database.");
        if (connection.Pooling)
            throw new InvalidOperationException("Connection pooling must be disabled for disposable provider acceptance.");

        var manifest = Required(values, "HRMS_EMPLOYEE_IMPORT_F1_MANIFEST_PATH");
        var result = Required(values, "HRMS_EMPLOYEE_IMPORT_F1_RESULT_PATH");
        var storageConfiguration = Required(values, "HRMS_EMPLOYEE_IMPORT_F1_STORAGE_CONFIGURATION");
        if (!Path.IsPathFullyQualified(manifest) || !Path.IsPathFullyQualified(result) || !Path.IsPathFullyQualified(storageConfiguration))
            throw new InvalidOperationException("Manifest, result, and storage configuration paths must be absolute.");
        if (!allowExistingManifest && File.Exists(manifest))
            throw new InvalidOperationException("The ownership manifest already exists; use a new run ID and manifest path.");
        if (!allowExistingManifest && File.Exists(result)) throw new InvalidOperationException("The result path already exists; use a new run-specific result path.");
        if (!int.TryParse(Required(values, "HRMS_EMPLOYEE_IMPORT_F1_MINIO_PORT"), out var minioPort) || minioPort is < 1024 or > 65535)
            throw new InvalidOperationException("The approved loopback MinIO port is invalid.");
        var project = Required(values, "HRMS_EMPLOYEE_IMPORT_F1_COMPOSE_PROJECT");
        if (!string.Equals(project, $"hrms-f1-{runId.ToLowerInvariant()}", StringComparison.Ordinal))
            throw new InvalidOperationException("The Docker Compose project must exactly match this unique F1 run ID.");
        var composeFile = Required(values, "HRMS_EMPLOYEE_IMPORT_F1_COMPOSE_FILE");
        if (!Path.IsPathFullyQualified(composeFile)) throw new InvalidOperationException("The F1 Compose file path must be absolute.");

        // Return the connection only to the caller. Never include it in messages, logs, records, or results.
        return new(runId, serverIdentity, port,
            serverPort, catalog, tenant, migrationDown, manifest, result, storageConfiguration, minioPort,
            project, composeFile, connection.ConnectionString) { DatabaseMode = mode };
    }

    private static string ValidateDatabaseName(string name, string prefix)
    {
        var protectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "hrms_catalog", "hrms_web1", "web1", "hrms", "mysql", "information_schema",
            "performance_schema", "sys", "master", "model", "msdb", "tempdb", "hrms_tenant"
        };
        if (protectedNames.Contains(name) || name.Contains("hrms_catalog", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("hrms_web1", StringComparison.OrdinalIgnoreCase) || name.Contains("web1", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Protected or ambiguous database names are forbidden.");
        if (name.Length > 64 || !name.StartsWith(prefix, StringComparison.Ordinal) ||
            name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException("Disposable database names must use the exact run-specific prefix and a valid MySQL identifier.");
        return name;
    }
}

public static class EmployeeImportMySqlAcceptanceEnvironment
{
    public static IReadOnlyDictionary<string, string?> Snapshot() =>
        typeof(EmployeeImportMySqlAcceptanceEnvironment).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToDictionary(name => name, name => System.Environment.GetEnvironmentVariable(name), StringComparer.Ordinal);

    public const string Execute = "HRMS_EMPLOYEE_IMPORT_F1_EXECUTE";
    public const string DatabaseMode = "HRMS_EMPLOYEE_IMPORT_F1_DATABASE_MODE";
    public const string ApproveAdoption = "HRMS_EMPLOYEE_IMPORT_F1_APPROVE_ADOPTION";
    public const string Environment = "HRMS_EMPLOYEE_IMPORT_F1_ENVIRONMENT";
    public const string RunId = "HRMS_EMPLOYEE_IMPORT_F1_RUN_ID";
    public const string HostPort = "HRMS_EMPLOYEE_IMPORT_F1_HOST_PORT";
    public const string ServerPort = "HRMS_EMPLOYEE_IMPORT_F1_SERVER_PORT";
    public const string ServerIdentity = "HRMS_EMPLOYEE_IMPORT_F1_SERVER_IDENTITY";
    public const string Connection = "HRMS_EMPLOYEE_IMPORT_F1_MYSQL_CONNECTION";
    public const string CatalogDatabase = "HRMS_EMPLOYEE_IMPORT_F1_CATALOG_DATABASE";
    public const string TenantDatabase = "HRMS_EMPLOYEE_IMPORT_F1_TENANT_DATABASE";
    public const string MigrationDownDatabase = "HRMS_EMPLOYEE_IMPORT_F1_MIGRATION_DOWN_DATABASE";
    public const string DatabaseAllowlist = "HRMS_EMPLOYEE_IMPORT_F1_DATABASE_ALLOWLIST";
    public const string ManifestPath = "HRMS_EMPLOYEE_IMPORT_F1_MANIFEST_PATH";
    public const string ResultPath = "HRMS_EMPLOYEE_IMPORT_F1_RESULT_PATH";
    public const string StorageConfiguration = "HRMS_EMPLOYEE_IMPORT_F1_STORAGE_CONFIGURATION";
    public const string MinioPort = "HRMS_EMPLOYEE_IMPORT_F1_MINIO_PORT";
    public const string ComposeProject = "HRMS_EMPLOYEE_IMPORT_F1_COMPOSE_PROJECT";
    public const string ComposeFile = "HRMS_EMPLOYEE_IMPORT_F1_COMPOSE_FILE";
    public const string ApproveConnection = "HRMS_EMPLOYEE_IMPORT_F1_APPROVE_CONNECTION";
    public const string ApproveCreate = "HRMS_EMPLOYEE_IMPORT_F1_APPROVE_CREATE";
    public const string ApproveMigration = "HRMS_EMPLOYEE_IMPORT_F1_APPROVE_MIGRATION";
    public const string ApproveWorker = "HRMS_EMPLOYEE_IMPORT_F1_APPROVE_WORKER";
    public const string ApproveDelete = "HRMS_EMPLOYEE_IMPORT_F1_APPROVE_DELETE";
    public const string ApproveMinioRestart = "HRMS_EMPLOYEE_IMPORT_F1_APPROVE_MINIO_RESTART";
}

public enum EmployeeImportDatabaseMode { CreateNew, AdoptExistingEmpty }

public sealed record EmployeeImportOwnedDatabase(string Name, bool Created, bool MigrationSucceeded, string OwnershipMarker);

public sealed record EmployeeImportMySqlOwnershipManifest(
    int SchemaVersion,
    string RunId,
    string ServerIdentity,
    int HostPort,
    int ServerPort,
    IReadOnlyList<EmployeeImportOwnedDatabase> Databases,
    DateTimeOffset CreatedAtUtc)
{
    public void ValidateCleanup(
        EmployeeImportMySqlAcceptanceConfiguration approved,
        bool deleteApproved,
        IReadOnlyDictionary<string, string> observedDatabaseMarkers)
    {
        if (!deleteApproved) throw new InvalidOperationException("Explicit deletion approval is required.");
        if (SchemaVersion != 1 || !string.Equals(RunId, approved.RunId, StringComparison.Ordinal) ||
            !string.Equals(ServerIdentity, approved.ServerIdentity, StringComparison.Ordinal) || HostPort != approved.HostPort || ServerPort != approved.ServerPort)
            throw new InvalidOperationException("Manifest identity does not match the approved disposable target.");
        var exactNames = new HashSet<string>([approved.CatalogDatabase, approved.TenantDatabase, approved.MigrationDownDatabase], StringComparer.Ordinal);
        if (Databases.Count != 3 || Databases.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != 3 ||
            !Databases.Select(x => x.Name).ToHashSet(StringComparer.Ordinal).SetEquals(exactNames))
            throw new InvalidOperationException("Manifest database names do not match the exact approved allowlist.");

        foreach (var database in Databases)
        {
            var expectedMarker = $"HRMS-EMPLOYEE-IMPORT-F1|{ServerIdentity}|{RunId}|{database.Name}";
            if (!database.Created || !database.MigrationSucceeded || !string.Equals(database.OwnershipMarker, expectedMarker, StringComparison.Ordinal) ||
                !observedDatabaseMarkers.TryGetValue(database.Name, out var actualMarker) ||
                !string.Equals(actualMarker, expectedMarker, StringComparison.Ordinal))
                throw new InvalidOperationException("Cleanup refused because database ownership or successful migration could not be verified.");
        }
    }

    public static async Task WriteNewAsync(EmployeeImportMySqlOwnershipManifest manifest, string path, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("The ownership manifest path must be absolute.");
        if (File.Exists(path)) throw new InvalidOperationException("Refusing to replace an existing ownership manifest.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), cancellationToken);
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static async Task WriteNewOrReplaceAsync(EmployeeImportMySqlOwnershipManifest manifest, string path, CancellationToken cancellationToken = default)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("The ownership manifest path must be absolute.");
        if (!File.Exists(path))
        {
            await WriteNewAsync(manifest, path, cancellationToken);
            return;
        }
        var temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
