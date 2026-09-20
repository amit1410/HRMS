using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class PayrollRunService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock, IPayrollApprovalGuard? approvalGuard = null) : IPayrollRunService
{
    public async Task<Result<PagedResult<PayrollRunDto>>> GetAsync(PayrollRunQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PagedResult<PayrollRunDto>>.Unauthorized("No authenticated tenant.");
        if (query.Page < 1 || query.PageSize is < 1 or > PagedQuery.MaxPageSize) return Result<PagedResult<PayrollRunDto>>.Invalid("page", "Page values are out of range.");
        var source = db.PayrollRuns.AsNoTracking().Include(x => x.PayrollPeriod).Where(x => x.TenantId == tenantId);
        if (query.PayrollPeriodId is { } periodId) source = source.Where(x => x.PayrollPeriodId == periodId);
        if (query.Status is { } status) source = source.Where(x => x.Status == status);
        if (query.RunType is { } type) source = source.Where(x => x.RunType == type);
        var total = await source.CountAsync(ct);
        var rows = await source.OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedResult<PayrollRunDto>>.Success(new PagedResult<PayrollRunDto>(rows.Select(x => ToDto(x, [])).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<PayrollRunDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PayrollRunDto>.Unauthorized("No authenticated tenant.");
        var row = await db.PayrollRuns.AsNoTracking().Include(x => x.PayrollPeriod).Include(x => x.Employees).ThenInclude(x => x.Employee).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        return row is null ? Result<PayrollRunDto>.NotFound("Payroll run not found.") : Result<PayrollRunDto>.Success(ToDto(row, row.Employees));
    }

    public async Task<Result<PayrollRunDto>> CreateAsync(PayrollRunRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PayrollRunDto>.Unauthorized("No authenticated tenant.");
        var period = await db.PayrollPeriods.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.PayrollPeriodId, ct);
        if (period is null) return Result<PayrollRunDto>.NotFound("Payroll period not found.");
        if (period.Status is PayrollPeriodStatus.Closed or PayrollPeriodStatus.Locked || !period.IsActive) return Result<PayrollRunDto>.Conflict("A closed, locked, or inactive period cannot receive a new payroll run.");
        var ordinal = await db.PayrollRuns.CountAsync(x => x.TenantId == tenantId && x.PayrollPeriodId == period.Id, ct) + 1;
        var row = new PayrollRun { Id = Guid.NewGuid(), TenantId = tenantId, PayrollPeriodId = period.Id, RunNumber = $"PR-{period.FiscalYear}-{period.PeriodNumber:D2}-{ordinal:D3}", RunType = request.RunType, Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim() };
        db.PayrollRuns.Add(row); AddHistory(row, PayrollRunHistoryChangeType.Created, null); await db.SaveChangesAsync(ct);
        row.PayrollPeriod = period; return Result<PayrollRunDto>.Success(ToDto(row, []), "Payroll run created.");
    }

    public async Task<Result<PayrollRunDto>> PrepareAsync(Guid id, bool rebuild, CancellationToken ct = default)
    {
        var loaded = await LoadAsync(id, ct); if (!loaded.ok) return Result<PayrollRunDto>.Failure(loaded.status, loaded.message);
        var row = loaded.row!; var period = row.PayrollPeriod!;
        if (row.Status is not (PayrollRunStatus.Draft or PayrollRunStatus.Prepared)) return Result<PayrollRunDto>.Conflict("Only draft or prepared runs may build a population.");
        if (!rebuild && row.Status == PayrollRunStatus.Prepared) return Result<PayrollRunDto>.Conflict("The run is already prepared; request an explicit rebuild.");
        if (row.Employees.Count > 0) db.PayrollRunEmployees.RemoveRange(row.Employees);
        var employees = await db.Employees.AsNoTracking().Where(x => x.TenantId == row.TenantId).OrderBy(x => x.EmployeeCode).ThenBy(x => x.Id).ToListAsync(ct);
        var assignments = await db.EmployeeSalaryAssignments.AsNoTracking().Where(x => x.TenantId == row.TenantId && x.Status == EmployeeSalaryAssignmentStatus.Active && x.EffectiveFrom <= period.EndDate && (x.EffectiveTo == null || period.EndDate <= x.EffectiveTo)).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.CreatedDate).ToListAsync(ct);
        var records = new List<PayrollRunEmployee>();
        foreach (var employee in employees)
        {
            var assignment = assignments.FirstOrDefault(x => x.EmployeeId == employee.Id);
            var eligible = employee.Status == EmployeeStatus.Active && employee.DateOfJoining <= period.EndDate && (employee.DateOfLeaving is null || employee.DateOfLeaving >= period.StartDate) && assignment is not null;
            records.Add(new PayrollRunEmployee { Id = Guid.NewGuid(), TenantId = row.TenantId, PayrollRunId = row.Id, EmployeeId = employee.Id, EmployeeSalaryAssignmentId = assignment?.Id, SalaryStructureId = assignment?.SalaryStructureId, SalaryStructureVersionId = assignment?.SalaryStructureVersionId, EmploymentSnapshotDate = period.EndDate, IsEligible = eligible, Status = eligible ? PayrollRunEmployeeStatus.Eligible : PayrollRunEmployeeStatus.Excluded, ExclusionReason = eligible ? null : assignment is null ? "No effective salary assignment" : employee.Status != EmployeeStatus.Active ? "Employee is not active" : "Employee is not employed during the period" });
        }
        db.PayrollRunEmployees.AddRange(records); row.Status = PayrollRunStatus.Prepared; row.EmployeeCount = records.Count; row.StartedAtUtc ??= clock.GetUtcNow().UtcDateTime; row.StartedByUserId ??= tenant.UserId; row.ConcurrencyVersion++; AddHistory(row, rebuild ? PayrollRunHistoryChangeType.PopulationRebuilt : PayrollRunHistoryChangeType.PopulationGenerated, null); AddHistory(row, PayrollRunHistoryChangeType.Prepared, null); await db.SaveChangesAsync(ct);
        row.Employees = records; return Result<PayrollRunDto>.Success(ToDto(row, records), "Payroll population prepared.");
    }

    public async Task<Result<PayrollRunDto>> TransitionAsync(Guid id, PayrollRunStatus target, CancellationToken ct = default)
    {
        var loaded = await LoadAsync(id, ct); if (!loaded.ok) return Result<PayrollRunDto>.Failure(loaded.status, loaded.message);
        var row = loaded.row!;
        if (!ValidTransition(row.Status, target)) return Result<PayrollRunDto>.Conflict("The payroll run transition is not allowed.");
        if (target is PayrollRunStatus.Approved or PayrollRunStatus.Finalized)
        {
            var guard = await (approvalGuard ?? new PayrollApprovalGuard(db, tenant)).ValidateAsync(row.StartedByUserId, target == PayrollRunStatus.Approved ? "approve" : "finalize", ct: ct);
            if (!guard.Succeeded) return Result<PayrollRunDto>.Failure(guard.Status, guard.Message, guard.Errors);
        }
        row.Status = target; row.ConcurrencyVersion++; if (target == PayrollRunStatus.Processing) { row.LockedAtUtc = clock.GetUtcNow().UtcDateTime; row.LockedByUserId = tenant.UserId; } if (target is PayrollRunStatus.Finalized or PayrollRunStatus.Cancelled) row.CompletedAtUtc = clock.GetUtcNow().UtcDateTime; row.CompletedByUserId = tenant.UserId; AddHistory(row, target == PayrollRunStatus.Approved ? PayrollRunHistoryChangeType.Approved : target == PayrollRunStatus.Finalized ? PayrollRunHistoryChangeType.Finalized : target == PayrollRunStatus.Cancelled ? PayrollRunHistoryChangeType.Cancelled : PayrollRunHistoryChangeType.StatusChanged, null); await db.SaveChangesAsync(ct); return Result<PayrollRunDto>.Success(ToDto(row, row.Employees), $"Payroll run moved to {target}.");
    }

    public async Task<Result<PagedResult<PayrollRunEmployeeDto>>> GetEmployeesAsync(Guid id, PagedQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PagedResult<PayrollRunEmployeeDto>>.Unauthorized("No authenticated tenant.");
        if (query.Page < 1 || query.PageSize is < 1 or > PagedQuery.MaxPageSize) return Result<PagedResult<PayrollRunEmployeeDto>>.Invalid("page", "Page values are out of range.");
        if (!await db.PayrollRuns.AnyAsync(x => x.TenantId == tenantId && x.Id == id, ct)) return Result<PagedResult<PayrollRunEmployeeDto>>.NotFound("Payroll run not found.");
        var source = db.PayrollRunEmployees.AsNoTracking().Include(x => x.Employee).Where(x => x.TenantId == tenantId && x.PayrollRunId == id); var total = await source.CountAsync(ct); var rows = await source.OrderBy(x => x.Employee!.EmployeeCode).ThenBy(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct); return Result<PagedResult<PayrollRunEmployeeDto>>.Success(new PagedResult<PayrollRunEmployeeDto>(rows.Select(ToEmployeeDto).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<IReadOnlyList<PayrollRunHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || !await db.PayrollRuns.AnyAsync(x => x.TenantId == tenantId && x.Id == id, ct)) return Result<IReadOnlyList<PayrollRunHistoryDto>>.NotFound("Payroll run not found.");
        var rows = await db.PayrollRunHistories.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == id).OrderByDescending(x => x.ChangedAtUtc).Select(x => new PayrollRunHistoryDto(x.Id, x.PayrollRunId, x.ChangeType, x.Status, x.Reason, x.ActorUserId, x.ChangedAtUtc)).ToListAsync(ct); return Result<IReadOnlyList<PayrollRunHistoryDto>>.Success(rows);
    }

    private async Task<(bool ok, ResultStatus status, string message, PayrollRun? row)> LoadAsync(Guid id, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId) return (false, ResultStatus.Unauthorized, "No authenticated tenant.", null);
        var row = await db.PayrollRuns.Include(x => x.PayrollPeriod).Include(x => x.Employees).ThenInclude(x => x.Employee).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); return row is null ? (false, ResultStatus.NotFound, "Payroll run not found.", null) : (true, ResultStatus.Success, "", row);
    }
    private void AddHistory(PayrollRun row, PayrollRunHistoryChangeType type, string? reason) => db.PayrollRunHistories.Add(new PayrollRunHistory { Id = Guid.NewGuid(), TenantId = row.TenantId, PayrollRunId = row.Id, ActorUserId = tenant.UserId, ChangeType = type, Status = row.Status, Reason = reason, ChangedAtUtc = clock.GetUtcNow().UtcDateTime });
    private static PayrollRunDto ToDto(PayrollRun row, IEnumerable<PayrollRunEmployee> employees) { var list = employees.Select(ToEmployeeDto).ToList(); return new(row.Id, row.PayrollPeriodId, row.PayrollPeriod?.Code ?? string.Empty, row.RunNumber, row.RunType, row.Status, row.StartedAtUtc, row.StartedByUserId, row.EmployeeCount, list.Count(x => x.IsEligible), list.Count(x => !x.IsEligible), row.Notes, row.ConcurrencyVersion, list); }
    private static PayrollRunEmployeeDto ToEmployeeDto(PayrollRunEmployee x) => new(x.Id, x.EmployeeId, x.Employee?.EmployeeCode ?? string.Empty, string.Join(' ', new[] { x.Employee?.FirstName, x.Employee?.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))), x.EmployeeSalaryAssignmentId, x.SalaryStructureId, x.SalaryStructureVersionId, x.EmploymentSnapshotDate, x.IsEligible, x.ExclusionReason, x.Status);
    private static bool ValidTransition(PayrollRunStatus from, PayrollRunStatus to) => (from, to) is (PayrollRunStatus.Prepared, PayrollRunStatus.Processing) or (PayrollRunStatus.Processing, PayrollRunStatus.Calculated) or (PayrollRunStatus.Calculated, PayrollRunStatus.Approved) or (PayrollRunStatus.Approved, PayrollRunStatus.Finalized) or (PayrollRunStatus.Draft, PayrollRunStatus.Cancelled) or (PayrollRunStatus.Prepared, PayrollRunStatus.Cancelled);
}
