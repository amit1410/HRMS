using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using DomainPermissions = HRMS.Domain.Authorization.Permissions;

namespace HRMS.Infrastructure.Persistence.Seed;

/// <summary>A user to be seeded, plus the role it should hold. Password is applied by the seeder.</summary>
public record SeedUser(Guid Id, Guid TenantId, string Email, string FirstName, string LastName, string RoleName);

/// <summary>An organizational unit to be seeded.</summary>
public record SeedDepartment(Guid Id, Guid TenantId, string Code, string Name, string Description);

/// <summary>A job title to be seeded.</summary>
public record SeedDesignation(Guid Id, Guid TenantId, string Code, string Name, string Description);

/// <summary>
/// An employee to be seeded. Department, designation and manager are given as <em>codes</em> rather than
/// ids: the seed list stays readable, and the seeder resolves each code within the same tenant, so a
/// cross-tenant reference cannot be written by a typo in this file.
/// </summary>
public record SeedEmployee(
    Guid Id,
    Guid TenantId,
    string EmployeeCode,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    DateOnly DateOfBirth,
    Gender Gender,
    DateOnly DateOfJoining,
    string DepartmentCode,
    string DesignationCode,
    string? ReportingManagerCode,
    string Address);

/// <summary>
/// Canonical development seed graph: roles, permissions, role→permission grants, two demo tenants,
/// and sample users for each. All ids are fixed so seeding is deterministic and idempotent, which is
/// what lets us reliably demonstrate that tenant A cannot see tenant B's data.
/// </summary>
public static class SeedData
{
    /// <summary>Shared development password for every seeded user. Development-only — never ship real credentials.</summary>
    public const string DefaultUserPassword = "Passw0rd!";

    public static class TenantIds
    {
        public static readonly Guid Demo01 = new("11111111-1111-1111-1111-111111111111");
        public static readonly Guid Demo02 = new("22222222-2222-2222-2222-222222222222");
    }

