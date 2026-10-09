using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Tenant-configurable confirmation rating scales. Every read and write goes through the tenant-filtered
/// DbContext sets, so cross-tenant rows are simply absent and no method trusts a caller-supplied tenant id.
/// A scale owns its ordered values; an update replaces the value set atomically.
/// </summary>
public sealed class ConfirmationRatingScaleService : IConfirmationRatingScaleService
{
    private const string NoTenant = "No authenticated tenant.";
    private const string ScaleNotFound = "Rating scale not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;

    public ConfirmationRatingScaleService(IHrmsDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Result<PagedResult<ConfirmationRatingScaleDto>>> GetScalesAsync(ConfirmationRatingScaleQuery query, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<PagedResult<ConfirmationRatingScaleDto>>.Unauthorized(NoTenant);

        var source = _db.ConfirmationRatingScales.AsNoTracking();
        if (query.IsActive.HasValue) source = source.Where(x => x.IsActive == query.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLowerInvariant();
            source = source.Where(x => x.Code.ToLower().Contains(s) || x.Name.ToLower().Contains(s));
        }

        var ordered = ApplySort(source, query);
        var page = await ordered.Select(x => new ConfirmationRatingScaleDto(
                x.Id, x.Code, x.Name, x.Description, x.MinRating, x.MaxRating, x.IsActive, x.DisplayOrder,
                x.EffectiveFrom, x.EffectiveTo, x.Values.Count, x.CreatedDate, x.ModifiedDate))
            .ToPagedResultAsync(query, ct);
        return Result<PagedResult<ConfirmationRatingScaleDto>>.Success(page);
    }

    public async Task<Result<ConfirmationRatingScaleDetailDto>> GetScaleAsync(Guid scaleId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<ConfirmationRatingScaleDetailDto>.Unauthorized(NoTenant);
        var scale = await _db.ConfirmationRatingScales.AsNoTracking().FirstOrDefaultAsync(x => x.Id == scaleId, ct);
        if (scale is null) return Result<ConfirmationRatingScaleDetailDto>.NotFound(ScaleNotFound);
        return Result<ConfirmationRatingScaleDetailDto>.Success(await BuildDetailAsync(scale, ct));
    }

    public async Task<Result<ConfirmationRatingScaleDetailDto>> CreateScaleAsync(ConfirmationRatingScaleRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<ConfirmationRatingScaleDetailDto>.Unauthorized(NoTenant);
        var validation = ValidateScale(request);
        if (validation is not null) return validation;

        var code = request.Code.Trim();
        if (await _db.ConfirmationRatingScales.AnyAsync(x => x.Code.ToLower() == code.ToLower(), ct))
            return Conflict(code);

        var scale = new ConfirmationRatingScale
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = request.Name.Trim(),
            Description = Normalize(request.Description), MinRating = request.MinRating, MaxRating = request.MaxRating,
            IsActive = request.IsActive, DisplayOrder = request.DisplayOrder, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo
        };
        _db.ConfirmationRatingScales.Add(scale);
        foreach (var value in BuildValues(tenantId, scale.Id, request.Values)) _db.ConfirmationRatingScaleValues.Add(value);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(code); }

