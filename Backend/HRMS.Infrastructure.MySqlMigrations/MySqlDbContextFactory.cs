using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MySql.Data.MySqlClient;

namespace HRMS.Infrastructure.MySqlMigrations;

public sealed class MySqlDbContextFactory : IDesignTimeDbContextFactory<HrmsDbContext>
{
    public HrmsDbContext CreateDbContext(string[] args)
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        var connection = string.IsNullOrWhiteSpace(configured) ? "Server=localhost;Database=HRMS_MigrationDesign;" : NormalizeForTest(configured);
        var options = new DbContextOptionsBuilder<HrmsDbContext>()
            .UseMySQL(
                connection,
                mysql => mysql.MigrationsAssembly(typeof(MySqlDbContextFactory).Assembly.FullName))
            .Options;

        return new HrmsDbContext(options, new DesignTimeTenantContext());
    }

    private static string NormalizeForTest(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString) { SslMode = MySqlSslMode.Disabled };
        foreach (var key in new[] { "CertificateFile", "CertificatePassword", "SslCa", "SslCert", "SslKey", "TlsVersion", "CertificateThumbprint" })
            builder.Remove(key);
        return builder.ConnectionString;
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid? TenantId => null;
        public Guid? UserId => null;
        public bool HasTenant => false;
    }
}
