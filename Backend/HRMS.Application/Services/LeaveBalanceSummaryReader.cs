using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LeaveBalanceSummaryReader : ILeaveBalanceSummaryReader
{
    private readonly IHrmsDbContext _db;
    private readonly IEmployeeIdentityResolver _identity;
    private readonly ILeavePolicyResolver _policyResolver;
    private readonly TimeProvider _timeProvider;

    public LeaveBalanceSummaryReader(IHrmsDbContext db, IEmployeeIdentityResolver identity, ILeavePolicyResolver policyResolver, TimeProvider timeProvider)
    {
        _db = db;
        _identity = identity;
        _policyResolver = policyResolver;
        _timeProvider = timeProvider;
    }

    public async Task<Result<IReadOnlyList<LeaveBalanceSummaryDto>>> GetMineAsync(CancellationToken cancellationToken = default)
    {
        var identity = await _identity.ResolveCurrentAsync(cancellationToken);
        if (!identity.Succeeded || identity.Value is null)
            return Result<IReadOnlyList<LeaveBalanceSummaryDto>>.Failure(identity.Status, identity.Message, identity.Errors);

        var balances = await _db.EmployeeLeaveBalances.AsNoTracking()
            .Where(x => x.TenantId == identity.Value.TenantId && x.EmployeeId == identity.Value.EmployeeId)
            .Select(x => new
            {
                x.LeaveTypeId,
                LeaveTypeCode = x.LeaveType!.Code,
                LeaveTypeName = x.LeaveType.Name,
                LeavePeriodName = x.LeavePeriod!.Name,
                x.GrantedQuantity,
                x.ReservedQuantity,
                x.ConsumedQuantity,
                AvailableQuantity = x.GrantedQuantity - x.ReservedQuantity - x.ConsumedQuantity
            })
            .ToListAsync(cancellationToken);

        var leaveTypes = await _db.LeaveTypes.AsNoTracking()
            .Where(x => x.TenantId == identity.Value.TenantId && x.IsActive)
            .OrderBy(x => x.Code)
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var result = new List<LeaveBalanceSummaryDto>();

        foreach (var leaveType in leaveTypes)
        {
            var typeBalances = balances.Where(x => x.LeaveTypeId == leaveType.Id)
                .OrderBy(x => x.LeavePeriodName)
                .ToList();
            var policy = await _policyResolver.ResolveAsync(identity.Value.TenantId, identity.Value.EmployeeId, leaveType.Id, today, cancellationToken);
            if (policy.Status == LeavePolicyResolutionStatus.Resolved && policy.LeavePolicyRuleId is Guid ruleId)
            {
                var mode = await _db.LeavePolicyEntitlementRules.AsNoTracking()
                    .Where(x => x.TenantId == identity.Value.TenantId && x.LeavePolicyRuleId == ruleId)
                    .Select(x => (EntitlementMode?)x.EntitlementMode)
                    .SingleOrDefaultAsync(cancellationToken);
                if (mode == EntitlementMode.Unlimited)
                {
                    result.Add(new(leaveType.Code, leaveType.Name, EntitlementMode.Unlimited, null, null, null, null, null));
                    continue;
                }
                if (mode == EntitlementMode.NoBalanceRequired)
                    continue;
            }

            result.AddRange(typeBalances.Select(balance => new LeaveBalanceSummaryDto(
                leaveType.Code, leaveType.Name, EntitlementMode.Allocated, balance.LeavePeriodName,
                balance.GrantedQuantity, balance.ReservedQuantity, balance.ConsumedQuantity, balance.AvailableQuantity)));
        }

        return Result<IReadOnlyList<LeaveBalanceSummaryDto>>.Success(result);
    }
}
