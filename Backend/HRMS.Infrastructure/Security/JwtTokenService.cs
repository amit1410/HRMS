using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HRMS.Application.Abstractions;
using HRMS.Application.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HRMS.Infrastructure.Security;

/// <summary>
/// Issues HMAC-SHA256 signed JWT access tokens and opaque refresh tokens.
/// <para>
/// The access token carries the tenant id, so the tenant a request operates on is signed by the server
/// and cannot be altered by the client. Permissions are emitted as repeated claims rather than a single
/// delimited value, so authorization policies can match them exactly.
/// </para>
/// <para>
/// Refresh tokens are 256 bits of cryptographic randomness rather than JWTs: they are opaque to the
/// client, revocable server-side, and stored only as a SHA-256 digest. A fast hash is appropriate here
/// (unlike for passwords) because the input is high-entropy random data, not a guessable secret.
/// </para>
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private const int RefreshTokenBytes = 32;

    private readonly JwtSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly SigningCredentials _signingCredentials;
    private readonly JsonWebTokenHandler _tokenHandler = new();

    public JwtTokenService(IOptions<JwtSettings> settings, TimeProvider timeProvider)
    {
        _settings = settings.Value;
        _timeProvider = timeProvider;

        var validationError = _settings.Validate();
        if (validationError is not null)
        {
            throw new InvalidOperationException($"JWT configuration is invalid. {validationError}");
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
        _signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    }

    public AccessTokenResult CreateAccessToken(AccessTokenDescriptor descriptor)
    {
        var issuedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = issuedAt.AddMinutes(_settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, descriptor.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(HrmsClaimTypes.UserId, descriptor.UserId.ToString()),
            new(HrmsClaimTypes.TenantId, descriptor.TenantId.ToString()),
            new(HrmsClaimTypes.TenantCode, descriptor.TenantCode),
            new(HrmsClaimTypes.Email, descriptor.Email),
            new(HrmsClaimTypes.FirstName, descriptor.FirstName),
            new(HrmsClaimTypes.LastName, descriptor.LastName)
        };

        claims.AddRange(descriptor.Roles.Select(role => new Claim(HrmsClaimTypes.Role, role)));
        claims.AddRange(descriptor.Permissions.Select(permission => new Claim(HrmsClaimTypes.Permission, permission)));

        // A ClaimsIdentity is used rather than SecurityTokenDescriptor.Claims because a dictionary
        // cannot hold the repeated role/permission claim types.
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expiresAt,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = _signingCredentials
        };

        return new AccessTokenResult(_tokenHandler.CreateToken(tokenDescriptor), expiresAt);
    }

    public string CreateRefreshToken() =>
        Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RefreshTokenBytes));

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
