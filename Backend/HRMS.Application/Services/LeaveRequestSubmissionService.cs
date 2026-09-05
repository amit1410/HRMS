using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Atomic, employee-self-service submission orchestration for the Unlimited and NoBalanceRequired MVP.
/// It deliberately has no HTTP or balance-reservation responsibilities.
/// </summary>
public sealed class LeaveRequestSubmissionService : ILeaveRequestSubmissionService
{
    private const string IdempotencyIndexName = "IX_LeaveRequests_TenantId_EmployeeId_IdempotencyKey";
    private readonly IHrmsDbContext _db;
    private readonly IEmployeeIdentityResolver _identityResolver;
    private readonly ILeaveRequestValidationService _validationService;
    private readonly ILeaveRequestSubmissionLock _submissionLock;
    private readonly TimeProvider _timeProvider;
    private readonly Action<Exception>? _diagnosticObserver;
    private readonly ILeaveRequestSubmissionRetryPolicy? _retryPolicy;
    private readonly ILeaveRequestSubmissionDeadlockClassifier? _deadlockClassifier;
    private readonly ILeaveBalanceAccountingService? _balanceAccountingService;

    public LeaveRequestSubmissionService(
        IHrmsDbContext db,
        IEmployeeIdentityResolver identityResolver,
        ILeaveRequestValidationService validationService,
        ILeaveRequestSubmissionLock submissionLock,
        TimeProvider timeProvider,
        Action<Exception>? diagnosticObserver = null,
        ILeaveRequestSubmissionRetryPolicy? retryPolicy = null,
        ILeaveRequestSubmissionDeadlockClassifier? deadlockClassifier = null,
        ILeaveBalanceAccountingService? balanceAccountingService = null)
    {
        _db = db;
        _identityResolver = identityResolver;
        _validationService = validationService;
        _submissionLock = submissionLock;
        _timeProvider = timeProvider;
        _diagnosticObserver = diagnosticObserver;
        _retryPolicy = retryPolicy;
        _deadlockClassifier = deadlockClassifier;
        _balanceAccountingService = balanceAccountingService;
    }

    public async Task<Result<LeaveRequestSubmissionResult>> SubmitAsync(
        LeaveRequestSubmissionInput input,
        CancellationToken cancellationToken = default)
    {
        var identity = await _identityResolver.ResolveCurrentAsync(cancellationToken);
        if (!identity.Succeeded || identity.Value is null)
            return Result<LeaveRequestSubmissionResult>.Failure(identity.Status, identity.Message, identity.Errors);

        if (_retryPolicy is null)
            return await SubmitAttemptAsync(identity.Value, input, cancellationToken);

        try
        {
            return await _retryPolicy.ExecuteAsync(
                attemptCancellationToken => SubmitAttemptAsync(identity.Value!, input, attemptCancellationToken),
                cancellationToken);
        }
        catch (Exception exception) when (IsDeadlock(exception))
        {
            return Result<LeaveRequestSubmissionResult>.Conflict(
                $"{LeaveRequestSubmissionErrorCodes.ConcurrencyConflict}: The request could not be submitted after the maximum deadlock retry attempts.");
        }
    }

    private async Task<Result<LeaveRequestSubmissionResult>> SubmitAttemptAsync(
        RuntimeEmployeeIdentity identity,
        LeaveRequestSubmissionInput input,
        CancellationToken cancellationToken)
    {

        var validation = await ValidateAsync(input, cancellationToken);
        if (!validation.Succeeded || validation.Value is null)
            return Convert(validation);

        // Allocated entitlement cannot enter the persistence path until balance reservation is implemented.
        // Keep this early gate side-effect free; the same gate is repeated after fresh validation below.
        var initialEntitlementFailure = ValidatePersistableEntitlement(validation.Value);
        if (initialEntitlementFailure is not null)
            return initialEntitlementFailure;

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            await _submissionLock.AcquireAsync(identity.TenantId, identity.EmployeeId, cancellationToken);

            var existing = await FindExistingAsync(
                identity.TenantId,
                identity.EmployeeId,
                validation.Value.IdempotencyKey,
                cancellationToken);
            if (existing is not null)
                return await ReplayOrConflictAsync(existing, validation.Value, transaction, cancellationToken);

            // Persistence-sensitive validation is deliberately repeated while the employee scope is held.
            validation = await ValidateAsync(input, cancellationToken);
            if (!validation.Succeeded || validation.Value is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Convert(validation);
            }

            var authoritative = validation.Value;
            if (authoritative.EmployeeId != identity.EmployeeId)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<LeaveRequestSubmissionResult>.Conflict(
                    $"{LeaveRequestSubmissionErrorCodes.ConcurrencyConflict}: The authenticated Employee context changed during submission.");
            }

            var entitlementFailure = ValidatePersistableEntitlement(authoritative);
            if (entitlementFailure is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return entitlementFailure;
            }

