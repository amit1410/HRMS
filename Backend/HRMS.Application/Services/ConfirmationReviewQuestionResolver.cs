using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Resolves the configured review questions (Phase 9c-1) that apply to an employee on a date.
/// <para>
/// Precedence / inclusion rules (deterministic):
/// <list type="number">
/// <item>A question is a candidate only when it is active and effective-dated for <c>effectiveDate</c>.</item>
/// <item>It is included when it has <em>no</em> applicability sets at all (a tenant-wide generic question),
/// or when at least one of its applicability sets matches — the set's <c>ConfirmationPolicyId</c> is null or
/// equals the resolved policy, <em>and</em> every populated dimension equals the employee's effective
/// employment snapshot (the same match semantics as <see cref="ConfirmationPolicyResolver"/>).</item>
/// <item>Applicable questions are unioned and de-duplicated by question id, then ordered by
/// <c>DisplayOrder</c>, then <c>Code</c>, then <c>Id</c> so the snapshot order is stable.</item>
/// </list>
/// This intentionally reuses the 9a specificity philosophy and does not introduce a second applicability
/// framework. Unlike policy resolution (where a single policy wins), every applicable question is included.
/// </para>
/// </summary>
public sealed class ConfirmationReviewQuestionResolver : IConfirmationReviewQuestionResolver
{
    private readonly IHrmsDbContext _db;
    private readonly IEffectiveEmploymentResolver _employmentResolver;
    private readonly ITenantContext? _tenantContext;

    public ConfirmationReviewQuestionResolver(
        IHrmsDbContext db,
        IEffectiveEmploymentResolver? employmentResolver = null,
        ITenantContext? tenantContext = null)
    {
        _db = db;
        _employmentResolver = employmentResolver ?? new EffectiveEmploymentResolver(db, tenantContext);
        _tenantContext = tenantContext;
    }

    public async Task<ConfirmationReviewQuestionResolutionResult> ResolveAsync(
        Guid tenantId, Guid employeeId, DateOnly effectiveDate, Guid? resolvedPolicyId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || (_tenantContext?.TenantId is Guid current && current != tenantId))
            return Failure(ConfirmationReviewQuestionResolutionStatus.InvalidTenant, "The requested tenant is not the authenticated tenant.");

        var employment = await _employmentResolver.ResolveAsync(tenantId, employeeId, effectiveDate, ct);
        if (employment.Status != EffectiveEmploymentResolutionStatus.Resolved || employment.Employment is null)
        {
            var status = employment.Status switch
            {
                EffectiveEmploymentResolutionStatus.NotFound => ConfirmationReviewQuestionResolutionStatus.EffectiveEmploymentNotFound,
                EffectiveEmploymentResolutionStatus.ConfigurationAmbiguity => ConfirmationReviewQuestionResolutionStatus.EffectiveEmploymentAmbiguous,
                _ => ConfirmationReviewQuestionResolutionStatus.InvalidTenant
            };
            return Failure(status, employment.Message);
        }
        var h = employment.Employment;

        var questions = await _db.ConfirmationReviewQuestions.AsNoTracking()
            .Where(q => q.TenantId == tenantId && q.IsActive
                && (q.EffectiveFrom == null || q.EffectiveFrom <= effectiveDate)
                && (q.EffectiveTo == null || effectiveDate <= q.EffectiveTo))
            .ToListAsync(ct);
        var questionIds = questions.Select(q => q.Id).ToList();

