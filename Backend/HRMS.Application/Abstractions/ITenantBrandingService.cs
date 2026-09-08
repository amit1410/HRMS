using HRMS.Application.Common;
using HRMS.Application.DTOs.Tenants;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

/// <summary>
/// Reads the branding the organization at this address has chosen to show on the sign-in screen.
/// <para>
/// Separate from <see cref="IAuthService"/> on purpose: this grants nothing and verifies nothing. It is
/// a presentation lookup that happens to run before authentication, and keeping it out of the
/// authentication service keeps that service's surface entirely about credentials.
/// </para>
/// </summary>
public interface ITenantBrandingService
{
    /// <summary>
    /// Branding for the active organization the request was addressed to. Missing custom branding is filled
    /// with product defaults; unknown, inactive, and unpublished workspaces return <c>NotFound</c>.
    /// <para>
    /// Takes no organization argument, and that is the design: the caller is anonymous and cannot be
    /// trusted to say which organization it is, so the only trustworthy answer to that question is the host
    /// the request arrived at — read from <see cref="IShardContext"/>. An argument here would be a way to
    /// ask about organizations other than the one being visited.
    /// </para>
    /// <para>
    /// </summary>
    Task<Result<TenantBrandingDto>> GetForCurrentOrganizationAsync(CancellationToken cancellationToken = default);

    Task<Result<TenantLoginIdentifierMode>> GetLoginIdentifierModeAsync(CancellationToken cancellationToken = default);

    Task<Result<TenantLoginIdentifierMode>> SetLoginIdentifierModeAsync(TenantLoginIdentifierMode mode, CancellationToken cancellationToken = default);
    Task<Result<TenantRecoverySettingsDto>> GetRecoverySettingsAsync(CancellationToken cancellationToken = default);
    Task<Result<TenantRecoverySettingsDto>> SetRecoverySettingsAsync(UpdateTenantRecoverySettingsRequest request, CancellationToken cancellationToken = default);
}
