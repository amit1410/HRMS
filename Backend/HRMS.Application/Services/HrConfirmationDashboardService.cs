using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class HrConfirmationDashboardService : IHrConfirmationDashboardService
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IEmployeeAccessScopeService _accessScope;
    private readonly TimeProvider _time;

    public HrConfirmationDashboardService(
        IHrmsDbContext db,
        ITenantContext tenant,
        IEmployeeAccessScopeService accessScope,
        TimeProvider? time = null)
    {
        _db = db;
        _tenant = tenant;
        _accessScope = accessScope;
        _time = time ?? TimeProvider.System;
    }

    private DateOnly Today => DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

    public async Task<Result<HrConfirmationDashboardSummaryDto>> GetSummaryAsync(
        HrConfirmationDashboardFilter filter,
        CancellationToken cancellationToken = default)
    {
        if (!_tenant.TenantId.HasValue)
            return Result<HrConfirmationDashboardSummaryDto>.Unauthorized("A tenant could not be resolved.");

        var today = Today;
        var from = filter.From ?? today.AddDays(-60); // Default to last 60 days
        var to = filter.To ?? today.AddDays(60);      // Default to next 60 days
        if (to < from)
            return Result<HrConfirmationDashboardSummaryDto>.Invalid("to", "The dashboard end date must not be before the start date.");

        // Build employee access predicate for authorization
        IQueryable<Employee>? authorizedEmployees = null;
        if (_accessScope is not null)
        {
            var access = await _accessScope.BuildPredicateAsync(today, cancellationToken);
            authorizedEmployees = _db.Employees.Where(access);
        }

        // Base query for confirmation review cases
        var casesQuery = _db.ConfirmationReviewCases.AsNoTracking()
            .Where(c => c.TenantId == _tenant.TenantId.Value);

        // Apply date filters (using due date as the primary date for consistency with KPIs)
        if (filter.From.HasValue)
            casesQuery = casesQuery.Where(c => c.DueDate >= filter.From.Value);
        if (filter.To.HasValue)
            casesQuery = casesQuery.Where(c => c.DueDate <= filter.To.Value);

        // Apply employee access filtering
        if (authorizedEmployees is not null)
        {
            casesQuery = from caseItem in casesQuery
                         join employee in authorizedEmployees
                             on new { caseItem.TenantId, caseItem.EmployeeId }
                             equals new { employee.TenantId, EmployeeId = employee.Id }
                         select caseItem;
        }

        // Apply department filter
        if (filter.DepartmentId.HasValue)
        {
            casesQuery = casesQuery.Where(c =>
                c.Employee.EmploymentHistory.Any(e =>
                    !e.IsSuperseded &&
                    e.EffectiveFrom <= today &&
                    (e.EffectiveTo == null || e.EffectiveTo >= today) &&
                    e.DepartmentId == filter.DepartmentId.Value));
        }

        // Apply designation filter
        if (filter.DesignationId.HasValue)
        {
            casesQuery = casesQuery.Where(c =>
                c.Employee.EmploymentHistory.Any(e =>
                    !e.IsSuperseded &&
                    e.EffectiveFrom <= today &&
                    (e.EffectiveTo == null || e.EffectiveTo >= today) &&
                    e.DesignationId == filter.DesignationId.Value));
        }

        // Execute the query to get the base dataset
        var cases = await casesQuery
            .Select(c => new ConfirmationCaseRow(
                c.Id,
                c.EmployeeId,
                c.Employee.EmployeeCode,
                c.Employee.FirstName + " " + c.Employee.LastName,
                GetCurrentDepartmentName(c.Employee.EmploymentHistory, today),
                GetCurrentDesignationName(c.Employee.EmploymentHistory, today),
                c.ProbationStartDate,
                c.ProbationEndDate,
                c.DueDate,
                c.Status,
                c.Outcome,
                c.ManagerReviewerEmployeeId,
                c.HrbpReviewerEmployeeId,
                c.ReviewRound,
                c.CycleNumber,
                $"{c.ReviewRound:D2}-{c.CycleNumber:D2}",
                c.CreatedDate,
                c.ModifiedDate))
            .ToListAsync(cancellationToken);

        // Calculate KPIs
        var probationEmployees = cases.Count(c => c.Status != ConfirmationReviewCaseStatus.Decided && c.Status != ConfirmationReviewCaseStatus.Cancelled);
        var upcomingConfirmations = cases.Count(c => c.DueDate >= today && c.Status != ConfirmationReviewCaseStatus.Decided && c.Status != ConfirmationReviewCaseStatus.Cancelled);
        var overdueConfirmations = cases.Count(c => c.DueDate < today && c.Status != ConfirmationReviewCaseStatus.Decided && c.Status != ConfirmationReviewCaseStatus.Cancelled);

        // Pending actions: cases where reviews are not submitted
        var pendingManagerActions = 0;
        var pendingHrbpActions = 0;
        var submittedManagerCaseIds = new List<Guid>();
        var submittedHrbpCaseIds = new List<Guid>();

        // We need to check review submission status for each case
        var caseIds = cases.Select(c => c.Id).ToList();
        if (caseIds.Any())
        {
            var reviews = await _db.ConfirmationReviewerReviews.AsNoTracking()
                .Where(r => caseIds.Contains(r.ConfirmationReviewCaseId) && r.Status == ConfirmationReviewStatus.Submitted)
                .ToListAsync(cancellationToken);

            submittedManagerCaseIds = reviews.Where(r => r.ReviewerType == ConfirmationReviewerType.ReportingManager)
                .Select(r => r.ConfirmationReviewCaseId).Distinct().ToList();
            submittedHrbpCaseIds = reviews.Where(r => r.ReviewerType == ConfirmationReviewerType.Hrbp)
                .Select(r => r.ConfirmationReviewCaseId).Distinct().ToList();

            pendingManagerActions = cases.Count(c => !submittedManagerCaseIds.Contains(c.Id));
            pendingHrbpActions = cases.Count(c => !submittedHrbpCaseIds.Contains(c.Id));
        }

        var confirmedEmployees = cases.Count(c => c.Outcome == ConfirmationOutcome.Confirmed);
        var extendedProbation = cases.Count(c => c.Outcome == ConfirmationOutcome.ProbationExtended);
        var rejectedNotConfirmed = cases.Count(c => c.Outcome == ConfirmationOutcome.NotConfirmed);

        // Status breakdown
        var statusBreakdown = cases.GroupBy(c => c.Status)
            .Select(g => new ConfirmationStatusBreakdownItem
            {
                Status = g.Key,
                Count = g.Count()
            })
            .OrderBy(s => s.Status)
            .ToList();

        // Outcome breakdown
        var outcomeBreakdown = cases.GroupBy(c => c.Outcome)
            .Select(g => new ConfirmationOutcomeBreakdownItem
            {
                Outcome = g.Key,
                Count = g.Count()
            })
            .OrderBy(o => o.Outcome)
            .ToList();

        // Department breakdown
        var departmentBreakdown = cases.Where(c => !string.IsNullOrEmpty(c.DepartmentName))
            .GroupBy(c => c.DepartmentName)
            .Select(g => new DepartmentBreakdownItem
            {
                Name = g.Key,
                EmployeeCount = g.Count()
            })
            .OrderByDescending(d => d.EmployeeCount)
            .ToList();

        // Designation breakdown
        var designationBreakdown = cases.Where(c => !string.IsNullOrEmpty(c.DesignationName))
            .GroupBy(c => c.DesignationName)
            .Select(g => new DesignationBreakdownItem
            {
                Name = g.Key,
                EmployeeCount = g.Count()
            })
            .OrderByDescending(d => d.EmployeeCount)
            .ToList();

        // Trends (monthly data for the last 6 months)
        var trendStartDate = today.AddMonths(-6);
        var trendCases = cases.Where(c => c.CreatedDate >= trendStartDate.ToDateTime(TimeOnly.MinValue)).ToList();

        var trend = trendCases.GroupBy(c =>
                new { Year = c.CreatedDate.Year, Month = c.CreatedDate.Month })
            .Select(g => new ConfirmationTrendItem
            {
                Period = $"{g.Key.Year:D4}-{g.Key.Month:D2}",
                ConfirmedCount = g.Count(c => c.Outcome == ConfirmationOutcome.Confirmed),
                ExtendedCount = g.Count(c => c.Outcome == ConfirmationOutcome.ProbationExtended),
                RejectedCount = g.Count(c => c.Outcome == ConfirmationOutcome.NotConfirmed)
            })
            .OrderBy(t => t.Period)
            .ToList();

        // Pending actions details (for display in dashboard)
        var pendingActions = new List<PendingActionItem>();
        if (caseIds.Any())
        {
            var pendingManagerCaseIds = cases.Where(c => !submittedManagerCaseIds.Contains(c.Id)).Select(c => c.Id).ToList();
            var pendingHrbpCaseIds = cases.Where(c => !submittedHrbpCaseIds.Contains(c.Id)).Select(c => c.Id).ToList();

            // Get pending manager actions
            var pendingManagerCases = cases.Where(c => pendingManagerCaseIds.Contains(c.Id)).ToList();
            foreach (var pendingCase in pendingManagerCases)
            {
                pendingActions.Add(new PendingActionItem
                {
                    EmployeeName = pendingCase.EmployeeName,
                    EmployeeCode = pendingCase.EmployeeCode,
                    ProbationCycleKey = pendingCase.ProbationCycleKey,
                    DueDate = pendingCase.DueDate,
                    ActionType = "Manager",
                    PendingReviewerType = "ReportingManager"
                });
            }

            // Get pending HRBP actions
            var pendingHrbpCases = cases.Where(c => pendingHrbpCaseIds.Contains(c.Id)).ToList();
            foreach (var pendingCase in pendingHrbpCases)
            {
                pendingActions.Add(new PendingActionItem
                {
                    EmployeeName = pendingCase.EmployeeName,
                    EmployeeCode = pendingCase.EmployeeCode,
                    ProbationCycleKey = pendingCase.ProbationCycleKey,
                    DueDate = pendingCase.DueDate,
                    ActionType = "HRBP",
                    PendingReviewerType = "Hrbp"
                });
            }
        }

        return Result<HrConfirmationDashboardSummaryDto>.Success(new HrConfirmationDashboardSummaryDto
        {
            From = from.ToString(),
            To = to.ToString(),
            CurrentConfirmationPeriodName = null, // Could be enhanced later
            Kpis = new ConfirmationDashboardKpis
            {
                ProbationEmployees = probationEmployees,
                UpcomingConfirmations = upcomingConfirmations,
                OverdueConfirmations = overdueConfirmations,
                PendingManagerActions = pendingManagerActions,
                PendingHrbpActions = pendingHrbpActions,
                ConfirmedEmployees = confirmedEmployees,
                ExtendedProbation = extendedProbation,
                RejectedNotConfirmed = rejectedNotConfirmed
            },
            StatusBreakdown = statusBreakdown,
            OutcomeBreakdown = outcomeBreakdown,
            DepartmentBreakdown = departmentBreakdown,
            DesignationBreakdown = designationBreakdown,
            Trend = trend,
            PendingActions = pendingActions
        });
    }

    private static string? GetCurrentDepartmentName(IEnumerable<EmployeeEmploymentHistory> history, DateOnly today)
    {
        return history.OrderByDescending(e => e.EffectiveFrom)
            .FirstOrDefault(e => !e.IsSuperseded && e.EffectiveFrom <= today && (e.EffectiveTo == null || e.EffectiveTo >= today))?.Department?.Name;
    }

    private static string? GetCurrentDesignationName(IEnumerable<EmployeeEmploymentHistory> history, DateOnly today)
    {
        return history.OrderByDescending(e => e.EffectiveFrom)
            .FirstOrDefault(e => !e.IsSuperseded && e.EffectiveFrom <= today && (e.EffectiveTo == null || e.EffectiveTo >= today))?.Designation?.Name;
    }

    // Helper record for querying case data
    private sealed record ConfirmationCaseRow(
        Guid Id,
        Guid EmployeeId,
        string? EmployeeCode,
        string EmployeeName,
        string? DepartmentName,
        string? DesignationName,
        DateOnly ProbationStartDate,
        DateOnly ProbationEndDate,
        DateOnly DueDate,
        ConfirmationReviewCaseStatus Status,
        ConfirmationOutcome Outcome,
        Guid? ManagerReviewerEmployeeId,
        Guid? HrbpReviewerEmployeeId,
        int ReviewRound,
        int CycleNumber,
        string ProbationCycleKey,
        DateTime CreatedDate,
        DateTime? ModifiedDate);
}