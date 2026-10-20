using Xunit;
using HRMS.Application;
using HRMS.Application.Abstractions;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class WelcomeInvitationOperatorArgsTests
{
    private const string Tenant = "16313e68-2e72-4a6a-b689-79b271cec295";
    private const string Batch = "2aa0443c-4e5d-4f3e-b618-681b24ecd6e2";
    private const string Attempt = "4f634cb1-a37a-4363-ba98-29bff4ba0e3a";
    private static string[] Preview(params string[] extra) => ["preview", "--tenant-id", Tenant, "--tenant-code", "WEB1", "--shard", "web1", "--database", "hrms_web1", "--batch-id", Batch, .. extra];

    [Fact] public void AcceptsCompletePreviewArguments() { var parsed = Args.Parse(Preview()); Assert.Equal("preview", parsed.Command); Assert.Equal(Tenant, parsed.Required("tenant-id")); }
    [Theory]
    [InlineData("--unknown", "x")]
    [InlineData("--tenant-id", "16313e68-2e72-4a6a-b689-79b271cec295")]
    [InlineData("--batch-id")]
    [InlineData("unexpected")]
    public void RejectsUnknownDuplicateIncompleteAndPositionalArguments(params string[] extra) => Assert.Throws<ArgumentException>(() => Args.Parse(Preview(extra)));
    [Fact] public void RejectsMissingRequiredArguments() => Assert.Throws<ArgumentException>(() => Args.Parse(["preview"]));
    [Fact] public void RejectsConfirmationForPreview() => Assert.Throws<ArgumentException>(() => Args.Parse(Preview("--confirm", "PROCESS")));
    [Fact] public void RequiresAttemptAndConfirmationForProcessing() => Assert.Throws<ArgumentException>(() => Args.Parse(["process", "--tenant-id", Tenant, "--tenant-code", "WEB1", "--shard", "web1", "--database", "hrms_web1", "--batch-id", Batch, "--confirm", $"PROCESS {Tenant} {Batch} {Attempt}"]));
    [Fact] public void AcceptsSingleAttemptProcessingConfirmationArgument() { var parsed = Args.Parse(["process", "--tenant-id", Tenant, "--tenant-code", "WEB1", "--shard", "web1", "--database", "hrms_web1", "--batch-id", Batch, "--attempt-id", Attempt, "--confirm", $"PROCESS {Tenant} {Batch} {Attempt}"]); Assert.Equal("process", parsed.Command); Assert.Equal(Attempt, parsed.Required("attempt-id")); }

    [Fact]
    public void Operator_connection_preflight_accepts_catalog_and_mysql_tenant_template()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "MySql",
            ["Database:CatalogProvider"] = "MySql",
            ["ConnectionStrings:Catalog"] = "isolated-catalog-value",
            ["Sharding:MySqlConnectionStringTemplate"] = "isolated-template-value"
        }).Build();

        Operator.ValidateConnectionConfiguration(configuration);
    }

    [Fact]
    public void Operator_connection_preflight_rejects_missing_catalog_before_database_access()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "MySql",
            ["Database:CatalogProvider"] = "MySql",
            ["Sharding:MySqlConnectionStringTemplate"] = "isolated-template-value"
        }).Build();

        var exception = Assert.Throws<InvalidOperationException>(() => Operator.ValidateConnectionConfiguration(configuration));

        Assert.Contains("ConnectionStrings:Catalog is required", exception.Message);
    }

    [Fact]
    public void Operator_catalog_preflight_does_not_guess_tenant_provider_from_catalog_provider()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:CatalogProvider"] = "MySql",
            ["ConnectionStrings:Catalog"] = "isolated-catalog-value"
        }).Build();

        Operator.ValidateConnectionConfiguration(configuration);
    }

    [Fact]
    public void Operator_routes_mysql_tenant_from_catalog_descriptor_metadata()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:CatalogProvider"] = "MySql",
            ["ConnectionStrings:Catalog"] = "Server=not-opened;Database=catalog;",
            ["Sharding:MySqlConnectionStringTemplate"] = "Server=not-opened;Database=hrms_{shardKey};"
        }).Build();

        using var host = Operator.BuildHost(Operator.PreviewConfiguration(configuration), Environments.Production, readOnlyPreview: true, isolatedTestHost: true);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IShardContext>().Use(new ShardDescriptor(
            Guid.Parse(Tenant), "WEB1", "web1.test", "web1", HRMS.Domain.Enums.TenantStatus.Active, HRMS.Domain.Enums.DatabaseProviderType.MySql));

        var db = scope.ServiceProvider.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>();
        Assert.Equal("MySql.EntityFrameworkCore", db.Database.ProviderName);
        Assert.Equal("hrms_web1", db.Database.GetDbConnection().Database);
    }

    [Fact]
    public void Operator_fails_closed_when_mysql_tenant_template_is_missing()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:CatalogProvider"] = "MySql",
            ["ConnectionStrings:Catalog"] = "Server=not-opened;Database=catalog;"
        }).Build();
        using var host = Operator.BuildHost(Operator.PreviewConfiguration(configuration), Environments.Production, readOnlyPreview: true, isolatedTestHost: true);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IShardContext>().Use(new ShardDescriptor(
            Guid.Parse(Tenant), "WEB1", "web1.test", "web1", HRMS.Domain.Enums.TenantStatus.Active, HRMS.Domain.Enums.DatabaseProviderType.MySql));

        var exception = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>());

        Assert.Contains("MySqlConnectionStringTemplate", exception.Message);
    }

    [Fact]
    public void Operator_fails_closed_when_catalog_provider_metadata_is_unsupported()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:CatalogProvider"] = "MySql",
            ["ConnectionStrings:Catalog"] = "Server=not-opened;Database=catalog;",
            ["Sharding:MySqlConnectionStringTemplate"] = "Server=not-opened;Database={shardKey};"
        }).Build();
        using var host = Operator.BuildHost(Operator.PreviewConfiguration(configuration), Environments.Production, readOnlyPreview: true, isolatedTestHost: true);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IShardContext>().Use(new ShardDescriptor(
            Guid.Parse(Tenant), "WEB1", "web1.test", "web1", HRMS.Domain.Enums.TenantStatus.Active, (HRMS.Domain.Enums.DatabaseProviderType)99));

        var exception = Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<HRMS.Infrastructure.Persistence.HrmsDbContext>());

        Assert.Contains("DatabaseProviderNotSupported", exception.Message);
    }

    [Fact]
    public void Operator_development_configuration_uses_api_base_path_and_api_user_secrets_identity()
    {
        var isolatedRoot = Path.Combine(Path.GetTempPath(), "hrms-operator-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedRoot);
        try
        {
            File.WriteAllText(Path.Combine(isolatedRoot, "appsettings.json"), "{\"ConnectionStrings\":{\"Catalog\":\"\"}}");
            var configuration = Operator.BuildConfiguration([], isolatedRoot,
                builder => builder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Catalog"] = "secret-source-value"
                }), Environments.Development);

            Assert.Equal("secret-source-value", configuration.GetConnectionString("Catalog"));
        }
        finally
        {
            Directory.Delete(isolatedRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("Fake")]
    [InlineData("Msg91")]
    public void Preview_startup_without_delivery_credentials_excludes_delivery_services(string smsProvider)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PasswordRecoveryProviders:SmsProvider"] = smsProvider,
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Sqlite"] = "Data Source=:memory:"
        }).Build();
        var previewConfiguration = Operator.PreviewConfiguration(configuration);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(previewConfiguration, new TestProductionEnvironment(), includeDeliveryInfrastructure: false);
        services.AddScoped<ITenantContext>(_ => new TestTenantContext(Guid.NewGuid()));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<IWelcomeInvitationProcessor>();

        Assert.Empty(provider.GetServices<IEmailSender>());
        Assert.Empty(provider.GetServices<ISmsOtpSender>());
    }

    [Theory]
    [InlineData("Fake")]
    [InlineData("Msg91")]
    public void Production_operator_host_starts_preview_without_delivery_credentials(string smsProvider)
    {
        var isolatedRoot = Path.Combine(Path.GetTempPath(), "hrms-operator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedRoot);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PasswordRecoveryProviders:SmsProvider"] = smsProvider,
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:SqliteCatalog"] = $"Data Source={Path.Combine(isolatedRoot, "catalog.db")}",
                ["ConnectionStrings:Sqlite"] = $"Data Source={Path.Combine(isolatedRoot, "tenant.db")}",
                ["Sharding:SqliteConnectionStringTemplate"] = $"Data Source={Path.Combine(isolatedRoot, "{{shardKey}}_{{database}}.db")}",
                ["Sharding:SqlServerConnectionStringTemplate"] = "Data Source={shardKey}_{database};TrustServerCertificate=True",
                ["Sharding:MySqlConnectionStringTemplate"] = "Server=localhost;Database={shardKey}_{database};User=operator;Password=isolated"
            }).Build();

            using var host = Operator.BuildHost(Operator.PreviewConfiguration(configuration), Environments.Production, readOnlyPreview: true, isolatedTestHost: true);
            host.Start();
            using var scope = host.Services.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IWelcomeInvitationProcessor>();
            var authorization = scope.ServiceProvider.GetRequiredService<ICurrentAuthorizationContext>();

            Assert.NotNull(processor);
            Assert.False(authorization.HasAnyPermission("any.permission"));
            Assert.Empty(host.Services.GetServices<IHostedService>());
            Assert.Empty(host.Services.GetServices<IEmailSender>());
            Assert.Empty(host.Services.GetServices<ISmsOtpSender>());
        }
        finally
        {
            Directory.Delete(isolatedRoot, recursive: true);
        }
    }

    [Fact]
    public void Process_mode_still_rejects_missing_smtp_credentials()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PasswordRecoveryProviders:EmailProvider"] = "Smtp",
            ["PasswordRecoveryProviders:SmsProvider"] = "Fake"
        }).Build();

        var exception = Assert.Throws<InvalidOperationException>(() => Operator.ValidateSmtp(configuration));

        Assert.Contains("Complete SMTP configuration is required", exception.Message);
    }

    [Fact]
    public void Process_mode_still_rejects_missing_msg91_credentials()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PasswordRecoveryProviders:EmailProvider"] = "Fake",
            ["PasswordRecoveryProviders:SmsProvider"] = "Msg91"
        }).Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddInfrastructure(configuration, new TestProductionEnvironment()));

        Assert.Contains("Msg91:AuthKey is required", exception.Message);
    }

    [Fact]
    public void Preview_email_override_does_not_change_sms_provider_precedence()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PasswordRecoveryProviders:EmailProvider"] = "Smtp",
            ["PasswordRecoveryProviders:SmsProvider"] = "Msg91"
        }).Build();

        var preview = Operator.PreviewConfiguration(configuration);
        var options = PasswordRecoveryProviderOptions.Load(preview, isDevelopment: false);

        Assert.Equal("Fake", preview["Email:Provider"]);
        Assert.Equal("Fake", options.EmailProvider);
        Assert.Equal("Msg91", options.SmsProvider);
    }

    private sealed class TestProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "HRMS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
