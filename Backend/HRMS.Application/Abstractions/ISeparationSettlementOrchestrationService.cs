using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;

namespace HRMS.Application.Abstractions;

public interface ISeparationSettlementOrchestrationService
{
    Task<Result<SeparationSettlementReadinessDto>> GetReadinessAsync(Guid separationId, CancellationToken ct = default);
    Task<Result<SeparationSettlementStatusDto>> GetStatusAsync(Guid separationId, CancellationToken ct = default);
    Task<Result<SeparationSettlementStatusDto>> InitiateAsync(Guid separationId, SettlementInitiationRequest request, CancellationToken ct = default);
    Task<Result<SeparationSettlementStatusDto>> RetryAsync(Guid separationId, SettlementRetryRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SeparationSettlementEventDto>>> GetHistoryAsync(Guid separationId, CancellationToken ct = default);
    Task<Result<PagedResult<SeparationSettlementDashboardItemDto>>> GetDashboardAsync(SettlementDashboardQuery query, CancellationToken ct = default);
}
