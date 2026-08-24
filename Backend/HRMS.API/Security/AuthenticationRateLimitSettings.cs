namespace HRMS.API.Security;

/// <summary>
/// Binding for the "RateLimiting:Authentication" configuration section.
/// </summary>
public sealed class AuthenticationRateLimitSettings
{
    public const string SectionName = "RateLimiting:Authentication";

    /// <summary>Requests allowed per client, per window.</summary>
    public int PermitLimit { get; set; } = 20;

    /// <summary>Length of the fixed window in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Returns the first problem found, or null when the settings are usable.</summary>
    public string? Validate()
    {
        if (PermitLimit < 1) return $"{SectionName}:PermitLimit must be at least 1.";
        if (WindowSeconds < 1) return $"{SectionName}:WindowSeconds must be at least 1.";

        return null;
    }
}
