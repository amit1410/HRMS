namespace HRMS.Application.Abstractions;

/// <summary>
/// Exposes the permissions already established for the current authenticated request.
/// Implementations must source these from the trusted authorization principal, never from request data.
/// </summary>
public interface ICurrentAuthorizationContext
{
    bool HasAnyPermission(params string[] permissions);
}
