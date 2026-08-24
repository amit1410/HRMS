using System.ComponentModel.DataAnnotations;

namespace HRMS.Application.Security;

/// <summary>
/// Strongly typed binding for the "Jwt" configuration section. Validated at startup (fail fast) so a
/// missing or too-short signing key stops the app instead of silently producing weak tokens.
/// </summary>
public class JwtSettings
{
    public const string SectionName = "Jwt";

    /// <summary>Minimum key length in bytes. HMAC-SHA256 requires a key of at least the hash size (256 bits).</summary>
    public const int MinimumSecretKeyBytes = 32;

    [Required(AllowEmptyStrings = false)]
    public string Issuer { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Symmetric signing key. Must be supplied per environment via user-secrets, environment variables
    /// or a secret store — never committed for anything but local development.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Access token lifetime. Kept short because access tokens cannot be revoked once issued.</summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 60;

    /// <summary>Refresh token lifetime. These are revocable, so they may live much longer.</summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>Tolerance for clock drift between the issuing and validating machines.</summary>
    [Range(0, 300)]
    public int ClockSkewSeconds { get; set; } = 30;

    /// <summary>
    /// Validates the settings beyond what data annotations can express. Returns the first problem
    /// found, or null when the settings are usable.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer)) return "Jwt:Issuer must be configured.";
        if (string.IsNullOrWhiteSpace(Audience)) return "Jwt:Audience must be configured.";

        if (string.IsNullOrWhiteSpace(SecretKey))
        {
            return "Jwt:SecretKey must be configured (use user-secrets or environment variables outside development).";
        }

        var keyBytes = System.Text.Encoding.UTF8.GetByteCount(SecretKey);
        if (keyBytes < MinimumSecretKeyBytes)
        {
            return $"Jwt:SecretKey must be at least {MinimumSecretKeyBytes} bytes ({keyBytes} supplied) to sign HMAC-SHA256 tokens.";
        }

        if (AccessTokenMinutes is < 1 or > 1440) return "Jwt:AccessTokenMinutes must be between 1 and 1440.";
        if (RefreshTokenDays is < 1 or > 365) return "Jwt:RefreshTokenDays must be between 1 and 365.";
        if (ClockSkewSeconds is < 0 or > 300) return "Jwt:ClockSkewSeconds must be between 0 and 300.";

        return null;
    }
}
