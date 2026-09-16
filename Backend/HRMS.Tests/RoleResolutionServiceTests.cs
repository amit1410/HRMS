using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class RoleResolutionServiceTests
{
    private static readonly Guid Tenant = SeedData.TenantIds.Demo01;
    private static readonly Guid User = SeedData.Users[1].Id;
    private static readonly int Role = SeedData.RoleId(RoleNames.HRBP);
    private static readonly DateOnly Today = new(2026, 3, 4);

    [Fact]
    public async Task Active_assignment_resolves_and_effective_end_date_is_inclusive()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        Add(harness, new(2026, 1, 1), Today);

        var ids = await new RoleResolutionService(harness.CreateContext()).GetEffectiveRoleIdsAsync(Tenant, User, Today);

        Assert.Contains(Role, ids);
    }

    [Fact]
    public async Task Future_assignment_does_not_resolve_before_its_start_date()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        Add(harness, Today.AddDays(1), null);

        var ids = await new RoleResolutionService(harness.CreateContext()).GetEffectiveRoleIdsAsync(Tenant, User, Today);

        Assert.DoesNotContain(Role, ids);
    }

    [Fact]
    public async Task Expired_assignment_does_not_resolve()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        Add(harness, Today.AddDays(-10), Today.AddDays(-1));

        var ids = await new RoleResolutionService(harness.CreateContext()).GetEffectiveRoleIdsAsync(Tenant, User, Today);

        Assert.DoesNotContain(Role, ids);
    }

    [Fact]
    public async Task Legacy_default_effective_date_is_immediate_but_explicit_future_date_is_preserved()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        using (var context = harness.CreateContext())
        {
            context.UserRoles.Add(new UserRole { TenantId = Tenant, UserId = User, RoleId = Role });
            context.UserRoles.Add(new UserRole { TenantId = Tenant, UserId = User, RoleId = SeedData.RoleId(RoleNames.IT), EffectiveFrom = Today.AddDays(2) });
            await context.SaveChangesAsync();
        }

        var ids = await new RoleResolutionService(harness.CreateContext()).GetEffectiveRoleIdsAsync(Tenant, User, Today);

        Assert.Contains(Role, ids);
        Assert.DoesNotContain(SeedData.RoleId(RoleNames.IT), ids);
    }

    private static void Add(OrganizationTestHarness harness, DateOnly from, DateOnly? to)
    {
        using var context = harness.CreateContext();
        context.UserRoles.Add(new UserRole { TenantId = Tenant, UserId = User, RoleId = Role, EffectiveFrom = from, EffectiveTo = to });
        context.SaveChanges();
    }
}
