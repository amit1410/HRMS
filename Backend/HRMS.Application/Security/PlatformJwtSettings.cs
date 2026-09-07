namespace HRMS.Application.Security;

public sealed class PlatformJwtSettings
{
    public const string SectionName = "PlatformJwt";
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
    public int ClockSkewSeconds { get; set; } = 30;

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer)) return "Issuer is required.";
        if (string.IsNullOrWhiteSpace(Audience)) return "Audience is required.";
        if (string.IsNullOrWhiteSpace(SecretKey) || SecretKey.Length < 32) return "SecretKey must be at least 32 characters.";
        if (AccessTokenMinutes is < 1 or > 1440) return "AccessTokenMinutes is outside the supported range.";
        if (RefreshTokenDays is < 1 or > 365) return "RefreshTokenDays is outside the supported range.";
        return null;
    }
}
