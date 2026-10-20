using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Email;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace HRMS.API;

/// <summary>Emits a safe snapshot of the effective welcome invitation email provider at startup.</summary>
public static class StartupEmailDiagnostics
{
    public static void LogEmailConfiguration(
        IHostEnvironment environment,
        IConfiguration configuration,
        PasswordRecoveryProviderOptions providerOptions,
        IEmailSender sender,
        ILogger logger)
    {
        var configuredProvider = configuration["Email:Provider"]
            ?? configuration[$"{PasswordRecoveryProviderOptions.SectionName}:EmailProvider"];
        var provider = sender switch
        {
            SmtpEmailSender when string.Equals(providerOptions.EmailProvider, "Smtp", StringComparison.OrdinalIgnoreCase)
                => string.IsNullOrWhiteSpace(configuredProvider) ? "Smtp (environment default)" : "Smtp",
            DevelopmentEmailSender when string.Equals(providerOptions.EmailProvider, "Fake", StringComparison.OrdinalIgnoreCase)
                => string.IsNullOrWhiteSpace(configuredProvider) ? "Fake (environment default)" : "Fake",
            _ => "UNKNOWN"
        };

        logger.LogInformation(
            "Welcome invitation email startup configuration. Environment {EnvironmentName}, ContentRoot {ContentRoot}, EmailProvider {EmailProvider}, EmailSenderImplementation {EmailSenderImplementation}.",
            environment.EnvironmentName,
            environment.ContentRootPath,
            provider,
            sender.GetType().FullName);
    }
}
