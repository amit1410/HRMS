using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlSalaryComponentIntegrationTests
{
    [Fact]
    public async Task MySql_salary_component_master_preserves_provider_neutral_semantics()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured))
            throw SkipException.ForSkip("MySQL Salary Component tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var databaseName = $"HRMS_Phase7A_SalaryComponent_{Guid.NewGuid():N}";
        var connection = MySqlApiFactory.NormalizeConnectionString(configured);
        var builder = new MySqlConnectionStringBuilder(connection);
        builder.Database = databaseName;
        var adminBuilder = new MySqlConnectionStringBuilder(connection) { Database = string.Empty };

        try
        {
            await using (var admin = new MySqlConnection(adminBuilder.ConnectionString))
            {
                await admin.OpenAsync();
                await using var create = admin.CreateCommand();
                create.CommandText = $"CREATE DATABASE `{databaseName}`";
                await create.ExecuteNonQueryAsync();
            }

            await using (var setup = CreateContext(builder.ConnectionString, new TestTenantContext()))
            {
                await setup.Database.MigrateAsync();
                setup.Tenants.AddRange(Tenant(Guid.NewGuid(), "MYSQLA"), Tenant(Guid.NewGuid(), "MYSQLB"));
                await setup.SaveChangesAsync();
            }

            var tenantA = await FindTenantIdAsync(builder.ConnectionString, "MYSQLA");
            var tenantB = await FindTenantIdAsync(builder.ConnectionString, "MYSQLB");
            await using var dbA = CreateContext(builder.ConnectionString, new TestTenantContext(tenantA));
            await using var dbB = CreateContext(builder.ConnectionString, new TestTenantContext(tenantB));
            var serviceA = new SalaryComponentService(dbA, new TestTenantContext(tenantA), TimeProvider.System);
            var serviceB = new SalaryComponentService(dbB, new TestTenantContext(tenantB), TimeProvider.System);

            var basic = await serviceA.CreateAsync(Request("BASIC", "Basic Salary", SalaryComponentType.Earning, SalaryCalculationType.FixedAmount, taxable: true));
            Assert.True(basic.Succeeded, basic.Message);
            Assert.True((await serviceA.CreateAsync(Request("BASIC", "Duplicate"))).Status == ResultStatus.Conflict);
            var otherBasic = await serviceB.CreateAsync(Request("BASIC", "Basic Salary"));
            Assert.True(otherBasic.Succeeded, otherBasic.Message);

            var hra = await serviceA.CreateAsync(Request("HRA", "House Rent Allowance", SalaryComponentType.Earning, SalaryCalculationType.Percentage, taxable: true));
            var pf = await serviceA.CreateAsync(Request("PF_EMPLOYEE", "Provident Fund - Employee", SalaryComponentType.Deduction, SalaryCalculationType.Statutory, taxable: false, statutory: true));
            Assert.True(hra.Succeeded && pf.Succeeded);

            var page = await serviceA.GetAsync(new SalaryComponentQuery { Page = 1, PageSize = 2 });
            Assert.Equal(3, page.Value!.TotalCount);
            Assert.Equal(2, page.Value.Items.Count);
            var filtered = await serviceA.GetAsync(new SalaryComponentQuery { Search = "basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, IsActive = true, IsStatutory = false });
            Assert.Single(filtered.Value!.Items);
            Assert.Equal("BASIC", filtered.Value.Items[0].Code);

            var update = Request("HRA", "House Rent Allowance Updated", SalaryComponentType.Earning, SalaryCalculationType.Percentage, taxable: true);
            update.ExpectedConcurrencyVersion = hra.Value!.ConcurrencyVersion;
            var updated = await serviceA.UpdateAsync(hra.Value.Id, update);
            Assert.True(updated.Succeeded, updated.Message);
            var deactivated = await serviceA.SetActiveAsync(hra.Value.Id, false, updated.Value!.ConcurrencyVersion);
            Assert.True(deactivated.Succeeded);
            var activated = await serviceA.SetActiveAsync(hra.Value.Id, true, deactivated.Value!.ConcurrencyVersion);
            Assert.True(activated.Succeeded);
            var history = await serviceA.GetHistoryAsync(hra.Value.Id);
            Assert.True(history.Succeeded);
            Assert.True(history.Value!.Count >= 4);

            var invalidStatutory = Request("INVALID", "Invalid", SalaryComponentType.Deduction, SalaryCalculationType.Statutory, taxable: false, statutory: false);
            invalidStatutory.StatutoryType = SalaryStatutoryType.ProvidentFund;
            Assert.Equal(ResultStatus.ValidationFailed, (await serviceA.CreateAsync(invalidStatutory)).Status);
            var invalidDates = Request("DATES", "Dates");
            invalidDates.EffectiveFrom = new DateOnly(2027, 1, 1);
            invalidDates.EffectiveTo = new DateOnly(2026, 12, 31);
            Assert.Equal(ResultStatus.ValidationFailed, (await serviceA.CreateAsync(invalidDates)).Status);

            Assert.Equal(ResultStatus.NotFound, (await serviceA.GetByIdAsync(otherBasic.Value!.Id)).Status);
            Assert.Equal(ResultStatus.NotFound, (await serviceA.UpdateAsync(otherBasic.Value.Id, Request("OTHER", "Other"))).Status);
            Assert.Equal(ResultStatus.NotFound, (await serviceA.SetActiveAsync(otherBasic.Value.Id, false, null)).Status);
            Assert.Equal(ResultStatus.NotFound, (await serviceA.GetHistoryAsync(otherBasic.Value.Id)).Status);
        }
        finally
        {
            await using var admin = new MySqlConnection(adminBuilder.ConnectionString);
            await admin.OpenAsync();
            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static HrmsDbContext CreateContext(string connection, TestTenantContext tenant) => new(
        new DbContextOptionsBuilder<HrmsDbContext>()
            .UseMySQL(connection, mysql => mysql.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations"))
            .Options, tenant);

    private static async Task<Guid> FindTenantIdAsync(string connection, string code)
    {
        await using var db = CreateContext(connection, new TestTenantContext());
        return await db.Tenants.IgnoreQueryFilters().Where(x => x.TenantCode == code).Select(x => x.Id).SingleAsync();
    }

    private static Tenant Tenant(Guid id, string code) => new() { Id = id, TenantCode = code, TenantName = code, Host = $"{code.ToLowerInvariant()}.test", ShardKey = $"{code.ToLowerInvariant()}-shard", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.MySql };

    private static SalaryComponentRequest Request(string code, string name, SalaryComponentType type = SalaryComponentType.Earning, SalaryCalculationType calculation = SalaryCalculationType.FixedAmount, bool taxable = false, bool statutory = false) => new()
    {
        Code = code,
        Name = name,
        ComponentType = type,
        CalculationType = calculation,
        IsTaxable = taxable,
        IsStatutory = statutory,
        StatutoryType = statutory ? SalaryStatutoryType.ProvidentFund : SalaryStatutoryType.None,
        IsRecurring = true,
        AffectsGross = type == SalaryComponentType.Earning,
        AffectsNetPay = type != SalaryComponentType.EmployerContribution,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        IsActive = true,
    };
}
