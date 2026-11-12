using System.Data.Common;

namespace HRMS.Tests.TestSupport;

/// <summary>Phase A adoption contracts. None of these contracts grant a write or delete capability.</summary>
public sealed record EmployeeImportAdoptionManifest(
    int SchemaVersion,
    string OwnershipKind,
    string RunId,
    string ContainerName,
    string ComposeProject,
    string ComposeService,
    IReadOnlyList<string> Databases,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string LifecycleState)
{
    public void Validate(EmployeeImportMySqlAcceptanceConfiguration target)
    {
        if (SchemaVersion != 1 || OwnershipKind != "AdoptedExistingEmpty" || LifecycleState != "PreflightOnly")
            throw new InvalidOperationException("Adoption manifest contract or ownership kind is invalid.");
        if (RunId != target.RunId || ComposeProject != target.ComposeProject || ContainerName != $"{ComposeProject}-mysql-1" || ComposeService != "mysql")
            throw new InvalidOperationException("Adoption manifest target identity does not match the approved F1 target.");
        var expected = new[] { target.CatalogDatabase, target.TenantDatabase, target.MigrationDownDatabase };
        if (Databases.Count != 3 || Databases.Distinct(StringComparer.Ordinal).Count() != 3 || !Databases.ToHashSet(StringComparer.Ordinal).SetEquals(expected))
            throw new InvalidOperationException("Adoption manifest databases must exactly match the three approved targets.");
        if (CreatedAtUtc.Offset != TimeSpan.Zero || (ExpiresAtUtc is { Offset: var offset } && offset != TimeSpan.Zero) ||
            (ExpiresAtUtc is { } expires && expires <= CreatedAtUtc))
            throw new InvalidOperationException("Adoption manifest lifecycle timestamps are invalid.");
    }

    // Deliberately no cleanup/deletion authorization method exists on an adoption manifest.
}

public sealed record DockerContainerObservation(
    string Name, string ComposeProject, string ComposeService, IReadOnlyDictionary<string, string> Labels,
    string RunId, string Hostname, bool Running, bool Healthy, string HostIp, int HostPort, int ContainerPort);

public static class EmployeeImportDockerIdentityVerifier
{
    public static void Verify(DockerContainerObservation observed, string expectedName, string project, string runId, string hostname, int hostPort)
    {
        if (observed.Name != expectedName || observed.ComposeProject != project || observed.ComposeService != "mysql" ||
            observed.RunId != runId || observed.Hostname != hostname || !observed.Running || !observed.Healthy ||
            observed.HostIp != "127.0.0.1" || observed.HostPort != hostPort || observed.ContainerPort != 3306 ||
            !observed.Labels.TryGetValue("com.docker.compose.project", out var projectLabel) || projectLabel != project ||
            !observed.Labels.TryGetValue("com.docker.compose.service", out var serviceLabel) || serviceLabel != "mysql" ||
            !observed.Labels.TryGetValue("com.hrms.owner", out var owner) || owner != "employee-bulk-import-f1" ||
            !observed.Labels.TryGetValue("com.hrms.disposable", out var disposable) || disposable != "true" ||
            !observed.Labels.TryGetValue("com.hrms.run-id", out var labelRun) || labelRun != runId)
            throw new InvalidOperationException("Docker container identity, ownership, health, hostname, or loopback port binding did not match the exact approved F1 target.");
    }
}

public sealed record MySqlDatabaseMetadata(
    string Database,
    IReadOnlyList<string> TablesAndViews,
    IReadOnlyList<string> MigrationHistory,
    IReadOnlyList<string> ProceduresAndFunctions,
    IReadOnlyList<string> Triggers,
    IReadOnlyList<string> Events,
    bool Complete);

public interface IEmployeeImportMySqlMetadataSource
{
    Task<bool> DatabaseExistsAsync(string database, CancellationToken cancellationToken);
    Task<MySqlDatabaseMetadata> ReadMetadataAsync(string database, CancellationToken cancellationToken);
}

public sealed class EmployeeImportMySqlMetadataInspector(IEmployeeImportMySqlMetadataSource source)
{
    public async Task VerifyAllEmptyAsync(IEnumerable<string> approvedDatabases, CancellationToken cancellationToken = default)
    {
        var targets = approvedDatabases.ToArray();
        if (targets.Length != 3 || targets.Distinct(StringComparer.Ordinal).Count() != 3)
            throw new InvalidOperationException("Exactly three unique approved databases are required for adoption.");
        foreach (var database in targets)
        {
            if (!await source.DatabaseExistsAsync(database, cancellationToken))
                throw new InvalidOperationException($"Approved adoption database '{database}' does not exist.");
            var metadata = await source.ReadMetadataAsync(database, cancellationToken);
            if (!metadata.Complete || metadata.Database != database || metadata.TablesAndViews.Count != 0 ||
                metadata.MigrationHistory.Count != 0 || metadata.ProceduresAndFunctions.Count != 0 ||
                metadata.Triggers.Count != 0 || metadata.Events.Count != 0)
                throw new InvalidOperationException($"Database '{database}' is not conclusively empty; adoption is refused.");
        }
    }
}

/// <summary>Read-only MySQL metadata reader. It issues INFORMATION_SCHEMA SELECTs only.</summary>
public sealed class MySqlReadOnlyMetadataSource(Func<CancellationToken, Task<DbConnection>> openConnection) : IEmployeeImportMySqlMetadataSource
{
    public async Task<bool> DatabaseExistsAsync(string database, CancellationToken cancellationToken)
    {
        await using var connection = await openConnection(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @database";
        AddParameter(command, "@database", database);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    public async Task<MySqlDatabaseMetadata> ReadMetadataAsync(string database, CancellationToken cancellationToken)
    {
        var tables = new List<string>(); var history = new List<string>(); var routines = new List<string>();
        var triggers = new List<string>(); var events = new List<string>();
        await using var connection = await openConnection(cancellationToken);
        async Task Read(string sql, List<string> output, bool nameOnly = false)
        {
            await using var command = connection.CreateCommand(); command.CommandText = sql; AddParameter(command, "@database", database);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) output.Add(nameOnly ? reader.GetString(0) : reader.GetString(0) + ":" + reader.GetString(1));
        }
        try
        {
            await Read("SELECT TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=@database", tables);
            if (tables.Any(x => x.StartsWith("__EFMigrationsHistory:", StringComparison.Ordinal))) history.Add("__EFMigrationsHistory");
            await Read("SELECT ROUTINE_NAME, ROUTINE_TYPE FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_SCHEMA=@database", routines);
            await Read("SELECT TRIGGER_NAME FROM INFORMATION_SCHEMA.TRIGGERS WHERE TRIGGER_SCHEMA=@database", triggers, true);
            await Read("SELECT EVENT_NAME FROM INFORMATION_SCHEMA.EVENTS WHERE EVENT_SCHEMA=@database", events, true);
            return new(database, tables, history, routines, triggers, events, true);
        }
        catch
        {
            // An incomplete snapshot is represented explicitly and therefore cannot pass verification.
            return new(database, tables, history, routines, triggers, events, false);
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
    }
}
