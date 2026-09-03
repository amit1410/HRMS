using HRMS.Application.DTOs.Masters;
using HRMS.Application.Services;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class MasterManagementTests
{
    [Fact]
    public async Task Authorized_user_can_create_holding_company_and_linked_lob()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var service = new MasterManagementService(harness.CreateContext(), harness.TenantContext);

        var holding = await service.CreateAsync("holding-companies", new MasterManagementRequest
        {
            Code = "HC99", Name = "Synthetic Holdings", IsActive = true
        });
        Assert.True(holding.Succeeded, holding.Message);

        var lob = await service.CreateAsync("lines-of-business", new MasterManagementRequest
        {
            Code = "LOB99", Name = "Synthetic LOB", ParentId = holding.Value!.Id, IsActive = true
        });
        Assert.True(lob.Succeeded, lob.Message);
        Assert.Equal("HC99", lob.Value!.ParentCode);

        var listed = await service.GetAsync("lines-of-business", new MasterManagementQuery(Search: "LOB99"));
        Assert.Equal("LOB99", listed.Value!.Items.Single().Code);
    }

    [Fact]
    public async Task Master_management_rejects_cross_tenant_parent_and_preserves_inactive_records()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var service = new MasterManagementService(harness.CreateContext(), harness.TenantContext);
        var otherTenantHolding = OrganizationTestHarness.HoldingCompanyId(SeedData.TenantIds.Demo02, "HC01");

        var rejected = await service.CreateAsync("lines-of-business", new MasterManagementRequest
        {
            Code = "LOB99", Name = "Invalid LOB", ParentId = otherTenantHolding, IsActive = true
        });
        Assert.False(rejected.Succeeded);

        var updated = await service.UpdateAsync("holding-companies", OrganizationTestHarness.HoldingCompanyId(SeedData.TenantIds.Demo01, "HC01"), new MasterManagementRequest
        {
            Code = "HC01", Name = "Acme Global Holdings", IsActive = false
        });
        Assert.True(updated.Succeeded, updated.Message);
        Assert.False((await service.GetByIdAsync("holding-companies", updated.Value!.Id)).Value!.IsActive);
    }
}
