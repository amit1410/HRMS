using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class PayrollAdjustmentService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock, IPayrollRunService payrollRuns, IPayrollCalculationService? calculations = null, IPayrollApprovalGuard? approvalGuard = null) : IPayrollAdjustmentService
{
    public async Task<Result<IReadOnlyList<PayrollAdjustmentReasonDto>>> GetReasonsAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<IReadOnlyList<PayrollAdjustmentReasonDto>>.Unauthorized("No authenticated tenant.");
        var rows = await db.PayrollAdjustmentReasons.AsNoTracking().Where(x => x.TenantId == tid).OrderBy(x => x.Code).ToListAsync(ct);
        return Result<IReadOnlyList<PayrollAdjustmentReasonDto>>.Success(rows.Select(ToReasonDto).ToList());
    }

    public async Task<Result<PayrollAdjustmentReasonDto>> CreateReasonAsync(PayrollAdjustmentReasonRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<PayrollAdjustmentReasonDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)) return Result<PayrollAdjustmentReasonDto>.Invalid("code", "Code and name are required.");
        if (await db.PayrollAdjustmentReasons.AnyAsync(x => x.TenantId == tid && x.Code == request.Code.Trim(), ct)) return Result<PayrollAdjustmentReasonDto>.Conflict("Reason code already exists.");
        var row = new PayrollAdjustmentReason { Id = Guid.NewGuid(), TenantId = tid, Code = request.Code.Trim(), Name = request.Name.Trim(), Description = request.Description?.Trim(), IsActive = request.IsActive, RequiresComment = request.RequiresComment, AllowedAdjustmentTypes = string.IsNullOrWhiteSpace(request.AllowedAdjustmentTypes) ? "[]" : request.AllowedAdjustmentTypes };
        db.PayrollAdjustmentReasons.Add(row); await db.SaveChangesAsync(ct); return Result<PayrollAdjustmentReasonDto>.Success(ToReasonDto(row));
    }

    public async Task<Result<PagedResult<PayrollAdjustmentDto>>> GetAsync(PayrollAdjustmentQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<PagedResult<PayrollAdjustmentDto>>.Unauthorized("No authenticated tenant.");
        if (query.Page < 1 || query.PageSize is < 1 or > PagedQuery.MaxPageSize) return Result<PagedResult<PayrollAdjustmentDto>>.Invalid("page", "Page values are out of range.");
        var source = db.PayrollAdjustments.AsNoTracking().Include(x => x.Employee).Where(x => x.TenantId == tid);
        if (query.EmployeeId is Guid employeeId) source = source.Where(x => x.EmployeeId == employeeId);
        if (query.Status is PayrollAdjustmentStatus status) source = source.Where(x => x.Status == status);
        if (query.AdjustmentType is PayrollAdjustmentType type) source = source.Where(x => x.AdjustmentType == type);
        if (query.Direction is PayrollAdjustmentDirection direction) source = source.Where(x => x.Direction == direction);
        var total = await source.CountAsync(ct); var rows = await source.OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.CreatedDate).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedResult<PayrollAdjustmentDto>>.Success(new PagedResult<PayrollAdjustmentDto>(rows.Select(ToDto).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<PayrollAdjustmentDto>> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<PayrollAdjustmentDto>.Unauthorized("No authenticated tenant.");
        var row = await db.PayrollAdjustments.AsNoTracking().Include(x => x.Employee).FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct);
        return row is null ? Result<PayrollAdjustmentDto>.NotFound("Payroll adjustment not found.") : Result<PayrollAdjustmentDto>.Success(ToDto(row));
    }

    public async Task<Result<PayrollAdjustmentDto>> CreateAsync(PayrollAdjustmentRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<PayrollAdjustmentDto>.Unauthorized("No authenticated tenant.");
        if (request.Amount <= 0) return Result<PayrollAdjustmentDto>.Invalid("amount", "Amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(request.Description)) return Result<PayrollAdjustmentDto>.Invalid("description", "Description is required.");
        if (request.EffectiveDate == default) return Result<PayrollAdjustmentDto>.Invalid("effectiveDate", "Effective date is required.");
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == request.EmployeeId, ct); if (employee is null) return Result<PayrollAdjustmentDto>.NotFound("Employee not found.");
        if (request.ReasonCodeId is Guid reasonId && !await db.PayrollAdjustmentReasons.AnyAsync(x => x.TenantId == tid && x.Id == reasonId && x.IsActive, ct)) return Result<PayrollAdjustmentDto>.Invalid("reasonCodeId", "Active reason code not found.");
        var sourceId = request.SourceReferenceId ?? Guid.NewGuid();
        if (await db.PayrollAdjustments.AnyAsync(x => x.TenantId == tid && x.SourceType == request.SourceType && x.SourceId == sourceId, ct)) return Result<PayrollAdjustmentDto>.Conflict("An adjustment already exists for this source.");
        var number = await NextNumberAsync(tid, request.EffectiveDate.Year, ct);
        var row = new PayrollAdjustment { Id = Guid.NewGuid(), TenantId = tid, EmployeeId = request.EmployeeId, SourceType = request.SourceType.Trim(), SourceId = sourceId, SourceReferenceId = request.SourceReferenceId, AdjustmentNumber = number, AdjustmentType = request.AdjustmentType, ComponentCode = request.ComponentCode.Trim(), Description = request.Description.Trim(), Reason = request.Reason?.Trim(), Amount = decimal.Round(request.Amount, 2, MidpointRounding.AwayFromZero), CurrencyCode = request.CurrencyCode.Trim(), Direction = request.Direction, TaxTreatment = request.TaxTreatment, StatutoryTreatment = request.StatutoryTreatment, SettlementMethod = request.SettlementMethod, EffectiveDate = request.EffectiveDate, PayrollPeriodId = request.PayrollPeriodId, OriginalPayrollRunId = request.OriginalPayrollRunId, OriginalPayrollResultId = request.OriginalPayrollResultId, TargetPayrollRunId = request.TargetPayrollRunId, SalaryComponentId = request.SalaryComponentId, ReasonCodeId = request.ReasonCodeId, Status = PayrollAdjustmentStatus.Draft };
        db.PayrollAdjustments.Add(row); AddHistory(row, PayrollAdjustmentHistoryEventType.Created, null, PayrollAdjustmentStatus.Draft, row.Amount, row.Reason);
        if (request.OriginalPayrollResultId is Guid originalResultId)
        {
            var original = await db.PayrollResults.AsNoTracking().Include(x => x.Components).FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == originalResultId, ct);
            if (original is null) return Result<PayrollAdjustmentDto>.NotFound("Original payroll result not found.");
            var earningDelta = request.Direction == PayrollAdjustmentDirection.Earning ? row.Amount : 0m;
            var deductionDelta = request.Direction == PayrollAdjustmentDirection.Deduction ? row.Amount : 0m;
            db.PayrollCorrectionSnapshots.Add(new PayrollCorrectionSnapshot { Id = Guid.NewGuid(), TenantId = tid, PayrollAdjustmentId = row.Id, OriginalPayrollRunId = original.PayrollRunId, OriginalPayrollResultId = original.Id, OriginalGross = original.GrossEarnings, OriginalDeduction = original.TotalDeductions, OriginalNet = original.NetPay, CorrectedGross = original.GrossEarnings + earningDelta, CorrectedDeduction = original.TotalDeductions + deductionDelta, CorrectedNet = original.NetPay + earningDelta - deductionDelta, DeltaGross = earningDelta, DeltaDeduction = deductionDelta, DeltaNet = earningDelta - deductionDelta, CapturedAtUtc = clock.GetUtcNow().UtcDateTime });
            AddHistory(row, PayrollAdjustmentHistoryEventType.CorrectionSnapshotCaptured, null, PayrollAdjustmentStatus.Draft, row.Amount, "Immutable prior-period correction snapshot captured.");
        }
        await db.SaveChangesAsync(ct); return Result<PayrollAdjustmentDto>.Success(ToDto(row), "Payroll adjustment created.");
    }

    public Task<Result<PayrollAdjustmentDto>> SubmitAsync(Guid id, CancellationToken ct = default) => TransitionAsync(id, PayrollAdjustmentStatus.Submitted, PayrollAdjustmentStatus.Draft, PayrollAdjustmentHistoryEventType.Submitted, null, ct);

    public async Task<Result<PayrollAdjustmentDto>> ApproveAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.PayrollAdjustments.FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.Id == id, ct); if (row is null) return Result<PayrollAdjustmentDto>.NotFound("Payroll adjustment not found.");
        var guard = await (approvalGuard ?? new PayrollApprovalGuard(db, tenant)).ValidateAsync(row.SubmittedByUserId, "approve", ct: ct); if (!guard.Succeeded) return Result<PayrollAdjustmentDto>.Failure(guard.Status, guard.Message, guard.Errors);
        return await TransitionAsync(id, PayrollAdjustmentStatus.Approved, PayrollAdjustmentStatus.Submitted, PayrollAdjustmentHistoryEventType.Approved, null, ct);
    }

    public Task<Result<PayrollAdjustmentDto>> RejectAsync(Guid id, string reason, CancellationToken ct = default) => TransitionAsync(id, PayrollAdjustmentStatus.Rejected, PayrollAdjustmentStatus.Submitted, PayrollAdjustmentHistoryEventType.Rejected, reason, ct);
    public Task<Result<PayrollAdjustmentDto>> CancelAsync(Guid id, string reason, CancellationToken ct = default) => TransitionAsync(id, PayrollAdjustmentStatus.Cancelled, PayrollAdjustmentStatus.Draft, PayrollAdjustmentHistoryEventType.Cancelled, reason, ct, allowSubmitted: true);

    public async Task<Result<IReadOnlyList<PayrollAdjustmentHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<IReadOnlyList<PayrollAdjustmentHistoryDto>>.Unauthorized("No authenticated tenant.");
        if (!await db.PayrollAdjustments.AnyAsync(x => x.TenantId == tid && x.Id == id, ct)) return Result<IReadOnlyList<PayrollAdjustmentHistoryDto>>.NotFound("Payroll adjustment not found.");
        var rows = await db.PayrollAdjustmentHistories.AsNoTracking().Where(x => x.TenantId == tid && x.PayrollAdjustmentId == id).OrderByDescending(x => x.OccurredAtUtc).ToListAsync(ct);
        return Result<IReadOnlyList<PayrollAdjustmentHistoryDto>>.Success(rows.Select(x => new PayrollAdjustmentHistoryDto(x.Id, x.EventType, x.PreviousStatus, x.NewStatus, x.Amount, x.Reason, x.OccurredAtUtc)).ToList());
    }

    public async Task<Result<PayrollRunDto>> CreateOffCycleRunAsync(PayrollOffCycleRunRequest request, CancellationToken ct = default)
    {
        if (request.RunType is not (PayrollRunType.OffCycle or PayrollRunType.Supplementary)) return Result<PayrollRunDto>.Invalid("runType", "Only off-cycle or supplementary runs are supported.");
        if (tenant.TenantId is not Guid tid) return Result<PayrollRunDto>.Unauthorized("No authenticated tenant.");
        var adjustmentIds = request.AdjustmentIds.Distinct().ToArray();
        DbContext? context = db as DbContext;
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            if (context is not null && context.Database.CurrentTransaction is null) transaction = await context.Database.BeginTransactionAsync(ct);
            if (adjustmentIds.Length > 0)
            {
                var rows = await db.PayrollAdjustments.Where(x => x.TenantId == tid && adjustmentIds.Contains(x.Id)).ToListAsync(ct);
                if (rows.Count != adjustmentIds.Length || rows.Any(x => x.Status != PayrollAdjustmentStatus.Approved || x.TargetPayrollRunId is not null)) return Result<PayrollRunDto>.Conflict("Every selected adjustment must be approved, outstanding, and unscheduled.");
            }
            var created = await payrollRuns.CreateAsync(new PayrollRunRequest { PayrollPeriodId = request.PayrollPeriodId, RunType = request.RunType, Notes = request.Notes }, ct); if (!created.Succeeded) return created;
            if (adjustmentIds.Length == 0) { if (transaction is not null) await transaction.CommitAsync(ct); return created; }
            var rowsToSchedule = await db.PayrollAdjustments.Where(x => x.TenantId == tid && adjustmentIds.Contains(x.Id) && x.Status == PayrollAdjustmentStatus.Approved && x.TargetPayrollRunId == null).ToListAsync(ct);
            if (rowsToSchedule.Count != adjustmentIds.Length) return Result<PayrollRunDto>.Conflict("A selected adjustment was changed while the off-cycle run was being created.");
            foreach (var row in rowsToSchedule) { row.TargetPayrollRunId = created.Value!.Id; row.Status = PayrollAdjustmentStatus.Scheduled; row.ConcurrencyVersion++; AddHistory(row, PayrollAdjustmentHistoryEventType.Scheduled, PayrollAdjustmentStatus.Approved, PayrollAdjustmentStatus.Scheduled, row.Amount, "Scheduled for off-cycle payroll."); }
            await db.SaveChangesAsync(ct); if (transaction is not null) await transaction.CommitAsync(ct); return created;
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            if (context is not null) context.ChangeTracker.Clear();
            return Result<PayrollRunDto>.Conflict("An adjustment was changed while the off-cycle run was being created; reload and retry.");
        }
        catch (DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            if (context is not null) context.ChangeTracker.Clear();
            return Result<PayrollRunDto>.Conflict("The off-cycle run could not be committed because another process changed its selected adjustments.");
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            throw;
        }
        finally { if (transaction is not null) await transaction.DisposeAsync(); }
    }

    public Task<Result<PagedResult<PayrollRunDto>>> GetOffCycleRunsAsync(PayrollRunQuery query, CancellationToken ct = default)
    {
        query.RunType = query.RunType ?? PayrollRunType.OffCycle;
        return payrollRuns.GetAsync(query, ct);
    }

    public Task<Result<PayrollRunDto>> PrepareOffCycleRunAsync(Guid id, CancellationToken ct = default) => payrollRuns.PrepareAsync(id, false, ct);
    public Task<Result<PayrollRunDto>> PreviewOffCycleRunAsync(Guid id, CancellationToken ct = default) => payrollRuns.GetByIdAsync(id, ct);

    public Task<Result<PayrollRunDto>> ApproveOffCycleRunAsync(Guid id, CancellationToken ct = default) => payrollRuns.TransitionAsync(id, PayrollRunStatus.Approved, ct);

    public async Task<Result<PayrollRunDto>> ProcessOffCycleRunAsync(Guid id, CancellationToken ct = default)
    {
        var run = await payrollRuns.GetByIdAsync(id, ct); if (!run.Succeeded) return run;
        if (run.Value!.Status == PayrollRunStatus.Draft) { run = await payrollRuns.PrepareAsync(id, false, ct); if (!run.Succeeded) return run; }
        if (run.Value!.Status == PayrollRunStatus.Prepared) { if (calculations is null) return Result<PayrollRunDto>.Conflict("Payroll calculation is unavailable for this process."); var calculated = await calculations.CalculateAsync(id, ct); if (!calculated.Succeeded) return Result<PayrollRunDto>.Failure(calculated.Status, calculated.Message, calculated.Errors); run = await payrollRuns.GetByIdAsync(id, ct); }
        if (!run.Succeeded) return run;
        if (run.Value!.Status == PayrollRunStatus.Calculated) { run = await payrollRuns.TransitionAsync(id, PayrollRunStatus.Approved, ct); if (!run.Succeeded) return run; }
        if (run.Value!.Status == PayrollRunStatus.Approved) return await payrollRuns.TransitionAsync(id, PayrollRunStatus.Finalized, ct);
        return run.Value.Status == PayrollRunStatus.Finalized ? run : Result<PayrollRunDto>.Conflict("The off-cycle run is not ready to process.");
    }

    public async Task<Result<PayrollRunDto>> CancelOffCycleRunAsync(Guid id, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result<PayrollRunDto>.Invalid("reason", "A cancellation reason is required.");
        var run = await payrollRuns.GetByIdAsync(id, ct); if (!run.Succeeded) return run;
        if (run.Value!.Status is not (PayrollRunStatus.Draft or PayrollRunStatus.Prepared)) return Result<PayrollRunDto>.Conflict("Only unprocessed off-cycle runs can be cancelled.");
        var rows = await db.PayrollAdjustments.Where(x => x.TenantId == tenant.TenantId && x.TargetPayrollRunId == id && x.Status == PayrollAdjustmentStatus.Scheduled).ToListAsync(ct);
        foreach (var row in rows) { row.TargetPayrollRunId = null; row.Status = PayrollAdjustmentStatus.Approved; row.ConcurrencyVersion++; AddHistory(row, PayrollAdjustmentHistoryEventType.Cancelled, PayrollAdjustmentStatus.Scheduled, PayrollAdjustmentStatus.Approved, row.Amount, reason.Trim()); }
        await db.SaveChangesAsync(ct); return await payrollRuns.TransitionAsync(id, PayrollRunStatus.Cancelled, ct);
    }

    public async Task<Result<PayrollReversalDto>> RequestReversalAsync(PayrollReversalRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<PayrollReversalDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return Result<PayrollReversalDto>.Invalid("reason", "A reversal reason is required.");
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == request.OriginalPayrollRunId && x.Status == PayrollRunStatus.Finalized, ct); if (run is null) return Result<PayrollReversalDto>.Conflict("Only finalized payroll runs can be reversed.");
        if (await db.PayrollReversals.AnyAsync(x => x.TenantId == tid && x.OriginalPayrollRunId == request.OriginalPayrollRunId, ct)) return Result<PayrollReversalDto>.Conflict("A reversal already exists for this payroll run.");
        var row = new PayrollReversal { Id = Guid.NewGuid(), TenantId = tid, OriginalPayrollRunId = request.OriginalPayrollRunId, OriginalPayrollResultId = request.OriginalPayrollResultId, ReasonCodeId = request.ReasonCodeId, Reason = request.Reason.Trim(), RequestedByUserId = tenant.UserId ?? Guid.Empty, Status = PayrollReversalStatus.Requested };
        db.PayrollReversals.Add(row); await db.SaveChangesAsync(ct); return Result<PayrollReversalDto>.Success(ToReversalDto(row));
    }

    public async Task<Result<PayrollReversalDto>> ApproveReversalAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<PayrollReversalDto>.Unauthorized("No authenticated tenant.");
        var row = await db.PayrollReversals.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct); if (row is null) return Result<PayrollReversalDto>.NotFound("Reversal not found.");
        if (row.Status != PayrollReversalStatus.Requested) return Result<PayrollReversalDto>.Conflict("Reversal is not awaiting approval.");
        var guard = await (approvalGuard ?? new PayrollApprovalGuard(db, tenant)).ValidateAsync(row.RequestedByUserId, "approve", ct: ct); if (!guard.Succeeded) return Result<PayrollReversalDto>.Failure(guard.Status, guard.Message, guard.Errors);
        row.Status = PayrollReversalStatus.Approved; row.ApprovedByUserId = tenant.UserId; await db.SaveChangesAsync(ct); return Result<PayrollReversalDto>.Success(ToReversalDto(row));
    }

    public async Task<Result<PayrollReversalDto>> ProcessReversalAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<PayrollReversalDto>.Unauthorized("No authenticated tenant.");
        var reversal = await db.PayrollReversals.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct); if (reversal is null) return Result<PayrollReversalDto>.NotFound("Reversal not found.");
        if (reversal.Status != PayrollReversalStatus.Approved) return Result<PayrollReversalDto>.Conflict("Only approved reversals can be processed.");
        var original = await db.PayrollRuns.AsNoTracking().Include(x => x.PayrollPeriod).FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == reversal.OriginalPayrollRunId, ct); if (original?.PayrollPeriod is null) return Result<PayrollReversalDto>.NotFound("Original payroll run not found.");
        var components = await db.PayrollResults.AsNoTracking().Where(x => x.TenantId == tid && x.PayrollRunId == original.Id && (reversal.OriginalPayrollResultId == null || x.Id == reversal.OriginalPayrollResultId)).Include(x => x.Components).ToListAsync(ct);
        if (components.Count == 0) return Result<PayrollReversalDto>.Conflict("No original payroll results are available for reversal.");
        var reversalRun = new PayrollRun { Id = Guid.NewGuid(), TenantId = tid, PayrollPeriodId = original.PayrollPeriodId, RunNumber = $"REV-{original.RunNumber}-{id:N}"[..Math.Min(100, $"REV-{original.RunNumber}-{id:N}".Length)], RunType = PayrollRunType.OffCycle, Notes = $"Reversal of {original.RunNumber}: {reversal.Reason}" };
        db.PayrollRuns.Add(reversalRun);
        foreach (var result in components)
            foreach (var component in result.Components.Where(x => x.CalculatedAmount > 0))
            {
                var deduction = component.IsEarning;
                db.PayrollAdjustments.Add(new PayrollAdjustment { Id = Guid.NewGuid(), TenantId = tid, EmployeeId = result.EmployeeId, SourceType = "PayrollReversal", SourceId = component.Id, SourceReferenceId = result.Id, AdjustmentNumber = $"PADJ/REV/{component.Id:N}", AdjustmentType = PayrollAdjustmentType.ManualPayrollCorrection, Direction = deduction ? PayrollAdjustmentDirection.Deduction : PayrollAdjustmentDirection.Earning, ComponentCode = component.ComponentCode, Description = $"Reversal of {component.ComponentName}", Amount = component.CalculatedAmount, CurrencyCode = result.CurrencyCode, EffectiveDate = result.PeriodEndDate, TargetPayrollRunId = reversalRun.Id, OriginalPayrollRunId = original.Id, OriginalPayrollResultId = result.Id, Status = PayrollAdjustmentStatus.Scheduled });
            }
        reversal.ReversalRunId = reversalRun.Id; reversal.Status = PayrollReversalStatus.Processed; reversal.FinalizedAtUtc = clock.GetUtcNow().UtcDateTime; await db.SaveChangesAsync(ct);
        return Result<PayrollReversalDto>.Success(ToReversalDto(reversal));
    }

    private async Task<Result<PayrollAdjustmentDto>> TransitionAsync(Guid id, PayrollAdjustmentStatus next, PayrollAdjustmentStatus required, PayrollAdjustmentHistoryEventType eventType, string? reason, CancellationToken ct, bool allowSubmitted = false)
    {
        if (tenant.TenantId is not Guid tid) return Result<PayrollAdjustmentDto>.Unauthorized("No authenticated tenant.");
        var row = await db.PayrollAdjustments.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct); if (row is null) return Result<PayrollAdjustmentDto>.NotFound("Payroll adjustment not found.");
        var valid = row.Status == required || (allowSubmitted && row.Status == PayrollAdjustmentStatus.Submitted); if (!valid) return Result<PayrollAdjustmentDto>.Conflict("The adjustment transition is not allowed.");
        if (next == PayrollAdjustmentStatus.Rejected && string.IsNullOrWhiteSpace(reason)) return Result<PayrollAdjustmentDto>.Invalid("reason", "A rejection reason is required.");
        var previous = row.Status; row.Status = next; row.Reason = string.IsNullOrWhiteSpace(reason) ? row.Reason : reason.Trim(); row.SubmittedAtUtc = next == PayrollAdjustmentStatus.Submitted ? clock.GetUtcNow().UtcDateTime : row.SubmittedAtUtc; row.SubmittedByUserId = next == PayrollAdjustmentStatus.Submitted ? tenant.UserId : row.SubmittedByUserId; row.ApprovedAtUtc = next == PayrollAdjustmentStatus.Approved ? clock.GetUtcNow().UtcDateTime : row.ApprovedAtUtc; row.ApprovedByUserId = next == PayrollAdjustmentStatus.Approved ? tenant.UserId : row.ApprovedByUserId; row.RejectedAtUtc = next == PayrollAdjustmentStatus.Rejected ? clock.GetUtcNow().UtcDateTime : row.RejectedAtUtc; row.RejectedByUserId = next == PayrollAdjustmentStatus.Rejected ? tenant.UserId : row.RejectedByUserId; row.CancelledAtUtc = next == PayrollAdjustmentStatus.Cancelled ? clock.GetUtcNow().UtcDateTime : row.CancelledAtUtc; row.CancelledByUserId = next == PayrollAdjustmentStatus.Cancelled ? tenant.UserId : row.CancelledByUserId; row.ConcurrencyVersion++; AddHistory(row, eventType, previous, next, row.Amount, row.Reason);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { if (db is DbContext context) context.ChangeTracker.Clear(); return Result<PayrollAdjustmentDto>.Conflict("The adjustment was changed by another request; reload and retry."); }
        return Result<PayrollAdjustmentDto>.Success(ToDto(row));
    }

    private async Task<string> NextNumberAsync(Guid tid, int year, CancellationToken ct)
    {
        // The sequence row carries a concurrency token. Retrying the allocation makes
        // simultaneous requests safe without relying on provider-specific SQL.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                var row = await db.PayrollAdjustmentNumberSequences.FirstOrDefaultAsync(x => x.TenantId == tid && x.Year == year, ct);
                if (row is null)
                {
                    row = new PayrollAdjustmentNumberSequence { Id = Guid.NewGuid(), TenantId = tid, Year = year, NextValue = 2 };
                    db.PayrollAdjustmentNumberSequences.Add(row);
                    await db.SaveChangesAsync(ct);
                    return $"PADJ/{year}/000001";
                }

                var value = row.NextValue;
                row.NextValue++;
                await db.SaveChangesAsync(ct);
                return $"PADJ/{year}/{value:D6}";
            }
            catch (DbUpdateConcurrencyException) when (attempt < 7)
            {
                if (db is DbContext context) context.ChangeTracker.Clear();
            }
            catch (DbUpdateException) when (attempt < 7)
            {
                // Two first writers can race to create the year row. Clear the failed
                // insert and let the next attempt observe the committed sequence.
                if (db is DbContext context) context.ChangeTracker.Clear();
            }
        }

        throw new InvalidOperationException("Unable to allocate a payroll adjustment number after concurrent retries.");
    }

    private void AddHistory(PayrollAdjustment row, PayrollAdjustmentHistoryEventType type, PayrollAdjustmentStatus? previous, PayrollAdjustmentStatus? next, decimal? amount, string? reason) => db.PayrollAdjustmentHistories.Add(new PayrollAdjustmentHistory { Id = Guid.NewGuid(), TenantId = row.TenantId, PayrollAdjustmentId = row.Id, EventType = type, PreviousStatus = previous, NewStatus = next, Amount = amount, Reason = reason, ActorUserId = tenant.UserId, OccurredAtUtc = clock.GetUtcNow().UtcDateTime });
    private static PayrollAdjustmentReasonDto ToReasonDto(PayrollAdjustmentReason x) => new(x.Id, x.Code, x.Name, x.Description, x.IsActive, x.RequiresComment, x.AllowedAdjustmentTypes);
    private static PayrollAdjustmentDto ToDto(PayrollAdjustment x) => new(x.Id, x.EmployeeId, x.Employee?.EmployeeCode ?? string.Empty, x.AdjustmentNumber, x.AdjustmentType, x.SourceType, x.SourceReferenceId, x.EffectiveDate, x.OriginalPayrollRunId, x.OriginalPayrollResultId, x.TargetPayrollRunId, x.ComponentCode, x.Description, x.Amount, x.AppliedAmount, x.Amount - x.AppliedAmount, x.CurrencyCode, x.Direction, x.TaxTreatment, x.StatutoryTreatment, x.SettlementMethod, x.Status, x.Reason);
    private static PayrollReversalDto ToReversalDto(PayrollReversal x) => new(x.Id, x.OriginalPayrollRunId, x.OriginalPayrollResultId, x.ReversalRunId, x.Status, x.Reason, x.CreatedDate, x.FinalizedAtUtc);
}
