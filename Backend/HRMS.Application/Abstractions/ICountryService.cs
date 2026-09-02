using HRMS.Application.Common;
using HRMS.Application.DTOs.Countries;

namespace HRMS.Application.Abstractions;

public interface ICountryService
{
    Task<Result<PagedResult<CountryDto>>> GetAsync(CountryQuery query, CancellationToken cancellationToken = default);
    Task<Result<CountryDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<CountryDto>> CreateAsync(CountryRequest request, CancellationToken cancellationToken = default);
    Task<Result<CountryDto>> UpdateAsync(Guid id, CountryRequest request, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
