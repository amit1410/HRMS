using FluentValidation;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class EmployeeCodeConfigurationService : IEmployeeCodeConfigurationService
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IValidator<EmployeeCodeConfigurationRequest> _validator;

    public EmployeeCodeConfigurationService(IHrmsDbContext db, ITenantContext tenant, IValidator<EmployeeCodeConfigurationRequest> validator)
    { _db = db; _tenant = tenant; _validator = validator; }

    public async Task<Result<EmployeeCodeConfigurationDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid) return Result<EmployeeCodeConfigurationDto>.Unauthorized("No authenticated tenant.");
        var config = await _db.EmployeeCodeConfigs.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return config is null ? Result<EmployeeCodeConfigurationDto>.NotFound("Employee Code configuration has not been created.") : Result<EmployeeCodeConfigurationDto>.Success(Map(config));
    }

    public async Task<Result<EmployeeCodeConfigurationDto>> SaveAsync(EmployeeCodeConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<EmployeeCodeConfigurationDto>.Unauthorized("No authenticated tenant.");
        // Legacy callers still send AutoGenerate only; derive the explicit mode for compatibility.
        if (!request.AutoGenerate)
        {
            request.AssignmentMode = EmployeeCodeAssignmentMode.Manual;
            request.GenerationMethod = null;
        }
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<EmployeeCodeConfigurationDto>.Invalid("Employee Code configuration is invalid.", validation.Errors.Select(e => new ValidationError(e.PropertyName, e.ErrorMessage)).ToList());
        var config = await _db.EmployeeCodeConfigs.FirstOrDefaultAsync(cancellationToken);
        if (config is null) { config = new EmployeeCodeConfig { Id = Guid.NewGuid(), TenantId = tenantId }; _db.EmployeeCodeConfigs.Add(config); }
        if (request.AutoGenerate)
        {
            var hasUsableRule = await _db.EmployeeCodeRules.AnyAsync(r => r.EmployeeCodeConfigId == config.Id && !r.IsDeleted && r.Status == EmployeeCodeRuleStatus.Active && (r.IsDefault || r.Segments.Any()), cancellationToken);
            if (!hasUsableRule && config.Id != Guid.Empty)
                return Result<EmployeeCodeConfigurationDto>.Invalid("autoGenerate", "At least one active Employee Code rule is required before Auto mode can be enabled.");
        }
        config.AssignmentMode = request.AssignmentMode;
        config.GenerationMethod = request.AssignmentMode == EmployeeCodeAssignmentMode.Manual ? null : request.GenerationMethod;
        config.AutoGenerate = request.AssignmentMode == EmployeeCodeAssignmentMode.Auto;
        config.Prefix = request.Prefix.Trim().ToUpperInvariant(); config.NextNumber = request.NextNumber; config.Padding = request.Padding; config.Separator = request.Separator; config.EffectiveFrom = request.EffectiveFrom; config.EffectiveTo = request.EffectiveTo;
        await _db.SaveChangesAsync(cancellationToken);
        return Result<EmployeeCodeConfigurationDto>.Success(Map(config), "Employee Code configuration saved.");
    }

    public async Task<Result<IReadOnlyList<EmployeeCodeRuleDto>>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid) return Result<IReadOnlyList<EmployeeCodeRuleDto>>.Unauthorized("No authenticated tenant.");
        var rules = await _db.EmployeeCodeRules.AsNoTracking().Where(r => !r.IsDeleted).Include(r => r.Conditions).Include(r => r.Segments).OrderBy(r => r.Priority).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<EmployeeCodeRuleDto>>.Success(rules.Select(MapRule).ToList());
    }

    public async Task<Result<EmployeeCodeRuleDto>> GetRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<EmployeeCodeRuleDto>.Unauthorized("No authenticated tenant.");
        var rule = await _db.EmployeeCodeRules.AsNoTracking()
            .Where(r => r.Id == id && r.TenantId == tenantId && !r.IsDeleted)
            .Include(r => r.Conditions).Include(r => r.Segments)
            .SingleOrDefaultAsync(cancellationToken);
        return rule is null ? Result<EmployeeCodeRuleDto>.NotFound("Employee Code rule was not found.") : Result<EmployeeCodeRuleDto>.Success(MapRule(rule));
    }

    public async Task<Result<EmployeeCodeRuleDto>> SaveRuleAsync(Guid? id, EmployeeCodeRuleRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<EmployeeCodeRuleDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Priority < 0)
            return Result<EmployeeCodeRuleDto>.Invalid("Rule name and a non-negative priority are required.");
        if (request.Status == EmployeeCodeRuleStatus.Active && request.Segments.Count == 0)
            return Result<EmployeeCodeRuleDto>.Invalid("Active rules require at least one segment.");
        if (request.Status == EmployeeCodeRuleStatus.Active && !request.IsDefault && request.Conditions.Count == 0)
            return Result<EmployeeCodeRuleDto>.Invalid("Active specific rules require at least one matching condition.");
        if (request.IsDefault && request.Conditions.Count > 0)
            return Result<EmployeeCodeRuleDto>.Invalid("Default fallback rules cannot contain conditions.");
        var sequenceSegments = request.Segments.Count(s => s.SegmentType == EmployeeCodeSegmentType.SequentialNumber);
        if (request.Status == EmployeeCodeRuleStatus.Active && sequenceSegments != 1)
            return Result<EmployeeCodeRuleDto>.Invalid("A rule must contain exactly one sequential-number segment.");
        if (request.Segments.Any(s => s.SegmentType is EmployeeCodeSegmentType.FixedText or EmployeeCodeSegmentType.CustomConstant && string.IsNullOrWhiteSpace(s.FixedValue)))
            return Result<EmployeeCodeRuleDto>.Invalid("Fixed text and custom constant segments require a value.");
        if (request.Segments.Any(s => s.SegmentType == EmployeeCodeSegmentType.SequentialNumber && (s.PaddingLength is < 0 or > 12)))
            return Result<EmployeeCodeRuleDto>.Invalid("Sequence padding must be between 0 and 12.");
        var config = await _db.EmployeeCodeConfigs.FirstOrDefaultAsync(cancellationToken);
        if (config is null) return Result<EmployeeCodeRuleDto>.Invalid("Create Employee Code configuration before adding rules.");
        var version = await _db.EmployeeCodeConfigVersions
            .Where(v => v.EmployeeCodeConfigId == config.Id && v.IsActive)
            .OrderByDescending(v => v.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        if (version is null) return Result<EmployeeCodeRuleDto>.Invalid("No active Employee Code configuration version exists.");
        if (request.IsDefault && request.Status == EmployeeCodeRuleStatus.Active)
        {
            var existingDefault = await _db.EmployeeCodeRules.AnyAsync(r => r.Id != id && r.EmployeeCodeConfigVersionId == version.Id && !r.IsDeleted && r.IsDefault && r.Status == EmployeeCodeRuleStatus.Active, cancellationToken);
            if (existingDefault) return Result<EmployeeCodeRuleDto>.Conflict("Only one active default Employee Code rule is allowed.");
        }
        var rule = id.HasValue ? await _db.EmployeeCodeRules.Where(r => !r.IsDeleted).Include(r => r.Conditions).Include(r => r.Segments).FirstOrDefaultAsync(r => r.Id == id.Value, cancellationToken) : null;
        if (rule is null) { rule = new EmployeeCodeRule { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCodeConfigId = config.Id, EmployeeCodeConfigVersionId = version.Id }; _db.EmployeeCodeRules.Add(rule); }
        rule.Name = request.Name.Trim(); rule.Priority = request.Priority; rule.IsDefault = request.IsDefault; rule.Status = request.Status;
        var conditionEntities = new List<EmployeeCodeRuleCondition>();
        var requestedConditionIds = request.Conditions.Where(c => c.Id.HasValue).Select(c => c.Id!.Value).ToHashSet();
        _db.EmployeeCodeRuleConditions.RemoveRange(rule.Conditions.Where(c => !requestedConditionIds.Contains(c.Id)));
        foreach (var c in request.Conditions)
        {
            var code = c.Value?.Trim();
            if (c.ReferenceId.HasValue && string.IsNullOrWhiteSpace(code))
            {
                code = c.Field switch
                {
                    EmployeeCodeConditionField.HoldingCompany => await _db.HoldingCompanies.Where(x => x.Id == c.ReferenceId && x.TenantId == tenantId).Select(x => x.Code).FirstOrDefaultAsync(cancellationToken),
                    EmployeeCodeConditionField.Lob => await _db.LinesOfBusiness.Where(x => x.Id == c.ReferenceId && x.TenantId == tenantId).Select(x => x.Code).FirstOrDefaultAsync(cancellationToken),
                    EmployeeCodeConditionField.Organisation => await _db.Organisations.Where(x => x.Id == c.ReferenceId && x.TenantId == tenantId).Select(x => x.Code).FirstOrDefaultAsync(cancellationToken),
                    EmployeeCodeConditionField.Department => await _db.Departments.Where(x => x.Id == c.ReferenceId && x.TenantId == tenantId).Select(x => x.Code).FirstOrDefaultAsync(cancellationToken),
                    _ => null
                };
                if (code is null) return Result<EmployeeCodeRuleDto>.Invalid("condition", "The selected master value is invalid or belongs to another tenant.");
            }
            var condition = c.Id.HasValue ? rule.Conditions.FirstOrDefault(existing => existing.Id == c.Id.Value) : null;
            if (condition is null) { condition = new EmployeeCodeRuleCondition { Id = c.Id ?? Guid.NewGuid(), TenantId = tenantId, EmployeeCodeRuleId = rule.Id }; _db.EmployeeCodeRuleConditions.Add(condition); }
            condition.Field = c.Field; condition.Operator = c.Operator; condition.ReferenceId = c.ReferenceId; condition.Value = code;
            conditionEntities.Add(condition);
        }
        rule.Conditions = conditionEntities;
        var requestedSegmentIds = request.Segments.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToHashSet();
        _db.EmployeeCodeSegments.RemoveRange(rule.Segments.Where(s => !requestedSegmentIds.Contains(s.Id)));
        var segmentEntities = new List<EmployeeCodeSegment>();
        foreach (var s in request.Segments.OrderBy(s => s.SequenceOrder))
        {
            var segment = s.Id.HasValue ? rule.Segments.FirstOrDefault(existing => existing.Id == s.Id.Value) : null;
            if (segment is null) { segment = new EmployeeCodeSegment { Id = s.Id ?? Guid.NewGuid(), TenantId = tenantId, EmployeeCodeRuleId = rule.Id }; _db.EmployeeCodeSegments.Add(segment); }
            segment.SequenceOrder = s.SequenceOrder; segment.SegmentType = s.SegmentType; segment.FixedValue = s.FixedValue?.Trim(); segment.PaddingLength = s.PaddingLength;
            segmentEntities.Add(segment);
        }
        rule.Segments = segmentEntities;
        await _db.SaveChangesAsync(cancellationToken);
        return Result<EmployeeCodeRuleDto>.Success(MapRule(rule), "Employee Code rule saved.");
    }

    public async Task<Result<EmployeeCodeRuleDto>> SoftDeleteRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<EmployeeCodeRuleDto>.Unauthorized("No authenticated tenant.");
        var rule = await _db.EmployeeCodeRules.Include(r => r.Conditions).Include(r => r.Segments).FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId && !r.IsDeleted, cancellationToken);
        if (rule is null) return Result<EmployeeCodeRuleDto>.NotFound("Employee Code rule was not found.");
        if (rule.IsDefault && rule.Status == EmployeeCodeRuleStatus.Active)
        {
            var anotherFallback = await _db.EmployeeCodeRules.AnyAsync(r => r.Id != id && r.EmployeeCodeConfigVersionId == rule.EmployeeCodeConfigVersionId && !r.IsDeleted && r.IsDefault && r.Status == EmployeeCodeRuleStatus.Active, cancellationToken);
            if (!anotherFallback) return Result<EmployeeCodeRuleDto>.Conflict("Cannot delete the active default fallback rule while Rule-Based generation is active. Create or activate another fallback rule first.");
        }
        rule.IsDeleted = true;
        rule.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result<EmployeeCodeRuleDto>.Success(MapRule(rule), "Employee Code rule deleted.");
    }

    private static EmployeeCodeConfigurationDto Map(EmployeeCodeConfig config) => new(config.Id, config.AutoGenerate, config.AssignmentMode, config.GenerationMethod, config.Prefix, config.NextNumber, config.Padding, config.Separator, config.EffectiveFrom, config.EffectiveTo);
    private static EmployeeCodeRuleDto MapRule(EmployeeCodeRule rule) => new(rule.Id, rule.Name, rule.Priority, rule.IsDefault, rule.Status, rule.Conditions.OrderBy(c => c.Id).Select(c => new EmployeeCodeConditionDto(c.Id, c.Field, c.Operator, c.ReferenceId, c.Value)).ToList(), rule.Segments.OrderBy(s => s.SequenceOrder).Select(s => new EmployeeCodeSegmentDto(s.Id, s.SequenceOrder, s.SegmentType, s.FixedValue, s.PaddingLength)).ToList());
}