            var overlap = await HasBlockingOverlapAsync(
                identity.TenantId,
                identity.EmployeeId,
                authoritative.RequestDays,
                cancellationToken);
            if (overlap)
            {
                await transaction.RollbackAsync(cancellationToken);
                return Result<LeaveRequestSubmissionResult>.Conflict(
                    $"{LeaveRequestSubmissionErrorCodes.Overlap}: The request overlaps an active Leave request.");
            }

            var submittedAt = _timeProvider.GetUtcNow().UtcDateTime;
            var request = CreateRequest(identity.TenantId, authoritative, submittedAt);
            var days = authoritative.RequestDays
                .OrderBy(x => x.Date)
                .Select(x => new LeaveRequestDay
                {
                    Id = Guid.NewGuid(),
                    TenantId = request.TenantId,
                    LeaveRequestId = request.Id,
                    Date = x.Date,
                    RequestedQuantity = x.RequestedQuantity,
                    ChargeableQuantity = x.ChargeableQuantity,
                    DayClassification = x.DayClassification,
                    CalculationReason = x.CalculationReason,
                    IsEmployeeRequested = x.IsEmployeeRequested
                })
                .ToList();
            var submittedEvent = new LeaveRequestEvent
            {
                Id = Guid.NewGuid(),
                TenantId = request.TenantId,
                LeaveRequestId = request.Id,
                EventType = LeaveRequestEventType.Submitted,
                OccurredAtUtc = submittedAt,
                ActorType = LeaveBalanceActorType.User,
                ActorUserId = identity.UserId,
                ActorEmployeeId = identity.EmployeeId
            };

