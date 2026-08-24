namespace HRMS.Application.DTOs.Auth;

/// <summary>
/// Credentials supplied at sign-in. There is no organization field: the organization is decided by the
/// host the request arrived on, resolved before the request reaches this DTO, so it is not something a
/// caller can state or mistype. Email addresses are unique per organization rather than globally, and the
/// host is what makes that lookup unambiguous. After sign-in the organization always comes from the
/// signed token.
/// </summary>
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
