namespace HRMS.API.Security;

public sealed class PasswordRecoveryRateLimitSettings
{
    public const string SectionName = "PasswordRecoveryRateLimit";

    public int PermitLimit { get; init; }

    public int WindowMinutes { get; init; }

    public static PasswordRecoveryRateLimitSettings Load(IConfiguration configuration, bool isDevelopment)
    {
        var section = configuration.GetSection(SectionName);
        var defaultPermitLimit = isDevelopment ? 20 : 5;
        var defaultWindowMinutes = isDevelopment ? 1 : 10;

        return new PasswordRecoveryRateLimitSettings
        {
            PermitLimit = Parse(section["PermitLimit"], defaultPermitLimit, $"{SectionName}:PermitLimit"),
            WindowMinutes = Parse(section["WindowMinutes"], defaultWindowMinutes, $"{SectionName}:WindowMinutes")
        };
    }

    public string? Validate()
    {
        if (PermitLimit <= 0) return $"{SectionName}:PermitLimit must be greater than zero.";
        if (WindowMinutes <= 0) return $"{SectionName}:WindowMinutes must be greater than zero.";
        return null;
    }

    private static int Parse(string? value, int defaultValue, string key)
    {
        if (value is null) return defaultValue;
        if (int.TryParse(value, out var parsed)) return parsed;
        throw new InvalidOperationException($"{key} must be a valid integer.");
    }
}
