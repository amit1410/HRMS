using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LeaveRequestCancellationService : ILeaveRequestCancellationService
{
    private readonly IHrmsDbContext _db;
    private readonly IEmployeeIdentityResolver _identityResolver;
    private readonly ILeaveRequestSubmissionLock _employeeLock;
    private readonly TimeProvider _timeProvider;
    private readonly ILeaveBalanceAccountingService? _balanceAccountingService;
    private readonly ILeaveRequestSubmissionRetryPolicy? _retryPolicy;
    private readonly ILeaveRequestSubmissionDeadlockClassifier? _deadlockClassifier;

    public LeaveRequestCancellationService(
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

    public async Task<Result<LeaveRequestCancellationResult>> CancelAsync(
        Guid requestId,
        CancellationToken cancellationToken = default)
    {
        var identity = await _identityResolver.ResolveCurrentAsync(cancellationToken);
        if (!identity.Succeeded || identity.Value is null)
            return Result<LeaveRequestCancellationResult>.Failure(identity.Status, identity.Message, identity.Errors);

        var requestEmployeeId = await _db.LeaveRequests.AsNoTracking()
            .Where(x => x.TenantId == identity.Value.TenantId && x.Id == requestId)
            .Select(x => (Guid?)x.EmployeeId)
            .SingleOrDefaultAsync(cancellationToken);
        if (requestEmployeeId is null)
            return Result<LeaveRequestCancellationResult>.NotFound("Leave request was not found.");

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
            return Result<LeaveRequestCancellationResult>.Conflict(
                "ConcurrencyConflict: The leave request could not be cancelled after the maximum deadlock retry attempts.");
        }
    }

    private async Task<Result<LeaveRequestCancellationResult>> AttemptAsync(
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
                return Result<LeaveRequestCancellationResult>.NotFound("Leave request was not found.");
            }

            if (request.Status != LeaveRequestStatus.Approved)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<LeaveRequestCancellationResult>.Conflict(
                    $"{LeaveRequestCancellationErrorCodes.InvalidStatusTransition}: Only Approved requests can be cancelled.");
            }

            var capturedRule = await _db.LeavePolicyRules
                .Include(x => x.CancellationRule)
                .Include(x => x.EntitlementRule)
                .SingleOrDefaultAsync(x =>
                    x.TenantId == identity.TenantId &&
                    x.Id == request.LeavePolicyRuleId &&
                    x.LeavePolicyVersionId == request.LeavePolicyVersionId &&
                    x.LeaveTypeId == request.LeaveTypeId,
                    cancellationToken);
            if (capturedRule is null || capturedRule.CancellationRule is null || !capturedRule.CancellationRule.CancelAllowed)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<LeaveRequestCancellationResult>.Conflict(
                    $"{LeaveRequestCancellationErrorCodes.CancellationNotAllowed}: Cancellation is not allowed for this leave request.");
            }

            if (capturedRule.EntitlementRule is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<LeaveRequestCancellationResult>.Conflict(
                    $"{LeaveRequestCancellationErrorCodes.UnsupportedConfiguration}: The captured entitlement configuration is unavailable.");
            }

            if (capturedRule.EntitlementRule.EntitlementMode == EntitlementMode.Allocated)
            {
                if (_balanceAccountingService is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<LeaveRequestCancellationResult>.Conflict(
                        $"{LeaveRequestCancellationErrorCodes.AllocatedCancellationBalanceReleaseNotReady}: Allocated leave cannot be cancelled until balance restoration is configured.");
                }

                var accounting = await _balanceAccountingService.RestoreConsumptionAsync(new(
                    identity.TenantId,
                    request.Id,
                    request.EmployeeId,
                    request.LeaveTypeId,
                    request.LeavePeriodId,
                    request.LeavePolicyVersionId,
                    request.LeavePolicyRuleId,
                    request.ChargeableQuantity,
                    request.StartDate,
                    LeaveBalanceActorType.User,
                    identity.UserId,
                    identity.EmployeeId,
                    null), cancellationToken);
                if (!accounting.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _db.ClearChangeTracker();
                    return Result<LeaveRequestCancellationResult>.Conflict(
                        $"{LeaveRequestCancellationErrorCodes.AllocatedConsumptionNotFound}: The request does not have sufficient consumed balance to restore.");
                }
            }

            else if (capturedRule.EntitlementRule.EntitlementMode is not (EntitlementMode.Unlimited or EntitlementMode.NoBalanceRequired))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<LeaveRequestCancellationResult>.Conflict(
                    $"{LeaveRequestCancellationErrorCodes.UnsupportedConfiguration}: The captured entitlement mode is not supported for cancellation.");
            }

            var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
            request.Status = LeaveRequestStatus.Cancelled;
            _db.LeaveRequestEvents.Add(new LeaveRequestEvent
            {
                Id = Guid.NewGuid(),
                TenantId = identity.TenantId,
                LeaveRequestId = request.Id,
                EventType = LeaveRequestEventType.Cancelled,
                OccurredAtUtc = occurredAt,
                ActorType = LeaveBalanceActorType.User,
                ActorUserId = identity.UserId,
                ActorEmployeeId = identity.EmployeeId
            });

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<LeaveRequestCancellationResult>.Success(
                new(request.Id, request.Status, LeaveRequestEventType.Cancelled, occurredAt));
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ClearChangeTracker();
            if (IsDeadlock(exception)) throw;
            return Result<LeaveRequestCancellationResult>.Conflict("ConcurrencyConflict: The leave request changed during cancellation.");
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ClearChangeTracker();
            if (IsDeadlock(exception)) throw;
            return Result<LeaveRequestCancellationResult>.Conflict("ConcurrencyConflict: The cancellation could not be persisted.");
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
