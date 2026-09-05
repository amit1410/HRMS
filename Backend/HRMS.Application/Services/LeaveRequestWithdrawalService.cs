using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LeaveRequestWithdrawalService : ILeaveRequestWithdrawalService
{
    private readonly IHrmsDbContext _db;
    private readonly IEmployeeIdentityResolver _identityResolver;
    private readonly ILeaveRequestSubmissionLock _employeeLock;
    private readonly TimeProvider _timeProvider;
    private readonly ILeaveRequestSubmissionRetryPolicy? _retryPolicy;
    private readonly ILeaveRequestSubmissionDeadlockClassifier? _deadlockClassifier;
    private readonly ILeaveBalanceAccountingService? _balanceAccountingService;

    public LeaveRequestWithdrawalService(
        IHrmsDbContext db,
        IEmployeeIdentityResolver identityResolver,
        ILeaveRequestSubmissionLock employeeLock,
        TimeProvider timeProvider,
        ILeaveRequestSubmissionRetryPolicy? retryPolicy = null,
        ILeaveRequestSubmissionDeadlockClassifier? deadlockClassifier = null,
        ILeaveBalanceAccountingService? balanceAccountingService = null)
    {
        _db = db;
        _identityResolver = identityResolver;
        _employeeLock = employeeLock;
        _timeProvider = timeProvider;
        _retryPolicy = retryPolicy;
        _deadlockClassifier = deadlockClassifier;
        _balanceAccountingService = balanceAccountingService;
    }

    public async Task<Result<LeaveRequestWithdrawalResult>> WithdrawAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var identity = await _identityResolver.ResolveCurrentAsync(cancellationToken);
        if (!identity.Succeeded || identity.Value is null)
            return Result<LeaveRequestWithdrawalResult>.Failure(identity.Status, identity.Message, identity.Errors);

        var requestEmployeeId = await _db.LeaveRequests.AsNoTracking()
            .Where(x => x.TenantId == identity.Value.TenantId && x.Id == requestId)
            .Select(x => (Guid?)x.EmployeeId)
            .SingleOrDefaultAsync(cancellationToken);
        if (requestEmployeeId is null)
            return Result<LeaveRequestWithdrawalResult>.NotFound("Leave request was not found.");

        try
        {
            var execute = _retryPolicy is null
                ? AttemptAsync(identity.Value, requestId, requestEmployeeId.Value, cancellationToken)
                : _retryPolicy.ExecuteAsync(
                    attemptCancellationToken => AttemptAsync(identity.Value!, requestId, requestEmployeeId.Value, attemptCancellationToken),
                    cancellationToken);
            return await execute;
        }
        catch (Exception exception) when (IsDeadlock(exception))
        {
            return Result<LeaveRequestWithdrawalResult>.Conflict(
                "ConcurrencyConflict: The leave request could not be withdrawn after the maximum deadlock retry attempts.");
        }
    }

    private async Task<Result<LeaveRequestWithdrawalResult>> AttemptAsync(
        RuntimeEmployeeIdentity identity,
        Guid requestId,
        Guid requestEmployeeId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            await _employeeLock.AcquireAsync(identity.TenantId, requestEmployeeId, cancellationToken);

            var request = await _db.LeaveRequests
                .SingleOrDefaultAsync(x => x.TenantId == identity.TenantId && x.Id == requestId, cancellationToken);
            if (request is null || request.EmployeeId != identity.EmployeeId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<LeaveRequestWithdrawalResult>.NotFound("Leave request was not found.");
            }

            if (request.Status != LeaveRequestStatus.PendingApproval)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<LeaveRequestWithdrawalResult>.Conflict(
                    $"{LeaveRequestWithdrawalErrorCodes.InvalidStatusTransition}: Only PendingApproval requests can be withdrawn.");
            }

            var entitlementMode = await _db.LeavePolicyRules
                .Where(x => x.TenantId == identity.TenantId &&
                            x.Id == request.LeavePolicyRuleId &&
                            x.LeavePolicyVersionId == request.LeavePolicyVersionId &&
                            x.LeaveTypeId == request.LeaveTypeId)
                .Select(x => x.EntitlementRule == null ? (EntitlementMode?)null : x.EntitlementRule.EntitlementMode)
                .SingleOrDefaultAsync(cancellationToken);
            if (entitlementMode == EntitlementMode.Allocated)
            {
                if (_balanceAccountingService is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<LeaveRequestWithdrawalResult>.Conflict(
                        $"{LeaveRequestWithdrawalErrorCodes.AllocatedReservationNotFound}: Allocated leave reservation accounting is unavailable.");
                }

                var accounting = await _balanceAccountingService.ReleaseReservationAsync(
                    new(identity.TenantId, request.Id, request.EmployeeId, request.LeaveTypeId, request.LeavePeriodId,
                        request.LeavePolicyVersionId, request.LeavePolicyRuleId, request.ChargeableQuantity, request.StartDate,
                        LeaveBalanceActorType.User, identity.UserId, identity.EmployeeId, null), cancellationToken);
                if (!accounting.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<LeaveRequestWithdrawalResult>.Conflict(
                        $"{LeaveRequestWithdrawalErrorCodes.AllocatedReservationNotFound}: The request does not have a sufficient reservation to be withdrawn.");
                }
            }

            var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
            request.Status = LeaveRequestStatus.Withdrawn;
            _db.LeaveRequestEvents.Add(new LeaveRequestEvent
            {
                Id = Guid.NewGuid(),
                TenantId = identity.TenantId,
                LeaveRequestId = request.Id,
                EventType = LeaveRequestEventType.Withdrawn,
                OccurredAtUtc = occurredAt,
                ActorType = LeaveBalanceActorType.User,
                ActorUserId = identity.UserId,
                ActorEmployeeId = identity.EmployeeId
            });

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<LeaveRequestWithdrawalResult>.Success(
                new(request.Id, request.Status, LeaveRequestEventType.Withdrawn, occurredAt));
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ClearChangeTracker();
            if (IsDeadlock(exception)) throw;
            return Result<LeaveRequestWithdrawalResult>.Conflict("ConcurrencyConflict: The leave request changed during withdrawal.");
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ClearChangeTracker();
            if (IsDeadlock(exception)) throw;
            return Result<LeaveRequestWithdrawalResult>.Conflict("ConcurrencyConflict: The withdrawal could not be persisted.");
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

    private bool IsDeadlock(Exception exception) => _deadlockClassifier?.IsDeadlock(exception) == true;
}
