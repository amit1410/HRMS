using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class PayrollPeriodService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock) : IPayrollPeriodService
{
    public async Task<Result<PagedResult<PayrollPeriodDto>>> GetAsync(PayrollPeriodQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PagedResult<PayrollPeriodDto>>.Unauthorized("No authenticated tenant.");
        if (query.Page < 1 || query.PageSize is < 1 or > PagedQuery.MaxPageSize) return Result<PagedResult<PayrollPeriodDto>>.Invalid("page", "Page values are out of range.");
        var source = db.PayrollPeriods.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (query.Status is { } status) source = source.Where(x => x.Status == status);
        if (query.PeriodType is { } type) source = source.Where(x => x.PeriodType == type);
        var total = await source.CountAsync(ct);
        var rows = await source.OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedResult<PayrollPeriodDto>>.Success(new PagedResult<PayrollPeriodDto>(rows.Select(ToDto).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<PayrollPeriodDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PayrollPeriodDto>.Unauthorized("No authenticated tenant.");
        var row = await db.PayrollPeriods.AsNoTracking().Include(x => x.Runs).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        return row is null ? Result<PayrollPeriodDto>.NotFound("Payroll period not found.") : Result<PayrollPeriodDto>.Success(ToDto(row));
    }

    public async Task<Result<PayrollPeriodDto>> CreateAsync(PayrollPeriodRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PayrollPeriodDto>.Unauthorized("No authenticated tenant.");
        var validation = Validate(request); if (validation is not null) return Result<PayrollPeriodDto>.Failure(validation.Value.status, validation.Value.message, validation.Value.errors);
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.PayrollPeriods.AnyAsync(x => x.TenantId == tenantId && x.Code == code, ct)) return Result<PayrollPeriodDto>.Conflict("A payroll period with this code already exists in this tenant.");
        if (request.IsActive && await db.PayrollPeriods.AnyAsync(x => x.TenantId == tenantId && x.IsActive && x.StartDate <= request.EndDate && request.StartDate <= x.EndDate, ct)) return Result<PayrollPeriodDto>.Conflict("The payroll period overlaps an existing active period.");
        var row = new PayrollPeriod { Id = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = request.Name.Trim(), PeriodType = request.PeriodType, StartDate = request.StartDate, EndDate = request.EndDate, PayDate = request.PayDate, FiscalYear = request.FiscalYear, PeriodNumber = request.PeriodNumber, IsActive = request.IsActive };
        db.PayrollPeriods.Add(row); AddHistory(row, "Created");
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { return Result<PayrollPeriodDto>.Conflict("The payroll period could not be created because it conflicts with existing data."); }
        return Result<PayrollPeriodDto>.Success(ToDto(row), "Payroll period created.");
    }

    public async Task<Result<PayrollPeriodDto>> UpdateAsync(Guid id, PayrollPeriodRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PayrollPeriodDto>.Unauthorized("No authenticated tenant.");
        var validation = Validate(request); if (validation is not null) return Result<PayrollPeriodDto>.Failure(validation.Value.status, validation.Value.message, validation.Value.errors);
        var row = await db.PayrollPeriods.Include(x => x.Runs).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (row is null) return Result<PayrollPeriodDto>.NotFound("Payroll period not found.");
        if (row.Status is PayrollPeriodStatus.Closed or PayrollPeriodStatus.Locked) return Result<PayrollPeriodDto>.Conflict("Closed or locked payroll periods cannot be edited.");
        if (request.ExpectedConcurrencyVersion is { } expected && expected != row.ConcurrencyVersion) return Result<PayrollPeriodDto>.Conflict("The payroll period was changed by another user.");
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.PayrollPeriods.AnyAsync(x => x.TenantId == tenantId && x.Id != id && x.Code == code, ct)) return Result<PayrollPeriodDto>.Conflict("A payroll period with this code already exists in this tenant.");
        if (request.IsActive && await db.PayrollPeriods.AnyAsync(x => x.TenantId == tenantId && x.Id != id && x.IsActive && x.StartDate <= request.EndDate && request.StartDate <= x.EndDate, ct)) return Result<PayrollPeriodDto>.Conflict("The payroll period overlaps an existing active period.");
        row.Code = code; row.Name = request.Name.Trim(); row.PeriodType = request.PeriodType; row.StartDate = request.StartDate; row.EndDate = request.EndDate; row.PayDate = request.PayDate; row.FiscalYear = request.FiscalYear; row.PeriodNumber = request.PeriodNumber; row.IsActive = request.IsActive; row.ConcurrencyVersion++; AddHistory(row, "Updated");
        await db.SaveChangesAsync(ct); return Result<PayrollPeriodDto>.Success(ToDto(row), "Payroll period updated.");
    }

    public async Task<Result<PayrollPeriodDto>> TransitionAsync(Guid id, string action, int? expectedVersion, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PayrollPeriodDto>.Unauthorized("No authenticated tenant.");
        var row = await db.PayrollPeriods.Include(x => x.Runs).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (row is null) return Result<PayrollPeriodDto>.NotFound("Payroll period not found.");
        if (expectedVersion is { } expected && expected != row.ConcurrencyVersion) return Result<PayrollPeriodDto>.Conflict("The payroll period was changed by another user.");
        var target = action.ToLowerInvariant() switch { "open" => PayrollPeriodStatus.Open, "close" => PayrollPeriodStatus.Closed, "lock" => PayrollPeriodStatus.Locked, _ => (PayrollPeriodStatus?)null };
        if (target is null || !ValidTransition(row.Status, target.Value)) return Result<PayrollPeriodDto>.Conflict("The payroll period transition is not allowed.");
        row.Status = target.Value; row.ConcurrencyVersion++; AddHistory(row, target.Value.ToString()); await db.SaveChangesAsync(ct);
        return Result<PayrollPeriodDto>.Success(ToDto(row), $"Payroll period {target.Value.ToString().ToLowerInvariant()}.");
    }

    public async Task<Result<IReadOnlyList<PayrollPeriodHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || !await db.PayrollPeriods.AnyAsync(x => x.TenantId == tenantId && x.Id == id, ct)) return Result<IReadOnlyList<PayrollPeriodHistoryDto>>.NotFound("Payroll period not found.");
        var rows = await db.PayrollPeriodHistories.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollPeriodId == id).OrderByDescending(x => x.ChangedAtUtc).Select(x => new PayrollPeriodHistoryDto(x.Id, x.PayrollPeriodId, x.ChangeType, x.Status, x.SnapshotJson, x.ActorUserId, x.ChangedAtUtc)).ToListAsync(ct);
        return Result<IReadOnlyList<PayrollPeriodHistoryDto>>.Success(rows);
    }

    private void AddHistory(PayrollPeriod row, string change) => db.PayrollPeriodHistories.Add(new PayrollPeriodHistory { Id = Guid.NewGuid(), TenantId = row.TenantId, PayrollPeriodId = row.Id, ActorUserId = tenant.UserId, ChangeType = change, ChangedAtUtc = clock.GetUtcNow().UtcDateTime, Status = row.Status, SnapshotJson = JsonSerializer.Serialize(new { row.Code, row.Name, row.StartDate, row.EndDate, row.PayDate, row.PeriodType }) });
    private static PayrollPeriodDto ToDto(PayrollPeriod row) => new(row.Id, row.Code, row.Name, row.PeriodType, row.StartDate, row.EndDate, row.PayDate, row.FiscalYear, row.PeriodNumber, row.Status, row.IsActive, row.Runs?.Count ?? 0, row.ConcurrencyVersion);
    private static bool ValidTransition(PayrollPeriodStatus from, PayrollPeriodStatus to) => (from, to) is (PayrollPeriodStatus.Draft, PayrollPeriodStatus.Open) or (PayrollPeriodStatus.Open, PayrollPeriodStatus.Closed) or (PayrollPeriodStatus.Closed, PayrollPeriodStatus.Locked);
    private static (ResultStatus status, string message, IReadOnlyList<ValidationError> errors)? Validate(PayrollPeriodRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Code)) return (ResultStatus.ValidationFailed, "Period code is required.", [new("code", "Code is required.")]);
        if (string.IsNullOrWhiteSpace(r.Name)) return (ResultStatus.ValidationFailed, "Period name is required.", [new("name", "Name is required.")]);
        if (r.StartDate > r.EndDate) return (ResultStatus.ValidationFailed, "Start date cannot be later than end date.", [new("endDate", "End date is invalid.")]);
        if (r.PayDate < r.StartDate) return (ResultStatus.ValidationFailed, "Pay date cannot be before the period start.", [new("payDate", "Pay date is invalid.")]);
        return null;
    }
}
