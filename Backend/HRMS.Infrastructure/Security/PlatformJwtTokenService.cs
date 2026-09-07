using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HRMS.Application.Abstractions;
using HRMS.Application.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HRMS.Infrastructure.Security;

public sealed class PlatformJwtTokenService : IPlatformTokenService
{
    private const int RefreshTokenBytes = 32;
    private readonly PlatformJwtSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new();

    public PlatformJwtTokenService(IOptions<PlatformJwtSettings> settings, TimeProvider timeProvider)
    {
        _settings = settings.Value;
        var error = _settings.Validate();
        if (error is not null) throw new InvalidOperationException($"Platform JWT configuration is invalid. {error}");
        _timeProvider = timeProvider;
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public PlatformAccessTokenResult CreateAccessToken(
        Guid platformUserId,
        string email,
        string firstName,
        string lastName,
        IReadOnlyCollection<string> roles,
        IReadOnlyCollection<string> permissions,
        int securityRevision)
    {
        var issued = _timeProvider.GetUtcNow().UtcDateTime;
        var expires = issued.AddMinutes(_settings.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, platformUserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(PlatformClaimTypes.Scope, "platform"),
            new("email", email),
            new("given_name", firstName),
            new("family_name", lastName),
            new("security_revision", securityRevision.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        claims.AddRange(roles.Select(x => new Claim("role", x)));
        claims.AddRange(permissions.Select(x => new Claim("permission", x)));
        return new PlatformAccessTokenResult(_handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            IssuedAt = issued,
            NotBefore = issued,
            Expires = expires,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = _credentials
        }), expires);
    }

    public string CreateRefreshToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RefreshTokenBytes));

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
