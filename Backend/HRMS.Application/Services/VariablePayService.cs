using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class VariablePayService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock) : IVariablePayService
{
    public async Task<Result<IReadOnlyList<VariablePayPlanDto>>> GetPlansAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid id) return Result<IReadOnlyList<VariablePayPlanDto>>.Unauthorized("No authenticated tenant.");
        var rows = await db.VariablePayPlans.AsNoTracking().Include(x => x.Versions).Where(x => x.TenantId == id).OrderBy(x => x.Code).ToListAsync(ct);
        return Result<IReadOnlyList<VariablePayPlanDto>>.Success(rows.Select(ToPlanDto).ToList());
    }

    public async Task<Result<VariablePayPlanDto>> CreatePlanAsync(VariablePayPlanRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid id) return Result<VariablePayPlanDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)) return Result<VariablePayPlanDto>.Invalid("code", "Plan code and name are required.");
        var code = request.Code.Trim();
        if (await db.VariablePayPlans.AnyAsync(x => x.TenantId == id && x.Code == code, ct)) return Result<VariablePayPlanDto>.Conflict("A variable-pay plan with this code already exists.");
        var plan = new VariablePayPlan { Id = Guid.NewGuid(), TenantId = id, Code = code, Name = request.Name.Trim(), Description = request.Description, PlanType = request.PlanType, CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(), IsActive = request.IsActive };
        db.VariablePayPlans.Add(plan); await db.SaveChangesAsync(ct);
        return Result<VariablePayPlanDto>.Success(ToPlanDto(plan));
    }

    public async Task<Result<VariablePayPlanDto>> UpdatePlanAsync(Guid id, VariablePayPlanRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<VariablePayPlanDto>.Unauthorized("No authenticated tenant.");
        var plan = await db.VariablePayPlans.Include(x => x.Versions).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (plan is null) return Result<VariablePayPlanDto>.NotFound("Variable-pay plan not found.");
        plan.Name = request.Name.Trim(); plan.Description = request.Description; plan.IsActive = request.IsActive; plan.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(); plan.ConcurrencyVersion++;
        await db.SaveChangesAsync(ct); return Result<VariablePayPlanDto>.Success(ToPlanDto(plan));
    }

    public async Task<Result<VariablePayPlanDto>> AddVersionAsync(Guid id, VariablePayPlanVersionRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<VariablePayPlanDto>.Unauthorized("No authenticated tenant.");
        var plan = await db.VariablePayPlans.Include(x => x.Versions).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (plan is null) return Result<VariablePayPlanDto>.NotFound("Variable-pay plan not found.");
        var error = ValidateVersion(request); if (error is not null) return Result<VariablePayPlanDto>.Invalid(error);
        if (plan.Versions.Any(x => x.Status == VariablePayPlanVersionStatus.Published && Overlaps(x.EffectiveFrom, x.EffectiveTo, request.EffectiveFrom, request.EffectiveTo))) return Result<VariablePayPlanDto>.Conflict("Published variable-pay plan versions cannot overlap.");
        var version = new VariablePayPlanVersion { Id = Guid.NewGuid(), TenantId = tenantId, VariablePayPlanId = id, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, Status = request.Status, CalculationMethod = request.CalculationMethod, SalaryBasisType = request.SalaryBasisType, Percentage = request.Percentage, FixedAmount = request.FixedAmount, TargetPercentage = request.TargetPercentage, MinimumAmount = request.MinimumAmount, MaximumAmount = request.MaximumAmount, PerformanceMultiplierMinimum = request.PerformanceMultiplierMinimum, PerformanceMultiplierMaximum = request.PerformanceMultiplierMaximum, ProrationMethod = request.ProrationMethod, EligibilityMethod = request.EligibilityMethod, MinimumServiceMonths = request.MinimumServiceMonths, PayoutFrequency = request.PayoutFrequency, PayoutMonth = request.PayoutMonth, TaxTreatment = request.TaxTreatment, TaxablePercentage = request.TaxablePercentage, FinalSettlementTreatment = request.FinalSettlementTreatment, RequiresApproval = request.RequiresApproval, AllowManualOverride = request.AllowManualOverride, PerformanceRatingRequired = request.PerformanceRatingRequired, SelectedSalaryComponentIdsJson = request.SelectedSalaryComponentIdsJson, ApplicabilityJson = request.ApplicabilityJson, Notes = request.Notes };
        db.VariablePayPlanVersions.Add(version); await db.SaveChangesAsync(ct);
        return Result<VariablePayPlanDto>.Success(ToPlanDto(plan));
    }

    public Task<Result<VariablePayAwardDto>> PreviewAsync(Guid employeeId, VariablePayAwardRequest request, CancellationToken ct = default) => CalculateCoreAsync(employeeId, request, false, ct);
    public Task<Result<VariablePayAwardDto>> GenerateAsync(Guid employeeId, VariablePayAwardRequest request, CancellationToken ct = default) => CalculateCoreAsync(employeeId, request, true, ct);

    public async Task<Result<VariablePayBulkGenerationResult>> GenerateBulkAsync(VariablePayBulkGenerationRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<VariablePayBulkGenerationResult>.Unauthorized("No authenticated tenant.");
        var employees = await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.Id).ToListAsync(ct);
        var generated = 0; var skipped = 0; var reasons = new List<string>();
        foreach (var employee in employees)
        {
            var result = await GenerateAsync(employee.Id, new VariablePayAwardRequest { PlanVersionId = request.PlanVersionId, AwardPeriodFrom = request.AwardPeriodFrom, AwardPeriodTo = request.AwardPeriodTo, EligibilityDate = request.EligibilityDate }, ct);
            if (result.Succeeded) generated++; else { skipped++; reasons.Add($"{employee.Id:D}: {result.Message}"); }
        }
        return Result<VariablePayBulkGenerationResult>.Success(new(employees.Count, generated, skipped, reasons));
    }

    public async Task<Result<PagedResult<VariablePayAwardDto>>> GetAwardsAsync(VariablePayAwardQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid id) return Result<PagedResult<VariablePayAwardDto>>.Unauthorized("No authenticated tenant.");
        var page = Math.Max(1, query.Page); var size = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize);
        var source = db.VariablePayAwards.AsNoTracking().Where(x => x.TenantId == id).AsQueryable();
        if (query.EmployeeId is Guid employeeId) source = source.Where(x => x.EmployeeId == employeeId);
        if (query.PlanId is Guid planId) source = source.Where(x => x.VariablePayPlanId == planId);
        if (query.Status is VariablePayAwardStatus status) source = source.Where(x => x.Status == status);
        if (query.PeriodFrom is DateOnly from) source = source.Where(x => x.AwardPeriodFrom >= from);
        if (query.PeriodTo is DateOnly to) source = source.Where(x => x.AwardPeriodTo <= to);
        var total = await source.CountAsync(ct); var rows = await source.OrderByDescending(x => x.AwardPeriodFrom).ThenBy(x => x.AwardNumber).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return Result<PagedResult<VariablePayAwardDto>>.Success(new(rows.Select(ToAwardDto).ToList(), page, size, total));
    }

    public Task<Result<VariablePayAwardDto>> SubmitAsync(Guid id, CancellationToken ct = default) => TransitionAsync(id, VariablePayAwardStatus.Submitted, VariablePayHistoryEventType.AwardSubmitted, null, ct);

    public async Task<Result<VariablePayAwardDto>> ApproveAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<VariablePayAwardDto>.Unauthorized("No authenticated tenant.");
        var award = await db.VariablePayAwards.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); if (award is null) return Result<VariablePayAwardDto>.NotFound("Variable-pay award not found.");
        if (award.Status is not VariablePayAwardStatus.Submitted and not VariablePayAwardStatus.Calculated) return Result<VariablePayAwardDto>.Conflict("Only calculated or submitted awards can be approved.");
        if (award.SubmittedByUserId.HasValue && award.SubmittedByUserId == tenant.UserId) return Result<VariablePayAwardDto>.Forbidden("The maker cannot approve the same award.");
        var previous = award.Status; var version = await db.VariablePayPlanVersions.AsNoTracking().SingleAsync(x => x.TenantId == tenantId && x.Id == award.VariablePayPlanVersionId, ct); var approved = award.ApprovedAmount ?? award.CalculatedAmount; var taxable = version.TaxTreatment switch { VariablePayTaxTreatment.NonTaxable => 0m, VariablePayTaxTreatment.PartiallyTaxable => Round(approved * (version.TaxablePercentage ?? 0m) / 100m), _ => approved }; var nonTaxable = Round(approved - taxable); var now = clock.GetUtcNow().UtcDateTime; db.ClearChangeTracker(); var updated = await db.VariablePayAwards.Where(x => x.TenantId == tenantId && x.Id == id && x.Status == previous).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, VariablePayAwardStatus.Approved).SetProperty(x => x.ApprovedAmount, approved).SetProperty(x => x.TaxableAmount, taxable).SetProperty(x => x.NonTaxableAmount, nonTaxable).SetProperty(x => x.ApprovedAtUtc, now).SetProperty(x => x.ApprovedByUserId, tenant.UserId).SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1), ct); if (updated != 1) return Result<VariablePayAwardDto>.Conflict("Variable-pay award was modified by another operation."); db.VariablePayAwardHistories.Add(new VariablePayAwardHistory { Id = Guid.NewGuid(), TenantId = tenantId, VariablePayAwardId = id, EventType = VariablePayHistoryEventType.AwardApproved, OccurredAtUtc = now, ActorUserId = tenant.UserId, PreviousStatus = previous, NewStatus = VariablePayAwardStatus.Approved, OriginalAmount = award.CalculatedAmount, NewAmount = approved, VariablePayPlanVersionId = award.VariablePayPlanVersionId, Reason = "Award approved." }); await db.SaveChangesAsync(ct); award.ApprovedAmount = approved; award.TaxableAmount = taxable; award.NonTaxableAmount = nonTaxable; award.Status = VariablePayAwardStatus.Approved; award.ApprovedAtUtc = now; award.ApprovedByUserId = tenant.UserId; award.ConcurrencyVersion++; return Result<VariablePayAwardDto>.Success(ToAwardDto(award));
    }

    public Task<Result<VariablePayAwardDto>> RejectAsync(Guid id, string reason, CancellationToken ct = default) => TransitionAsync(id, VariablePayAwardStatus.Rejected, VariablePayHistoryEventType.AwardRejected, reason, ct);

    public async Task<Result<VariablePayAwardDto>> OverrideAsync(Guid id, VariablePayOverrideRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<VariablePayAwardDto>.Unauthorized("No authenticated tenant.");
        if (request.OverrideAmount < 0 || string.IsNullOrWhiteSpace(request.Reason)) return Result<VariablePayAwardDto>.Invalid("override", "A non-negative amount and reason are required.");
        var award = await db.VariablePayAwards.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); if (award is null) return Result<VariablePayAwardDto>.NotFound("Variable-pay award not found.");
        var version = await db.VariablePayPlanVersions.AsNoTracking().SingleAsync(x => x.TenantId == tenantId && x.Id == award.VariablePayPlanVersionId, ct); if (!version.AllowManualOverride) return Result<VariablePayAwardDto>.Forbidden("This plan version does not allow overrides.");
        var previous = award.CalculatedAmount; award.CalculatedAmount = request.OverrideAmount; award.ApprovedAmount = null; award.TaxableAmount = null; award.NonTaxableAmount = null; award.Status = VariablePayAwardStatus.Calculated; award.Reason = request.Reason.Trim(); award.ConcurrencyVersion++; AddHistory(award, VariablePayHistoryEventType.AwardOverridden, VariablePayAwardStatus.Calculated, award.Status, request.Reason.Trim(), previous, request.OverrideAmount); await db.SaveChangesAsync(ct); return Result<VariablePayAwardDto>.Success(ToAwardDto(award));
    }

    public Task<Result<VariablePayAwardDto>> CancelAsync(Guid id, string reason, CancellationToken ct = default) => TransitionAsync(id, VariablePayAwardStatus.Cancelled, VariablePayHistoryEventType.Cancelled, reason, ct);

    public async Task<Result<VariablePayAwardDto>> SettleAsync(Guid id, VariablePaySettlementRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<VariablePayAwardDto>.Unauthorized("No authenticated tenant.");
        var award = await db.VariablePayAwards.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); if (award is null) return Result<VariablePayAwardDto>.NotFound("Variable-pay award not found.");
        if (award.Status is not VariablePayAwardStatus.Approved and not VariablePayAwardStatus.Scheduled and not VariablePayAwardStatus.PartiallyPaid) return Result<VariablePayAwardDto>.Conflict("Only approved awards can be settled.");
        var approved = award.ApprovedAmount ?? award.CalculatedAmount; var outstanding = approved - award.SettledAmount; if (request.Amount <= 0 || request.Amount > outstanding) return Result<VariablePayAwardDto>.Invalid("amount", "Settlement must be positive and cannot exceed outstanding approved amount.");
        var duplicate = await db.VariablePaySettlements.AnyAsync(x => x.TenantId == tenantId && x.VariablePayAwardId == id && x.SettlementType == request.SettlementType && x.PayrollRunId == request.PayrollRunId && x.FinalSettlementId == request.FinalSettlementId, ct); if (duplicate) return Result<VariablePayAwardDto>.Conflict("This settlement has already been recorded.");
        var taxable = TaxAmount(request.Amount, award, approved); var settlement = new VariablePaySettlement { Id = Guid.NewGuid(), TenantId = tenantId, VariablePayAwardId = id, EmployeeId = award.EmployeeId, SettlementType = request.SettlementType, Amount = request.Amount, TaxableAmount = taxable, NonTaxableAmount = request.Amount - taxable, SettlementDate = request.SettlementDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), PayrollRunId = request.PayrollRunId, PayrollResultId = request.PayrollResultId, FinalSettlementId = request.FinalSettlementId, Reference = request.Reference?.Trim(), CreatedAtUtc = clock.GetUtcNow().UtcDateTime, CreatedByUserId = tenant.UserId };
        db.ClearChangeTracker(); db.VariablePaySettlements.Add(settlement); var newSettledAmount = award.SettledAmount + request.Amount; var newStatus = newSettledAmount == approved ? VariablePayAwardStatus.Paid : VariablePayAwardStatus.PartiallyPaid; var updated = await db.VariablePayAwards.Where(x => x.TenantId == tenantId && x.Id == id && x.SettledAmount == award.SettledAmount && x.Status == award.Status).ExecuteUpdateAsync(s => s.SetProperty(x => x.SettledAmount, newSettledAmount).SetProperty(x => x.Status, newStatus).SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1), ct); if (updated != 1) return Result<VariablePayAwardDto>.Conflict("Variable-pay award was modified by another operation."); var eventType = request.SettlementType == VariablePaySettlementType.FinalSettlement ? VariablePayHistoryEventType.FinalSettlementSettled : request.Amount == outstanding ? VariablePayHistoryEventType.PayrollSettled : VariablePayHistoryEventType.PartiallyPaid; db.VariablePayAwardHistories.Add(new VariablePayAwardHistory { Id = Guid.NewGuid(), TenantId = tenantId, VariablePayAwardId = id, EventType = eventType, OccurredAtUtc = settlement.CreatedAtUtc, ActorUserId = tenant.UserId, PreviousStatus = award.Status, NewStatus = newStatus, OriginalAmount = approved, NewAmount = request.Amount, VariablePayPlanVersionId = award.VariablePayPlanVersionId, Reason = "Variable-pay settlement recorded." }); await db.SaveChangesAsync(ct); award.SettledAmount = newSettledAmount; award.Status = newStatus; return Result<VariablePayAwardDto>.Success(ToAwardDto(award));
    }

    public async Task<Result<IReadOnlyList<VariablePayAwardHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<IReadOnlyList<VariablePayAwardHistoryDto>>.Unauthorized("No authenticated tenant.");
        if (!await db.VariablePayAwards.AnyAsync(x => x.TenantId == tenantId && x.Id == id, ct)) return Result<IReadOnlyList<VariablePayAwardHistoryDto>>.NotFound("Variable-pay award not found.");
        var rows = await db.VariablePayAwardHistories.AsNoTracking().Where(x => x.TenantId == tenantId && x.VariablePayAwardId == id).OrderByDescending(x => x.OccurredAtUtc).ToListAsync(ct);
        return Result<IReadOnlyList<VariablePayAwardHistoryDto>>.Success(rows.Select(x => new VariablePayAwardHistoryDto(x.Id, x.EventType, x.OccurredAtUtc, x.PreviousStatus, x.NewStatus, x.OriginalAmount, x.NewAmount, x.Reason)).ToList());
    }

    public async Task<Result<VariablePayAwardDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<VariablePayAwardDto>.Unauthorized("No authenticated tenant.");
        var award = await db.VariablePayAwards.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); return award is null ? Result<VariablePayAwardDto>.NotFound("Variable-pay award not found.") : Result<VariablePayAwardDto>.Success(ToAwardDto(award));
    }

    private async Task<Result<VariablePayAwardDto>> CalculateCoreAsync(Guid employeeId, VariablePayAwardRequest request, bool persist, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<VariablePayAwardDto>.Unauthorized("No authenticated tenant.");
        if (request.AwardPeriodTo < request.AwardPeriodFrom || request.AwardPeriodFrom == default || request.AwardPeriodTo == default) return Result<VariablePayAwardDto>.Invalid("period", "A valid award period is required.");
        var version = await db.VariablePayPlanVersions.Include(x => x.VariablePayPlan).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.PlanVersionId && x.Status == VariablePayPlanVersionStatus.Published && x.EffectiveFrom <= request.EligibilityDate && (x.EffectiveTo == null || x.EffectiveTo >= request.EligibilityDate) && x.VariablePayPlan!.IsActive, ct);
        if (version is null) return Result<VariablePayAwardDto>.Invalid("planVersionId", "An active published plan version effective on the eligibility date is required.");
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == employeeId, ct); if (employee is null) return Result<VariablePayAwardDto>.NotFound("Employee not found.");
        if (version.EligibilityMethod == VariablePayEligibilityMethod.ActiveEmployment && employee.Status != EmployeeStatus.Active) return Result<VariablePayAwardDto>.Invalid("employeeId", "The employee is not active for this plan.");
        if (version.MinimumServiceMonths is int months && employee.DateOfJoining.AddMonths(months) > request.EligibilityDate) return Result<VariablePayAwardDto>.Invalid("eligibility", "The minimum configured service period is not met.");
        var basis = await ResolveSalaryBasisAsync(employeeId, request.EligibilityDate, version, ct); if (!basis.Succeeded) return Result<VariablePayAwardDto>.Failure(basis.Status, basis.Message, basis.Errors);
        var factor = ResolveProration(employee.DateOfJoining, employee.DateOfLeaving, request.AwardPeriodFrom, request.AwardPeriodTo, request.EligibilityDate, version.ProrationMethod); var multiplier = request.PerformanceMultiplier ?? (version.PerformanceRatingRequired ? null : 1m); if (version.PerformanceRatingRequired && multiplier is null) return Result<VariablePayAwardDto>.Invalid("performanceMultiplier", "A performance multiplier is required by this plan."); if (multiplier is decimal m && ((version.PerformanceMultiplierMinimum is decimal min && m < min) || (version.PerformanceMultiplierMaximum is decimal max && m > max) || m < 0)) return Result<VariablePayAwardDto>.Invalid("performanceMultiplier", "The performance multiplier is outside the configured range.");
        var target = version.TargetPercentage.HasValue ? Round(basis.Value * version.TargetPercentage.Value / 100m) : (decimal?)null; var amount = version.CalculationMethod == VariablePayCalculationMethod.ManualAmount ? request.ManualAmount ?? 0m : version.CalculationMethod == VariablePayCalculationMethod.FixedAmount ? version.FixedAmount ?? 0m : version.CalculationMethod == VariablePayCalculationMethod.PercentageOfSalary ? basis.Value * (version.Percentage ?? 0m) / 100m : (target ?? 0m) * (multiplier ?? 1m); amount *= factor; if (version.MinimumAmount is decimal floor) amount = Math.Max(amount, floor); if (version.MaximumAmount is decimal cap) amount = Math.Min(amount, cap); amount = Round(Math.Max(0m, amount)); if (version.CalculationMethod == VariablePayCalculationMethod.ManualAmount && string.IsNullOrWhiteSpace(request.Reason)) return Result<VariablePayAwardDto>.Invalid("reason", "A reason is required for a manual award.");
        var award = new VariablePayAward { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, VariablePayPlanId = version.VariablePayPlanId, VariablePayPlanVersionId = version.Id, AwardPeriodFrom = request.AwardPeriodFrom, AwardPeriodTo = request.AwardPeriodTo, EligibilityDate = request.EligibilityDate, SalaryBasisAmount = basis.Value, TargetAmount = target, PerformanceMultiplier = multiplier, ProrationFactor = factor, CalculatedAmount = amount, CurrencyCode = version.VariablePayPlan!.CurrencyCode, PayoutDate = request.PayoutDate, SettlementMethod = request.SettlementMethod, Status = VariablePayAwardStatus.Calculated, Reason = request.Reason?.Trim(), CreatedByUserId = tenant.UserId };
        SetTaxSplit(award, version);
        if (!persist) { award.AwardNumber = "PREVIEW"; return Result<VariablePayAwardDto>.Success(ToAwardDto(award)); }
        var existing = await db.VariablePayAwards.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.VariablePayPlanVersionId == version.Id && x.AwardPeriodFrom == request.AwardPeriodFrom && x.AwardPeriodTo == request.AwardPeriodTo, ct); if (existing is not null) return Result<VariablePayAwardDto>.Success(ToAwardDto(existing), "Existing idempotent award returned.");
        award.AwardNumber = await NextAwardNumberAsync(tenantId, request.AwardPeriodFrom.Year, ct); db.VariablePayAwards.Add(award); AddHistory(award, VariablePayHistoryEventType.AwardGenerated, null, award.Status, "Award generated."); AddHistory(award, VariablePayHistoryEventType.AwardCalculated, null, award.Status, "Award calculated."); await db.SaveChangesAsync(ct); return Result<VariablePayAwardDto>.Success(ToAwardDto(award));
    }

    private async Task<Result<decimal>> ResolveSalaryBasisAsync(Guid employeeId, DateOnly date, VariablePayPlanVersion version, CancellationToken ct)
    {
        if (version.SalaryBasisType == VariablePaySalaryBasisType.FixedConfiguredAmount) return Result<decimal>.Success(0m);
        var assignment = await db.EmployeeSalaryAssignments.AsNoTracking().Include(x => x.Components).ThenInclude(x => x.SalaryComponent).Where(x => x.TenantId == tenant.TenantId && x.EmployeeId == employeeId && x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date) && x.Status == EmployeeSalaryAssignmentStatus.Active).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct); if (assignment is null) return Result<decimal>.Invalid("salary", "No effective salary assignment is available.");
        if (version.SalaryBasisType == VariablePaySalaryBasisType.Gross) return Result<decimal>.Success(assignment.MonthlyCtc ?? assignment.AnnualCtc / 12m ?? 0m);
        if (version.SalaryBasisType == VariablePaySalaryBasisType.SelectedSalaryComponents)
        {
            var ids = string.IsNullOrWhiteSpace(version.SelectedSalaryComponentIdsJson) ? [] : JsonSerializer.Deserialize<Guid[]>(version.SelectedSalaryComponentIdsJson) ?? [];
            if (ids.Length == 0) return Result<decimal>.Invalid("salaryBasis", "Selected salary component IDs are required.");
            return Result<decimal>.Success(assignment.Components.Where(x => ids.Contains(x.SalaryComponentId) && x.IsActive).Sum(x => x.OverrideValue ?? 0m));
        }
        return Result<decimal>.Invalid("salaryBasis", "This salary basis requires configured salary component IDs.");
    }

    private async Task<string> NextAwardNumberAsync(Guid tenantId, int year, CancellationToken ct)
    {
        var sequence = await db.VariablePayNumberSequences.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Year == year, ct); long value;
        if (sequence is null) { sequence = new VariablePayNumberSequence { Id = Guid.NewGuid(), TenantId = tenantId, Year = year, NextValue = 2 }; db.VariablePayNumberSequences.Add(sequence); value = 1; }
        else { value = sequence.NextValue; sequence.NextValue++; sequence.ConcurrencyVersion++; }
        await db.SaveChangesAsync(ct); return $"VPA/{year}/{value:000000}";
    }

    private async Task<Result<VariablePayAwardDto>> TransitionAsync(Guid id, VariablePayAwardStatus target, VariablePayHistoryEventType eventType, string? reason, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<VariablePayAwardDto>.Unauthorized("No authenticated tenant.");
        var award = await db.VariablePayAwards.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (award is null) return Result<VariablePayAwardDto>.NotFound("Variable-pay award not found.");
        db.ClearChangeTracker();
        var valid = (award.Status, target) switch
        {
            (VariablePayAwardStatus.Calculated, VariablePayAwardStatus.Submitted) => true,
            (VariablePayAwardStatus.Submitted, VariablePayAwardStatus.Rejected) => true,
            (VariablePayAwardStatus.Calculated, VariablePayAwardStatus.Rejected) => true,
            (VariablePayAwardStatus.Draft, VariablePayAwardStatus.Cancelled) => true,
            (VariablePayAwardStatus.Calculated, VariablePayAwardStatus.Cancelled) => true,
            (VariablePayAwardStatus.Submitted, VariablePayAwardStatus.Cancelled) => true,
            (VariablePayAwardStatus.Approved, VariablePayAwardStatus.Cancelled) => award.SettledAmount == 0,
            _ => false
        };
        if (!valid) return Result<VariablePayAwardDto>.Conflict("Invalid variable-pay award lifecycle transition.");
        if (target is VariablePayAwardStatus.Rejected or VariablePayAwardStatus.Cancelled && string.IsNullOrWhiteSpace(reason)) return Result<VariablePayAwardDto>.Invalid("reason", "A reason is required.");
        var previous = award.Status;
        var now = clock.GetUtcNow().UtcDateTime;
        var submittedAt = target == VariablePayAwardStatus.Submitted ? now : award.SubmittedAtUtc;
        var submittedBy = target == VariablePayAwardStatus.Submitted ? tenant.UserId : award.SubmittedByUserId;
        var rejectedAt = target == VariablePayAwardStatus.Rejected ? now : award.RejectedAtUtc;
        var rejectedBy = target == VariablePayAwardStatus.Rejected ? tenant.UserId : award.RejectedByUserId;
        var cancelledAt = target == VariablePayAwardStatus.Cancelled ? now : award.CancelledAtUtc;
        var cancelledBy = target == VariablePayAwardStatus.Cancelled ? tenant.UserId : award.CancelledByUserId;
        var updated = await db.VariablePayAwards
            .Where(x => x.TenantId == tenantId && x.Id == id && x.Status == previous)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, target)
                .SetProperty(x => x.Reason, reason == null ? award.Reason : reason.Trim())
                .SetProperty(x => x.SubmittedAtUtc, submittedAt)
                .SetProperty(x => x.SubmittedByUserId, submittedBy)
                .SetProperty(x => x.RejectedAtUtc, rejectedAt)
                .SetProperty(x => x.RejectedByUserId, rejectedBy)
                .SetProperty(x => x.CancelledAtUtc, cancelledAt)
                .SetProperty(x => x.CancelledByUserId, cancelledBy)
                .SetProperty(x => x.ConcurrencyVersion, x => x.ConcurrencyVersion + 1), ct);
        if (updated != 1) return Result<VariablePayAwardDto>.Conflict("Variable-pay award was modified by another operation.");
        db.VariablePayAwardHistories.Add(new VariablePayAwardHistory { Id = Guid.NewGuid(), TenantId = tenantId, VariablePayAwardId = id, EventType = eventType, OccurredAtUtc = now, ActorUserId = tenant.UserId, PreviousStatus = previous, NewStatus = target, VariablePayPlanVersionId = award.VariablePayPlanVersionId, Reason = reason });
        await db.SaveChangesAsync(ct);
        award.Status = target; award.Reason = reason ?? award.Reason; award.SubmittedAtUtc = submittedAt; award.SubmittedByUserId = submittedBy; award.RejectedAtUtc = rejectedAt; award.RejectedByUserId = rejectedBy; award.CancelledAtUtc = cancelledAt; award.CancelledByUserId = cancelledBy; award.ConcurrencyVersion++;
        return Result<VariablePayAwardDto>.Success(ToAwardDto(award));
    }

    private static string? ValidateVersion(VariablePayPlanVersionRequest r) { if (r.EffectiveTo < r.EffectiveFrom) return "EffectiveTo cannot precede EffectiveFrom."; if (r.Percentage is < 0 or > 100 || r.TargetPercentage is < 0 or > 100) return "Percentages must be between 0 and 100."; if (r.FixedAmount is < 0 || r.MinimumAmount is < 0 || r.MaximumAmount is < 0 || (r.MaximumAmount.HasValue && r.MinimumAmount.HasValue && r.MaximumAmount < r.MinimumAmount)) return "Amounts must be non-negative and max must not be below min."; if (r.PerformanceMultiplierMinimum is < 0 || r.PerformanceMultiplierMaximum is < 0 || (r.PerformanceMultiplierMaximum.HasValue && r.PerformanceMultiplierMinimum.HasValue && r.PerformanceMultiplierMaximum < r.PerformanceMultiplierMinimum)) return "Performance multiplier range is invalid."; if (r.PayoutMonth is < 1 or > 12) return "Payout month is invalid."; if (r.TaxablePercentage is < 0 or > 100) return "Taxable percentage must be between 0 and 100."; return null; }
    private static bool Overlaps(DateOnly fromA, DateOnly? toA, DateOnly fromB, DateOnly? toB) => fromA <= (toB ?? DateOnly.MaxValue) && fromB <= (toA ?? DateOnly.MaxValue);
    private static decimal ResolveProration(DateOnly joining, DateOnly? leaving, DateOnly from, DateOnly to, DateOnly eligibility, VariablePayProrationMethod method) { if (method == VariablePayProrationMethod.None) return 1m; var start = joining > from ? joining : from; var end = leaving.HasValue && leaving.Value < to ? leaving.Value : to; if (end < start) return 0m; var total = to.DayNumber - from.DayNumber + 1; var eligible = end.DayNumber - start.DayNumber + 1; return method == VariablePayProrationMethod.CompletedMonths ? Math.Min(1m, Math.Max(0m, (end.Year * 12 + end.Month - start.Year * 12 - start.Month + 1) / Math.Max(1m, (to.Year * 12 + to.Month - from.Year * 12 - from.Month + 1m)))) : Math.Min(1m, Math.Max(0m, (decimal)eligible / total)); }
    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static void SetTaxSplit(VariablePayAward award, VariablePayPlanVersion version) { var approved = award.ApprovedAmount ?? award.CalculatedAmount; var taxable = version.TaxTreatment switch { VariablePayTaxTreatment.NonTaxable => 0m, VariablePayTaxTreatment.PartiallyTaxable => approved * (version.TaxablePercentage ?? 0m) / 100m, _ => approved }; award.TaxableAmount = Round(taxable); award.NonTaxableAmount = Round(approved - award.TaxableAmount.Value); }
    private static decimal TaxAmount(decimal amount, VariablePayAward award, decimal approved) => approved <= 0 ? 0m : Round(amount * (award.TaxableAmount ?? approved) / approved);
    private static void AddHistory(VariablePayAward award, VariablePayHistoryEventType type, VariablePayAwardStatus? previous, VariablePayAwardStatus? next, string? reason, decimal? original = null, decimal? changed = null) => award.History.Add(new VariablePayAwardHistory { Id = Guid.NewGuid(), TenantId = award.TenantId, VariablePayAwardId = award.Id, EventType = type, OccurredAtUtc = DateTime.UtcNow, PreviousStatus = previous, NewStatus = next, OriginalAmount = original, NewAmount = changed, VariablePayPlanVersionId = award.VariablePayPlanVersionId, Reason = reason });
    private static VariablePayPlanDto ToPlanDto(VariablePayPlan x) => new(x.Id, x.Code, x.Name, x.PlanType, x.CurrencyCode, x.IsActive, x.Versions.OrderByDescending(v => v.EffectiveFrom).Select(v => new VariablePayPlanVersionDto(v.Id, v.EffectiveFrom, v.EffectiveTo, v.Status, v.CalculationMethod, v.SalaryBasisType, v.Percentage, v.FixedAmount, v.TargetPercentage, v.MinimumAmount, v.MaximumAmount, v.ProrationMethod, v.PayoutFrequency, v.TaxTreatment, v.FinalSettlementTreatment)).ToList());
    private static VariablePayAwardDto ToAwardDto(VariablePayAward x) { var approved = x.ApprovedAmount ?? x.CalculatedAmount; return new(x.Id, x.EmployeeId, x.VariablePayPlanId, x.VariablePayPlanVersionId, x.AwardNumber, x.AwardPeriodFrom, x.AwardPeriodTo, x.SalaryBasisAmount, x.TargetAmount, x.PerformanceMultiplier, x.ProrationFactor, x.CalculatedAmount, x.ApprovedAmount, x.TaxableAmount ?? 0m, x.NonTaxableAmount ?? 0m, x.SettledAmount, approved - x.SettledAmount, x.CurrencyCode, x.PayoutDate, x.Status, x.SettlementMethod, x.Reason); }
}
