using HRMS.Application.DTOs.PlatformTenants;
using HRMS.Application.Validators.PlatformTenants;

namespace HRMS.Tests;

public sealed class PlatformTenantRecoveryValidatorTests
{
    [Fact]
    public void Retry_requires_a_new_or_existing_initial_admin_identity()
    {
        var result = new RetryPlatformTenantRequestValidator().Validate(new RetryPlatformTenantRequest());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(RetryPlatformTenantRequest.InitialAdminEmail));
    }

    [Fact]
    public void Inactive_update_requires_a_host_and_name()
    {
        var result = new UpdateInactivePlatformTenantRequestValidator().Validate(new UpdateInactivePlatformTenantRequest());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateInactivePlatformTenantRequest.Host));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateInactivePlatformTenantRequest.TenantName));
    }
}
