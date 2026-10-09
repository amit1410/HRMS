using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Confirmation/probation policy configuration. Every read and write goes through the tenant-filtered
/// DbContext sets, so cross-tenant rows are simply absent; no method trusts a caller-supplied tenant id.
/// Draft versions are editable; once published a version is immutable (a new draft is created to change it).
/// </summary>
public sealed class ConfirmationPolicyService : IConfirmationPolicyService
{
    private const string NoTenant = "No authenticated tenant.";
    private const string PolicyNotFound = "Confirmation policy not found.";
    private const string VersionNotFound = "Confirmation policy version not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IConfirmationPolicyResolver _resolver;
    private readonly TimeProvider _clock;

    public ConfirmationPolicyService(
        IHrmsDbContext db,
        ITenantContext tenant,
        IConfirmationPolicyResolver? resolver = null,
        TimeProvider? clock = null)
    {
        _db = db;
        _tenant = tenant;
        _resolver = resolver ?? new ConfirmationPolicyResolver(db, tenantContext: tenant);
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<Result<PagedResult<ConfirmationPolicyDto>>> GetPoliciesAsync(ConfirmationPolicyQuery query, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<PagedResult<ConfirmationPolicyDto>>.Unauthorized(NoTenant);

        var source = _db.ConfirmationPolicies.AsNoTracking();
        if (query.IsActive.HasValue) source = source.Where(x => x.IsActive == query.IsActive.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLowerInvariant();
            source = source.Where(x => x.Code.ToLower().Contains(s) || x.Name.ToLower().Contains(s));
        }

        var ordered = ApplySort(source, query);
        var page = await ordered.Select(x => new ConfirmationPolicyDto(
                x.Id, x.Code, x.Name, x.Description, x.IsActive,
                x.Versions.Count,
                x.Versions.Count(v => v.Status == ConfirmationPolicyVersionStatus.Published),
                x.CreatedDate, x.ModifiedDate))
            .ToPagedResultAsync(query, ct);
        return Result<PagedResult<ConfirmationPolicyDto>>.Success(page);
    }

    public async Task<Result<ConfirmationPolicyDto>> GetPolicyAsync(Guid policyId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<ConfirmationPolicyDto>.Unauthorized(NoTenant);
        var dto = await ProjectPolicy(_db.ConfirmationPolicies.AsNoTracking().Where(x => x.Id == policyId)).FirstOrDefaultAsync(ct);
        return dto is null ? Result<ConfirmationPolicyDto>.NotFound(PolicyNotFound) : Result<ConfirmationPolicyDto>.Success(dto);
    }

    public async Task<Result<ConfirmationPolicyDto>> CreatePolicyAsync(ConfirmationPolicyRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<ConfirmationPolicyDto>.Unauthorized(NoTenant);
        var code = request.Code.Trim();
        if (await _db.ConfirmationPolicies.AnyAsync(x => x.Code.ToLower() == code.ToLower(), ct))
            return Result<ConfirmationPolicyDto>.Conflict($"A confirmation policy with code '{code}' already exists.", [new ValidationError("code", "This code is already in use.")]);

        var policy = new ConfirmationPolicy { Id = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = request.Name.Trim(), Description = Normalize(request.Description), IsActive = request.IsActive };
        _db.ConfirmationPolicies.Add(policy);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Result<ConfirmationPolicyDto>.Conflict($"A confirmation policy with code '{code}' already exists.", [new ValidationError("code", "This code is already in use.")]); }
        return Result<ConfirmationPolicyDto>.Success(new ConfirmationPolicyDto(policy.Id, policy.Code, policy.Name, policy.Description, policy.IsActive, 0, 0, policy.CreatedDate, policy.ModifiedDate), "Confirmation policy created.");
    }

