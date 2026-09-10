using HRMS.Application.Abstractions;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRMS.Tests;

public sealed class PasswordRecoveryProviderRegistrationTests
{
    [Fact]
    public void Development_defaults_to_fake_providers_and_exposes_development_otp()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>(), isDevelopment: true);
        using var scope = provider.CreateScope();

        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var sms = scope.ServiceProvider.GetRequiredService<ISmsOtpSender>();

        Assert.IsType<DevelopmentEmailSender>(email);
        Assert.IsType<DevelopmentSmsOtpSender>(sms);
    }

    [Fact]
    public void Explicit_smtp_and_msg91_are_selected_in_development()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["PasswordRecoveryProviders:EmailProvider"] = "Smtp",
            ["PasswordRecoveryProviders:SmsProvider"] = "Msg91",
            ["Email:SmtpHost"] = "smtp.example.test",
            ["Email:SmtpPort"] = "587",
            ["Email:SmtpUsername"] = "user",
            ["Email:SmtpPassword"] = "password",
            ["Email:FromEmail"] = "hrms@example.test",
            ["Email:FromName"] = "HRMS",
            ["Email:EnableSsl"] = "true",
            ["Msg91:BaseUrl"] = "https://api.msg91.com/api/",
            ["Msg91:AuthKey"] = "configured-outside-source",
            ["Msg91:FlowId"] = "flow-template-id",
            ["Msg91:SenderId"] = "ANEVRA"
        }, isDevelopment: true);
        using var scope = provider.CreateScope();

        Assert.IsType<SmtpEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
        Assert.IsType<Msg91SmsOtpSender>(scope.ServiceProvider.GetRequiredService<ISmsOtpSender>());
    }

    [Fact]
    public void Nested_email_configuration_is_supported_and_selected_provider_is_validated()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "Smtp",
            ["Email:Smtp:Host"] = "smtp.example.test",
            ["Email:Smtp:Port"] = "465",
            ["Email:Smtp:Username"] = "user",
            ["Email:Smtp:Password"] = "password",
            ["Email:Smtp:EnableSsl"] = "true",
            ["Email:FromEmail"] = "hrms@example.test",
            ["Email:FromName"] = "HRMS",
            ["PasswordRecoveryProviders:SmsProvider"] = "Fake"
        });

        var options = PasswordRecoveryProviderOptions.Load(configuration, isDevelopment: true);
        options.Validate(configuration);

        Assert.Equal("Smtp", options.EmailProvider);
    }

    [Fact]
    public void Nested_sms_provider_is_preferred_over_legacy_provider_selection()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Sms:Provider"] = "Fake",
            ["PasswordRecoveryProviders:SmsProvider"] = "Msg91"
        });

        var options = PasswordRecoveryProviderOptions.Load(configuration, isDevelopment: false);

        Assert.Equal("Fake", options.SmsProvider);
        options.Validate(configuration);
    }

    [Fact]
    public void Unselected_smtp_does_not_require_smtp_settings()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "Fake",
            ["PasswordRecoveryProviders:SmsProvider"] = "Fake"
        });

        var options = PasswordRecoveryProviderOptions.Load(configuration, isDevelopment: false);
        options.Validate(configuration);
    }

    [Fact]
    public void Missing_selected_nested_smtp_setting_names_the_nested_key()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "Smtp",
            ["PasswordRecoveryProviders:SmsProvider"] = "Fake"
        });

        var options = PasswordRecoveryProviderOptions.Load(configuration, isDevelopment: true);
        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate(configuration));

        Assert.Contains("Email:Smtp:Host", exception.Message);
    }

    [Fact]
    public void Environment_variables_override_appsettings_values_for_nested_email_configuration()
    {
        const string prefix = "HRMS_TEST_NESTED_";
        var values = new Dictionary<string, string?>
        {
            [prefix + "Email__Provider"] = "Smtp",
            [prefix + "Email__Smtp__Host"] = "smtp.environment.test",
            [prefix + "Email__Smtp__Port"] = "465",
            [prefix + "Email__Smtp__Username"] = "environment-user",
            [prefix + "Email__Smtp__Password"] = "environment-password",
            [prefix + "Email__Smtp__EnableSsl"] = "true",
            [prefix + "Email__FromEmail"] = "environment@example.test",
            [prefix + "Email__FromName"] = "Environment",
            [prefix + "PasswordRecoveryProviders__SmsProvider"] = "Fake"
        };

        try
        {
            foreach (var (key, value) in values) System.Environment.SetEnvironmentVariable(key, value);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Provider"] = "Fake" })
                .AddEnvironmentVariables(prefix)
                .Build();

            var options = PasswordRecoveryProviderOptions.Load(configuration, isDevelopment: false);
            options.Validate(configuration);

            Assert.Equal("Smtp", options.EmailProvider);
            Assert.Equal("environment@example.test", configuration["Email:FromEmail"]);
        }
        finally
        {
            foreach (var key in values.Keys) System.Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void Environment_variables_bind_email_from_email_and_from_name_keys()
    {
        const string prefix = "HRMS_TEST_";
        var values = new Dictionary<string, string?>
        {
            [prefix + "Email__SmtpHost"] = "smtp.hostinger.com",
            [prefix + "Email__SmtpPort"] = "587",
            [prefix + "Email__SmtpUsername"] = "amit@anevratechnologies.com",
            [prefix + "Email__SmtpPassword"] = "mailbox-password",
            [prefix + "Email__FromEmail"] = "no-reply@anevratechnologies.com",
            [prefix + "Email__FromName"] = "Anevra Technologies",
            [prefix + "Email__EnableSsl"] = "true",
            [prefix + "PasswordRecoveryProviders__EmailProvider"] = "Smtp",
            [prefix + "PasswordRecoveryProviders__SmsProvider"] = "Fake"
        };
        try
        {
            foreach (var (key, value) in values) System.Environment.SetEnvironmentVariable(key, value);
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var options = PasswordRecoveryProviderOptions.Load(configuration, isDevelopment: false);
            options.Validate(configuration);

            Assert.Equal("no-reply@anevratechnologies.com", configuration["Email:FromEmail"]);
            Assert.Equal("Anevra Technologies", configuration["Email:FromName"]);
            Assert.Equal("Smtp", options.EmailProvider);
        }
        finally
        {
            foreach (var key in values.Keys) System.Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void Environment_variables_bind_msg91_configuration()
    {
        const string prefix = "HRMS_TEST_";
        var values = new Dictionary<string, string?>
        {
            [prefix + "PasswordRecoveryProviders__SmsProvider"] = "Msg91",
            [prefix + "Msg91__AuthKey"] = "configured-outside-source",
            [prefix + "Msg91__BaseUrl"] = "https://control.msg91.com/api/v5/",
            [prefix + "Msg91__FlowId"] = "flow-template-id",
            [prefix + "Msg91__SenderId"] = "ANEVRA"
        };
        try
        {
            foreach (var (key, value) in values) System.Environment.SetEnvironmentVariable(key, value);
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
            var options = PasswordRecoveryProviderOptions.Load(configuration, isDevelopment: true);
            options.Validate(configuration);

            Assert.Equal("Msg91", options.SmsProvider);
            Assert.Equal("flow-template-id", configuration["Msg91:FlowId"]);
            Assert.Equal("ANEVRA", configuration["Msg91:SenderId"]);
        }
        finally
        {
            foreach (var key in values.Keys) System.Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void Invalid_provider_name_fails_during_registration()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["PasswordRecoveryProviders:EmailProvider"] = "Unknown"
        });
        var services = new ServiceCollection();
        services.AddLogging();

        Assert.Throws<InvalidOperationException>(() => services.AddInfrastructure(configuration, DevelopmentEnvironment()));
    }

    [Fact]
    public async Task Fake_provider_returns_otp_only_in_development()
    {
        var message = new OtpDeliveryMessage(
            "person@example.test", "123456", "Tenant", "Person", DateTime.UtcNow, HRMS.Domain.Enums.PasswordRecoveryChannel.Email);

        using var development = BuildProvider(new Dictionary<string, string?>(), isDevelopment: true);
        using var developmentScope = development.CreateScope();
        var developmentOtp = await developmentScope.ServiceProvider.GetRequiredService<IEmailSender>()
            .SendPasswordResetOtpAsync(message);
        Assert.Equal("123456", developmentOtp);

        using var production = BuildProvider(new Dictionary<string, string?>
        {
            ["PasswordRecoveryProviders:EmailProvider"] = "Fake",
            ["PasswordRecoveryProviders:SmsProvider"] = "Fake"
        }, isDevelopment: false);
        using var productionScope = production.CreateScope();
        var productionOtp = await productionScope.ServiceProvider.GetRequiredService<IEmailSender>()
            .SendPasswordResetOtpAsync(message);
        Assert.Null(productionOtp);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values, bool isDevelopment)
    {
        var configuration = Configuration(values);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration, isDevelopment ? DevelopmentEnvironment() : ProductionEnvironment());
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IHostEnvironment DevelopmentEnvironment() => Environment("Development");

    private static IHostEnvironment ProductionEnvironment() => Environment("Production");

    private static IHostEnvironment Environment(string name) => new TestHostEnvironment { EnvironmentName = name };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "HRMS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
    }
}
