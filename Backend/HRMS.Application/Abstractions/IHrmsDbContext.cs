using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Abstractions;

/// <summary>
/// The persistence surface the Application layer is allowed to use. Application services depend on
/// this abstraction rather than on the concrete <c>HrmsDbContext</c>, which keeps business logic in the
/// Application layer while the dependency direction still points inward (Infrastructure implements it).
/// EF Core's DbSet is exposed directly and intentionally: per-entity repositories would add a layer
/// without adding capability, and LINQ against a DbSet is already a testable, provider-agnostic API.
/// <para>
/// This reaches <em>one</em> tenant's database — the request's own shard — and every tenant-scoped set on
/// it is filtered to that tenant besides. Anything that has to run before a tenant is known belongs on
/// <see cref="IHrmsCatalogDbContext"/> instead.
/// </para>
/// </summary>
public interface IHrmsDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Department> Departments { get; }
    DbSet<Designation> Designations { get; }
    DbSet<Employee> Employees { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