    public async Task<Result<ConfirmationPolicyDto>> UpdatePolicyAsync(Guid policyId, ConfirmationPolicyRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<ConfirmationPolicyDto>.Unauthorized(NoTenant);
        var policy = await _db.ConfirmationPolicies.FirstOrDefaultAsync(x => x.Id == policyId, ct);
        if (policy is null) return Result<ConfirmationPolicyDto>.NotFound(PolicyNotFound);
        var code = request.Code.Trim();
        if (await _db.ConfirmationPolicies.AnyAsync(x => x.Id != policyId && x.Code.ToLower() == code.ToLower(), ct))
            return Result<ConfirmationPolicyDto>.Conflict($"A confirmation policy with code '{code}' already exists.", [new ValidationError("code", "This code is already in use.")]);

        policy.Code = code; policy.Name = request.Name.Trim(); policy.Description = Normalize(request.Description); policy.IsActive = request.IsActive;
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Result<ConfirmationPolicyDto>.Conflict($"A confirmation policy with code '{code}' already exists.", [new ValidationError("code", "This code is already in use.")]); }
        return await GetPolicyAsync(policyId, ct);
    }

    public async Task<Result<PagedResult<ConfirmationPolicyVersionDto>>> GetVersionsAsync(Guid policyId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<PagedResult<ConfirmationPolicyVersionDto>>.Unauthorized(NoTenant);
        if (!await _db.ConfirmationPolicies.AnyAsync(x => x.Id == policyId, ct)) return Result<PagedResult<ConfirmationPolicyVersionDto>>.NotFound(PolicyNotFound);
        var items = await _db.ConfirmationPolicyVersions.AsNoTracking()
            .Where(x => x.ConfirmationPolicyId == policyId)
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => new ConfirmationPolicyVersionDto(x.Id, x.VersionNumber, x.EffectiveFrom, x.EffectiveTo, x.Status, x.Priority, x.ApplicabilitySets.Count, x.CreatedDate, x.CreatedBy, x.ModifiedDate))
            .ToListAsync(ct);
        return Result<PagedResult<ConfirmationPolicyVersionDto>>.Success(new PagedResult<ConfirmationPolicyVersionDto>(items, 1, Math.Max(1, items.Count), items.Count));
    }

    public async Task<Result<ConfirmationPolicyEditorDto>> GetEditorAsync(Guid policyId, Guid? versionId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<ConfirmationPolicyEditorDto>.Unauthorized(NoTenant);
        var policy = await _db.ConfirmationPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == policyId, ct);
        if (policy is null) return Result<ConfirmationPolicyEditorDto>.NotFound(PolicyNotFound);

        var versionsQuery = _db.ConfirmationPolicyVersions.AsNoTracking().Where(x => x.ConfirmationPolicyId == policyId);
        var version = versionId is Guid vid
            ? await versionsQuery.FirstOrDefaultAsync(x => x.Id == vid, ct)
            : await versionsQuery.OrderByDescending(x => x.VersionNumber).FirstOrDefaultAsync(ct);
        if (version is null) return Result<ConfirmationPolicyEditorDto>.NotFound(VersionNotFound);

        return Result<ConfirmationPolicyEditorDto>.Success(await BuildEditorAsync(policy, version, ct));
    }

    public async Task<Result<ConfirmationPolicyEditorDto>> CreateVersionAsync(Guid policyId, ConfirmationPolicyVersionRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<ConfirmationPolicyEditorDto>.Unauthorized(NoTenant);
        var policy = await _db.ConfirmationPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == policyId, ct);
        if (policy is null) return Result<ConfirmationPolicyEditorDto>.NotFound(PolicyNotFound);

        var validation = ValidateVersion(request);
        if (validation is not null) return validation;

        var nextNumber = (await _db.ConfirmationPolicyVersions.Where(x => x.ConfirmationPolicyId == policyId).MaxAsync(x => (int?)x.VersionNumber, ct) ?? 0) + 1;
        var version = new ConfirmationPolicyVersion
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationPolicyId = policyId, VersionNumber = nextNumber,
            EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, Priority = request.Priority, Status = ConfirmationPolicyVersionStatus.Draft
        };
        _db.ConfirmationPolicyVersions.Add(version);
        _db.ConfirmationPolicyRules.Add(BuildRule(tenantId, version.Id, request.Probation));
        foreach (var set in BuildApplicability(tenantId, version.Id, request.Applicability)) _db.ConfirmationPolicyApplicabilitySets.Add(set);
        foreach (var dr in BuildDecisionRules(tenantId, version.Id, request.DecisionRules)) _db.ConfirmationDecisionRules.Add(dr);
        await _db.SaveChangesAsync(ct);

        return Result<ConfirmationPolicyEditorDto>.Success(await BuildEditorAsync(policy, version, ct), "Draft version created.");
    }

    public async Task<Result<ConfirmationPolicyEditorDto>> UpdateVersionAsync(Guid policyId, Guid versionId, ConfirmationPolicyVersionRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<ConfirmationPolicyEditorDto>.Unauthorized(NoTenant);
        var version = await _db.ConfirmationPolicyVersions.FirstOrDefaultAsync(x => x.Id == versionId && x.ConfirmationPolicyId == policyId, ct);
        if (version is null) return Result<ConfirmationPolicyEditorDto>.NotFound(VersionNotFound);
        if (version.Status != ConfirmationPolicyVersionStatus.Draft)
            return Result<ConfirmationPolicyEditorDto>.Conflict("Only draft versions can be edited. Create a new version to change a published policy.");

        var validation = ValidateVersion(request);
        if (validation is not null) return validation;

        version.EffectiveFrom = request.EffectiveFrom; version.EffectiveTo = request.EffectiveTo; version.Priority = request.Priority;

        var rule = await _db.ConfirmationPolicyRules.FirstOrDefaultAsync(x => x.ConfirmationPolicyVersionId == versionId, ct);
        if (rule is null) _db.ConfirmationPolicyRules.Add(BuildRule(tenantId, versionId, request.Probation));
        else ApplyRule(rule, request.Probation);

        var existingSets = await _db.ConfirmationPolicyApplicabilitySets.Where(x => x.ConfirmationPolicyVersionId == versionId).ToListAsync(ct);
        _db.ConfirmationPolicyApplicabilitySets.RemoveRange(existingSets);
        foreach (var set in BuildApplicability(tenantId, versionId, request.Applicability)) _db.ConfirmationPolicyApplicabilitySets.Add(set);

        var existingRules = await _db.ConfirmationDecisionRules.Where(x => x.ConfirmationPolicyVersionId == versionId).ToListAsync(ct);
        _db.ConfirmationDecisionRules.RemoveRange(existingRules);
        foreach (var dr in BuildDecisionRules(tenantId, versionId, request.DecisionRules)) _db.ConfirmationDecisionRules.Add(dr);
        await _db.SaveChangesAsync(ct);

        var policy = await _db.ConfirmationPolicies.AsNoTracking().FirstAsync(x => x.Id == policyId, ct);
        return Result<ConfirmationPolicyEditorDto>.Success(await BuildEditorAsync(policy, version, ct), "Draft version updated.");
    }

    public async Task<Result<ConfirmationPolicyVersionDto>> PublishVersionAsync(Guid policyId, Guid versionId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<ConfirmationPolicyVersionDto>.Unauthorized(NoTenant);
        var version = await _db.ConfirmationPolicyVersions.FirstOrDefaultAsync(x => x.Id == versionId && x.ConfirmationPolicyId == policyId, ct);
        if (version is null) return Result<ConfirmationPolicyVersionDto>.NotFound(VersionNotFound);
        if (version.Status == ConfirmationPolicyVersionStatus.Published) return Result<ConfirmationPolicyVersionDto>.Conflict("This version is already published.");
        if (!await _db.ConfirmationPolicyRules.AnyAsync(x => x.ConfirmationPolicyVersionId == versionId, ct))
            return Result<ConfirmationPolicyVersionDto>.Invalid("probation", "A probation configuration is required before publishing.");

        version.Status = ConfirmationPolicyVersionStatus.Published;
        await _db.SaveChangesAsync(ct);
        var setCount = await _db.ConfirmationPolicyApplicabilitySets.CountAsync(x => x.ConfirmationPolicyVersionId == versionId, ct);
        return Result<ConfirmationPolicyVersionDto>.Success(ToVersionDto(version, setCount), "Version published.");
    }

    public async Task<Result<ConfirmationPolicyResolveDto>> ResolveAsync(ConfirmationPolicyResolveRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<ConfirmationPolicyResolveDto>.Unauthorized(NoTenant);
        var effectiveDate = request.EffectiveDate ?? DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var r = await _resolver.ResolveAsync(tenantId, request.EmployeeId, effectiveDate, ct);
        var dto = new ConfirmationPolicyResolveDto(r.Status, r.EmployeeId, r.EffectiveDate, r.ConfirmationPolicyId, r.ConfirmationPolicyVersionId,
            r.Priority, r.Specificity, r.ProbationDuration, r.ProbationDurationUnit, r.TriggerDaysBeforeEnd, r.DateOfJoining, r.ProbationEndDate, r.Message);
        return r.Status == ConfirmationPolicyResolutionStatus.InvalidTenant
            ? Result<ConfirmationPolicyResolveDto>.NotFound(r.Message)
            : Result<ConfirmationPolicyResolveDto>.Success(dto);
    }

    public async Task<Result<EmployeeConfirmationStatusDto>> GetEmployeeStatusAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<EmployeeConfirmationStatusDto>.Unauthorized(NoTenant);
        var employee = await _db.Employees.AsNoTracking().Include(x => x.Employment).FirstOrDefaultAsync(x => x.Id == employeeId, ct);
        if (employee is null) return Result<EmployeeConfirmationStatusDto>.NotFound("Employee not found.");

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var r = await _resolver.ResolveAsync(tenantId, employeeId, today, ct);
        if (r.Status == ConfirmationPolicyResolutionStatus.Resolved && r.ProbationEndDate is DateOnly end && r.DateOfJoining is DateOnly doj)
        {
            var policyName = await _db.ConfirmationPolicies.AsNoTracking().Where(x => x.Id == r.ConfirmationPolicyId).Select(x => x.Name).FirstOrDefaultAsync(ct);
            var trigger = ConfirmationDateCalculator.ReviewTriggerDate(doj, end, r.TriggerDaysBeforeEnd ?? 0);
            return Result<EmployeeConfirmationStatusDto>.Success(new EmployeeConfirmationStatusDto(
                employeeId, doj, true, r.ConfirmationPolicyId, policyName, end, trigger,
                employee.Employment?.ConfirmationDate, employee.Employment?.JobStatus,
                "Probation end and confirmation are managed by the applicable policy."));
        }

        // No applicable policy: fall back to the employee's existing manual values, unchanged.
        return Result<EmployeeConfirmationStatusDto>.Success(new EmployeeConfirmationStatusDto(
            employeeId, employee.DateOfJoining, false, null, null, null, null,
            employee.Employment?.ConfirmationDate, employee.Employment?.JobStatus,
            "No confirmation policy applies; existing manual confirmation data is used."));
    }

    // --- helpers ---

    private async Task<ConfirmationPolicyEditorDto> BuildEditorAsync(ConfirmationPolicy policy, ConfirmationPolicyVersion version, CancellationToken ct)
    {
        var rule = await _db.ConfirmationPolicyRules.AsNoTracking().FirstOrDefaultAsync(x => x.ConfirmationPolicyVersionId == version.Id, ct);
        var sets = await _db.ConfirmationPolicyApplicabilitySets.AsNoTracking().Where(x => x.ConfirmationPolicyVersionId == version.Id).ToListAsync(ct);
        var decisionRules = await _db.ConfirmationDecisionRules.AsNoTracking().Where(x => x.ConfirmationPolicyVersionId == version.Id)
            .OrderBy(x => x.Priority).ThenBy(x => x.Id).ToListAsync(ct);
        var policyDto = new ConfirmationPolicyDto(policy.Id, policy.Code, policy.Name, policy.Description, policy.IsActive,
            await _db.ConfirmationPolicyVersions.CountAsync(x => x.ConfirmationPolicyId == policy.Id, ct),
            await _db.ConfirmationPolicyVersions.CountAsync(x => x.ConfirmationPolicyId == policy.Id && x.Status == ConfirmationPolicyVersionStatus.Published, ct),
            policy.CreatedDate, policy.ModifiedDate);
        var probationDto = rule is null
            ? new ConfirmationProbationConfigDto(0, ProbationDurationUnit.Months, 30, 0, 0, ProbationDurationUnit.Months, ConfirmationApprovalRequirement.ManagerAndHrbp, null, true, OverallRatingMethod.AverageOfRatings, ApprovalSequence.Parallel, false, null, null)
            : new ConfirmationProbationConfigDto(rule.ProbationDuration, rule.ProbationDurationUnit, rule.TriggerDaysBeforeEnd, rule.MaxExtensions, rule.ExtensionDuration, rule.ExtensionDurationUnit, rule.RequiredApproval, rule.ConfirmRatingThreshold, rule.AutoConfirmOnApproval, rule.OverallRatingMethod, rule.ApprovalSequence, rule.RequireReviewerComments, rule.ConfirmedJobStatus, rule.NotConfirmedJobStatus);
        var applicability = sets.Select(s => new ConfirmationPolicyApplicabilityDto(s.Id, s.Gender, s.HoldingCompanyId, s.LobId, s.OrganisationId, s.DepartmentId, s.SubDepartmentId, s.SectionId, s.SubSectionId, s.FunctionId, s.SubFunctionId, s.GradeId, s.DesignationId, s.EmployeeTypeId, s.CountryLocationId, s.WorkLocationId, s.CostCenterId)).ToList();
        var decisionRuleDtos = decisionRules.Select(d => new ConfirmationDecisionRuleDto(d.Id, d.Priority, d.Name, d.Description, d.MinOverallRating, d.MaxOverallRating, d.Outcome, d.IsActive)).ToList();
        return new ConfirmationPolicyEditorDto(policyDto, ToVersionDto(version, sets.Count), probationDto, applicability, decisionRuleDtos);
    }

    private static Result<ConfirmationPolicyEditorDto>? ValidateVersion(ConfirmationPolicyVersionRequest request)
    {
        var errors = new List<ValidationError>();
        if (request.EffectiveTo is DateOnly to && to < request.EffectiveFrom) errors.Add(new("effectiveTo", "Effective-to cannot be before effective-from."));
        if (request.Probation.ProbationDuration <= 0) errors.Add(new("probation.probationDuration", "Probation duration must be greater than zero."));
        if (request.Probation.TriggerDaysBeforeEnd < 0) errors.Add(new("probation.triggerDaysBeforeEnd", "Trigger days cannot be negative."));
        if (request.Probation.MaxExtensions < 0) errors.Add(new("probation.maxExtensions", "Max extensions cannot be negative."));
        if (request.Probation.MaxExtensions > 0 && request.Probation.ExtensionDuration <= 0) errors.Add(new("probation.extensionDuration", "Extension duration must be greater than zero when extensions are allowed."));

        var rules = request.DecisionRules ?? Array.Empty<ConfirmationDecisionRuleRequest>();
        for (var i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (string.IsNullOrWhiteSpace(r.Name)) errors.Add(new($"decisionRules[{i}].name", "A decision rule name is required."));
            if (r.Outcome == ConfirmationOutcome.Pending) errors.Add(new($"decisionRules[{i}].outcome", "A decision rule must map to a terminal outcome (Confirm, Extend, Not Confirmed or Additional Review)."));
            if (r.MinOverallRating is decimal min && r.MaxOverallRating is decimal max && min > max)
                errors.Add(new($"decisionRules[{i}].minOverallRating", "Minimum overall rating cannot exceed the maximum."));
        }
        // Reject ambiguous rule sets: two active rules whose inclusive ranges overlap.
        var candidates = rules.Select(r => new ConfirmationDecisionCalculator.RuleCandidate(
            Guid.NewGuid(), r.Priority, r.MinOverallRating, r.MaxOverallRating, r.Outcome, r.IsActive, r.Name ?? string.Empty)).ToList();
        if (ConfirmationDecisionCalculator.FindOverlap(candidates) is { } overlap)
            errors.Add(new("decisionRules", $"Active decision rules '{overlap.First.Name}' and '{overlap.Second.Name}' have overlapping rating ranges. Make the ranges mutually exclusive."));

        return errors.Count == 0 ? null : Result<ConfirmationPolicyEditorDto>.Invalid("One or more fields are invalid.", errors);
    }

    private static ConfirmationPolicyRule BuildRule(Guid tenantId, Guid versionId, ConfirmationProbationConfigRequest p)
    {
        var rule = new ConfirmationPolicyRule { Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationPolicyVersionId = versionId };
        ApplyRule(rule, p);
        return rule;
    }

    private static void ApplyRule(ConfirmationPolicyRule rule, ConfirmationProbationConfigRequest p)
    {
        rule.ProbationDuration = p.ProbationDuration;
        rule.ProbationDurationUnit = p.ProbationDurationUnit;
        rule.TriggerDaysBeforeEnd = p.TriggerDaysBeforeEnd;
        rule.MaxExtensions = p.MaxExtensions;
        rule.ExtensionDuration = p.ExtensionDuration;
        rule.ExtensionDurationUnit = p.ExtensionDurationUnit;
        rule.RequiredApproval = p.RequiredApproval;
        rule.ConfirmRatingThreshold = p.ConfirmRatingThreshold;
        rule.AutoConfirmOnApproval = p.AutoConfirmOnApproval;
        rule.OverallRatingMethod = p.OverallRatingMethod;
        rule.ApprovalSequence = p.ApprovalSequence;
        rule.RequireReviewerComments = p.RequireReviewerComments;
        rule.ConfirmedJobStatus = Normalize(p.ConfirmedJobStatus);
        rule.NotConfirmedJobStatus = Normalize(p.NotConfirmedJobStatus);
    }

    private static IEnumerable<ConfirmationDecisionRule> BuildDecisionRules(Guid tenantId, Guid versionId, IReadOnlyList<ConfirmationDecisionRuleRequest>? rules)
    {
        if (rules is null) yield break;
        foreach (var r in rules)
            yield return new ConfirmationDecisionRule
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationPolicyVersionId = versionId,
                Priority = r.Priority, Name = r.Name.Trim(), Description = Normalize(r.Description),
                MinOverallRating = r.MinOverallRating, MaxOverallRating = r.MaxOverallRating,
                Outcome = r.Outcome, IsActive = r.IsActive
            };
    }

    private static IEnumerable<ConfirmationPolicyApplicabilitySet> BuildApplicability(Guid tenantId, Guid versionId, IReadOnlyList<ConfirmationPolicyApplicabilityRequest>? sets)
    {
        if (sets is null) yield break;
        foreach (var s in sets)
            yield return new ConfirmationPolicyApplicabilitySet
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationPolicyVersionId = versionId,
                Gender = s.Gender, HoldingCompanyId = s.HoldingCompanyId, LobId = s.LobId, OrganisationId = s.OrganisationId,
                DepartmentId = s.DepartmentId, SubDepartmentId = s.SubDepartmentId, SectionId = s.SectionId, SubSectionId = s.SubSectionId,
                FunctionId = s.FunctionId, SubFunctionId = s.SubFunctionId, GradeId = s.GradeId, DesignationId = s.DesignationId,
                EmployeeTypeId = s.EmployeeTypeId, CountryLocationId = s.CountryLocationId, WorkLocationId = s.WorkLocationId, CostCenterId = s.CostCenterId
            };
    }

    private static IQueryable<ConfirmationPolicyDto> ProjectPolicy(IQueryable<ConfirmationPolicy> source) =>
        source.Select(x => new ConfirmationPolicyDto(
            x.Id, x.Code, x.Name, x.Description, x.IsActive,
            x.Versions.Count,
            x.Versions.Count(v => v.Status == ConfirmationPolicyVersionStatus.Published),
            x.CreatedDate, x.ModifiedDate));

    private static ConfirmationPolicyVersionDto ToVersionDto(ConfirmationPolicyVersion x, int applicabilitySetCount) =>
        new(x.Id, x.VersionNumber, x.EffectiveFrom, x.EffectiveTo, x.Status, x.Priority, applicabilitySetCount, x.CreatedDate, x.CreatedBy, x.ModifiedDate);

    private static IQueryable<ConfirmationPolicy> ApplySort(IQueryable<ConfirmationPolicy> source, ConfirmationPolicyQuery query)
    {
        var desc = query.SortDescending;
        var ordered = query.SortBy?.Trim().ToLowerInvariant() switch
        {
            "name" => desc ? source.OrderByDescending(x => x.Name) : source.OrderBy(x => x.Name),
            "isactive" => desc ? source.OrderByDescending(x => x.IsActive) : source.OrderBy(x => x.IsActive),
            "createddate" => desc ? source.OrderByDescending(x => x.CreatedDate) : source.OrderBy(x => x.CreatedDate),
            _ => desc ? source.OrderByDescending(x => x.Code) : source.OrderBy(x => x.Code)
        };
        return ordered.ThenBy(x => x.Id);
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
