using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class SeparationBenefitsService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock, IPayrollApprovalGuard? approvalGuard = null) : ISeparationBenefitsService
{
    public async Task<Result<IReadOnlyList<GratuityPolicyDto>>> GetPoliciesAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid id) return Result<IReadOnlyList<GratuityPolicyDto>>.Unauthorized("No authenticated tenant.");
        var rows = await db.GratuityPolicies.AsNoTracking().Include(x => x.Versions).Where(x => x.TenantId == id).OrderBy(x => x.Code).ToListAsync(ct);
        return Result<IReadOnlyList<GratuityPolicyDto>>.Success(rows.Select(ToPolicyDto).ToList());
    }

    public async Task<Result<GratuityPolicyDto>> CreatePolicyAsync(GratuityPolicyRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid id) return Result<GratuityPolicyDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)) return Result<GratuityPolicyDto>.Invalid("code", "Policy code and name are required.");
        if (await db.GratuityPolicies.AnyAsync(x => x.TenantId == id && x.Code == request.Code.Trim(), ct)) return Result<GratuityPolicyDto>.Conflict("A gratuity policy with this code already exists.");
        var item = new GratuityPolicy { Id = Guid.NewGuid(), TenantId = id, Code = request.Code.Trim(), Name = request.Name.Trim(), Description = request.Description, IsActive = request.IsActive, CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant() };
        db.GratuityPolicies.Add(item); db.SeparationBenefitHistories.Add(History(item.TenantId, SeparationBenefitHistoryEventType.PolicyCreated, policyId: item.Id, reason: "Policy created.")); await db.SaveChangesAsync(ct);
        return Result<GratuityPolicyDto>.Success(ToPolicyDto(item));
    }

    public async Task<Result<GratuityPolicyDto>> UpdatePolicyAsync(Guid id, GratuityPolicyRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<GratuityPolicyDto>.Unauthorized("No authenticated tenant.");
        var item = await db.GratuityPolicies.Include(x => x.Versions).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); if (item is null) return Result<GratuityPolicyDto>.NotFound("Gratuity policy not found.");
        item.Name = request.Name.Trim(); item.Description = request.Description; item.IsActive = request.IsActive; item.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(); item.ConcurrencyVersion++;
        await db.SaveChangesAsync(ct); return Result<GratuityPolicyDto>.Success(ToPolicyDto(item));
    }

    public async Task<Result<GratuityPolicyDto>> AddVersionAsync(Guid id, GratuityPolicyVersionRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<GratuityPolicyDto>.Unauthorized("No authenticated tenant.");
        var policy = await db.GratuityPolicies.Include(x => x.Versions).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); if (policy is null) return Result<GratuityPolicyDto>.NotFound("Gratuity policy not found.");
        var validation = ValidateVersion(request); if (validation is not null) return Result<GratuityPolicyDto>.Invalid(validation);
        if (policy.Versions.Any(x => x.Status == GratuityPolicyVersionStatus.Published && Overlaps(x.EffectiveFrom, x.EffectiveTo, request.EffectiveFrom, request.EffectiveTo))) return Result<GratuityPolicyDto>.Conflict("Published gratuity policy versions cannot overlap.");
        var version = ToVersion(tenantId, id, request); db.GratuityPolicyVersions.Add(version); db.SeparationBenefitHistories.Add(History(tenantId, SeparationBenefitHistoryEventType.PolicyVersionCreated, policyId: id, versionId: version.Id, reason: "Policy version created.")); await db.SaveChangesAsync(ct); policy.Versions.Add(version); return Result<GratuityPolicyDto>.Success(ToPolicyDto(policy));
    }

    public async Task<Result<IReadOnlyList<SeparationBenefitHistoryDto>>> GetPolicyHistoryAsync(Guid policyId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid id) return Result<IReadOnlyList<SeparationBenefitHistoryDto>>.Unauthorized("No authenticated tenant.");
        if (!await db.GratuityPolicies.AnyAsync(x => x.TenantId == id && x.Id == policyId, ct)) return Result<IReadOnlyList<SeparationBenefitHistoryDto>>.NotFound("Gratuity policy not found.");
        var rows = await db.SeparationBenefitHistories.AsNoTracking().Where(x => x.TenantId == id && x.GratuityPolicyId == policyId).OrderByDescending(x => x.OccurredAtUtc).ToListAsync(ct);
        return Result<IReadOnlyList<SeparationBenefitHistoryDto>>.Success(rows.Select(x => new SeparationBenefitHistoryDto(x.Id, x.EventType, x.OccurredAtUtc, x.ActorUserId, x.SourceType, x.SourceId, x.OriginalAmount, x.FinalAmount, x.Reason)).ToList());
    }

    public Task<Result<SeparationBenefitDto>> PreviewAsync(Guid employeeId, SeparationBenefitCalculationRequest request, CancellationToken ct = default) => CalculateCoreAsync(employeeId, null, request, false, ct);

    public Task<Result<SeparationBenefitDto>> CalculateAsync(Guid employeeId, Guid finalSettlementId, SeparationBenefitCalculationRequest request, CancellationToken ct = default) => CalculateCoreAsync(employeeId, finalSettlementId, request, true, ct);

    public async Task<Result<SeparationBenefitDto>> GetAsync(Guid employeeId, Guid? finalSettlementId = null, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid id) return Result<SeparationBenefitDto>.Unauthorized("No authenticated tenant.");
        var gratuity = await db.GratuityCalculations.AsNoTracking().Include(x => x.GratuityPolicy).Include(x => x.GratuityPolicyVersion).Where(x => x.TenantId == id && x.EmployeeId == employeeId && x.FinalSettlementId == finalSettlementId).OrderByDescending(x => x.CreatedDate).FirstOrDefaultAsync(ct);
        var leave = finalSettlementId is Guid fs ? await db.LeaveEncashmentCalculations.AsNoTracking().Where(x => x.TenantId == id && x.EmployeeId == employeeId && x.FinalSettlementId == fs).ToListAsync(ct) : [];
        var notice = finalSettlementId is Guid nf ? await db.NoticeSettlementCalculations.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == id && x.EmployeeId == employeeId && x.FinalSettlementId == nf, ct) : null;
        var settlement = finalSettlementId is Guid sid ? await db.FinalSettlementCases.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == id && x.Id == sid, ct) : null;
        return Result<SeparationBenefitDto>.Success(new(employeeId, finalSettlementId, gratuity is null ? null : ToCalculationDto(gratuity), leave.Select(ToLeaveDto).ToList(), notice is null ? null : ToNoticeDto(notice), settlement?.Status));
    }

    public async Task<Result<IReadOnlyList<SeparationBenefitHistoryDto>>> GetHistoryAsync(Guid employeeId, Guid? finalSettlementId = null, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid id) return Result<IReadOnlyList<SeparationBenefitHistoryDto>>.Unauthorized("No authenticated tenant.");
        var rows = await db.SeparationBenefitHistories.AsNoTracking().Where(x => x.TenantId == id && x.EmployeeId == employeeId && (finalSettlementId == null || x.FinalSettlementId == finalSettlementId)).OrderByDescending(x => x.OccurredAtUtc).ToListAsync(ct);
        return Result<IReadOnlyList<SeparationBenefitHistoryDto>>.Success(rows.Select(x => new SeparationBenefitHistoryDto(x.Id, x.EventType, x.OccurredAtUtc, x.ActorUserId, x.SourceType, x.SourceId, x.OriginalAmount, x.FinalAmount, x.Reason)).ToList());
    }

    public async Task<Result<PagedResult<SeparationBenefitRegisterRowDto>>> RegisterAsync(SeparationBenefitQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid id) return Result<PagedResult<SeparationBenefitRegisterRowDto>>.Unauthorized("No authenticated tenant.");
        var source = db.GratuityCalculations.AsNoTracking().Include(x => x.Employee).Include(x => x.FinalSettlement).Where(x => x.TenantId == id);
        if (query.EmployeeId is Guid employeeId) source = source.Where(x => x.EmployeeId == employeeId);
        if (query.SeparationReason is SeparationReason reason) source = source.Where(x => x.SeparationReason == reason);
        if (query.From is DateOnly from) source = source.Where(x => x.ServiceEndDate >= from);
        if (query.To is DateOnly to) source = source.Where(x => x.ServiceEndDate <= to);
        var total = await source.CountAsync(ct); var rows = await source.OrderByDescending(x => x.ServiceEndDate).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.FinalSettlementId).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        var leave = await db.LeaveEncashmentCalculations.AsNoTracking().Where(x => x.TenantId == id && ids.Contains(x.FinalSettlementId)).GroupBy(x => x.FinalSettlementId).Select(x => new { Id = x.Key, Amount = x.Sum(y => y.GrossAmount), Taxable = x.Sum(y => y.TaxableAmount), NonTaxable = x.Sum(y => y.NonTaxableAmount) }).ToListAsync(ct);
        var notice = await db.NoticeSettlementCalculations.AsNoTracking().Where(x => x.TenantId == id && ids.Contains(x.FinalSettlementId)).ToListAsync(ct);
        var result = rows.Select(x => { var l = leave.FirstOrDefault(y => y.Id == x.FinalSettlementId); var n = notice.FirstOrDefault(y => y.FinalSettlementId == x.FinalSettlementId); var gross = x.FinalGratuityAmount + (l?.Amount ?? 0) + (n?.Type == NoticeSettlementType.NoticePay ? n.Amount : 0) - (n?.Type == NoticeSettlementType.NoticeRecovery ? n.Amount : 0); return new SeparationBenefitRegisterRowDto(x.EmployeeId, x.FinalSettlementId, x.Employee?.EmployeeCode ?? string.Empty, string.Join(' ', new[] { x.Employee?.FirstName, x.Employee?.LastName }.Where(y => !string.IsNullOrWhiteSpace(y))), x.Employee?.DateOfJoining, x.ServiceEndDate, x.SeparationReason, x.TotalServiceMonths, x.FinalGratuityAmount, l?.Amount ?? 0, n?.Type == NoticeSettlementType.NoticePay ? n.Amount : 0, n?.Type == NoticeSettlementType.NoticeRecovery ? n.Amount : 0, x.TaxableAmount + (l?.Taxable ?? 0) + (n?.TaxableAmount ?? 0), x.NonTaxableAmount + (l?.NonTaxable ?? 0) + (n?.NonTaxableAmount ?? 0), gross, x.FinalSettlement?.Status); }).ToList(); return Result<PagedResult<SeparationBenefitRegisterRowDto>>.Success(new(result, query.Page, query.PageSize, total));
    }

    public async Task<Result<GratuityCalculationDto>> OverrideAsync(Guid id, GratuityOverrideRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<GratuityCalculationDto>.Unauthorized("No authenticated tenant.");
        var calc = await db.GratuityCalculations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); if (calc is null) return Result<GratuityCalculationDto>.NotFound("Gratuity calculation not found.");
        if (calc.Status == SeparationBenefitCalculationStatus.Finalized) return Result<GratuityCalculationDto>.Conflict("Finalized gratuity cannot be overridden.");
        if (request.OverrideAmount < 0 || string.IsNullOrWhiteSpace(request.Reason)) return Result<GratuityCalculationDto>.Invalid("override", "A non-negative override amount and reason are required.");
        db.GratuityOverrides.Add(new GratuityOverride { Id = Guid.NewGuid(), TenantId = tenantId, GratuityCalculationId = id, OriginalAmount = calc.FinalGratuityAmount, OverrideAmount = request.OverrideAmount, Reason = request.Reason.Trim(), RequestedByUserId = tenant.UserId, CreatedAtUtc = clock.GetUtcNow().UtcDateTime }); db.SeparationBenefitHistories.Add(History(tenantId, SeparationBenefitHistoryEventType.OverrideRequested, calc.GratuityPolicyId, calc.GratuityPolicyVersionId, calc.EmployeeId, calc.FinalSettlementId, id, calc.FinalGratuityAmount, request.OverrideAmount, request.Reason)); await db.SaveChangesAsync(ct); return Result<GratuityCalculationDto>.Success(ToCalculationDto(calc));
    }

    public async Task<Result<GratuityCalculationDto>> ApproveOverrideAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<GratuityCalculationDto>.Unauthorized("No authenticated tenant.");
        var calc = await db.GratuityCalculations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); if (calc is null) return Result<GratuityCalculationDto>.NotFound("Gratuity calculation not found.");
        var pending = await db.GratuityOverrides.Where(x => x.TenantId == tenantId && x.GratuityCalculationId == id && x.ApprovedAtUtc == null).OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct); if (pending is null) return Result<GratuityCalculationDto>.NotFound("No pending gratuity override found.");
        var guard = await (approvalGuard ?? new PayrollApprovalGuard(db, tenant)).ValidateAsync(pending.RequestedByUserId, "approve", ct: ct); if (!guard.Succeeded) return Result<GratuityCalculationDto>.Failure(guard.Status, guard.Message, guard.Errors);
        pending.ApprovedByUserId = tenant.UserId; pending.ApprovedAtUtc = clock.GetUtcNow().UtcDateTime; calc.FinalGratuityAmount = pending.OverrideAmount; (calc.TaxableAmount, calc.NonTaxableAmount) = SplitTax(calc.FinalGratuityAmount, calc.GratuityPolicyVersion?.TaxTreatment ?? SeparationBenefitTaxTreatment.NonTaxable, calc.GratuityPolicyVersion?.TaxablePercentage); db.SeparationBenefitHistories.Add(History(tenantId, SeparationBenefitHistoryEventType.OverrideApproved, calc.GratuityPolicyId, calc.GratuityPolicyVersionId, calc.EmployeeId, calc.FinalSettlementId, id, pending.OriginalAmount, pending.OverrideAmount, pending.Reason)); await db.SaveChangesAsync(ct); return Result<GratuityCalculationDto>.Success(ToCalculationDto(calc));
    }

    public async Task<Result<bool>> FinalizeAsync(Guid finalSettlementId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<bool>.Unauthorized("No authenticated tenant.");
        var calc = await db.GratuityCalculations.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.FinalSettlementId == finalSettlementId, ct); if (calc is null) return Result<bool>.Success(false, "No gratuity calculation applies."); if (calc.Status == SeparationBenefitCalculationStatus.Finalized) return Result<bool>.Success(true, "Gratuity was already finalized.");
        calc.Status = SeparationBenefitCalculationStatus.Finalized; calc.ConcurrencyVersion++; db.SeparationBenefitHistories.Add(History(tenantId, SeparationBenefitHistoryEventType.Finalized, calc.GratuityPolicyId, calc.GratuityPolicyVersionId, calc.EmployeeId, finalSettlementId, calc.Id, calc.FinalGratuityAmount, calc.FinalGratuityAmount, "Gratuity finalized with Final Settlement.")); await db.SaveChangesAsync(ct); return Result<bool>.Success(true);
    }

    private async Task<Result<SeparationBenefitDto>> CalculateCoreAsync(Guid employeeId, Guid? finalSettlementId, SeparationBenefitCalculationRequest request, bool persist, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<SeparationBenefitDto>.Unauthorized("No authenticated tenant.");
        if (request.LastWorkingDate < request.SeparationDate) return Result<SeparationBenefitDto>.Invalid("dates", "Last Working Date cannot be earlier than Separation Date.");
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == employeeId, ct); if (employee is null) return Result<SeparationBenefitDto>.NotFound("Employee not found.");
        if (persist && finalSettlementId is Guid fs && await db.GratuityCalculations.AnyAsync(x => x.TenantId == tenantId && x.FinalSettlementId == fs, ct)) return await GetAsync(employeeId, fs, ct);
        var policy = await db.GratuityPolicies.AsNoTracking().Include(x => x.Versions).Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code).FirstOrDefaultAsync(ct); if (policy is null) return Result<SeparationBenefitDto>.Success(new(employeeId, finalSettlementId, null, [], null, null), "No active gratuity policy applies.");
        var version = policy.Versions.Where(x => x.Status == GratuityPolicyVersionStatus.Published && x.EffectiveFrom <= request.LastWorkingDate && (x.EffectiveTo == null || x.EffectiveTo >= request.LastWorkingDate)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefault(); if (version is null) return Result<SeparationBenefitDto>.Conflict("No effective gratuity policy version applies.");
        var service = ServiceLength(employee.DateOfJoining, request.LastWorkingDate, version); var eligibleReason = IsReasonEligible(version, request.SeparationReason); var overrideApplied = request.SeparationReason == SeparationReason.Death && version.AllowMinimumServiceOverrideForDeath || request.SeparationReason == SeparationReason.Disability && version.AllowMinimumServiceOverrideForDisability; if (!eligibleReason || service.Months < version.MinimumServiceMonths && !overrideApplied) return Result<SeparationBenefitDto>.Success(new(employeeId, finalSettlementId, null, [], null, null));
        var wage = version.FormulaType == GratuityFormulaType.FixedAmount
            ? Result<(decimal Amount, string Snapshot)>.Success((version.FixedAmount ?? 0m, JsonSerializer.Serialize(new { version.FixedAmount, basis = "ConfiguredFixedAmount" })))
            : await ResolveWageAsync(tenantId, employeeId, request.LastWorkingDate, version, ct);
        if (!wage.Succeeded) return Result<SeparationBenefitDto>.Failure(wage.Status, wage.Message, wage.Errors);
        var raw = CalculateAmount(wage.Value!.Amount, service.Units, version); var rounded = Round(raw, version.RoundingPrecision, version.MonetaryRoundingMethod); var capped = version.MaximumBenefit is decimal cap && rounded > cap ? cap : rounded; var final = version.MinimumBenefit is decimal min && capped < min ? min : capped; var (taxable, nonTaxable) = SplitTax(final, version.TaxTreatment, version.TaxablePercentage);
        if (!persist) return Result<SeparationBenefitDto>.Success(new(employeeId, null, ToPreviewDto(employeeId, policy, version, request.SeparationReason, service, wage.Value.Amount, raw, capped != rounded, version.MaximumBenefit, final, taxable, nonTaxable), [], null, null));
        var settlement = finalSettlementId is Guid settlementId ? await db.FinalSettlementCases.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == settlementId, ct) : null; if (finalSettlementId is not null && settlement is null) return Result<SeparationBenefitDto>.NotFound("Final settlement not found.");
        var calculation = new GratuityCalculation { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, FinalSettlementId = finalSettlementId, GratuityPolicyId = policy.Id, GratuityPolicyVersionId = version.Id, SeparationReason = request.SeparationReason, ServiceStartDate = service.Start, ServiceEndDate = service.End, TotalServiceDays = service.Days, TotalServiceMonths = service.Months, EligibleServiceUnits = service.Units, AppliedRoundingRule = service.Rule, WageBasisType = version.WageBasisType, WageBasisAmount = wage.Value.Amount, WageSnapshotJson = wage.Value.Snapshot, NumeratorDays = version.NumeratorDays, DenominatorDays = version.DenominatorDays, GrossCalculatedAmount = raw, CapApplied = capped != rounded, CapAmount = version.MaximumBenefit, FinalGratuityAmount = final, TaxableAmount = taxable, NonTaxableAmount = nonTaxable, CurrencyCode = policy.CurrencyCode, CalculationDateUtc = clock.GetUtcNow().UtcDateTime, CalculatedByUserId = tenant.UserId, Status = SeparationBenefitCalculationStatus.Calculated };
        db.GratuityCalculations.Add(calculation); if (settlement is not null) db.FinalSettlementLines.Add(new FinalSettlementLine { Id = Guid.NewGuid(), TenantId = tenantId, FinalSettlementCaseId = settlement.Id, LineType = FinalSettlementLineType.Gratuity, ComponentCode = "GRATUITY", Description = $"Gratuity under {policy.Code}", Amount = final, IsEarning = true, SourceType = "Gratuity", SourceId = calculation.Id, Sequence = await db.FinalSettlementLines.CountAsync(x => x.TenantId == tenantId && x.FinalSettlementCaseId == settlement.Id, ct) + 1 });
        var leave = await CalculateLeaveAsync(tenantId, employeeId, settlement, version, wage.Value.Amount, ct); var notice = await CalculateNoticeAsync(tenantId, employeeId, settlement, version, wage.Value.Amount, ct); db.SeparationBenefitHistories.Add(History(tenantId, SeparationBenefitHistoryEventType.GratuityCalculated, policy.Id, version.Id, employeeId, finalSettlementId, calculation.Id, raw, final, overrideApplied ? "Minimum service override applied." : "Gratuity calculated from effective policy.")); await db.SaveChangesAsync(ct);
        return await GetAsync(employeeId, finalSettlementId, ct);
    }

    private async Task<Result<(decimal Amount, string Snapshot)>> ResolveWageAsync(Guid tenantId, Guid employeeId, DateOnly date, GratuityPolicyVersion version, CancellationToken ct)
    {
        if (version.WageBasisType == GratuityWageBasisType.FixedConfiguredAmount) return version.FixedAmount is decimal fixedAmount && fixedAmount >= 0 ? Result<(decimal, string)>.Success((fixedAmount, JsonSerializer.Serialize(new { version.FixedAmount }))) : Result<(decimal, string)>.Conflict("Fixed gratuity wage basis is not configured.");
        var assignment = await db.EmployeeSalaryAssignments.Include(x => x.Components).ThenInclude(x => x.SalaryComponent).Include(x => x.Components).ThenInclude(x => x.SalaryStructureComponent).Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Status == EmployeeSalaryAssignmentStatus.Active && x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
        if (assignment is null) return Result<(decimal, string)>.Conflict("No effective salary assignment is available for the gratuity wage basis.");
        var selected = ParseIds(version.SelectedSalaryComponentIdsJson); var values = assignment.Components.Where(x => x.IsActive && (x.EffectiveTo == null || x.EffectiveTo >= date) && x.EffectiveFrom <= date && (selected.Count == 0 || selected.Contains(x.SalaryComponentId))).Select(x => new { x.SalaryComponentId, Value = x.OverrideValue ?? x.SalaryStructureComponent?.Value ?? 0m, Code = x.SalaryComponent?.Code }).ToList();
        decimal amount = version.WageBasisType == GratuityWageBasisType.Gross ? assignment.MonthlyCtc ?? assignment.AnnualCtc / 12m ?? 0m : values.Sum(x => x.Value); if (version.WageBasisType is GratuityWageBasisType.Basic or GratuityWageBasisType.BasicPlusDA && amount == 0) amount = assignment.MonthlyCtc ?? assignment.AnnualCtc / 12m ?? 0m; if (amount <= 0) return Result<(decimal, string)>.Conflict("The configured gratuity wage basis resolved to zero."); return Result<(decimal, string)>.Success((amount, JsonSerializer.Serialize(values)));
    }

    private async Task<List<LeaveEncashmentCalculation>> CalculateLeaveAsync(Guid tenantId, Guid employeeId, FinalSettlementCase? settlement, GratuityPolicyVersion version, decimal wage, CancellationToken ct)
    {
        if (settlement is null || !version.LeaveEncashmentEnabled || version.LeaveEncashmentDivisor is not decimal divisor || divisor <= 0) return [];
        var typeIds = ParseIds(version.LeaveTypeIdsJson); var balances = await db.EmployeeLeaveBalances.Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && (typeIds.Count == 0 || typeIds.Contains(x.LeaveTypeId)) && x.GrantedQuantity - x.ReservedQuantity - x.ConsumedQuantity > 0).ToListAsync(ct); var result = new List<LeaveEncashmentCalculation>(); var remaining = version.MaximumLeaveEncashmentDays;
        foreach (var balance in balances) { var days = balance.AvailableQuantity; if (remaining is decimal r) days = Math.Min(days, r); if (days <= 0) continue; var gross = Round(wage / divisor * days, 2, MidpointRounding.AwayFromZero); var (taxable, nonTaxable) = SplitTax(gross, version.LeaveEncashmentTaxTreatment, null); result.Add(new LeaveEncashmentCalculation { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, FinalSettlementId = settlement.Id, LeaveTypeId = balance.LeaveTypeId, EligibleDays = balance.AvailableQuantity, EncashableDays = days, WageBasisAmount = wage, Divisor = divisor, GrossAmount = gross, TaxableAmount = taxable, NonTaxableAmount = nonTaxable, SourceBalanceReference = balance.Id.ToString(), CalculationDateUtc = clock.GetUtcNow().UtcDateTime }); if (remaining is decimal) remaining -= days; }
        if (result.Count > 0) { db.LeaveEncashmentCalculations.AddRange(result); foreach (var x in result) db.FinalSettlementLines.Add(new FinalSettlementLine { Id = Guid.NewGuid(), TenantId = tenantId, FinalSettlementCaseId = settlement.Id, LineType = FinalSettlementLineType.LeaveEncashment, ComponentCode = $"LEAVE-{x.LeaveTypeId:N}"[..40], Description = "Leave encashment from authoritative leave balance", Amount = x.GrossAmount, IsEarning = true, SourceType = "LeaveEncashment", SourceId = x.Id, Sequence = await db.FinalSettlementLines.CountAsync(y => y.TenantId == tenantId && y.FinalSettlementCaseId == settlement.Id, ct) + 1 }); }
        return result;
    }

    private async Task<NoticeSettlementCalculation?> CalculateNoticeAsync(Guid tenantId, Guid employeeId, FinalSettlementCase? settlement, GratuityPolicyVersion version, decimal wage, CancellationToken ct)
    {
        if (settlement is null || version.NoticeSettlementType == NoticeSettlementType.None || version.NoticeDivisor is not decimal divisor || divisor <= 0) return null;
        var employment = await db.EmployeeEmployments.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId, ct); if (employment?.NoticePeriod is not int noticePeriod || employment.NoticeStartDate is not DateOnly noticeStart) return null;
        var required = employment.NoticePeriodUnit?.Equals("Months", StringComparison.OrdinalIgnoreCase) == true
            ? noticeStart.AddMonths(noticePeriod).DayNumber - noticeStart.DayNumber
            : noticePeriod;
        var served = employment.NoticeEndDate is DateOnly noticeEnd ? Math.Max(0, noticeEnd.DayNumber - noticeStart.DayNumber + 1) : Math.Max(0, settlement.LastWorkingDate.DayNumber - noticeStart.DayNumber + 1);
        var difference = Math.Max(0, required - served); var amount = Round(wage / divisor * difference, 2, MidpointRounding.AwayFromZero); var (taxable, nonTaxable) = SplitTax(amount, SeparationBenefitTaxTreatment.NonTaxable, null); var result = new NoticeSettlementCalculation { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, FinalSettlementId = settlement.Id, Type = version.NoticeSettlementType, RequiredDays = required, ServedDays = served, DifferenceDays = difference, WageBasisAmount = wage, Divisor = divisor, Amount = amount, TaxableAmount = taxable, NonTaxableAmount = nonTaxable, CalculationDateUtc = clock.GetUtcNow().UtcDateTime }; db.NoticeSettlementCalculations.Add(result); if (amount > 0) db.FinalSettlementLines.Add(new FinalSettlementLine { Id = Guid.NewGuid(), TenantId = tenantId, FinalSettlementCaseId = settlement.Id, LineType = version.NoticeSettlementType == NoticeSettlementType.NoticePay ? FinalSettlementLineType.NoticePay : FinalSettlementLineType.NoticeRecovery, ComponentCode = version.NoticeSettlementType == NoticeSettlementType.NoticePay ? "NOTICE-PAY" : "NOTICE-RECOVERY", Description = version.NoticeSettlementType.ToString(), Amount = amount, IsEarning = version.NoticeSettlementType == NoticeSettlementType.NoticePay, IsDeduction = version.NoticeSettlementType == NoticeSettlementType.NoticeRecovery, SourceType = "NoticeSettlement", SourceId = result.Id, Sequence = await db.FinalSettlementLines.CountAsync(x => x.TenantId == tenantId && x.FinalSettlementCaseId == settlement.Id, ct) + 1 }); return result;
    }

    private static ServiceParts ServiceLength(DateOnly start, DateOnly end, GratuityPolicyVersion version)
    {
        var days = end.DayNumber - start.DayNumber + 1; if (days < 0) return new(start, end, 0, 0, 0, 0, "Invalid"); var wholeMonths = (end.Year - start.Year) * 12 + end.Month - start.Month; if (end.Day < start.Day) wholeMonths--; var months = Math.Max(0, wholeMonths) + Math.Max(0, days - Math.Max(1, wholeMonths) * 30) / 30m; var years = months / 12m; var units = version.ServiceRoundingMethod switch { ServiceRoundingMethod.CompletedYearsOnly => Math.Floor(years), ServiceRoundingMethod.RoundRemainingMonthsUpAtThreshold => Math.Floor(years) + (months - Math.Floor(years) * 12m >= (version.ServiceRoundingThresholdMonths ?? 0m) ? 1m : 0m), ServiceRoundingMethod.ExactMonths => months, ServiceRoundingMethod.ExactDays => days, _ => years }; return new(start, end, days, decimal.Round(months, 6), decimal.Round(years, 6), decimal.Round(units, 6), version.ServiceRoundingMethod.ToString());
    }

    private static decimal CalculateAmount(decimal wage, decimal units, GratuityPolicyVersion version)
    {
        if (version.FormulaType == GratuityFormulaType.FixedAmount) return version.FixedAmount ?? 0m;
        var denominator = version.DenominatorDays ?? 0m; if (denominator <= 0) return 0m;
        return wage * (version.NumeratorDays ?? 0m) / denominator * units;
    }
    private static (decimal Taxable, decimal NonTaxable) SplitTax(decimal amount, SeparationBenefitTaxTreatment treatment, decimal? percentage) { var taxable = treatment == SeparationBenefitTaxTreatment.Taxable ? amount : treatment == SeparationBenefitTaxTreatment.PartiallyTaxable ? amount * ((percentage ?? 0m) / 100m) : 0m; taxable = decimal.Round(taxable, 2, MidpointRounding.AwayFromZero); return (taxable, decimal.Round(amount - taxable, 2, MidpointRounding.AwayFromZero)); }
    private static decimal Round(decimal value, int precision, MidpointRounding mode) => decimal.Round(Math.Max(0, value), precision, mode);
    private static bool IsReasonEligible(GratuityPolicyVersion x, SeparationReason reason) => reason switch { SeparationReason.Resignation => x.IsResignationEligible, SeparationReason.Retirement => x.IsRetirementEligible, SeparationReason.Termination => x.IsTerminationEligible, SeparationReason.Death => x.IsDeathEligible, SeparationReason.Disability => x.IsDisabilityEligible, SeparationReason.Redundancy => x.IsRedundancyEligible, _ => false };
    private static bool Overlaps(DateOnly fromA, DateOnly? toA, DateOnly fromB, DateOnly? toB) => fromA <= (toB ?? DateOnly.MaxValue) && fromB <= (toA ?? DateOnly.MaxValue);
    private static string? ValidateVersion(GratuityPolicyVersionRequest x) { if (x.EffectiveTo < x.EffectiveFrom) return "Effective To cannot be earlier than Effective From."; if (x.MinimumServiceMonths < 0 || x.RoundingPrecision is < 0 or > 6) return "Policy service and rounding values are invalid."; if (x.FormulaType != GratuityFormulaType.FixedAmount && (x.DenominatorDays is null or <= 0 || x.NumeratorDays is null or < 0)) return "Formula numerator and denominator are required."; if (x.ServiceRoundingMethod == ServiceRoundingMethod.RoundRemainingMonthsUpAtThreshold && (x.ServiceRoundingThresholdMonths is null or <= 0 or > 12)) return "A valid service rounding threshold is required."; if (x.MaximumBenefit is < 0 || x.MinimumBenefit is < 0 || x.FixedAmount is < 0) return "Benefit amounts cannot be negative."; if (x.TaxTreatment == SeparationBenefitTaxTreatment.PartiallyTaxable && (x.TaxablePercentage is null or < 0 or > 100)) return "A taxable percentage from 0 to 100 is required."; return null; }
    private static List<Guid> ParseIds(string? json) { if (string.IsNullOrWhiteSpace(json)) return []; try { return JsonSerializer.Deserialize<List<Guid>>(json) ?? []; } catch { return []; } }
    private static GratuityPolicyVersion ToVersion(Guid tenantId, Guid policyId, GratuityPolicyVersionRequest x) => new() { Id = Guid.NewGuid(), TenantId = tenantId, GratuityPolicyId = policyId, EffectiveFrom = x.EffectiveFrom, EffectiveTo = x.EffectiveTo, Status = x.Status, MinimumServiceMonths = x.MinimumServiceMonths, ServiceRoundingMethod = x.ServiceRoundingMethod, ServiceRoundingThresholdMonths = x.ServiceRoundingThresholdMonths, FormulaType = x.FormulaType, NumeratorDays = x.NumeratorDays, DenominatorDays = x.DenominatorDays, WageBasisType = x.WageBasisType, SelectedSalaryComponentIdsJson = x.SelectedSalaryComponentIdsJson, FixedAmount = x.FixedAmount, MaximumBenefit = x.MaximumBenefit, MinimumBenefit = x.MinimumBenefit, MonetaryRoundingMethod = x.MonetaryRoundingMethod, RoundingPrecision = x.RoundingPrecision, TaxTreatment = x.TaxTreatment, TaxablePercentage = x.TaxablePercentage, IsResignationEligible = x.IsResignationEligible, IsRetirementEligible = x.IsRetirementEligible, IsTerminationEligible = x.IsTerminationEligible, IsDeathEligible = x.IsDeathEligible, IsDisabilityEligible = x.IsDisabilityEligible, IsRedundancyEligible = x.IsRedundancyEligible, AllowMinimumServiceOverrideForDeath = x.AllowMinimumServiceOverrideForDeath, AllowMinimumServiceOverrideForDisability = x.AllowMinimumServiceOverrideForDisability, IncludeNoticePeriodInService = x.IncludeNoticePeriodInService, LeaveEncashmentEnabled = x.LeaveEncashmentEnabled, LeaveTypeIdsJson = x.LeaveTypeIdsJson, MaximumLeaveEncashmentDays = x.MaximumLeaveEncashmentDays, LeaveEncashmentDivisor = x.LeaveEncashmentDivisor, LeaveEncashmentTaxTreatment = x.LeaveEncashmentTaxTreatment, NoticeSettlementType = x.NoticeSettlementType, NoticeDivisor = x.NoticeDivisor, NoticeWageBasisType = x.NoticeWageBasisType };
    private static GratuityPolicyDto ToPolicyDto(GratuityPolicy x) => new(x.Id, x.Code, x.Name, x.IsActive, x.CurrencyCode, x.Versions.OrderByDescending(v => v.EffectiveFrom).Select(v => new GratuityPolicyVersionDto(v.Id, v.EffectiveFrom, v.EffectiveTo, v.Status, v.MinimumServiceMonths, v.ServiceRoundingMethod, v.FormulaType, v.NumeratorDays, v.DenominatorDays, v.WageBasisType, v.MaximumBenefit, v.MinimumBenefit, v.TaxTreatment, v.LeaveEncashmentEnabled, v.NoticeSettlementType)).ToList());
    private static GratuityCalculationDto ToPreviewDto(Guid employeeId, GratuityPolicy p, GratuityPolicyVersion v, SeparationReason reason, ServiceParts s, decimal wage, decimal raw, bool cap, decimal? capAmount, decimal finalAmount, decimal taxable, decimal nonTaxable) => new(Guid.Empty, employeeId, null, p.Id, v.Id, reason, new(s.Start, s.End, s.Days, s.Months, s.Years, s.Units, s.Rule), v.WageBasisType, wage, raw, cap, capAmount, finalAmount, taxable, nonTaxable, p.CurrencyCode, SeparationBenefitCalculationStatus.Preview);
    private static GratuityCalculationDto ToCalculationDto(GratuityCalculation x) => new(x.Id, x.EmployeeId, x.FinalSettlementId, x.GratuityPolicyId, x.GratuityPolicyVersionId, x.SeparationReason, new(x.ServiceStartDate, x.ServiceEndDate, x.TotalServiceDays, x.TotalServiceMonths, x.TotalServiceMonths / 12m, x.EligibleServiceUnits, x.AppliedRoundingRule), x.WageBasisType, x.WageBasisAmount, x.GrossCalculatedAmount, x.CapApplied, x.CapAmount, x.FinalGratuityAmount, x.TaxableAmount, x.NonTaxableAmount, x.CurrencyCode, x.Status);
    private static LeaveEncashmentCalculationDto ToLeaveDto(LeaveEncashmentCalculation x) => new(x.Id, x.LeaveTypeId, x.EligibleDays, x.EncashableDays, x.WageBasisAmount, x.Divisor, x.GrossAmount, x.TaxableAmount, x.NonTaxableAmount);
    private static NoticeSettlementCalculationDto ToNoticeDto(NoticeSettlementCalculation x) => new(x.Id, x.Type, x.RequiredDays, x.ServedDays, x.DifferenceDays, x.WageBasisAmount, x.Divisor, x.Amount, x.TaxableAmount, x.NonTaxableAmount);
    private static SeparationBenefitHistory History(Guid tenantId, SeparationBenefitHistoryEventType type, Guid? policyId = null, Guid? versionId = null, Guid? employeeId = null, Guid? settlementId = null, Guid? sourceId = null, decimal? original = null, decimal? final = null, string? reason = null) => new() { Id = Guid.NewGuid(), TenantId = tenantId, GratuityPolicyId = policyId, GratuityPolicyVersionId = versionId, EmployeeId = employeeId, FinalSettlementId = settlementId, EventType = type, OccurredAtUtc = DateTime.UtcNow, ActorUserId = null, SourceType = type.ToString(), SourceId = sourceId, OriginalAmount = original, FinalAmount = final, Reason = reason };
    private sealed record ServiceParts(DateOnly Start, DateOnly End, int Days, decimal Months, decimal Years, decimal Units, string Rule);
    private sealed record Wage(decimal Amount, string Snapshot);
}
