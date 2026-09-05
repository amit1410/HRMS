using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LeaveApprovalReadService : ILeaveApprovalReadService
{
    private readonly IHrmsDbContext _db;
    private readonly IEmployeeIdentityResolver _identity;
    private readonly IEmployeeManagerResolver _managerResolver;
    private readonly TimeProvider _timeProvider;

    public LeaveApprovalReadService(
        IHrmsDbContext db,
        IEmployeeIdentityResolver identity,
        IEmployeeManagerResolver managerResolver,
        TimeProvider timeProvider)
    {
        _db = db;
        _identity = identity;
        _managerResolver = managerResolver;
        _timeProvider = timeProvider;
    }

    public async Task<Result<PagedResult<LeaveApprovalListItemDto>>> GetInboxAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var identity = await ResolveApproverAsync(cancellationToken);
        if (!identity.Succeeded)
            return Result<PagedResult<LeaveApprovalListItemDto>>.Failure(identity.Status, identity.Message, identity.Errors);
        if (page < 1 || pageSize is < 1 or > 100)
            return Result<PagedResult<LeaveApprovalListItemDto>>.Invalid("page", "Page must be at least 1 and page size must be between 1 and 100.");

        var asOfDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var candidateEmployeeIds = await _db.LeaveRequests.AsNoTracking()
            .Where(x => x.TenantId == identity.Value!.TenantId && x.Status == LeaveRequestStatus.PendingApproval)
            .Select(x => x.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var managedEmployeeIds = new List<Guid>();
        foreach (var employeeId in candidateEmployeeIds)
        {
            if (await IsCurrentManagerAsync(identity.Value, employeeId, asOfDate, cancellationToken))
                managedEmployeeIds.Add(employeeId);
        }

        var query = _db.LeaveRequests.AsNoTracking()
            .Where(x => x.TenantId == identity.Value.TenantId &&
                        x.Status == LeaveRequestStatus.PendingApproval &&
                        managedEmployeeIds.Contains(x.EmployeeId));
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(x => x.SubmittedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                Request = x,
                Employee = x.Employee!,
                LeaveType = x.LeaveType!
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new LeaveApprovalListItemDto(
            x.Request.Id,
            x.Request.EmployeeId,
            x.Employee.EmployeeCode ?? string.Empty,
            FullName(x.Employee),
            x.Request.LeaveTypeId,
            x.LeaveType.Code,
            x.LeaveType.Name,
            x.Request.StartDate,
            x.Request.EndDate,
            x.Request.RequestedQuantity,
            x.Request.ChargeableQuantity,
            x.Request.Status,
            x.Request.SubmittedAtUtc)).ToList();

        return Result<PagedResult<LeaveApprovalListItemDto>>.Success(new(items, page, pageSize, total));
    }

    public async Task<Result<LeaveApprovalDetailDto>> GetByIdAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        var identity = await ResolveApproverAsync(cancellationToken);
        if (!identity.Succeeded)
            return Result<LeaveApprovalDetailDto>.Failure(identity.Status, identity.Message, identity.Errors);

        var request = await _db.LeaveRequests.AsNoTracking()
            .Include(x => x.Employee)
            .Include(x => x.LeaveType)
            .Include(x => x.LeavePeriod)
            .Include(x => x.Days)
            .Include(x => x.Events)
            .SingleOrDefaultAsync(x => x.TenantId == identity.Value!.TenantId && x.Id == requestId, cancellationToken);

        if (request is null || request.Employee is null ||
            request.EmployeeId == identity.Value.EmployeeId ||
            !await IsCurrentManagerAsync(identity.Value, request.EmployeeId, DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime), cancellationToken))
            return Result<LeaveApprovalDetailDto>.NotFound("Leave request was not found.");

        return Result<LeaveApprovalDetailDto>.Success(new(
            request.Id,
            request.EmployeeId,
            request.Employee.EmployeeCode ?? string.Empty,
            FullName(request.Employee),
            request.LeaveTypeId,
            request.LeaveType!.Code,
            request.LeaveType.Name,
            request.StartDate,
            request.EndDate,
            request.RequestedQuantity,
            request.ChargeableQuantity,
            request.Status,
            request.SubmittedAtUtc,
            request.LeavePeriodId,
            request.LeavePeriod!.Code,
            request.LeavePeriod.Name,
            request.LeavePolicyVersionId,
            request.Days.OrderBy(x => x.Date).ThenBy(x => x.Id)
                .Select(x => new LeaveRequestDetailDayDto(x.Date, x.RequestedQuantity, x.ChargeableQuantity, x.DayClassification, x.CalculationReason, x.IsEmployeeRequested)).ToList(),
            request.Events.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id)
                .Select(x => new LeaveRequestEventDto(x.EventType, x.OccurredAtUtc)).ToList()));
    }

    private async Task<Result<RuntimeEmployeeIdentity>> ResolveApproverAsync(CancellationToken cancellationToken)
    {
        var identity = await _identity.ResolveCurrentAsync(cancellationToken);
        if (!identity.Succeeded || identity.Value is null)
            return identity;

        var active = await _db.Users.AsNoTracking().AnyAsync(
            x => x.TenantId == identity.Value.TenantId && x.Id == identity.Value.UserId && x.IsActive,
            cancellationToken);
        if (!active)
            return Result<RuntimeEmployeeIdentity>.Forbidden("The authenticated account is not active.");

        var hasPermission = await (
            from userRole in _db.UserRoles
            join rolePermission in _db.RolePermissions on userRole.RoleId equals rolePermission.RoleId
            join permission in _db.Permissions on rolePermission.PermissionId equals permission.Id
            where userRole.TenantId == identity.Value.TenantId &&
                  userRole.UserId == identity.Value.UserId &&
                  permission.Name == Permissions.Leave.Approve
            select permission.Id).AnyAsync(cancellationToken);
        return hasPermission
            ? identity
            : Result<RuntimeEmployeeIdentity>.Forbidden("ApproverNotAuthorized: The authenticated account lacks Leave.Approve.");
    }

    private async Task<bool> IsCurrentManagerAsync(
        RuntimeEmployeeIdentity identity,
        Guid employeeId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        if (employeeId == identity.EmployeeId)
            return false;

        var manager = await _managerResolver.ResolveAsync(employeeId, asOfDate, cancellationToken);
        return manager.Succeeded && manager.Value?.Status == EmployeeManagerResolutionStatus.Resolved &&
               manager.Value.ManagerId == identity.EmployeeId;
    }

    private static string FullName(Employee employee) =>
        string.Join(" ", new[] { employee.FirstName, employee.MiddleName, employee.LastName }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
}
