using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Security;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using DomainPermissions = HRMS.Domain.Authorization.Permissions;

namespace HRMS.Tests;

public class SeedDataTests
{
    [Fact]
    public async Task Seed_populates_expected_reference_data_and_demo_tenants()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());

        Assert.Equal(RoleNames.All.Count, await context.Roles.CountAsync());
        Assert.Equal(DomainPermissions.All.Count, await context.Permissions.CountAsync());
        Assert.Equal(2, await context.Tenants.CountAsync());
        Assert.Equal(4, await context.Users.IgnoreQueryFilters().CountAsync());
        Assert.Equal(4, await context.UserRoles.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Seed_is_idempotent()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();
        await db.SeedAsync(); // second run must neither duplicate nor throw

        using var context = db.CreateContext(new TestTenantContext());

        Assert.Equal(2, await context.Tenants.CountAsync());
        Assert.Equal(4, await context.Users.IgnoreQueryFilters().CountAsync());
        Assert.Equal(RoleNames.All.Count, await context.Roles.CountAsync());
        Assert.Equal(SeedData.Departments.Count, await context.Departments.IgnoreQueryFilters().CountAsync());
        Assert.Equal(SeedData.Designations.Count, await context.Designations.IgnoreQueryFilters().CountAsync());
        Assert.Equal(SeedData.Employees.Count, await context.Employees.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Seed_populates_an_organization_structure_for_each_tenant()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());

        Assert.Equal(SeedData.Departments.Count, await context.Departments.IgnoreQueryFilters().CountAsync());
        Assert.Equal(SeedData.Designations.Count, await context.Designations.IgnoreQueryFilters().CountAsync());
        Assert.Equal(SeedData.Employees.Count, await context.Employees.IgnoreQueryFilters().CountAsync());

        // Deliberately different sizes, so a leak between the two shows up as a wrong number rather than as
        // two tenants that happen to look alike.
        Assert.Equal(3, await context.Departments.IgnoreQueryFilters()
            .CountAsync(d => d.TenantId == SeedData.TenantIds.Demo01));
        Assert.Equal(2, await context.Departments.IgnoreQueryFilters()
            .CountAsync(d => d.TenantId == SeedData.TenantIds.Demo02));
        Assert.Equal(6, await context.Employees.IgnoreQueryFilters()
            .CountAsync(e => e.TenantId == SeedData.TenantIds.Demo01));
        Assert.Equal(2, await context.Employees.IgnoreQueryFilters()
            .CountAsync(e => e.TenantId == SeedData.TenantIds.Demo02));
    }

    /// <summary>
    /// The seed is the one place that writes employees without going through the service, so it is also the
    /// one place that could quietly create a cross-tenant reference — which every later isolation test would
    /// then inherit as its starting state.
    /// </summary>
    [Fact]
    public async Task Every_seeded_employee_references_only_its_own_tenants_rows()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());
        var employees = await context.Employees
            .IgnoreQueryFilters()
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Include(e => e.ReportingManager)
            .ToListAsync();

        Assert.Equal(SeedData.Employees.Count, employees.Count);

        foreach (var employee in employees)
        {
            Assert.Equal(employee.TenantId, employee.Department!.TenantId);
            Assert.Equal(employee.TenantId, employee.Designation!.TenantId);

            if (employee.ReportingManager is not null)
            {
                Assert.Equal(employee.TenantId, employee.ReportingManager.TenantId);
            }
        }
    }

    [Fact]
    public async Task Seeded_reporting_lines_are_wired_by_employee_code()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());
        var employees = await context.Employees
            .IgnoreQueryFilters()
            .Include(e => e.ReportingManager)
            .ToDictionaryAsync(e => $"{e.TenantId}/{e.EmployeeCode}");

        var demo01 = SeedData.TenantIds.Demo01;
        var demo02 = SeedData.TenantIds.Demo02;

        Assert.Null(employees[$"{demo01}/EMP-001"].ReportingManagerId);
        Assert.Equal("EMP-001", employees[$"{demo01}/EMP-002"].ReportingManager!.EmployeeCode);
        Assert.Equal("EMP-002", employees[$"{demo01}/EMP-003"].ReportingManager!.EmployeeCode);
        Assert.Null(employees[$"{demo02}/E-100"].ReportingManagerId);
        Assert.Equal("E-100", employees[$"{demo02}/E-101"].ReportingManager!.EmployeeCode);

        // Nobody reports to somebody in another organization.
        Assert.All(employees.Values, e => Assert.True(
            e.ReportingManager is null || e.ReportingManager.TenantId == e.TenantId));
    }

    [Fact]
    public async Task Seeded_admin_belongs_to_its_tenant_and_password_verifies()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());
        var admin = await context.Users.IgnoreQueryFilters()
            .SingleAsync(u => u.Email == "admin@demo01.com");

        Assert.Equal(SeedData.TenantIds.Demo01, admin.TenantId);

        var hasher = new IdentityPasswordHasher();
        Assert.True(hasher.Verify(admin.PasswordHash, SeedData.DefaultUserPassword));
        Assert.False(hasher.Verify(admin.PasswordHash, "not-the-password"));
    }

    [Fact]
    public async Task Role_permission_grants_match_the_seed_map()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());

        var tenantAdminGrants = await context.RolePermissions
            .CountAsync(rp => rp.RoleId == SeedData.RoleId(RoleNames.TenantAdmin));
        Assert.Equal(DomainPermissions.All.Count, tenantAdminGrants);

        var employeeGrants = await context.RolePermissions
            .CountAsync(rp => rp.RoleId == SeedData.RoleId(RoleNames.Employee));
        Assert.Equal(SeedData.RolePermissionMap[RoleNames.Employee].Length, employeeGrants);
        Assert.Equal(new[] { DomainPermissions.Geography.View }, SeedData.RolePermissionMap[RoleNames.Employee]);
    }

    /// <summary>
    /// The seeded branding is read by anonymous callers, so what it holds is a disclosure decision rather
    /// than sample data. It is asserted against the catalog, which is where it lives — it deliberately left
    /// the tenant databases, since it has to be readable before one has been chosen.
    /// </summary>
    [Fact]
    public async Task Seed_gives_each_demo_organization_public_branding_with_its_own_accent()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var catalog = db.CreateCatalogContext();
        var branding = await catalog.TenantBranding.AsNoTracking().ToDictionaryAsync(b => b.TenantId);

        Assert.Equal(SeedData.Branding.Count, branding.Count);
        Assert.Equal(await catalog.Tenants.CountAsync(), branding.Count);

        var demo01 = branding[SeedData.TenantIds.Demo01];
        var demo02 = branding[SeedData.TenantIds.Demo02];

        // Opted in, or the development login screen would show the neutral response and the feature would
        // look broken rather than closed.
        Assert.True(demo01.IsPublic);
        Assert.True(demo02.IsPublic);

        // Different accents, so which organization's branding resolved is visible without reading the DOM.
        Assert.Equal("#0F766E", demo01.PrimaryColor);
        Assert.Equal("#7C3AED", demo02.PrimaryColor);

        // No logo, because a seeded URL would be a dead link or a dependency on someone else's host; and no
        // single sign-on, because no provider is implemented and advertising one would be a lie in the UI.
        Assert.All(branding.Values, b =>
        {
            Assert.Null(b.LogoUrl);
            Assert.False(b.SsoEnabled);
            Assert.Null(b.SsoProviderName);
        });
    }

    /// <summary>
    /// Branding is admin-editable, so a restart must not put the seed values back over someone's choices —
    /// including a choice to stop publishing. An unconditional upsert would silently re-open that.
    /// </summary>
    [Fact]
    public async Task Reseeding_leaves_edited_branding_alone()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using (var catalog = db.CreateCatalogContext())
        {
            var branding = await catalog.TenantBranding.SingleAsync(b => b.TenantId == SeedData.TenantIds.Demo01);
            branding.IsPublic = false;
            branding.DisplayName = "Renamed By An Administrator";
            await catalog.SaveChangesAsync();
        }

        await db.SeedAsync();

        using var verification = db.CreateCatalogContext();
        var reread = await verification.TenantBranding.AsNoTracking()
            .SingleAsync(b => b.TenantId == SeedData.TenantIds.Demo01);

        Assert.False(reread.IsPublic);
        Assert.Equal("Renamed By An Administrator", reread.DisplayName);
        Assert.Equal(SeedData.Branding.Count, await verification.TenantBranding.CountAsync());
    }

    /// <summary>
    /// Reference-row ids are hand-assigned, replicated into every tenant database, and never regenerated —
    /// so a missing or duplicated one is not a test-time inconvenience, it is data corruption spread across
    /// every customer's database by the next startup. These three tests are the guard that used to be
    /// unnecessary when the ids were list positions, and that became necessary the moment they were not:
    /// nothing else notices a role added to <see cref="RoleNames.All"/> without an id, or two names given the
    /// same number.
    /// </summary>
    [Fact]
    public void Every_role_and_permission_has_a_seeded_id()
    {
        Assert.All(RoleNames.All, name => Assert.True(SeedData.RoleId(name) > 0));
        Assert.All(DomainPermissions.All, name => Assert.True(SeedData.PermissionId(name) > 0));
    }

    [Fact]
    public void Seeded_ids_are_unique_within_roles_and_within_permissions()
    {
        var roleIds = RoleNames.All.Select(SeedData.RoleId).ToList();
        Assert.Equal(roleIds.Count, roleIds.Distinct().Count());

        var permissionIds = DomainPermissions.All.Select(SeedData.PermissionId).ToList();
        Assert.Equal(permissionIds.Count, permissionIds.Distinct().Count());
    }

    /// <summary>
    /// An unmapped name has to throw rather than fall back to zero or to a position: a grant written against
    /// id 0 would insert cleanly and grant nothing, which is the failure mode hardest to notice.
    /// </summary>
    [Fact]
    public void An_unmapped_role_or_permission_name_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SeedData.RoleId("NoSuchRole"));
        Assert.Throws<ArgumentOutOfRangeException>(() => SeedData.PermissionId("nosuch.permission"));
    }

    /// <summary>
    /// The rows that get inserted must carry the same ids the grants are written against. They come from two
    /// different code paths — the entity lists and <see cref="SeedData.RoleId"/> — and agreeing today is not
    /// the same as agreeing after an edit.
    /// </summary>
    [Fact]
    public void Seeded_rows_carry_the_ids_the_lookup_reports()
    {
        Assert.All(SeedData.Roles, role => Assert.Equal(SeedData.RoleId(role.Name), role.Id));
        Assert.All(SeedData.Permissions, p => Assert.Equal(SeedData.PermissionId(p.Name), p.Id));
    }
}
