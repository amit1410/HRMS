using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class EmployeeSalaryAssignmentService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock) : IEmployeeSalaryAssignmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<PagedResult<EmployeeSalaryAssignmentDto>>> GetAsync(EmployeeSalaryAssignmentQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PagedResult<EmployeeSalaryAssignmentDto>>.Unauthorized("No authenticated tenant.");
        if (!ValidPage(query)) return Result<PagedResult<EmployeeSalaryAssignmentDto>>.Invalid("page", "Page values are out of range.");
        var source = db.EmployeeSalaryAssignments.AsNoTracking().Include(x => x.Employee).Include(x => x.SalaryStructure).Include(x => x.SalaryStructureVersion).ThenInclude(x => x!.Components).ThenInclude(x => x.SalaryComponent).Include(x => x.Components).Where(x => x.TenantId == tenantId);
        if (query.EmployeeId is { } employeeId) source = source.Where(x => x.EmployeeId == employeeId);
        if (query.SalaryStructureId is { } structureId) source = source.Where(x => x.SalaryStructureId == structureId);
        if (query.Status is { } status) source = source.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search)) { var search = query.Search.Trim().ToLowerInvariant(); source = source.Where(x => (x.Employee!.EmployeeCode ?? "").ToLower().Contains(search) || (x.Employee.FirstName + " " + x.Employee.LastName).ToLower().Contains(search) || x.SalaryStructure!.Code.ToLower().Contains(search)); }
        if (query.EffectiveOn is { } date) source = source.Where(x => x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date));
        var total = await source.CountAsync(ct);
        var rows = await source.OrderByDescending(x => x.EffectiveFrom).ThenBy(x => x.Employee!.EmployeeCode).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedResult<EmployeeSalaryAssignmentDto>>.Success(new PagedResult<EmployeeSalaryAssignmentDto>(rows.Select(ToDto).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<EmployeeSalaryAssignmentDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<EmployeeSalaryAssignmentDto>.Unauthorized("No authenticated tenant.");
        var row = await LoadAsync(id, tenantId, ct);
        return row is null ? Result<EmployeeSalaryAssignmentDto>.NotFound("Employee salary assignment not found.") : Result<EmployeeSalaryAssignmentDto>.Success(ToDto(row));
    }

    public Task<Result<PagedResult<EmployeeSalaryAssignmentDto>>> GetForEmployeeAsync(Guid employeeId, EmployeeSalaryAssignmentQuery query, CancellationToken ct = default)
    {
        query.EmployeeId = employeeId;
        return GetAsync(query, ct);
    }

    public async Task<Result<EmployeeSalaryAssignmentDto>> GetEffectiveAsync(Guid employeeId, DateOnly date, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<EmployeeSalaryAssignmentDto>.Unauthorized("No authenticated tenant.");
        var rows = await db.EmployeeSalaryAssignments.Include(x => x.Employee).Include(x => x.SalaryStructure).Include(x => x.SalaryStructureVersion).ThenInclude(x => x!.Components).ThenInclude(x => x.SalaryComponent).Include(x => x.Components).Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Status == EmployeeSalaryAssignmentStatus.Active && x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date)).ToListAsync(ct);
        return rows.Count switch { 0 => Result<EmployeeSalaryAssignmentDto>.NotFound("No effective salary assignment exists for this employee and date."), 1 => Result<EmployeeSalaryAssignmentDto>.Success(ToDto(rows[0])), _ => Result<EmployeeSalaryAssignmentDto>.Conflict("Multiple effective salary assignments exist for this employee and date.") };
    }

    public async Task<Result<EmployeeSalaryAssignmentDto>> CreateAsync(EmployeeSalaryAssignmentRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<EmployeeSalaryAssignmentDto>.Unauthorized("No authenticated tenant.");
        var error = ValidateRequest(request); if (error is not null) return Result<EmployeeSalaryAssignmentDto>.Failure(error.Value.status, error.Value.message, error.Value.errors);
        var employee = await db.Employees.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId, ct);
        if (employee is null) return Result<EmployeeSalaryAssignmentDto>.NotFound("Employee not found.");
        var structure = await db.SalaryStructures.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.SalaryStructureId, ct);
        if (structure is null) return Result<EmployeeSalaryAssignmentDto>.NotFound("Salary Structure not found.");
        if (!structure.IsActive) return Result<EmployeeSalaryAssignmentDto>.Conflict("The Salary Structure is inactive.");
        var version = await ResolveVersionAsync(request.SalaryStructureId, request.EffectiveFrom, tenantId, ct);
        if (version is null) return Result<EmployeeSalaryAssignmentDto>.Invalid("salaryStructureId", "No single active Salary Structure version is effective on the assignment date.");
        if (await OverlapsAsync(request.EmployeeId, request.EffectiveFrom, request.EffectiveTo, tenantId, null, ct)) return Result<EmployeeSalaryAssignmentDto>.Conflict("The employee already has an overlapping salary assignment.");
        var validation = ValidateComponents(request.Components, version); if (validation is not null) return Result<EmployeeSalaryAssignmentDto>.Failure(validation.Value.status, validation.Value.message, validation.Value.errors);
        var row = BuildAssignment(request, tenantId, version);
        foreach (var component in request.Components) row.Components.Add(BuildComponent(row, component, version));
        AddHistory(row, EmployeeSalaryAssignmentChangeType.Created);
        db.EmployeeSalaryAssignments.Add(row);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { return Result<EmployeeSalaryAssignmentDto>.Conflict("The salary assignment conflicts with existing data."); }
        return Result<EmployeeSalaryAssignmentDto>.Success(ToDto(row), "Employee salary assignment created.");
    }

    public async Task<Result<EmployeeSalaryAssignmentDto>> UpdateAsync(Guid id, EmployeeSalaryAssignmentRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<EmployeeSalaryAssignmentDto>.Unauthorized("No authenticated tenant.");
        var error = ValidateRequest(request); if (error is not null) return Result<EmployeeSalaryAssignmentDto>.Failure(error.Value.status, error.Value.message, error.Value.errors);
        var row = await LoadAsync(id, tenantId, ct); if (row is null) return Result<EmployeeSalaryAssignmentDto>.NotFound("Employee salary assignment not found.");
        if (request.ExpectedConcurrencyVersion is { } expected && expected != row.ConcurrencyVersion) return Result<EmployeeSalaryAssignmentDto>.Conflict("The employee salary assignment was changed by another user.");
        if (await db.Employees.AllAsync(x => x.TenantId != tenantId || x.Id != request.EmployeeId, ct)) return Result<EmployeeSalaryAssignmentDto>.NotFound("Employee not found.");
        var structure = await db.SalaryStructures.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.SalaryStructureId, ct); if (structure is null) return Result<EmployeeSalaryAssignmentDto>.NotFound("Salary Structure not found."); if (!structure.IsActive) return Result<EmployeeSalaryAssignmentDto>.Conflict("The Salary Structure is inactive.");
        var version = await ResolveVersionAsync(request.SalaryStructureId, request.EffectiveFrom, tenantId, ct); if (version is null) return Result<EmployeeSalaryAssignmentDto>.Invalid("salaryStructureId", "No single active Salary Structure version is effective on the assignment date.");
        if (await OverlapsAsync(request.EmployeeId, request.EffectiveFrom, request.EffectiveTo, tenantId, id, ct)) return Result<EmployeeSalaryAssignmentDto>.Conflict("The employee already has an overlapping salary assignment.");
        var validation = ValidateComponents(request.Components, version); if (validation is not null) return Result<EmployeeSalaryAssignmentDto>.Failure(validation.Value.status, validation.Value.message, validation.Value.errors);
        row.EmployeeId = request.EmployeeId; row.SalaryStructureId = request.SalaryStructureId; row.SalaryStructureVersionId = version.Id; row.EffectiveFrom = request.EffectiveFrom; row.EffectiveTo = request.EffectiveTo; row.AnnualCtc = request.AnnualCtc; row.MonthlyCtc = request.MonthlyCtc; row.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(); row.PayFrequency = request.PayFrequency; row.Status = request.Status; row.ChangeReason = request.ChangeReason; row.Remarks = request.Remarks?.Trim();
        ReconcileComponents(row, request.Components, version); AddHistory(row, EmployeeSalaryAssignmentChangeType.Updated);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<EmployeeSalaryAssignmentDto>.Conflict("The employee salary assignment was changed by another user."); }
        return Result<EmployeeSalaryAssignmentDto>.Success(ToDto(row), "Employee salary assignment updated.");
    }

    public async Task<Result<EmployeeSalaryAssignmentDto>> SetActiveAsync(Guid id, bool active, int? expectedConcurrencyVersion, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<EmployeeSalaryAssignmentDto>.Unauthorized("No authenticated tenant.");
        var row = await LoadAsync(id, tenantId, ct); if (row is null) return Result<EmployeeSalaryAssignmentDto>.NotFound("Employee salary assignment not found.");
        if (expectedConcurrencyVersion is { } expected && expected != row.ConcurrencyVersion) return Result<EmployeeSalaryAssignmentDto>.Conflict("The employee salary assignment was changed by another user.");
        row.Status = active ? EmployeeSalaryAssignmentStatus.Active : EmployeeSalaryAssignmentStatus.Inactive; AddHistory(row, active ? EmployeeSalaryAssignmentChangeType.Activated : EmployeeSalaryAssignmentChangeType.Deactivated); await db.SaveChangesAsync(ct);
        return Result<EmployeeSalaryAssignmentDto>.Success(ToDto(row));
    }

    public async Task<Result<IReadOnlyList<EmployeeSalaryAssignmentHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<IReadOnlyList<EmployeeSalaryAssignmentHistoryDto>>.Unauthorized("No authenticated tenant.");
        if (!await db.EmployeeSalaryAssignments.AnyAsync(x => x.TenantId == tenantId && x.Id == id, ct)) return Result<IReadOnlyList<EmployeeSalaryAssignmentHistoryDto>>.NotFound("Employee salary assignment not found.");
        var rows = await db.EmployeeSalaryAssignmentHistories.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeSalaryAssignmentId == id).OrderByDescending(x => x.ChangedAtUtc).Select(x => new EmployeeSalaryAssignmentHistoryDto(x.Id, x.EmployeeSalaryAssignmentId, x.ChangeType, x.EmployeeId, x.SalaryStructureId, x.SalaryStructureVersionId, x.EffectiveFrom, x.EffectiveTo, x.AnnualCtc, x.MonthlyCtc, x.CurrencyCode, x.PayFrequency, x.Status, x.ChangeReason, x.Remarks, x.ComponentsJson, x.ChangedAtUtc, x.ActorUserId)).ToListAsync(ct);
        return Result<IReadOnlyList<EmployeeSalaryAssignmentHistoryDto>>.Success(rows);
    }

    public Task<Result<EmployeeSalaryComponentDto>> AddComponentAsync(Guid id, EmployeeSalaryComponentRequest request, CancellationToken ct = default) => ChangeComponentAsync(id, null, request, EmployeeSalaryAssignmentChangeType.OverrideAdded, ct);
    public Task<Result<EmployeeSalaryComponentDto>> UpdateComponentAsync(Guid id, Guid componentId, EmployeeSalaryComponentRequest request, CancellationToken ct = default) => ChangeComponentAsync(id, componentId, request, EmployeeSalaryAssignmentChangeType.OverrideChanged, ct);

    public async Task<Result<bool>> RemoveComponentAsync(Guid id, Guid componentId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<bool>.Unauthorized("No authenticated tenant.");
        var row = await LoadAsync(id, tenantId, ct); var component = row?.Components.FirstOrDefault(x => x.Id == componentId); if (row is null || component is null) return Result<bool>.NotFound("Employee salary override not found.");
        component.IsActive = false; AddHistory(row, EmployeeSalaryAssignmentChangeType.OverrideRemoved); await db.SaveChangesAsync(ct); return Result<bool>.Success(true);
    }

    private async Task<Result<EmployeeSalaryComponentDto>> ChangeComponentAsync(Guid id, Guid? componentId, EmployeeSalaryComponentRequest request, EmployeeSalaryAssignmentChangeType changeType, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<EmployeeSalaryComponentDto>.Unauthorized("No authenticated tenant.");
        var row = await LoadAsync(id, tenantId, ct); if (row is null) return Result<EmployeeSalaryComponentDto>.NotFound("Employee salary assignment not found.");
        var structureComponent = await db.SalaryStructureComponents.Include(x => x.SalaryComponent).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.SalaryStructureComponentId && x.SalaryStructureVersionId == row.SalaryStructureVersionId && x.IsActive, ct);
        if (structureComponent is null) return Result<EmployeeSalaryComponentDto>.NotFound("Salary Structure component not found for the bound version.");
        var validation = ValidateComponent(request, structureComponent); if (validation is not null) return Result<EmployeeSalaryComponentDto>.Failure(validation.Value.status, validation.Value.message, validation.Value.errors);
        var existing = componentId is { } componentIdValue ? row.Components.FirstOrDefault(x => x.Id == componentIdValue) : row.Components.FirstOrDefault(x => x.SalaryStructureComponentId == request.SalaryStructureComponentId && x.IsActive);
        if (componentId is null && existing is not null) return Result<EmployeeSalaryComponentDto>.Conflict("This Salary Structure component already has an override.");
        if (existing is null) { existing = BuildComponent(row, request, row.SalaryStructureVersion!, structureComponent); db.EmployeeSalaryComponents.Add(existing); } else ApplyComponent(existing, request);
        AddHistory(row, changeType); await db.SaveChangesAsync(ct); return Result<EmployeeSalaryComponentDto>.Success(ToComponentDto(existing, structureComponent));
    }

    private async Task<EmployeeSalaryAssignment?> LoadAsync(Guid id, Guid tenantId, CancellationToken ct) => await db.EmployeeSalaryAssignments.Include(x => x.Employee).Include(x => x.SalaryStructure).Include(x => x.SalaryStructureVersion).ThenInclude(x => x!.Components).ThenInclude(x => x.SalaryComponent).Include(x => x.Components).ThenInclude(x => x.SalaryStructureComponent).ThenInclude(x => x!.SalaryComponent).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
    private async Task<SalaryStructureVersion?> ResolveVersionAsync(Guid structureId, DateOnly date, Guid tenantId, CancellationToken ct)
    {
        var structure = await db.SalaryStructures.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == structureId && x.IsActive, ct); if (structure is null) return null;
        var versions = await db.SalaryStructureVersions.Include(x => x.Components).ThenInclude(x => x.SalaryComponent).Where(x => x.TenantId == tenantId && x.SalaryStructureId == structureId && x.IsActive && x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date)).ToListAsync(ct);
        return versions.Count == 1 ? versions[0] : null;
    }
    private async Task<bool> OverlapsAsync(Guid employeeId, DateOnly from, DateOnly? to, Guid tenantId, Guid? exclude, CancellationToken ct) => await db.EmployeeSalaryAssignments.AnyAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Status == EmployeeSalaryAssignmentStatus.Active && (!exclude.HasValue || x.Id != exclude.Value) && x.EffectiveFrom <= (to ?? DateOnly.MaxValue) && (x.EffectiveTo ?? DateOnly.MaxValue) >= from, ct);
    private static EmployeeSalaryAssignment BuildAssignment(EmployeeSalaryAssignmentRequest r, Guid tenantId, SalaryStructureVersion version) => new() { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = r.EmployeeId, SalaryStructureId = version.SalaryStructureId, SalaryStructureVersionId = version.Id, EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo, AnnualCtc = r.AnnualCtc, MonthlyCtc = r.MonthlyCtc, CurrencyCode = r.CurrencyCode.Trim().ToUpperInvariant(), PayFrequency = r.PayFrequency, Status = r.Status, ChangeReason = r.ChangeReason, Remarks = r.Remarks?.Trim() };
    private static EmployeeSalaryComponent BuildComponent(EmployeeSalaryAssignment row, EmployeeSalaryComponentRequest r, SalaryStructureVersion version, SalaryStructureComponent? resolved = null) { var sc = resolved ?? version.Components.First(x => x.Id == r.SalaryStructureComponentId); var c = new EmployeeSalaryComponent { Id = Guid.NewGuid(), TenantId = row.TenantId, EmployeeSalaryAssignmentId = row.Id, SalaryStructureComponentId = sc.Id, SalaryComponentId = sc.SalaryComponentId }; ApplyComponent(c, r); return c; }
    private static void ApplyComponent(EmployeeSalaryComponent c, EmployeeSalaryComponentRequest r) { c.OverrideValue = r.OverrideValue; c.OverridePercentage = r.OverridePercentage; c.OverrideFormula = string.IsNullOrWhiteSpace(r.OverrideFormula) ? null : r.OverrideFormula.Trim(); c.EffectiveFrom = r.EffectiveFrom; c.EffectiveTo = r.EffectiveTo; c.IsActive = r.IsActive; c.Remarks = r.Remarks?.Trim(); }
    private static void ReconcileComponents(EmployeeSalaryAssignment row, IEnumerable<EmployeeSalaryComponentRequest> requests, SalaryStructureVersion version) { var list = requests.ToList(); var ids = list.Select(x => x.SalaryStructureComponentId).ToHashSet(); foreach (var old in row.Components.Where(x => !ids.Contains(x.SalaryStructureComponentId))) old.IsActive = false; foreach (var request in list) { var existing = row.Components.FirstOrDefault(x => x.SalaryStructureComponentId == request.SalaryStructureComponentId); if (existing is null) row.Components.Add(BuildComponent(row, request, version)); else ApplyComponent(existing, request); } }
    private void AddHistory(EmployeeSalaryAssignment row, EmployeeSalaryAssignmentChangeType type)
    {
        var history = new EmployeeSalaryAssignmentHistory { Id = Guid.NewGuid(), TenantId = row.TenantId, EmployeeSalaryAssignmentId = row.Id, ChangeType = type, ChangedAtUtc = clock.GetUtcNow().UtcDateTime, EmployeeId = row.EmployeeId, SalaryStructureId = row.SalaryStructureId, SalaryStructureVersionId = row.SalaryStructureVersionId, EffectiveFrom = row.EffectiveFrom, EffectiveTo = row.EffectiveTo, AnnualCtc = row.AnnualCtc, MonthlyCtc = row.MonthlyCtc, CurrencyCode = row.CurrencyCode, PayFrequency = row.PayFrequency, Status = row.Status, ChangeReason = row.ChangeReason, Remarks = row.Remarks, ComponentsJson = JsonSerializer.Serialize(row.Components.Select(x => new { x.SalaryStructureComponentId, x.OverrideValue, x.OverridePercentage, x.OverrideFormula, x.EffectiveFrom, x.EffectiveTo, x.IsActive }), JsonOptions) };
        row.History.Add(history);
        db.EmployeeSalaryAssignmentHistories.Add(history);
    }
    private static (ResultStatus status, string message, IReadOnlyList<ValidationError>? errors)? ValidateRequest(EmployeeSalaryAssignmentRequest r) { if (r.EffectiveTo is { } to && to < r.EffectiveFrom) return (ResultStatus.ValidationFailed, "EffectiveTo cannot be earlier than EffectiveFrom.", [new("effectiveTo", "EffectiveTo cannot be earlier than EffectiveFrom.")]); if (r.AnnualCtc is < 0 || r.MonthlyCtc is < 0) return (ResultStatus.ValidationFailed, "CTC values cannot be negative.", null); if (string.IsNullOrWhiteSpace(r.CurrencyCode) || r.CurrencyCode.Trim().Length != 3) return (ResultStatus.ValidationFailed, "CurrencyCode must be a three-letter code.", null); return null; }
    private static (ResultStatus status, string message, IReadOnlyList<ValidationError>? errors)? ValidateComponents(IEnumerable<EmployeeSalaryComponentRequest> requests, SalaryStructureVersion version) { var list = requests.ToList(); if (list.Select(x => x.SalaryStructureComponentId).Distinct().Count() != list.Count) return (ResultStatus.ValidationFailed, "Duplicate employee salary overrides are not allowed.", null); foreach (var r in list) { var sc = version.Components.FirstOrDefault(x => x.Id == r.SalaryStructureComponentId && x.IsActive); var error = sc is null ? (ResultStatus.ValidationFailed, "The Salary Structure component is invalid.", (IReadOnlyList<ValidationError>?)null) : ValidateComponent(r, sc); if (error is not null) return error; } return null; }
    private static (ResultStatus status, string message, IReadOnlyList<ValidationError>? errors)? ValidateComponent(EmployeeSalaryComponentRequest r, SalaryStructureComponent sc) { if (!sc.IsEditableAtEmployeeLevel) return (ResultStatus.Forbidden, "This Salary Structure component does not allow employee-level overrides.", null); if (r.EffectiveTo is { } to && to < r.EffectiveFrom) return (ResultStatus.ValidationFailed, "Override EffectiveTo cannot be earlier than EffectiveFrom.", null); if (sc.CalculationType == SalaryStructureCalculationType.FixedAmount && (r.OverrideValue is null || r.OverrideValue < 0 || r.OverridePercentage is not null || r.OverrideFormula is not null)) return (ResultStatus.ValidationFailed, "A fixed amount override requires a non-negative value.", null); if (sc.CalculationType == SalaryStructureCalculationType.Percentage && (r.OverridePercentage is null || r.OverridePercentage < 0 || r.OverridePercentage > 100 || r.OverrideValue is not null || r.OverrideFormula is not null)) return (ResultStatus.ValidationFailed, "A percentage override requires a value between 0 and 100.", null); if (sc.CalculationType == SalaryStructureCalculationType.Formula && (string.IsNullOrWhiteSpace(r.OverrideFormula) || r.OverrideValue is not null || r.OverridePercentage is not null)) return (ResultStatus.ValidationFailed, "A formula override requires formula text.", null); return null; }
    private static bool ValidPage(PagedQuery q) => q.Page > 0 && q.PageSize is > 0 and <= 200;
    private static EmployeeSalaryAssignmentDto ToDto(EmployeeSalaryAssignment x) => new(x.Id, x.EmployeeId, x.Employee?.EmployeeCode ?? "", $"{x.Employee?.FirstName} {x.Employee?.LastName}".Trim(), x.SalaryStructureId, x.SalaryStructure?.Code ?? "", x.SalaryStructure?.Name ?? "", x.SalaryStructureVersionId, x.EffectiveFrom, x.EffectiveTo, x.AnnualCtc, x.MonthlyCtc, x.CurrencyCode, x.PayFrequency, x.Status, x.ChangeReason, x.Remarks, x.ConcurrencyVersion, x.Components.Where(c => c.IsActive).Select(c => ToComponentDto(c, c.SalaryStructureComponent ?? x.SalaryStructureVersion?.Components.FirstOrDefault(v => v.Id == c.SalaryStructureComponentId))).ToList());
    private static EmployeeSalaryComponentDto ToComponentDto(EmployeeSalaryComponent c, SalaryStructureComponent? sc) => new(c.Id, c.SalaryStructureComponentId, c.SalaryComponentId, sc?.SalaryComponent?.Code ?? "", sc?.SalaryComponent?.Name ?? "", sc?.CalculationType ?? SalaryStructureCalculationType.Manual, sc?.IsEditableAtEmployeeLevel ?? false, c.OverrideValue, c.OverridePercentage, c.OverrideFormula, c.EffectiveFrom, c.EffectiveTo, c.IsActive, c.Remarks);
}
