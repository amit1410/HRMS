using HRMS.Application.Common;
using HRMS.Application.DTOs.Cities;

namespace HRMS.Application.Abstractions;

public interface ICityService
{
    Task<Result<PagedResult<CityDto>>> GetAsync(CityQuery query, CancellationToken cancellationToken = default);
    Task<Result<CityDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<CityDto>> CreateAsync(CityRequest request, CancellationToken cancellationToken = default);
    Task<Result<CityDto>> UpdateAsync(Guid id, CityRequest request, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
