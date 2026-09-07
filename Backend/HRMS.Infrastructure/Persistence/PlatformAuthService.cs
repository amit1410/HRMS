using System.Globalization;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.PlatformAuth;
using HRMS.Application.Security;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRMS.Infrastructure.Persistence;

public sealed class PlatformAuthService : IPlatformAuthService
{
    private const string InvalidCredentials = "Invalid email address or password.";
    private const string InvalidRefresh = "The platform session is no longer valid. Please sign in again.";
    private readonly IHrmsCatalogDbContext _catalog;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IPlatformTokenService _tokens;
    private readonly PlatformJwtSettings _settings;
    private readonly TimeProvider _clock;
    private readonly ILogger<PlatformAuthService> _logger;
    private readonly IPlatformContext _platformContext;
    private readonly string _dummyPasswordHash;

    public PlatformAuthService(
        IHrmsCatalogDbContext catalog,
        IPasswordHasher passwordHasher,
        IPlatformTokenService tokens,
        IOptions<PlatformJwtSettings> settings,
        TimeProvider clock,
        ILogger<PlatformAuthService> logger,
        IPlatformContext platformContext)
    {
        _catalog = catalog;
        _passwordHasher = passwordHasher;
        _tokens = tokens;
        _settings = settings.Value;
        _clock = clock;
        _logger = logger;
        _platformContext = platformContext;
        _dummyPasswordHash = _passwordHasher.Hash(Guid.NewGuid().ToString("N"));
    }

