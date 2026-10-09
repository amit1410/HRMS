using HRMS.Domain.Enums;

namespace HRMS.Application.Services;

/// <summary>
/// Pure, database-free decision arithmetic for Phase 9c-3. Kept separate from the service so the rules are unit
/// testable: it computes the single overall rating from the reviewer's Rating answers per the configured
/// <see cref="OverallRatingMethod"/>, evaluates configured decision rules deterministically (ascending priority,
/// then rule id), and can validate that a set of active rules is unambiguous (no overlapping ranges).
/// All inputs are plain values taken from the case's immutable snapshot — never the live master.
/// </summary>
public static class ConfirmationDecisionCalculator
{
    /// <summary>A decision rule reduced to the values the engine needs. Ranges are inclusive; null = unbounded.</summary>
    public sealed record RuleCandidate(Guid RuleId, int Priority, decimal? Min, decimal? Max, ConfirmationOutcome Outcome, bool IsActive, string Name);

    /// <summary>The engine's verdict for a case.</summary>
    public sealed record Evaluation(decimal? OverallRating, Guid? AppliedRuleId, string? AppliedRuleName, ConfirmationOutcome Outcome, string Explanation);

    /// <summary>
    /// The single overall rating from all Rating answers across the submitted reviews. Returns null when there
    /// are no rating answers (the caller treats this as "no rule can match").
    /// </summary>
    public static decimal? CalculateOverallRating(IReadOnlyCollection<int> ratingValues, OverallRatingMethod method)
    {
        if (ratingValues is null || ratingValues.Count == 0) return null;
        return method switch
        {
            OverallRatingMethod.MinimumRating => ratingValues.Min(),
            _ => Math.Round(ratingValues.Select(v => (decimal)v).Average(), 3, MidpointRounding.AwayFromZero)
        };
    }

    /// <summary>
    /// Evaluate the configured rules against the overall rating. Rules are matched by ascending priority then id
    /// so the result is deterministic even if ranges were (invalidly) left overlapping. Only active rules match.
    /// </summary>
    public static Evaluation Evaluate(decimal? overallRating, OverallRatingMethod method, IReadOnlyList<RuleCandidate> rules)
    {
        if (overallRating is null)
            return new(null, null, null, ConfirmationOutcome.Pending,
                "No overall rating could be calculated (no Rating answers were submitted), so no decision rule applies.");

        var ordered = rules.Where(r => r.IsActive).OrderBy(r => r.Priority).ThenBy(r => r.RuleId);
        foreach (var rule in ordered)
        {
            if ((rule.Min is null || overallRating >= rule.Min) && (rule.Max is null || overallRating <= rule.Max))
                return new(overallRating, rule.RuleId, rule.Name, rule.Outcome,
                    $"Overall rating {overallRating} ({method}) matched rule '{rule.Name}' [priority {rule.Priority}, range {Bound(rule.Min)}..{Bound(rule.Max)}] → {rule.Outcome}.");
        }

        return new(overallRating, null, null, ConfirmationOutcome.Pending,
            $"Overall rating {overallRating} ({method}) did not match any active decision rule.");
    }

    /// <summary>
    /// Return the first pair of active rules whose inclusive ranges overlap (used to reject ambiguous rule sets),
    /// or null when the set is unambiguous. Treats null bounds as ±infinity.
    /// </summary>
    public static (RuleCandidate First, RuleCandidate Second)? FindOverlap(IReadOnlyList<RuleCandidate> rules)
    {
        var active = rules.Where(r => r.IsActive).ToList();
        for (var i = 0; i < active.Count; i++)
            for (var j = i + 1; j < active.Count; j++)
                if (RangesOverlap(active[i], active[j]))
                    return (active[i], active[j]);
        return null;
    }

    private static bool RangesOverlap(RuleCandidate a, RuleCandidate b)
    {
        var lowerMax = Max(a.Min, b.Min);          // greatest lower bound (null = -inf)
        var upperMin = Min(a.Max, b.Max);          // least upper bound (null = +inf)
        return lowerMax is null || upperMin is null || lowerMax <= upperMin;
    }

    private static decimal? Max(decimal? x, decimal? y) => x is null ? y : y is null ? x : Math.Max(x.Value, y.Value);
    private static decimal? Min(decimal? x, decimal? y) => x is null ? y : y is null ? x : Math.Min(x.Value, y.Value);
    private static string Bound(decimal? v) => v?.ToString() ?? "∞";
}
