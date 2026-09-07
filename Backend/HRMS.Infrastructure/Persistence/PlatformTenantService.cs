using System.Security.Cryptography;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.PlatformTenants;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Domain.Authorization;

namespace HRMS.Infrastructure.Persistence;

/// <summary>
/// Orchestrates platform onboarding. The catalog row is deliberately inserted as Inactive and is only
/// activated after the existing idempotent shard provisioning and initial-admin creation both succeed.
/// </summary>
public sealed class PlatformTenantService : IPlatformTenantService
{
    private readonly IHrmsCatalogDbContext _catalog;
    private readonly ITenantProvisioningService _provisioning;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IPlatformContext _actor;
    private readonly ILogger<PlatformTenantService> _logger;

    public PlatformTenantService(
        IHrmsCatalogDbContext catalog,
        ITenantProvisioningService provisioning,
        IServiceScopeFactory scopeFactory,
        IPasswordHasher passwordHasher,
        IHostEnvironment environment,
        IConfiguration configuration,
        IPlatformContext actor,
        ILogger<PlatformTenantService> logger)
    {
        _catalog = catalog;
        _provisioning = provisioning;
        _scopeFactory = scopeFactory;
        _passwordHasher = passwordHasher;
        _environment = environment;
        _configuration = configuration;
        _actor = actor;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<PlatformTenantListItemDto>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _catalog.Tenants.AsNoTracking().OrderBy(x => x.TenantCode).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<PlatformTenantListItemDto>>.Success(
            tenants.Select(ToListItem).ToList());
    }

