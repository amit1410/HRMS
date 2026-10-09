using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HRMS.API.Controllers;

/// <summary>
/// Diagnostic endpoint that confirms the databases were created and seeded. It stays anonymous because it
/// must be usable before anyone can sign in, so outside development it reports nothing but the fact that
/// the API is up. The counts, the provider name and the per-tenant breakdown are development-only: to an
/// unauthenticated caller they would disclose how large the platform is, what it runs on, and who its
/// customers are.
/// </summary>
[ApiController]
[Route("api/system")]
[Produces("application/json")]
[AllowAnonymous]
public class SystemController : ControllerBase
{
    /// <summary>
    /// What of the product this build belongs to. Kept in one place so the two responses below cannot
    /// drift apart, and updated as phases land.
    /// </summary>
    private const string CurrentPhase = "Phase 7 - whitelabel host resolution with a database per organization";

    private readonly IHrmsCatalogDbContext _catalog;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SystemController> _logger;
    private readonly PasswordRecoveryProviderOptions? _recoveryProviders;
    private readonly IEmailSender? _emailSender;

    /// <summary>
    /// Deliberately no <see cref="HrmsDbContext"/>. This endpoint is reachable at the apex — the address the
    /// client is served from before any organization is chosen — where no shard is resolved and a tenant
    /// context cannot be constructed at all. Injecting one would turn the liveness check into a 500 on
    /// exactly the host used to find out whether the API is alive.
    /// </summary>
    public SystemController(
        IHrmsCatalogDbContext catalog,
        IServiceScopeFactory scopeFactory,
        IHostEnvironment environment,
        ILogger<SystemController> logger,
        PasswordRecoveryProviderOptions? recoveryProviders = null,
        IEmailSender? emailSender = null)
    {
        _catalog = catalog;
        _scopeFactory = scopeFactory;
        _environment = environment;
        _logger = logger;
        _recoveryProviders = recoveryProviders;
        _emailSender = emailSender;
    }

