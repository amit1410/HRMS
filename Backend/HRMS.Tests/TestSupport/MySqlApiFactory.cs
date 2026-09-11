using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using System.Data.Common;
using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// Runs the real API startup and middleware pipeline against the disposable MySQL catalog and tenant
/// databases. The base factory remains SQLite-backed for the ordinary integration suite.
/// </summary>
public sealed class MySqlApiFactory : HrmsApiFactory
{
    private readonly string _tenantConnection;
    private readonly string _catalogConnection;

    public MySqlApiFactory(string tenantConnection, string catalogConnection)
    {
        _tenantConnection = NormalizeConnectionString(tenantConnection);
        _catalogConnection = NormalizeConnectionString(catalogConnection);
    }

    protected override bool RequireInitialization => false;

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<IDbContextOptionsConfiguration<HrmsDbContext>>(
            new MySqlTestConnectionConfiguration(_tenantConnection));
    }

    protected override void ConfigureTestConfiguration(IConfigurationBuilder configuration)
    {
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "MySql",
            ["Database:CatalogProvider"] = "MySql",
            ["ConnectionStrings:Catalog"] = _catalogConnection,
            ["Sharding:MySqlConnectionStringTemplate"] = _tenantConnection,
            ["Database:SkipInitialization"] = "true",
            ["Database:SeedDemoTenants"] = "false",
            ["PasswordRecoveryProviders:EmailProvider"] = "Fake",
            ["PasswordRecoveryProviders:SmsProvider"] = "Fake",
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:SecretKey"] = SigningKey,
            ["Jwt:AccessTokenMinutes"] = "30",
            ["Jwt:RefreshTokenDays"] = "7",
            ["Jwt:ClockSkewSeconds"] = "0",
            ["RateLimiting:Authentication:PermitLimit"] = "10000",
            ["RateLimiting:Authentication:WindowSeconds"] = "60",
            ["Cors:AllowedOrigins:0"] = ClientOrigin,
            ["Cors:AllowedOrigins:1"] = "https://localhost:5173",
            ["Cors:WorkspaceOriginTemplates:0"] = "http://{workspace}.localhost:5173",
            ["Cors:WorkspaceOriginTemplates:1"] = "https://{workspace}.localhost:5173",
            ["Serilog:MinimumLevel:Default"] = "Warning",
            ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Warning",
            ["Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore.Database.Command"] = "Warning"
        });
    }

    internal static string NormalizeConnectionString(string connection)
    {
        var builder = new MySqlConnectionStringBuilder(connection);
        builder.SslMode = MySqlSslMode.Disabled;
        builder.AllowPublicKeyRetrieval = true;
        foreach (var key in new[]
                 {
                     "CertificateFile", "CertificatePassword", "SslCa", "SslCert", "SslKey",
                     "TlsVersion", "CertificateThumbprint"
                 })
        {
            builder.Remove(key);
        }

        // Keep the no-TLS mode explicit in the serialized string consumed by the EF provider.
        builder["SslMode"] = "Disabled";
        var explicitOptions = new DbConnectionStringBuilder
        {
            ConnectionString = builder.ConnectionString
        };
        explicitOptions["SslMode"] = "Disabled";
        return explicitOptions.ConnectionString;
    }

    private sealed class MySqlTestConnectionConfiguration : IDbContextOptionsConfiguration<HrmsDbContext>
    {
        private readonly MySqlConnectionStringBuilder _template;

        public MySqlTestConnectionConfiguration(string connectionString) =>
            _template = new MySqlConnectionStringBuilder(connectionString);

        public void Configure(IServiceProvider serviceProvider, DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.AddInterceptors(new MySqlTestConnectionInterceptor(_template));
    }

    private sealed class MySqlTestConnectionInterceptor : DbConnectionInterceptor
    {
        private readonly MySqlConnectionStringBuilder _template;

        public MySqlTestConnectionInterceptor(MySqlConnectionStringBuilder template) => _template = template;

        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result)
        {
            Normalize(connection);
            return result;
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            Normalize(connection);
            return ValueTask.FromResult(result);
        }

        private void Normalize(DbConnection connection)
        {
            if (connection is not MySqlConnection)
                return;

            var options = new MySqlConnectionStringBuilder(NormalizeConnectionString(connection.ConnectionString));
            options.Database = _template.Database;
            options.UserID = _template.UserID;
            options.Password = _template.Password;
            connection.ConnectionString = options.ConnectionString;
        }
    }
}
