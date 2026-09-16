using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public enum LeavePolicyResolutionStatus
{
    Resolved,
    NoPolicy,
    NotConfigured = NoPolicy,
    ConfigurationAmbiguity,
    InvalidTenant,
    NoApplicableEmployment,
    EffectiveEmploymentNotFound,
    EffectiveEmploymentAmbiguous,
    LeaveTypeNotConfigured
}

public sealed record LeavePolicyResolutionResult(
    LeavePolicyResolutionStatus Status,
    Guid TenantId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateOnly EffectiveDate,
    Guid? LeavePolicyId,
    Guid? LeavePolicyVersionId,
    Guid? LeavePolicyRuleId,
    int? Priority,
    int? Specificity,
    string Message,
    IReadOnlyList<LeavePolicyResolutionCandidate>? Candidates = null);

public sealed record LeavePolicyResolutionCandidate(
    Guid PolicyId, Guid PolicyVersionId, Guid RuleId, string PolicyCode, string PolicyName,
    int VersionNumber, LeavePolicyVersionStatus Status, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    int Priority, int Specificity, bool IsWinner);

public sealed class LeavePolicyResolver : ILeavePolicyResolver
{
    private readonly IHrmsDbContext _db;
    private readonly IEffectiveEmploymentResolver _employmentResolver;
    private readonly ITenantContext? _tenantContext;

    public LeavePolicyResolver(
        IHrmsDbContext db,
        IEffectiveEmploymentResolver? employmentResolver = null,
        ITenantContext? tenantContext = null)
    {
        _db = db;
        _employmentResolver = employmentResolver ?? new EffectiveEmploymentResolver(db, tenantContext);
        _tenantContext = tenantContext;
    }

    public async Task<LeavePolicyResolutionResult> ResolveAsync(Guid tenantId, Guid employeeId, Guid leaveTypeId, DateOnly effectiveDate, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || (_tenantContext?.TenantId is Guid currentTenantId && currentTenantId != tenantId))
            return Failure(LeavePolicyResolutionStatus.InvalidTenant, tenantId, employeeId, leaveTypeId, effectiveDate, "The requested tenant is not the authenticated tenant.");

