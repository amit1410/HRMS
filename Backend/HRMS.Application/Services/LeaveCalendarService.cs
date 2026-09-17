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
    private readonly ILeaveAuthorizationService? _authorization;
    private readonly TimeProvider _timeProvider;

    public LeaveCalendarService(IHrmsDbContext db, IEmployeeIdentityResolver identity, IEmployeeManagerResolver managerResolver, TimeProvider timeProvider, ILeaveAuthorizationService? authorization = null)
    {
        _db = db;
        _identity = identity;
        _managerResolver = managerResolver;
        _authorization = authorization;
        _timeProvider = timeProvider;
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

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        IQueryable<Guid> authorizedEmployeeIds;
        var legacyCanViewTeam = false;
        var authorizationEnabled = _authorization is not null;
        if (_authorization is null)
        {
            // Direct construction in existing lifecycle tests has no authorization service;
            // preserve the legacy manager fallback and apply it below after the query.
            authorizedEmployeeIds = _db.Employees.AsNoTracking()
                .Where(x => x.TenantId == identity.Value.TenantId).Select(x => x.Id);
            legacyCanViewTeam = await HasApprovalPermissionAsync(identity.Value, cancellationToken);
        }
        else
        {
            authorizedEmployeeIds = _db.Employees.AsNoTracking().Where(x => x.Id == identity.Value.EmployeeId).Select(x => x.Id);
            var scopedAccess = await _authorization.BuildEmployeePredicateAsync(
                Permissions.Leave.Approve, includeManager: true, includeRoleScope: true, today, cancellationToken);
            if (scopedAccess.Succeeded && scopedAccess.Value is not null)
                authorizedEmployeeIds = authorizedEmployeeIds.Union(_db.Employees.AsNoTracking().Where(scopedAccess.Value).Select(x => x.Id));
        }

        var candidates = await _db.LeaveRequests.AsNoTracking()
            .Where(x => x.TenantId == identity.Value.TenantId &&
                        x.StartDate <= to && x.EndDate >= from &&
                        (x.Status == LeaveRequestStatus.Approved || x.Status == LeaveRequestStatus.PendingApproval) &&
                        (authorizationEnabled || x.Status == LeaveRequestStatus.Approved || legacyCanViewTeam) &&
                        authorizedEmployeeIds.Contains(x.EmployeeId))
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
            if (_authorization is null && !visible)
            {
                var manager = await _managerResolver.ResolveAsync(candidate.EmployeeId, today, cancellationToken);
                visible = manager.Succeeded && manager.Value?.Status == EmployeeManagerResolutionStatus.Resolved && manager.Value.ManagerId == identity.Value.EmployeeId;
            }
            if (visible || authorizationEnabled)
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
               where userRole.TenantId == identity.TenantId && userRole.UserId == identity.UserId &&
                     userRole.EffectiveFrom <= DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime) &&
                     (userRole.EffectiveTo == null || userRole.EffectiveTo >= DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime)) &&
                     permission.Name == Permissions.Leave.Approve
               select permission.Id).AnyAsync(cancellationToken);

}
