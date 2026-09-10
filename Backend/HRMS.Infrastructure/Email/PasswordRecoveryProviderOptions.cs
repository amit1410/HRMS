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
            EmailProvider = configuration["Email:Provider"]
                ?? section["EmailProvider"]
                ?? (isDevelopment ? "Fake" : "Smtp"),
            SmsProvider = configuration["Sms:Provider"]
                ?? section["SmsProvider"]
                ?? (isDevelopment ? "Fake" : "Msg91")
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
            var host = Value(configuration, "Email:Smtp:Host", "Email:SmtpHost");
            var port = Value(configuration, "Email:Smtp:Port", "Email:SmtpPort");
            Require(configuration, "Email:Smtp:Host", host);
            Require(configuration, "Email:Smtp:Port", port);
            Require(configuration, "Email:Smtp:Username", Value(configuration, "Email:Smtp:Username", "Email:SmtpUsername"));
            Require(configuration, "Email:Smtp:Password", Value(configuration, "Email:Smtp:Password", "Email:SmtpPassword"));
            Require(configuration, "Email:FromEmail");
            Require(configuration, "Email:FromName");
            Require(configuration, "Email:Smtp:EnableSsl", Value(configuration, "Email:Smtp:EnableSsl", "Email:EnableSsl"));

            if (!int.TryParse(port, out var parsedPort) || parsedPort is < 1 or > 65535)
                throw new InvalidOperationException("Email:Smtp:Port must be a valid TCP port.");
        }

        if (string.Equals(SmsProvider, "Msg91", StringComparison.OrdinalIgnoreCase))
        {
            Require(configuration, "Msg91:AuthKey");
            Require(configuration, "Msg91:BaseUrl");
            Require(configuration, "Msg91:FlowId");
            Require(configuration, "Msg91:SenderId");

            if (!Uri.TryCreate(configuration["Msg91:BaseUrl"], UriKind.Absolute, out var baseUrl)
                || baseUrl.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Msg91:BaseUrl must be an absolute HTTP or HTTPS URL.");
        }
    }

    private static bool IsOneOf(string value, params string[] allowed) =>
        allowed.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));

    private static string? Value(IConfiguration configuration, string preferredKey, string legacyKey) =>
        configuration[preferredKey] ?? configuration[legacyKey];

    private static void Require(IConfiguration configuration, string key, string? value = null)
    {
        if (string.IsNullOrWhiteSpace(value ?? configuration[key]))
            throw new InvalidOperationException($"{key} is required for the selected password-recovery provider.");
    }
}
