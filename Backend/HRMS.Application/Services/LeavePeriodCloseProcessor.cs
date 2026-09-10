using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>Closes a LeavePeriod through ledger transactions. Source-aware expiry is intentionally not
/// performed here because the existing aggregate balance has no grant allocation information.</summary>
public sealed class LeavePeriodCloseProcessor : ILeavePeriodCloseProcessor
{
    private readonly IHrmsDbContext _db; private readonly ITenantContext _tenant; private readonly ILeavePolicyResolver _policies; private readonly ILeaveBalanceTransactionPoster _poster; private readonly TimeProvider _clock;
    public LeavePeriodCloseProcessor(IHrmsDbContext db, ITenantContext tenant, ILeavePolicyResolver policies, ILeaveBalanceTransactionPoster poster, TimeProvider clock) { _db = db; _tenant = tenant; _policies = policies; _poster = poster; _clock = clock; }

    public async Task<LeavePeriodCloseProcessResult> ProcessAsync(Guid sourceLeavePeriodId, DateOnly asOf, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return new(0, 0, 0, 0, 0);
        var source = await _db.LeavePeriods.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == sourceLeavePeriodId, ct);
        var destination = source is null ? null : await _db.LeavePeriods.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.StartDate > source.EndDate).OrderBy(x => x.StartDate).FirstOrDefaultAsync(ct);
        if (source is null || destination is null || source.EndDate > asOf) return new(0, 0, 0, 0, 0);
        var balances = await _db.EmployeeLeaveBalances.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.LeavePeriodId == source.Id && x.GrantedQuantity - x.ReservedQuantity - x.ConsumedQuantity > 0)
            .Select(x => new BalanceRow(x.Id, x.EmployeeId, x.LeaveTypeId, x.GrantedQuantity - x.ReservedQuantity - x.ConsumedQuantity))
            .ToListAsync(ct);
        var done = 0; var skipped = 0; var failed = 0; decimal carried = 0; decimal lapsed = 0;
        foreach (var balance in balances)
        {
            var policy = await _policies.ResolveAsync(tenantId, balance.EmployeeId, balance.LeaveTypeId, source.EndDate, ct);
            if (policy.Status != LeavePolicyResolutionStatus.Resolved) { skipped++; continue; }
            var rule = await _db.LeavePolicyRules.AsNoTracking().Include(x => x.EntitlementRule).SingleAsync(x => x.TenantId == tenantId && x.Id == policy.LeavePolicyRuleId, ct);
            var e = rule.EntitlementRule;
            if (e is null || e.EntitlementMode != EntitlementMode.Allocated) { skipped++; continue; }
            var carry = e.CarryForwardEnabled ? (e.MaximumCarryForwardQuantity is decimal max ? Math.Min(max, balance.AvailableQuantity) : balance.AvailableQuantity) : 0;
            var lapse = balance.AvailableQuantity - carry;
            var key = $"{tenantId:D}:close:{source.Id:D}:{destination.Id:D}:{balance.EmployeeId:D}:{balance.LeaveTypeId:D}";
            var occurrence = await ClaimAsync(new LeavePeriodCloseOccurrence { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = balance.EmployeeId, LeaveTypeId = balance.LeaveTypeId, SourceLeavePeriodId = source.Id, DestinationLeavePeriodId = destination.Id, LeavePolicyVersionId = policy.LeavePolicyVersionId!.Value, LeavePolicyRuleId = policy.LeavePolicyRuleId!.Value, OccurrenceKey = key, ClosingQuantity = balance.AvailableQuantity, CarriedQuantity = carry, LapsedQuantity = lapse }, ct);
            if (occurrence is null) { skipped++; continue; }
            try
            {
                var grants = await _db.LeaveEntitlementGrants.Where(x => x.TenantId == tenantId && x.EmployeeLeaveBalanceId == balance.BalanceId && x.Status == "Active" && x.GrantedQuantity - x.ReservedQuantity - x.ConsumedQuantity - x.ExpiredQuantity > 0).OrderBy(x => x.ExpiresOn == null).ThenBy(x => x.ExpiresOn).ThenBy(x => x.GrantedOn).ThenBy(x => x.Id).ToListAsync(ct);
                if (lapse > 0 && grants.Count > 0)
                {
                    var remainingLapse = lapse;
                    foreach (var grant in grants)
                    {
                        var amount = Math.Min(remainingLapse, grant.AvailableQuantity);
                        if (amount <= 0) continue;
                        await DebitAsync(tenantId, balance, source, policy, amount, key, grant.Id, ct);
                        remainingLapse -= amount;
                        if (remainingLapse <= 0) break;
                    }
                    if (remainingLapse > 0) throw new InvalidOperationException("Source grants did not cover the period-close lapse.");
                }
                else if (lapse > 0) await DebitAsync(tenantId, balance, source, policy, lapse, key, null, ct);
                var expiresOn = e.CarryForwardExpiryDays is int expiryDays ? destination.StartDate.AddDays(expiryDays) : (DateOnly?)null;
                if (carry > 0) await CreditAsync(tenantId, balance, destination, policy, carry, key, expiresOn, ct);
                if (await FinalizeAsync(occurrence, ct)) { done++; carried += carry; lapsed += lapse; } else skipped++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { failed++; var detail = ex.InnerException?.Message ?? ex.Message; var failure = detail.Length <= 190 ? $"{ex.GetType().Name}:{detail}" : ex.GetType().Name; await _db.LeavePeriodCloseOccurrences.Where(x => x.Id == occurrence.Id && x.ClaimToken == occurrence.ClaimToken).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Failed").SetProperty(x => x.FailureCode, failure), ct); }
        }
        return new(done, skipped, failed, carried, lapsed);
    }

    private async Task DebitAsync(Guid tenant, BalanceRow balance, LeavePeriod period, LeavePolicyResolutionResult p, decimal quantity, string key, Guid? grantId, CancellationToken ct)
    { var identity = $"{period.Id:N}:{balance.EmployeeId:N}:{balance.LeaveTypeId:N}:{grantId:N}"; var result = await _poster.PostDebitAsync(new(tenant, balance.EmployeeId, balance.LeaveTypeId, period.Id, LeaveBalanceTransactionType.Expiry, quantity, period.EndDate, p.LeavePolicyVersionId, p.LeavePolicyRuleId, LeaveBalanceSourceType.Policy, $"LeavePeriodClose:{identity}", LeaveBalanceActorType.System, null, null, $"leave-close-expiry:{identity}", null, grantId), ct); if (!result.Succeeded) throw new InvalidOperationException(result.Message); }
    private async Task CreditAsync(Guid tenant, BalanceRow balance, LeavePeriod period, LeavePolicyResolutionResult p, decimal quantity, string key, DateOnly? expiresOn, CancellationToken ct)
    { var identity = $"{period.Id:N}:{balance.EmployeeId:N}:{balance.LeaveTypeId:N}"; var result = await _poster.PostCreditAsync(new(tenant, balance.EmployeeId, balance.LeaveTypeId, period.Id, LeaveBalanceTransactionType.CarryForward, quantity, period.StartDate, p.LeavePolicyVersionId, p.LeavePolicyRuleId, LeaveBalanceSourceType.CarryForward, $"LeavePeriodClose:{identity}", LeaveBalanceActorType.System, null, null, $"leave-close-carry:{identity}", null, expiresOn), ct); if (!result.Succeeded) throw new InvalidOperationException(result.Message); }
    private async Task<LeavePeriodCloseOccurrence?> ClaimAsync(LeavePeriodCloseOccurrence candidate, CancellationToken ct)
    { var row = await _db.LeavePeriodCloseOccurrences.SingleOrDefaultAsync(x => x.TenantId == candidate.TenantId && x.OccurrenceKey == candidate.OccurrenceKey, ct); if (row is null) { _db.LeavePeriodCloseOccurrences.Add(candidate); try { await _db.SaveChangesAsync(ct); row = candidate; } catch (DbUpdateException) { _db.ClearChangeTracker(); row = await _db.LeavePeriodCloseOccurrences.SingleAsync(x => x.TenantId == candidate.TenantId && x.OccurrenceKey == candidate.OccurrenceKey, ct); } } if (row.Status == "Processed") return null; var token = Guid.NewGuid().ToString("N"); var count = await _db.LeavePeriodCloseOccurrences.Where(x => x.Id == row.Id && (x.Status == "Pending" || x.Status == "Failed") && x.ClaimToken == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Processing").SetProperty(x => x.ClaimToken, token).SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1), ct); if (count != 1) return null; _db.ClearChangeTracker(); return await _db.LeavePeriodCloseOccurrences.AsNoTracking().SingleAsync(x => x.Id == row.Id, ct); }
    private async Task<bool> FinalizeAsync(LeavePeriodCloseOccurrence row, CancellationToken ct) => await _db.LeavePeriodCloseOccurrences.Where(x => x.Id == row.Id && x.Status == "Processing" && x.ClaimToken == row.ClaimToken).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Processed").SetProperty(x => x.ProcessedAtUtc, _clock.GetUtcNow().UtcDateTime), ct) == 1;

    private sealed record BalanceRow(Guid BalanceId, Guid EmployeeId, Guid LeaveTypeId, decimal AvailableQuantity);
}
