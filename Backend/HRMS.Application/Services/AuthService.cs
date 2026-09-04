using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Application.Security;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HRMS.Application.Services;

/// <summary>
/// Authentication business logic: tenant-scoped credential verification, JWT issuance, refresh-token
/// rotation and sign-out.
/// <para>
/// Multi-tenancy notes. Sign-in and refresh run before any tenant is authenticated, so there is no
/// tenant context to filter by; those reads therefore use <c>IgnoreQueryFilters()</c> and instead filter
/// explicitly on the TenantId of the organization the request's host resolved to. The organization is
/// never something the caller states — it comes from <see cref="IShardContext"/>, which the host
/// resolution middleware fills in from the catalog — so there is no organization field to guess at, and
/// no way to aim a password attempt at an organization other than the one whose address was used.
/// </para>
/// </summary>
public class AuthService : IAuthService
{
    // Generic message for every pre-authentication failure. Using one message for "unknown email" and
    // "wrong password" prevents probing for valid accounts.
    private const string InvalidCredentialsMessage = "Invalid email address or password.";
    private const string InvalidRefreshTokenMessage = "The session is no longer valid. Please sign in again.";

    // Its own message, deliberately not folded into the generic one. Which organization a request belongs
    // to is now the address it arrived at, and an address that belongs to nobody is not a credentials
    // problem: the generic message would send someone to re-type a password that was never wrong. Nor is
    // it a disclosure — a request only reaches here after DNS resolved the host, so the address already
    // answered the question this message would be protecting.
    private const string UnknownWorkspaceMessage =
        "There is no organization at this address. Check the address you used, or ask your administrator for it.";

    private static string? _dummyPasswordHash;

    private readonly IHrmsDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _tokenService;
    private readonly ITenantContext _tenantContext;
    private readonly IShardContext _shardContext;
    private readonly JwtSettings _jwtSettings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IHrmsDbContext db,
        IPasswordHasher passwordHasher,
        IJwtTokenService tokenService,
        ITenantContext tenantContext,
        IShardContext shardContext,
        IOptions<JwtSettings> jwtSettings,
        TimeProvider timeProvider,
        ILogger<AuthService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _tenantContext = tenantContext;
        _shardContext = shardContext;
        _jwtSettings = jwtSettings.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        // The organization comes from the address, not from the request body. With no organization there is
        // no set of credentials to check against and — in a sharded deployment — no database to check them
        // in, so this is not a "wrong password" case and is not reported as one.
        if (_shardContext.Current is not ShardDescriptor shard)
        {
            _logger.LogWarning("Sign-in rejected: the request's host resolves to no organization.");
            return Result<LoginResponse>.Unauthorized(UnknownWorkspaceMessage);
        }

        var email = (request.Email ?? string.Empty).Trim();
        var normalizedEmail = email.ToLowerInvariant();

