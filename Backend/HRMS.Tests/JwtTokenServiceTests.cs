using System.Text;
using HRMS.Application.Abstractions;
using HRMS.Application.Security;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HRMS.Tests;

/// <summary>
/// Access-token issuance: the tenant travels inside a signed token (so it cannot be altered by a client),
/// permissions are individually addressable claims, and refresh tokens are unguessable and stored hashed.
/// </summary>
public class JwtTokenServiceTests
{
    private const string SecretKey = "unit-test-signing-key-of-sufficient-length-for-hmac-sha256";
    private static readonly Guid UserId = new("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantId = new("bbbbbbbb-0000-0000-0000-000000000002");

    private static JwtSettings Settings(string? secretKey = null, int accessTokenMinutes = 45) => new()
    {
        Issuer = "HRMS.API",
        Audience = "HRMS.Client",
        SecretKey = secretKey ?? SecretKey,
        AccessTokenMinutes = accessTokenMinutes,
        RefreshTokenDays = 7,
        ClockSkewSeconds = 0
    };

    private static AccessTokenDescriptor Descriptor() => new(
        UserId: UserId,
        TenantId: TenantId,
        TenantCode: "DEMO01",
        Email: "admin@demo01.com",
        FirstName: "Alice",
        LastName: "Admin",
        Roles: [RoleNames.TenantAdmin],
        Permissions: [Permissions.Employee.View, Permissions.Employee.Create]);

    private static JwtTokenService CreateService(JwtSettings? settings = null, TimeProvider? timeProvider = null) =>
        new(Options.Create(settings ?? Settings()), timeProvider ?? TimeProvider.System);

    [Fact]
    public void Access_token_carries_identity_tenant_role_and_permission_claims()
    {
        var token = CreateService().CreateAccessToken(Descriptor()).Token;

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        var claims = jwt.Claims.ToList();

        Assert.Equal(UserId.ToString(), claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(UserId.ToString(), claims.Single(c => c.Type == HrmsClaimTypes.UserId).Value);
        Assert.Equal(TenantId.ToString(), claims.Single(c => c.Type == HrmsClaimTypes.TenantId).Value);
        Assert.Equal("DEMO01", claims.Single(c => c.Type == HrmsClaimTypes.TenantCode).Value);
        Assert.Equal("admin@demo01.com", claims.Single(c => c.Type == HrmsClaimTypes.Email).Value);
        Assert.Equal("Alice", claims.Single(c => c.Type == HrmsClaimTypes.FirstName).Value);
        Assert.Equal("Admin", claims.Single(c => c.Type == HrmsClaimTypes.LastName).Value);
        Assert.Equal(RoleNames.TenantAdmin, claims.Single(c => c.Type == HrmsClaimTypes.Role).Value);

        // Repeated claims rather than one delimited value, so a policy can require an exact permission.
        var permissions = claims.Where(c => c.Type == HrmsClaimTypes.Permission).Select(c => c.Value).ToList();
        Assert.Equal([Permissions.Employee.View, Permissions.Employee.Create], permissions);

        // A token identifier is present so individual tokens are traceable in logs.
        Assert.False(string.IsNullOrWhiteSpace(claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value));

        // Nothing password-related is ever emitted.
        Assert.DoesNotContain(claims, c => c.Type.Contains("password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Access_token_is_signed_with_hmac_sha256_and_validates_against_the_configured_key()
    {
        var settings = Settings();
        var issued = CreateService(settings).CreateAccessToken(Descriptor());

        var result = Validate(issued.Token, settings.SecretKey, settings);

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(SecurityAlgorithms.HmacSha256, ((JsonWebToken)result.SecurityToken).Alg);
    }

    [Fact]
    public void Access_token_signed_with_another_key_is_rejected()
    {
        var settings = Settings();
        var issued = CreateService(settings).CreateAccessToken(Descriptor());

        var result = Validate(issued.Token, "a-completely-different-signing-key-of-adequate-length", settings);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Tampering_with_the_payload_invalidates_the_token()
    {
        var settings = Settings();
        var issued = CreateService(settings).CreateAccessToken(Descriptor());

        // Swap the payload segment for one claiming a different tenant, leaving the signature intact.
        var segments = issued.Token.Split('.');
        var forgedPayload = Base64UrlEncoder.Encode(
            Base64UrlEncoder.Decode(segments[1]).Replace(TenantId.ToString(), Guid.Empty.ToString()));
        var forged = string.Join('.', segments[0], forgedPayload, segments[2]);

        var result = Validate(forged, settings.SecretKey, settings);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Access_token_lifetime_follows_configuration()
    {
        var issuedAt = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
        var settings = Settings(accessTokenMinutes: 15);
        var clock = new FakeTimeProvider(issuedAt);

        var issued = CreateService(settings, clock).CreateAccessToken(Descriptor());

        Assert.Equal(issuedAt.UtcDateTime.AddMinutes(15), issued.ExpiresAtUtc);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(issued.Token);
        Assert.Equal(issued.ExpiresAtUtc, jwt.ValidTo, TimeSpan.FromSeconds(1));
        Assert.Equal(issuedAt.UtcDateTime, jwt.ValidFrom, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void An_expired_access_token_fails_validation()
    {
        var settings = Settings(accessTokenMinutes: 5);
        var clock = new FakeTimeProvider(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var issued = CreateService(settings, clock).CreateAccessToken(Descriptor());
        var result = Validate(issued.Token, settings.SecretKey, settings);

        Assert.False(result.IsValid);
        Assert.IsType<SecurityTokenExpiredException>(result.Exception);
    }

    [Fact]
    public void Refresh_tokens_are_random_and_never_repeat()
    {
        var service = CreateService();

        var tokens = Enumerable.Range(0, 200).Select(_ => service.CreateRefreshToken()).ToList();

        Assert.Equal(tokens.Count, tokens.Distinct().Count());
        // 32 random bytes base64url-encoded: no padding, comfortably beyond guessing.
        Assert.All(tokens, t => Assert.True(t.Length >= 43, $"Unexpectedly short refresh token: {t.Length} chars."));
    }

    [Fact]
    public void Refresh_token_hashing_is_deterministic_and_one_way()
    {
        var service = CreateService();
        var token = service.CreateRefreshToken();

        var hash = service.HashRefreshToken(token);

        Assert.Equal(hash, service.HashRefreshToken(token));
        Assert.NotEqual(token, hash);
        Assert.DoesNotContain(token, hash);
        Assert.Equal(64, hash.Length);
        Assert.NotEqual(hash, service.HashRefreshToken(service.CreateRefreshToken()));
    }

    [Theory]
    [InlineData("", "SecretKey")]
    [InlineData("too-short", "SecretKey")]
    public void Construction_fails_fast_on_an_unusable_signing_key(string secretKey, string expectedMention)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateService(Settings(secretKey)));
        Assert.Contains(expectedMention, exception.Message);
    }

    [Fact]
    public void Configuration_validation_accepts_a_sound_configuration()
    {
        Assert.Null(Settings().Validate());
    }

    private static TokenValidationResult Validate(string token, string signingKey, JwtSettings settings) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        }).GetAwaiter().GetResult();

    /// <summary>Fixed clock, so token lifetimes can be asserted exactly.</summary>
    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
