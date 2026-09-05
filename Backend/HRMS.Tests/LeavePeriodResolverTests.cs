using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class LeavePeriodResolverTests
{
    [Fact]
    public async Task Resolves_one_active_period_with_inclusive_boundaries()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid();
        await SeedPeriodsAsync(db, tenant, new LeavePeriod { Id = Guid.NewGuid(), TenantId = tenant, Code = "2027", Name = "2027", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31) });
        using var context = db.CreateContext(new TestTenantContext(tenant));
        var resolver = new LeavePeriodResolver(context);

        Assert.Equal(LeavePeriodResolutionStatus.Resolved, (await resolver.ResolveAsync(tenant, new(2027, 1, 1))).Status);
        Assert.Equal(LeavePeriodResolutionStatus.Resolved, (await resolver.ResolveAsync(tenant, new(2027, 12, 31))).Status);
    }

    [Fact]
    public async Task Ignores_inactive_periods_and_reports_missing_configuration()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid();
        await SeedPeriodsAsync(db, tenant, new LeavePeriod { Id = Guid.NewGuid(), TenantId = tenant, Code = "OLD", Name = "Old", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31), IsActive = false });
        using var context = db.CreateContext(new TestTenantContext(tenant));

        var result = await new LeavePeriodResolver(context).ResolveAsync(tenant, new(2027, 6, 1));

        Assert.Equal(LeavePeriodResolutionStatus.NotConfigured, result.Status);
        Assert.Null(result.Period);
    }

    [Fact]
    public async Task Reports_ambiguity_without_using_insertion_order()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid();
        await SeedPeriodsAsync(db, tenant,
            new LeavePeriod { Id = Guid.NewGuid(), TenantId = tenant, Code = "B", Name = "B", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31) },
            new LeavePeriod { Id = Guid.NewGuid(), TenantId = tenant, Code = "A", Name = "A", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31) });
        using var context = db.CreateContext(new TestTenantContext(tenant));

        var result = await new LeavePeriodResolver(context).ResolveAsync(tenant, new(2027, 6, 1));

        Assert.Equal(LeavePeriodResolutionStatus.ConfigurationAmbiguity, result.Status);
        Assert.Null(result.Period);
    }

    [Fact]
    public async Task Does_not_resolve_another_tenants_period()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var otherTenant = Guid.NewGuid();
        await SeedPeriodsAsync(db, tenant, new LeavePeriod { Id = Guid.NewGuid(), TenantId = tenant, Code = "2027", Name = "2027", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31) });
        using var context = db.CreateContext(new TestTenantContext(otherTenant));

        var result = await new LeavePeriodResolver(context).ResolveAsync(otherTenant, new(2027, 6, 1));

        Assert.Equal(LeavePeriodResolutionStatus.NotConfigured, result.Status);
    }

    [Theory]
    [InlineData(typeof(LeavePolicyRequestRule))]
    [InlineData(typeof(LeavePolicyCalendarRule))]
    [InlineData(typeof(LeavePolicyAttachmentRule))]
    [InlineData(typeof(LeavePolicyClubbingRule))]
    [InlineData(typeof(LeavePolicyCancellationRule))]
    public void Detailed_leave_entities_have_tenant_query_filters(Type entityType)
    {
        using var db = new SqliteInMemoryDatabase();
        using var context = db.CreateContext(new TestTenantContext(Guid.NewGuid()));

        Assert.NotNull(context.Model.FindEntityType(entityType)?.GetQueryFilter());
    }

    private static async Task SeedPeriodsAsync(SqliteInMemoryDatabase db, Guid tenant, params LeavePeriod[] periods)
    {
        using var context = db.CreateContext(new TestTenantContext());
        context.Tenants.Add(new Tenant { Id = tenant, TenantCode = tenant.ToString("N")[..8], Host = tenant + ".local", ShardKey = tenant.ToString("N"), TenantName = "Test" });
        context.LeavePeriods.AddRange(periods);
        await context.SaveChangesAsync();
    }
}
