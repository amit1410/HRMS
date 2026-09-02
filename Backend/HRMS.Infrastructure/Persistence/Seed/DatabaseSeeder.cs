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
        await SeedBanksAsync(db, tenant.Id, ct);
        await SeedEmployeesAsync(db, tenant.Id, ct);
        await SeedPositionChangeReasonsAsync(db, tenant.Id, ct);
        await SeedOrganisationHierarchyAsync(db, tenant.Id, ct);

        // Global reference data — not tenant-scoped, seeded once per database.
        await SeedCountriesAsync(db, ct);
        await SeedStatesAsync(db, ct);
        await SeedCitiesAsync(db, ct);
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

    private static async Task SeedBanksAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.Banks.IgnoreQueryFilters()
            .Select(b => new { b.TenantId, b.Code })
            .ToListAsync(ct);
        var existingSet = existing.Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.Banks
            .Where(b => b.TenantId == tenantId)
            .Where(b => !existingSet.Contains((b.TenantId, b.Code.ToLowerInvariant())))
            .Select(b => new Bank
            {
                Id = b.Id,
                TenantId = b.TenantId,
                Code = b.Code,
                Name = b.Name,
                Description = b.Description,
                IsActive = b.IsActive
            })
            .ToList();

        if (toAdd.Count > 0)
        {
            db.Banks.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedEmployeesAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.Employees.IgnoreQueryFilters()
            .Select(e => new { e.TenantId, e.EmployeeCode })
            .ToListAsync(ct);
        var existingSet = existing.Select(x => (x.TenantId, Code: (x.EmployeeCode ?? string.Empty).ToLowerInvariant())).ToHashSet();

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

    private static async Task SeedPositionChangeReasonsAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = await db.PositionChangeReasons.IgnoreQueryFilters()
            .Select(r => new { r.TenantId, r.Code })
            .ToListAsync(ct);
        var existingSet = existing.Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.PositionChangeReasons
            .Where(r => r.TenantId == tenantId)
            .Where(r => !existingSet.Contains((r.TenantId, r.Code.ToLowerInvariant())))
            .Select(r => new PositionChangeReason
            {
                Id = r.Id,
                TenantId = r.TenantId,
                Code = r.Code,
                Name = r.Name,
                Description = r.Description,
                SortOrder = r.SortOrder,
                IsActive = true
            })
            .ToList();

        if (toAdd.Count > 0)
        {
            db.PositionChangeReasons.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Seeds the tenant-scoped organizational hierarchy masters (holding companies, lines of business,
    /// organisations, sub-departments, sections, sub-sections, functions, sub-functions, grades,
    /// work locations, employee types, cost centers) that the employee employment section depends on.
    /// Each is matched on (TenantId, Code) so a re-run never duplicates, mirroring
    /// <see cref="SeedBanksAsync"/>.
    /// </summary>
    private static async Task SeedOrganisationHierarchyAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        await SeedHoldingCompaniesAsync(db, tenantId, ct);
        await SeedLinesOfBusinessAsync(db, tenantId, ct);
        await SeedOrganisationsAsync(db, tenantId, ct);
        await SeedSubDepartmentsAsync(db, tenantId, ct);
        await SeedSectionsAsync(db, tenantId, ct);
        await SeedSubSectionsAsync(db, tenantId, ct);
        await SeedFunctionsAsync(db, tenantId, ct);
        await SeedSubFunctionsAsync(db, tenantId, ct);
        await SeedGradesAsync(db, tenantId, ct);
        await SeedWorkLocationsAsync(db, tenantId, ct);
        await SeedEmployeeTypesAsync(db, tenantId, ct);
        await SeedCostCentersAsync(db, tenantId, ct);
    }

    private static async Task SeedHoldingCompaniesAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = (await db.HoldingCompanies.IgnoreQueryFilters()
                .Select(h => new { h.TenantId, h.Code }).ToListAsync(ct))
            .Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.HoldingCompanies
            .Where(h => h.TenantId == tenantId)
            .Where(h => !existing.Contains((h.TenantId, h.Code.ToLowerInvariant())))
            .Select(h => new HoldingCompany
            {
                Id = h.Id, TenantId = h.TenantId, Code = h.Code, Name = h.Name,
                Description = h.Description, IsActive = h.IsActive
            })
            .ToList();

        if (toAdd.Count > 0) { db.HoldingCompanies.AddRange(toAdd); await db.SaveChangesAsync(ct); }
    }

    private static async Task SeedLinesOfBusinessAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = (await db.LinesOfBusiness.IgnoreQueryFilters()
                .Select(l => new { l.TenantId, l.Code }).ToListAsync(ct))
            .Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.LinesOfBusiness
            .Where(l => l.TenantId == tenantId)
            .Where(l => !existing.Contains((l.TenantId, l.Code.ToLowerInvariant())))
            .Select(l => new Lob
            {
                Id = l.Id, TenantId = l.TenantId, Code = l.Code, Name = l.Name,
                Description = l.Description, IsActive = l.IsActive, HoldingCompanyId = l.HoldingCompanyId
            })
            .ToList();

        if (toAdd.Count > 0) { db.LinesOfBusiness.AddRange(toAdd); await db.SaveChangesAsync(ct); }
    }

    private static async Task SeedOrganisationsAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = (await db.Organisations.IgnoreQueryFilters()
                .Select(o => new { o.TenantId, o.Code }).ToListAsync(ct))
            .Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.Organisations
            .Where(o => o.TenantId == tenantId)
            .Where(o => !existing.Contains((o.TenantId, o.Code.ToLowerInvariant())))
            .Select(o => new Organisation
            {
                Id = o.Id, TenantId = o.TenantId, Code = o.Code, Name = o.Name,
                Description = o.Description, IsActive = o.IsActive
            })
            .ToList();

        if (toAdd.Count > 0) { db.Organisations.AddRange(toAdd); await db.SaveChangesAsync(ct); }
    }

    private static async Task SeedSubDepartmentsAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = (await db.SubDepartments.IgnoreQueryFilters()
                .Select(s => new { s.TenantId, s.Code }).ToListAsync(ct))
            .Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.SubDepartments
            .Where(s => s.TenantId == tenantId)
            .Where(s => !existing.Contains((s.TenantId, s.Code.ToLowerInvariant())))
            .Select(s => new SubDepartment
            {
                Id = s.Id, TenantId = s.TenantId, Code = s.Code, Name = s.Name,
                Description = s.Description, IsActive = s.IsActive, DepartmentId = s.DepartmentId
            })
            .ToList();

        if (toAdd.Count > 0) { db.SubDepartments.AddRange(toAdd); await db.SaveChangesAsync(ct); }
    }

    private static async Task SeedSectionsAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = (await db.Sections.IgnoreQueryFilters()
                .Select(s => new { s.TenantId, s.Code }).ToListAsync(ct))
            .Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.Sections
            .Where(s => s.TenantId == tenantId)
            .Where(s => !existing.Contains((s.TenantId, s.Code.ToLowerInvariant())))
            .Select(s => new Section
            {
                Id = s.Id, TenantId = s.TenantId, Code = s.Code, Name = s.Name,
                Description = s.Description, IsActive = s.IsActive, SubDepartmentId = s.SubDepartmentId
            })
            .ToList();

        if (toAdd.Count > 0) { db.Sections.AddRange(toAdd); await db.SaveChangesAsync(ct); }
    }

    private static async Task SeedSubSectionsAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = (await db.SubSections.IgnoreQueryFilters()
                .Select(s => new { s.TenantId, s.Code }).ToListAsync(ct))
            .Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.SubSections
            .Where(s => s.TenantId == tenantId)
            .Where(s => !existing.Contains((s.TenantId, s.Code.ToLowerInvariant())))
            .Select(s => new SubSection
            {
                Id = s.Id, TenantId = s.TenantId, Code = s.Code, Name = s.Name,
                Description = s.Description, IsActive = s.IsActive, SectionId = s.SectionId
            })
            .ToList();

        if (toAdd.Count > 0) { db.SubSections.AddRange(toAdd); await db.SaveChangesAsync(ct); }
    }

    private static async Task SeedFunctionsAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = (await db.Functions.IgnoreQueryFilters()
                .Select(f => new { f.TenantId, f.Code }).ToListAsync(ct))
            .Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.Functions
            .Where(f => f.TenantId == tenantId)
            .Where(f => !existing.Contains((f.TenantId, f.Code.ToLowerInvariant())))
            .Select(f => new Function
            {
                Id = f.Id, TenantId = f.TenantId, Code = f.Code, Name = f.Name,
                Description = f.Description, IsActive = f.IsActive
            })
            .ToList();

        if (toAdd.Count > 0) { db.Functions.AddRange(toAdd); await db.SaveChangesAsync(ct); }
    }

    private static async Task SeedSubFunctionsAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = (await db.SubFunctions.IgnoreQueryFilters()
                .Select(s => new { s.TenantId, s.Code }).ToListAsync(ct))
            .Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.SubFunctions
            .Where(s => s.TenantId == tenantId)
            .Where(s => !existing.Contains((s.TenantId, s.Code.ToLowerInvariant())))
            .Select(s => new SubFunction
            {
                Id = s.Id, TenantId = s.TenantId, Code = s.Code, Name = s.Name,
                Description = s.Description, IsActive = s.IsActive, FunctionId = s.FunctionId
            })
            .ToList();

        if (toAdd.Count > 0) { db.SubFunctions.AddRange(toAdd); await db.SaveChangesAsync(ct); }
    }

    private static async Task SeedGradesAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = (await db.Grades.IgnoreQueryFilters()
                .Select(g => new { g.TenantId, g.Code }).ToListAsync(ct))
            .Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.Grades
            .Where(g => g.TenantId == tenantId)
            .Where(g => !existing.Contains((g.TenantId, g.Code.ToLowerInvariant())))
            .Select(g => new Grade
            {
                Id = g.Id, TenantId = g.TenantId, Code = g.Code, Name = g.Name,
                Description = g.Description, IsActive = g.IsActive, SortOrder = g.SortOrder
            })
            .ToList();

        if (toAdd.Count > 0) { db.Grades.AddRange(toAdd); await db.SaveChangesAsync(ct); }
    }

    private static async Task SeedWorkLocationsAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = (await db.WorkLocations.IgnoreQueryFilters()
                .Select(w => new { w.TenantId, w.Code }).ToListAsync(ct))
            .Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.WorkLocations
            .Where(w => w.TenantId == tenantId)
            .Where(w => !existing.Contains((w.TenantId, w.Code.ToLowerInvariant())))
            .Select(w => new WorkLocation
            {
                Id = w.Id, TenantId = w.TenantId, Code = w.Code, Name = w.Name,
                Description = w.Description, IsActive = w.IsActive
            })
            .ToList();

        if (toAdd.Count > 0) { db.WorkLocations.AddRange(toAdd); await db.SaveChangesAsync(ct); }
    }

    private static async Task SeedEmployeeTypesAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = (await db.EmployeeTypes.IgnoreQueryFilters()
                .Select(e => new { e.TenantId, e.Code }).ToListAsync(ct))
            .Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.EmployeeTypes
            .Where(e => e.TenantId == tenantId)
            .Where(e => !existing.Contains((e.TenantId, e.Code.ToLowerInvariant())))
            .Select(e => new EmployeeType
            {
                Id = e.Id, TenantId = e.TenantId, Code = e.Code, Name = e.Name,
                Description = e.Description, IsActive = e.IsActive, SortOrder = e.SortOrder
            })
            .ToList();

        if (toAdd.Count > 0) { db.EmployeeTypes.AddRange(toAdd); await db.SaveChangesAsync(ct); }
    }

    private static async Task SeedCostCentersAsync(HrmsDbContext db, Guid tenantId, CancellationToken ct)
    {
        var existing = (await db.CostCenters.IgnoreQueryFilters()
                .Select(c => new { c.TenantId, c.Code }).ToListAsync(ct))
            .Select(x => (x.TenantId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.CostCenters
            .Where(c => c.TenantId == tenantId)
            .Where(c => !existing.Contains((c.TenantId, c.Code.ToLowerInvariant())))
            .Select(c => new CostCenter
            {
                Id = c.Id, TenantId = c.TenantId, Code = c.Code, Name = c.Name,
                Description = c.Description, IsActive = c.IsActive
            })
            .ToList();

        if (toAdd.Count > 0) { db.CostCenters.AddRange(toAdd); await db.SaveChangesAsync(ct); }
    }

    /// <summary>
    /// Seeds global reference countries. Not tenant-scoped — one copy per database.
    /// </summary>
    private static async Task SeedCountriesAsync(HrmsDbContext db, CancellationToken ct)
    {
        var existing = await db.Countries.Select(c => c.Code).ToListAsync(ct);
        var existingSet = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var toAdd = SeedData.Countries
            .Where(c => !existingSet.Contains(c.Code))
            .Select(c => new Country
            {
                Id = c.Id,
                Code = c.Code,
                Name = c.Name,
                IsActive = true
            })
            .ToList();

        if (toAdd.Count > 0)
        {
            db.Countries.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Seeds global reference states. Not tenant-scoped — one copy per database.
    /// </summary>
    private static async Task SeedStatesAsync(HrmsDbContext db, CancellationToken ct)
    {
        var existing = await db.States
            .Select(s => new { s.CountryId, s.Code })
            .ToListAsync(ct);
        var existingSet = existing.Select(x => (x.CountryId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.States
            .Where(s => !existingSet.Contains((s.CountryId, s.Code.ToLowerInvariant())))
            .Select(s => new State
            {
                Id = s.Id,
                CountryId = s.CountryId,
                Code = s.Code,
                Name = s.Name,
                IsActive = true
            })
            .ToList();

        if (toAdd.Count > 0)
        {
            db.States.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Seeds global reference cities. Not tenant-scoped — one copy per database.
    /// </summary>
    private static async Task SeedCitiesAsync(HrmsDbContext db, CancellationToken ct)
    {
        var existing = await db.Cities
            .Select(c => new { c.StateId, c.Code })
            .ToListAsync(ct);
        var existingSet = existing.Select(x => (x.StateId, Code: x.Code.ToLowerInvariant())).ToHashSet();

        var toAdd = SeedData.Cities
            .Where(c => !existingSet.Contains((c.StateId, c.Code.ToLowerInvariant())))
            .Select(c => new City
            {
                Id = c.Id,
                StateId = c.StateId,
                Code = c.Code,
                Name = c.Name,
                IsActive = true
            })
            .ToList();

        if (toAdd.Count > 0)
        {
            db.Cities.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }
}
