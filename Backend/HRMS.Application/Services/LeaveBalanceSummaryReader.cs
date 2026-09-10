using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LeaveBalanceSummaryReader : ILeaveBalanceSummaryReader
{
    private readonly IHrmsDbContext _db;
    private readonly IEmployeeIdentityResolver _identity;

    public LeaveBalanceSummaryReader(IHrmsDbContext db, IEmployeeIdentityResolver identity)
    {
        _db = db;
        _identity = identity;
    }

    public async Task<Result<IReadOnlyList<LeaveBalanceSummaryDto>>> GetMineAsync(CancellationToken cancellationToken = default)
    {
        var identity = await _identity.ResolveCurrentAsync(cancellationToken);
        if (!identity.Succeeded || identity.Value is null)
            return Result<IReadOnlyList<LeaveBalanceSummaryDto>>.Failure(identity.Status, identity.Message, identity.Errors);

        var rows = await _db.EmployeeLeaveBalances.AsNoTracking()
            .Where(x => x.TenantId == identity.Value.TenantId && x.EmployeeId == identity.Value.EmployeeId)
            .Select(x => new
            {
                x.Id,
                x.LeaveTypeId,
                LeaveTypeCode = x.LeaveType!.Code,
                LeaveTypeName = x.LeaveType.Name,
                x.LeavePeriodId,
                LeavePeriodCode = x.LeavePeriod!.Code,
                LeavePeriodName = x.LeavePeriod.Name,
                PeriodStartDate = x.LeavePeriod.StartDate,
                PeriodEndDate = x.LeavePeriod.EndDate,
                x.GrantedQuantity,
                x.ReservedQuantity,
                x.ConsumedQuantity,
                AvailableQuantity = x.GrantedQuantity - x.ReservedQuantity - x.ConsumedQuantity
            })
            .OrderBy(x => x.PeriodEndDate)
            .ThenBy(x => x.LeaveTypeCode)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<LeaveBalanceSummaryDto>>.Success(rows.Select(x => new LeaveBalanceSummaryDto(
            x.Id, x.LeaveTypeId, x.LeaveTypeCode, x.LeaveTypeName, x.LeavePeriodId, x.LeavePeriodCode,
            x.LeavePeriodName, x.PeriodStartDate, x.PeriodEndDate, x.GrantedQuantity, x.ReservedQuantity,
            x.ConsumedQuantity, x.AvailableQuantity)).ToList());
    }
}