    /// <summary>
    /// Role ids, written out rather than computed from a position in <see cref="RoleNames.All"/>.
    /// <para>
    /// These are <c>ValueGeneratedNever</c> columns, and the same reference rows are seeded into every
    /// tenant database — so an id here is a value shared across databases that may have been seeded by
    /// different builds. Deriving it from a list position meant that reordering that list, a change with no
    /// other consequence anywhere, renumbered every row after the one that moved, while
    /// <see cref="RolePermissionMap"/> went on writing grants under the new numbering. A tenant database
    /// seeded before the reorder would then hold one role's id against another role's permissions, and
    /// nothing in the system would report it: the ids all still resolve.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> RoleIds =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [RoleNames.SuperAdmin] = 1,
            [RoleNames.TenantAdmin] = 2,
            [RoleNames.HRAdmin] = 3,
            [RoleNames.HRManager] = 4,
            [RoleNames.Manager] = 5,
            [RoleNames.Employee] = 6
        };

    /// <summary>Permission ids. Fixed for the same reasons as <see cref="RoleIds"/>.</summary>
    private static readonly IReadOnlyDictionary<string, int> PermissionIds =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [DomainPermissions.Employee.View] = 1,
            [DomainPermissions.Employee.Create] = 2,
            [DomainPermissions.Employee.Edit] = 3,
            [DomainPermissions.Employee.Delete] = 4,
            [DomainPermissions.Employee.Export] = 5,
            [DomainPermissions.Department.View] = 6,
            [DomainPermissions.Department.Create] = 7,
            [DomainPermissions.Department.Edit] = 8,
            [DomainPermissions.Department.Delete] = 9,
            [DomainPermissions.Designation.View] = 10,
            [DomainPermissions.Designation.Create] = 11,
            [DomainPermissions.Designation.Edit] = 12,
            [DomainPermissions.Designation.Delete] = 13,
            [DomainPermissions.User.View] = 14,
            [DomainPermissions.User.Create] = 15,
            [DomainPermissions.User.Edit] = 16,
            [DomainPermissions.User.Delete] = 17
        };

    /// <summary>The fixed id for a role. Throws for a role that has not been given one.</summary>
    public static int RoleId(string name) =>
        RoleIds.TryGetValue(name, out var id)
            ? id
            : throw new ArgumentOutOfRangeException(
                nameof(name),
                $"Role '{name}' has no seeded id. Add it to {nameof(SeedData)}.{nameof(RoleIds)} with the "
                + "next unused number; never reuse or renumber an existing one.");

    /// <summary>The fixed id for a permission. Throws for a permission that has not been given one.</summary>
    public static int PermissionId(string name) =>
        PermissionIds.TryGetValue(name, out var id)
            ? id
            : throw new ArgumentOutOfRangeException(
                nameof(name),
                $"Permission '{name}' has no seeded id. Add it to {nameof(SeedData)}.{nameof(PermissionIds)} "
                + "with the next unused number; never reuse or renumber an existing one.");

    private static readonly IReadOnlyDictionary<string, string> RoleDescriptions = new Dictionary<string, string>
    {
        [RoleNames.SuperAdmin] = "Platform super administrator with full access across all tenants.",
        [RoleNames.TenantAdmin] = "Administrator with full access within their own tenant.",
        [RoleNames.HRAdmin] = "HR administrator managing employees, departments and designations.",
        [RoleNames.HRManager] = "HR manager with employee management capabilities.",
        [RoleNames.Manager] = "Manager with read access to organizational data.",
        [RoleNames.Employee] = "Standard employee with self-service access."
    };

    public static IReadOnlyList<Role> Roles =>
        RoleNames.All.Select(name => new Role
        {
            Id = RoleId(name),
            Name = name,
            Description = RoleDescriptions[name]
        }).ToList();

    public static IReadOnlyList<Permission> Permissions =>
        DomainPermissions.All.Select(name => new Permission
        {
            Id = PermissionId(name),
            Name = name,
            Description = name.Replace('.', ' ')
        }).ToList();

    /// <summary>Maps each role to the permission names it is granted. Employee has none by default.</summary>
    public static IReadOnlyDictionary<string, string[]> RolePermissionMap => new Dictionary<string, string[]>
    {
        [RoleNames.SuperAdmin] = DomainPermissions.All.ToArray(),
        [RoleNames.TenantAdmin] = DomainPermissions.All.ToArray(),
        [RoleNames.HRAdmin] = new[]
        {
            DomainPermissions.Employee.View, DomainPermissions.Employee.Create, DomainPermissions.Employee.Edit,
            DomainPermissions.Employee.Delete, DomainPermissions.Employee.Export,
            DomainPermissions.Department.View, DomainPermissions.Department.Create, DomainPermissions.Department.Edit,
            DomainPermissions.Department.Delete,
            DomainPermissions.Designation.View, DomainPermissions.Designation.Create, DomainPermissions.Designation.Edit,
            DomainPermissions.Designation.Delete,
            DomainPermissions.User.View, DomainPermissions.User.Create, DomainPermissions.User.Edit
        },
        [RoleNames.HRManager] = new[]
        {
            DomainPermissions.Employee.View, DomainPermissions.Employee.Create, DomainPermissions.Employee.Edit,
            DomainPermissions.Employee.Export,
            DomainPermissions.Department.View, DomainPermissions.Designation.View
        },
        [RoleNames.Manager] = new[]
        {
            DomainPermissions.Employee.View, DomainPermissions.Department.View, DomainPermissions.Designation.View
        },
        [RoleNames.Employee] = Array.Empty<string>()
    };

    /// <summary>
    /// The demo organizations, as the catalog knows them.
    /// <para>
    /// The hosts are <c>*.localhost</c> because every label under <c>localhost</c> resolves to the loopback
    /// address without any hosts-file entry on Windows, macOS and Linux — so
    /// <c>http://demo01.localhost:5173</c> works on a fresh clone, and whitelabel routing can be exercised
    /// in development the same way it will run in production instead of being stubbed out.
    /// </para>
    /// <para>
    /// <see cref="Tenant.ShardKey"/> matches the lowercased code here, which is a convenience of the demo
    /// data and not a rule: the key is looked up in configuration to find a connection string, so a real
    /// tenant can be moved to another database by changing its key without touching its code or host.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Tenant> Tenants => new List<Tenant>
    {
        new()
        {
            Id = TenantIds.Demo01,
            TenantCode = "DEMO01",
            Host = "demo01.localhost",
            ShardKey = "demo01",
            TenantName = "Demo Organization",
            Email = "contact@demo01.com",
            Phone = "+1-555-0100",
            Address = "100 Demo Street",
            Status = TenantStatus.Active
        },
        new()
        {
            Id = TenantIds.Demo02,
            TenantCode = "DEMO02",
            Host = "demo02.localhost",
            ShardKey = "demo02",
            TenantName = "Sample Organization",
            Email = "contact@demo02.com",
            Phone = "+1-555-0200",
            Address = "200 Sample Avenue",
            Status = TenantStatus.Active
        }
    };

    /// <summary>
    /// Sign-in branding for the demo tenants.
    /// <para>
    /// Both opt in via <see cref="TenantBranding.IsPublic"/> so the tenant-aware login screen has
    /// something real to show in development, and their accent colours are deliberately unlike each
    /// other <em>and</em> unlike the product accent — opening one organization's address and then the
    /// other visibly re-themes the page, which is the fastest way to see that branding follows the host
    /// rather than being hard-coded.
    /// </para>
    /// <para>
    /// <see cref="TenantBranding.LogoUrl"/> is null on purpose. A seeded URL would either point at an
    /// asset that does not exist or make development depend on a third-party host, so the client draws a
    /// monogram from the display name instead. <see cref="TenantBranding.SsoEnabled"/> is false because
    /// no identity provider is implemented; setting it true would advertise a route that goes nowhere.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TenantBranding> Branding => new List<TenantBranding>
    {
        new()
        {
            TenantId = TenantIds.Demo01,
            IsPublic = true,
            DisplayName = "Demo Organization",
            LogoUrl = null,
            PrimaryColor = "#0F766E",
            WelcomeMessage = "Sign in to the Demo Organization workspace.",
            SupportEmail = "itsupport@demo01.com",
            SsoEnabled = false,
            SsoProviderName = null
        },
        new()
        {
            TenantId = TenantIds.Demo02,
            IsPublic = true,
            DisplayName = "Sample Organization",
            LogoUrl = null,
            PrimaryColor = "#7C3AED",
            WelcomeMessage = "Sign in to the Sample Organization workspace.",
            SupportEmail = "itsupport@demo02.com",
            SsoEnabled = false,
            SsoProviderName = null
        }
    };

    public static IReadOnlyList<SeedUser> Users => new List<SeedUser>
    {
        new(new("a1111111-1111-1111-1111-111111111111"), TenantIds.Demo01, "admin@demo01.com", "Alice", "Admin", RoleNames.TenantAdmin),
        new(new("a2222222-2222-2222-2222-222222222222"), TenantIds.Demo01, "hr@demo01.com", "Henry", "Human", RoleNames.HRManager),
        new(new("b1111111-1111-1111-1111-111111111111"), TenantIds.Demo02, "admin@demo02.com", "Bob", "Admin", RoleNames.TenantAdmin),
        new(new("b2222222-2222-2222-2222-222222222222"), TenantIds.Demo02, "hr@demo02.com", "Hana", "Resource", RoleNames.HRManager)
    };

    /// <summary>
    /// Departments per tenant. The leading digit of each id encodes the tenant (1… = DEMO01, 2… = DEMO02),
    /// which makes a cross-tenant mistake visible at a glance in test output and log lines.
    /// </summary>
    public static IReadOnlyList<SeedDepartment> Departments => new List<SeedDepartment>
    {
        new(new("10000000-0000-0000-0000-000000000001"), TenantIds.Demo01, "ENG", "Engineering", "Product engineering and platform."),
        new(new("10000000-0000-0000-0000-000000000002"), TenantIds.Demo01, "HR", "Human Resources", "People operations and recruitment."),
        new(new("10000000-0000-0000-0000-000000000003"), TenantIds.Demo01, "FIN", "Finance", "Accounting, payroll and reporting."),
        new(new("20000000-0000-0000-0000-000000000001"), TenantIds.Demo02, "OPS", "Operations", "Service delivery and logistics."),
        new(new("20000000-0000-0000-0000-000000000002"), TenantIds.Demo02, "SLS", "Sales", "New business and account management.")
    };

    public static IReadOnlyList<SeedDesignation> Designations => new List<SeedDesignation>
    {
        new(new("11000000-0000-0000-0000-000000000001"), TenantIds.Demo01, "CTO", "Chief Technology Officer", "Leads the technology organization."),
        new(new("11000000-0000-0000-0000-000000000002"), TenantIds.Demo01, "EM", "Engineering Manager", "Leads an engineering team."),
        new(new("11000000-0000-0000-0000-000000000003"), TenantIds.Demo01, "SSE", "Senior Software Engineer", "Senior individual contributor."),
        new(new("11000000-0000-0000-0000-000000000004"), TenantIds.Demo01, "SE", "Software Engineer", "Individual contributor."),
        new(new("11000000-0000-0000-0000-000000000005"), TenantIds.Demo01, "HRM", "HR Manager", "Leads people operations."),
        new(new("11000000-0000-0000-0000-000000000006"), TenantIds.Demo01, "ACC", "Accountant", "Bookkeeping and reporting."),
        new(new("21000000-0000-0000-0000-000000000001"), TenantIds.Demo02, "OPSM", "Operations Manager", "Leads service delivery."),
        new(new("21000000-0000-0000-0000-000000000002"), TenantIds.Demo02, "SR", "Sales Representative", "Field sales.")
    };

    /// <summary>
    /// Demo employees, including a small reporting hierarchy so the manager relationship (and the loop
    /// check that guards it) has something real to run against. The two tenants deliberately have
    /// different sizes and unrelated names, which makes a leak between them obvious rather than subtle.
    /// </summary>
    public static IReadOnlyList<SeedEmployee> Employees => new List<SeedEmployee>
    {
        new(new("12000000-0000-0000-0000-000000000001"), TenantIds.Demo01, "EMP-001", "Nadia", "Farrell",
            "nadia.farrell@demo01.com", "555-0101", new DateOnly(1980, 4, 12), Gender.Female,
            new DateOnly(2015, 1, 5), "ENG", "CTO", null, "12 Bridge Road"),
        new(new("12000000-0000-0000-0000-000000000002"), TenantIds.Demo01, "EMP-002", "Owen", "Brand",
            "owen.brand@demo01.com", "555-0102", new DateOnly(1986, 9, 30), Gender.Male,
            new DateOnly(2017, 3, 20), "ENG", "EM", "EMP-001", "8 Mill Lane"),
        new(new("12000000-0000-0000-0000-000000000003"), TenantIds.Demo01, "EMP-003", "Priya", "Raman",
            "priya.raman@demo01.com", "555-0103", new DateOnly(1991, 2, 18), Gender.Female,
            new DateOnly(2019, 7, 1), "ENG", "SSE", "EMP-002", "44 Orchard Street"),
        new(new("12000000-0000-0000-0000-000000000004"), TenantIds.Demo01, "EMP-004", "Diego", "Santos",
            "diego.santos@demo01.com", "555-0104", new DateOnly(1995, 11, 7), Gender.Male,
            new DateOnly(2022, 2, 14), "ENG", "SE", "EMP-002", "3 Harbour View"),
        new(new("12000000-0000-0000-0000-000000000005"), TenantIds.Demo01, "EMP-005", "Mira", "Kovac",
            "mira.kovac@demo01.com", "555-0105", new DateOnly(1988, 6, 25), Gender.Female,
            new DateOnly(2018, 5, 2), "HR", "HRM", "EMP-001", "77 Elm Avenue"),
        new(new("12000000-0000-0000-0000-000000000006"), TenantIds.Demo01, "EMP-006", "Tomas", "Lind",
            "tomas.lind@demo01.com", "555-0106", new DateOnly(1983, 8, 9), Gender.Male,
            new DateOnly(2016, 10, 17), "FIN", "ACC", "EMP-001", "5 Castle Terrace"),
        new(new("22000000-0000-0000-0000-000000000001"), TenantIds.Demo02, "E-100", "Grace", "Okoro",
            "grace.okoro@demo02.com", "555-0201", new DateOnly(1984, 12, 1), Gender.Female,
            new DateOnly(2016, 4, 11), "OPS", "OPSM", null, "21 Sample Way"),
        new(new("22000000-0000-0000-0000-000000000002"), TenantIds.Demo02, "E-101", "Liam", "Hayes",
            "liam.hayes@demo02.com", "555-0202", new DateOnly(1993, 5, 22), Gender.Male,
            new DateOnly(2021, 9, 6), "SLS", "SR", "E-100", "9 Sample Close")
    };
}
