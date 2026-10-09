using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Resolves the single applicable confirmation/probation policy for an employee on a date. The algorithm
/// is intentionally identical to <see cref="LeavePolicyResolver"/>: published, effective-dated versions
/// whose applicability matches the employee's effective employment snapshot compete, and the winner is the
/// one with the highest priority, then the greatest applicability specificity. A version with zero
/// applicability sets is the explicit tenant-wide default (specificity 0). When no policy applies the caller
/// falls back to the employee's existing manual confirmation data, so employees without a configured policy
/// keep working unchanged.
/// </summary>
public sealed class ConfirmationPolicyResolver : IConfirmationPolicyResolver
{
    private readonly IHrmsDbContext _db;
    private readonly IEffectiveEmploymentResolver _employmentResolver;
    private readonly ITenantContext? _tenantContext;

    public ConfirmationPolicyResolver(
        IHrmsDbContext db,
        IEffectiveEmploymentResolver? employmentResolver = null,
        ITenantContext? tenantContext = null)
    {
        _db = db;
        _employmentResolver = employmentResolver ?? new EffectiveEmploymentResolver(db, tenantContext);
        _tenantContext = tenantContext;
    }

    public async Task<ConfirmationPolicyResolutionResult> ResolveAsync(Guid tenantId, Guid employeeId, DateOnly effectiveDate, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || (_tenantContext?.TenantId is Guid current && current != tenantId))
            return Failure(ConfirmationPolicyResolutionStatus.InvalidTenant, tenantId, employeeId, effectiveDate, "The requested tenant is not the authenticated tenant.");

