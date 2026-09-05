using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests.TestSupport;

/// <summary>Creates synthetic link data after the run has created and migrated its owned databases.</summary>
public sealed class SqlServerPhase3BFixture : IAsyncLifetime
{
    private readonly System.Collections.Concurrent.ConcurrentBag<HrmsDbContext> _serviceContexts = [];
    public static readonly Guid TenantA = new("a5000000-0000-0000-0000-000000000001");
    public static readonly Guid TenantB = new("b5000000-0000-0000-0000-000000000001");
    public static readonly Guid ActorA = new("a5000000-0000-0000-0000-000000000101");
    public static readonly Guid ActorB = new("a5000000-0000-0000-0000-000000000102");
    public static readonly Guid SubjectA = new("a5000000-0000-0000-0000-000000000201");
    public static readonly Guid SubjectB = new("a5000000-0000-0000-0000-000000000202");
    public static readonly Guid EmployeeA = new("a5000000-0000-0000-0000-000000000301");
    public static readonly Guid EmployeeB = new("a5000000-0000-0000-0000-000000000302");
    public static readonly Guid BrowserViewOnly = new("a5000000-0000-0000-0000-000000000401");
    public static readonly Guid BrowserHistory = new("a5000000-0000-0000-0000-000000000402");
    public static readonly Guid BrowserManageOnly = new("a5000000-0000-0000-0000-000000000403");
    public const int BrowserViewOnlyRole = 901;
    public const int BrowserHistoryRole = 902;
    public const int BrowserManageOnlyRole = 903;
    public const string TenantADisplayName = "Phase 3B Tenant A";
    public const string TenantBDisplayName = "Phase 3B Tenant B";

    public SqlServerAcceptanceRun? Run { get; private set; }
    public string SyntheticPassword => $"P3B-{Run?.RunId ?? "uninitialized"}-LocalOnly!";

    public async Task InitializeAsync()
    {
        Run = SqlServerAcceptanceRun.FromEnvironment();
        if (Run is null) return;
        await Run.CreateDatabasesAsync();
        await Run.MigrateAsync();
        await SeedAsync(0, TenantA, "tenant-a.localhost");
        await SeedAsync(1, TenantB, "tenant-b.localhost");
    }

    public async Task DisposeAsync()
    {
        foreach (var context in _serviceContexts)
            await context.DisposeAsync();
        SqlConnection.ClearAllPools();
        if (Run is not null)
            await Run.DropDatabasesAsync();
    }

    public SqlServerAcceptanceRun RequireRun() =>
        Run ?? throw SkipException.ForSkip("SQL Server acceptance tests not executed: HRMS_SQLSERVER_TEST_SERVER is absent.");

    public AccountEmployeeLinkService Service(int database, Guid tenantId, Guid actorId)
    {
        var context = RequireRun().CreateTenantContext(database, new TestTenantContext(tenantId, actorId));
        _serviceContexts.Add(context);
        return new(context, new TestTenantContext(tenantId, actorId));
    }

