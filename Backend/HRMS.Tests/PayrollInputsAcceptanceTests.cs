using System.Text;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
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

internal static class PayrollInputsProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant)
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var earningId = Guid.NewGuid();
        var deductionId = Guid.NewGuid();
        tenant.TenantId = tenantId;
        tenant.UserId = actorId;

        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"PI{tenantId:N}"[..12], TenantName = "Payroll inputs provider", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "PI-001", FirstName = "Provider", LastName = "Employee", Email = $"{employeeId:N}@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
        db.SalaryComponents.AddRange(
            Component(tenantId, earningId, "PI-EARN", SalaryComponentType.Earning),
            Component(tenantId, deductionId, "PI-DEDUCT", SalaryComponentType.Deduction));
        await db.SaveChangesAsync();

        var service = new PayrollInputService(db, tenant, TimeProvider.System, new AllowApprovalGuard());
        var template = await service.CreateTemplateAsync(new PayrollInputTemplateRequest
        {
            Code = "PI_PROVIDER", Name = "Provider template", InputType = PayrollInputType.OneTimeEarning,
            Columns = [new PayrollInputTemplateColumnRequest { SourceColumnName = "EmployeeCode", TargetField = "EmployeeCode", Required = true, Position = 1 }]
        });
        Assert.True(template.Succeeded, template.Message);

        var batch = await service.CreateBatchAsync(new PayrollInputBatchRequest
        {
            Name = "Provider batch", EffectiveDate = new DateOnly(2026, 9, 1), TemplateId = template.Value!.Id,
            Lines =
            [
                new PayrollInputLineRequest { EmployeeCode = "PI-001", ComponentCode = "PI-EARN", InputType = PayrollInputType.OneTimeEarning, Amount = 125, EffectiveDate = new DateOnly(2026, 9, 1) },
                new PayrollInputLineRequest { EmployeeCode = "PI-001", ComponentCode = "PI-DEDUCT", InputType = PayrollInputType.OneTimeDeduction, Amount = 25, EffectiveDate = new DateOnly(2026, 9, 1) }
            ]
        });
        Assert.True(batch.Succeeded, batch.Message);
        Assert.True((await service.ValidateAsync(batch.Value!.Id)).Succeeded);
        Assert.True((await service.GetPreviewAsync(batch.Value.Id)).Succeeded);
        var submitted = await service.SubmitAsync(batch.Value.Id);
        Assert.True(submitted.Succeeded, submitted.Message);
        Assert.Equal(PayrollInputBatchStatus.Submitted, submitted.Value!.Status);
        Assert.Equal(PayrollInputBatchStatus.Submitted, (await db.PayrollInputBatches.SingleAsync(x => x.Id == batch.Value.Id)).Status);
        var approved = await new PayrollInputService(db, tenant, TimeProvider.System, new AllowApprovalGuard()).ApproveAsync(batch.Value.Id);
        Assert.True(approved.Succeeded, approved.Message);
        Assert.True((await service.PostAsync(batch.Value.Id)).Succeeded);
        Assert.True((await service.PostAsync(batch.Value.Id)).Succeeded);

        Assert.Equal(2, await db.PayrollAdjustments.CountAsync(x => x.TenantId == tenantId && x.SourceType == "PayrollInputBatch"));
        Assert.Equal(2, await db.PayrollInputLines.CountAsync(x => x.TenantId == tenantId && x.Status == PayrollInputLineStatus.Posted));
        Assert.True((await service.ExportResultAsync(batch.Value.Id)).Succeeded);
        Assert.True((await service.GetHistoryAsync(batch.Value.Id)).Value!.Count >= 5);
        Assert.False((await new PayrollInputService(db, new TestTenantContext(Guid.NewGuid()), TimeProvider.System).GetBatchesAsync(new PayrollInputBatchQuery())).Value!.Items.Any());

        var templateUpdate = await service.UpdateTemplateAsync(template.Value.Id, new PayrollInputTemplateRequest
        {
            Code = "PI_PROVIDER", Name = "Provider template v2", InputType = PayrollInputType.OneTimeEarning,
            Columns = [new PayrollInputTemplateColumnRequest { SourceColumnName = "Amount", TargetField = "Amount", Required = true, Position = 1 }]
        });
        Assert.True(templateUpdate.Succeeded, templateUpdate.Message);
        Assert.Equal(2, templateUpdate.Value!.Version);
    }

    private static SalaryComponent Component(Guid tenantId, Guid id, string code, SalaryComponentType type) => new()
    {
        Id = id, TenantId = tenantId, Code = code, Name = code, ComponentType = type,
        CalculationType = SalaryCalculationType.ManualInput, StatutoryType = SalaryStatutoryType.None,
        EffectiveFrom = new DateOnly(2025, 1, 1), IsActive = true, AffectsGross = type == SalaryComponentType.Earning,
        AffectsNetPay = true
    };

    private sealed class AllowApprovalGuard : IPayrollApprovalGuard
    {
        public Task<Result<bool>> ValidateAsync(Guid? makerUserId, string action, string? reason = null, CancellationToken ct = default) => Task.FromResult(Result<bool>.Success(true));
    }
}

