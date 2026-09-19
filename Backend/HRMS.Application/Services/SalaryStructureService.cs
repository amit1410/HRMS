using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class SalaryStructureService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock) : ISalaryStructureService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<PagedResult<SalaryStructureDto>>> GetAsync(SalaryStructureQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PagedResult<SalaryStructureDto>>.Unauthorized("No authenticated tenant.");
        if (!ValidPage(query)) return Result<PagedResult<SalaryStructureDto>>.Invalid("page", "Page values are out of range.");

        var source = db.SalaryStructures.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            source = source.Where(x => x.Code.ToLower().Contains(search) || x.Name.ToLower().Contains(search));
        }
        if (query.IsActive is { } active) source = source.Where(x => x.IsActive == active);
        var total = await source.CountAsync(ct);
        var page = await source.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);

        var effectiveOn = query.EffectiveOn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var ids = page.Select(x => x.Id).ToList();
        var versions = await db.SalaryStructureVersions.AsNoTracking()
            .Include(x => x.Components).ThenInclude(x => x.SalaryComponent)
            .Where(x => x.TenantId == tenantId && ids.Contains(x.SalaryStructureId))
            .ToListAsync(ct);
        var items = page.Select(x => ToDto(x, SelectVersion(versions.Where(v => v.SalaryStructureId == x.Id), effectiveOn))).ToList();
        return Result<PagedResult<SalaryStructureDto>>.Success(new PagedResult<SalaryStructureDto>(items, query.Page, query.PageSize, total));
    }

    public async Task<Result<SalaryStructureDto>> GetByIdAsync(Guid id, DateOnly? effectiveOn = null, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<SalaryStructureDto>.Unauthorized("No authenticated tenant.");
        var structure = await db.SalaryStructures.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (structure is null) return Result<SalaryStructureDto>.NotFound("Salary structure not found.");
        var versions = await db.SalaryStructureVersions.AsNoTracking().Include(x => x.Components).ThenInclude(x => x.SalaryComponent)
            .Where(x => x.TenantId == tenantId && x.SalaryStructureId == id).ToListAsync(ct);
        return Result<SalaryStructureDto>.Success(ToDto(structure, SelectVersion(versions, effectiveOn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))));
    }

    public async Task<Result<SalaryStructureDto>> CreateAsync(SalaryStructureRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<SalaryStructureDto>.Unauthorized("No authenticated tenant.");
        var headerError = ValidateHeader(request);
        if (headerError is not null) return Result<SalaryStructureDto>.Failure(headerError.Value.status, headerError.Value.message, headerError.Value.errors);
        var code = NormalizeCode(request.Code);
        if (await db.SalaryStructures.AnyAsync(x => x.TenantId == tenantId && x.Code == code, ct)) return Result<SalaryStructureDto>.Conflict("A salary structure with this code already exists in this tenant.");
        var validation = await ValidateComponentsAsync(request.Components, request.EffectiveFrom, request.EffectiveTo, tenantId, ct);
        if (validation is not null) return Result<SalaryStructureDto>.Failure(validation.Value.status, validation.Value.message, validation.Value.errors);

        var structure = new SalaryStructure { Id = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = request.Name.Trim(), Description = Normalize(request.Description), IsActive = request.IsActive };
        var version = BuildVersion(structure, request);
        structure.Versions.Add(version);
        AddHistory(structure, version, SalaryStructureChangeType.Created);
        db.SalaryStructures.Add(structure);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Result<SalaryStructureDto>.Conflict("A salary structure with this code already exists or the configuration is invalid."); }
        return Result<SalaryStructureDto>.Success(ToDto(structure, version), "Salary structure created.");
    }

    public async Task<Result<SalaryStructureDto>> UpdateAsync(Guid id, SalaryStructureRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<SalaryStructureDto>.Unauthorized("No authenticated tenant.");
        var headerError = ValidateHeader(request);
        if (headerError is not null) return Result<SalaryStructureDto>.Failure(headerError.Value.status, headerError.Value.message, headerError.Value.errors);
        var structure = await db.SalaryStructures.Include(x => x.Versions).ThenInclude(x => x.Components).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (structure is null) return Result<SalaryStructureDto>.NotFound("Salary structure not found.");
        if (request.ExpectedConcurrencyVersion is { } expected && expected != structure.ConcurrencyVersion) return Result<SalaryStructureDto>.Conflict("The salary structure was changed by another user.");
        var code = NormalizeCode(request.Code);
        if (await db.SalaryStructures.AnyAsync(x => x.TenantId == tenantId && x.Id != id && x.Code == code, ct)) return Result<SalaryStructureDto>.Conflict("A salary structure with this code already exists in this tenant.");
        var validation = await ValidateComponentsAsync(request.Components, request.EffectiveFrom, request.EffectiveTo, tenantId, ct);
        if (validation is not null) return Result<SalaryStructureDto>.Failure(validation.Value.status, validation.Value.message, validation.Value.errors);

        var version = structure.Versions.SingleOrDefault(x => x.EffectiveFrom == request.EffectiveFrom);
        if (version is null)
        {
            if (structure.Versions.Any(x => Overlaps(x.EffectiveFrom, x.EffectiveTo, request.EffectiveFrom, request.EffectiveTo))) return Result<SalaryStructureDto>.Conflict("The effective salary structure version overlaps an existing version.");
            version = BuildVersion(structure, request);
            structure.Versions.Add(version);
        }
        else
        {
            version.EffectiveTo = request.EffectiveTo;
            version.IsActive = request.IsActive;
            ReconcileComponents(version, request.Components);
        }
        structure.Code = code;
        structure.Name = request.Name.Trim();
        structure.Description = Normalize(request.Description);
        structure.IsActive = request.IsActive;
        structure.ConcurrencyVersion++;
        AddHistory(structure, version, SalaryStructureChangeType.Updated);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Result<SalaryStructureDto>.Conflict("The salary structure was changed by another user."); }
        return Result<SalaryStructureDto>.Success(ToDto(structure, version), "Salary structure updated.");
    }

    public async Task<Result<SalaryStructureDto>> SetActiveAsync(Guid id, bool active, int? expectedConcurrencyVersion, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<SalaryStructureDto>.Unauthorized("No authenticated tenant.");
        var structure = await db.SalaryStructures.Include(x => x.Versions).ThenInclude(x => x.Components).ThenInclude(x => x.SalaryComponent).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (structure is null) return Result<SalaryStructureDto>.NotFound("Salary structure not found.");
        if (expectedConcurrencyVersion is { } expected && expected != structure.ConcurrencyVersion) return Result<SalaryStructureDto>.Conflict("The salary structure was changed by another user.");
        if (structure.IsActive != active)
        {
            structure.IsActive = active;
            structure.ConcurrencyVersion++;
            AddHistory(structure, SelectVersion(structure.Versions, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), active ? SalaryStructureChangeType.Activated : SalaryStructureChangeType.Deactivated);
            await db.SaveChangesAsync(ct);
        }
        return Result<SalaryStructureDto>.Success(ToDto(structure, SelectVersion(structure.Versions, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))), active ? "Salary structure activated." : "Salary structure deactivated.");
    }

    public async Task<Result<IReadOnlyList<SalaryStructureHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || !await db.SalaryStructures.AnyAsync(x => x.TenantId == tenantId && x.Id == id, ct)) return Result<IReadOnlyList<SalaryStructureHistoryDto>>.NotFound("Salary structure not found.");
        var rows = await db.SalaryStructureHistories.AsNoTracking().Where(x => x.TenantId == tenantId && x.SalaryStructureId == id).OrderByDescending(x => x.ChangedAtUtc).ThenByDescending(x => x.Id).Select(x => new SalaryStructureHistoryDto(x.Id, x.SalaryStructureId, x.SalaryStructureVersionId, x.ChangeType, x.Code, x.Name, x.Description, x.EffectiveFrom, x.EffectiveTo, x.IsActive, x.ComponentsJson, x.ActorUserId, x.ChangedAtUtc)).ToListAsync(ct);
        return Result<IReadOnlyList<SalaryStructureHistoryDto>>.Success(rows);
    }

    public async Task<Result<SalaryStructureComponentDto>> AddComponentAsync(Guid id, SalaryStructureComponentRequest request, CancellationToken ct = default)
    {
        var loaded = await LoadCurrentAsync(id, ct);
        if (!loaded.succeeded) return Result<SalaryStructureComponentDto>.Failure(loaded.status, loaded.message);
        var loadedValue = loaded.value!.Value;
        var structure = loadedValue.structure; var version = loadedValue.version; var tenantId = loadedValue.tenantId;
        var validation = await ValidateComponentsAsync([request], version.EffectiveFrom, version.EffectiveTo, tenantId, ct, version.Components.Select(x => x.SalaryComponentId));
        if (validation is not null) return Result<SalaryStructureComponentDto>.Failure(validation.Value.status, validation.Value.message, validation.Value.errors);
        if (version.Components.Any(x => x.SalaryComponentId == request.SalaryComponentId && x.IsActive)) return Result<SalaryStructureComponentDto>.Conflict("This Salary Component is already in the structure version.");
        var component = BuildComponent(version, request);
        version.Components.Add(component); structure.ConcurrencyVersion++;
        AddHistory(structure, version, SalaryStructureChangeType.ComponentAdded); await db.SaveChangesAsync(ct);
        await LoadComponentNameAsync(component, ct);
        return Result<SalaryStructureComponentDto>.Success(ToComponentDto(component));
    }

    public async Task<Result<SalaryStructureComponentDto>> UpdateComponentAsync(Guid id, Guid componentId, SalaryStructureComponentRequest request, CancellationToken ct = default)
    {
        var loaded = await LoadCurrentAsync(id, ct);
        if (!loaded.succeeded) return Result<SalaryStructureComponentDto>.Failure(loaded.status, loaded.message);
        var loadedValue = loaded.value!.Value;
        var structure = loadedValue.structure; var version = loadedValue.version; var tenantId = loadedValue.tenantId;
        var component = version.Components.FirstOrDefault(x => x.Id == componentId);
        if (component is null) return Result<SalaryStructureComponentDto>.NotFound("Salary structure component not found.");
        var validation = await ValidateComponentsAsync([request], version.EffectiveFrom, version.EffectiveTo, tenantId, ct, version.Components.Where(x => x.Id != componentId).Select(x => x.SalaryComponentId));
        if (validation is not null) return Result<SalaryStructureComponentDto>.Failure(validation.Value.status, validation.Value.message, validation.Value.errors);
        ApplyComponent(component, request, version.EffectiveFrom);
        structure.ConcurrencyVersion++;
        AddHistory(structure, version, SalaryStructureChangeType.ComponentChanged); await db.SaveChangesAsync(ct);
        await LoadComponentNameAsync(component, ct);
        return Result<SalaryStructureComponentDto>.Success(ToComponentDto(component));
    }

    public async Task<Result<bool>> RemoveComponentAsync(Guid id, Guid componentId, CancellationToken ct = default)
    {
        var loaded = await LoadCurrentAsync(id, ct);
        if (!loaded.succeeded) return Result<bool>.Failure(loaded.status, loaded.message);
        var loadedValue = loaded.value!.Value;
        var structure = loadedValue.structure; var version = loadedValue.version;
        var component = version.Components.FirstOrDefault(x => x.Id == componentId);
        if (component is null) return Result<bool>.NotFound("Salary structure component not found.");
        component.IsActive = false; structure.ConcurrencyVersion++;
        AddHistory(structure, version, SalaryStructureChangeType.ComponentRemoved); await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true, "Salary structure component deactivated.");
    }

    private async Task<(bool succeeded, ResultStatus status, string message, (SalaryStructure structure, SalaryStructureVersion version, Guid tenantId)? value)> LoadCurrentAsync(Guid id, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId) return (false, ResultStatus.Unauthorized, "No authenticated tenant.", null);
        var structure = await db.SalaryStructures.Include(x => x.Versions).ThenInclude(x => x.Components).ThenInclude(x => x.SalaryComponent).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (structure is null) return (false, ResultStatus.NotFound, "Salary structure not found.", null);
        var version = SelectVersion(structure.Versions, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        return version is null ? (false, ResultStatus.NotFound, "No effective salary structure version exists.", null) : (true, ResultStatus.Success, "", (structure, version, tenantId));
    }

    private async Task LoadComponentNameAsync(SalaryStructureComponent component, CancellationToken ct)
    {
        if (component.SalaryComponent is null) component.SalaryComponent = await db.SalaryComponents.AsNoTracking().FirstAsync(x => x.TenantId == component.TenantId && x.Id == component.SalaryComponentId, ct);
    }

    private static SalaryStructureVersion BuildVersion(SalaryStructure structure, SalaryStructureRequest request)
    {
        var version = new SalaryStructureVersion { Id = Guid.NewGuid(), TenantId = structure.TenantId, SalaryStructureId = structure.Id, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, IsActive = request.IsActive };
        AddComponents(version, request.Components);
        return version;
    }

    private static void AddComponents(SalaryStructureVersion version, IEnumerable<SalaryStructureComponentRequest> requests)
    {
        foreach (var request in requests) version.Components.Add(BuildComponent(version, request));
    }

    private static void ReconcileComponents(SalaryStructureVersion version, IEnumerable<SalaryStructureComponentRequest> requests)
    {
        var requested = requests.ToList();
        var requestedIds = requested.Select(x => x.SalaryComponentId).ToHashSet();
        foreach (var existing in version.Components.Where(x => !requestedIds.Contains(x.SalaryComponentId))) existing.IsActive = false;
        foreach (var request in requested)
        {
            var existing = version.Components.FirstOrDefault(x => x.SalaryComponentId == request.SalaryComponentId);
            if (existing is null) version.Components.Add(BuildComponent(version, request));
            else ApplyComponent(existing, request, version.EffectiveFrom);
        }
    }

    private static SalaryStructureComponent BuildComponent(SalaryStructureVersion version, SalaryStructureComponentRequest request)
    {
        var component = new SalaryStructureComponent { Id = Guid.NewGuid(), TenantId = version.TenantId, SalaryStructureVersionId = version.Id };
        ApplyComponent(component, request, version.EffectiveFrom); return component;
    }

    private static void ApplyComponent(SalaryStructureComponent component, SalaryStructureComponentRequest request, DateOnly defaultFrom)
    {
        component.SalaryComponentId = request.SalaryComponentId; component.Sequence = request.Sequence; component.CalculationType = request.CalculationType; component.Value = request.Value; component.PercentageOfComponentId = request.PercentageOfComponentId; component.Formula = Normalize(request.Formula); component.IsProratable = request.IsProratable; component.IsEditableAtEmployeeLevel = request.IsEditableAtEmployeeLevel; component.MinimumAmount = request.MinimumAmount; component.MaximumAmount = request.MaximumAmount; component.IsActive = request.IsActive; component.EffectiveFrom = request.EffectiveFrom ?? defaultFrom; component.EffectiveTo = request.EffectiveTo;
    }

    private async Task<(ResultStatus status, string message, IReadOnlyList<ValidationError>? errors)?> ValidateComponentsAsync(IEnumerable<SalaryStructureComponentRequest> requests, DateOnly versionFrom, DateOnly? versionTo, Guid tenantId, CancellationToken ct, IEnumerable<Guid>? existingIds = null)
    {
        var rows = requests.ToList(); var ids = rows.Select(x => x.SalaryComponentId).ToList();
        if (rows.Count == 0) return (ResultStatus.ValidationFailed, "At least one Salary Component is required.", [new("components", "Add at least one Salary Component.")]);
        if (rows.Any(x => x.SalaryComponentId == Guid.Empty)) return (ResultStatus.ValidationFailed, "Every structure row must reference a Salary Component.", [new("salaryComponentId", "Salary Component is required.")]);
        if (ids.Count != ids.Distinct().Count() || (existingIds ?? []).Intersect(ids).Any()) return (ResultStatus.Conflict, "A Salary Component may appear only once in a structure version.", null);
        if (rows.Any(x => x.Sequence < 1) || rows.Select(x => x.Sequence).Distinct().Count() != rows.Count) return (ResultStatus.ValidationFailed, "Component sequence values must be positive and unique.", [new("sequence", "Sequence values must be positive and unique.")]);
        if (rows.Any(x => x.EffectiveFrom is { } from && from < versionFrom || x.EffectiveTo is { } to && to < (x.EffectiveFrom ?? versionFrom) || x.EffectiveTo is { } rowTo && versionTo is { } max && rowTo > max)) return (ResultStatus.ValidationFailed, "Component effective dates must remain within the structure version.", [new("effectiveTo", "Component effective dates are invalid.")]);
        var found = await db.SalaryComponents.AsNoTracking().Where(x => x.TenantId == tenantId && ids.Contains(x.Id) && x.IsActive).ToListAsync(ct);
        if (found.Count != ids.Distinct().Count()) return (ResultStatus.ValidationFailed, "Every Salary Component must exist, be active, and belong to this tenant.", [new("salaryComponentId", "Salary Component was not found in this tenant.")]);
        var known = found.Select(x => x.Id).ToHashSet();
        foreach (var row in rows)
        {
            if (row.PercentageOfComponentId is Guid baseId && (!known.Contains(baseId) || baseId == row.SalaryComponentId)) return (ResultStatus.ValidationFailed, "Percentage base must reference another active Salary Component in this tenant.", [new("percentageOfComponentId", "Percentage base is invalid.")]);
            var error = row.CalculationType switch
            {
                SalaryStructureCalculationType.FixedAmount when row.Value is null || row.Value < 0 => ("value", "Fixed amount must be zero or greater."),
                SalaryStructureCalculationType.FixedAmount when row.Formula is not null || row.PercentageOfComponentId is not null => ("value", "Fixed amount cannot include a percentage base or formula."),
                SalaryStructureCalculationType.Percentage when row.Value is null || row.Value < 0 || row.Value > 100 => ("value", "Percentage must be between 0 and 100."),
                SalaryStructureCalculationType.Percentage when row.PercentageOfComponentId is null => ("percentageOfComponentId", "Percentage base is required."),
                SalaryStructureCalculationType.Formula when string.IsNullOrWhiteSpace(row.Formula) => ("formula", "Formula text is required."),
                SalaryStructureCalculationType.Formula when row.Value is not null || row.PercentageOfComponentId is not null => ("formula", "Formula cannot include a fixed value or percentage base."),
                SalaryStructureCalculationType.Manual when row.Value is not null || row.PercentageOfComponentId is not null || !string.IsNullOrWhiteSpace(row.Formula) => ("calculationType", "Manual rows cannot include a calculated value."),
                _ => ((string field, string message)?)null
            };
            if (error is not null) return (ResultStatus.ValidationFailed, error.Value.message, [new(error.Value.field, error.Value.message)]);
            if (row.MinimumAmount is < 0 || row.MaximumAmount is < 0 || row.MinimumAmount is not null && row.MaximumAmount is not null && row.MinimumAmount > row.MaximumAmount) return (ResultStatus.ValidationFailed, "Minimum and maximum amounts are invalid.", [new("maximumAmount", "Maximum must be greater than or equal to minimum.")]);
        }
        return null;
    }

    private void AddHistory(SalaryStructure structure, SalaryStructureVersion? version, SalaryStructureChangeType change)
    {
        var snapshot = version?.Components.OrderBy(x => x.Sequence).Select(x => new { x.Id, x.SalaryComponentId, x.Sequence, x.CalculationType, x.Value, x.PercentageOfComponentId, x.Formula, x.IsProratable, x.IsEditableAtEmployeeLevel, x.MinimumAmount, x.MaximumAmount, x.IsActive, x.EffectiveFrom, x.EffectiveTo }) ?? [];
        db.SalaryStructureHistories.Add(new SalaryStructureHistory { Id = Guid.NewGuid(), TenantId = structure.TenantId, SalaryStructureId = structure.Id, SalaryStructureVersionId = version?.Id, ActorUserId = tenant.UserId, ChangeType = change, ChangedAtUtc = clock.GetUtcNow().UtcDateTime, Code = structure.Code, Name = structure.Name, Description = structure.Description, EffectiveFrom = version?.EffectiveFrom, EffectiveTo = version?.EffectiveTo, IsActive = structure.IsActive, ComponentsJson = JsonSerializer.Serialize(snapshot, JsonOptions) });
    }

    private static SalaryStructureDto ToDto(SalaryStructure structure, SalaryStructureVersion? version)
    {
        var components = version?.Components.OrderBy(x => x.Sequence).Select(ToComponentDto).ToList() ?? [];
        return new SalaryStructureDto(structure.Id, version?.Id ?? Guid.Empty, structure.Code, structure.Name, structure.Description, version?.EffectiveFrom ?? default, version?.EffectiveTo, structure.IsActive && (version?.IsActive ?? false), components.Count, structure.ConcurrencyVersion, components, structure.CreatedDate, structure.ModifiedDate);
    }

    private static SalaryStructureComponentDto ToComponentDto(SalaryStructureComponent x) => new(x.Id, x.SalaryComponentId, x.SalaryComponent?.Code ?? string.Empty, x.SalaryComponent?.Name ?? string.Empty, x.SalaryComponent?.ComponentType ?? SalaryComponentType.Information, x.Sequence, x.CalculationType, x.Value, x.PercentageOfComponentId, x.Formula, x.IsProratable, x.IsEditableAtEmployeeLevel, x.MinimumAmount, x.MaximumAmount, x.IsActive, x.EffectiveFrom, x.EffectiveTo);
    private static SalaryStructureVersion? SelectVersion(IEnumerable<SalaryStructureVersion> versions, DateOnly date) => versions.Where(x => x.IsActive && x.EffectiveFrom <= date && (x.EffectiveTo is null || date <= x.EffectiveTo)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefault() ?? versions.Where(x => x.IsActive).OrderByDescending(x => x.EffectiveFrom).FirstOrDefault();
    private static bool Overlaps(DateOnly fromA, DateOnly? toA, DateOnly fromB, DateOnly? toB) => fromA <= (toB ?? DateOnly.MaxValue) && fromB <= (toA ?? DateOnly.MaxValue);
    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool ValidPage(SalaryStructureQuery q) => q.Page > 0 && q.PageSize > 0 && q.PageSize <= PagedQuery.MaxPageSize;
    private static (ResultStatus status, string message, IReadOnlyList<ValidationError>? errors)? ValidateHeader(SalaryStructureRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Code)) return (ResultStatus.ValidationFailed, "Salary Structure Code is required.", [new("code", "Code is required.")]);
        if (string.IsNullOrWhiteSpace(r.Name)) return (ResultStatus.ValidationFailed, "Salary Structure Name is required.", [new("name", "Name is required.")]);
        if (r.EffectiveTo < r.EffectiveFrom) return (ResultStatus.ValidationFailed, "Effective To cannot be earlier than Effective From.", [new("effectiveTo", "Effective To cannot be earlier than Effective From.")]);
        return null;
    }
}
