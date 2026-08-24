using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotent seeding of reference data and demo tenants/users. Safe to run on every startup: each
/// step inserts only what is missing (matched on natural keys), so it never duplicates or overwrites.
/// Tenant-scoped reads use IgnoreQueryFilters() because seeding runs without a resolved tenant.
/// <para>
/// There are two entry points because there are two kinds of database. <see cref="SeedCatalogAsync"/> fills
/// the routing database and has to run first — until an organization exists in the catalog, nothing knows
/// which database <see cref="SeedShardAsync"/> should even open. <see cref="SeedShardAsync"/> then fills
/// <em>one</em> organization's database: the shared reference data, that organization's own row, and its demo
/// content and nobody else's.
/// </para>
/// <para>
/// The tenant row is passed in from the catalog rather than looked up in <see cref="SeedData"/>, so the copy
/// a tenant database carries to satisfy its foreign keys cannot drift from the catalog's authoritative one —
/// and an organization created through onboarding, which appears in no seed list, is seeded by exactly this
/// code path rather than a second one written later.
/// </para>
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// Seeds the catalog: the tenants themselves and the branding their sign-in screens show. Insert-only,
    /// like every other step here.
    /// </summary>
    public static async Task SeedCatalogAsync(HrmsCatalogDbContext catalog, CancellationToken ct = default)
    {
        await SeedCatalogTenantsAsync(catalog, ct);
        await SeedTenantBrandingAsync(catalog, ct);
    }

    /// <summary>
    /// Seeds one organization's database: the reference data every database holds, the organization's own
    /// row copied from <paramref name="tenant"/>, and the demo content belonging to that organization.
    /// <para>
    /// Every demo step filters on <paramref name="tenant"/>'s id, which is what keeps one organization's
    /// people out of another's database. It also means an organization with no seed data — every real one —
    /// gets the reference rows and its own tenant row and nothing else, with no branching to say so.
    /// </para>
    /// </summary>
    public static async Task SeedShardAsync(
        HrmsDbContext db,
        IPasswordHasher passwordHasher,
        Tenant tenant,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        await SeedPermissionsAsync(db, ct);
        await SeedRolesAsync(db, ct);
        await SeedRolePermissionsAsync(db, ct);
        await SeedTenantAsync(db, tenant, ct);
        await SeedUsersAsync(db, passwordHasher, tenant.Id, ct);
        await SeedUserRolesAsync(db, tenant.Id, ct);

        // Order matters: an employee's department, designation and manager must already exist, because the
        // composite (TenantId, …) foreign keys refuse a reference the database cannot resolve.
        await SeedDepartmentsAsync(db, tenant.Id, ct);
        await SeedDesignationsAsync(db, tenant.Id, ct);
        await SeedEmployeesAsync(db, tenant.Id, ct);
    }

    private static async Task SeedPermissionsAsync(HrmsDbContext db, CancellationToken ct)
    {
        var existing = await db.Permissions.Select(p => p.Name).ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);

        var toAdd = SeedData.Permissions.Where(p => !existingSet.Contains(p.Name)).ToList();
        if (toAdd.Count > 0)
        {
            db.Permissions.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedRolesAsync(HrmsDbContext db, CancellationToken ct)
    {
        var existing = await db.Roles.Select(r => r.Name).ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);

        var toAdd = SeedData.Roles.Where(r => !existingSet.Contains(r.Name)).ToList();
        if (toAdd.Count > 0)
        {
            db.Roles.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedRolePermissionsAsync(HrmsDbContext db, CancellationToken ct)
    {
        var existing = await db.RolePermissions
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync(ct);
        var existingSet = existing.Select(x => (x.RoleId, x.PermissionId)).ToHashSet();

        var toAdd = new List<RolePermission>();
        foreach (var (roleName, permissionNames) in SeedData.RolePermissionMap)
        {
            var roleId = SeedData.RoleId(roleName);
            foreach (var permissionName in permissionNames)
            {
                var permissionId = SeedData.PermissionId(permissionName);
                if (existingSet.Add((roleId, permissionId)))
                {
                    toAdd.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
                }
            }
        }

        if (toAdd.Count > 0)
        {
            db.RolePermissions.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Inserts the organizations the catalog is missing. The catalog is the authority for routing, so this is
    /// the row that decides a host maps to an organization at all; <see cref="SeedTenantAsync"/> copies it
    /// into that organization's own database, where it exists to satisfy foreign keys.
    /// </summary>
    private static async Task SeedCatalogTenantsAsync(HrmsCatalogDbContext catalog, CancellationToken ct)
    {
        var existingCodes = await catalog.Tenants.Select(t => t.TenantCode).ToListAsync(ct);
        var existingSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);

        var toAdd = SeedData.Tenants.Where(t => !existingSet.Contains(t.TenantCode)).ToList();

        if (toAdd.Count > 0)
        {
            catalog.Tenants.AddRange(toAdd);
            await catalog.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Copies one organization's catalog row into its own database. Matched on id rather than code, because
    /// the id is what every tenant-scoped foreign key in this database points at — a row with the right code
    /// and a different id would satisfy this check and then fail every insert that followed.
    /// </summary>
    private static async Task SeedTenantAsync(HrmsDbContext db, Tenant tenant, CancellationToken ct)
    {
        if (await db.Tenants.AnyAsync(t => t.Id == tenant.Id, ct))
        {
            return;
        }

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Inserts branding for tenants that do not have a row yet. Insert-only, like every other step here:
    /// once a tenant has branding, whatever an administrator has since changed it to is theirs, and a
    /// restart must not quietly put the seed values back.
    /// </summary>
    private static async Task SeedTenantBrandingAsync(HrmsCatalogDbContext catalog, CancellationToken ct)
    {
        // TenantId is the whole key, so the natural key and the primary key are the same thing here.
        var existingTenantIds = (await catalog.TenantBranding.Select(b => b.TenantId).ToListAsync(ct)).ToHashSet();

        var toAdd = SeedData.Branding.Where(b => !existingTenantIds.Contains(b.TenantId)).ToList();
        if (toAdd.Count > 0)
        {
            catalog.TenantBranding.AddRange(toAdd);
            await catalog.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedUsersAsync(
        HrmsDbContext db,
        IPasswordHasher passwordHasher,
        Guid tenantId,
        CancellationToken ct)
    {
        // No tenant is resolved during seeding, so bypass the tenant query filter to see all users.
        var existing = await db.Users.IgnoreQueryFilters()
            .Select(u => new { u.TenantId, u.Email })
            .ToListAsync(ct);
        var existingSet = existing
            .Select(x => (x.TenantId, Email: x.Email.ToLowerInvariant()))
            .ToHashSet();

        var toAdd = new List<User>();
        foreach (var seedUser in SeedData.Users.Where(u => u.TenantId == tenantId))
        {
            if (existingSet.Contains((seedUser.TenantId, seedUser.Email.ToLowerInvariant()))) continue;

            toAdd.Add(new User
            {
                Id = seedUser.Id,
                TenantId = seedUser.TenantId,
                Email = seedUser.Email,
                FirstName = seedUser.FirstName,
                LastName = seedUser.LastName,
                IsActive = true,
                PasswordHash = passwordHasher.Hash(SeedData.DefaultUserPassword)
            });
        }

        if (toAdd.Count > 0)
        {
            db.Users.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedUserRolesAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.UserRoles.IgnoreQueryFilters()
            .Select(ur => new { ur.UserId, ur.RoleId })
            .ToListAsync(ct);
        var existingSet = existing.Select(x => (x.UserId, x.RoleId)).ToHashSet();

        var toAdd = new List<UserRole>();
        foreach (var seedUser in SeedData.Users.Where(u => u.TenantId == tenantId))
        {
            var roleId = SeedData.RoleId(seedUser.RoleName);
            if (existingSet.Add((seedUser.Id, roleId)))
            {
                toAdd.Add(new UserRole
                {
                    UserId = seedUser.Id,
                    RoleId = roleId,
                    TenantId = seedUser.TenantId
                });
            }
        }

        if (toAdd.Count > 0)
        {
            db.UserRoles.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedDepartmentsAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.Departments.IgnoreQueryFilters()
            .Select(d => new { d.TenantId, d.Code })
            .ToListAsync(ct);
        var existingSet = existing.Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.Departments
            .Where(d => d.TenantId == tenantId)
            .Where(d => !existingSet.Contains((d.TenantId, d.Code.ToLowerInvariant())))
            .Select(d => new Department
            {
                Id = d.Id,
                TenantId = d.TenantId,
                Code = d.Code,
                Name = d.Name,
                Description = d.Description,
                IsActive = true
            })
            .ToList();

        if (toAdd.Count > 0)
        {
            db.Departments.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedDesignationsAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.Designations.IgnoreQueryFilters()
            .Select(d => new { d.TenantId, d.Code })
            .ToListAsync(ct);
        var existingSet = existing.Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.Designations
            .Where(d => d.TenantId == tenantId)
            .Where(d => !existingSet.Contains((d.TenantId, d.Code.ToLowerInvariant())))
            .Select(d => new Designation
            {
                Id = d.Id,
                TenantId = d.TenantId,
                Code = d.Code,
                Name = d.Name,
                Description = d.Description,
                IsActive = true
            })
            .ToList();

        if (toAdd.Count > 0)
        {
            db.Designations.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedEmployeesAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.Employees.IgnoreQueryFilters()
            .Select(e => new { e.TenantId, e.EmployeeCode })
            .ToListAsync(ct);
        var existingSet = existing.Select(x => (x.TenantId, Code: x.EmployeeCode.ToLowerInvariant())).ToHashSet();

        var seedEmployees = SeedData.Employees.Where(e => e.TenantId == tenantId).ToList();

        var toAdd = seedEmployees
            .Where(e => !existingSet.Contains((e.TenantId, e.EmployeeCode.ToLowerInvariant())))
            .ToList();

        if (toAdd.Count == 0)
        {
            return;
        }

        // Codes are resolved to ids within the seed row's own tenant, so a code that only exists in the
        // other demo tenant fails to resolve rather than silently pointing across the boundary.
        var departmentIds = (await db.Departments.IgnoreQueryFilters()
                .Select(d => new { d.TenantId, d.Code, d.Id })
                .ToListAsync(ct))
            .ToDictionary(d => (d.TenantId, Code: d.Code.ToLowerInvariant()), d => d.Id);

        var designationIds = (await db.Designations.IgnoreQueryFilters()
                .Select(d => new { d.TenantId, d.Code, d.Id })
                .ToListAsync(ct))
            .ToDictionary(d => (d.TenantId, Code: d.Code.ToLowerInvariant()), d => d.Id);

        var employeeIdsByCode = seedEmployees
            .ToDictionary(e => (e.TenantId, Code: e.EmployeeCode.ToLowerInvariant()), e => e.Id);

        var entities = toAdd.Select(e => new Employee
        {
            Id = e.Id,
            TenantId = e.TenantId,
            EmployeeCode = e.EmployeeCode,
            FirstName = e.FirstName,
            LastName = e.LastName,
            Email = e.Email,
            Phone = e.Phone,
            DateOfBirth = e.DateOfBirth,
            Gender = e.Gender,
            DateOfJoining = e.DateOfJoining,
            Status = EmployeeStatus.Active,
            DepartmentId = departmentIds[(e.TenantId, e.DepartmentCode.ToLowerInvariant())],
            DesignationId = designationIds[(e.TenantId, e.DesignationCode.ToLowerInvariant())],
            ReportingManagerId = e.ReportingManagerCode is null
                ? null
                : employeeIdsByCode[(e.TenantId, e.ReportingManagerCode.ToLowerInvariant())],
            Address = e.Address
        }).ToList();

        // A single save: managers are employees too, and EF orders inserts so that a row is written after
        // the row it references.
        db.Employees.AddRange(entities);
        await db.SaveChangesAsync(ct);
    }
}
