using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Tenant-configurable confirmation review questions. Every read and write goes through the tenant-filtered
/// DbContext sets, so cross-tenant rows are simply absent and no method trusts a caller-supplied tenant id.
/// A question owns its options (for Dropdown/MultiSelect) and applicability rows; an update replaces both
/// child sets atomically. Rating questions must reference a rating scale that exists in the tenant.
/// </summary>
public sealed class ConfirmationReviewQuestionService : IConfirmationReviewQuestionService
{
    private const string NoTenant = "No authenticated tenant.";
    private const string QuestionNotFound = "Review question not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;

    public ConfirmationReviewQuestionService(IHrmsDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Result<PagedResult<ConfirmationReviewQuestionDto>>> GetQuestionsAsync(ConfirmationReviewQuestionQuery query, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<PagedResult<ConfirmationReviewQuestionDto>>.Unauthorized(NoTenant);

        var source = _db.ConfirmationReviewQuestions.AsNoTracking();
        if (query.IsActive.HasValue) source = source.Where(x => x.IsActive == query.IsActive.Value);
        if (query.QuestionType.HasValue) source = source.Where(x => x.QuestionType == query.QuestionType.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLowerInvariant();
            source = source.Where(x => x.Code.ToLower().Contains(s) || x.QuestionText.ToLower().Contains(s));
        }

        var ordered = ApplySort(source, query);
        var page = await ordered.Select(x => new ConfirmationReviewQuestionDto(
                x.Id, x.Code, x.QuestionText, x.HelpText, x.QuestionType, x.IsMandatory, x.IsActive, x.DisplayOrder,
                x.EffectiveFrom, x.EffectiveTo, x.ConfirmationRatingScaleId,
                x.ConfirmationRatingScale != null ? x.ConfirmationRatingScale.Name : null,
                x.Options.Count, x.ApplicabilitySets.Count, x.CreatedDate, x.ModifiedDate))
            .ToPagedResultAsync(query, ct);
        return Result<PagedResult<ConfirmationReviewQuestionDto>>.Success(page);
    }

    public async Task<Result<ConfirmationReviewQuestionDetailDto>> GetQuestionAsync(Guid questionId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<ConfirmationReviewQuestionDetailDto>.Unauthorized(NoTenant);
        var question = await _db.ConfirmationReviewQuestions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == questionId, ct);
        if (question is null) return Result<ConfirmationReviewQuestionDetailDto>.NotFound(QuestionNotFound);
        return Result<ConfirmationReviewQuestionDetailDto>.Success(await BuildDetailAsync(question, ct));
    }