        return Result<ConfirmationRatingScaleDetailDto>.Success(await BuildDetailAsync(scale, ct), "Rating scale created.");
    }

    public async Task<Result<ConfirmationRatingScaleDetailDto>> UpdateScaleAsync(Guid scaleId, ConfirmationRatingScaleRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<ConfirmationRatingScaleDetailDto>.Unauthorized(NoTenant);
        var validation = ValidateScale(request);
        if (validation is not null) return validation;

        var scale = await _db.ConfirmationRatingScales.FirstOrDefaultAsync(x => x.Id == scaleId, ct);
        if (scale is null) return Result<ConfirmationRatingScaleDetailDto>.NotFound(ScaleNotFound);

        var code = request.Code.Trim();
        if (await _db.ConfirmationRatingScales.AnyAsync(x => x.Id != scaleId && x.Code.ToLower() == code.ToLower(), ct))
            return Conflict(code);

        scale.Code = code; scale.Name = request.Name.Trim(); scale.Description = Normalize(request.Description);
        scale.MinRating = request.MinRating; scale.MaxRating = request.MaxRating; scale.IsActive = request.IsActive;
        scale.DisplayOrder = request.DisplayOrder; scale.EffectiveFrom = request.EffectiveFrom; scale.EffectiveTo = request.EffectiveTo;

        var existing = await _db.ConfirmationRatingScaleValues.Where(x => x.ConfirmationRatingScaleId == scaleId).ToListAsync(ct);
        _db.ConfirmationRatingScaleValues.RemoveRange(existing);
        foreach (var value in BuildValues(tenantId, scaleId, request.Values)) _db.ConfirmationRatingScaleValues.Add(value);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(code); }

        return await GetScaleAsync(scaleId, ct);
    }

    // --- helpers ---

    private async Task<ConfirmationRatingScaleDetailDto> BuildDetailAsync(ConfirmationRatingScale scale, CancellationToken ct)
    {
        var values = await _db.ConfirmationRatingScaleValues.AsNoTracking()
            .Where(x => x.ConfirmationRatingScaleId == scale.Id)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.RatingValue)
            .Select(x => new ConfirmationRatingScaleValueDto(x.Id, x.RatingValue, x.Label, x.Description, x.DisplayOrder, x.IsActive))
            .ToListAsync(ct);
        var dto = new ConfirmationRatingScaleDto(scale.Id, scale.Code, scale.Name, scale.Description, scale.MinRating, scale.MaxRating,
            scale.IsActive, scale.DisplayOrder, scale.EffectiveFrom, scale.EffectiveTo, values.Count, scale.CreatedDate, scale.ModifiedDate);
        return new ConfirmationRatingScaleDetailDto(dto, values);
    }

    private static Result<ConfirmationRatingScaleDetailDto>? ValidateScale(ConfirmationRatingScaleRequest request)
    {
        var errors = new List<ValidationError>();
        if (request.MaxRating <= request.MinRating) errors.Add(new("maxRating", "Maximum rating must be greater than the minimum rating."));
        if (request.EffectiveTo is DateOnly to && request.EffectiveFrom is DateOnly from && to < from)
            errors.Add(new("effectiveTo", "Effective-to cannot be before effective-from."));

        var values = request.Values ?? Array.Empty<ConfirmationRatingScaleValueRequest>();
        var seen = new HashSet<int>();
        for (var i = 0; i < values.Count; i++)
        {
            var v = values[i];
            if (string.IsNullOrWhiteSpace(v.Label)) errors.Add(new($"values[{i}].label", "Rating label is required."));
            if (v.RatingValue < request.MinRating || v.RatingValue > request.MaxRating)
                errors.Add(new($"values[{i}].ratingValue", $"Rating value must be between {request.MinRating} and {request.MaxRating}."));
            if (!seen.Add(v.RatingValue))
                errors.Add(new($"values[{i}].ratingValue", "Duplicate rating value in this scale."));
        }
        return errors.Count == 0 ? null : Result<ConfirmationRatingScaleDetailDto>.Invalid("One or more fields are invalid.", errors);
    }

    private static Result<ConfirmationRatingScaleDetailDto> Conflict(string code) =>
        Result<ConfirmationRatingScaleDetailDto>.Conflict($"A rating scale with code '{code}' already exists.", [new ValidationError("code", "This code is already in use.")]);

    private static IEnumerable<ConfirmationRatingScaleValue> BuildValues(Guid tenantId, Guid scaleId, IReadOnlyList<ConfirmationRatingScaleValueRequest>? values)
    {
        if (values is null) yield break;
        foreach (var v in values)
            yield return new ConfirmationRatingScaleValue
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationRatingScaleId = scaleId,
                RatingValue = v.RatingValue, Label = v.Label.Trim(), Description = Normalize(v.Description),
                DisplayOrder = v.DisplayOrder, IsActive = v.IsActive
            };
    }

    private static IQueryable<ConfirmationRatingScale> ApplySort(IQueryable<ConfirmationRatingScale> source, ConfirmationRatingScaleQuery query)
    {
        var desc = query.SortDescending;
        var ordered = query.SortBy?.Trim().ToLowerInvariant() switch
        {
            "name" => desc ? source.OrderByDescending(x => x.Name) : source.OrderBy(x => x.Name),
            "displayorder" => desc ? source.OrderByDescending(x => x.DisplayOrder) : source.OrderBy(x => x.DisplayOrder),
            "isactive" => desc ? source.OrderByDescending(x => x.IsActive) : source.OrderBy(x => x.IsActive),
            "createddate" => desc ? source.OrderByDescending(x => x.CreatedDate) : source.OrderBy(x => x.CreatedDate),
            _ => desc ? source.OrderByDescending(x => x.Code) : source.OrderBy(x => x.Code)
        };
        return ordered.ThenBy(x => x.Id);
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