            _db.LeaveRequests.Add(request);
            _db.LeaveRequestDays.AddRange(days);
            _db.LeaveRequestEvents.Add(submittedEvent);
            if (authoritative.EntitlementMode == EntitlementMode.Allocated)
            {
                if (_balanceAccountingService is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<LeaveRequestSubmissionResult>.Invalid(
                        "entitlement",
                        $"{LeaveRequestSubmissionErrorCodes.AllocatedBalanceReservationNotReady}: Allocated leave reservation is not configured.");
                }

                var reservation = await _balanceAccountingService.ReserveAsync(
                    new(
                        identity.TenantId,
                        request.Id,
                        identity.EmployeeId,
                        authoritative.LeaveTypeId,
                        authoritative.LeavePeriodId,
                        authoritative.LeavePolicyVersionId,
                        authoritative.LeavePolicyRuleId,
                        authoritative.ChargeableQuantity,
                        authoritative.StartDate,
                        LeaveBalanceActorType.User,
                        identity.UserId,
                        identity.EmployeeId,
                        null),
                    cancellationToken);
                if (!reservation.Succeeded)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Result<LeaveRequestSubmissionResult>.Failure(reservation.Status, reservation.Message, reservation.Errors);
                }
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result<LeaveRequestSubmissionResult>.Success(ToResult(request, days, false));
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _diagnosticObserver?.Invoke(exception);
            await transaction.RollbackAsync(cancellationToken);
            if (IsDeadlock(exception))
            {
                _db.ClearChangeTracker();
                throw;
            }
            return Result<LeaveRequestSubmissionResult>.Conflict(
                $"{LeaveRequestSubmissionErrorCodes.ConcurrencyConflict}: The request could not be submitted because it conflicted with another change.");
        }
        catch (DbUpdateException exception) when (IsIdempotencyViolation(exception))
        {
            _diagnosticObserver?.Invoke(exception);
            await transaction.RollbackAsync(cancellationToken);
            if (IsDeadlock(exception))
            {
                _db.ClearChangeTracker();
                throw;
            }
            var winner = await FindExistingAsync(
                identity.TenantId,
                identity.EmployeeId,
                validation.Value!.IdempotencyKey,
                cancellationToken);
            if (winner is null)
                return Result<LeaveRequestSubmissionResult>.Conflict(
                    $"{LeaveRequestSubmissionErrorCodes.ConcurrencyConflict}: The idempotency race could not be resolved.");
            return await ReplayOrConflictAsync(winner, validation.Value, null, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            _diagnosticObserver?.Invoke(exception);
            await transaction.RollbackAsync(cancellationToken);
            if (IsDeadlock(exception))
            {
                _db.ClearChangeTracker();
                throw;
            }
            return Result<LeaveRequestSubmissionResult>.Conflict(
                $"{LeaveRequestSubmissionErrorCodes.ConcurrencyConflict}: The request could not be submitted because of a persistence conflict.");
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

    private async Task<Result<LeaveRequestValidationResult>> ValidateAsync(
        LeaveRequestSubmissionInput input,
        CancellationToken cancellationToken) =>
        await _validationService.ValidateAsync(
            new LeaveRequestValidationInput(input.LeaveTypeId, input.StartDate, input.EndDate, input.IdempotencyKey),
            cancellationToken);

    private static Result<LeaveRequestSubmissionResult> Convert(Result<LeaveRequestValidationResult> result) =>
        Result<LeaveRequestSubmissionResult>.Failure(result.Status, result.Message, result.Errors);

    private static Result<LeaveRequestSubmissionResult>? ValidatePersistableEntitlement(LeaveRequestValidationResult result)
    {
        if (result.EntitlementMode == EntitlementMode.Allocated)
            return null;
        if (result.BalanceReservationRequired)
            return Result<LeaveRequestSubmissionResult>.Invalid(
                "entitlement",
                $"{LeaveRequestSubmissionErrorCodes.AllocatedBalanceReservationNotReady}: Allocated leave cannot be submitted until balance reservation is implemented.");
        if (result.EntitlementMode is not (EntitlementMode.Unlimited or EntitlementMode.NoBalanceRequired))
            return Result<LeaveRequestSubmissionResult>.Invalid(
                "entitlement",
                "UnsupportedConfiguration: The resolved entitlement mode cannot be persisted by this submission foundation.");
        return null;
    }

    private async Task<LeaveRequest?> FindExistingAsync(
        Guid tenantId,
        Guid employeeId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        await _db.LeaveRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.IdempotencyKey == idempotencyKey, cancellationToken);

    private async Task<Result<LeaveRequestSubmissionResult>> ReplayOrConflictAsync(
        LeaveRequest existing,
        LeaveRequestValidationResult validation,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(existing.PayloadFingerprint, validation.PayloadFingerprint, StringComparison.Ordinal))
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return Result<LeaveRequestSubmissionResult>.Conflict(
                $"{LeaveRequestSubmissionErrorCodes.IdempotencyConflict}: The idempotency key was already used for a different request.");
        }

        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        var days = await _db.LeaveRequestDays.AsNoTracking()
            .Where(x => x.TenantId == existing.TenantId && x.LeaveRequestId == existing.Id)
            .OrderBy(x => x.Date)
            .ToListAsync(cancellationToken);
        return Result<LeaveRequestSubmissionResult>.Success(ToResult(existing, days, true));
    }

    private async Task<bool> HasBlockingOverlapAsync(
        Guid tenantId,
        Guid employeeId,
        IReadOnlyList<LeaveRequestDayValidationResult> requestedDays,
        CancellationToken cancellationToken)
    {
        var dates = requestedDays.Select(x => x.Date).ToArray();
        return await _db.LeaveRequestDays.AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId &&
            dates.Contains(x.Date) &&
            x.LeaveRequest!.TenantId == tenantId &&
            x.LeaveRequest.EmployeeId == employeeId &&
            (x.LeaveRequest.Status == LeaveRequestStatus.PendingApproval ||
             x.LeaveRequest.Status == LeaveRequestStatus.Approved), cancellationToken);
    }

    private static LeaveRequest CreateRequest(Guid tenantId, LeaveRequestValidationResult result, DateTime submittedAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        EmployeeId = result.EmployeeId,
        LeaveTypeId = result.LeaveTypeId,
        LeavePeriodId = result.LeavePeriodId,
        LeavePolicyVersionId = result.LeavePolicyVersionId,
        LeavePolicyRuleId = result.LeavePolicyRuleId,
        EmployeeEmploymentHistoryId = result.EmployeeEmploymentHistoryId,
        PolicyGenderSnapshot = result.PolicyGenderSnapshot,
        StartDate = result.StartDate,
        EndDate = result.EndDate,
        RequestedQuantity = result.RequestedQuantity,
        ChargeableQuantity = result.ChargeableQuantity,
        Status = LeaveRequestStatus.PendingApproval,
        SubmittedAtUtc = submittedAt,
        IdempotencyKey = result.IdempotencyKey,
        PayloadFingerprint = result.PayloadFingerprint
    };

    private static LeaveRequestSubmissionResult ToResult(LeaveRequest request, IReadOnlyList<LeaveRequestDay> days, bool replay) => new(
        request.Id,
        request.Status,
        request.EmployeeId,
        request.EmployeeEmploymentHistoryId,
        request.LeaveTypeId,
        request.LeavePeriodId,
        request.LeavePolicyVersionId,
        request.LeavePolicyRuleId,
        request.StartDate,
        request.EndDate,
        request.RequestedQuantity,
        request.ChargeableQuantity,
        request.SubmittedAtUtc!.Value,
        days.Select(x => new LeaveRequestSubmissionDay(
            x.Date, x.RequestedQuantity, x.ChargeableQuantity, x.DayClassification, x.CalculationReason, x.IsEmployeeRequested)).ToArray(),
        replay);

    private static bool IsIdempotencyViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(IdempotencyIndexName, StringComparison.OrdinalIgnoreCase))
                return true;
            var number = current.GetType().GetProperty("Number")?.GetValue(current) as int?;
            if (number is (2601 or 2627) && current.Message.Contains("LeaveRequests", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
