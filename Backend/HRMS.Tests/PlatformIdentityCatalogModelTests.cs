using HRMS.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PlatformIdentityCatalogModelTests
{
    [Fact]
    public void SqlServer_catalog_model_contains_separate_platform_tables_without_tenant_id()
    {
        using var context = new HrmsCatalogDbContext(new DbContextOptionsBuilder<HrmsCatalogDbContext>()
            .UseSqlServer("Server=not-opened;Database=not-opened;").Options);
        AssertPlatformModel(context);
    }

    [Fact]
    public void MySql_catalog_model_contains_separate_platform_tables_without_tenant_id()
    {
        using var context = new HrmsCatalogDbContext(new DbContextOptionsBuilder<HrmsCatalogDbContext>()
            .UseMySQL("Server=not-opened;Database=not-opened;").Options);
        AssertPlatformModel(context);
    }

    private static void AssertPlatformModel(HrmsCatalogDbContext context)
    {
        var model = context.Model;
        Assert.NotNull(model.FindEntityType("HRMS.Domain.Entities.PlatformUser"));
        Assert.NotNull(model.FindEntityType("HRMS.Domain.Entities.PlatformRole"));
        Assert.NotNull(model.FindEntityType("HRMS.Domain.Entities.PlatformPermission"));
        Assert.NotNull(model.FindEntityType("HRMS.Domain.Entities.PlatformRefreshToken"));
        Assert.DoesNotContain(model.FindEntityType("HRMS.Domain.Entities.PlatformUser")!.GetProperties(), p => p.Name == "TenantId");
    }
}
