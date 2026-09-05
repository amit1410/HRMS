using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Applies one fixed, request-linked balance operation. The caller owns the transaction and the
/// Employee-scoped serialization lock; this service deliberately never begins or commits a transaction.
/// </summary>
public sealed class LeaveBalanceAccountingService : ILeaveBalanceAccountingService
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public LeaveBalanceAccountingService(IHrmsDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public Task<Result<LeaveBalanceAccountingResult>> ReserveAsync(LeaveBalanceAccountingCommand command, CancellationToken cancellationToken = default) =>
        ApplyAsync(command, LeaveBalanceTransactionType.Reservation, cancellationToken);

    public Task<Result<LeaveBalanceAccountingResult>> ConsumeReservationAsync(LeaveBalanceAccountingCommand command, CancellationToken cancellationToken = default) =>
        ApplyAsync(command, LeaveBalanceTransactionType.Consumption, cancellationToken);

    public Task<Result<LeaveBalanceAccountingResult>> ReleaseReservationAsync(LeaveBalanceAccountingCommand command, CancellationToken cancellationToken = default) =>
        ApplyAsync(command, LeaveBalanceTransactionType.ReservationRelease, cancellationToken);

    public Task<Result<LeaveBalanceAccountingResult>> RestoreConsumptionAsync(LeaveBalanceAccountingCommand command, CancellationToken cancellationToken = default) =>
        ApplyAsync(command, LeaveBalanceTransactionType.CancellationRestore, cancellationToken);

    private async Task<Result<LeaveBalanceAccountingResult>> ApplyAsync(
        LeaveBalanceAccountingCommand command,
        LeaveBalanceTransactionType transactionType,
        CancellationToken cancellationToken)
    {
        var validation = Validate(command);
        if (validation is not null)
            return Result<LeaveBalanceAccountingResult>.Invalid(validation.Value.Field, validation.Value.Message);

        if (_tenantContext.TenantId is not Guid currentTenantId || currentTenantId != command.TenantId)
            return Result<LeaveBalanceAccountingResult>.Unauthorized("The requested tenant is not the authenticated tenant.");

        var idempotencyKey = OperationKey(command, transactionType);
        var fingerprint = Fingerprint(command, transactionType);
        var existing = await _db.LeaveBalanceTransactions.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == command.TenantId && x.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.PayloadFingerprint != fingerprint || existing.TransactionType != transactionType || existing.LeaveRequestId != command.LeaveRequestId)
                return Result<LeaveBalanceAccountingResult>.Conflict("The balance operation key was already used for different accounting data.");
            return await ReplayAsync(existing, cancellationToken);
        }

        // The request lifecycle owns the outer transaction and must acquire the Employee lock before
        // calling this method. This row read/update is therefore compatible with Employee -> Balance order.
        var balance = await _db.EmployeeLeaveBalances.SingleOrDefaultAsync(x =>
            x.TenantId == command.TenantId &&
            x.EmployeeId == command.EmployeeId &&
            x.LeaveTypeId == command.LeaveTypeId &&
            x.LeavePeriodId == command.LeavePeriodId,
            cancellationToken);
        if (balance is null)
            return Result<LeaveBalanceAccountingResult>.NotFound(
                $"{LeaveBalanceAccountingErrorCodes.BalanceNotInitialized}: The Employee leave balance is not initialized.");

        var failure = ApplyProjection(balance, transactionType, command.Quantity);
        if (failure is not null)
            return failure;

        var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
        var ledger = new LeaveBalanceTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = command.TenantId,
            EmployeeLeaveBalanceId = balance.Id,
            EmployeeId = command.EmployeeId,
            LeaveTypeId = command.LeaveTypeId,
            LeavePeriodId = command.LeavePeriodId,
            LeaveRequestId = command.LeaveRequestId,
            TransactionType = transactionType,
            Quantity = command.Quantity,
            EffectiveDate = command.EffectiveDate,
            OccurredAtUtc = occurredAt,
            LeavePolicyVersionId = command.LeavePolicyVersionId,
            LeavePolicyRuleId = command.LeavePolicyRuleId,
            SourceType = LeaveBalanceSourceType.Policy,
            SourceReference = $"LeaveRequest:{command.LeaveRequestId:D}",
            ActorType = command.ActorType,
            ActorUserId = command.ActorUserId,
            ActorEmployeeId = command.ActorEmployeeId,
            CorrelationId = command.CorrelationId,
            IdempotencyKey = idempotencyKey,
            PayloadFingerprint = fingerprint
        };
        _db.LeaveBalanceTransactions.Add(ledger);

        // SaveChanges participates in the caller's transaction. There is intentionally no helper-owned
        // BeginTransactionAsync or CommitAsync, so request, event, projection, and ledger can commit together.
        await _db.SaveChangesAsync(cancellationToken);
        return Result<LeaveBalanceAccountingResult>.Success(ToResult(ledger, balance, false));
    }

    private async Task<Result<LeaveBalanceAccountingResult>> ReplayAsync(LeaveBalanceTransaction existing, CancellationToken cancellationToken)
    {
        var balance = await _db.EmployeeLeaveBalances.AsNoTracking().SingleOrDefaultAsync(
            x => x.TenantId == existing.TenantId && x.Id == existing.EmployeeLeaveBalanceId,
            cancellationToken);
        return balance is null
            ? Result<LeaveBalanceAccountingResult>.Conflict("The idempotent balance operation references a missing balance projection.")
            : Result<LeaveBalanceAccountingResult>.Success(ToResult(existing, balance, true));
    }

    private static Result<LeaveBalanceAccountingResult>? ApplyProjection(
        EmployeeLeaveBalance balance,
        LeaveBalanceTransactionType transactionType,
        decimal quantity)
    {
        switch (transactionType)
        {
            case LeaveBalanceTransactionType.Reservation:
                if (balance.AvailableQuantity < quantity)
                    return Result<LeaveBalanceAccountingResult>.Conflict(
                        $"{LeaveBalanceAccountingErrorCodes.InsufficientLeaveBalance}: Available balance is insufficient for this request.");
                balance.ReservedQuantity += quantity;
                break;
            case LeaveBalanceTransactionType.Consumption:
                if (balance.ReservedQuantity < quantity)
                    return Result<LeaveBalanceAccountingResult>.Conflict("The balance does not contain enough reserved quantity to consume.");
                balance.ReservedQuantity -= quantity;
                balance.ConsumedQuantity += quantity;
                break;
            case LeaveBalanceTransactionType.ReservationRelease:
                if (balance.ReservedQuantity < quantity)
                    return Result<LeaveBalanceAccountingResult>.Conflict("The balance does not contain enough reserved quantity to release.");
                balance.ReservedQuantity -= quantity;
                break;
            case LeaveBalanceTransactionType.CancellationRestore:
                if (balance.ConsumedQuantity < quantity)
                    return Result<LeaveBalanceAccountingResult>.Conflict("The balance does not contain enough consumed quantity to restore.");
                balance.ConsumedQuantity -= quantity;
                break;
            default:
                return Result<LeaveBalanceAccountingResult>.Invalid(
                    "transactionType",
                    $"{LeaveBalanceAccountingErrorCodes.UnsupportedOperation}: The requested operation is not a lifecycle accounting operation.");
        }

        return null;
    }

    private static (string Field, string Message)? Validate(LeaveBalanceAccountingCommand command)
    {
        if (command.TenantId == Guid.Empty || command.LeaveRequestId == Guid.Empty || command.EmployeeId == Guid.Empty ||
            command.LeaveTypeId == Guid.Empty || command.LeavePeriodId == Guid.Empty ||
            command.LeavePolicyVersionId == Guid.Empty || command.LeavePolicyRuleId == Guid.Empty)
            return ("context", "Tenant, LeaveRequest, Employee, LeaveType, LeavePeriod, and captured policy identifiers are required.");
        if (command.Quantity <= 0)
            return ("quantity", "Quantity must be greater than zero.");
        if (command.CorrelationId?.Length > 100)
            return ("correlationId", "CorrelationId is too long.");
        if (command.ActorType == LeaveBalanceActorType.User && command.ActorUserId is null)
            return ("actorUserId", "A user actor requires ActorUserId.");
        return null;
    }

    private static string OperationKey(LeaveBalanceAccountingCommand command, LeaveBalanceTransactionType transactionType) =>
        $"{command.TenantId:D}:leave-request:{command.LeaveRequestId:D}:{transactionType}";

    private static string Fingerprint(LeaveBalanceAccountingCommand command, LeaveBalanceTransactionType transactionType)
    {
        var payload = string.Join('|',
            command.TenantId.ToString("D"), command.LeaveRequestId.ToString("D"), command.EmployeeId.ToString("D"),
            command.LeaveTypeId.ToString("D"), command.LeavePeriodId.ToString("D"),
            command.LeavePolicyVersionId.ToString("D"), command.LeavePolicyRuleId.ToString("D"),
            transactionType.ToString(), command.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
            command.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ((int)command.ActorType).ToString(CultureInfo.InvariantCulture),
            command.ActorUserId?.ToString("D") ?? "", command.ActorEmployeeId?.ToString("D") ?? "", command.CorrelationId ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static LeaveBalanceAccountingResult ToResult(
        LeaveBalanceTransaction transaction,
        EmployeeLeaveBalance balance,
        bool isReplay) =>
        new(transaction.Id, balance.Id, transaction.TransactionType, balance.GrantedQuantity,
            balance.ReservedQuantity, balance.ConsumedQuantity, balance.AvailableQuantity, isReplay);
}
