using HRMS.Application.Common;
using HRMS.Application.DTOs.PlatformTenants;

namespace HRMS.Application.Abstractions;

public interface IPlatformTenantService
{
    Task<Result<IReadOnlyList<PlatformTenantListItemDto>>> ListAsync(CancellationToken cancellationToken = default);
    Task<Result<PlatformTenantDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<PlatformTenantDetailDto>> CreateAsync(
        CreatePlatformTenantRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<PlatformTenantDetailDto>> UpdateInactiveAsync(
        Guid id,
        UpdateInactivePlatformTenantRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<PlatformTenantDetailDto>> ActivateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<PlatformTenantDetailDto>> DeactivateAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<PlatformTenantDetailDto>> RetryProvisioningAsync(
        Guid id,
        RetryPlatformTenantRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<ResetTenantAdminPasswordResponse>> ResetTenantAdminPasswordAsync(
        Guid id,
        ResetTenantAdminPasswordRequest request,
        CancellationToken cancellationToken = default);
}