public sealed class PayrollInputsAcceptanceTests
{
    [Fact]
    public async Task Sequential_post_retry_is_exactly_once()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext();
        await using var db = database.CreateContext(tenant);
        await PayrollInputsProviderAcceptance.RunAsync(db, tenant);
    }

    [Fact]
    public async Task Large_batch_supports_validation_issue_filtering_and_server_paging()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var tenant = new TestTenantContext(tenantId, Guid.NewGuid());
        await using var db = database.CreateContext(tenant);
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"LG{tenantId:N}"[..12], TenantName = "Large payroll input test", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
        var componentId = Guid.NewGuid();
        db.SalaryComponents.Add(new SalaryComponent { Id = componentId, TenantId = tenantId, Code = "LG-EARN", Name = "Large earning", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.ManualInput, EffectiveFrom = new DateOnly(2025, 1, 1), IsActive = true, AffectsGross = true, AffectsNetPay = true });
        for (var i = 0; i < 1000; i++) db.Employees.Add(new Employee { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCode = $"LG-{i:0000}", FirstName = "Large", LastName = $"Employee {i}", Email = $"lg-{i}@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = i < 50 ? EmployeeStatus.Terminated : EmployeeStatus.Active });
        await db.SaveChangesAsync();
        var service = new PayrollInputService(db, tenant, TimeProvider.System);
        var batch = await service.CreateBatchAsync(new PayrollInputBatchRequest { Name = "10k input", EffectiveDate = new DateOnly(2026, 9, 1) });
        Assert.True(batch.Succeeded, batch.Message);
        var csv = new StringBuilder("EmployeeCode,ComponentCode,Amount,EffectiveDate,InputType,Remarks\r\n");
        for (var i = 0; i < 10000; i++)
        {
            var employee = i < 500 ? "LG-MISSING" : $"LG-{(i - 500) % 1000:0000}";
            csv.Append($"{employee},LG-EARN,{i + 1},2026-09-01,OneTimeEarning,\"row,{i} café\"\r\n");
        }
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(csv.ToString()));
        Assert.True((await service.UploadAsync(batch.Value!.Id, content, "large.csv")).Succeeded);
        var validated = await service.ValidateAsync(batch.Value.Id);
        Assert.True(validated.Succeeded, validated.Message);
        Assert.Equal(10000, validated.Value!.TotalRows);
        Assert.Equal(500, validated.Value.InvalidRows);
        Assert.Equal(500, validated.Value.WarningRows);
        Assert.Equal(9500, validated.Value.ValidRows);
        var page = await service.GetLinesAsync(batch.Value.Id, 2, 200, null);
        Assert.True(page.Succeeded);
        Assert.Equal(200, page.Value!.Items.Count);
        Assert.Equal(10000, page.Value.TotalCount);
        var issues = await service.GetIssuesAsync(batch.Value.Id, 1, 100, PayrollInputIssueSeverity.Error);
        Assert.True(issues.Succeeded);
        Assert.Equal(500, issues.Value!.TotalCount);
        Assert.True((await service.ExportPreviewAsync(batch.Value.Id)).Succeeded);
    }
}

public sealed class MySqlPayrollInputsIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_payroll_inputs_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Payroll Inputs acceptance not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var name = $"HRMS_Phase7V_Inputs_{Guid.NewGuid():N}";
        var connection = new MySqlConnectionStringBuilder(normalized) { Database = name };
        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(); await using var create = server.CreateCommand(); create.CommandText = $"CREATE DATABASE `{name}`"; await create.ExecuteNonQueryAsync(); }
            var tenant = new TestTenantContext();
            await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant);
            await db.Database.MigrateAsync();
            await PayrollInputsProviderAcceptance.RunAsync(db, tenant);
        }
        finally
        {
            await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS `{name}`"; await drop.ExecuteNonQueryAsync();
        }
    }
}

public sealed class SqlServerPayrollInputsIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;

    public SqlServerPayrollInputsIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerPayrollInputsFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_inputs_provider_acceptance()
    {
        var tenant = new TestTenantContext();
        await using var db = fixture.CreateContext(tenant);
        await PayrollInputsProviderAcceptance.RunAsync(db, tenant);
    }
}

public sealed class SqlServerPayrollInputsFactAttribute : FactAttribute
{
    public SqlServerPayrollInputsFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable))
        ? $"SQL Server Payroll Inputs tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent."
        : null;
}
