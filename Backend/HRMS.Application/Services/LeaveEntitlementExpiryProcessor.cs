using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LeaveEntitlementExpiryProcessor : ILeaveEntitlementExpiryProcessor
{
    private readonly IHrmsDbContext _db; private readonly ITenantContext _tenant; private readonly ILeaveBalanceTransactionPoster _poster;
    public LeaveEntitlementExpiryProcessor(IHrmsDbContext db, ITenantContext tenant, ILeaveBalanceTransactionPoster poster) { _db = db; _tenant = tenant; _poster = poster; }

    public async Task<LeaveEntitlementExpiryResult> ProcessAsync(DateOnly throughDate, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return new(0, 0, 0);
        var grants = await _db.LeaveEntitlementGrants.Where(x => x.TenantId == tenantId && x.SourceType == LeaveBalanceSourceType.CarryForward && x.ExpiresOn != null && x.ExpiresOn <= throughDate && x.Status == "Active").OrderBy(x => x.ExpiresOn).ThenBy(x => x.GrantedOn).ThenBy(x => x.Id).ToListAsync(ct);
        var processed = 0; var skipped = 0; decimal expired = 0;
        foreach (var grant in grants)
        {
            var quantity = grant.AvailableQuantity;
            if (quantity <= 0)
            {
                await _db.LeaveEntitlementGrants.Where(x => x.TenantId == tenantId && x.Id == grant.Id && x.Status == "Active")
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Expired"), ct);
                skipped++;
                continue;
            }
            var key = $"leave-grant-expiry:{grant.Id:D}:{grant.ExpiresOn:yyyy-MM-dd}";
            var result = await _poster.PostDebitAsync(new(tenantId, grant.EmployeeId, grant.LeaveTypeId, grant.LeavePeriodId, LeaveBalanceTransactionType.Expiry, quantity, grant.ExpiresOn!.Value, null, null, LeaveBalanceSourceType.Policy, $"LeaveEntitlementGrant:{grant.Id:D}", LeaveBalanceActorType.System, null, null, key, null, grant.Id), ct);
            if (result.Succeeded) { processed++; expired += quantity; } else skipped++;
        }
        return new(processed, skipped, expired);
    }
}
