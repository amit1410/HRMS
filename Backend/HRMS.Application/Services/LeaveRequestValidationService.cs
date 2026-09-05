using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public static class LeaveRequestValidationErrorCodes
{
    public const string UnsupportedConfiguration = "UnsupportedConfiguration";
    public const string MinimumRequestQuantityNotMet = "MinimumRequestQuantityNotMet";
    public const string MaximumRequestQuantityExceeded = "MaximumRequestQuantityExceeded";
    public const string MaximumConsecutiveLeaveExceeded = "MaximumConsecutiveLeaveExceeded";
    public const string RequestCountLimitExceeded = "RequestCountLimitExceeded";
    public const string RequestQuantityLimitExceeded = "RequestQuantityLimitExceeded";
}

/// <summary>
/// Side-effect-free orchestration for the deliberately restricted Leave request MVP.
/// This service reads identity and policy context, but never creates request, event, balance,
/// or ledger rows.
/// </summary>
public sealed class LeaveRequestValidationService : ILeaveRequestValidationService
{
    private const int MaxIdempotencyKeyLength = 200;
    private readonly IHrmsDbContext _db;
    private readonly IEmployeeIdentityResolver _identityResolver;
    private readonly IEffectiveEmploymentResolver _employmentResolver;
    private readonly ILeavePeriodResolver _periodResolver;
    private readonly ILeavePolicyResolver _policyResolver;

    public LeaveRequestValidationService(
        IHrmsDbContext db,
        IEmployeeIdentityResolver identityResolver,
        IEffectiveEmploymentResolver employmentResolver,
        ILeavePeriodResolver periodResolver,
        ILeavePolicyResolver policyResolver)
    {
        _db = db;
        _identityResolver = identityResolver;
        _employmentResolver = employmentResolver;
        _periodResolver = periodResolver;
        _policyResolver = policyResolver;
    }

    public async Task<Result<LeaveRequestValidationResult>> ValidateAsync(
        LeaveRequestValidationInput input,
        CancellationToken cancellationToken = default)
    {
        var shape = ValidateShape(input);
        if (shape is not null)
            return Result<LeaveRequestValidationResult>.Invalid(shape.Value.Field, shape.Value.Message);

        var identity = await _identityResolver.ResolveCurrentAsync(cancellationToken);
        if (!identity.Succeeded || identity.Value is null)
            return Result<LeaveRequestValidationResult>.Failure(identity.Status, identity.Message, identity.Errors);

        var subject = identity.Value;
        var employee = await _db.Employees.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == subject.TenantId && x.Id == subject.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<LeaveRequestValidationResult>.NotFound("The authenticated Employee was not found in the tenant.");
        if (employee.Status != EmployeeStatus.Active)
            return Result<LeaveRequestValidationResult>.Forbidden("The authenticated Employee is not active.");

        var leaveType = await _db.LeaveTypes.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == subject.TenantId && x.Id == input.LeaveTypeId, cancellationToken);
        if (leaveType is null)
            return Result<LeaveRequestValidationResult>.NotFound("The requested LeaveType was not found.");
        if (!leaveType.IsActive)
            return Result<LeaveRequestValidationResult>.Invalid("leaveTypeId", "The requested LeaveType is inactive.");
        if (leaveType.DefaultUnit != LeaveUnit.Day)
            return LeaveRequestValidationFailures.Unsupported("Hourly or non-day LeaveTypes are not supported by the current request runtime.");

        var startContext = await ResolveContextAsync(subject, input.LeaveTypeId, input.StartDate, cancellationToken);
        if (!startContext.Succeeded)
            return Result<LeaveRequestValidationResult>.Failure(startContext.Status, startContext.Message, startContext.Errors);

