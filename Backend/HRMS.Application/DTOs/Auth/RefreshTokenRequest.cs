namespace HRMS.Application.DTOs.Auth;

/// <summary>A refresh token being exchanged for a new token pair, or revoked at sign-out.</summary>
public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}
