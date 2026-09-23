using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

internal static class StatutoryFilingProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant)
    {
        var tenantId = Guid.NewGuid(); var makerId = Guid.NewGuid(); var checkerId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var batchId = Guid.NewGuid(); var employeeRowId = Guid.NewGuid();
        tenant.TenantId = tenantId; tenant.UserId = makerId;
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"SF{tenantId:N}"[..12], TenantName = "Statutory filing provider tenant", Host = $"{tenantId:N}.filing.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
        db.Users.AddRange(new User { Id = makerId, TenantId = tenantId, Email = $"{makerId:N}@filing.test", PasswordHash = "provider-test-hash", FirstName = "Filing", LastName = "Maker", IsActive = true }, new User { Id = checkerId, TenantId = tenantId, Email = $"{checkerId:N}@filing.test", PasswordHash = "provider-test-hash", FirstName = "Filing", LastName = "Checker", IsActive = true });
        db.PayrollControlConfigurations.Add(new PayrollControlConfiguration { Id = Guid.NewGuid(), TenantId = tenantId, RequireMakerChecker = true, PreventSelfApproval = true, UpdatedAtUtc = DateTime.UtcNow, UpdatedByUserId = makerId });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "SF-001", FirstName = "Statutory", LastName = "Employee", Email = $"{employeeId:N}@filing.test", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active });
        var period = new PayrollCompliancePeriod { Id = periodId, TenantId = tenantId, JurisdictionCode = "IN", ComplianceType = PayrollComplianceType.IncomeTaxTds, PeriodStart = new(2026, 9, 1), PeriodEnd = new(2026, 9, 30), Status = PayrollCompliancePeriodStatus.Open };
        var batch = new PayrollStatutoryReturnBatch { Id = batchId, TenantId = tenantId, PayrollCompliancePeriodId = periodId, ComplianceType = PayrollComplianceType.IncomeTaxTds, JurisdictionCode = "IN", BatchNumber = "SF-STAT-001", Status = PayrollStatutoryReturnStatus.Approved, EmployeeCount = 1, TotalPayable = 750m };
        batch.Employees.Add(new PayrollStatutoryReturnEmployee { Id = employeeRowId, TenantId = tenantId, PayrollStatutoryReturnBatchId = batchId, EmployeeId = employeeId, EmployeeCodeSnapshot = "SF-001", EmployeeNameSnapshot = "Statutory Employee", Pan = "ABCDE1234F", GrossWages = 10000m, StatutoryWages = 10000m, EmployeeContribution = 750m, DeductionAmount = 750m, PayableAmount = 750m, Sequence = 1 });
        db.PayrollCompliancePeriods.Add(period); db.PayrollStatutoryReturnBatches.Add(batch); await db.SaveChangesAsync();

        var service = new StatutoryFilingService(db, tenant, TimeProvider.System, new PayrollApprovalGuard(db, tenant));
        var definition = await service.CreateDefinitionAsync(new StatutoryFilingDefinitionRequest { Code = "TDS-GENERIC", Name = "Configured tax return export", FilingType = "TaxDeductionReturn", JurisdictionCode = "IN", DestinationType = StatutoryFilingDestinationType.ManualDownload }); Assert.True(definition.Succeeded, definition.Message);
        var run = await service.CreateRunAsync(new StatutoryFilingRunRequest { DefinitionId = definition.Value!.Id, FilingPeriod = "2026-09" }); Assert.True(run.Succeeded, run.Message);
        var generated = await service.GenerateAsync(run.Value!.Id); Assert.True(generated.Succeeded, generated.Message); Assert.Equal(1, generated.Value!.RowCount);
        var validated = await service.ValidateAsync(run.Value.Id); Assert.True(validated.Succeeded, validated.Message);
        Assert.True((await service.SubmitForApprovalAsync(run.Value.Id)).Succeeded);
        Assert.False((await service.ApproveAsync(run.Value.Id)).Succeeded);
        tenant.UserId = checkerId; var approved = await service.ApproveAsync(run.Value.Id); Assert.True(approved.Succeeded, approved.Message);
        var package = await service.GetPackageAsync(run.Value.Id); Assert.True(package.Succeeded); Assert.False(string.IsNullOrWhiteSpace(package.Value!.PackageHash));
        var submitted = await service.SubmitAsync(run.Value.Id); Assert.True(submitted.Succeeded, submitted.Message); var repeated = await service.SubmitAsync(run.Value.Id); Assert.True(repeated.Succeeded, repeated.Message);
        var acknowledged = await service.AcknowledgeAsync(run.Value.Id, new StatutoryFilingAcknowledgementRequest { ReferenceNumber = "MANUAL-ACK-001", Notes = "Recorded after operator upload." }); Assert.True(acknowledged.Succeeded, acknowledged.Message);
        var download = await service.DownloadAsync(run.Value.Id); Assert.True(download.Succeeded); Assert.Contains("SF-001", System.Text.Encoding.UTF8.GetString(download.Value!.Content), StringComparison.Ordinal);
        Assert.Equal(1, await db.StatutoryFilingSubmissions.CountAsync(x => x.TenantId == tenantId && x.PackageId == package.Value.Id)); Assert.Equal(1, await db.StatutoryFilingAcknowledgements.CountAsync(x => x.TenantId == tenantId));

        tenant.UserId = makerId;
        var testProfile = await service.CreateConnectionProfileAsync(new StatutoryFilingConnectionProfileRequest
        {
            Name = "Deterministic test connector",
            ConnectorType = StatutoryFilingConnectorType.Test,
            Endpoint = "test://statutory-filing",
            NonSecretConfigurationJson = "{\"outcome\":\"rejected\"}",
            SecretReference = "vault://deferred/test-connector",
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        Assert.True(testProfile.Succeeded, testProfile.Message);
        Assert.True(testProfile.Value!.HasSecretReference);
        var profiles = await service.GetConnectionProfilesAsync();
        Assert.True(profiles.Succeeded);
        Assert.DoesNotContain("vault://deferred/test-connector", System.Text.Json.JsonSerializer.Serialize(profiles.Value));

        var configuredDefinition = await service.CreateDefinitionAsync(new StatutoryFilingDefinitionRequest
        {
            Code = "TDS-CONNECTOR",
            Name = "Deterministic connector rejection/resubmission",
            FilingType = "TaxDeductionReturn",
            JurisdictionCode = "IN",
            DestinationType = StatutoryFilingDestinationType.HttpApi,
            ConnectionProfileId = testProfile.Value.Id
        });
        Assert.True(configuredDefinition.Succeeded, configuredDefinition.Message);
        var configuredRun = await service.CreateRunAsync(new StatutoryFilingRunRequest { DefinitionId = configuredDefinition.Value!.Id, FilingPeriod = "2026-09" });
        Assert.True(configuredRun.Succeeded, configuredRun.Message);
        var connectorGenerated = await service.GenerateAsync(configuredRun.Value!.Id);
        Assert.True(connectorGenerated.Succeeded, connectorGenerated.Message);
        var connectorValidated = await service.ValidateAsync(configuredRun.Value.Id);
        Assert.True(connectorValidated.Succeeded, connectorValidated.Message);
        Assert.True((await service.SubmitForApprovalAsync(configuredRun.Value.Id)).Succeeded);
        tenant.UserId = checkerId;
        Assert.True((await service.ApproveAsync(configuredRun.Value.Id)).Succeeded);
        var rejectedPackage = await service.GetPackageAsync(configuredRun.Value.Id);
        Assert.True(rejectedPackage.Succeeded);
        Assert.True((await service.SubmitAsync(configuredRun.Value.Id)).Succeeded);
        var rejectedRun = await db.StatutoryFilingRuns.AsNoTracking().SingleAsync(x => x.Id == configuredRun.Value.Id);
        Assert.Equal(StatutoryFilingRunStatus.Rejected, rejectedRun.Status);
        var originalSubmission = await db.StatutoryFilingSubmissions.AsNoTracking().SingleAsync(x => x.RunId == configuredRun.Value.Id);
        var originalHash = rejectedPackage.Value!.PackageHash;

        var sourceRow = await db.PayrollStatutoryReturnEmployees.SingleAsync(x => x.Id == employeeRowId);
        sourceRow.PayableAmount = 751m;
        await db.SaveChangesAsync();
        tenant.UserId = makerId;
        var acceptedProfile = await service.UpdateConnectionProfileAsync(testProfile.Value.Id, new StatutoryFilingConnectionProfileRequest
        {
            Name = "Deterministic test connector",
            ConnectorType = StatutoryFilingConnectorType.Test,
            Endpoint = "test://statutory-filing",
            NonSecretConfigurationJson = "{\"outcome\":\"accepted\"}",
            SecretReference = "vault://deferred/test-connector",
            EffectiveFrom = new DateOnly(2026, 1, 1)
        });
        Assert.True(acceptedProfile.Succeeded, acceptedProfile.Message);
        Assert.True((await service.GenerateAsync(configuredRun.Value.Id)).Succeeded);
        var correctedPackage = await service.GetPackageAsync(configuredRun.Value.Id);
        Assert.True(correctedPackage.Succeeded);
        Assert.NotEqual(originalHash, correctedPackage.Value!.PackageHash);
        Assert.Equal(2, correctedPackage.Value.Version);
        Assert.True((await service.ValidateAsync(configuredRun.Value.Id)).Succeeded);
        Assert.True((await service.SubmitForApprovalAsync(configuredRun.Value.Id)).Succeeded);
        tenant.UserId = checkerId;
        Assert.True((await service.ApproveAsync(configuredRun.Value.Id)).Succeeded);
        Assert.True((await service.SubmitAsync(configuredRun.Value.Id)).Succeeded);
        var submissions = await db.StatutoryFilingSubmissions.AsNoTracking().Where(x => x.RunId == configuredRun.Value.Id).OrderBy(x => x.CreatedDate).ToListAsync();
        Assert.Equal(2, submissions.Count);
        Assert.Equal(originalSubmission.Id, submissions[0].Id);
        Assert.Equal(originalSubmission.Id, submissions[1].ResubmissionOfSubmissionId);
        Assert.Equal(2, await db.StatutoryFilingPackages.CountAsync(x => x.RunId == configuredRun.Value.Id));
        Assert.Equal(originalHash, (await db.StatutoryFilingPackages.AsNoTracking().SingleAsync(x => x.Id == rejectedPackage.Value.Id)).PackageHash);
    }
}

public sealed class MySqlStatutoryFilingIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_statutory_filing_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION"); if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Statutory Filing acceptance not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured); var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty }; var name = $"HRMS_Phase7X_Filing_{Guid.NewGuid():N}"; var connection = new MySqlConnectionStringBuilder(normalized) { Database = name };
        try { await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(); await using var create = server.CreateCommand(); create.CommandText = $"CREATE DATABASE `{name}`"; await create.ExecuteNonQueryAsync(); } var tenant = new TestTenantContext(); await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant); await db.Database.MigrateAsync(); await StatutoryFilingProviderAcceptance.RunAsync(db, tenant); }
        finally { await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS `{name}`"; await drop.ExecuteNonQueryAsync(); }
    }
}

public sealed class SqlServerStatutoryFilingIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerStatutoryFilingIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;
    [SqlServerStatutoryFilingFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_statutory_filing_provider_acceptance() { var tenant = new TestTenantContext(); await using var db = fixture.CreateContext(tenant); await StatutoryFilingProviderAcceptance.RunAsync(db, tenant); }
}

public sealed class SqlServerStatutoryFilingFactAttribute : FactAttribute
{
    public SqlServerStatutoryFilingFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable)) ? $"SQL Server Statutory Filing tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
