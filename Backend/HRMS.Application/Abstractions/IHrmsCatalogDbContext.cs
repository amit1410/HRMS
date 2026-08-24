using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Abstractions;

/// <summary>
/// The catalog persistence surface: the shared database that maps a request's host to an organization and
/// holds the branding shown before anyone signs in.
/// <para>
/// Separate from <see cref="IHrmsDbContext"/> on purpose, and much smaller. <c>IHrmsDbContext</c> reaches
/// one tenant's own database and is protected by global query filters; this one spans every tenant and has
/// no filters at all, because it is what resolves <em>which</em> tenant a request belongs to. Depending on
/// it is therefore a statement that the code in question runs before a tenant is known — sign-in,
/// branding, provisioning — and nothing else should.
/// </para>
/// </summary>
public interface IHrmsCatalogDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<TenantBranding> TenantBranding { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