    [HttpGet("info")]
    [ProducesResponseType(typeof(ApiResponse<SystemInfoResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<SystemInfoResponse>>> GetInfo(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            // Not one query, on purpose. A liveness check that reads a database reports the database's health
            // as its own, and this one has to answer while a shard is unreachable.
            return Ok(ApiResponse<SystemInfoResponse>.Ok(
                new SystemInfoResponse(
                    Application: "HRMS API",
                    Phase: CurrentPhase,
                    UtcNow: DateTime.UtcNow),
                "HRMS API is running."));
        }

        // Development only from here down.
        var organizations = await ReadCatalogAsync(cancellationToken);
        var breakdown = await CountPerOrganizationAsync(organizations, cancellationToken);
        var platformUser = await ReadPlatformUserAsync(cancellationToken);

        var info = new SystemInfoResponse(
            Application: "HRMS API",
            Phase: CurrentPhase,
            UtcNow: DateTime.UtcNow,
            DatabaseProvider: breakdown.DatabaseProvider,
            CatalogDatabase: (_catalog as DbContext)?.Database.GetDbConnection().Database,
            RoleCount: breakdown.RoleCount,
            PermissionCount: breakdown.PermissionCount,
            TenantCount: organizations.Count,
            UserCount: breakdown.Summaries.Sum(summary => summary.UserCount),
            Tenants: breakdown.Summaries,
            Environment: _environment.EnvironmentName,
            PasswordRecoveryEmailProvider: _recoveryProviders?.EmailProvider,
            PasswordRecoveryEmailSender: _emailSender?.GetType().Name,
            DevelopmentOtpAvailable: _environment.IsDevelopment()
                && _emailSender is DevelopmentEmailSender,
            PlatformUser: platformUser);

        return Ok(ApiResponse<SystemInfoResponse>.Ok(info, "HRMS API is running and the database is seeded."));
    }

    /// <summary>
    /// Which organizations exist, from the catalog — the only place that knows. The catalog spans every
    /// tenant and carries no query filters, so this needs no <c>IgnoreQueryFilters</c> and no ambient tenant.
    /// </summary>
    private async Task<List<CatalogOrganization>> ReadCatalogAsync(CancellationToken cancellationToken) =>
        await _catalog.Tenants
            .AsNoTracking()
            .OrderBy(tenant => tenant.TenantCode)
            .Select(tenant => new CatalogOrganization(
                new ShardDescriptor(tenant.Id, tenant.TenantCode, tenant.Host, tenant.ShardKey, tenant.Status, tenant.DatabaseProvider),
                tenant.TenantName))
            .ToListAsync(cancellationToken);

    private async Task<PlatformUserSummary> ReadPlatformUserAsync(CancellationToken cancellationToken)
    {
        const string normalizedEmail = "AMIT@ANEVRATECHNOLOGIES.COM";
        var user = await _catalog.PlatformUsers
            .AsNoTracking()
            .Where(candidate => candidate.NormalizedEmail == normalizedEmail)
            .Select(candidate => new PlatformUserSummary(
                true,
                candidate.IsActive,
                candidate.PasswordHash != null && candidate.PasswordHash != string.Empty,
                candidate.UserRoles.Any(role => role.PlatformRole != null && role.PlatformRole.Name == "PlatformSuperAdmin")))
            .SingleOrDefaultAsync(cancellationToken);

        return user ?? new PlatformUserSummary(false, false, false, false);
    }

    /// <summary>
    /// Counts each organization's users in that organization's own database, one scope at a time.
    /// <para>
    /// The fan-out is the point. There is no cross-tenant query to write any more — in the sharded mode the
    /// rows are in different databases, and no join reaches them — so a platform-wide figure can only be a
    /// sum of per-organization reads. Same shape as
    /// <see cref="HRMS.Infrastructure.Persistence.TenantProvisioningService"/>, and for the same reason: a
    /// scope picks its shard once, when the context is first resolved.
    /// </para>
    /// <para>
    /// One organization's unreachable database is reported as zero and logged, never propagated. This is the
    /// endpoint an operator opens <em>because</em> something is wrong, and failing the whole response would
    /// hide which of the shards is the broken one — the single most useful thing it has to say.
    /// </para>
    /// </summary>
    private async Task<PlatformBreakdown> CountPerOrganizationAsync(
        List<CatalogOrganization> organizations,
        CancellationToken cancellationToken)
    {
        var summaries = new List<TenantSummary>(organizations.Count);
        string? databaseProvider = null;
        int? roleCount = null;
        int? permissionCount = null;

        foreach (var organization in organizations)
        {
            var shard = organization.Shard;
            var userCount = 0;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var provider = scope.ServiceProvider;
                provider.GetRequiredService<IShardContext>().Use(shard);

                var db = provider.GetRequiredService<HrmsDbContext>();

                // Filtered on TenantId rather than counting the table, which is what makes this one
                // expression correct in both deployment modes: in the sharded mode the database holds only
                // this organization's users and the predicate matches all of them, and in the shared-database
                // mode it is what stops every organization from reporting the platform's total as its own.
                // IgnoreQueryFilters because no tenant is resolved on an anonymous call, so the ambient
                // filter would match nothing at all.
                userCount = await db.Users
                    .IgnoreQueryFilters()
                    .CountAsync(user => user.TenantId == shard.TenantId, cancellationToken);

                // Roles and permissions are platform-wide definitions replicated into every shard by the
                // seeder, identical by construction — so the first shard that answers speaks for all of them,
                // and asking each one would report the same two numbers N times.
                if (databaseProvider is null)
                {
                    databaseProvider = db.Database.ProviderName ?? "unknown";
                    roleCount = await db.Roles.CountAsync(cancellationToken);
                    permissionCount = await db.Permissions.CountAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Could not read organization {TenantCode} on shard {ShardKey}; reporting zero users for "
                    + "it. Every other organization in this response was read normally.",
                    shard.TenantCode,
                    shard.ShardKey);
            }

            summaries.Add(new TenantSummary(
                shard.TenantId, shard.TenantCode, organization.TenantName, shard.Host, shard.ShardKey,
                shard.DatabaseProvider.ToString(), shard.Status.ToString(), userCount));
        }

        return new PlatformBreakdown(summaries, databaseProvider, roleCount, permissionCount);
    }

    /// <summary>
    /// An organization as the catalog holds it: how to reach its database, plus the name to display. The
    /// descriptor carries no connection string by design, so this is safe to project into a response.
    /// </summary>
    private sealed record CatalogOrganization(ShardDescriptor Shard, string TenantName);

    /// <summary>
    /// The result of the fan-out. The three nullable members stay null when no shard could be read at all,
    /// which is a meaningfully different answer from a zero.
    /// </summary>
    private sealed record PlatformBreakdown(
        List<TenantSummary> Summaries,
        string? DatabaseProvider,
        int? RoleCount,
        int? PermissionCount);
}

/// <summary>
/// Everything after <see cref="UtcNow"/> is development-only detail and is omitted from the response
/// outside development (null members are not serialized).
/// </summary>
public record SystemInfoResponse(
    string Application,
    string Phase,
    DateTime UtcNow,
    string? DatabaseProvider = null,
    string? CatalogDatabase = null,
    int? RoleCount = null,
    int? PermissionCount = null,
    int? TenantCount = null,
    int? UserCount = null,
    IReadOnlyList<TenantSummary>? Tenants = null,
    string? Environment = null,
    string? PasswordRecoveryEmailProvider = null,
    string? PasswordRecoveryEmailSender = null,
    bool? DevelopmentOtpAvailable = null,
    PlatformUserSummary? PlatformUser = null);

public record TenantSummary(
    Guid TenantId,
    string TenantCode,
    string TenantName,
    string Host,
    string ShardKey,
    string DatabaseProvider,
    string Status,
    int UserCount);

public record PlatformUserSummary(bool Exists, bool Active, bool PasswordHashPresent, bool PlatformSuperAdminAssigned);
