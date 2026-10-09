using HRMS.Application.Common;

namespace HRMS.Application.DTOs.Confirmation;

// --- Rating scale ---

public sealed record ConfirmationRatingScaleDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    int MinRating,
    int MaxRating,
    bool IsActive,
    int DisplayOrder,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    int ValueCount,
    DateTime CreatedDate,
    DateTime? ModifiedDate);

public sealed record ConfirmationRatingScaleValueDto(
    Guid Id,
    int RatingValue,
    string Label,
    string? Description,
    int DisplayOrder,
    bool IsActive);

public sealed record ConfirmationRatingScaleDetailDto(
    ConfirmationRatingScaleDto Scale,
    IReadOnlyList<ConfirmationRatingScaleValueDto> Values);

public sealed class ConfirmationRatingScaleQuery : PagedQuery
{
    public bool? IsActive { get; set; }

    /// <summary>Supported sort fields for this endpoint (validated against this whitelist).</summary>
    public static readonly IReadOnlyList<string> SortFields = new[] { "code", "name", "displayorder", "isactive", "createddate" };
}

public sealed record ConfirmationRatingScaleValueRequest(
    int RatingValue,
    string Label,
    string? Description = null,
    int DisplayOrder = 0,
    bool IsActive = true);

public sealed record ConfirmationRatingScaleRequest(
    string Code,
    string Name,
    string? Description,
    int MinRating,
    int MaxRating,
    bool IsActive = true,
    int DisplayOrder = 0,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    IReadOnlyList<ConfirmationRatingScaleValueRequest>? Values = null);
