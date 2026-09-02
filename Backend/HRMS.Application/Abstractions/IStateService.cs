using HRMS.Application.Common;
using HRMS.Application.DTOs.States;

namespace HRMS.Application.Abstractions;

public interface IStateService
{
    Task<Result<PagedResult<StateDto>>> GetAsync(StateQuery query, CancellationToken cancellationToken = default);
    Task<Result<StateDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<StateDto>> CreateAsync(StateRequest request, CancellationToken cancellationToken = default);
    Task<Result<StateDto>> UpdateAsync(Guid id, StateRequest request, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
