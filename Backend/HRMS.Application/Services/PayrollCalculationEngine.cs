using System.Globalization;
using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class PayrollCalculationEngine(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock, IStatutoryPayrollService? statutory = null, ILoanPayrollRecoveryResolver? loanRecovery = null, IReimbursementPayrollResolver? reimbursementResolver = null, IAttendancePayrollSnapshotResolver? attendance = null) : IPayrollCalculationEngine
{
    public async Task<Result<PayrollCalculationSummaryDto>> CalculateAsync(Guid payrollRunId, bool recalculate, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PayrollCalculationSummaryDto>.Unauthorized("No authenticated tenant.");
        var run = await db.PayrollRuns.Include(x => x.PayrollPeriod).Include(x => x.Employees).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == payrollRunId, ct);
        if (run is null) return Result<PayrollCalculationSummaryDto>.NotFound("Payroll run not found.");
        if (run.Status == PayrollRunStatus.Calculated && !recalculate) return Result<PayrollCalculationSummaryDto>.Conflict("The payroll run is already calculated.");
        if (recalculate && run.Status is PayrollRunStatus.Approved or PayrollRunStatus.Finalized) return Result<PayrollCalculationSummaryDto>.Conflict("Approved or finalized payroll runs cannot be recalculated.");
        if (run.Status != PayrollRunStatus.Prepared && !(recalculate && run.Status == PayrollRunStatus.Calculated)) return Result<PayrollCalculationSummaryDto>.Conflict("Only prepared payroll runs may be calculated.");
        var attemptId = Guid.NewGuid();
        var priorResults = await db.PayrollResults.Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.IsCurrent).ToListAsync(ct);
        var priorErrors = await db.PayrollCalculationErrors.Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.IsCurrent).ToListAsync(ct);
        foreach (var old in priorErrors) old.IsCurrent = false;
        var calculationVersion = Math.Max(1, priorResults.Select(x => x.CalculationVersion).DefaultIfEmpty(0).Max() + 1);
        if (recalculate) db.PayrollCalculationHistories.Add(Event(run, attemptId, PayrollCalculationHistoryChangeType.RecalculationRequested, null, "Recalculation requested."));
        run.Status = PayrollRunStatus.Processing; run.LockedAtUtc ??= clock.GetUtcNow().UtcDateTime; run.LockedByUserId ??= tenant.UserId; run.ConcurrencyVersion++;
        db.PayrollCalculationHistories.Add(Event(run, attemptId, PayrollCalculationHistoryChangeType.CalculationStarted, null, "Payroll calculation started."));
        await db.SaveChangesAsync(ct);

        var errors = new List<PayrollCalculationError>(); var calculated = 0; var pendingResults = new List<PayrollResult>();
        foreach (var snapshot in run.Employees.Where(x => x.IsEligible))
        {
            var outcome = await CalculateEmployeeAsync(run, snapshot, attemptId, calculationVersion, ct);
            if (outcome.Error is not null)
            {
                outcome.Error.CalculationAttemptId = attemptId; errors.Add(outcome.Error); db.PayrollCalculationHistories.Add(Event(run, attemptId, PayrollCalculationHistoryChangeType.EmployeeCalculationFailed, snapshot, outcome.Error.Message));
            }
            else
            {
                db.PayrollResults.Add(outcome.Result!); pendingResults.Add(outcome.Result!); calculated++; db.PayrollCalculationHistories.Add(Event(run, attemptId, PayrollCalculationHistoryChangeType.EmployeeCalculated, snapshot, "Employee payroll calculated."));
            }
        }
        if (errors.Count > 0) db.PayrollCalculationErrors.AddRange(errors);
        if (errors.Count == 0) foreach (var pending in pendingResults) { await PersistLoanRecoveriesAsync(pending, ct); await PersistReimbursementSettlementsAsync(pending, ct); await PersistAdjustmentApplicationsAsync(pending, ct); }
        var failed = errors.Count; var allEligible = run.Employees.Count(x => x.IsEligible);
        if (failed == 0) { foreach (var old in priorResults) old.IsCurrent = false; run.Status = PayrollRunStatus.Calculated; run.CompletedAtUtc = clock.GetUtcNow().UtcDateTime; run.CompletedByUserId = tenant.UserId; db.PayrollCalculationHistories.Add(Event(run, attemptId, PayrollCalculationHistoryChangeType.CalculationCompleted, null, recalculate ? "Payroll recalculation completed." : "Payroll calculation completed.")); if (recalculate) db.PayrollCalculationHistories.Add(Event(run, attemptId, PayrollCalculationHistoryChangeType.RecalculationCompleted, null, "Payroll recalculation completed.")); }
        else run.Status = PayrollRunStatus.Prepared;
        run.ConcurrencyVersion++;
        await db.SaveChangesAsync(ct);
        var results = await db.PayrollResults.AsNoTracking().Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.IsCurrent).ToListAsync(ct);
        return Result<PayrollCalculationSummaryDto>.Success(new PayrollCalculationSummaryDto(run.Id, run.Status, run.EmployeeCount, calculated, failed, results.Sum(x => x.GrossEarnings), results.Sum(x => x.TotalDeductions), results.Sum(x => x.NetPay)), failed == 0 ? "Payroll calculation completed." : "Payroll calculation completed with errors.");
    }

    private async Task<(PayrollResult? Result, PayrollCalculationError? Error)> CalculateEmployeeAsync(PayrollRun run, PayrollRunEmployee snapshot, Guid attemptId, int calculationVersion, CancellationToken ct)
    {
        PayrollAttendanceSnapshotContract? attendanceSnapshot = null;
        if (attendance is not null)
        {
            var attendanceResult = await attendance.ResolveAsync(snapshot.EmployeeId, run.PayrollPeriod!.StartDate, run.PayrollPeriod.EndDate, ct);
            if (!attendanceResult.Succeeded) return (null, Error(run, snapshot, attendanceResult.Message.StartsWith("AttendanceSnapshotMissing", StringComparison.Ordinal) ? PayrollCalculationErrorCode.AttendanceSnapshotMissing : PayrollCalculationErrorCode.AttendanceNotFinalized, attendanceResult.Message));
            attendanceSnapshot = attendanceResult.Value;
        }
        if (snapshot.EmployeeSalaryAssignmentId is not Guid assignmentId || snapshot.SalaryStructureVersionId is not Guid versionId) return (null, Error(run, snapshot, PayrollCalculationErrorCode.NoSalaryAssignment, "No salary assignment/version is present in the prepared payroll snapshot."));
        var assignment = await db.EmployeeSalaryAssignments.AsNoTracking().Include(x => x.Employee).FirstOrDefaultAsync(x => x.TenantId == run.TenantId && x.Id == assignmentId, ct);
        var version = await db.SalaryStructureVersions.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == run.TenantId && x.Id == versionId, ct);
        if (assignment is null) return (null, Error(run, snapshot, PayrollCalculationErrorCode.NoSalaryAssignment, "The snapshot salary assignment no longer exists."));
        if (version is null) return (null, Error(run, snapshot, PayrollCalculationErrorCode.NoSalaryStructureVersion, "The snapshot salary structure version no longer exists."));
        if (run.RunType is PayrollRunType.OffCycle or PayrollRunType.Supplementary)
            return await CalculateAdjustmentOnlyAsync(run, snapshot, assignment, versionId, attemptId, calculationVersion, ct);
        var overlappingAssignments = await db.EmployeeSalaryAssignments.AsNoTracking().CountAsync(x => x.TenantId == run.TenantId && x.EmployeeId == snapshot.EmployeeId && x.Status == EmployeeSalaryAssignmentStatus.Active && x.EffectiveFrom <= run.PayrollPeriod!.EndDate && (x.EffectiveTo == null || run.PayrollPeriod.StartDate <= x.EffectiveTo), ct);
        if (overlappingAssignments > 1) return (null, Error(run, snapshot, PayrollCalculationErrorCode.CalculationFailed, "Multiple salary assignments overlap the payroll period; segmented calculation is required."));
        var definitions = await db.SalaryStructureComponents.AsNoTracking().Include(x => x.SalaryComponent).Where(x => x.TenantId == run.TenantId && x.SalaryStructureVersionId == versionId && x.IsActive).OrderBy(x => x.Sequence).ThenBy(x => x.Id).ToListAsync(ct);
        if (definitions.Count == 0) return (null, Error(run, snapshot, PayrollCalculationErrorCode.NoSalaryStructureVersion, "The salary structure version has no active components."));
        var overrides = await db.EmployeeSalaryComponents.AsNoTracking().Where(x => x.TenantId == run.TenantId && x.EmployeeSalaryAssignmentId == assignmentId && x.IsActive && x.EffectiveFrom <= run.PayrollPeriod!.EndDate && (x.EffectiveTo == null || run.PayrollPeriod.EndDate <= x.EffectiveTo)).ToListAsync(ct);
        var values = new Dictionary<Guid, decimal>(); var resultComponents = new List<PayrollResultComponent>();
        foreach (var definition in definitions)
        {
            var component = definition.SalaryComponent; if (component is null) return (null, Error(run, snapshot, PayrollCalculationErrorCode.CalculationFailed, "A salary structure component is missing its salary component."));
            var overrideRow = overrides.FirstOrDefault(x => x.SalaryStructureComponentId == definition.Id);
            var calculation = CalculateValue(definition, component, overrideRow, values, resultComponents, run.PayrollPeriod!, assignment);
            if (calculation.Error is not null) return (null, Error(run, snapshot, calculation.Error.Value.Code, calculation.Error.Value.Message, component.Id));
            var amount = PayrollRoundingPolicy.RoundMoney(calculation.Value);
            values[component.Id] = amount;
            resultComponents.Add(new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = run.TenantId, SalaryComponentId = component.Id, SalaryStructureComponentId = definition.Id, CalculationAttemptId = attemptId, ComponentCode = component.Code, ComponentName = component.Name, ComponentType = component.ComponentType, CalculationType = definition.CalculationType, BaseAmount = calculation.Base, Rate = calculation.Rate, UnproratedAmount = calculation.Value, ProrationFactor = calculation.ProrationFactor, CalculatedAmount = amount, IsEarning = component.ComponentType is SalaryComponentType.Earning or SalaryComponentType.Reimbursement, IsDeduction = component.ComponentType == SalaryComponentType.Deduction, IsEmployerContribution = component.ComponentType == SalaryComponentType.EmployerContribution, IsTaxable = component.IsTaxable, IsProrated = calculation.Prorated, CalculationSequence = definition.Sequence, CalculationSource = calculation.Source, FormulaSnapshot = definition.Formula, CalculationMetadata = JsonSerializer.Serialize(new { assignment.EffectiveFrom, assignment.EffectiveTo, run.PayrollPeriod.StartDate, run.PayrollPeriod.EndDate }) });
        }
        var gross = PayrollRoundingPolicy.RoundMoney(resultComponents.Where(x => x.IsEarning).Sum(x => x.CalculatedAmount)); var deductions = PayrollRoundingPolicy.RoundMoney(resultComponents.Where(x => x.IsDeduction).Sum(x => x.CalculatedAmount)); var net = PayrollRoundingPolicy.RoundMoney(gross - deductions);
        if (net < 0) return (null, Error(run, snapshot, PayrollCalculationErrorCode.NegativeNetPay, "Total deductions exceed gross earnings."));
        var totalDays = run.PayrollPeriod.EndDate.DayNumber - run.PayrollPeriod.StartDate.DayNumber + 1;
        var payableFrom = assignment.EffectiveFrom > run.PayrollPeriod.StartDate ? assignment.EffectiveFrom : run.PayrollPeriod.StartDate;
        if (assignment.Employee?.DateOfJoining > payableFrom) payableFrom = assignment.Employee.DateOfJoining;
        var payableTo = assignment.EffectiveTo is DateOnly end && end < run.PayrollPeriod.EndDate ? end : run.PayrollPeriod.EndDate;
        if (assignment.Employee?.DateOfLeaving is DateOnly leaving && leaving < payableTo) payableTo = leaving;
        var eligibleDays = Math.Max(0, payableTo.DayNumber - payableFrom.DayNumber + 1);
        var result = new PayrollResult { Id = Guid.NewGuid(), TenantId = run.TenantId, PayrollRunId = run.Id, PayrollRunEmployeeId = snapshot.Id, EmployeeId = snapshot.EmployeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = snapshot.SalaryStructureId ?? assignment.SalaryStructureId, SalaryStructureVersionId = versionId, CalculationAttemptId = attemptId, PeriodStartDate = run.PayrollPeriod.StartDate, PeriodEndDate = run.PayrollPeriod.EndDate, EmploymentSnapshotDate = run.PayrollPeriod.EndDate, CalendarDays = totalDays, EligibleDays = eligibleDays, AttendanceSnapshotId = attendanceSnapshot?.SnapshotId, AttendanceVersion = attendanceSnapshot?.Version, AttendanceEligibleDays = attendanceSnapshot?.EligibleDays, AttendancePayableDays = attendanceSnapshot?.PayableDays, AttendanceLopDays = attendanceSnapshot?.LopDays, ProrationFactor = totalDays == 0 ? 0 : (decimal)eligibleDays / totalDays, CalculationDateUtc = clock.GetUtcNow().UtcDateTime, CurrencyCode = assignment.CurrencyCode, GrossEarnings = gross, TotalDeductions = deductions, NetPay = net, EmployerContributions = resultComponents.Where(x => x.IsEmployerContribution).Sum(x => x.CalculatedAmount), CalculationVersion = calculationVersion, CalculatedAtUtc = clock.GetUtcNow().UtcDateTime, CalculatedByUserId = tenant.UserId, Components = resultComponents };
        var adjustmentError = await ApplyAdjustmentsAsync(run, result, attemptId, ct);
        if (adjustmentError is not null) return (null, Error(run, snapshot, PayrollCalculationErrorCode.CalculationFailed, adjustmentError));
        if (statutory is not null)
        {
            var statutoryResult = await statutory.CalculateAsync(result, resultComponents, ct);
            if (!statutoryResult.Succeeded) return (null, Error(run, snapshot, PayrollCalculationErrorCode.StatutoryCalculationFailed, statutoryResult.Message));
            result.TotalDeductions = PayrollRoundingPolicy.RoundMoney(result.TotalDeductions + statutoryResult.Value!.EmployeeAmount);
            result.EmployerContributions = PayrollRoundingPolicy.RoundMoney(result.EmployerContributions + statutoryResult.Value.EmployerAmount);
            result.NetPay = PayrollRoundingPolicy.RoundMoney(result.GrossEarnings - result.TotalDeductions);
            if (result.NetPay < 0) return (null, Error(run, snapshot, PayrollCalculationErrorCode.NegativeNetPay, "Total deductions exceed gross earnings after statutory deductions."));
        }
        if (loanRecovery is not null)
        {
            var recoveries = await loanRecovery.ResolveAsync(snapshot.EmployeeId, run.PayrollPeriod.PayDate, ct);
            foreach (var recovery in recoveries)
            {
                if (recovery.RecoveryPolicy == LoanRecoveryPolicy.DeferInstallment) continue;
                var amount = recovery.TotalDue;
                if (amount > result.NetPay)
                {
                    if (recovery.RecoveryPolicy == LoanRecoveryPolicy.RecoverFullOrFail) return (null, Error(run, snapshot, PayrollCalculationErrorCode.CalculationFailed, $"Loan recovery {recovery.LoanNumber} exceeds available net pay."));
                    amount = PayrollRoundingPolicy.RoundMoney(Math.Max(0m, result.NetPay));
                }
                if (amount <= 0) continue;
                result.Components.Add(new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = run.TenantId, SalaryComponentId = null, SalaryStructureComponentId = null, CalculationAttemptId = attemptId, ComponentCode = $"LOAN-{recovery.LoanNumber}", ComponentName = $"Loan recovery {recovery.LoanNumber} installment {recovery.InstallmentNumber}", ComponentType = SalaryComponentType.Deduction, CalculationType = SalaryStructureCalculationType.Manual, UnproratedAmount = amount, ProrationFactor = 1m, CalculatedAmount = amount, IsDeduction = true, CalculationSequence = 900000 + recovery.InstallmentNumber, CalculationSource = "LoanRecovery", CalculationMetadata = JsonSerializer.Serialize(new { recovery.EmployeeLoanId, recovery.LoanInstallmentId, recovery.PrincipalDue, recovery.InterestDue, recovery.TotalDue, recovery.RecoveryPolicy }) , EmployeeLoanId = recovery.EmployeeLoanId, LoanInstallmentId = recovery.LoanInstallmentId });
                result.TotalDeductions = PayrollRoundingPolicy.RoundMoney(result.TotalDeductions + amount); result.NetPay = PayrollRoundingPolicy.RoundMoney(result.NetPay - amount);
            }
        }
        if (reimbursementResolver is not null)
        {
            var recoveries = await reimbursementResolver.ResolveAsync(snapshot.EmployeeId, run.PayrollPeriod.PayDate, ct);
            foreach (var recovery in recoveries)
            {
                var amount = PayrollRoundingPolicy.RoundMoney(recovery.ApprovedAmount);
                if (amount <= 0) continue;
                if (recovery.TaxableAmount > 0) result.Components.Add(ReimbursementComponent(run, attemptId, recovery, recovery.TaxableAmount, true));
                if (recovery.NonTaxableAmount > 0) result.Components.Add(ReimbursementComponent(run, attemptId, recovery, recovery.NonTaxableAmount, false));
                result.GrossEarnings = PayrollRoundingPolicy.RoundMoney(result.GrossEarnings + amount);
                result.NetPay = PayrollRoundingPolicy.RoundMoney(result.NetPay + amount);
            }
        }
        return (result, null);
    }

    private static PayrollResultComponent ReimbursementComponent(PayrollRun run, Guid attemptId, ReimbursementPayrollRecovery recovery, decimal amount, bool taxable) => new() { Id = Guid.NewGuid(), TenantId = run.TenantId, CalculationAttemptId = attemptId, ComponentCode = $"REIMB-{(taxable ? "TAX" : "NONTAX")}-{recovery.ClaimLineId:N}"[..50], ComponentName = $"{(taxable ? "Taxable" : "Non-taxable")} reimbursement {recovery.ClaimNumber}", ComponentType = SalaryComponentType.Reimbursement, CalculationType = SalaryStructureCalculationType.Manual, UnproratedAmount = amount, ProrationFactor = 1m, CalculatedAmount = amount, IsEarning = true, IsTaxable = taxable, CalculationSequence = 910000, CalculationSource = taxable ? "TaxableReimbursement" : "NonTaxableReimbursement", CalculationMetadata = JsonSerializer.Serialize(new { recovery.ClaimId, recovery.ClaimLineId, recovery.CategoryCode, recovery.SettlementMethod }), ReimbursementClaimId = recovery.ClaimId, ReimbursementClaimLineId = recovery.ClaimLineId };

    private async Task<(PayrollResult? Result, PayrollCalculationError? Error)> CalculateAdjustmentOnlyAsync(PayrollRun run, PayrollRunEmployee snapshot, EmployeeSalaryAssignment assignment, Guid versionId, Guid attemptId, int calculationVersion, CancellationToken ct)
    {
        var totalDays = run.PayrollPeriod!.EndDate.DayNumber - run.PayrollPeriod.StartDate.DayNumber + 1;
        var result = new PayrollResult { Id = Guid.NewGuid(), TenantId = run.TenantId, PayrollRunId = run.Id, PayrollRunEmployeeId = snapshot.Id, EmployeeId = snapshot.EmployeeId, EmployeeSalaryAssignmentId = assignment.Id, SalaryStructureId = snapshot.SalaryStructureId ?? assignment.SalaryStructureId, SalaryStructureVersionId = versionId, CalculationAttemptId = attemptId, PeriodStartDate = run.PayrollPeriod.StartDate, PeriodEndDate = run.PayrollPeriod.EndDate, EmploymentSnapshotDate = run.PayrollPeriod.EndDate, CalendarDays = totalDays, EligibleDays = totalDays, ProrationFactor = 1m, CalculationDateUtc = clock.GetUtcNow().UtcDateTime, CurrencyCode = assignment.CurrencyCode, CalculationVersion = calculationVersion, CalculatedAtUtc = clock.GetUtcNow().UtcDateTime, CalculatedByUserId = tenant.UserId, Components = [] };
        var error = await ApplyAdjustmentsAsync(run, result, attemptId, ct);
        if (error is not null) return (null, Error(run, snapshot, PayrollCalculationErrorCode.CalculationFailed, error));
        if (statutory is not null) { var statutoryResult = await statutory.CalculateAsync(result, result.Components.ToList(), ct); if (!statutoryResult.Succeeded) return (null, Error(run, snapshot, PayrollCalculationErrorCode.StatutoryCalculationFailed, statutoryResult.Message)); result.TotalDeductions = PayrollRoundingPolicy.RoundMoney(result.TotalDeductions + statutoryResult.Value!.EmployeeAmount); result.EmployerContributions = PayrollRoundingPolicy.RoundMoney(statutoryResult.Value.EmployerAmount); result.NetPay = PayrollRoundingPolicy.RoundMoney(result.GrossEarnings - result.TotalDeductions); }
        if (result.NetPay < 0) return (null, Error(run, snapshot, PayrollCalculationErrorCode.NegativeNetPay, "Total deductions exceed gross earnings."));
        return (result, null);
    }

    private async Task<string?> ApplyAdjustmentsAsync(PayrollRun run, PayrollResult result, Guid attemptId, CancellationToken ct)
    {
        var adjustments = await db.PayrollAdjustments.AsNoTracking().Where(x => x.TenantId == run.TenantId && x.EmployeeId == result.EmployeeId && (x.TargetPayrollRunId == run.Id || (x.TargetPayrollRunId == null && x.PayrollPeriodId == run.PayrollPeriodId)) && (x.Status == PayrollAdjustmentStatus.Approved || x.Status == PayrollAdjustmentStatus.Scheduled) && !db.PayrollAdjustmentApplications.Any(a => a.TenantId == run.TenantId && a.PayrollAdjustmentId == x.Id && a.PayrollRunId == run.Id)).OrderBy(x => x.CreatedDate).ToListAsync(ct);
        foreach (var adjustment in adjustments)
        {
            var amount = PayrollRoundingPolicy.RoundMoney(adjustment.Amount - adjustment.AppliedAmount); if (amount <= 0) continue;
            var deduction = adjustment.Direction == PayrollAdjustmentDirection.Deduction;
            if (deduction && amount > result.GrossEarnings - result.TotalDeductions) return $"Adjustment {adjustment.AdjustmentNumber} would create negative net pay.";
            result.Components.Add(new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = run.TenantId, CalculationAttemptId = attemptId, ComponentCode = adjustment.ComponentCode[..Math.Min(100, adjustment.ComponentCode.Length)], ComponentName = adjustment.Description, ComponentType = deduction ? SalaryComponentType.Deduction : SalaryComponentType.Earning, CalculationType = SalaryStructureCalculationType.Manual, UnproratedAmount = amount, ProrationFactor = 1m, CalculatedAmount = amount, IsEarning = !deduction, IsDeduction = deduction, IsTaxable = adjustment.TaxTreatment is PayrollAdjustmentTaxTreatment.Taxable or PayrollAdjustmentTaxTreatment.RecalculateAccordingToPayrollRules, CalculationSequence = 920000 + result.Components.Count, CalculationSource = "PayrollAdjustment", CalculationMetadata = JsonSerializer.Serialize(new { adjustment.Id, adjustment.AdjustmentNumber, adjustment.SourceType, adjustment.SourceId, adjustment.TaxTreatment, adjustment.StatutoryTreatment }) });
            if (deduction) result.TotalDeductions = PayrollRoundingPolicy.RoundMoney(result.TotalDeductions + amount); else result.GrossEarnings = PayrollRoundingPolicy.RoundMoney(result.GrossEarnings + amount);
            result.NetPay = PayrollRoundingPolicy.RoundMoney(result.GrossEarnings - result.TotalDeductions);
        }
        return null;
    }

    private async Task PersistAdjustmentApplicationsAsync(PayrollResult result, CancellationToken ct)
    {
        foreach (var component in result.Components.Where(x => x.CalculationSource == "PayrollAdjustment" && x.CalculatedAmount > 0))
        {
            using var document = JsonDocument.Parse(component.CalculationMetadata ?? "{}"); if (!document.RootElement.TryGetProperty("Id", out var idProperty) || !Guid.TryParse(idProperty.GetString(), out var adjustmentId)) continue;
            if (await db.PayrollAdjustmentApplications.AnyAsync(x => x.TenantId == result.TenantId && x.PayrollAdjustmentId == adjustmentId && x.PayrollRunId == result.PayrollRunId, ct)) continue;
            var adjustment = await db.PayrollAdjustments.FirstOrDefaultAsync(x => x.TenantId == result.TenantId && x.Id == adjustmentId, ct); if (adjustment is null) continue;
            db.PayrollAdjustmentApplications.Add(new PayrollAdjustmentApplication { Id = Guid.NewGuid(), TenantId = result.TenantId, PayrollAdjustmentId = adjustmentId, PayrollRunId = result.PayrollRunId, PayrollResultId = result.Id, AppliedAmount = component.CalculatedAmount, AppliedDate = result.PeriodEndDate });
            var previous = adjustment.Status; adjustment.AppliedAmount = PayrollRoundingPolicy.RoundMoney(adjustment.AppliedAmount + component.CalculatedAmount); adjustment.AppliedAtUtc = clock.GetUtcNow().UtcDateTime; adjustment.Status = adjustment.AppliedAmount >= adjustment.Amount ? PayrollAdjustmentStatus.Applied : PayrollAdjustmentStatus.PartiallyApplied; adjustment.ConcurrencyVersion++;
            db.PayrollAdjustmentHistories.Add(new PayrollAdjustmentHistory { Id = Guid.NewGuid(), TenantId = result.TenantId, PayrollAdjustmentId = adjustmentId, EventType = adjustment.Status == PayrollAdjustmentStatus.Applied ? PayrollAdjustmentHistoryEventType.Applied : PayrollAdjustmentHistoryEventType.PartialApplication, PreviousStatus = previous, NewStatus = adjustment.Status, Amount = component.CalculatedAmount, ActorUserId = tenant.UserId, OccurredAtUtc = clock.GetUtcNow().UtcDateTime });
        }
    }

    private async Task PersistLoanRecoveriesAsync(PayrollResult result, CancellationToken ct)
    {
        foreach (var component in result.Components.Where(x => x.LoanInstallmentId.HasValue && x.EmployeeLoanId.HasValue && x.CalculatedAmount > 0))
        {
            var installment = await db.LoanInstallments.FirstOrDefaultAsync(x => x.TenantId == result.TenantId && x.Id == component.LoanInstallmentId.Value, ct);
            var loan = await db.EmployeeLoans.FirstOrDefaultAsync(x => x.TenantId == result.TenantId && x.Id == component.EmployeeLoanId.Value, ct);
            if (installment is null || loan is null || await db.LoanRepayments.AnyAsync(x => x.TenantId == result.TenantId && x.LoanInstallmentId == installment.Id && x.PayrollResultId == result.Id, ct)) continue;
            var remainingPrincipal = Math.Max(0m, installment.PrincipalAmount - Math.Min(installment.RecoveredAmount, installment.PrincipalAmount)); var remainingInterest = Math.Max(0m, installment.InterestAmount - Math.Max(0m, installment.RecoveredAmount - installment.PrincipalAmount)); var interest = Math.Min(component.CalculatedAmount, remainingInterest); var principal = component.CalculatedAmount - interest;
            db.LoanRepayments.Add(new LoanRepayment { Id = Guid.NewGuid(), TenantId = result.TenantId, EmployeeLoanId = loan.Id, LoanInstallmentId = installment.Id, Amount = component.CalculatedAmount, PrincipalAmount = principal, InterestAmount = interest, RepaymentType = LoanRepaymentType.Payroll, PaymentDate = result.PeriodEndDate, SourceType = "PayrollResult", PayrollRunId = result.PayrollRunId, PayrollResultId = result.Id, CreatedByUserId = tenant.UserId ?? Guid.Empty });
            installment.RecoveredAmount = PayrollRoundingPolicy.RoundMoney(installment.RecoveredAmount + component.CalculatedAmount); installment.PayrollRunId = result.PayrollRunId; installment.PayrollResultId = result.Id; installment.RecoveredAtUtc = clock.GetUtcNow().UtcDateTime; installment.Status = installment.RecoveredAmount >= installment.InstallmentAmount ? LoanInstallmentStatus.Recovered : LoanInstallmentStatus.PartiallyRecovered;
            loan.OutstandingPrincipal = Math.Max(0m, loan.OutstandingPrincipal - principal); loan.OutstandingInterest = Math.Max(0m, loan.OutstandingInterest - interest); loan.OutstandingTotal = PayrollRoundingPolicy.RoundMoney(loan.OutstandingPrincipal + loan.OutstandingInterest); loan.ConcurrencyVersion++;
            db.LoanHistories.Add(new LoanHistory { Id = Guid.NewGuid(), TenantId = result.TenantId, EmployeeLoanId = loan.Id, EventType = LoanHistoryEventType.PayrollRecovery, PreviousStatus = loan.Status, NewStatus = loan.Status, Amount = component.CalculatedAmount, SourceType = "PayrollResult", SourceId = result.Id, ActorUserId = tenant.UserId, OccurredAtUtc = clock.GetUtcNow().UtcDateTime });
        }
    }

    private async Task PersistReimbursementSettlementsAsync(PayrollResult result, CancellationToken ct)
    {
        if (reimbursementResolver is null) return;
        foreach (var component in result.Components.Where(x => x.ReimbursementClaimLineId.HasValue && x.CalculatedAmount > 0).GroupBy(x => x.ReimbursementClaimLineId!.Value).Select(x => x.First()))
        {
            var line = await db.ReimbursementClaimLines.Include(x => x.Claim).FirstOrDefaultAsync(x => x.TenantId == result.TenantId && x.Id == component.ReimbursementClaimLineId.Value, ct);
            if (line?.Claim is null || await db.ReimbursementSettlements.AnyAsync(x => x.TenantId == result.TenantId && x.ReimbursementClaimLineId == line.Id && x.PayrollResultId == result.Id, ct)) continue;
            var amount = result.Components.Where(x => x.ReimbursementClaimLineId == line.Id).Sum(x => x.CalculatedAmount);
            var taxable = result.Components.Where(x => x.ReimbursementClaimLineId == line.Id && x.IsTaxable).Sum(x => x.CalculatedAmount);
            db.ReimbursementSettlements.Add(new ReimbursementSettlement { Id = Guid.NewGuid(), TenantId = result.TenantId, ReimbursementClaimId = line.ReimbursementClaimId, ReimbursementClaimLineId = line.Id, EmployeeId = line.Claim.EmployeeId, SettlementType = ReimbursementSettlementType.Payroll, Amount = amount, TaxableAmount = taxable, NonTaxableAmount = amount - taxable, SettlementDate = result.PeriodEndDate, PayrollRunId = result.PayrollRunId, PayrollResultId = result.Id, CreatedByUserId = tenant.UserId });
            line.Claim.SettledAmount = PayrollRoundingPolicy.RoundMoney(line.Claim.SettledAmount + amount); if (line.Claim.SettledAmount >= line.Claim.TotalApprovedAmount) { line.Claim.SettledAmount = line.Claim.TotalApprovedAmount; line.Claim.Status = ReimbursementClaimStatus.Settled; line.Claim.SettledAtUtc = clock.GetUtcNow().UtcDateTime; line.Claim.SettledByUserId = tenant.UserId; } line.Status = ReimbursementClaimLineStatus.Settled;
        }
    }

    private static (decimal Value, decimal? Base, decimal? Rate, bool Prorated, string Source, decimal ProrationFactor, (PayrollCalculationErrorCode Code, string Message)? Error) CalculateValue(SalaryStructureComponent definition, SalaryComponent component, EmployeeSalaryComponent? overrideRow, IReadOnlyDictionary<Guid, decimal> values, IReadOnlyList<PayrollResultComponent> prior, PayrollPeriod period, EmployeeSalaryAssignment assignment)
    {
        decimal value; decimal? baseAmount = null; decimal? rate = null; var source = "Structure";
        if (definition.CalculationType == SalaryStructureCalculationType.FixedAmount) { value = overrideRow?.OverrideValue ?? definition.Value ?? 0; source = overrideRow?.OverrideValue is not null ? "EmployeeOverride" : "Structure"; }
        else if (definition.CalculationType == SalaryStructureCalculationType.Percentage) { rate = overrideRow?.OverridePercentage ?? definition.Value; if (rate is null or < 0 or > 100) return Invalid(PayrollCalculationErrorCode.InvalidOverride, "Percentage must be between 0 and 100."); if (definition.PercentageOfComponentId is not Guid baseId || !values.TryGetValue(baseId, out var baseValue)) return Invalid(PayrollCalculationErrorCode.MissingBaseComponent, "Percentage base component is missing or has not been calculated."); baseAmount = baseValue; value = baseValue * rate.Value / 100m; source = overrideRow?.OverridePercentage is not null ? "EmployeeOverride" : "Structure"; }
        else if (definition.CalculationType == SalaryStructureCalculationType.Manual) { value = overrideRow?.OverrideValue ?? definition.Value ?? 0; source = overrideRow?.OverrideValue is not null ? "EmployeeOverride" : "Manual"; }
        else { if (string.IsNullOrWhiteSpace(definition.Formula)) return Invalid(PayrollCalculationErrorCode.InvalidFormula, "Formula is required."); if (!PayrollFormula.TryEvaluate(definition.Formula, values, prior, out value)) return Invalid(PayrollCalculationErrorCode.InvalidFormula, "Formula contains an unsupported token or unresolved component."); source = "Formula"; }
        if (value < 0) return Invalid(PayrollCalculationErrorCode.InvalidOverride, "Negative component values are not supported by the generic engine.");
        var payableFrom = assignment.EffectiveFrom > period.StartDate ? assignment.EffectiveFrom : period.StartDate; if (assignment.Employee?.DateOfJoining > payableFrom) payableFrom = assignment.Employee.DateOfJoining; var payableTo = assignment.EffectiveTo is DateOnly end && end < period.EndDate ? end : period.EndDate; if (assignment.Employee?.DateOfLeaving is DateOnly leaving && leaving < payableTo) payableTo = leaving; var referencedProrated = prior.Any(x => x.IsProrated && ((definition.PercentageOfComponentId is Guid baseId && x.SalaryComponentId == baseId) || (definition.Formula?.Contains(x.ComponentCode, StringComparison.OrdinalIgnoreCase) ?? false))); var prorated = definition.IsProratable && !referencedProrated && (payableFrom > period.StartDate || payableTo < period.EndDate);
        if (prorated) { var totalDays = period.EndDate.DayNumber - period.StartDate.DayNumber + 1; var payableDays = Math.Max(0, payableTo.DayNumber - payableFrom.DayNumber + 1); value *= (decimal)payableDays / totalDays; }
        if (definition.MinimumAmount is decimal min) value = Math.Max(value, min); if (definition.MaximumAmount is decimal max) value = Math.Min(value, max);
        var totalDaysForFactor = period.EndDate.DayNumber - period.StartDate.DayNumber + 1; var payableDaysForFactor = Math.Max(0, payableTo.DayNumber - payableFrom.DayNumber + 1);
        return (value, baseAmount, rate, prorated, source, totalDaysForFactor == 0 ? 0 : (decimal)payableDaysForFactor / totalDaysForFactor, null);
    }

    private static (decimal Value, decimal? Base, decimal? Rate, bool Prorated, string Source, decimal ProrationFactor, (PayrollCalculationErrorCode Code, string Message)? Error) Invalid(PayrollCalculationErrorCode code, string message) => (0, null, null, false, string.Empty, 1m, (code, message));

    private PayrollCalculationError Error(PayrollRun run, PayrollRunEmployee snapshot, PayrollCalculationErrorCode code, string message, Guid? componentId = null) => new() { Id = Guid.NewGuid(), TenantId = run.TenantId, PayrollRunId = run.Id, PayrollRunEmployeeId = snapshot.Id, EmployeeId = snapshot.EmployeeId, ErrorCode = code, Message = message, SalaryComponentId = componentId, CreatedAtUtc = clock.GetUtcNow().UtcDateTime };
    private PayrollCalculationHistory Event(PayrollRun run, Guid attemptId, PayrollCalculationHistoryChangeType type, PayrollRunEmployee? employee, string message) => new() { Id = Guid.NewGuid(), TenantId = run.TenantId, PayrollRunId = run.Id, CalculationAttemptId = attemptId, PayrollRunEmployeeId = employee?.Id, EmployeeId = employee?.EmployeeId, ChangeType = type, Message = message, ChangedAtUtc = clock.GetUtcNow().UtcDateTime, ActorUserId = tenant.UserId };
}

