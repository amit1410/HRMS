using System.Globalization;
using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Discovers and posts policy accruals. Occurrences are claimed and recorded durably; the balance
/// poster remains the only balance mutation boundary.
/// </summary>
public sealed class LeaveAccrualProcessor : ILeaveAccrualProcessor
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ILeavePolicyResolver _policyResolver;
    private readonly ILeavePeriodResolver _periodResolver;
    private readonly ILeaveBalanceTransactionPoster _poster;
    private readonly TimeProvider _clock;
    private readonly LeaveAccrualOptions _options;

    public LeaveAccrualProcessor(IHrmsDbContext db, ITenantContext tenant, ILeavePolicyResolver policyResolver,
        ILeavePeriodResolver periodResolver, ILeaveBalanceTransactionPoster poster, TimeProvider clock,
        LeaveAccrualOptions? options = null)
    {
        _db = db; _tenant = tenant; _policyResolver = policyResolver; _periodResolver = periodResolver;
        _poster = poster; _clock = clock; _options = options ?? new LeaveAccrualOptions();
    }

    public async Task<LeaveAccrualProcessResult> ProcessAsync(DateOnly throughDate, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return new(0, 0, 0, 0);
        var employees = await _db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId && x.Status == EmployeeStatus.Active && x.DateOfJoining <= throughDate).Select(x => new { x.Id, x.DateOfJoining, x.DateOfLeaving }).ToListAsync(ct);
        var leaveTypes = await _db.LeaveTypes.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => x.Id).ToListAsync(ct);
        var processed = 0; var skipped = 0; var failed = 0; decimal credited = 0;
        foreach (var employee in employees)
        foreach (var leaveTypeId in leaveTypes)
        {
            var start = employee.DateOfJoining > throughDate.AddDays(-366) ? employee.DateOfJoining : throughDate.AddDays(-366);
            for (var date = start; date <= throughDate; date = date.AddDays(1))
            {
                var resolution = await _policyResolver.ResolveAsync(tenantId, employee.Id, leaveTypeId, date, ct);
                if (resolution.Status != LeavePolicyResolutionStatus.Resolved) continue;
                var rule = await _db.LeavePolicyRules.AsNoTracking().Include(x => x.EntitlementRule).SingleAsync(x => x.TenantId == tenantId && x.Id == resolution.LeavePolicyRuleId, ct);
                var entitlement = rule.EntitlementRule;
                if (entitlement is null || entitlement.EntitlementMode != EntitlementMode.Allocated || entitlement.EntitlementSource != EntitlementSource.PolicyAccrual || entitlement.AccrualFrequency is AccrualFrequency.None or AccrualFrequency.Upfront) continue;
                var periodResult = await _periodResolver.ResolveAsync(tenantId, date, ct);
                if (periodResult.Status != LeavePeriodResolutionStatus.Resolved || periodResult.Period is null) continue;
                if (!IsDue(entitlement.AccrualFrequency, entitlement.AccrualTiming, date, periodResult.Period.StartDate, periodResult.Period.EndDate, throughDate)) continue;
                var quantity = Quantity(entitlement, date, employee.DateOfJoining, employee.DateOfLeaving, periodResult.Period.StartDate, periodResult.Period.EndDate);
                var occurrenceKey = Key(tenantId, employee.Id, leaveTypeId, resolution.LeavePolicyVersionId!.Value, date, entitlement.AccrualFrequency);
                var occurrence = await ClaimAsync(tenantId, employee.Id, leaveTypeId, periodResult.Period.Id, resolution.LeavePolicyVersionId.Value, resolution.LeavePolicyRuleId!.Value, entitlement.AccrualFrequency, date, occurrenceKey, quantity, ct);
                if (occurrence is null) { skipped++; continue; }
                try
                {
                    var credit = await CreditAsync(tenantId, employee.Id, leaveTypeId, periodResult.Period.Id, resolution, entitlement, date, occurrence, ct);
                    if (credit > 0) credited += credit;
                    if (await FinalizeAsync(occurrence.Id, occurrence.ClaimToken!, credit > 0 ? "Processed" : "Skipped", credit, null, ct)) processed++; else skipped++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    await FinalizeAsync(occurrence.Id, occurrence.ClaimToken!, "Failed", 0, ex.GetType().Name, ct);
                }
            }
        }
        return new(processed, skipped, failed, credited);
    }

    private async Task<decimal> CreditAsync(Guid tenantId, Guid employeeId, Guid leaveTypeId, Guid periodId,
        LeavePolicyResolutionResult resolution, LeavePolicyEntitlementRule entitlement, DateOnly date,
        LeaveAccrualOccurrence occurrence, CancellationToken ct)
    {
        if (occurrence.CalculatedQuantity <= 0) return 0;
        var balance = await _db.EmployeeLeaveBalances.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.LeaveTypeId == leaveTypeId && x.LeavePeriodId == periodId, ct);
        var held = balance is null ? 0 : balance.GrantedQuantity - balance.ConsumedQuantity;
        var quantity = entitlement.MaximumAccumulation is decimal cap ? Math.Max(0, Math.Min(occurrence.CalculatedQuantity, cap - held)) : occurrence.CalculatedQuantity;
        if (quantity <= 0) return 0;
        var result = await _poster.PostCreditAsync(new(tenantId, employeeId, leaveTypeId, periodId, LeaveBalanceTransactionType.Accrual, quantity, date, resolution.LeavePolicyVersionId, resolution.LeavePolicyRuleId, LeaveBalanceSourceType.Policy, occurrence.OccurrenceKey, LeaveBalanceActorType.System, null, null, $"leave-accrual:{occurrence.OccurrenceKey}", null), ct);
        return result.Succeeded ? quantity : throw new InvalidOperationException(result.Message);
    }

    private async Task<LeaveAccrualOccurrence?> ClaimAsync(Guid tenantId, Guid employeeId, Guid leaveTypeId, Guid periodId, Guid versionId, Guid ruleId, AccrualFrequency frequency, DateOnly date, string key, decimal quantity, CancellationToken ct)
    {
        var row = await _db.LeaveAccrualOccurrences.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.OccurrenceKey == key, ct);
        if (row is null)
        {
            row = new LeaveAccrualOccurrence { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, LeaveTypeId = leaveTypeId, LeavePeriodId = periodId, LeavePolicyVersionId = versionId, LeavePolicyRuleId = ruleId, AccrualFrequency = frequency, OccurrenceDate = date, OccurrenceKey = key, CalculatedQuantity = quantity };
            _db.LeaveAccrualOccurrences.Add(row);
            try { await _db.SaveChangesAsync(ct); } catch (DbUpdateException) { _db.ClearChangeTracker(); row = await _db.LeaveAccrualOccurrences.SingleAsync(x => x.TenantId == tenantId && x.OccurrenceKey == key, ct); }
        }
        if (row.Status is "Processed" or "Skipped" || row.AttemptCount >= _options.MaxAttempts) return null;
        var now = _clock.GetUtcNow().UtcDateTime;
        var token = Guid.NewGuid().ToString("N");
        var updated = await _db.LeaveAccrualOccurrences.Where(x => x.TenantId == tenantId && x.Id == row.Id && (x.Status == "Pending" || x.Status == "Failed") && (x.ClaimToken == null || x.LastAttemptAtUtc == null || x.LastAttemptAtUtc < now.AddMinutes(-_options.ClaimLeaseMinutes))).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Processing").SetProperty(x => x.ClaimToken, token).SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1).SetProperty(x => x.LastAttemptAtUtc, now), ct);
        if (updated != 1) return null;
        _db.ClearChangeTracker();
        return await _db.LeaveAccrualOccurrences.AsNoTracking().SingleAsync(x => x.TenantId == tenantId && x.Id == row.Id, ct);
    }

    private async Task<bool> FinalizeAsync(Guid id, string token, string status, decimal credited, string? failure, CancellationToken ct) =>
        await _db.LeaveAccrualOccurrences.Where(x => x.Id == id && x.ClaimToken == token && x.Status == "Processing").ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status).SetProperty(x => x.CreditedQuantity, credited).SetProperty(x => x.FailureCode, failure).SetProperty(x => x.ProcessedAtUtc, _clock.GetUtcNow().UtcDateTime), ct) == 1;

    private static bool IsDue(AccrualFrequency frequency, AccrualTiming? timing, DateOnly date, DateOnly periodStart, DateOnly periodEnd, DateOnly through) => frequency switch
    {
        AccrualFrequency.Daily => true,
        AccrualFrequency.Monthly => timing == AccrualTiming.StartOfPeriod ? date.Day == 1 : date == new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)),
        AccrualFrequency.Annual or AccrualFrequency.Yearly => timing == AccrualTiming.StartOfPeriod ? date == periodStart : date == periodEnd,
        _ => false
    } && date <= through;

    private static string Key(Guid tenant, Guid employee, Guid type, Guid version, DateOnly date, AccrualFrequency frequency) => $"{tenant:D}:accrual:{employee:D}:{type:D}:{version:D}:{frequency}:{date:yyyy-MM-dd}";

    private static decimal Quantity(LeavePolicyEntitlementRule rule, DateOnly date, DateOnly joining, DateOnly? leaving, DateOnly periodStart, DateOnly periodEnd) =>
        rule.EntitlementQuantity is not decimal configured ? 0 : rule.AccrualFrequency switch
        {
            AccrualFrequency.Daily => configured,
            AccrualFrequency.Monthly => rule.ProratePartialPeriod ? configured * EligibleDays(joining, leaving, new DateOnly(date.Year, date.Month, 1), new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month))) / DateTime.DaysInMonth(date.Year, date.Month) : configured,
            AccrualFrequency.Annual or AccrualFrequency.Yearly => rule.ProratePartialPeriod ? configured * EligibleDays(joining, leaving, periodStart, periodEnd) / (periodEnd.DayNumber - periodStart.DayNumber + 1) : configured,
            _ => 0
        };

    private static int EligibleDays(DateOnly joining, DateOnly? leaving, DateOnly from, DateOnly to)
    {
        var start = joining > from ? joining : from;
        var end = leaving is DateOnly last && last < to ? last : to;
        return end < start ? 0 : end.DayNumber - start.DayNumber + 1;
    }
}
