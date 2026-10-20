using HRMS.API;
using HRMS.Application.Abstractions;
using HRMS.Infrastructure;
using HRMS.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HRMS.Tests;

public sealed class StartupEmailDiagnosticsTests
{
    [Fact]
    public void Development_default_logs_effective_fake_provider_and_registered_sender_without_secrets()
    {
        AssertDiagnostic(new Dictionary<string, string?>(), "Development", "Fake (environment default)", typeof(DevelopmentEmailSender));
    }

    [Fact]
    public void Explicit_smtp_provider_logs_smtp_and_actual_sender_registration()
    {
        AssertDiagnostic(SmtpConfiguration(), "Production", "Smtp", typeof(SmtpEmailSender));
    }

    [Fact]
    public void Explicit_fake_provider_logs_fake_and_actual_sender_registration()
    {
        AssertDiagnostic(new Dictionary<string, string?> { ["Email:Provider"] = "Fake" }, "Production", "Fake", typeof(DevelopmentEmailSender));
    }

    [Fact]
    public void Top_level_email_provider_precedes_nested_provider_configuration()
    {
        var values = SmtpConfiguration();
        values["Email:Provider"] = "Fake";
        AssertDiagnostic(values, "Production", "Fake", typeof(DevelopmentEmailSender));
    }

    [Fact]
    public void Missing_provider_in_production_reports_resolved_smtp_environment_default()
    {
        AssertDiagnostic(SmtpConfiguration().Where(p => !p.Key.StartsWith("PasswordRecoveryProviders:", StringComparison.Ordinal))
                .ToDictionary(p => p.Key, p => p.Value),
            "Production", "Smtp (environment default)", typeof(SmtpEmailSender));
    }

    [Fact]
    public void Invalid_provider_value_is_reported_unknown_without_exposing_configuration()
    {
        var logger = new RecordingLogger();
        var environment = new TestHostEnvironment { EnvironmentName = "Development", ContentRootPath = "safe" };
        var values = new Dictionary<string, string?> { ["PasswordRecoveryProviders:EmailProvider"] = "invalid-recipient@example.test" };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new PasswordRecoveryProviderOptions { EmailProvider = "invalid-recipient@example.test" };
        StartupEmailDiagnostics.LogEmailConfiguration(environment, configuration, options,
            new DevelopmentEmailSender(new LoggerFactory().CreateLogger<DevelopmentEmailSender>()), logger);
        Assert.Contains("UNKNOWN", logger.Message);
        Assert.DoesNotContain("invalid-recipient@example.test", logger.Message);
    }

    private static void AssertDiagnostic(
        Dictionary<string, string?> values,
        string environmentName,
        string expectedProvider,
        Type expectedSender)
    {
        const string smtpPassword = "DO_NOT_LOG_SMTP_PASSWORD_93481";
        const string smtpUsername = "DO_NOT_LOG_SMTP_USERNAME_93481";
        const string connectionString = "DO_NOT_LOG_CONNECTION_STRING_93481";
        values.TryAdd("PasswordRecoveryProviders:SmsProvider", "Fake");
        if (expectedProvider == "Smtp")
            values["Email:Smtp:Password"] = smtpPassword;
        values["Email:Smtp:Username"] = smtpUsername;
        values["ConnectionStrings:Catalog"] = connectionString;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var environment = new TestHostEnvironment { EnvironmentName = environmentName, ContentRootPath = "D:\\isolated\\hrms" };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration, environment);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<PasswordRecoveryProviderOptions>();
        var sender = provider.GetRequiredService<IEmailSender>();
        Assert.IsType(expectedSender, sender);

        var logger = new RecordingLogger();
        StartupEmailDiagnostics.LogEmailConfiguration(environment, configuration, options, sender, logger);

        Assert.Equal(LogLevel.Information, logger.Level);
        Assert.Contains(environmentName, logger.Message);
        Assert.Contains("D:\\isolated\\hrms", logger.Message);
        Assert.Contains(expectedProvider, logger.Message);
        Assert.Contains(expectedSender.FullName!, logger.Message);
        Assert.DoesNotContain(smtpPassword, logger.Message);
        Assert.DoesNotContain(smtpUsername, logger.Message);
        Assert.DoesNotContain(connectionString, logger.Message);
    }

    private static Dictionary<string, string?> SmtpConfiguration() => new()
    {
        ["PasswordRecoveryProviders:EmailProvider"] = "Smtp",
        ["Email:Smtp:Host"] = "smtp.example.test",
        ["Email:Smtp:Port"] = "587",
        ["Email:Smtp:Username"] = "smtp-user",
        ["Email:Smtp:Password"] = "smtp-password",
        ["Email:Smtp:EnableSsl"] = "true",
        ["Email:FromEmail"] = "hrms@example.test",
        ["Email:FromName"] = "HRMS"
    };

    private sealed class RecordingLogger : ILogger
    {
        public LogLevel Level { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Level = logLevel;
            Message = formatter(state, exception);
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = "HRMS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
    }
}
