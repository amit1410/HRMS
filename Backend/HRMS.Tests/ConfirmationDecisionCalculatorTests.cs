using HRMS.Application.Services;
using HRMS.Domain.Enums;

namespace HRMS.Tests;

/// <summary>
/// Phase 9c-3 — pure decision arithmetic: overall-rating calculation methods, deterministic rule evaluation
/// (threshold / priority / no-match), and the overlapping-range detection that keeps a rule set unambiguous.
/// </summary>
public sealed class ConfirmationDecisionCalculatorTests
{
    private static ConfirmationDecisionCalculator.RuleCandidate Rule(int priority, decimal? min, decimal? max, ConfirmationOutcome outcome, bool active = true, string? name = null)
        => new(Guid.NewGuid(), priority, min, max, outcome, active, name ?? $"P{priority}");

    [Fact]
    public void Average_rating_rounds_half_away_from_zero()
    {
        Assert.Equal(4m, ConfirmationDecisionCalculator.CalculateOverallRating(new[] { 4, 4 }, OverallRatingMethod.AverageOfRatings));
        Assert.Equal(3.5m, ConfirmationDecisionCalculator.CalculateOverallRating(new[] { 3, 4 }, OverallRatingMethod.AverageOfRatings));
    }

    [Fact]
    public void Minimum_rating_takes_the_lowest()
    {
        Assert.Equal(2m, ConfirmationDecisionCalculator.CalculateOverallRating(new[] { 5, 2, 4 }, OverallRatingMethod.MinimumRating));
    }

    [Fact]
    public void No_rating_answers_yields_null()
    {
        Assert.Null(ConfirmationDecisionCalculator.CalculateOverallRating(Array.Empty<int>(), OverallRatingMethod.AverageOfRatings));
    }

    [Fact]
    public void Evaluate_matches_threshold_rule()
    {
        var rules = new[]
        {
            Rule(1, 4, null, ConfirmationOutcome.Confirmed),
            Rule(2, 3, 3.99m, ConfirmationOutcome.ProbationExtended),
            Rule(3, null, 2.99m, ConfirmationOutcome.NotConfirmed)
        };
        Assert.Equal(ConfirmationOutcome.Confirmed, ConfirmationDecisionCalculator.Evaluate(4m, OverallRatingMethod.AverageOfRatings, rules).Outcome);
        Assert.Equal(ConfirmationOutcome.ProbationExtended, ConfirmationDecisionCalculator.Evaluate(3m, OverallRatingMethod.AverageOfRatings, rules).Outcome);
        Assert.Equal(ConfirmationOutcome.NotConfirmed, ConfirmationDecisionCalculator.Evaluate(2m, OverallRatingMethod.AverageOfRatings, rules).Outcome);
    }

    [Fact]
    public void Evaluate_respects_priority_when_ranges_would_both_match()
    {
        // Lower priority number wins deterministically even if two ranges contain the value.
        var rules = new[]
        {
            Rule(2, 3, 5, ConfirmationOutcome.ProbationExtended),
            Rule(1, 4, 5, ConfirmationOutcome.Confirmed)
        };
        Assert.Equal(ConfirmationOutcome.Confirmed, ConfirmationDecisionCalculator.Evaluate(4.5m, OverallRatingMethod.AverageOfRatings, rules).Outcome);
    }

    [Fact]
    public void Evaluate_returns_pending_when_nothing_matches_or_no_rating()
    {
        var rules = new[] { Rule(1, 4, null, ConfirmationOutcome.Confirmed) };
        Assert.Equal(ConfirmationOutcome.Pending, ConfirmationDecisionCalculator.Evaluate(2m, OverallRatingMethod.AverageOfRatings, rules).Outcome);
        Assert.Equal(ConfirmationOutcome.Pending, ConfirmationDecisionCalculator.Evaluate(null, OverallRatingMethod.AverageOfRatings, rules).Outcome);
    }

    [Fact]
    public void Evaluate_ignores_inactive_rules()
    {
        var rules = new[] { Rule(1, 4, null, ConfirmationOutcome.Confirmed, active: false) };
        Assert.Equal(ConfirmationOutcome.Pending, ConfirmationDecisionCalculator.Evaluate(5m, OverallRatingMethod.AverageOfRatings, rules).Outcome);
    }

    [Fact]
    public void FindOverlap_detects_overlapping_ranges()
    {
        var overlapping = new[]
        {
            Rule(1, 3, 5, ConfirmationOutcome.Confirmed, name: "A"),
            Rule(2, 4, 6, ConfirmationOutcome.ProbationExtended, name: "B")
        };
        Assert.NotNull(ConfirmationDecisionCalculator.FindOverlap(overlapping));
    }

    [Fact]
    public void FindOverlap_passes_adjacent_exclusive_ranges()
    {
        var clean = new[]
        {
            Rule(1, 4, null, ConfirmationOutcome.Confirmed),
            Rule(2, 3, 3.99m, ConfirmationOutcome.ProbationExtended),
            Rule(3, null, 2.99m, ConfirmationOutcome.NotConfirmed)
        };
        Assert.Null(ConfirmationDecisionCalculator.FindOverlap(clean));
    }

    [Fact]
    public void FindOverlap_ignores_inactive_rules()
    {
        var rules = new[]
        {
            Rule(1, 3, 5, ConfirmationOutcome.Confirmed),
            Rule(2, 4, 6, ConfirmationOutcome.ProbationExtended, active: false)
        };
        Assert.Null(ConfirmationDecisionCalculator.FindOverlap(rules));
    }
}
