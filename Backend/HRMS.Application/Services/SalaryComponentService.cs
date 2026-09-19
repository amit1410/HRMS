using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class SalaryComponentService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock) : ISalaryComponentService
{
    public async Task<Result<PagedResult<SalaryComponentDto>>> GetAsync(SalaryComponentQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is null) return Result<PagedResult<SalaryComponentDto>>.Unauthorized("No authenticated tenant.");
        var tenantId = tenant.TenantId.Value;
        var source = db.SalaryComponents.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(query.Search)) { var search = query.Search.Trim().ToLowerInvariant(); source = source.Where(x => x.Code.ToLower().Contains(search) || x.Name.ToLower().Contains(search)); }
        if (query.ComponentType is { } type) source = source.Where(x => x.ComponentType == type);
        if (query.CalculationType is { } calculation) source = source.Where(x => x.CalculationType == calculation);
        if (query.IsActive is { } active) source = source.Where(x => x.IsActive == active);
        if (query.IsStatutory is { } statutory) source = source.Where(x => x.IsStatutory == statutory);
        var page = await source.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code).ThenBy(x => x.Id).Select(Projection).ToPagedResultAsync(query, ct);
        return Result<PagedResult<SalaryComponentDto>>.Success(page);
    }

    public async Task<Result<SalaryComponentDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is null) return Result<SalaryComponentDto>.Unauthorized("No authenticated tenant.");
        var item = await db.SalaryComponents.AsNoTracking().Where(x => x.TenantId == tenant.TenantId && x.Id == id).Select(Projection).FirstOrDefaultAsync(ct);
        return item is null ? Result<SalaryComponentDto>.NotFound("Salary component not found.") : Result<SalaryComponentDto>.Success(item);
    }

    public async Task<Result<SalaryComponentDto>> CreateAsync(SalaryComponentRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<SalaryComponentDto>.Unauthorized("No authenticated tenant.");
        var validation = Validate(request); if (validation is not null) return Result<SalaryComponentDto>.Failure(validation.Status, validation.Message, validation.Errors);
        var code = NormalizeCode(request.Code);
        if (await db.SalaryComponents.AnyAsync(x => x.TenantId == tenantId && x.Code == code, ct)) return Result<SalaryComponentDto>.Conflict($"A salary component with code '{code}' already exists.", [new("code", "This code is already in use.")]);
        var item = Map(new SalaryComponent { Id = Guid.NewGuid(), TenantId = tenantId, Code = code }, request);
        db.SalaryComponents.Add(item);
        AddHistory(item, SalaryComponentChangeType.Created);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            if (await db.SalaryComponents.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Code == code, ct)) return Result<SalaryComponentDto>.Conflict("A salary component with this code already exists.");
            throw;
        }
        return Result<SalaryComponentDto>.Success(ToDto(item), "Salary component created.");
    }

    public async Task<Result<SalaryComponentDto>> UpdateAsync(Guid id, SalaryComponentRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<SalaryComponentDto>.Unauthorized("No authenticated tenant.");
        var validation = Validate(request); if (validation is not null) return Result<SalaryComponentDto>.Failure(validation.Status, validation.Message, validation.Errors);
        var item = await db.SalaryComponents.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (item is null) return Result<SalaryComponentDto>.NotFound("Salary component not found.");
        if (request.ExpectedConcurrencyVersion is { } version && item.ConcurrencyVersion != version) return Result<SalaryComponentDto>.Conflict("The salary component was changed by another user.");
        var code = NormalizeCode(request.Code);
        if (await db.SalaryComponents.AnyAsync(x => x.TenantId == tenantId && x.Id != id && x.Code == code, ct)) return Result<SalaryComponentDto>.Conflict($"A salary component with code '{code}' already exists.");
        var wasActive = item.IsActive; Map(item, request); item.Code = code; item.ConcurrencyVersion++;
        AddHistory(item, wasActive == item.IsActive ? SalaryComponentChangeType.Updated : item.IsActive ? SalaryComponentChangeType.Activated : SalaryComponentChangeType.Deactivated);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<SalaryComponentDto>.Conflict("The salary component was changed by another user."); }
        return Result<SalaryComponentDto>.Success(ToDto(item), "Salary component updated.");
    }

    public async Task<Result<SalaryComponentDto>> SetActiveAsync(Guid id, bool active, int? expectedConcurrencyVersion, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<SalaryComponentDto>.Unauthorized("No authenticated tenant.");
        var item = await db.SalaryComponents.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (item is null) return Result<SalaryComponentDto>.NotFound("Salary component not found.");
        if (expectedConcurrencyVersion is { } version && item.ConcurrencyVersion != version) return Result<SalaryComponentDto>.Conflict("The salary component was changed by another user.");
        if (item.IsActive != active) { item.IsActive = active; item.ConcurrencyVersion++; AddHistory(item, active ? SalaryComponentChangeType.Activated : SalaryComponentChangeType.Deactivated); await db.SaveChangesAsync(ct); }
        return Result<SalaryComponentDto>.Success(ToDto(item), active ? "Salary component activated." : "Salary component deactivated.");
    }

    public async Task<Result<IReadOnlyList<SalaryComponentHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || !await db.SalaryComponents.AnyAsync(x => x.TenantId == tenantId && x.Id == id, ct)) return Result<IReadOnlyList<SalaryComponentHistoryDto>>.NotFound("Salary component not found.");
        var rows = await db.SalaryComponentHistories.AsNoTracking().Where(x => x.TenantId == tenantId && x.SalaryComponentId == id).OrderByDescending(x => x.ChangedAtUtc).ThenByDescending(x => x.Id).Select(x => new SalaryComponentHistoryDto(x.Id, x.SalaryComponentId, x.ChangeType, x.Code, x.Name, x.ComponentType, x.CalculationType, x.StatutoryType, x.IsTaxable, x.IsStatutory, x.IsRecurring, x.AffectsGross, x.AffectsNetPay, x.EffectiveFrom, x.EffectiveTo, x.IsActive, x.ActorUserId, x.ChangedAtUtc)).ToListAsync(ct);
        return Result<IReadOnlyList<SalaryComponentHistoryDto>>.Success(rows);
    }

    private void AddHistory(SalaryComponent x, SalaryComponentChangeType change) => db.SalaryComponentHistories.Add(new SalaryComponentHistory { Id = Guid.NewGuid(), TenantId = x.TenantId, SalaryComponentId = x.Id, ActorUserId = tenant.UserId, ChangeType = change, ChangedAtUtc = clock.GetUtcNow().UtcDateTime, Code = x.Code, Name = x.Name, ComponentType = x.ComponentType, CalculationType = x.CalculationType, StatutoryType = x.StatutoryType, IsTaxable = x.IsTaxable, IsStatutory = x.IsStatutory, IsRecurring = x.IsRecurring, AffectsGross = x.AffectsGross, AffectsNetPay = x.AffectsNetPay, EffectiveFrom = x.EffectiveFrom, EffectiveTo = x.EffectiveTo, IsActive = x.IsActive });
    private static SalaryComponent Map(SalaryComponent x, SalaryComponentRequest r) { x.Name = r.Name.Trim(); x.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim(); x.ComponentType = r.ComponentType; x.CalculationType = r.CalculationType; x.StatutoryType = r.StatutoryType; x.IsTaxable = r.IsTaxable; x.IsStatutory = r.IsStatutory; x.IsRecurring = r.IsRecurring; x.AffectsGross = r.AffectsGross; x.AffectsNetPay = r.AffectsNetPay; x.DisplayOrder = r.DisplayOrder; x.EffectiveFrom = r.EffectiveFrom; x.EffectiveTo = r.EffectiveTo; x.IsActive = r.IsActive; return x; }
    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
    private static Result<SalaryComponentDto>? Validate(SalaryComponentRequest r) { if (string.IsNullOrWhiteSpace(r.Code)) return Result<SalaryComponentDto>.Invalid("code", "Salary Component Code is required."); if (string.IsNullOrWhiteSpace(r.Name)) return Result<SalaryComponentDto>.Invalid("name", "Salary Component Name is required."); if (r.EffectiveTo < r.EffectiveFrom) return Result<SalaryComponentDto>.Invalid("effectiveTo", "Effective To cannot be earlier than Effective From."); if (r.IsStatutory != (r.StatutoryType != SalaryStatutoryType.None)) return Result<SalaryComponentDto>.Invalid("statutoryType", "Statutory Type must match IsStatutory."); if (r.ComponentType == SalaryComponentType.EmployerContribution && r.AffectsNetPay) return Result<SalaryComponentDto>.Invalid("affectsNetPay", "Employer contributions cannot affect employee net pay."); return null; }
    private static SalaryComponentDto ToDto(SalaryComponent x) => new(x.Id, x.Code, x.Name, x.Description, x.ComponentType, x.CalculationType, x.StatutoryType, x.IsTaxable, x.IsStatutory, x.IsRecurring, x.AffectsGross, x.AffectsNetPay, x.DisplayOrder, x.EffectiveFrom, x.EffectiveTo, x.IsActive, x.ConcurrencyVersion, x.CreatedDate, x.ModifiedDate);
    private static readonly System.Linq.Expressions.Expression<Func<SalaryComponent, SalaryComponentDto>> Projection = x => new(x.Id, x.Code, x.Name, x.Description, x.ComponentType, x.CalculationType, x.StatutoryType, x.IsTaxable, x.IsStatutory, x.IsRecurring, x.AffectsGross, x.AffectsNetPay, x.DisplayOrder, x.EffectiveFrom, x.EffectiveTo, x.IsActive, x.ConcurrencyVersion, x.CreatedDate, x.ModifiedDate);
}
