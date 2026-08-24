using HRMS.Application.Common;
using HRMS.Application.DTOs.Designations;

namespace HRMS.Application.Abstractions;

/// <summary>Designation (job title) management within the caller's own tenant.</summary>
public interface IDesignationService
{
    Task<Result<PagedResult<DesignationDto>>> GetAsync(DesignationQuery query, CancellationToken cancellationToken = default);

    Task<Result<DesignationDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<DesignationDto>> CreateAsync(DesignationRequest request, CancellationToken cancellationToken = default);

    Task<Result<DesignationDto>> UpdateAsync(Guid id, DesignationRequest request, CancellationToken cancellationToken = default);

    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
