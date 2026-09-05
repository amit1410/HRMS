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
/// The only mutation boundary for the initial finite Leave balance foundation. It accepts only
/// unambiguous credit transactions; request reservations, consumption, manual adjustments, and
/// compensating credits belong to later runtime phases.
/// </summary>
public sealed class LeaveBalanceTransactionPoster : ILeaveBalanceTransactionPoster
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public LeaveBalanceTransactionPoster(IHrmsDbContext db, ITenantContext tenantContext, TimeProvider timeProvider)
    {
        _db = db;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
    }

    public async Task<Result<LeaveBalanceCreditResult>> PostCreditAsync(
        LeaveBalanceCreditCommand command,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId || tenantId != command.TenantId)
            return Result<LeaveBalanceCreditResult>.Unauthorized("The requested tenant is not the authenticated tenant.");
        if (command.EmployeeId == Guid.Empty || command.LeaveTypeId == Guid.Empty || command.LeavePeriodId == Guid.Empty)
            return Result<LeaveBalanceCreditResult>.Invalid("A valid Employee, LeaveType, and LeavePeriod are required.");
        if (!Enum.IsDefined(command.TransactionType) || command.TransactionType is not (LeaveBalanceTransactionType.Opening or LeaveBalanceTransactionType.Accrual or LeaveBalanceTransactionType.ExternalGrant))
            return Result<LeaveBalanceCreditResult>.Invalid("transactionType", "Only Opening, Accrual, and ExternalGrant credits are supported.");
        if (command.Quantity <= 0)
            return Result<LeaveBalanceCreditResult>.Invalid("quantity", "Quantity must be greater than zero.");
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            return Result<LeaveBalanceCreditResult>.Invalid("idempotencyKey", "An idempotency key is required.");
        if (command.IdempotencyKey.Trim().Length > 200)
            return Result<LeaveBalanceCreditResult>.Invalid("idempotencyKey", "IdempotencyKey is too long.");

        command = command with
        {
            IdempotencyKey = command.IdempotencyKey.Trim(),
            SourceReference = command.SourceReference?.Trim(),
            CorrelationId = command.CorrelationId?.Trim()
        };

        var sourceError = ValidateSource(command);
        if (sourceError is not null)
            return Result<LeaveBalanceCreditResult>.Invalid(sourceError.Value.Field, sourceError.Value.Message);
        if ((command.LeavePolicyVersionId is null) != (command.LeavePolicyRuleId is null))
            return Result<LeaveBalanceCreditResult>.Invalid("policy", "PolicyVersionId and LeavePolicyRuleId must be supplied together.");
        if (command.CorrelationId?.Length > 100)
            return Result<LeaveBalanceCreditResult>.Invalid("correlationId", "CorrelationId is too long.");
        if (command.ActorType == LeaveBalanceActorType.User && command.ActorUserId is null)
            return Result<LeaveBalanceCreditResult>.Invalid("actorUserId", "A user actor requires ActorUserId.");
        if (command.ActorType != LeaveBalanceActorType.User && command.ActorUserId is not null)
            return Result<LeaveBalanceCreditResult>.Invalid("actorUserId", "ActorUserId is only valid for a user actor.");

        if (!await _db.Employees.AnyAsync(x => x.TenantId == tenantId && x.Id == command.EmployeeId, cancellationToken))
            return Result<LeaveBalanceCreditResult>.Invalid("employeeId", "Employee was not found in the tenant.");
        if (!await _db.LeaveTypes.AnyAsync(x => x.TenantId == tenantId && x.Id == command.LeaveTypeId, cancellationToken))
            return Result<LeaveBalanceCreditResult>.Invalid("leaveTypeId", "LeaveType was not found in the tenant.");
        if (!await _db.LeavePeriods.AnyAsync(x => x.TenantId == tenantId && x.Id == command.LeavePeriodId, cancellationToken))
            return Result<LeaveBalanceCreditResult>.Invalid("leavePeriodId", "LeavePeriod was not found in the tenant.");
        if (command.ActorUserId is Guid actorUserId && !await _db.Users.AnyAsync(x => x.TenantId == tenantId && x.Id == actorUserId, cancellationToken))
            return Result<LeaveBalanceCreditResult>.Invalid("actorUserId", "Actor user was not found in the tenant.");
        if (command.ActorEmployeeId is Guid actorEmployeeId && !await _db.Employees.AnyAsync(x => x.TenantId == tenantId && x.Id == actorEmployeeId, cancellationToken))
            return Result<LeaveBalanceCreditResult>.Invalid("actorEmployeeId", "Actor employee was not found in the tenant.");

        if (command.LeavePolicyRuleId is Guid ruleId && command.LeavePolicyVersionId is Guid versionId)
        {
            var ruleValid = await _db.LeavePolicyRules.AnyAsync(
                x => x.TenantId == tenantId && x.Id == ruleId && x.LeavePolicyVersionId == versionId && x.LeaveTypeId == command.LeaveTypeId,
                cancellationToken);
            if (!ruleValid)
                return Result<LeaveBalanceCreditResult>.Invalid("policy", "The policy references do not belong to this tenant, LeaveType, and version.");
        }

        var fingerprint = Fingerprint(command);
        var existing = await _db.LeaveBalanceTransactions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.IdempotencyKey == command.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadFingerprint, fingerprint, StringComparison.Ordinal))
                return Result<LeaveBalanceCreditResult>.Conflict("The idempotency key was already used for a different transaction.");
            return await ExistingResultAsync(existing, cancellationToken);
        }

        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        try
        {
            var balance = await _db.EmployeeLeaveBalances.SingleOrDefaultAsync(x =>
                x.TenantId == tenantId && x.EmployeeId == command.EmployeeId &&
                x.LeaveTypeId == command.LeaveTypeId && x.LeavePeriodId == command.LeavePeriodId, cancellationToken);
            if (balance is null)
            {
                balance = new EmployeeLeaveBalance
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = command.EmployeeId,
                    LeaveTypeId = command.LeaveTypeId, LeavePeriodId = command.LeavePeriodId,
                    GrantedQuantity = 0, ReservedQuantity = 0, ConsumedQuantity = 0
                };
                _db.EmployeeLeaveBalances.Add(balance);
            }

            balance.GrantedQuantity += command.Quantity;
            var ledger = new LeaveBalanceTransaction
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeLeaveBalanceId = balance.Id,
                EmployeeId = command.EmployeeId, LeaveTypeId = command.LeaveTypeId, LeavePeriodId = command.LeavePeriodId,
                TransactionType = command.TransactionType, Quantity = command.Quantity, EffectiveDate = command.EffectiveDate,
                OccurredAtUtc = _timeProvider.GetUtcNow().UtcDateTime, LeavePolicyVersionId = command.LeavePolicyVersionId,
                LeavePolicyRuleId = command.LeavePolicyRuleId, SourceType = command.SourceType, SourceReference = command.SourceReference,
                ActorType = command.ActorType, ActorUserId = command.ActorUserId, ActorEmployeeId = command.ActorEmployeeId,
                CorrelationId = command.CorrelationId, IdempotencyKey = command.IdempotencyKey, PayloadFingerprint = fingerprint
            };
            _db.LeaveBalanceTransactions.Add(ledger);
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<LeaveBalanceCreditResult>.Success(ToResult(ledger, balance));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<LeaveBalanceCreditResult>.Conflict("The balance changed concurrently. Retry with the same idempotency key.");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Result<LeaveBalanceCreditResult>.Conflict("The balance transaction could not be posted because the state changed concurrently or violated an integrity constraint.");
        }
    }

    private async Task<Result<LeaveBalanceCreditResult>> ExistingResultAsync(LeaveBalanceTransaction existing, CancellationToken ct)
    {
        var balance = await _db.EmployeeLeaveBalances.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == existing.TenantId && x.Id == existing.EmployeeLeaveBalanceId, ct);
        return balance is null
            ? Result<LeaveBalanceCreditResult>.Conflict("The idempotent transaction references a missing balance projection.")
            : Result<LeaveBalanceCreditResult>.Success(ToResult(existing, balance));
    }

    private static (string Field, string Message)? ValidateSource(LeaveBalanceCreditCommand command)
    {
        if ((command.TransactionType is LeaveBalanceTransactionType.Opening or LeaveBalanceTransactionType.Accrual) && command.SourceType != LeaveBalanceSourceType.Policy)
            return ("sourceType", "Opening and Accrual require the Policy source.");
        if (command.TransactionType == LeaveBalanceTransactionType.ExternalGrant && command.SourceType != LeaveBalanceSourceType.External)
            return ("sourceType", "ExternalGrant requires the External source.");
        if (command.TransactionType == LeaveBalanceTransactionType.ExternalGrant && string.IsNullOrWhiteSpace(command.SourceReference))
            return ("sourceReference", "ExternalGrant requires a source reference.");
        if (command.SourceReference?.Length > 200)
            return ("sourceReference", "SourceReference is too long.");
        return null;
    }

    private static LeaveBalanceCreditResult ToResult(LeaveBalanceTransaction transaction, EmployeeLeaveBalance balance) =>
        new(transaction.Id, balance.Id, balance.GrantedQuantity, balance.ReservedQuantity, balance.ConsumedQuantity, balance.AvailableQuantity);

    private static string Fingerprint(LeaveBalanceCreditCommand c)
    {
        var payload = string.Join('|',
            c.TenantId.ToString("D"), c.EmployeeId.ToString("D"), c.LeaveTypeId.ToString("D"), c.LeavePeriodId.ToString("D"),
            ((int)c.TransactionType).ToString(CultureInfo.InvariantCulture), c.Quantity.ToString("0.000", CultureInfo.InvariantCulture),
            c.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), c.LeavePolicyVersionId?.ToString("D") ?? "",
            c.LeavePolicyRuleId?.ToString("D") ?? "", ((int)c.SourceType).ToString(CultureInfo.InvariantCulture), c.SourceReference ?? "",
            ((int)c.ActorType).ToString(CultureInfo.InvariantCulture), c.ActorUserId?.ToString("D") ?? "", c.ActorEmployeeId?.ToString("D") ?? "",
            c.CorrelationId ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}

public sealed class LeaveBalanceReader : ILeaveBalanceReader
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public LeaveBalanceReader(IHrmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<Result<LeaveBalanceSnapshot>> GetAsync(Guid tenantId, Guid employeeId, Guid leaveTypeId, Guid leavePeriodId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid currentTenantId || currentTenantId != tenantId)
            return Result<LeaveBalanceSnapshot>.Unauthorized("The requested tenant is not the authenticated tenant.");
        var balance = await _db.EmployeeLeaveBalances.AsNoTracking().SingleOrDefaultAsync(x =>
            x.TenantId == tenantId && x.EmployeeId == employeeId && x.LeaveTypeId == leaveTypeId && x.LeavePeriodId == leavePeriodId, cancellationToken);
        return balance is null
            ? Result<LeaveBalanceSnapshot>.NotFound("No finite balance exists for the requested Employee, LeaveType, and LeavePeriod.")
            : Result<LeaveBalanceSnapshot>.Success(new(balance.Id, balance.TenantId, balance.EmployeeId, balance.LeaveTypeId, balance.LeavePeriodId, balance.GrantedQuantity, balance.ReservedQuantity, balance.ConsumedQuantity, balance.AvailableQuantity, balance.RowVersion));
    }
}