        var rule = await _db.LeavePolicyRules.AsNoTracking()
            .Include(x => x.EligibilityRule)
            .Include(x => x.EntitlementRule)
            .Include(x => x.RequestRule)
            .Include(x => x.CalendarRule)
            .Include(x => x.AttachmentRule)
            .SingleOrDefaultAsync(x => x.TenantId == subject.TenantId &&
                x.Id == startContext.Value!.PolicyRuleId &&
                x.LeavePolicyVersionId == startContext.Value.PolicyVersionId &&
                x.LeaveTypeId == input.LeaveTypeId && x.IsActive, cancellationToken);
        if (rule is null)
            return Result<LeaveRequestValidationResult>.NotFound("The resolved Leave Policy rule was not found.");

        var supportedPolicy = ValidateSupportedPolicy(rule);
        if (supportedPolicy is not null)
            return LeaveRequestValidationFailures.Unsupported(supportedPolicy);
        var context = startContext.Value!;

        var clubbingExists = await _db.LeavePolicyClubbingRules.AsNoTracking().AnyAsync(x =>
            x.TenantId == subject.TenantId &&
            x.LeavePolicyVersionId == context.PolicyVersionId &&
            x.Relation == ClubbingRelation.NotAllowed &&
            (x.LowerLeavePolicyRuleId == rule.Id || x.HigherLeavePolicyRuleId == rule.Id), cancellationToken);
        if (clubbingExists)
            return LeaveRequestValidationFailures.Unsupported("A configured Clubbing rule requires adjacency evaluation, which is not supported by the MVP.");

        var span = await ValidateSingleContextAsync(subject, input.LeaveTypeId, input.StartDate, input.EndDate, context, cancellationToken);
        if (!span.Succeeded)
            return span.Error!;

        var days = BuildFullDayResults(input.StartDate, input.EndDate);
        var quantity = days.Sum(x => x.ChargeableQuantity);
        var requestRule = rule.RequestRule;
        if (requestRule?.MinimumRequestQuantity is decimal min && quantity < min)
            return LimitFailure(LeaveRequestValidationErrorCodes.MinimumRequestQuantityNotMet, "The request is below the configured minimum quantity.");
        if (requestRule?.MaximumRequestQuantity is decimal max && quantity > max)
            return LimitFailure(LeaveRequestValidationErrorCodes.MaximumRequestQuantityExceeded, "The request exceeds the configured maximum quantity.");
        if (requestRule?.MaximumConsecutiveQuantity is decimal consecutive && MaximumConsecutiveQuantity(days) > consecutive)
            return LimitFailure(LeaveRequestValidationErrorCodes.MaximumConsecutiveLeaveExceeded, "The request exceeds the configured maximum consecutive quantity.");

        var limitFailure = await ValidatePeriodLimitsAsync(
            subject.TenantId,
            subject.EmployeeId,
            input.LeaveTypeId,
            context.LeavePeriodId,
            input.IdempotencyKey.Trim(),
            days,
            quantity,
            requestRule,
            cancellationToken);
        if (limitFailure is not null)
            return limitFailure;

