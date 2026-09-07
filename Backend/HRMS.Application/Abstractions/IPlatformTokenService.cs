namespace HRMS.Application.Abstractions;

public sealed record PlatformAccessTokenResult(string Token, DateTime ExpiresAtUtc);

public interface IPlatformTokenService
{
    PlatformAccessTokenResult CreateAccessToken(
        Guid platformUserId,
        string email,
        string firstName,
        string lastName,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        int securityRevision);

    string CreateRefreshToken();
    string HashRefreshToken(string refreshToken);
}
