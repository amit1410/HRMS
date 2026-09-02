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

    private static bool Matches(EmployeeCodeRuleCondition condition, EmployeeCodeGenerationContext context)
    {
        if (condition.Operator != EmployeeCodeConditionOperator.Equals) return false;
        var actual = context.Values.TryGetValue(condition.Field, out var value) ? value : null;
        var expected = condition.Value ?? condition.ReferenceId?.ToString();
        return actual is not null && expected is not null && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }
}
