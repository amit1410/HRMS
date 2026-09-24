using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;

namespace HRMS.Application.Abstractions;

public interface ISeparationExitService
{
    Task<Result<SeparationExitReadinessDto>> GetReadinessAsync(Guid separationId, CancellationToken ct = default);
    Task<Result<SeparationExitExecutionDto>> ExecuteAsync(Guid separationId, SeparationExitExecuteRequest request, CancellationToken ct = default);
    Task<Result<SeparationExitExecutionDto>> RetryAsync(Guid separationId, SeparationExitRetryRequest request, CancellationToken ct = default);
    Task<Result<SeparationExitExecutionDto>> GetAsync(Guid separationId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SeparationExitExecutionEventDto>>> HistoryAsync(Guid separationId, CancellationToken ct = default);
    Task<Result<PagedResult<SeparationExitDashboardItemDto>>> DashboardAsync(SeparationExitDashboardQuery query, CancellationToken ct = default);
}