        var employee = await _db.Employees.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == employeeId, ct);
        if (employee is null) return Failure(LeavePolicyResolutionStatus.InvalidTenant, tenantId, employeeId, leaveTypeId, effectiveDate, "Employee was not found in the tenant.");
        var leaveType = await _db.LeaveTypes.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == leaveTypeId, ct);
        if (leaveType is null)
            return Failure(LeavePolicyResolutionStatus.InvalidTenant, tenantId, employeeId, leaveTypeId, effectiveDate, "LeaveType was not found in the tenant.");
        if (!leaveType.IsActive)
            return Failure(LeavePolicyResolutionStatus.LeaveTypeNotConfigured, tenantId, employeeId, leaveTypeId, effectiveDate, "The requested LeaveType is inactive.");

        var employment = await _employmentResolver.ResolveAsync(tenantId, employeeId, effectiveDate, ct);
        if (employment.Status != EffectiveEmploymentResolutionStatus.Resolved || employment.Employment is null)
        {
            var status = employment.Status switch
            {
                EffectiveEmploymentResolutionStatus.NotFound => LeavePolicyResolutionStatus.EffectiveEmploymentNotFound,
                EffectiveEmploymentResolutionStatus.ConfigurationAmbiguity => LeavePolicyResolutionStatus.EffectiveEmploymentAmbiguous,
                _ => LeavePolicyResolutionStatus.InvalidTenant
            };
            return Failure(status, tenantId, employeeId, leaveTypeId, effectiveDate, employment.Message);
        }
        var h = employment.Employment;

        var versions = await _db.LeavePolicyVersions.AsNoTracking().Include(x => x.LeavePolicy).Where(x => x.TenantId == tenantId && x.Status == LeavePolicyVersionStatus.Published && x.EffectiveFrom <= effectiveDate && (x.EffectiveTo == null || effectiveDate <= x.EffectiveTo) && x.LeavePolicy != null && x.LeavePolicy.IsActive).ToListAsync(ct);
        var versionIds = versions.Select(x => x.Id).ToList();
        var rules = await _db.LeavePolicyRules.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && versionIds.Contains(x.LeavePolicyVersionId) && x.LeaveTypeId == leaveTypeId).ToListAsync(ct);
        var sets = await _db.LeavePolicyApplicabilitySets.AsNoTracking().Where(x => x.TenantId == tenantId && versionIds.Contains(x.LeavePolicyVersionId)).ToListAsync(ct);
        var candidates = new List<Candidate>();
        foreach (var rule in rules)
        {
            var version = versions.Single(x => x.Id == rule.LeavePolicyVersionId);
            var matching = sets.Where(x => x.LeavePolicyVersionId == version.Id && Matches(x, h)).ToList();
            // Zero sets is the explicit tenant-wide default; otherwise the matching set with the greatest
            // number of populated dimensions determines this version's specificity.
            var specificity = sets.Any(x => x.LeavePolicyVersionId == version.Id) ? matching.Select(Specificity).DefaultIfEmpty(-1).Max() : 0;
            if (specificity >= 0) candidates.Add(new(version, rule, specificity));
        }
        if (candidates.Count == 0) return Failure(LeavePolicyResolutionStatus.NoPolicy, tenantId, employeeId, leaveTypeId, effectiveDate, "No published policy applies to this LeaveType and effective employment.");
        var samePolicyVersions = candidates.GroupBy(x => x.Version.LeavePolicyId).Where(x => x.Select(candidate => candidate.Version.Id).Distinct().Count() > 1).ToList();
        if (samePolicyVersions.Count != 0)
        {
            var samePolicyDiagnosticCandidates = candidatesForDiagnostic(candidates, null);
            return new(LeavePolicyResolutionStatus.ConfigurationAmbiguity, tenantId, employeeId, leaveTypeId, effectiveDate, null, null, null, null, null,
                "Multiple Published versions of the same LeavePolicy apply to this date. Repair the policy version effective ranges before resolving leave.", samePolicyDiagnosticCandidates);
        }
        var bestPriority = candidates.Max(x => x.Version.Priority);
        var priorityCandidates = candidates.Where(x => x.Version.Priority == bestPriority).ToList();
        var bestSpecificity = priorityCandidates.Max(x => x.Specificity);
        var winners = priorityCandidates.Where(x => x.Specificity == bestSpecificity).ToList();
        var diagnosticCandidates = candidatesForDiagnostic(candidates, winners.Count == 1 ? winners[0] : null);
        if (winners.Count != 1) return new(LeavePolicyResolutionStatus.ConfigurationAmbiguity, tenantId, employeeId, leaveTypeId, effectiveDate, null, null, null, null, null, "Multiple published policies have the same best priority and specificity.", diagnosticCandidates);
        var winner = winners[0];
        return new(LeavePolicyResolutionStatus.Resolved, tenantId, employeeId, leaveTypeId, effectiveDate, winner.Version.LeavePolicyId, winner.Version.Id, winner.Rule.Id, winner.Version.Priority, winner.Specificity, "A unique published policy rule was resolved.", diagnosticCandidates);
    }

    private static bool Matches(LeavePolicyApplicabilitySet s, EffectiveEmploymentSnapshot h) =>
        (!s.Gender.HasValue || s.Gender == h.Gender) &&
        (!s.HoldingCompanyId.HasValue || s.HoldingCompanyId == h.HoldingCompanyId) && (!s.LobId.HasValue || s.LobId == h.LobId) &&
        (!s.OrganisationId.HasValue || s.OrganisationId == h.OrganisationId) && (!s.DepartmentId.HasValue || s.DepartmentId == h.DepartmentId) &&
        (!s.SubDepartmentId.HasValue || s.SubDepartmentId == h.SubDepartmentId) && (!s.SectionId.HasValue || s.SectionId == h.SectionId) &&
        (!s.SubSectionId.HasValue || s.SubSectionId == h.SubSectionId) && (!s.FunctionId.HasValue || s.FunctionId == h.FunctionId) &&
        (!s.SubFunctionId.HasValue || s.SubFunctionId == h.SubFunctionId) && (!s.GradeId.HasValue || s.GradeId == h.GradeId) &&
        (!s.DesignationId.HasValue || s.DesignationId == h.DesignationId) && (!s.EmployeeTypeId.HasValue || s.EmployeeTypeId == h.EmployeeTypeId) &&
        (!s.CountryLocationId.HasValue || s.CountryLocationId == h.CountryLocationId) && (!s.WorkLocationId.HasValue || s.WorkLocationId == h.WorkLocationId) &&
        (!s.CostCenterId.HasValue || s.CostCenterId == h.CostCenterId);

    private static int Specificity(LeavePolicyApplicabilitySet s) =>
        (s.Gender.HasValue ? 1 : 0) + new Guid?[] { s.HoldingCompanyId, s.LobId, s.OrganisationId, s.DepartmentId, s.SubDepartmentId, s.SectionId, s.SubSectionId, s.FunctionId, s.SubFunctionId, s.GradeId, s.DesignationId, s.EmployeeTypeId, s.CountryLocationId, s.WorkLocationId, s.CostCenterId }.Count(x => x.HasValue);

    private static IReadOnlyList<LeavePolicyResolutionCandidate> candidatesForDiagnostic(IEnumerable<Candidate> source, Candidate? winner) => source.Select(x => new LeavePolicyResolutionCandidate(x.Version.LeavePolicyId, x.Version.Id, x.Rule.Id, x.Version.LeavePolicy!.Code, x.Version.LeavePolicy.Name, x.Version.VersionNumber, x.Version.Status, x.Version.EffectiveFrom, x.Version.EffectiveTo, x.Version.Priority, x.Specificity, winner?.Version.Id == x.Version.Id && winner.Rule.Id == x.Rule.Id)).ToList();

    private static LeavePolicyResolutionResult Failure(LeavePolicyResolutionStatus status, Guid tenantId, Guid employeeId, Guid leaveTypeId, DateOnly date, string message) => new(status, tenantId, employeeId, leaveTypeId, date, null, null, null, null, null, message);
    private sealed record Candidate(LeavePolicyVersion Version, LeavePolicyRule Rule, int Specificity);
}