        // Read from the organization's own database rather than trusting the descriptor, which is built
        // from the catalog and cached: the name shown to the user and the status enforced below have to be
        // the ones in the database this sign-in will actually operate on. Tenants are not tenant-scoped,
        // so this read needs no filter bypass.
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == shard.TenantId, cancellationToken);

        if (tenant is null)
        {
            // The catalog routes this host to an organization its own database has no row for, which means
            // the shard was never provisioned or was pointed at the wrong database. Logged as an error
            // because it is our misconfiguration, and reported generically because the caller can do
            // nothing with the detail.
            VerifyAgainstDummyHash(request.Password);
            _logger.LogError(
                "Sign-in rejected: shard {ShardKey} holds no row for tenant {TenantId} that the catalog routes to it.",
                shard.ShardKey, shard.TenantId);
            return Result<LoginResponse>.Unauthorized(InvalidCredentialsMessage);
        }

        // No tenant is authenticated yet, so bypass the global filter and scope explicitly to the
        // organization the host resolved to. Email uniqueness is per tenant, so both predicates are
        // required — and in the shared-database deployment this predicate is the whole isolation.
        // Compared lower-cased so sign-in is case-insensitive on every provider (SQL Server's default
        // collation already is; SQLite is not).
        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                u => u.TenantId == tenant.Id && u.Email.ToLower() == normalizedEmail,
                cancellationToken);

        if (user is null)
        {
            // Spend comparable time to a real verification so response latency does not reveal whether
            // the account exists.
            VerifyAgainstDummyHash(request.Password);
            _logger.LogWarning(
                "Sign-in rejected: no account for {Email} in tenant {TenantCode}.", email, tenant.TenantCode);
            return Result<LoginResponse>.Unauthorized(InvalidCredentialsMessage);
        }

        if (!_passwordHasher.Verify(user.PasswordHash, request.Password ?? string.Empty))
        {
            _logger.LogWarning(
                "Sign-in rejected: incorrect password for user {UserId} in tenant {TenantId}.", user.Id, tenant.Id);
            return Result<LoginResponse>.Unauthorized(InvalidCredentialsMessage);
        }

        // Past this point the credentials are proven, so specific (actionable) messages leak nothing.
        if (!user.IsActive)
        {
            _logger.LogWarning("Sign-in rejected: user {UserId} is deactivated.", user.Id);
            return Result<LoginResponse>.Forbidden("This account has been deactivated. Please contact your administrator.");
        }

        // Checked here as well as by the host resolver, which refuses a suspended organization before the
        // request reaches this service. Two copies of the status exist — the catalog's and the shard's —
        // and this is the one in the database the session would operate on, so it is the one that decides.
        if (tenant.Status != TenantStatus.Active)
        {
            _logger.LogWarning(
                "Sign-in rejected: tenant {TenantId} has status {Status}.", tenant.Id, tenant.Status);
            return Result<LoginResponse>.Forbidden($"Your organization's account is {tenant.Status.ToString().ToLowerInvariant()}. Please contact support.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        user.LastLoginDate = now;

        var response = await IssueTokensAsync(user, tenant, now, cancellationToken);

        _logger.LogInformation(
            "User {UserId} signed in to tenant {TenantId}.", user.Id, tenant.Id);

        return Result<LoginResponse>.Success(response, "Sign-in successful.");
    }

    public async Task<Result<LoginResponse>> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var presented = (request.RefreshToken ?? string.Empty).Trim();
        if (presented.Length == 0)
        {
            return Result<LoginResponse>.Unauthorized(InvalidRefreshTokenMessage);
        }

        var tokenHash = _tokenService.HashRefreshToken(presented);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Refresh is an unauthenticated endpoint (the access token may already have expired), so no
        // tenant is resolved and the filter must be bypassed; the token row itself carries the tenant.
        var stored = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (stored is null)
        {
            _logger.LogWarning("Refresh rejected: token not recognized.");
            return Result<LoginResponse>.Unauthorized(InvalidRefreshTokenMessage);
        }

        // A refresh token belongs to one organization, and so does the address it was presented at. If they
        // disagree, the token was taken to a workspace it does not belong to: in a sharded deployment the
        // hash was found in a database that is not this token's, and in the shared deployment the
        // replacement session would be issued to a caller at somebody else's address.
        //
        // Null-tolerant, matching the authorization requirement on every other route: a host that resolves
        // to no organization has nothing for the token to disagree with, and nothing can be read without a
        // shard anyway once each organization has its own database.
        if (_shardContext.Current is ShardDescriptor shard && shard.TenantId != stored.TenantId)
        {
            _logger.LogWarning(
                "Refresh rejected: a token for tenant {TokenTenantId} was presented at the host of {HostTenantId}.",
                stored.TenantId, shard.TenantId);
            return Result<LoginResponse>.Unauthorized(InvalidRefreshTokenMessage);
        }

        // Separately, if a caller happens to also present a bearer token it must belong to the same tenant
        // as the refresh token. Refresh is anonymous, so the default authorization policy — which is what
        // enforces token/host agreement everywhere else — never runs on this path.
        if (_tenantContext.TenantId is Guid callerTenantId && callerTenantId != stored.TenantId)
        {
            _logger.LogWarning(
                "Refresh rejected: caller tenant {CallerTenantId} does not own refresh token for tenant {TokenTenantId}.",
                callerTenantId, stored.TenantId);
            return Result<LoginResponse>.Unauthorized(InvalidRefreshTokenMessage);
        }

        if (stored.RevokedAtUtc is not null)
        {
            // A consumed token was replayed: either the client retried, or a stolen token is in play.
            // Treat it as compromise and drop every active session for that user.
            await RevokeAllActiveTokensForUserAsync(stored.TenantId, stored.UserId, now, cancellationToken);
            _logger.LogWarning(
                "Refresh rejected: replay of a revoked token for user {UserId}; all active sessions revoked.",
                stored.UserId);
            return Result<LoginResponse>.Unauthorized(InvalidRefreshTokenMessage);
        }

        if (stored.ExpiresAtUtc <= now)
        {
            _logger.LogInformation("Refresh rejected: token expired for user {UserId}.", stored.UserId);
            return Result<LoginResponse>.Unauthorized(InvalidRefreshTokenMessage);
        }

        var user = await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == stored.UserId && u.TenantId == stored.TenantId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            stored.RevokedAtUtc = now;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Refresh rejected: user {UserId} is missing or deactivated.", stored.UserId);
            return Result<LoginResponse>.Unauthorized(InvalidRefreshTokenMessage);
        }

        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == stored.TenantId, cancellationToken);

        if (tenant is null || tenant.Status != TenantStatus.Active)
        {
            stored.RevokedAtUtc = now;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning("Refresh rejected: tenant {TenantId} is missing or not active.", stored.TenantId);
            return Result<LoginResponse>.Unauthorized(InvalidRefreshTokenMessage);
        }

        // Rotation: the presented token is consumed and replaced. Consuming it is a single conditional
        // UPDATE rather than a read-modify-save, because the row was read a few statements ago: if the
        // same token is presented twice at once, both callers would otherwise see it as live and both
        // would be issued a new family, which is precisely the theft the rotation scheme exists to catch.
        // The UPDATE matches only while the token is still live, so exactly one caller can win.
        var consumed = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.Id == stored.Id && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.RevokedAtUtc, now)
                    .SetProperty(t => t.ModifiedDate, now),
                cancellationToken);

        if (consumed == 0)
        {
            // The token was revoked between the read above and this update — a concurrent presentation of
            // the same token. Indistinguishable from a replay, so it is handled as one.
            await RevokeAllActiveTokensForUserAsync(stored.TenantId, stored.UserId, now, cancellationToken);
            _logger.LogWarning(
                "Refresh rejected: token for user {UserId} was already consumed concurrently; all active sessions revoked.",
                stored.UserId);
            return Result<LoginResponse>.Unauthorized(InvalidRefreshTokenMessage);
        }

        // Roles and permissions are re-read from the database, so an administrator's change takes effect
        // on the next refresh.
        var response = await IssueTokensAsync(user, tenant, now, cancellationToken, replaces: stored);

        _logger.LogInformation("Refreshed tokens for user {UserId} in tenant {TenantId}.", user.Id, tenant.Id);

        return Result<LoginResponse>.Success(response, "Token refreshed.");
    }

    public async Task<Result<bool>> LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var presented = (request.RefreshToken ?? string.Empty).Trim();
        if (presented.Length == 0)
        {
            // Nothing to revoke; report success so sign-out is idempotent and reveals nothing.
            return Result<bool>.Success(true, "Signed out.");
        }

        var tokenHash = _tokenService.HashRefreshToken(presented);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Sign-out is authenticated, so the tenant filter applies and a caller can only revoke tokens
        // belonging to their own tenant. The user check confines it further to their own sessions.
        //
        // Nothing here consults the shard, and it does not need to: this route is [Authorize], so the
        // default policy has already refused any token that disagrees with the host — the filter's tenant
        // and the database this scope opened cannot be different by the time the query runs.
        var query = _db.RefreshTokens.Where(t => t.TokenHash == tokenHash);
        if (_tenantContext.UserId is Guid callerUserId)
        {
            query = query.Where(t => t.UserId == callerUserId);
        }

        var stored = await query.FirstOrDefaultAsync(cancellationToken);
        if (stored is not null && stored.RevokedAtUtc is null)
        {
            stored.RevokedAtUtc = now;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("User {UserId} signed out; refresh token revoked.", stored.UserId);
        }

        return Result<bool>.Success(true, "Signed out.");
    }

    public async Task<Result<AuthenticatedUserDto>> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.UserId is not Guid userId || _tenantContext.TenantId is not Guid tenantId)
        {
            return Result<AuthenticatedUserDto>.Unauthorized("No authenticated user.");
        }

        // Read through the global query filter on purpose: it proves the request's tenant scope is what
        // reaches the database, rather than trusting the token's claims alone.
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            _logger.LogWarning(
                "Authenticated user {UserId} was not found within tenant {TenantId}.", userId, tenantId);
            return Result<AuthenticatedUserDto>.NotFound("The signed-in account no longer exists.");
        }

        if (!user.IsActive)
        {
            return Result<AuthenticatedUserDto>.Forbidden("This account has been deactivated.");
        }

        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == user.TenantId, cancellationToken);

        if (tenant is null)
        {
            return Result<AuthenticatedUserDto>.NotFound("The organization no longer exists.");
        }

        var (roles, permissions) = await LoadAuthorizationAsync(user.Id, user.TenantId, cancellationToken);
        var dto = ToDto(user, tenant, roles, permissions);
        var identity = await ResolveEmployeeIdentityAsync(user.Id, user.TenantId, cancellationToken);
        return Result<AuthenticatedUserDto>.Success(dto with { EmployeeIdentity = identity });
    }

    /// <summary>
    /// Mints an access token plus a fresh refresh token, records the refresh token's hash, and persists
    /// everything (including any pending audit changes) in a single save.
    /// </summary>
    private async Task<LoginResponse> IssueTokensAsync(
        User user,
        Tenant tenant,
        DateTime now,
        CancellationToken cancellationToken,
        RefreshToken? replaces = null)
    {
        var (roles, permissions) = await LoadAuthorizationAsync(user.Id, tenant.Id, cancellationToken);

        var accessToken = _tokenService.CreateAccessToken(new AccessTokenDescriptor(
            UserId: user.Id,
            TenantId: tenant.Id,
            TenantCode: tenant.TenantCode,
            Email: user.Email,
            FirstName: user.FirstName,
            LastName: user.LastName,
            Roles: roles,
            Permissions: permissions));

        var refreshToken = _tokenService.CreateRefreshToken();
        var refreshTokenHash = _tokenService.HashRefreshToken(refreshToken);

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            ExpiresAtUtc = now.AddDays(_jwtSettings.RefreshTokenDays),
            CreatedDate = now
        });

        if (replaces is not null)
        {
            // Only this column is marked modified, so the save cannot overwrite the revocation that the
            // conditional update above already wrote (the tracked copy still believes it is live).
            replaces.ReplacedByTokenHash = refreshTokenHash;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var expiresInSeconds = (int)Math.Max(0, (accessToken.ExpiresAtUtc - now).TotalSeconds);

        return new LoginResponse(
            AccessToken: accessToken.Token,
            RefreshToken: refreshToken,
            AccessTokenExpiresAtUtc: accessToken.ExpiresAtUtc,
            ExpiresInSeconds: expiresInSeconds,
            User: ToDto(user, tenant, roles, permissions));
    }

    /// <summary>
    /// Resolves the role names a user holds in a tenant and the distinct permissions those roles grant.
    /// Roles, permissions and role→permission grants are platform-wide reference data (not tenant-scoped);
    /// only the user→role assignment carries a tenant, and it is filtered explicitly here.
    /// </summary>
    private async Task<(List<string> Roles, List<string> Permissions)> LoadAuthorizationAsync(
        Guid userId, Guid tenantId, CancellationToken cancellationToken)
    {
        var roleIds = await _db.UserRoles
            .IgnoreQueryFilters()
            .Where(ur => ur.UserId == userId && ur.TenantId == tenantId)
            .Select(ur => ur.RoleId)
            .ToListAsync(cancellationToken);

        if (roleIds.Count == 0)
        {
            return ([], []);
        }

        var roles = await _db.Roles
            .Where(r => roleIds.Contains(r.Id))
            .OrderBy(r => r.Name)
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        var permissions = await (
                from rolePermission in _db.RolePermissions
                join permission in _db.Permissions on rolePermission.PermissionId equals permission.Id
                where roleIds.Contains(rolePermission.RoleId)
                select permission.Name)
            .Distinct()
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);

        return (roles, permissions);
    }

    private async Task RevokeAllActiveTokensForUserAsync(
        Guid tenantId, Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var active = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId && t.UserId == userId && t.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in active)
        {
            token.RevokedAtUtc = now;
        }

        if (active.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static AuthenticatedUserDto ToDto(
        User user, Tenant tenant, IReadOnlyList<string> roles, IReadOnlyList<string> permissions) =>
        new(
            Id: user.Id,
            TenantId: user.TenantId,
            TenantCode: tenant.TenantCode,
            TenantName: tenant.TenantName,
            Email: user.Email,
            FirstName: user.FirstName,
            LastName: user.LastName,
            FullName: $"{user.FirstName} {user.LastName}".Trim(),
            LastLoginDateUtc: user.LastLoginDate,
            Roles: roles,
            Permissions: permissions);

    private async Task<EmployeeIdentityDto> ResolveEmployeeIdentityAsync(Guid userId, Guid tenantId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var latest = await _db.AccountEmployeeLinkEvents.AsNoTracking().Where(x => x.SubjectUserId == userId).OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
        var link = await _db.AccountEmployeeCurrentLinks.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (link is null) return new("Unlinked", latest?.Id, null, null, "NotLinked", today);
        var employee = await _db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.Id == link.EmployeeId && x.TenantId == tenantId, ct);
        if (employee is null) return new("Invalid", null, null, null, "RequiresReview", today);
        var history = await _db.EmployeeEmploymentHistory.AsNoTracking().Where(x => x.EmployeeId == employee.Id && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today)).ToListAsync(ct);
        var eligibility = employee.DateOfJoining > today ? "FutureJoining" : history.Count != 1 ? "NoApplicableEmployment" : history[0].EmploymentStatus == EmployeeStatus.Active && employee.Status == EmployeeStatus.Active ? "ActiveEmployment" : "Separated";
        var creation = await _db.AccountEmployeeLinkEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == link.LinkId, ct);
        if (creation is null) return new("Invalid", null, null, null, "RequiresReview", today);
        return new("Linked", latest?.Id, link.LinkId, new(employee.Id, string.Join(" ", new[] { employee.FirstName, employee.MiddleName, employee.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))), employee.EmployeeCode), eligibility, today);
    }

    /// <summary>
    /// Runs a password verification against a throwaway hash so that "no such tenant/user" costs roughly
    /// the same as a real check. The hash is computed once per process and never leaves it.
    /// </summary>
    private void VerifyAgainstDummyHash(string? password)
    {
        var hash = _dummyPasswordHash ??= _passwordHasher.Hash($"unused-{Guid.NewGuid()}");
        _passwordHasher.Verify(hash, password ?? string.Empty);
    }
}
