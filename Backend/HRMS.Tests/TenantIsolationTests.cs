using HRMS.Domain.Entities;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public class TenantIsolationTests
{
    [Fact]
    public async Task Query_filter_scopes_users_to_the_current_tenant()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var demo01 = db.CreateContext(new TestTenantContext(SeedData.TenantIds.Demo01));
        var demo01Emails = await demo01.Users.Select(u => u.Email).ToListAsync();
        Assert.Equal(2, demo01Emails.Count);
        Assert.All(demo01Emails, email => Assert.EndsWith("@demo01.com", email));

        using var demo02 = db.CreateContext(new TestTenantContext(SeedData.TenantIds.Demo02));
        var demo02Emails = await demo02.Users.Select(u => u.Email).ToListAsync();
        Assert.Equal(2, demo02Emails.Count);
        Assert.All(demo02Emails, email => Assert.EndsWith("@demo02.com", email));
    }

    [Fact]
    public async Task Query_filter_returns_nothing_when_no_tenant_is_resolved()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext()); // null tenant
        Assert.Empty(await context.Users.ToListAsync());
    }

    [Fact]
    public async Task SaveChanges_forces_new_rows_to_the_resolved_tenant_ignoring_client_value()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        var newUserId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");

        // Acting as Demo01, try to plant a row carrying a spoofed Demo02 TenantId.
        using (var demo01 = db.CreateContext(new TestTenantContext(SeedData.TenantIds.Demo01)))
        {
            demo01.Users.Add(new User
            {
                Id = newUserId,
                TenantId = SeedData.TenantIds.Demo02, // spoofed — must be overwritten server-side
                Email = "intruder@demo01.com",
                PasswordHash = "x",
                FirstName = "In",
                LastName = "Truder"
            });
            await demo01.SaveChangesAsync();
        }

        using var verify = db.CreateContext(new TestTenantContext());
        var stored = await verify.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == newUserId);
        Assert.Equal(SeedData.TenantIds.Demo01, stored.TenantId);
    }

    [Fact]
    public async Task One_tenant_cannot_read_rows_created_by_another_tenant()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        var demo02UserId = new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd");
        using (var demo02 = db.CreateContext(new TestTenantContext(SeedData.TenantIds.Demo02)))
        {
            demo02.Users.Add(new User
            {
                Id = demo02UserId,
                Email = "newcomer@demo02.com",
                PasswordHash = "x",
                FirstName = "New",
                LastName = "Comer"
            });
            await demo02.SaveChangesAsync();
        }

        using var demo01 = db.CreateContext(new TestTenantContext(SeedData.TenantIds.Demo01));
        Assert.False(await demo01.Users.AnyAsync(u => u.Id == demo02UserId));
    }

    [Fact]
    public async Task Update_cannot_relocate_a_row_to_another_tenant()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        // Load one of Demo01's own users (permitted), then try to move it to Demo02 via update.
        using (var demo01 = db.CreateContext(new TestTenantContext(SeedData.TenantIds.Demo01)))
        {
            var user = await demo01.Users.SingleAsync(u => u.Email == "admin@demo01.com");
            user.TenantId = SeedData.TenantIds.Demo02; // attempt to relocate — must be ignored
            await demo01.SaveChangesAsync();
        }

        using var verify = db.CreateContext(new TestTenantContext());
        var stored = await verify.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == "admin@demo01.com");
        Assert.Equal(SeedData.TenantIds.Demo01, stored.TenantId); // TenantId is immutable on update
    }
}
