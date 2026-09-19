using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlStatutoryPayrollIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_statutory_framework_persists_tenant_scoped_configuration()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Statutory tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured); var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty }; var databaseName = $"HRMS_Phase7F_Statutory_{Guid.NewGuid():N}"; var connection = new MySqlConnectionStringBuilder(normalized) { Database = databaseName }; var tenant = Guid.NewGuid();
        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(); await using var command = server.CreateCommand(); command.CommandText = $"CREATE DATABASE `{databaseName}`"; await command.ExecuteNonQueryAsync(); }
            await using var setup = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, o => o.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, new TestTenantContext());
            await setup.Database.MigrateAsync(); var code = $"MYSQL7F{Guid.NewGuid():N}"[..12].ToUpperInvariant(); setup.Tenants.Add(new Tenant { Id = tenant, TenantCode = code, TenantName = code, Host = code.ToLowerInvariant() + ".test", ShardKey = code.ToLowerInvariant() + "-shard", Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.MySql }); setup.StatutoryConfigurations.Add(new StatutoryConfiguration { Id = Guid.NewGuid(), TenantId = tenant, Code = "PF-TEST", Name = "PF test", JurisdictionCode = "IN", StatutoryType = StatutoryType.ProvidentFund }); await setup.SaveChangesAsync();
            await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, o => o.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, new TestTenantContext(tenant)); Assert.Equal("PF-TEST", await db.StatutoryConfigurations.Where(x => x.TenantId == tenant).Select(x => x.Code).SingleAsync());
        }
        finally { await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`"; await drop.ExecuteNonQueryAsync(); }
    }
}