    public async Task<Result<PlatformTenantDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenant = await _catalog.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return tenant is null
            ? Result<PlatformTenantDetailDto>.NotFound("Tenant was not found.")
            : Result<PlatformTenantDetailDto>.Success(ToDetail(tenant));
    }

    public async Task<Result<PlatformTenantDetailDto>> CreateAsync(
        CreatePlatformTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            return Result<PlatformTenantDetailDto>.Invalid(
                "Initial administrator invitations are not configured outside Development. Onboarding is unavailable until an invitation provider is configured.");
        }

        var tenantCode = request.TenantCode.Trim().ToUpperInvariant();
        var host = request.Host.Trim().ToLowerInvariant();
        var shardKey = request.ShardKey.Trim().ToLowerInvariant();
        var adminEmail = request.InitialAdminEmail.Trim().ToLowerInvariant();

        var shapeError = ValidateNormalizedValues(tenantCode, host, shardKey, _environment.IsDevelopment());
        if (shapeError is not null)
            return Result<PlatformTenantDetailDto>.Invalid(shapeError.Value.Field, shapeError.Value.Message);
        if (request.DatabaseProvider is not (DatabaseProviderType.SqlServer or DatabaseProviderType.MySql))
            return Result<PlatformTenantDetailDto>.Invalid(nameof(request.DatabaseProvider), "DatabaseProvider is not supported.");

        if (await _catalog.Tenants.AnyAsync(x => x.TenantCode.ToUpper() == tenantCode, cancellationToken))
            return Result<PlatformTenantDetailDto>.Conflict("TenantCode is already in use.");
        if (await _catalog.Tenants.AnyAsync(x => x.Host.ToLower() == host, cancellationToken))
            return Result<PlatformTenantDetailDto>.Conflict("Host is already in use.");
        if (await _catalog.Tenants.AnyAsync(x => x.ShardKey.ToLower() == shardKey, cancellationToken))
            return Result<PlatformTenantDetailDto>.Conflict("ShardKey is already in use.");

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            TenantCode = tenantCode,
            TenantName = request.TenantName.Trim(),
            Host = host,
            ShardKey = shardKey,
            DatabaseProvider = request.DatabaseProvider,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            Status = TenantStatus.Inactive
        };

        _catalog.Tenants.Add(tenant);
        try
        {
            await _catalog.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result<PlatformTenantDetailDto>.Conflict("Tenant identity already exists.");
        }

        string? temporaryPassword = null;
        var phase = "CatalogCreated";
        try
        {
            var shard = new ShardDescriptor(
                tenant.Id, tenant.TenantCode, tenant.Host, tenant.ShardKey, tenant.Status, tenant.DatabaseProvider);
            phase = "DatabaseProvisioning";
            await _provisioning.ProvisionAsync(shard, cancellationToken);
            phase = "InitialAdminCreation";
            temporaryPassword = await CreateInitialAdminAsync(tenant, request, adminEmail, cancellationToken);

            phase = "TenantActivation";
            await ActivateTenantCopyAsync(tenant, cancellationToken);

            tenant.Status = TenantStatus.Active;
            await _catalog.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Platform tenant onboarding completed by {ActorUserId}: {TenantId} {TenantCode} {Host} {Provider} at {UtcNow}.",
                _actor.UserId, tenant.Id, tenant.TenantCode, tenant.Host, tenant.DatabaseProvider, DateTime.UtcNow);

            return Result<PlatformTenantDetailDto>.Success(ToDetail(tenant, adminEmail, temporaryPassword), "Tenant created successfully.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                "Platform tenant onboarding failed by {ActorUserId}: {TenantId} {TenantCode} {Host} {Provider}. "
                + "Tenant remains Inactive for operator retry. ProvisioningPhase={ProvisioningPhase} ErrorCode={ErrorCode}.",
                _actor.UserId, tenant.Id, tenant.TenantCode, tenant.Host, tenant.DatabaseProvider, phase, SafeErrorCode(ex));
            return Result<PlatformTenantDetailDto>.Failure(
                ResultStatus.Conflict,
                "Tenant provisioning failed. The tenant remains Inactive for operator diagnosis and retry.");
        }
    }

    public async Task<Result<PlatformTenantDetailDto>> UpdateInactiveAsync(
        Guid id,
        UpdateInactivePlatformTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _catalog.Tenants.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (tenant is null)
            return Result<PlatformTenantDetailDto>.NotFound("Tenant was not found.");
        if (tenant.Status != TenantStatus.Inactive)
            return Result<PlatformTenantDetailDto>.Conflict("Only an Inactive tenant can be edited before retrying provisioning.");

        var host = request.Host.Trim().ToLowerInvariant();
        var shapeError = ValidateNormalizedValues(tenant.TenantCode, host, tenant.ShardKey, _environment.IsDevelopment());
        if (shapeError is not null)
            return Result<PlatformTenantDetailDto>.Invalid(shapeError.Value.Field, shapeError.Value.Message);
        if (string.IsNullOrWhiteSpace(request.TenantName))
            return Result<PlatformTenantDetailDto>.Invalid(nameof(request.TenantName), "TenantName is required.");
        if (await _catalog.Tenants.AnyAsync(x => x.Id != id && x.Host.ToLower() == host, cancellationToken))
            return Result<PlatformTenantDetailDto>.Conflict("Host is already in use.");

        tenant.Host = host;
        tenant.TenantName = request.TenantName.Trim();
        await _catalog.SaveChangesAsync(cancellationToken);
        return Result<PlatformTenantDetailDto>.Success(ToDetail(tenant), "Inactive tenant details updated.");
    }

    public async Task<Result<PlatformTenantDetailDto>> RetryProvisioningAsync(
        Guid id,
        RetryPlatformTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
            return Result<PlatformTenantDetailDto>.Invalid(
                "Initial administrator invitations are not configured outside Development. Retry is unavailable until an invitation provider is configured.");

        var tenant = await _catalog.Tenants.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (tenant is null)
            return Result<PlatformTenantDetailDto>.NotFound("Tenant was not found.");
        if (tenant.Status != TenantStatus.Inactive)
            return Result<PlatformTenantDetailDto>.Conflict("Only an Inactive tenant can be retried. Active and Suspended tenants are not eligible.");

        var adminEmail = request.InitialAdminEmail.Trim().ToLowerInvariant();
        var shapeError = ValidateNormalizedValues(tenant.TenantCode, tenant.Host, tenant.ShardKey, true);
        if (shapeError is not null)
            return Result<PlatformTenantDetailDto>.Invalid(shapeError.Value.Field, shapeError.Value.Message);
        if (tenant.DatabaseProvider is not (DatabaseProviderType.SqlServer or DatabaseProviderType.MySql))
            return Result<PlatformTenantDetailDto>.Invalid(nameof(tenant.DatabaseProvider), "DatabaseProvider is not supported.");
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName) || string.IsNullOrWhiteSpace(adminEmail))
            return Result<PlatformTenantDetailDto>.Invalid("InitialAdmin", "Initial administrator name and email are required for retry.");
        try
        {
            if (!new System.Net.Mail.MailAddress(adminEmail).Address.Equals(adminEmail, StringComparison.OrdinalIgnoreCase))
                return Result<PlatformTenantDetailDto>.Invalid(nameof(request.InitialAdminEmail), "InitialAdminEmail is invalid.");
        }
        catch (FormatException)
        {
            return Result<PlatformTenantDetailDto>.Invalid(nameof(request.InitialAdminEmail), "InitialAdminEmail is invalid.");
        }

        if (await _catalog.Tenants.AnyAsync(x => x.Id != id && x.TenantCode.ToUpper() == tenant.TenantCode, cancellationToken)
            || await _catalog.Tenants.AnyAsync(x => x.Id != id && x.Host.ToLower() == tenant.Host, cancellationToken)
            || await _catalog.Tenants.AnyAsync(x => x.Id != id && x.ShardKey.ToLower() == tenant.ShardKey, cancellationToken))
            return Result<PlatformTenantDetailDto>.Conflict("Tenant routing identity is no longer unique; operator diagnosis is required.");

        var phase = "CatalogCreated";
        try
        {
            var shard = new ShardDescriptor(
                tenant.Id, tenant.TenantCode, tenant.Host, tenant.ShardKey, tenant.Status, tenant.DatabaseProvider);
            phase = "DatabaseProvisioning";
            await _provisioning.ProvisionAsync(shard, cancellationToken);
            await _provisioning.SynchronizeTenantIdentityAsync(shard, cancellationToken);
            phase = "InitialAdminCreation";
            var temporaryPassword = await CreateInitialAdminAsync(tenant, request, adminEmail, cancellationToken);
            phase = "TenantActivation";
            await ActivateTenantCopyAsync(tenant, cancellationToken);
            tenant.Status = TenantStatus.Active;
            await _catalog.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Platform tenant retry completed by {ActorUserId}: {TenantId} {TenantCode} {Host} {Provider}.",
                _actor.UserId, tenant.Id, tenant.TenantCode, tenant.Host, tenant.DatabaseProvider);
            return Result<PlatformTenantDetailDto>.Success(
                ToDetail(tenant, adminEmail, temporaryPassword), "Tenant provisioning retry completed successfully.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                "Platform tenant retry failed by {ActorUserId}: {TenantId} {TenantCode} {Host} {Provider}. "
                + "Tenant remains Inactive. ProvisioningPhase={ProvisioningPhase} ErrorCode={ErrorCode}.",
                _actor.UserId, tenant.Id, tenant.TenantCode, tenant.Host, tenant.DatabaseProvider, phase, SafeErrorCode(ex));
            return Result<PlatformTenantDetailDto>.Failure(
                ResultStatus.Conflict,
                "Tenant provisioning retry failed. The tenant remains Inactive for operator diagnosis.");
        }
    }

    private async Task ActivateTenantCopyAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<IShardContext>().Use(new ShardDescriptor(
            tenant.Id, tenant.TenantCode, tenant.Host, tenant.ShardKey, TenantStatus.Active, tenant.DatabaseProvider));
        var db = provider.GetRequiredService<HrmsDbContext>();
        var tenantCopy = await db.Tenants.SingleAsync(x => x.Id == tenant.Id, cancellationToken);
        tenantCopy.Status = TenantStatus.Active;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> CreateInitialAdminAsync(
        Tenant tenant,
        CreatePlatformTenantRequest request,
        string adminEmail,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<IShardContext>().Use(new ShardDescriptor(
            tenant.Id, tenant.TenantCode, tenant.Host, tenant.ShardKey, tenant.Status, tenant.DatabaseProvider));
        var db = provider.GetRequiredService<HrmsDbContext>();

        var existing = await db.Users.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenant.Id && x.Email.ToLower() == adminEmail, cancellationToken);
        if (existing is not null)
        {
            var hasTenantAdminRole = existing.IsActive && await db.UserRoles.AnyAsync(
                x => x.UserId == existing.Id && x.TenantId == tenant.Id && x.RoleId == SeedData.RoleId(RoleNames.TenantAdmin),
                cancellationToken);
            if (!hasTenantAdminRole)
                throw new InvalidOperationException("An existing initial administrator is inactive or lacks the tenant administrator role.");
            return string.Empty;
        }

        var password = GenerateTemporaryPassword();
        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Email = adminEmail,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PasswordHash = _passwordHasher.Hash(password),
            IsActive = true
        };
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            TenantId = tenant.Id,
            RoleId = SeedData.RoleId(RoleNames.TenantAdmin)
        });
        await db.SaveChangesAsync(cancellationToken);
        return password;
    }

    private Task<string> CreateInitialAdminAsync(
        Tenant tenant,
        RetryPlatformTenantRequest request,
        string adminEmail,
        CancellationToken cancellationToken) =>
        CreateInitialAdminAsync(tenant, new CreatePlatformTenantRequest
        {
            TenantName = tenant.TenantName,
            TenantCode = tenant.TenantCode,
            Host = tenant.Host,
            DatabaseProvider = tenant.DatabaseProvider,
            ShardKey = tenant.ShardKey,
            Email = tenant.Email,
            Phone = tenant.Phone,
            Address = tenant.Address,
            FirstName = request.FirstName,
            LastName = request.LastName,
            InitialAdminEmail = request.InitialAdminEmail
        }, adminEmail, cancellationToken);

    private PlatformTenantListItemDto ToListItem(Tenant tenant) => new(
        tenant.Id, tenant.TenantCode, tenant.TenantName, tenant.Host, tenant.DatabaseProvider,
        tenant.ShardKey, tenant.Status, WebUrl(tenant.Host));

    private PlatformTenantDetailDto ToDetail(Tenant tenant, string? adminEmail = null, string? temporaryPassword = null) => new(
        tenant.Id, tenant.TenantCode, tenant.TenantName, tenant.Host, tenant.DatabaseProvider,
        tenant.ShardKey, tenant.Status, tenant.Email, tenant.Phone, tenant.Address, WebUrl(tenant.Host),
        adminEmail, temporaryPassword);

    private string WebUrl(string host) => (_configuration["Frontend:TenantWebUrlTemplate"] ?? "http://{host}")
        .Replace("{host}", host, StringComparison.Ordinal);

    private static string SafeErrorCode(Exception exception) => exception is TenantProvisioningException classified
        ? classified.Code
        : "TenantProvisioningFailed";

    private static (string Field, string Message)? ValidateNormalizedValues(
        string code, string host, string shardKey, bool developmentHostRequired)
    {
        if (Uri.CheckHostName(host) is not UriHostNameType.Dns)
            return ("Host", "Host must be a valid DNS host name.");
        if (host.Contains("..", StringComparison.Ordinal) || host.StartsWith('.') || host.EndsWith('.'))
            return ("Host", "Host must be a valid DNS host name.");
        if (developmentHostRequired &&
            (!host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
             || host[..^".localhost".Length].Length == 0
             || host[..^".localhost".Length].Contains('.', StringComparison.Ordinal)))
            return ("Host", "Development tenant hosts must use the supported <workspace>.localhost format.");
        if (code.Length is < 2 or > 20)
            return ("TenantCode", "TenantCode must be between 2 and 20 characters.");
        if (code.Any(character => !char.IsAsciiLetterOrDigit(character)))
            return ("TenantCode", "TenantCode must contain only letters and digits.");
        if (shardKey.Length is < 1 or > 64)
            return ("ShardKey", "ShardKey must be between 1 and 64 characters.");
        if (!char.IsAsciiLetterLower(shardKey[0]) && !char.IsAsciiDigit(shardKey[0]) ||
            shardKey.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character is not ('-' or '_')))
            return ("ShardKey", "ShardKey must start with a lowercase letter or digit and contain only lowercase letters, digits, '-' or '_'.");
        return null;
    }

    private static string GenerateTemporaryPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes).Replace('+', 'x').Replace('/', 'y').TrimEnd('=');
    }

}