    public async Task<Result<ConfirmationReviewQuestionDetailDto>> CreateQuestionAsync(ConfirmationReviewQuestionRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<ConfirmationReviewQuestionDetailDto>.Unauthorized(NoTenant);
        var validation = await ValidateAsync(request, ct);
        if (validation is not null) return validation;

        var code = request.Code.Trim();
        if (await _db.ConfirmationReviewQuestions.AnyAsync(x => x.Code.ToLower() == code.ToLower(), ct))
            return Conflict(code);

        var question = new ConfirmationReviewQuestion
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Code = code, QuestionText = request.QuestionText.Trim(),
            HelpText = Normalize(request.HelpText), QuestionType = request.QuestionType, IsMandatory = request.IsMandatory,
            IsActive = request.IsActive, DisplayOrder = request.DisplayOrder, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo,
            ConfirmationRatingScaleId = request.QuestionType == ReviewQuestionType.Rating ? request.ConfirmationRatingScaleId : null
        };
        _db.ConfirmationReviewQuestions.Add(question);
        foreach (var option in BuildOptions(tenantId, question.Id, request)) _db.ConfirmationReviewQuestionOptions.Add(option);
        foreach (var set in BuildApplicability(tenantId, question.Id, request.Applicability)) _db.ConfirmationReviewQuestionApplicabilitySets.Add(set);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(code); }

        return Result<ConfirmationReviewQuestionDetailDto>.Success(await BuildDetailAsync(question, ct), "Review question created.");
    }

    public async Task<Result<ConfirmationReviewQuestionDetailDto>> UpdateQuestionAsync(Guid questionId, ConfirmationReviewQuestionRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<ConfirmationReviewQuestionDetailDto>.Unauthorized(NoTenant);
        var validation = await ValidateAsync(request, ct);
        if (validation is not null) return validation;

        var question = await _db.ConfirmationReviewQuestions.FirstOrDefaultAsync(x => x.Id == questionId, ct);
        if (question is null) return Result<ConfirmationReviewQuestionDetailDto>.NotFound(QuestionNotFound);

        var code = request.Code.Trim();
        if (await _db.ConfirmationReviewQuestions.AnyAsync(x => x.Id != questionId && x.Code.ToLower() == code.ToLower(), ct))
            return Conflict(code);

        question.Code = code; question.QuestionText = request.QuestionText.Trim(); question.HelpText = Normalize(request.HelpText);
        question.QuestionType = request.QuestionType; question.IsMandatory = request.IsMandatory; question.IsActive = request.IsActive;
        question.DisplayOrder = request.DisplayOrder; question.EffectiveFrom = request.EffectiveFrom; question.EffectiveTo = request.EffectiveTo;
        question.ConfirmationRatingScaleId = request.QuestionType == ReviewQuestionType.Rating ? request.ConfirmationRatingScaleId : null;

        var existingOptions = await _db.ConfirmationReviewQuestionOptions.Where(x => x.ConfirmationReviewQuestionId == questionId).ToListAsync(ct);
        _db.ConfirmationReviewQuestionOptions.RemoveRange(existingOptions);
        foreach (var option in BuildOptions(tenantId, questionId, request)) _db.ConfirmationReviewQuestionOptions.Add(option);

        var existingSets = await _db.ConfirmationReviewQuestionApplicabilitySets.Where(x => x.ConfirmationReviewQuestionId == questionId).ToListAsync(ct);
        _db.ConfirmationReviewQuestionApplicabilitySets.RemoveRange(existingSets);
        foreach (var set in BuildApplicability(tenantId, questionId, request.Applicability)) _db.ConfirmationReviewQuestionApplicabilitySets.Add(set);

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(code); }

        return await GetQuestionAsync(questionId, ct);
    }

    // --- helpers ---

    private async Task<ConfirmationReviewQuestionDetailDto> BuildDetailAsync(ConfirmationReviewQuestion question, CancellationToken ct)
    {
        var scaleName = question.ConfirmationRatingScaleId is Guid sid
            ? await _db.ConfirmationRatingScales.AsNoTracking().Where(x => x.Id == sid).Select(x => x.Name).FirstOrDefaultAsync(ct)
            : null;
        var options = await _db.ConfirmationReviewQuestionOptions.AsNoTracking()
            .Where(x => x.ConfirmationReviewQuestionId == question.Id)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code)
            .Select(x => new ConfirmationReviewQuestionOptionDto(x.Id, x.Code, x.Label, x.DisplayOrder, x.IsActive))
            .ToListAsync(ct);
        var sets = await _db.ConfirmationReviewQuestionApplicabilitySets.AsNoTracking()
            .Where(x => x.ConfirmationReviewQuestionId == question.Id)
            .Select(s => new ConfirmationReviewQuestionApplicabilityDto(
                s.Id, s.ConfirmationPolicyId, s.Gender, s.HoldingCompanyId, s.LobId, s.OrganisationId, s.DepartmentId,
                s.SubDepartmentId, s.SectionId, s.SubSectionId, s.FunctionId, s.SubFunctionId, s.GradeId, s.DesignationId,
                s.EmployeeTypeId, s.CountryLocationId, s.WorkLocationId, s.CostCenterId))
            .ToListAsync(ct);
        var dto = new ConfirmationReviewQuestionDto(question.Id, question.Code, question.QuestionText, question.HelpText,
            question.QuestionType, question.IsMandatory, question.IsActive, question.DisplayOrder, question.EffectiveFrom, question.EffectiveTo,
            question.ConfirmationRatingScaleId, scaleName, options.Count, sets.Count, question.CreatedDate, question.ModifiedDate);
        return new ConfirmationReviewQuestionDetailDto(dto, options, sets);
    }

    private async Task<Result<ConfirmationReviewQuestionDetailDto>?> ValidateAsync(ConfirmationReviewQuestionRequest request, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        if (request.EffectiveTo is DateOnly to && request.EffectiveFrom is DateOnly from && to < from)
            errors.Add(new("effectiveTo", "Effective-to cannot be before effective-from."));

        var options = request.Options ?? Array.Empty<ConfirmationReviewQuestionOptionRequest>();
        var needsOptions = request.QuestionType is ReviewQuestionType.Dropdown or ReviewQuestionType.MultiSelect;
        if (needsOptions && options.Count == 0)
            errors.Add(new("options", "At least one option is required for a dropdown or multi-select question."));
        if (!needsOptions && options.Count > 0)
            errors.Add(new("options", "Options are only allowed for dropdown or multi-select questions."));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < options.Count; i++)
        {
            var o = options[i];
            if (string.IsNullOrWhiteSpace(o.Code)) errors.Add(new($"options[{i}].code", "Option code is required."));
            else if (!seen.Add(o.Code.Trim())) errors.Add(new($"options[{i}].code", "Duplicate option code."));
            if (string.IsNullOrWhiteSpace(o.Label)) errors.Add(new($"options[{i}].label", "Option label is required."));
        }

        if (request.QuestionType == ReviewQuestionType.Rating)
        {
            if (request.ConfirmationRatingScaleId is not Guid scaleId)
                errors.Add(new("confirmationRatingScaleId", "A rating scale is required for a rating question."));
            else if (!await _db.ConfirmationRatingScales.AnyAsync(x => x.Id == scaleId, ct))
                errors.Add(new("confirmationRatingScaleId", "The selected rating scale does not exist."));
        }
        else if (request.ConfirmationRatingScaleId.HasValue)
        {
            errors.Add(new("confirmationRatingScaleId", "A rating scale may only be set on a rating question."));
        }

        foreach (var (set, i) in (request.Applicability ?? Array.Empty<ConfirmationReviewQuestionApplicabilityRequest>()).Select((s, i) => (s, i)))
        {
            if (set.ConfirmationPolicyId is Guid pid && !await _db.ConfirmationPolicies.AnyAsync(x => x.Id == pid, ct))
                errors.Add(new($"applicability[{i}].confirmationPolicyId", "The selected confirmation policy does not exist."));
        }

        return errors.Count == 0 ? null : Result<ConfirmationReviewQuestionDetailDto>.Invalid("One or more fields are invalid.", errors);
    }

    private static Result<ConfirmationReviewQuestionDetailDto> Conflict(string code) =>
        Result<ConfirmationReviewQuestionDetailDto>.Conflict($"A review question with code '{code}' already exists.", [new ValidationError("code", "This code is already in use.")]);

    private static IEnumerable<ConfirmationReviewQuestionOption> BuildOptions(Guid tenantId, Guid questionId, ConfirmationReviewQuestionRequest request)
    {
        var needsOptions = request.QuestionType is ReviewQuestionType.Dropdown or ReviewQuestionType.MultiSelect;
        if (!needsOptions || request.Options is null) yield break;
        foreach (var o in request.Options)
            yield return new ConfirmationReviewQuestionOption
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationReviewQuestionId = questionId,
                Code = o.Code.Trim(), Label = o.Label.Trim(), DisplayOrder = o.DisplayOrder, IsActive = o.IsActive
            };
    }

    private static IEnumerable<ConfirmationReviewQuestionApplicabilitySet> BuildApplicability(Guid tenantId, Guid questionId, IReadOnlyList<ConfirmationReviewQuestionApplicabilityRequest>? sets)
    {
        if (sets is null) yield break;
        foreach (var s in sets)
            yield return new ConfirmationReviewQuestionApplicabilitySet
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationReviewQuestionId = questionId,
                ConfirmationPolicyId = s.ConfirmationPolicyId, Gender = s.Gender,
                HoldingCompanyId = s.HoldingCompanyId, LobId = s.LobId, OrganisationId = s.OrganisationId,
                DepartmentId = s.DepartmentId, SubDepartmentId = s.SubDepartmentId, SectionId = s.SectionId, SubSectionId = s.SubSectionId,
                FunctionId = s.FunctionId, SubFunctionId = s.SubFunctionId, GradeId = s.GradeId, DesignationId = s.DesignationId,
                EmployeeTypeId = s.EmployeeTypeId, CountryLocationId = s.CountryLocationId, WorkLocationId = s.WorkLocationId, CostCenterId = s.CostCenterId
            };
    }

    private static IQueryable<ConfirmationReviewQuestion> ApplySort(IQueryable<ConfirmationReviewQuestion> source, ConfirmationReviewQuestionQuery query)
    {
        var desc = query.SortDescending;
        var ordered = query.SortBy?.Trim().ToLowerInvariant() switch
        {
            "displayorder" => desc ? source.OrderByDescending(x => x.DisplayOrder) : source.OrderBy(x => x.DisplayOrder),
            "questiontype" => desc ? source.OrderByDescending(x => x.QuestionType) : source.OrderBy(x => x.QuestionType),
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