    public async Task<Result<PlatformLoginResponse>> LoginAsync(PlatformLoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = (request.Email ?? string.Empty).Trim().ToUpperInvariant();
        var user = await _catalog.PlatformUsers
            .Include(x => x.UserRoles).ThenInclude(x => x.PlatformRole)
            .SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, cancellationToken);
        var passwordHash = user?.PasswordHash ?? _dummyPasswordHash;
        if (!_passwordHasher.Verify(passwordHash, request.Password ?? string.Empty))
        {
            _logger.LogWarning("Platform login rejected for normalized email {NormalizedEmail}.", normalized);
            return Result<PlatformLoginResponse>.Unauthorized(InvalidCredentials);
        }
        if (user is null) return Result<PlatformLoginResponse>.Unauthorized(InvalidCredentials);
        if (!user.IsActive) return Result<PlatformLoginResponse>.Forbidden("This platform account has been deactivated.");
        var now = _clock.GetUtcNow().UtcDateTime;
        var grants = await LoadGrantsAsync(user.Id, cancellationToken);
        user.LastLoginAtUtc = now;
        var response = await IssueAsync(user, grants, now, cancellationToken);
        _logger.LogInformation("Platform login succeeded for {PlatformUserId}.", user.Id);
        return Result<PlatformLoginResponse>.Success(response, "Sign-in successful.");
    }

    public async Task<Result<PlatformLoginResponse>> RefreshAsync(PlatformRefreshRequest request, CancellationToken cancellationToken = default)
    {
        var presented = (request.RefreshToken ?? string.Empty).Trim();
        if (presented.Length == 0) return Result<PlatformLoginResponse>.Unauthorized(InvalidRefresh);
        var now = _clock.GetUtcNow().UtcDateTime;
        var hash = _tokens.HashRefreshToken(presented);
        var stored = await _catalog.PlatformRefreshTokens
            .AsNoTracking()
            .Include(x => x.PlatformUser)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (stored is null || stored.RevokedAtUtc is not null || stored.ExpiresAtUtc <= now || stored.PlatformUser is null)
            return Result<PlatformLoginResponse>.Unauthorized(InvalidRefresh);
        var user = stored.PlatformUser;
        if (!user.IsActive) return Result<PlatformLoginResponse>.Forbidden("This platform account has been deactivated.");
        var consumed = await _catalog.PlatformRefreshTokens
            .Where(x => x.Id == stored.Id && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAtUtc, now), cancellationToken);
        if (consumed != 1)
            return Result<PlatformLoginResponse>.Unauthorized(InvalidRefresh);
        var grants = await LoadGrantsAsync(user.Id, cancellationToken);
        var response = await IssueAsync(user, grants, now, cancellationToken, stored);
        return Result<PlatformLoginResponse>.Success(response, "Token refreshed.");
    }

    public async Task<Result<bool>> LogoutAsync(PlatformLogoutRequest request, CancellationToken cancellationToken = default)
    {
        var hash = _tokens.HashRefreshToken((request.RefreshToken ?? string.Empty).Trim());
        var token = await _catalog.PlatformRefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (token is not null && token.RevokedAtUtc is null)
        {
            token.RevokedAtUtc = _clock.GetUtcNow().UtcDateTime;
            await _catalog.SaveChangesAsync(cancellationToken);
        }
        return Result<bool>.Success(true, "Signed out.");
    }

    public async Task<Result<PlatformIdentityDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (_platformContext.UserId is not Guid id) return Result<PlatformIdentityDto>.Unauthorized("A platform identity is required.");
        var user = await _catalog.PlatformUsers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null || !user.IsActive) return Result<PlatformIdentityDto>.Forbidden("This platform account is no longer active.");
        if (!_platformContext.HasSecurityRevision ||
            _platformContext.SecurityRevision is not int tokenRevision ||
            tokenRevision != user.SecurityRevision)
            return Result<PlatformIdentityDto>.Unauthorized("The platform session is no longer valid. Please sign in again.");
        var grants = await LoadGrantsAsync(id, cancellationToken);
        return Result<PlatformIdentityDto>.Success(new PlatformIdentityDto(
            user.Id, user.Email, user.FirstName, user.LastName, $"{user.FirstName} {user.LastName}".Trim(), grants.Roles, grants.Permissions));
    }

    private async Task<PlatformLoginResponse> IssueAsync(
        PlatformUser user,
        (List<string> Roles, List<string> Permissions) grants,
        DateTime now,
        CancellationToken cancellationToken,
        PlatformRefreshToken? replaced = null)
    {
        var access = _tokens.CreateAccessToken(user.Id, user.Email, user.FirstName, user.LastName, grants.Roles, grants.Permissions, user.SecurityRevision);
        var refresh = _tokens.CreateRefreshToken();
        var entity = new PlatformRefreshToken
        {
            Id = Guid.NewGuid(), PlatformUserId = user.Id, TokenHash = _tokens.HashRefreshToken(refresh),
            CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(_settings.RefreshTokenDays),
            ReplacedByTokenId = null
        };
        _catalog.PlatformRefreshTokens.Add(entity);
        if (replaced is not null) replaced.ReplacedByTokenId = entity.Id;
        await _catalog.SaveChangesAsync(cancellationToken);
        return new PlatformLoginResponse(
            access.Token, refresh, access.ExpiresAtUtc,
            (int)Math.Max(0, (access.ExpiresAtUtc - now).TotalSeconds),
            new PlatformIdentityDto(user.Id, user.Email, user.FirstName, user.LastName,
                $"{user.FirstName} {user.LastName}".Trim(), grants.Roles, grants.Permissions));
    }

    private async Task<(List<string> Roles, List<string> Permissions)> LoadGrantsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var roleIds = await _catalog.PlatformUserRoles.Where(x => x.PlatformUserId == userId).Select(x => x.PlatformRoleId).ToListAsync(cancellationToken);
        var roles = await _catalog.PlatformRoles.Where(x => roleIds.Contains(x.Id)).OrderBy(x => x.Name).Select(x => x.Name).ToListAsync(cancellationToken);
        var permissions = await (from rp in _catalog.PlatformRolePermissions
                                 join p in _catalog.PlatformPermissions on rp.PlatformPermissionId equals p.Id
                                 where roleIds.Contains(rp.PlatformRoleId)
                                 select p.Name).Distinct().OrderBy(x => x).ToListAsync(cancellationToken);
        return (roles, permissions);
    }
}
