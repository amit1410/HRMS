using HRMS.Application.Common;
using HRMS.Domain.Entities;

namespace HRMS.Application.Abstractions;

public interface IReimbursementPolicyResolver
{
    Task<ReimbursementPolicyVersion?> ResolveAsync(Guid categoryId, DateOnly expenseDate, CancellationToken ct = default);
}

public interface IReimbursementEligibilityService
{
    Task<Result<ReimbursementEligibilityResult>> EvaluateAsync(Guid employeeId, Guid categoryId, decimal amount, DateOnly expenseDate, CancellationToken ct = default);
}

public sealed record ReimbursementEligibilityResult(bool Eligible, decimal EligibleAmount, IReadOnlyList<string> BlockingIssues, IReadOnlyList<string> Warnings, Guid? PolicyVersionId);
