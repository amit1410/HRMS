using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;

namespace HRMS.Application.Abstractions;

public interface IClearanceService
{
    Task<Result<ClearanceTemplateDto>> CreateTemplateAsync(ClearanceTemplateRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ClearanceTemplateDto>>> GetTemplatesAsync(CancellationToken ct = default);
    Task<Result<SeparationClearanceDto>> StartAsync(Guid separationId, CancellationToken ct = default);
    Task<Result<SeparationClearanceDto>> GetAsync(Guid clearanceId, CancellationToken ct = default);
    Task<Result<SeparationClearanceDto>> GetForSeparationAsync(Guid separationId, CancellationToken ct = default);
    Task<Result<SeparationClearanceDto>> ClearTaskAsync(Guid taskId, ClearanceTaskActionRequest request, CancellationToken ct = default);
    Task<Result<SeparationClearanceDto>> BlockTaskAsync(Guid taskId, ClearanceTaskActionRequest request, CancellationToken ct = default);
    Task<Result<SeparationClearanceDto>> WaiveTaskAsync(Guid taskId, ClearanceTaskActionRequest request, CancellationToken ct = default);
    Task<Result<SeparationClearanceDto>> CompleteAsync(Guid clearanceId, CancellationToken ct = default);
    Task<Result<SeparationClearanceDto>> ReopenAsync(Guid clearanceId, string reason, CancellationToken ct = default);
    Task<Result<SeparationClearanceTaskDto>> AddAssetReturnAsync(Guid taskId, SeparationAssetReturnRequest request, CancellationToken ct = default);
    Task<Result<SeparationClearanceTaskDto>> UpdateAssetReturnAsync(Guid taskId, Guid assetId, SeparationAssetReturnRequest request, CancellationToken ct = default);
    Task<Result<PagedResult<ClearanceInboxItemDto>>> GetManagerInboxAsync(ClearanceInboxQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<ClearanceInboxItemDto>>> GetFunctionalInboxAsync(ClearanceInboxQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<ClearanceDashboardItemDto>>> GetHrDashboardAsync(ClearanceInboxQuery query, CancellationToken ct = default);
}
