using System.Security.Claims;
using System.Text;
using HRMS.Application.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// Mints access tokens directly, so tests can present tokens the API would never issue — a forged tenant
/// claim, a foreign signing key, a swapped algorithm, an expired lifetime — and assert they are refused.
/// </summary>
public static class TestTokens
{
    public static string Create(
        Guid userId,
        Guid tenantId,
        string tenantCode = "DEMO01",
        string email = "admin@demo01.com",
        IEnumerable<string>? roles = null,
        IEnumerable<string>? permissions = null,
        string? signingKey = null,
        string algorithm = SecurityAlgorithms.HmacSha256,
        string? issuer = HrmsApiFactory.Issuer,
        string? audience = HrmsApiFactory.Audience,
        DateTime? notBefore = null,
        DateTime? expires = null,
        bool includeTenantClaim = true)
    {
        var issuedAt = notBefore ?? DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(HrmsClaimTypes.UserId, userId.ToString()),
            new(HrmsClaimTypes.TenantCode, tenantCode),
            new(HrmsClaimTypes.Email, email),
            new(HrmsClaimTypes.FirstName, "Test"),
            new(HrmsClaimTypes.LastName, "User")
        };

        if (includeTenantClaim)
        {
            claims.Add(new Claim(HrmsClaimTypes.TenantId, tenantId.ToString()));
        }

        claims.AddRange((roles ?? []).Select(role => new Claim(HrmsClaimTypes.Role, role)));
        claims.AddRange((permissions ?? []).Select(p => new Claim(HrmsClaimTypes.Permission, p)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? HrmsApiFactory.SigningKey));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = expires ?? issuedAt.AddMinutes(30),
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(key, algorithm)
        });
    }
}