        var employee = await _db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == employeeId, ct);
        if (employee is null)
            return Failure(ConfirmationPolicyResolutionStatus.InvalidTenant, tenantId, employeeId, effectiveDate, "Employee was not found in the tenant.");

        var employment = await _employmentResolver.ResolveAsync(tenantId, employeeId, effectiveDate, ct);
        if (employment.Status != EffectiveEmploymentResolutionStatus.Resolved || employment.Employment is null)
        {
            var status = employment.Status switch
            {
                EffectiveEmploymentResolutionStatus.NotFound => ConfirmationPolicyResolutionStatus.EffectiveEmploymentNotFound,
                EffectiveEmploymentResolutionStatus.ConfigurationAmbiguity => ConfirmationPolicyResolutionStatus.EffectiveEmploymentAmbiguous,
                _ => ConfirmationPolicyResolutionStatus.InvalidTenant
            };
            return Failure(status, tenantId, employeeId, effectiveDate, employment.Message);
        }
        var h = employment.Employment;

        var versions = await _db.ConfirmationPolicyVersions.AsNoTracking()
            .Include(x => x.ConfirmationPolicy)
            .Where(x => x.TenantId == tenantId && x.Status == ConfirmationPolicyVersionStatus.Published
                && x.EffectiveFrom <= effectiveDate && (x.EffectiveTo == null || effectiveDate <= x.EffectiveTo)
                && x.ConfirmationPolicy != null && x.ConfirmationPolicy.IsActive)
            .ToListAsync(ct);
        var versionIds = versions.Select(x => x.Id).ToList();
        var rules = await _db.ConfirmationPolicyRules.AsNoTracking().Where(x => x.TenantId == tenantId && versionIds.Contains(x.ConfirmationPolicyVersionId)).ToListAsync(ct);
        var sets = await _db.ConfirmationPolicyApplicabilitySets.AsNoTracking().Where(x => x.TenantId == tenantId && versionIds.Contains(x.ConfirmationPolicyVersionId)).ToListAsync(ct);

        var candidates = new List<Candidate>();
        foreach (var version in versions)
        {
            var rule = rules.SingleOrDefault(x => x.ConfirmationPolicyVersionId == version.Id);
            if (rule is null) continue; // a published version with no probation rule cannot be applied
            var matching = sets.Where(x => x.ConfirmationPolicyVersionId == version.Id && Matches(x, h)).ToList();
            var specificity = sets.Any(x => x.ConfirmationPolicyVersionId == version.Id) ? matching.Select(Specificity).DefaultIfEmpty(-1).Max() : 0;
            if (specificity >= 0) candidates.Add(new(version, rule, specificity));
        }

        if (candidates.Count == 0)
            return Failure(ConfirmationPolicyResolutionStatus.NoPolicy, tenantId, employeeId, effectiveDate, "No published confirmation policy applies to this employee.");

        var samePolicyVersions = candidates.GroupBy(x => x.Version.ConfirmationPolicyId).Where(g => g.Select(c => c.Version.Id).Distinct().Count() > 1).ToList();
        if (samePolicyVersions.Count != 0)
            return Failure(ConfirmationPolicyResolutionStatus.ConfigurationAmbiguity, tenantId, employeeId, effectiveDate, "Multiple published versions of the same confirmation policy apply to this date. Repair the version effective ranges.");

        var bestPriority = candidates.Max(x => x.Version.Priority);
        var priorityCandidates = candidates.Where(x => x.Version.Priority == bestPriority).ToList();
        var bestSpecificity = priorityCandidates.Max(x => x.Specificity);
        var winners = priorityCandidates.Where(x => x.Specificity == bestSpecificity).ToList();
        if (winners.Count != 1)
            return Failure(ConfirmationPolicyResolutionStatus.ConfigurationAmbiguity, tenantId, employeeId, effectiveDate, "Multiple published confirmation policies have the same best priority and specificity.");

        var winner = winners[0];
        var r = winner.Rule;
        var probationEnd = ConfirmationDateCalculator.ProbationEndDate(h.DateOfJoining, r.ProbationDuration, r.ProbationDurationUnit);

        // Phase 9c-3: a persisted *active* probation cycle (created by a probation extension) overrides the
        // DOJ+duration baseline. This is the single point that makes the probation-end date reflect extensions,
        // so both the review-case cycle key and the Phase 9b reminder dedup (keyed on probation-end) move to the
        // new cycle without any change to 9b's logic. Closed/baseline cycles are ignored.
        var activeCycle = await _db.ConfirmationProbationCycles.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Status == ConfirmationProbationCycleStatus.Active)
            .OrderByDescending(x => x.CycleNumber)
            .FirstOrDefaultAsync(ct);
        if (activeCycle is not null) probationEnd = activeCycle.CurrentEndDate;

        return new(ConfirmationPolicyResolutionStatus.Resolved, tenantId, employeeId, effectiveDate,
            winner.Version.ConfirmationPolicyId, winner.Version.Id, winner.Version.Priority, winner.Specificity,
            r.ProbationDuration, r.ProbationDurationUnit, r.TriggerDaysBeforeEnd, r.MaxExtensions, r.ExtensionDuration, r.ExtensionDurationUnit,
            h.DateOfJoining, probationEnd, "A unique published confirmation policy was resolved.");
    }

    private static bool Matches(ConfirmationPolicyApplicabilitySet s, EffectiveEmploymentSnapshot h) =>
        (!s.Gender.HasValue || s.Gender == h.Gender) &&
        (!s.HoldingCompanyId.HasValue || s.HoldingCompanyId == h.HoldingCompanyId) && (!s.LobId.HasValue || s.LobId == h.LobId) &&
        (!s.OrganisationId.HasValue || s.OrganisationId == h.OrganisationId) && (!s.DepartmentId.HasValue || s.DepartmentId == h.DepartmentId) &&
        (!s.SubDepartmentId.HasValue || s.SubDepartmentId == h.SubDepartmentId) && (!s.SectionId.HasValue || s.SectionId == h.SectionId) &&
        (!s.SubSectionId.HasValue || s.SubSectionId == h.SubSectionId) && (!s.FunctionId.HasValue || s.FunctionId == h.FunctionId) &&
        (!s.SubFunctionId.HasValue || s.SubFunctionId == h.SubFunctionId) && (!s.GradeId.HasValue || s.GradeId == h.GradeId) &&
        (!s.DesignationId.HasValue || s.DesignationId == h.DesignationId) && (!s.EmployeeTypeId.HasValue || s.EmployeeTypeId == h.EmployeeTypeId) &&
        (!s.CountryLocationId.HasValue || s.CountryLocationId == h.CountryLocationId) && (!s.WorkLocationId.HasValue || s.WorkLocationId == h.WorkLocationId) &&
        (!s.CostCenterId.HasValue || s.CostCenterId == h.CostCenterId);

    private static int Specificity(ConfirmationPolicyApplicabilitySet s) =>
        (s.Gender.HasValue ? 1 : 0) + new Guid?[] { s.HoldingCompanyId, s.LobId, s.OrganisationId, s.DepartmentId, s.SubDepartmentId, s.SectionId, s.SubSectionId, s.FunctionId, s.SubFunctionId, s.GradeId, s.DesignationId, s.EmployeeTypeId, s.CountryLocationId, s.WorkLocationId, s.CostCenterId }.Count(x => x.HasValue);

    private static ConfirmationPolicyResolutionResult Failure(ConfirmationPolicyResolutionStatus status, Guid tenantId, Guid employeeId, DateOnly date, string message) =>
        new(status, tenantId, employeeId, date, null, null, null, null, null, null, null, null, null, null, null, null, message);

    private sealed record Candidate(ConfirmationPolicyVersion Version, ConfirmationPolicyRule Rule, int Specificity);
}