public sealed class PayrollCalculationService(IPayrollCalculationEngine engine, IHrmsDbContext db, ITenantContext tenant) : IPayrollCalculationService
{
    public Task<Result<PayrollCalculationSummaryDto>> CalculateAsync(Guid payrollRunId, CancellationToken ct = default) => engine.CalculateAsync(payrollRunId, false, ct);
    public Task<Result<PayrollCalculationSummaryDto>> RecalculateAsync(Guid payrollRunId, CancellationToken ct = default) => engine.CalculateAsync(payrollRunId, true, ct);
    public async Task<Result<PagedResult<PayrollResultDto>>> GetResultsAsync(Guid payrollRunId, PayrollResultQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PagedResult<PayrollResultDto>>.Unauthorized("No authenticated tenant.");
        var source = db.PayrollResults.AsNoTracking().Include(x => x.Employee).Include(x => x.Components).Where(x => x.TenantId == tenantId && x.PayrollRunId == payrollRunId && x.IsCurrent); if (query.Status is { } status) source = source.Where(x => x.Status == status); var total = await source.CountAsync(ct); var rows = await source.OrderBy(x => x.Employee!.EmployeeCode).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct); return Result<PagedResult<PayrollResultDto>>.Success(new PagedResult<PayrollResultDto>(rows.Select(ToDto).ToList(), query.Page, query.PageSize, total));
    }
    public async Task<Result<PayrollResultDto>> GetResultAsync(Guid payrollRunId, Guid employeeId, CancellationToken ct = default) { var row = await db.PayrollResults.AsNoTracking().Include(x => x.Employee).Include(x => x.Components).FirstOrDefaultAsync(x => x.TenantId == tenant.TenantId && x.PayrollRunId == payrollRunId && x.EmployeeId == employeeId && x.IsCurrent, ct); return row is null ? Result<PayrollResultDto>.NotFound("Payroll result not found.") : Result<PayrollResultDto>.Success(ToDto(row)); }
    public async Task<Result<IReadOnlyList<PayrollCalculationErrorDto>>> GetErrorsAsync(Guid payrollRunId, CancellationToken ct = default) { var rows = await db.PayrollCalculationErrors.AsNoTracking().Include(x => x.Employee).Where(x => x.TenantId == tenant.TenantId && x.PayrollRunId == payrollRunId && x.IsCurrent).OrderBy(x => x.CreatedAtUtc).ToListAsync(ct); return Result<IReadOnlyList<PayrollCalculationErrorDto>>.Success(rows.Select(x => new PayrollCalculationErrorDto(x.Id, x.PayrollRunId, x.PayrollRunEmployeeId, x.EmployeeId, x.CalculationAttemptId, x.Employee?.EmployeeCode ?? string.Empty, x.ErrorCode, x.Message, x.SalaryComponentId, x.CreatedAtUtc, x.IsCurrent)).ToList()); }
    private static PayrollResultDto ToDto(PayrollResult x) => new(x.Id, x.PayrollRunId, x.EmployeeId, x.Employee?.EmployeeCode ?? string.Empty, string.Join(' ', new[] { x.Employee?.FirstName, x.Employee?.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))), x.EmployeeSalaryAssignmentId, x.SalaryStructureId, x.SalaryStructureVersionId, x.CalculationAttemptId, x.PeriodStartDate, x.PeriodEndDate, x.EmploymentSnapshotDate, x.CalendarDays, x.EligibleDays, x.ProrationFactor, x.CurrencyCode, x.GrossEarnings, x.TotalDeductions, x.EmployerContributions, x.NetPay, x.Status, x.CalculationVersion, x.IsCurrent, x.Components.OrderBy(c => c.CalculationSequence).Select(c => new PayrollResultComponentDto(c.Id, c.SalaryComponentId, c.SalaryStructureComponentId, c.CalculationAttemptId, c.ComponentCode, c.ComponentName, c.ComponentType, c.CalculationType, c.BaseAmount, c.Rate, c.UnproratedAmount, c.ProrationFactor, c.CalculatedAmount, c.IsEarning, c.IsDeduction, c.IsProrated, c.CalculationSequence, c.CalculationSource, c.FormulaSnapshot, c.CalculationMetadata)).ToList());
}

