using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class EmployeeImportAdoptionSafetyTests
{
    [Fact]
    public void Docker_identity_mismatch_fails_closed()
    {
        var observation = new DockerContainerObservation("p-mysql-1", "p", "mysql",
            new Dictionary<string, string> { ["com.docker.compose.project"] = "p", ["com.docker.compose.service"] = "mysql", ["com.hrms.owner"] = "employee-bulk-import-f1", ["com.hrms.disposable"] = "true", ["com.hrms.run-id"] = "run123456789" },
            "run123456789", "host", true, true, "0.0.0.0", 13307, 3306);
        Assert.Throws<InvalidOperationException>(() => EmployeeImportDockerIdentityVerifier.Verify(observation, "p-mysql-1", "p", "run123456789", "host", 13307));
    }

    [Theory]
    [InlineData("table")]
    [InlineData("view")]
    [InlineData("migration")]
    [InlineData("routine")]
    [InlineData("trigger")]
    [InlineData("event")]
    public async Task Any_schema_object_category_blocks_adoption(string category)
    {
        var source = new FakeMetadataSource();
        source.Data["catalog"] = category switch
        {
            "table" => source.Data["catalog"] with { TablesAndViews = ["Employees:BASE TABLE"] },
            "view" => source.Data["catalog"] with { TablesAndViews = ["vEmployees:VIEW"] },
            "migration" => source.Data["catalog"] with { MigrationHistory = ["__EFMigrationsHistory"] },
            "routine" => source.Data["catalog"] with { ProceduresAndFunctions = ["f:FUNCTION"] },
            "trigger" => source.Data["catalog"] with { Triggers = ["t"] },
            _ => source.Data["catalog"] with { Events = ["e"] }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EmployeeImportMySqlMetadataInspector(source).VerifyAllEmptyAsync(["catalog", "tenant", "down"]));
    }

    [Fact]
    public async Task Metadata_inspector_requires_all_targets_to_exist_and_be_completely_empty()
    {
        var source = new FakeMetadataSource();
        var inspector = new EmployeeImportMySqlMetadataInspector(source);
        await inspector.VerifyAllEmptyAsync(["catalog", "tenant", "down"]);
        source.Data["tenant"] = source.Data["tenant"] with { Triggers = ["unexpected"] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => inspector.VerifyAllEmptyAsync(["catalog", "tenant", "down"]));
        source.Missing = "down";
        await Assert.ThrowsAsync<InvalidOperationException>(() => inspector.VerifyAllEmptyAsync(["catalog", "tenant", "down"]));
        source.Missing = null;
        source.Data["down"] = source.Data["down"] with { Complete = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => inspector.VerifyAllEmptyAsync(["catalog", "tenant", "down"]));
    }

    private sealed class FakeMetadataSource : IEmployeeImportMySqlMetadataSource
    {
        public string? Missing { get; set; }
        public Dictionary<string, MySqlDatabaseMetadata> Data { get; } = new(StringComparer.Ordinal)
        {
            ["catalog"] = Empty("catalog"), ["tenant"] = Empty("tenant"), ["down"] = Empty("down")
        };
        public Task<bool> DatabaseExistsAsync(string database, CancellationToken cancellationToken) => Task.FromResult(database != Missing);
        public Task<MySqlDatabaseMetadata> ReadMetadataAsync(string database, CancellationToken cancellationToken) => Task.FromResult(Data[database]);
        private static MySqlDatabaseMetadata Empty(string name) => new(name, [], [], [], [], [], true);
    }
}
