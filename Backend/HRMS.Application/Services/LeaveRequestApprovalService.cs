using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Single-step direct-manager approval and rejection. The employee lock is deliberately shared with
/// submission because rejection changes whether the request blocks future overlap checks.
/// </summary>
public sealed class LeaveRequestApprovalService : ILeaveRequestApprovalService
{
    private readonly IHrmsDbContext _db;
    private readonly IEmployeeIdentityResolver _identityResolver;
    private readonly IEmployeeManagerResolver _managerResolver;
    private readonly ILeaveRequestSubmissionLock _employeeLock;
    private readonly TimeProvider _timeProvider;
    private readonly ILeaveRequestSubmissionRetryPolicy? _retryPolicy;
    private readonly ILeaveRequestSubmissionDeadlockClassifier? _deadlockClassifier;
    private readonly ILeaveBalanceAccountingService? _balanceAccountingService;

    public LeaveRequestApprovalService(
        IHrmsDbContext db,
        IEmployeeIdentityResolver identityResolver,
        IEmployeeManagerResolver managerResolver,
        ILeaveRequestSubmissionLock employeeLock,
        TimeProvider timeProvider,
        ILeaveRequestSubmissionRetryPolicy? retryPolicy = null,
        ILeaveRequestSubmissionDeadlockClassifier? deadlockClassifier = null,
        ILeaveBalanceAccountingService? balanceAccountingService = null)
    {
        _db = db;
        _identityResolver = identityResolver;
        _managerResolver = managerResolver;
        _employeeLock = employeeLock;
        _timeProvider = timeProvider;
        _retryPolicy = retryPolicy;
        _deadlockClassifier = deadlockClassifier;
        _balanceAccountingService = balanceAccountingService;
    }

    public Task<Result<LeaveRequestApprovalResult>> ApproveAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        TransitionAsync(requestId, LeaveRequestStatus.Approved, LeaveRequestEventType.Approved, cancellationToken);

    public Task<Result<LeaveRequestApprovalResult>> RejectAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        TransitionAsync(requestId, LeaveRequestStatus.Rejected, LeaveRequestEventType.Rejected, cancellationToken);

    private async Task<Result<LeaveRequestApprovalResult>> TransitionAsync(
        Guid requestId,
        LeaveRequestStatus targetStatus,
        LeaveRequestEventType eventType,
        CancellationToken cancellationToken)
    {
        var identity = await _identityResolver.ResolveCurrentAsync(cancellationToken);
        if (!identity.Succeeded || identity.Value is null)
            return Result<LeaveRequestApprovalResult>.Failure(identity.Status, identity.Message, identity.Errors);

        var requestEmployeeId = await _db.LeaveRequests.AsNoTracking()
            .Where(x => x.TenantId == identity.Value.TenantId && x.Id == requestId)
            .Select(x => (Guid?)x.EmployeeId)
            .SingleOrDefaultAsync(cancellationToken);
        if (requestEmployeeId is null)
            return Result<LeaveRequestApprovalResult>.NotFound("Leave request was not found.");

        try
        {
            var execute = _retryPolicy is null
                ? AttemptAsync(identity.Value, requestId, requestEmployeeId.Value, targetStatus, eventType, cancellationToken)
                : _retryPolicy.ExecuteAsync(
                    attemptCancellationToken => AttemptAsync(identity.Value!, requestId, requestEmployeeId.Value, targetStatus, eventType, attemptCancellationToken),
                    cancellationToken);
            return await execute;
        }
        catch (Exception exception) when (IsDeadlock(exception))
        {
            return Result<LeaveRequestApprovalResult>.Conflict(
                "ConcurrencyConflict: The leave request could not be changed after the maximum deadlock retry attempts.");
        }
    }

    private async Task<Result<LeaveRequestApprovalResult>> AttemptAsync(
        RuntimeEmployeeIdentity identity,
        Guid requestId,
        Guid requestEmployeeId,
        LeaveRequestStatus targetStatus,
        LeaveRequestEventType eventType,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            await _employeeLock.AcquireAsync(identity.TenantId, requestEmployeeId, cancellationToken);

            var request = await _db.LeaveRequests
                .SingleOrDefaultAsync(x => x.TenantId == identity.TenantId && x.Id == requestId, cancellationToken);
            if (request is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<LeaveRequestApprovalResult>.NotFound("Leave request was not found.");
            }

            var authorization = await AuthorizeAsync(identity, request, cancellationToken);
            if (authorization is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return authorization;
            }

            if (request.Status != LeaveRequestStatus.PendingApproval)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<LeaveRequestApprovalResult>.Conflict(
                    $"{LeaveRequestApprovalErrorCodes.InvalidStatusTransition}: Only PendingApproval requests can be approved or rejected.");
            }

            var allocatedAccounting = await ApplyAllocatedAccountingAsync(identity, request, eventType, cancellationToken);
            if (allocatedAccounting is not null && !allocatedAccounting.Succeeded)
            {
                await transaction.RollbackAsync(cancellationToken);
                return allocatedAccounting;
            }

