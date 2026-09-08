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
            ["Email:From"] = "hrms@example.test",
            ["Email:EnableSsl"] = "true",
            ["Msg91:BaseUrl"] = "https://api.msg91.com/api/",
            ["Msg91:Endpoint"] = "sendotp.php",
            ["Msg91:AuthKey"] = "configured-outside-source"
        }, isDevelopment: true);
        using var scope = provider.CreateScope();

        Assert.IsType<SmtpEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
        Assert.IsType<Msg91SmsOtpSender>(scope.ServiceProvider.GetRequiredService<ISmsOtpSender>());
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
