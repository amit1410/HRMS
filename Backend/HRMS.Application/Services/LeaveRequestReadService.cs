using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LeaveRequestReadService : ILeaveRequestReadService
{
    private readonly IHrmsDbContext _db;
    private readonly IEmployeeIdentityResolver _identity;

    public LeaveRequestReadService(IHrmsDbContext db, IEmployeeIdentityResolver identity)
    {
        _db = db;
        _identity = identity;
    }

    public async Task<Result<PagedResult<LeaveRequestListItemDto>>> GetMineAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var identity = await _identity.ResolveCurrentAsync(cancellationToken);
        if (!identity.Succeeded) return Result<PagedResult<LeaveRequestListItemDto>>.Failure(identity.Status, identity.Message, identity.Errors);
        if (page < 1 || pageSize is < 1 or > 100) return Result<PagedResult<LeaveRequestListItemDto>>.Invalid("page", "Page must be at least 1 and page size must be between 1 and 100.");

        var query = _db.LeaveRequests.AsNoTracking()
            .Where(x => x.TenantId == identity.Value!.TenantId && x.EmployeeId == identity.Value.EmployeeId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.SubmittedAtUtc).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new LeaveRequestListItemDto(x.Id, x.LeaveTypeId, x.LeaveType!.Code, x.LeaveType.Name, x.StartDate, x.EndDate, x.RequestedQuantity, x.ChargeableQuantity, x.Status, x.SubmittedAtUtc, x.LeavePeriodId, x.LeavePolicyVersionId))
            .ToListAsync(cancellationToken);
        return Result<PagedResult<LeaveRequestListItemDto>>.Success(new(rows, page, pageSize, total));
    }

    public async Task<Result<LeaveRequestDetailDto>> GetMineByIdAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var identity = await _identity.ResolveCurrentAsync(cancellationToken);
        if (!identity.Succeeded) return Result<LeaveRequestDetailDto>.Failure(identity.Status, identity.Message, identity.Errors);

        var request = await _db.LeaveRequests.AsNoTracking()
            .Include(x => x.LeaveType).Include(x => x.LeavePeriod).Include(x => x.Days).Include(x => x.Events)
            .SingleOrDefaultAsync(x => x.Id == requestId && x.TenantId == identity.Value!.TenantId && x.EmployeeId == identity.Value.EmployeeId, cancellationToken);
        if (request is null) return Result<LeaveRequestDetailDto>.NotFound("Leave request was not found.");

        return Result<LeaveRequestDetailDto>.Success(new(
            request.Id, request.LeaveType!.Id, request.LeaveType.Code, request.LeaveType.Name,
            request.StartDate, request.EndDate, request.RequestedQuantity, request.ChargeableQuantity,
            request.Status, request.SubmittedAtUtc, request.LeavePeriodId, request.LeavePeriod!.Code,
            request.LeavePeriod.Name, request.LeavePolicyVersionId,
            request.Days.OrderBy(x => x.Date).ThenBy(x => x.Id).Select(x => new LeaveRequestDetailDayDto(x.Date, x.RequestedQuantity, x.ChargeableQuantity, x.DayClassification, x.CalculationReason, x.IsEmployeeRequested)).ToList(),
            request.Events.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id).Select(x => new LeaveRequestEventDto(x.EventType, x.OccurredAtUtc)).ToList()));
    }
}