        var entitlementRule = rule.EntitlementRule!;
        var entitlement = entitlementRule.EntitlementMode;
        return Result<LeaveRequestValidationResult>.Success(new(
            subject.EmployeeId,
            input.LeaveTypeId,
            context.EmploymentHistoryId,
            context.LeavePeriodId,
            context.PolicyVersionId,
            context.PolicyRuleId,
            context.Gender,
            input.StartDate,
            input.EndDate,
            quantity,
            quantity,
            days,
            entitlement,
            entitlement == EntitlementMode.Allocated,
            false,
            input.IdempotencyKey.Trim(),
            LeaveRequestValidationFingerprint.Create(subject.EmployeeId, input, days),
            context.PolicyPriority,
            context.PolicySpecificity));
    }

    private async Task<Result<ValidationContext>> ResolveContextAsync(
        RuntimeEmployeeIdentity subject,
        Guid leaveTypeId,
        DateOnly date,
        CancellationToken ct)
    {
        var employment = await _employmentResolver.ResolveAsync(subject.TenantId, subject.EmployeeId, date, ct);
        if (employment.Status != EffectiveEmploymentResolutionStatus.Resolved || employment.Employment is null)
        {
            var status = employment.Status switch
            {
                EffectiveEmploymentResolutionStatus.ConfigurationAmbiguity => ResultStatus.Conflict,
                EffectiveEmploymentResolutionStatus.InvalidTenant => ResultStatus.Unauthorized,
                _ => ResultStatus.NotFound
            };
            return Result<ValidationContext>.Failure(status, employment.Message);
        }

        var period = await _periodResolver.ResolveAsync(subject.TenantId, date, ct);
        if (period.Status != LeavePeriodResolutionStatus.Resolved || period.Period is null)
        {
            var status = period.Status switch
            {
                LeavePeriodResolutionStatus.ConfigurationAmbiguity => ResultStatus.Conflict,
                LeavePeriodResolutionStatus.InvalidTenant => ResultStatus.Unauthorized,
                _ => ResultStatus.NotFound
            };
            return Result<ValidationContext>.Failure(status, period.Message);
        }

        var policy = await _policyResolver.ResolveAsync(subject.TenantId, subject.EmployeeId, leaveTypeId, date, ct);
        if (policy.Status != LeavePolicyResolutionStatus.Resolved ||
            policy.LeavePolicyVersionId is not Guid versionId || policy.LeavePolicyRuleId is not Guid ruleId)
        {
            var status = policy.Status switch
            {
                LeavePolicyResolutionStatus.ConfigurationAmbiguity or LeavePolicyResolutionStatus.EffectiveEmploymentAmbiguous => ResultStatus.Conflict,
                LeavePolicyResolutionStatus.InvalidTenant => ResultStatus.Unauthorized,
                _ => ResultStatus.NotFound
            };
            return Result<ValidationContext>.Failure(status, policy.Message);
        }

        return Result<ValidationContext>.Success(new(
            employment.Employment.HistoryId,
            period.Period.Id,
            versionId,
            ruleId,
            employment.Employment.Gender,
            policy.Priority ?? 0,
            policy.Specificity ?? 0));
    }

    private async Task<ContextCheck> ValidateSingleContextAsync(
        RuntimeEmployeeIdentity subject,
        Guid leaveTypeId,
        DateOnly start,
        DateOnly end,
        ValidationContext expected,
        CancellationToken ct)
    {
        for (var date = start; ; date = date.AddDays(1))
        {
            if (date != start)
            {
                var current = await ResolveContextAsync(subject, leaveTypeId, date, ct);
                if (!current.Succeeded)
                    return new(Result<LeaveRequestValidationResult>.Failure(current.Status, current.Message, current.Errors));
                var actual = current.Value!;
                if (actual.EmploymentHistoryId != expected.EmploymentHistoryId ||
                    actual.LeavePeriodId != expected.LeavePeriodId ||
                    actual.PolicyVersionId != expected.PolicyVersionId ||
                    actual.PolicyRuleId != expected.PolicyRuleId)
                    return new(Result<LeaveRequestValidationResult>.Invalid("dateRange", "The request crosses an employment, LeavePeriod, or Policy context boundary."));
            }
            if (date == end)
                break;
        }
        return new(null);
    }

    private static (string Field, string Message)? ValidateShape(LeaveRequestValidationInput input)
    {
        if (input.LeaveTypeId == Guid.Empty) return ("leaveTypeId", "LeaveTypeId is required.");
        if (input.StartDate > input.EndDate) return ("dateRange", "StartDate must be on or before EndDate.");
        if (string.IsNullOrWhiteSpace(input.IdempotencyKey) || input.IdempotencyKey.Trim().Length > MaxIdempotencyKeyLength)
            return ("idempotencyKey", "IdempotencyKey is required and must be 200 characters or fewer.");
        return null;
    }

    private static string? ValidateSupportedPolicy(LeavePolicyRule rule)
    {
        var eligibility = rule.EligibilityRule;
        if (eligibility is not null &&
            (eligibility.EligibilityMode != EligibilityMode.Immediate ||
             eligibility.ProbationMode != ProbationMode.Allowed ||
             eligibility.NoticePeriodMode != NoticePeriodMode.Allowed))
            return "The resolved Eligibility configuration requires an unsupported employment rule.";

        var request = rule.RequestRule;
        if (request is not null && (request.MinimumAdvanceNoticeDays != 0 ||
            request.BackdatedRequestMode != BackdatedRequestMode.NotAllowed ||
            request.MaximumBackdatedDays is not null))
            return "The resolved Request Rule contains a time-dependent or historical-limit rule unsupported by the MVP.";

        var calendar = rule.CalendarRule;
        if (calendar is not null && (calendar.SandwichMode != SandwichMode.Disabled ||
            calendar.HolidayTreatment != HolidayTreatment.Exclude ||
            calendar.WeekOffTreatment != WeekOffTreatment.Exclude ||
            calendar.ApplyToPrefix || calendar.ApplyToSuffix || calendar.ApplyToBetween))
            return "The resolved Calendar configuration requires an unavailable calendar or Sandwich source.";

        if (rule.AttachmentRule is { AttachmentRequirement: not AttachmentRequirement.None })
            return "The resolved Attachment configuration requires attachment runtime support.";
        if (rule.EntitlementRule is null)
            return "The resolved Policy has no Entitlement configuration.";
        if (!Enum.IsDefined(rule.EntitlementRule.EntitlementMode))
            return "The resolved Policy has an unsupported Entitlement mode.";
        return null;
    }

    private static IReadOnlyList<LeaveRequestDayValidationResult> BuildFullDayResults(DateOnly start, DateOnly end)
    {
        var days = new List<LeaveRequestDayValidationResult>();
        for (var date = start; ; date = date.AddDays(1))
        {
            // The restricted MVP has no authoritative calendar source, so it deliberately leaves
            // classification/reason unset instead of fabricating a WorkingDay classification.
            days.Add(new(date, 1.000m, 1.000m, null, null, true));
            if (date == end) break;
        }
        return days;
    }

    private async Task<Result<LeaveRequestValidationResult>?> ValidatePeriodLimitsAsync(
        Guid tenantId,
        Guid employeeId,
        Guid leaveTypeId,
        Guid leavePeriodId,
        string idempotencyKey,
        IReadOnlyList<LeaveRequestDayValidationResult> candidateDays,
        decimal candidateQuantity,
        LeavePolicyRequestRule? requestRule,
        CancellationToken ct)
    {
        if (requestRule is null ||
            (requestRule.MaximumRequestsPerPeriod is null && requestRule.MaximumQuantityPerPeriod is null))
            return null;

        if (requestRule.RequestLimitPeriod is null)
            return LeaveRequestValidationFailures.Unsupported("A request count or quantity limit has no configured period.");

        if (requestRule.RequestLimitPeriod == RequestLimitPeriod.LeavePeriod)
        {
            var history = await _db.LeaveRequests.AsNoTracking()
                .Where(x => x.TenantId == tenantId &&
                    x.EmployeeId == employeeId &&
                    x.LeaveTypeId == leaveTypeId &&
                    x.LeavePeriodId == leavePeriodId &&
                    x.IdempotencyKey != idempotencyKey &&
                    (x.Status == LeaveRequestStatus.PendingApproval || x.Status == LeaveRequestStatus.Approved))
                .Select(x => x.ChargeableQuantity)
                .ToListAsync(ct);

            return CheckPeriodTotals(
                history.Count + 1,
                history.Sum() + candidateQuantity,
                requestRule);
        }

        var candidateMonths = candidateDays
            .Select(x => (x.Date.Year, x.Date.Month))
            .Distinct()
            .ToArray();
        var historyDays = await _db.LeaveRequestDays.AsNoTracking()
            .Where(x => x.TenantId == tenantId &&
                x.LeaveRequest!.TenantId == tenantId &&
                x.LeaveRequest.EmployeeId == employeeId &&
                x.LeaveRequest.LeaveTypeId == leaveTypeId &&
                x.LeaveRequest.IdempotencyKey != idempotencyKey &&
                (x.LeaveRequest.Status == LeaveRequestStatus.PendingApproval ||
                 x.LeaveRequest.Status == LeaveRequestStatus.Approved))
            .Select(x => new { x.LeaveRequestId, x.Date, x.ChargeableQuantity })
            .ToListAsync(ct);

        foreach (var month in candidateMonths)
        {
            var historical = historyDays
                .Where(x => x.Date.Year == month.Year && x.Date.Month == month.Month)
                .ToList();
            var candidateMonthQuantity = candidateDays
                .Where(x => x.Date.Year == month.Year && x.Date.Month == month.Month)
                .Sum(x => x.ChargeableQuantity);
            var failure = CheckPeriodTotals(
                historical.Select(x => x.LeaveRequestId).Distinct().Count() + 1,
                historical.Sum(x => x.ChargeableQuantity) + candidateMonthQuantity,
                requestRule);
            if (failure is not null)
                return failure;
        }

        return null;
    }

    private static Result<LeaveRequestValidationResult>? CheckPeriodTotals(
        int requestCount,
        decimal quantity,
        LeavePolicyRequestRule requestRule)
    {
        if (requestRule.MaximumRequestsPerPeriod is int maxRequests && requestCount > maxRequests)
            return LimitFailure(LeaveRequestValidationErrorCodes.RequestCountLimitExceeded, "The request count limit for the configured period has been exceeded.");
        if (requestRule.MaximumQuantityPerPeriod is decimal maxQuantity && quantity > maxQuantity)
            return LimitFailure(LeaveRequestValidationErrorCodes.RequestQuantityLimitExceeded, "The request quantity limit for the configured period has been exceeded.");
        return null;
    }

    private static decimal MaximumConsecutiveQuantity(IReadOnlyList<LeaveRequestDayValidationResult> days)
    {
        var maximum = 0m;
        var running = 0m;
        DateOnly? previous = null;
        foreach (var day in days.OrderBy(x => x.Date))
        {
            running = previous is DateOnly prior && day.Date == prior.AddDays(1)
                ? running + day.ChargeableQuantity
                : day.ChargeableQuantity;
            maximum = Math.Max(maximum, running);
            previous = day.Date;
        }
        return maximum;
    }

    private static Result<LeaveRequestValidationResult> LimitFailure(string code, string message) =>
        Result<LeaveRequestValidationResult>.Invalid("quantity", $"{code}: {message}");

    private sealed record ValidationContext(
        Guid EmploymentHistoryId,
        Guid LeavePeriodId,
        Guid PolicyVersionId,
        Guid PolicyRuleId,
        Gender Gender,
        int PolicyPriority,
        int PolicySpecificity);

    private sealed record ContextCheck(Result<LeaveRequestValidationResult>? Error)
    {
        public bool Succeeded => Error is null;
    }

}

public static class LeaveRequestValidationFingerprint
{
    public static string Create(Guid employeeId, LeaveRequestValidationInput input, IReadOnlyList<LeaveRequestDayValidationResult> days)
    {
        var canonical = string.Join('|',
            "leave-request-v1",
            $"employee:{employeeId:D}",
            $"leaveType:{input.LeaveTypeId:D}",
            $"start:{input.StartDate:yyyy-MM-dd}",
            $"end:{input.EndDate:yyyy-MM-dd}",
            $"days:{string.Join(',', days.Select(x => x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

internal static class LeaveRequestValidationFailures
{
    public static Result<LeaveRequestValidationResult> Unsupported(string detail) =>
        Result<LeaveRequestValidationResult>.Invalid(
            "configuration",
            $"{LeaveRequestValidationErrorCodes.UnsupportedConfiguration}: {detail}");
}
