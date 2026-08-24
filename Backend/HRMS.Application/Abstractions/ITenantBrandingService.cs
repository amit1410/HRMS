using HRMS.Application.Common;
using HRMS.Application.DTOs.Tenants;

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
    /// Branding for the organization the request was addressed to, or the neutral response when there is
    /// none to show.
    /// <para>
    /// Takes no organization argument, and that is the design: the caller is anonymous and cannot be
    /// trusted to say which organization it is, so the only trustworthy answer to that question is the host
    /// the request arrived at — read from <see cref="IShardContext"/>. An argument here would be a way to
    /// ask about organizations other than the one being visited.
    /// </para>
    /// <para>
    /// Succeeds for every request, including ones addressed to no organization at all. See the
    /// implementation for why a not-found result would be a security problem rather than a nicety.
    /// </para>
    /// </summary>
    Task<Result<TenantBrandingDto>> GetForCurrentOrganizationAsync(CancellationToken cancellationToken = default);
}