            var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
            request.Status = targetStatus;
            _db.LeaveRequestEvents.Add(new LeaveRequestEvent
            {
                Id = Guid.NewGuid(),
                TenantId = identity.TenantId,
                LeaveRequestId = request.Id,
                EventType = eventType,
                OccurredAtUtc = occurredAt,
                ActorType = LeaveBalanceActorType.User,
                ActorUserId = identity.UserId,
                ActorEmployeeId = identity.EmployeeId
            });

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<LeaveRequestApprovalResult>.Success(new(request.Id, request.Status, eventType, occurredAt));
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ClearChangeTracker();
            if (IsDeadlock(exception)) throw;
            return Result<LeaveRequestApprovalResult>.Conflict("ConcurrencyConflict: The leave request changed during approval.");
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ClearChangeTracker();
            if (IsDeadlock(exception)) throw;
            return Result<LeaveRequestApprovalResult>.Conflict("ConcurrencyConflict: The approval could not be persisted.");
        }
        catch (Exception exception) when (IsDeadlock(exception))
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            finally
            {
                _db.ClearChangeTracker();
            }

            throw;
        }
    }

    private async Task<Result<LeaveRequestApprovalResult>?> ApplyAllocatedAccountingAsync(
        RuntimeEmployeeIdentity identity,
        LeaveRequest request,
        LeaveRequestEventType eventType,
        CancellationToken cancellationToken)
    {
        var entitlementMode = await _db.LeavePolicyRules
            .Where(x => x.TenantId == identity.TenantId &&
                        x.Id == request.LeavePolicyRuleId &&
                        x.LeavePolicyVersionId == request.LeavePolicyVersionId &&
                        x.LeaveTypeId == request.LeaveTypeId)
            .Select(x => x.EntitlementRule == null ? (EntitlementMode?)null : x.EntitlementRule.EntitlementMode)
            .SingleOrDefaultAsync(cancellationToken);
        if (entitlementMode != EntitlementMode.Allocated)
            return null;

        if (_balanceAccountingService is null)
            return Result<LeaveRequestApprovalResult>.Conflict(
                $"{LeaveRequestApprovalErrorCodes.AllocatedReservationNotFound}: Allocated leave reservation accounting is unavailable.");

        var accounting = eventType == LeaveRequestEventType.Approved
            ? await _balanceAccountingService.ConsumeReservationAsync(AccountingCommand(identity, request), cancellationToken)
            : await _balanceAccountingService.ReleaseReservationAsync(AccountingCommand(identity, request), cancellationToken);
        if (accounting.Succeeded)
            return null;

        return Result<LeaveRequestApprovalResult>.Conflict(
            $"{LeaveRequestApprovalErrorCodes.AllocatedReservationNotFound}: The request does not have a sufficient reservation to complete this transition.");
    }

    private static LeaveBalanceAccountingCommand AccountingCommand(RuntimeEmployeeIdentity identity, LeaveRequest request) => new(
        identity.TenantId, request.Id, request.EmployeeId, request.LeaveTypeId, request.LeavePeriodId,
        request.LeavePolicyVersionId, request.LeavePolicyRuleId, request.ChargeableQuantity, request.StartDate,
        LeaveBalanceActorType.User, identity.UserId, identity.EmployeeId, null);

    private async Task<Result<LeaveRequestApprovalResult>?> AuthorizeAsync(
        RuntimeEmployeeIdentity identity,
        LeaveRequest request,
        CancellationToken cancellationToken)
    {
        if (identity.EmployeeId == request.EmployeeId)
            return Result<LeaveRequestApprovalResult>.Forbidden(
                $"{LeaveRequestApprovalErrorCodes.ApproverNotAuthorized}: An employee cannot approve or reject their own request.");

        var active = await _db.Users.AsNoTracking().AnyAsync(
            x => x.TenantId == identity.TenantId && x.Id == identity.UserId && x.IsActive,
            cancellationToken);
        if (!active)
            return Result<LeaveRequestApprovalResult>.Forbidden("The authenticated account is not active.");

        var hasPermission = await (
            from userRole in _db.UserRoles
            join rolePermission in _db.RolePermissions on userRole.RoleId equals rolePermission.RoleId
            join permission in _db.Permissions on rolePermission.PermissionId equals permission.Id
            where userRole.TenantId == identity.TenantId &&
                  userRole.UserId == identity.UserId &&
                  permission.Name == Permissions.Leave.Approve
            select permission.Id).AnyAsync(cancellationToken);
        if (!hasPermission)
            return Result<LeaveRequestApprovalResult>.Forbidden(
                $"{LeaveRequestApprovalErrorCodes.ApproverNotAuthorized}: The authenticated account lacks Leave.Approve.");

        var asOfDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var manager = await _managerResolver.ResolveAsync(request.EmployeeId, asOfDate, cancellationToken);
        if (!manager.Succeeded || manager.Value is null)
            return Result<LeaveRequestApprovalResult>.Failure(manager.Status, manager.Message, manager.Errors);

        if (manager.Value.Status is EmployeeManagerResolutionStatus.LegacyConflict or
            EmployeeManagerResolutionStatus.OverlappingEmployment)
            return Result<LeaveRequestApprovalResult>.Conflict(
                $"{LeaveRequestApprovalErrorCodes.ConfigurationAmbiguity}: The current manager configuration requires reconciliation.");

        if (manager.Value.Status != EmployeeManagerResolutionStatus.Resolved ||
            manager.Value.ManagerId != identity.EmployeeId)
            return Result<LeaveRequestApprovalResult>.Forbidden(
                $"{LeaveRequestApprovalErrorCodes.ApproverNotAuthorized}: The authenticated Employee is not the current direct manager.");

        return null;
    }

    private bool IsDeadlock(Exception exception) => _deadlockClassifier?.IsDeadlock(exception) == true;
}