internal static class PayrollFormula
{
    public static bool TryEvaluate(string formula, IReadOnlyDictionary<Guid, decimal> values, IReadOnlyList<PayrollResultComponent> prior, out decimal result)
    {
        var names = prior.ToDictionary(x => x.ComponentCode, x => x.CalculatedAmount, StringComparer.OrdinalIgnoreCase); var parser = new Parser(formula, names); return parser.TryParse(out result);
    }
    private sealed class Parser(string text, IReadOnlyDictionary<string, decimal> values)
    {
        private readonly string input = text; private int index;
        public bool TryParse(out decimal value) { value = 0; try { value = Expression(); Skip(); return index == input.Length; } catch { return false; } }
        private decimal Expression() { var value = Term(); while (true) { Skip(); if (Take('+')) value += Term(); else if (Take('-')) value -= Term(); else return value; } }
        private decimal Term() { var value = Factor(); while (true) { Skip(); if (Take('*')) value *= Factor(); else if (Take('/')) { var divisor = Factor(); if (divisor == 0) throw new InvalidOperationException(); value /= divisor; } else return value; } }
        private decimal Factor() { Skip(); if (Take('(')) { var value = Expression(); if (!Take(')')) throw new InvalidOperationException(); return value; } if (Take('-')) return -Factor(); var start = index; while (index < input.Length && (char.IsLetterOrDigit(input[index]) || input[index] == '_' || input[index] == '.')) index++; var token = input[start..index]; if (decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)) return number; if (values.TryGetValue(token, out var named)) return named; throw new InvalidOperationException(); }
        private void Skip() { while (index < input.Length && char.IsWhiteSpace(input[index])) index++; }
        private bool Take(char c) { if (index < input.Length && input[index] == c) { index++; return true; } return false; }
    }
}
