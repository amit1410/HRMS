using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.PlatformTenants;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Catalog;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Infrastructure.Security;
using HRMS.Infrastructure.Sharding;
using HRMS.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PlatformTenantPasswordResetTests
{
    [Fact]
    public async Task Development_with_one_admin_resets_hash_and_returns_one_time_password()
    {
        using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.ResetTenantAdminPasswordAsync(fixture.Tenant.Id, new());

        Assert.True(result.Succeeded);
        var response = Assert.IsType<ResetTenantAdminPasswordResponse>(result.Value);
        Assert.Equal(fixture.AdminEmail, response.AdminEmail);
        Assert.NotEqual(response.TemporaryPassword, fixture.OriginalPassword);
        Assert.DoesNotContain(response.TemporaryPassword, response.Message);

        using var db = fixture.Database.CreateContext(new TestTenantContext());
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == fixture.AdminEmail);
        Assert.NotEqual(response.TemporaryPassword, user.PasswordHash);
        Assert.True(fixture.Hasher.Verify(user.PasswordHash, response.TemporaryPassword));
    }

    [Fact]
    public async Task Inactive_and_unknown_tenants_are_rejected()
    {
        using var fixture = await Fixture.CreateAsync(TenantStatus.Inactive);

        var inactive = await fixture.Service.ResetTenantAdminPasswordAsync(fixture.Tenant.Id, new());
        var unknown = await fixture.Service.ResetTenantAdminPasswordAsync(Guid.NewGuid(), new());

        Assert.Equal(ResultStatus.Conflict, inactive.Status);
        Assert.Equal(ResultStatus.NotFound, unknown.Status);
    }

    [Fact]
    public async Task No_admin_and_multiple_admins_fail_closed_until_an_email_is_selected()
    {
        using (var noAdmin = await Fixture.CreateAsync())
        {
            noAdmin.RemoveAdmin();
            var result = await noAdmin.Service.ResetTenantAdminPasswordAsync(noAdmin.Tenant.Id, new());
            Assert.Equal(ResultStatus.Conflict, result.Status);
        }

        using var multiple = await Fixture.CreateAsync();
        multiple.AddAdmin("second@example.test");
        var ambiguous = await multiple.Service.ResetTenantAdminPasswordAsync(multiple.Tenant.Id, new());
        var selected = await multiple.Service.ResetTenantAdminPasswordAsync(
            multiple.Tenant.Id, new ResetTenantAdminPasswordRequest { AdminEmail = "second@example.test" });

        Assert.Equal(ResultStatus.Conflict, ambiguous.Status);
        Assert.True(selected.Succeeded);
        Assert.Equal("second@example.test", selected.Value!.AdminEmail);
    }

    [Fact]
    public async Task Non_admin_cannot_be_selected_and_production_is_rejected()
    {
        using var fixture = await Fixture.CreateAsync();
        fixture.AddNonAdmin("employee@example.test");

        var nonAdmin = await fixture.Service.ResetTenantAdminPasswordAsync(
            fixture.Tenant.Id, new ResetTenantAdminPasswordRequest { AdminEmail = "employee@example.test" });
        Assert.Equal(ResultStatus.NotFound, nonAdmin.Status);

        using var production = await Fixture.CreateAsync(environment: Environments.Production);
        var rejected = await production.Service.ResetTenantAdminPasswordAsync(production.Tenant.Id, new());
        Assert.Equal(ResultStatus.Forbidden, rejected.Status);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly HrmsCatalogDbContext _catalog;

        private Fixture(
            SqliteInMemoryDatabase database,
            HrmsCatalogDbContext catalog,
            ServiceProvider provider,
            PlatformTenantService service,
            Tenant tenant,
            IdentityPasswordHasher hasher,
            string adminEmail,
            string originalPassword)
        {
            Database = database;
            _catalog = catalog;
            _provider = provider;
            Service = service;
            Tenant = tenant;
            Hasher = hasher;
            AdminEmail = adminEmail;
            OriginalPassword = originalPassword;
        }

        public SqliteInMemoryDatabase Database { get; }
        public PlatformTenantService Service { get; }
        public Tenant Tenant { get; }
        public IdentityPasswordHasher Hasher { get; }
        public string AdminEmail { get; }
        public string OriginalPassword { get; }

        public static async Task<Fixture> CreateAsync(
            TenantStatus status = TenantStatus.Active,
            string environment = "Development")
        {
            var database = new SqliteInMemoryDatabase();
            var tenant = new Tenant
            {
                Id = Guid.NewGuid(), TenantCode = "RESET01", TenantName = "Reset Test",
                Host = "reset01.localhost", ShardKey = "reset01", Status = status,
                DatabaseProvider = DatabaseProviderType.MySql
            };
            var catalog = database.CreateCatalogContext();
            catalog.Tenants.Add(tenant);
            await catalog.SaveChangesAsync();

            var hasher = new IdentityPasswordHasher();
            using (var db = database.CreateContext(new TestTenantContext()))
            {
                await DatabaseSeeder.SeedShardAsync(db, hasher, tenant, CancellationToken.None);
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(), TenantId = tenant.Id, Email = "admin@example.test",
                    FirstName = "Tenant", LastName = "Admin", IsActive = true,
                    PasswordHash = hasher.Hash("Original-password-1")
                });
                await db.SaveChangesAsync();
                var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.Email == "admin@example.test");
                db.UserRoles.Add(new UserRole { UserId = user.Id, TenantId = tenant.Id, RoleId = SeedData.RoleId(RoleNames.TenantAdmin) });
                await db.SaveChangesAsync();
            }

            var services = new ServiceCollection();
            services.AddScoped<IShardContext, ShardContext>();
            services.AddScoped(_ => database.CreateContext(new TestTenantContext()));
            var provider = services.BuildServiceProvider();
            var service = new PlatformTenantService(
                catalog,
                new NoopProvisioning(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                hasher,
                new TestHostEnvironment(environment),
                new ConfigurationBuilder().AddInMemoryCollection().Build(),
                new TestPlatformContext(),
                NullLogger<PlatformTenantService>.Instance);

            return new Fixture(database, catalog, provider, service, tenant, hasher, "admin@example.test", "Original-password-1");
        }

        public void AddAdmin(string email)
        {
            using var db = Database.CreateContext(new TestTenantContext());
            var user = new User { Id = Guid.NewGuid(), TenantId = Tenant.Id, Email = email, FirstName = "Second", LastName = "Admin", PasswordHash = Hasher.Hash("Original-password-2"), IsActive = true };
            db.Users.Add(user);
            db.UserRoles.Add(new UserRole { UserId = user.Id, TenantId = Tenant.Id, RoleId = SeedData.RoleId(RoleNames.TenantAdmin) });
            db.SaveChanges();
        }

        public void AddNonAdmin(string email)
        {
            using var db = Database.CreateContext(new TestTenantContext());
            db.Users.Add(new User { Id = Guid.NewGuid(), TenantId = Tenant.Id, Email = email, FirstName = "Employee", LastName = "User", PasswordHash = Hasher.Hash("Original-password-3"), IsActive = true });
            db.SaveChanges();
        }

        public void RemoveAdmin()
        {
            using var db = Database.CreateContext(new TestTenantContext());
            var user = db.Users.IgnoreQueryFilters().Single(x => x.Email == AdminEmail);
            db.UserRoles.RemoveRange(db.UserRoles.IgnoreQueryFilters().Where(x => x.UserId == user.Id));
            db.Users.Remove(user);
            db.SaveChanges();
        }

        public void Dispose()
        {
            _provider.Dispose();
            _catalog.Dispose();
            Database.Dispose();
        }
    }

    private sealed class NoopProvisioning : ITenantProvisioningService
    {
        public Task ProvisionAsync(ShardDescriptor shard, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SynchronizeTenantIdentityAsync(ShardDescriptor shard, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestPlatformContext : IPlatformContext
    {
        public Guid? UserId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public int? SecurityRevision => 1;
        public bool HasSecurityRevision => true;
    }

    private sealed class TestHostEnvironment(string environment) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environment;
        public string ApplicationName { get; set; } = "HRMS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