    public async Task<(Guid UserId, Guid EmployeeId)> AddPairAsync(int database, Guid tenantId, string suffix)
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await using var db = RequireRun().CreateTenantContext(database, new TestTenantContext(tenantId));
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"{userId:N}@{suffix}.test", FirstName = "Synthetic", LastName = suffix, PasswordHash = "synthetic" });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, Email = $"{employeeId:N}@{suffix}.test", FirstName = "Synthetic", LastName = suffix, DateOfJoining = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), Status = EmployeeStatus.Active });
        await db.SaveChangesAsync();
        return (userId, employeeId);
    }

    private async Task SeedAsync(int database, Guid tenantId, string host)
    {
        await using var catalog = RequireRun().CreateCatalogContext();
        if (!await catalog.Tenants.AnyAsync(x => x.Id == tenantId))
            catalog.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"P3B{database}", Host = host, ShardKey = database == 0 ? "tenanta" : "tenantb", TenantName = $"Phase 3B {database}", Status = TenantStatus.Active });
        await catalog.SaveChangesAsync();
        await EnsureSyntheticBrandingAsync(catalog, tenantId, database);

        await using var db = RequireRun().CreateTenantContext(database, new TestTenantContext());
        var passwordHasher = new HRMS.Infrastructure.Security.IdentityPasswordHasher();
        await DatabaseSeeder.SeedShardAsync(db, new HRMS.Infrastructure.Security.IdentityPasswordHasher(), new Tenant
        {
            Id = tenantId, TenantCode = $"P3B{database}", Host = host, ShardKey = database == 0 ? "tenanta" : "tenantb", TenantName = $"Phase 3B {database}", Status = TenantStatus.Active
        });
        var roleId = SeedData.RoleId(RoleNames.AccountLinkAdministrator);
        db.RolePermissions.AddRange(
            new RolePermission { RoleId = roleId, PermissionId = SeedData.PermissionId(Permissions.AccountEmployeeLink.View) },
            new RolePermission { RoleId = roleId, PermissionId = SeedData.PermissionId(Permissions.AccountEmployeeLink.ViewHistory) },
            new RolePermission { RoleId = roleId, PermissionId = SeedData.PermissionId(Permissions.AccountEmployeeLink.Manage) });
        db.UserRoles.AddRange(
            new UserRole { UserId = ActorA, RoleId = roleId, TenantId = tenantId },
            new UserRole { UserId = ActorB, RoleId = roleId, TenantId = tenantId },
            new UserRole { UserId = BrowserViewOnly, RoleId = BrowserViewOnlyRole, TenantId = tenantId },
            new UserRole { UserId = BrowserHistory, RoleId = BrowserHistoryRole, TenantId = tenantId },
            new UserRole { UserId = BrowserManageOnly, RoleId = BrowserManageOnlyRole, TenantId = tenantId });
        db.Roles.AddRange(
            new Role { Id = BrowserViewOnlyRole, Name = "Phase3BViewOnly", Description = "Synthetic browser View user" },
            new Role { Id = BrowserHistoryRole, Name = "Phase3BHistory", Description = "Synthetic browser history user" },
            new Role { Id = BrowserManageOnlyRole, Name = "Phase3BManageOnly", Description = "Synthetic browser manage-only user" });
        db.Users.AddRange(
            new User { Id = ActorA, TenantId = tenantId, Email = $"actor-a@p3b{database}.test", FirstName = "Actor", LastName = "A", PasswordHash = passwordHasher.Hash(SyntheticPassword) },
            new User { Id = ActorB, TenantId = tenantId, Email = $"actor-b@p3b{database}.test", FirstName = "Actor", LastName = "B", PasswordHash = passwordHasher.Hash(SyntheticPassword) },
            new User { Id = SubjectA, TenantId = tenantId, Email = $"subject-a@p3b{database}.test", FirstName = "Subject", LastName = "A", PasswordHash = passwordHasher.Hash(SyntheticPassword) },
            new User { Id = SubjectB, TenantId = tenantId, Email = $"subject-b@p3b{database}.test", FirstName = "Subject", LastName = "B", PasswordHash = passwordHasher.Hash(SyntheticPassword) },
            new User { Id = BrowserViewOnly, TenantId = tenantId, Email = $"view-only@p3b{database}.test", FirstName = "Browser", LastName = "View", PasswordHash = passwordHasher.Hash(SyntheticPassword) },
            new User { Id = BrowserHistory, TenantId = tenantId, Email = $"history@p3b{database}.test", FirstName = "Browser", LastName = "History", PasswordHash = passwordHasher.Hash(SyntheticPassword) },
            new User { Id = BrowserManageOnly, TenantId = tenantId, Email = $"manage-only@p3b{database}.test", FirstName = "Browser", LastName = "Manage", PasswordHash = passwordHasher.Hash(SyntheticPassword) });
        db.RolePermissions.AddRange(
            new RolePermission { RoleId = BrowserViewOnlyRole, PermissionId = SeedData.PermissionId(Permissions.AccountEmployeeLink.View) },
            new RolePermission { RoleId = BrowserHistoryRole, PermissionId = SeedData.PermissionId(Permissions.AccountEmployeeLink.View) },
            new RolePermission { RoleId = BrowserHistoryRole, PermissionId = SeedData.PermissionId(Permissions.AccountEmployeeLink.ViewHistory) },
            new RolePermission { RoleId = BrowserManageOnlyRole, PermissionId = SeedData.PermissionId(Permissions.AccountEmployeeLink.Manage) });
        db.Employees.AddRange(
            new Employee { Id = EmployeeA, TenantId = tenantId, Email = $"employee-a@p3b{database}.test", FirstName = "Future", LastName = "A", DateOfJoining = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), Status = EmployeeStatus.Active },
            new Employee { Id = EmployeeB, TenantId = tenantId, Email = $"employee-b@p3b{database}.test", FirstName = "Future", LastName = "B", DateOfJoining = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), Status = EmployeeStatus.Active });
        await db.SaveChangesAsync();
    }

    private static async Task EnsureSyntheticBrandingAsync(
        HrmsCatalogDbContext catalog,
        Guid tenantId,
        int database)
    {
        var expected = new TenantBranding
        {
            TenantId = tenantId,
            IsPublic = true,
            DisplayName = database == 0 ? TenantADisplayName : TenantBDisplayName,
            PrimaryColor = database == 0 ? "#0F766E" : "#7C3AED",
            WelcomeMessage = database == 0
                ? "Sign in to the Phase 3B Tenant A workspace."
                : "Sign in to the Phase 3B Tenant B workspace.",
            SupportEmail = database == 0 ? "support-a@phase3b.test" : "support-b@phase3b.test",
            SsoEnabled = false
        };

        var existing = await catalog.TenantBranding
            .SingleOrDefaultAsync(x => x.TenantId == tenantId);
        if (existing is null)
        {
            catalog.TenantBranding.Add(expected);
            await catalog.SaveChangesAsync();
            return;
        }

        if (existing.IsPublic != expected.IsPublic ||
            existing.DisplayName != expected.DisplayName ||
            existing.PrimaryColor != expected.PrimaryColor ||
            existing.WelcomeMessage != expected.WelcomeMessage ||
            existing.SupportEmail != expected.SupportEmail ||
            existing.SsoEnabled != expected.SsoEnabled)
        {
            throw new InvalidOperationException(
                $"Synthetic branding for tenant {tenantId} does not match the acceptance fixture and will not be overwritten.");
        }
    }
}
