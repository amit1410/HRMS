using HRMS.Domain.Entities;
using HRMS.Domain.Enums;

namespace HRMS.Application.EmployeeCodes;

public sealed class EmployeeCodeRuleMatcher
{
    public EmployeeCodeRule? Match(IEnumerable<EmployeeCodeRule> rules, EmployeeCodeGenerationContext context) =>
        rules.Where(r => r.Status == EmployeeCodeRuleStatus.Active)
            .Where(r => r.Conditions.Count == 0 || r.Conditions.All(c => Matches(c, context)))
            .OrderBy(r => r.Priority)
            .ThenByDescending(r => r.IsDefault)
            .ThenBy(r => r.Id)
            .FirstOrDefault();

    public EmployeeCodeRule? Match(
        IEnumerable<EmployeeCodeRule> rules,
        IReadOnlyDictionary<EmployeeCodeConditionField, string?> values,
        IReadOnlyDictionary<EmployeeCodeConditionField, Guid?> referenceIds) =>
        rules.Where(r => r.Status == EmployeeCodeRuleStatus.Active && !r.IsDefault && r.Conditions.Count > 0)
            .Where(r => r.Conditions.All(c => Matches(c, values, referenceIds)))
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .FirstOrDefault()
        ?? rules.Where(r => r.Status == EmployeeCodeRuleStatus.Active).SingleOrDefault(r => r.IsDefault);

    private static bool Matches(EmployeeCodeRuleCondition condition, EmployeeCodeGenerationContext context)
    {
        if (condition.Operator != EmployeeCodeConditionOperator.Equals) return false;
        var actual = context.Values.TryGetValue(condition.Field, out var value) ? value : null;
        var expected = condition.Value ?? condition.ReferenceId?.ToString();
        return actual is not null && expected is not null && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(
        EmployeeCodeRuleCondition condition,
        IReadOnlyDictionary<EmployeeCodeConditionField, string?> values,
        IReadOnlyDictionary<EmployeeCodeConditionField, Guid?> referenceIds)
    {
        if (condition.Operator != EmployeeCodeConditionOperator.Equals) return false;
        if (condition.ReferenceId is Guid referenceId)
            return referenceIds.TryGetValue(condition.Field, out var actualId) && actualId == referenceId;
        return values.TryGetValue(condition.Field, out var actualCode) && actualCode is not null &&
               string.Equals(actualCode, condition.Value, StringComparison.OrdinalIgnoreCase);
    }
}