        var sets = await _db.ConfirmationReviewQuestionApplicabilitySets.AsNoTracking()
            .Where(s => s.TenantId == tenantId && questionIds.Contains(s.ConfirmationReviewQuestionId)).ToListAsync(ct);
        var options = await _db.ConfirmationReviewQuestionOptions.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.IsActive && questionIds.Contains(o.ConfirmationReviewQuestionId)).ToListAsync(ct);

        var scaleIds = questions.Where(q => q.ConfirmationRatingScaleId.HasValue).Select(q => q.ConfirmationRatingScaleId!.Value).Distinct().ToList();
        var scales = await _db.ConfirmationRatingScales.AsNoTracking().Where(s => s.TenantId == tenantId && scaleIds.Contains(s.Id)).ToListAsync(ct);
        var scaleValues = await _db.ConfirmationRatingScaleValues.AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.IsActive && scaleIds.Contains(v.ConfirmationRatingScaleId)).ToListAsync(ct);

        var resolved = new List<ResolvedReviewQuestion>();
        foreach (var q in questions)
        {
            var qSets = sets.Where(s => s.ConfirmationReviewQuestionId == q.Id).ToList();
            var applicable = qSets.Count == 0 || qSets.Any(s => MatchesPolicy(s, resolvedPolicyId) && Matches(s, h));
            if (!applicable) continue;

            var scale = q.ConfirmationRatingScaleId is Guid sid ? scales.FirstOrDefault(s => s.Id == sid) : null;
            var ratingValues = scale is null
                ? (IReadOnlyList<ResolvedReviewRatingValue>)Array.Empty<ResolvedReviewRatingValue>()
                : scaleValues.Where(v => v.ConfirmationRatingScaleId == scale.Id)
                    .OrderBy(v => v.DisplayOrder).ThenBy(v => v.RatingValue)
                    .Select(v => new ResolvedReviewRatingValue(v.RatingValue, v.Label, v.DisplayOrder)).ToList();
            var questionOptions = options.Where(o => o.ConfirmationReviewQuestionId == q.Id)
                .OrderBy(o => o.DisplayOrder).ThenBy(o => o.Code)
                .Select(o => new ResolvedReviewQuestionOption(o.Code, o.Label, o.DisplayOrder)).ToList();

            resolved.Add(new ResolvedReviewQuestion(
                q.Id, q.Code, q.QuestionText, q.HelpText, q.QuestionType, q.IsMandatory, q.DisplayOrder,
                scale?.Id, scale?.Code, scale?.Name, scale?.MinRating, scale?.MaxRating,
                questionOptions, ratingValues));
        }

        var ordered = resolved.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.QuestionId).ToList();
        return new ConfirmationReviewQuestionResolutionResult(ConfirmationReviewQuestionResolutionStatus.Resolved, ordered, "Applicable review questions resolved.");
    }

    private static bool MatchesPolicy(ConfirmationReviewQuestionApplicabilitySet s, Guid? resolvedPolicyId) =>
        !s.ConfirmationPolicyId.HasValue || s.ConfirmationPolicyId == resolvedPolicyId;

    private static bool Matches(ConfirmationReviewQuestionApplicabilitySet s, EffectiveEmploymentSnapshot h) =>
        (!s.Gender.HasValue || s.Gender == h.Gender) &&
        (!s.HoldingCompanyId.HasValue || s.HoldingCompanyId == h.HoldingCompanyId) && (!s.LobId.HasValue || s.LobId == h.LobId) &&
        (!s.OrganisationId.HasValue || s.OrganisationId == h.OrganisationId) && (!s.DepartmentId.HasValue || s.DepartmentId == h.DepartmentId) &&
        (!s.SubDepartmentId.HasValue || s.SubDepartmentId == h.SubDepartmentId) && (!s.SectionId.HasValue || s.SectionId == h.SectionId) &&
        (!s.SubSectionId.HasValue || s.SubSectionId == h.SubSectionId) && (!s.FunctionId.HasValue || s.FunctionId == h.FunctionId) &&
        (!s.SubFunctionId.HasValue || s.SubFunctionId == h.SubFunctionId) && (!s.GradeId.HasValue || s.GradeId == h.GradeId) &&
        (!s.DesignationId.HasValue || s.DesignationId == h.DesignationId) && (!s.EmployeeTypeId.HasValue || s.EmployeeTypeId == h.EmployeeTypeId) &&
        (!s.CountryLocationId.HasValue || s.CountryLocationId == h.CountryLocationId) && (!s.WorkLocationId.HasValue || s.WorkLocationId == h.WorkLocationId) &&
        (!s.CostCenterId.HasValue || s.CostCenterId == h.CostCenterId);

    private static ConfirmationReviewQuestionResolutionResult Failure(ConfirmationReviewQuestionResolutionStatus status, string message) =>
        new(status, Array.Empty<ResolvedReviewQuestion>(), message);
}
