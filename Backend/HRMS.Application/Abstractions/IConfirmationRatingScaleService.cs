using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;

namespace HRMS.Application.Abstractions;

/// <summary>
/// CRUD for tenant-configurable confirmation rating scales and their values. Mirrors the confirmation/leave
/// configuration service conventions (Result/PagedResult, tenant-scoped via the DbContext filter). Scales
/// are referenced by <see cref="ReviewQuestionType.Rating"/> review questions and are foundation only — no
/// decision processing happens here.
/// </summary>
public interface IConfirmationRatingScaleService
{
    Task<Result<PagedResult<ConfirmationRatingScaleDto>>> GetScalesAsync(ConfirmationRatingScaleQuery query, CancellationToken ct = default);
    Task<Result<ConfirmationRatingScaleDetailDto>> GetScaleAsync(Guid scaleId, CancellationToken ct = default);
    Task<Result<ConfirmationRatingScaleDetailDto>> CreateScaleAsync(ConfirmationRatingScaleRequest request, CancellationToken ct = default);
    Task<Result<ConfirmationRatingScaleDetailDto>> UpdateScaleAsync(Guid scaleId, ConfirmationRatingScaleRequest request, CancellationToken ct = default);
}
