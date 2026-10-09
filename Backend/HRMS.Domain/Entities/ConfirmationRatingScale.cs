using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// A tenant-configurable rating scale used by confirmation review <see cref="ReviewQuestionType.Rating"/>
/// questions. The numeric bounds (<see cref="MinRating"/>/<see cref="MaxRating"/>) plus the configured
/// <see cref="ConfirmationRatingScaleValue"/>s are intentionally data-driven so Phase 9c-3 decision rules
/// (e.g. "overall rating &gt;= X =&gt; Confirm") can be configured against them rather than against hard-coded
/// numbers. Every value here is per-customer; nothing about the scale is baked into code.
/// </summary>
public sealed class ConfirmationRatingScale : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Lowest selectable rating value (e.g. 1). Decision rules read this/<see cref="MaxRating"/>.</summary>
    public int MinRating { get; set; }

    /// <summary>Highest selectable rating value (e.g. 5).</summary>
    public int MaxRating { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Sort order in pickers and admin lists.</summary>
    public int DisplayOrder { get; set; }

    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }

    public Tenant? Tenant { get; set; }
    public ICollection<ConfirmationRatingScaleValue> Values { get; set; } = new List<ConfirmationRatingScaleValue>();
}

/// <summary>
/// One point on a <see cref="ConfirmationRatingScale"/> — a numeric value and its label/description
/// (e.g. 3 = "Meets Expectations"). Labels are configured per customer and must never be hard-coded.
/// </summary>
public sealed class ConfirmationRatingScaleValue : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ConfirmationRatingScaleId { get; set; }

    /// <summary>The numeric rating (e.g. 1..5). Unique within the scale.</summary>
    public int RatingValue { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ConfirmationRatingScale? ConfirmationRatingScale { get; set; }
}
