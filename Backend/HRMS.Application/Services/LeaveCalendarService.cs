using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LeaveCalendarService : ILeaveCalendarService
{
    private readonly IHrmsDbContext _db;
    private readonly IEmployeeIdentityResolver _identity;
    private readonly IEmployeeManagerResolver _managerResolver;

    public LeaveCalendarService(IHrmsDbContext db, IEmployeeIdentityResolver identity, IEmployeeManagerResolver managerResolver)
    {
        _db = db;
        _identity = identity;
        _managerResolver = managerResolver;
    }

    public async Task<Result<IReadOnlyList<LeaveCalendarEventDto>>> GetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from)
            return Result<IReadOnlyList<LeaveCalendarEventDto>>.Invalid("to", "The calendar end date must not be before the start date.");
        if (to.DayNumber - from.DayNumber > 366)
            return Result<IReadOnlyList<LeaveCalendarEventDto>>.Invalid("to", "The calendar range cannot exceed 366 days.");

        var identity = await _identity.ResolveCurrentAsync(cancellationToken);
        if (!identity.Succeeded || identity.Value is null)
            return Result<IReadOnlyList<LeaveCalendarEventDto>>.Failure(identity.Status, identity.Message, identity.Errors);

        var canViewTeam = await HasApprovalPermissionAsync(identity.Value, cancellationToken);
        var candidates = await _db.LeaveRequests.AsNoTracking()
            .Where(x => x.TenantId == identity.Value.TenantId &&
                        x.StartDate <= to && x.EndDate >= from &&
                        (x.Status == LeaveRequestStatus.Approved || canViewTeam && x.Status == LeaveRequestStatus.PendingApproval))
            .Select(x => new
            {
                x.Id,
                x.EmployeeId,
                x.StartDate,
                x.EndDate,
                x.ChargeableQuantity,
                x.Status,
                EmployeeCode = x.Employee!.EmployeeCode,
                x.Employee.FirstName,
                x.Employee.MiddleName,
                x.Employee.LastName,
                LeaveTypeCode = x.LeaveType!.Code,
                LeaveTypeName = x.LeaveType.Name
            })
            .OrderBy(x => x.StartDate)
            .ThenBy(x => x.EmployeeId)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var result = new List<LeaveCalendarEventDto>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var visible = candidate.EmployeeId == identity.Value.EmployeeId;
            if (!visible && canViewTeam)
            {
                var manager = await _managerResolver.ResolveAsync(candidate.EmployeeId, candidate.StartDate, cancellationToken);
                visible = manager.Succeeded && manager.Value?.Status == EmployeeManagerResolutionStatus.Resolved &&
                          manager.Value.ManagerId == identity.Value.EmployeeId;
            }

            if (visible)
                result.Add(new(candidate.Id, candidate.EmployeeId, candidate.EmployeeCode ?? string.Empty,
                    string.Join(" ", new[] { candidate.FirstName, candidate.MiddleName, candidate.LastName }.Where(value => !string.IsNullOrWhiteSpace(value))),
                    candidate.LeaveTypeCode, candidate.LeaveTypeName, candidate.StartDate, candidate.EndDate,
                    candidate.ChargeableQuantity, candidate.Status));
        }

        return Result<IReadOnlyList<LeaveCalendarEventDto>>.Success(result);
    }

    private async Task<bool> HasApprovalPermissionAsync(RuntimeEmployeeIdentity identity, CancellationToken cancellationToken) =>
        await (from userRole in _db.UserRoles
               join rolePermission in _db.RolePermissions on userRole.RoleId equals rolePermission.RoleId
               join permission in _db.Permissions on rolePermission.PermissionId equals permission.Id
               where userRole.TenantId == identity.TenantId && userRole.UserId == identity.UserId && permission.Name == Permissions.Leave.Approve
               select permission.Id).AnyAsync(cancellationToken);
}
