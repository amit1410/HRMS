using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public class UniqueConstraintTests
{
    [Fact]
    public async Task Duplicate_tenant_code_is_rejected()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync(); // seeds a tenant with code DEMO01

        using var context = db.CreateContext(new TestTenantContext());
        context.Tenants.Add(new Tenant
        {
            Id = new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            TenantCode = "DEMO01", // collides with the seeded tenant
            TenantName = "Duplicate Organization",
            Status = TenantStatus.Active
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Duplicate_email_within_the_same_tenant_is_rejected()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        // Null tenant so the explicit TenantIds below are honoured (no server stamping).
        using var context = db.CreateContext(new TestTenantContext());
        context.Users.Add(NewUser(SeedData.TenantIds.Demo01, "clash@demo01.com"));
        context.Users.Add(NewUser(SeedData.TenantIds.Demo01, "clash@demo01.com"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Same_email_in_different_tenants_is_allowed()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());
        context.Users.Add(NewUser(SeedData.TenantIds.Demo01, "shared@example.com"));
        context.Users.Add(NewUser(SeedData.TenantIds.Demo02, "shared@example.com"));

        var affected = await context.SaveChangesAsync();
        Assert.Equal(2, affected);
    }

    private static User NewUser(Guid tenantId, string email) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        Email = email,
        PasswordHash = "x",
        FirstName = "Test",
        LastName = "User"
    };
}
