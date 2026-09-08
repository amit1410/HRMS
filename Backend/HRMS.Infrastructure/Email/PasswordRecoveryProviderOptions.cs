using Microsoft.Extensions.Configuration;

namespace HRMS.Infrastructure.Email;

public sealed class PasswordRecoveryProviderOptions
{
    public const string SectionName = "PasswordRecoveryProviders";

    public string EmailProvider { get; init; } = "Fake";

    public string SmsProvider { get; init; } = "Fake";

    public static PasswordRecoveryProviderOptions Load(IConfiguration configuration, bool isDevelopment)
    {
        var section = configuration.GetSection(SectionName);
        return new PasswordRecoveryProviderOptions
        {
            EmailProvider = section["EmailProvider"] ?? (isDevelopment ? "Fake" : "Smtp"),
            SmsProvider = section["SmsProvider"] ?? (isDevelopment ? "Fake" : "Msg91")
        };
    }

    public void Validate(IConfiguration configuration)
    {
        if (!IsOneOf(EmailProvider, "Fake", "Smtp"))
            throw new InvalidOperationException("PasswordRecoveryProviders:EmailProvider must be Fake or Smtp.");

        if (!IsOneOf(SmsProvider, "Fake", "Msg91"))
            throw new InvalidOperationException("PasswordRecoveryProviders:SmsProvider must be Fake or Msg91.");

        if (string.Equals(EmailProvider, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            Require(configuration, "Email:SmtpHost");
            Require(configuration, "Email:SmtpPort");
            Require(configuration, "Email:SmtpUsername");
            Require(configuration, "Email:SmtpPassword");
            Require(configuration, "Email:From");
            Require(configuration, "Email:EnableSsl");

            if (!int.TryParse(configuration["Email:SmtpPort"], out var port) || port is < 1 or > 65535)
                throw new InvalidOperationException("Email:SmtpPort must be a valid TCP port.");
        }

        if (string.Equals(SmsProvider, "Msg91", StringComparison.OrdinalIgnoreCase))
        {
            Require(configuration, "Msg91:AuthKey");
            Require(configuration, "Msg91:BaseUrl");
            Require(configuration, "Msg91:Endpoint");

            if (!Uri.TryCreate(configuration["Msg91:BaseUrl"], UriKind.Absolute, out var baseUrl)
                || baseUrl.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Msg91:BaseUrl must be an absolute HTTP or HTTPS URL.");
        }
    }

    private static bool IsOneOf(string value, params string[] allowed) =>
        allowed.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));

    private static void Require(IConfiguration configuration, string key)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
            throw new InvalidOperationException($"{key} is required for the selected password-recovery provider.");
    }
}
