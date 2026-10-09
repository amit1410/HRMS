using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Confirmation;

/// <summary>
/// Summary data for the HR confirmation dashboard, similar to HrLeaveDashboardSummaryDto.
/// </summary>
public sealed record HrConfirmationDashboardSummaryDto
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public string? CurrentConfirmationPeriodName { get; init; } = null;

    public ConfirmationDashboardKpis Kpis { get; init; } = new();

    public IReadOnlyList<ConfirmationStatusBreakdownItem> StatusBreakdown { get; init; } = Array.Empty<ConfirmationStatusBreakdownItem>();
    public IReadOnlyList<ConfirmationOutcomeBreakdownItem> OutcomeBreakdown { get; init; } = Array.Empty<ConfirmationOutcomeBreakdownItem>();
    public IReadOnlyList<DepartmentBreakdownItem> DepartmentBreakdown { get; init; } = Array.Empty<DepartmentBreakdownItem>();
    public IReadOnlyList<DesignationBreakdownItem> DesignationBreakdown { get; init; } = Array.Empty<DesignationBreakdownItem>();

    public IReadOnlyList<ConfirmationTrendItem> Trend { get; init; } = Array.Empty<ConfirmationTrendItem>();
    public IReadOnlyList<PendingActionItem> PendingActions { get; init; } = Array.Empty<PendingActionItem>();
}

public sealed record ConfirmationDashboardKpis
{
    public int ProbationEmployees { get; init; }
    public int UpcomingConfirmations { get; init; }
    public int OverdueConfirmations { get; init; }
    public int PendingManagerActions { get; init; }
    public int PendingHrbpActions { get; init; }
    public int ConfirmedEmployees { get; init; }
    public int ExtendedProbation { get; init; }
    public int RejectedNotConfirmed { get; init; }
}

public sealed record ConfirmationStatusBreakdownItem
{
    public ConfirmationReviewCaseStatus Status { get; init; }
    public int Count { get; init; }
}

public sealed record ConfirmationOutcomeBreakdownItem
{
    public ConfirmationOutcome Outcome { get; init; }
    public int Count { get; init; }
}

public sealed record DepartmentBreakdownItem
{
    public string Name { get; init; } = string.Empty;
    public int EmployeeCount { get; init; }
}

public sealed record DesignationBreakdownItem
{
    public string Name { get; init; } = string.Empty;
    public int EmployeeCount { get; init; }
}

public sealed record ConfirmationTrendItem
{
    public string Period { get; init; } = string.Empty;
    public int ConfirmedCount { get; init; }
    public int ExtendedCount { get; init; }
    public int RejectedCount { get; init; }
}

public sealed record PendingActionItem
{
    public string EmployeeName { get; init; } = string.Empty;
    public string? EmployeeCode { get; init; }
    public string ProbationCycleKey { get; init; } = string.Empty;
    public DateOnly DueDate { get; init; }
    public string? ActionType { get; init; } // "Manager" or "HRBP"
    public string? PendingReviewerType { get; init; } // "ReportingManager" or "Hrbp"
}