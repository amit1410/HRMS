using HRMS.Application.DTOs.PlatformTenants;
using HRMS.Application.Validators.PlatformTenants;
using HRMS.Domain.Enums;

namespace HRMS.Tests;

public sealed class PlatformTenantRequestValidatorTests
{
    [Fact]
    public void Valid_sql_server_request_passes_shape_validation()
    {
        var result = new CreatePlatformTenantRequestValidator().Validate(Valid());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Unsupported_provider_value_fails_shape_validation()
    {
        var request = Valid();
        request.DatabaseProvider = (DatabaseProviderType)99;
        var result = new CreatePlatformTenantRequestValidator().Validate(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Invalid_shard_key_fails_shape_validation()
    {
        var request = Valid();
        request.ShardKey = "Demo 03";
        var result = new CreatePlatformTenantRequestValidator().Validate(request);
        Assert.False(result.IsValid);
    }

    private static CreatePlatformTenantRequest Valid() => new()
    {
        TenantName = "Demo Company",
        TenantCode = "DEMO03",
        Host = "demo03.localhost",
        DatabaseProvider = DatabaseProviderType.SqlServer,
        ShardKey = "demo03",
        FirstName = "Demo",
        LastName = "Admin",
        InitialAdminEmail = "admin@demo03.com"
    };
}
