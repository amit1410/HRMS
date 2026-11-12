using FluentValidation;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.EmployeeCodes;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

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
        if (config is null) return Result<EmployeeCodeConfigurationDto>.NotFound("Employee Code configuration has not been created.");
        var version = await _db.EmployeeCodeConfigVersions.AsNoTracking()
            .Where(v => v.EmployeeCodeConfigId == config.Id)
            .OrderByDescending(v => v.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);
        return Result<EmployeeCodeConfigurationDto>.Success(Map(config, version));
    }

    public async Task<Result<IReadOnlyList<EmployeeCodeConfigurationDto>>> GetVersionsAsync(CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<IReadOnlyList<EmployeeCodeConfigurationDto>>.Unauthorized("No authenticated tenant.");
        var config = await _db.EmployeeCodeConfigs.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (config is null) return Result<IReadOnlyList<EmployeeCodeConfigurationDto>>.Success([]);
        var versions = await _db.EmployeeCodeConfigVersions.AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.EmployeeCodeConfigId == config.Id)
            .OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.CreatedDate)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<EmployeeCodeConfigurationDto>>.Success(versions.Select(v => Map(config, v)).ToList());
    }

    public async Task<Result<IReadOnlyList<EmployeeCodeConfigurationAuditDto>>> GetAuditAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<IReadOnlyList<EmployeeCodeConfigurationAuditDto>>.Unauthorized("No authenticated tenant.");
        var versionExists = await _db.EmployeeCodeConfigVersions.AnyAsync(v => v.TenantId == tenantId && v.Id == versionId, cancellationToken);
        if (!versionExists) return Result<IReadOnlyList<EmployeeCodeConfigurationAuditDto>>.NotFound("Employee Code configuration version was not found.");
        var events = await _db.EmployeeCodeConfigurationAuditEvents.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeCodeConfigVersionId == versionId)
            .OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
            .Select(x => new EmployeeCodeConfigurationAuditDto(x.Id, x.EmployeeCodeConfigVersionId,
                x.Action, x.ActorUserId, x.OccurredAtUtc, x.OldValue, x.NewValue))
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<EmployeeCodeConfigurationAuditDto>>.Success(events);
    }

    public async Task<Result<EmployeeCodeConfigurationDto>> SaveAsync(EmployeeCodeConfigurationRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<EmployeeCodeConfigurationDto>.Unauthorized("No authenticated tenant.");
        if (_tenant.UserId is not Guid actorUserId) return Result<EmployeeCodeConfigurationDto>.Unauthorized("An authenticated user is required to change Employee Code configuration.");
        // Legacy callers still send AutoGenerate only; derive the explicit mode for compatibility.
        if (!request.AutoGenerate)
        {
            request.AssignmentMode = EmployeeCodeAssignmentMode.Manual;
            request.GenerationMethod = null;
        }
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Result<EmployeeCodeConfigurationDto>.Invalid("Employee Code configuration is invalid.", validation.Errors.Select(e => new ValidationError(e.PropertyName, e.ErrorMessage)).ToList());
        await using var transaction = await _db.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var config = await _db.EmployeeCodeConfigs.FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (config is null) { config = new EmployeeCodeConfig { Id = Guid.NewGuid(), TenantId = tenantId }; _db.EmployeeCodeConfigs.Add(config); }

        // Rules are optional while a configuration is being created. Rule-Based generation reports a
        // useful error at employment time until an active matching/fallback rule exists; Simple mode
        // must never depend on a rule at all.
        var version = request.VersionId is Guid versionId
            ? await _db.EmployeeCodeConfigVersions.FirstOrDefaultAsync(v => v.Id == versionId && v.EmployeeCodeConfigId == config.Id, cancellationToken)
            : await _db.EmployeeCodeConfigVersions.FirstOrDefaultAsync(v => v.EmployeeCodeConfigId == config.Id && v.EffectiveFrom == request.EffectiveFrom, cancellationToken);
        if (request.VersionId is not null && version is null)
            return Result<EmployeeCodeConfigurationDto>.NotFound("The selected Employee Code configuration version was not found.");

        var isNewVersion = version is null;
        string? oldSnapshot = null;
        if (version is not null)
        {
            oldSnapshot = Snapshot(version);
            if (request.NextNumber < version.NextNumber)
                return InvalidField("NextNumber", "Next number cannot be lower than the current sequence value.");

            // Codes have no direct version foreign key. Protect a version when a coded employee's
            // joining date or first employment effective date falls in its period; the latter is the
            // date used by employment-time code generation and may differ from DateOfJoining.
            var assignedEmployeeIds = await _db.Employees.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.TenantId == tenantId && e.EmployeeCode != null &&
                    ((e.DateOfJoining >= version.EffectiveFrom &&
                      (version.EffectiveTo == null || e.DateOfJoining <= version.EffectiveTo)) ||
                     _db.EmployeeEmploymentHistory.IgnoreQueryFilters().Any(h => h.TenantId == tenantId &&
                         h.EmployeeId == e.Id && h.EffectiveFrom >= version.EffectiveFrom &&
                         (version.EffectiveTo == null || h.EffectiveFrom <= version.EffectiveTo))))
                .Select(e => e.Id).ToListAsync(cancellationToken);
            var assignedUseDates = await _db.EmployeeEmploymentHistory.IgnoreQueryFilters().AsNoTracking()
                .Where(h => h.TenantId == tenantId && assignedEmployeeIds.Contains(h.EmployeeId) &&
                    h.EffectiveFrom >= version.EffectiveFrom &&
                    (version.EffectiveTo == null || h.EffectiveFrom <= version.EffectiveTo))
                .Select(h => h.EffectiveFrom).Distinct().ToListAsync(cancellationToken);
            var assignedJoiningDates = await _db.Employees.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.TenantId == tenantId && assignedEmployeeIds.Contains(e.Id) &&
                    e.DateOfJoining >= version.EffectiveFrom &&
                    (version.EffectiveTo == null || e.DateOfJoining <= version.EffectiveTo))
                .Select(e => e.DateOfJoining).ToListAsync(cancellationToken);
            var assignedUseDatesForValidation = assignedUseDates.Concat(assignedJoiningDates).Distinct().ToList();
            if (assignedEmployeeIds.Count > 0)
            {
                if (request.AssignmentMode != version.AssignmentMode)
                    return InvalidField("AssignmentMode", "Assignment mode cannot change after employee codes have been assigned in this effective period.");
                if (request.GenerationMethod != version.GenerationMethod)
                    return InvalidField("GenerationMethod", "Generation method cannot change after employee codes have been assigned in this effective period.");
                if (!string.Equals(request.Prefix.Trim(), version.Prefix, StringComparison.OrdinalIgnoreCase))
                    return InvalidField("Prefix", "Prefix cannot change after employee codes have been assigned in this effective period.");
                if (request.Separator != version.Separator)
                    return InvalidField("Separator", "Separator cannot change after employee codes have been assigned in this effective period.");
                if (request.Padding != version.Padding)
                    return InvalidField("Padding", "Padding cannot change after employee codes have been assigned in this effective period.");
                var requestedEndForAssignments = request.EffectiveTo ?? DateOnly.MaxValue;
                if (assignedUseDatesForValidation.Any(date => date < request.EffectiveFrom || date > requestedEndForAssignments))
                {
                    var field = assignedUseDatesForValidation.Any(date => date < request.EffectiveFrom) ? "EffectiveFrom" : "EffectiveTo";
                    return InvalidField(field, "The effective date range cannot exclude employees whose codes were assigned during this configuration period.");
                }
            }

        }
        if (version is null)
        {
            var requestedEnd = request.EffectiveTo ?? DateOnly.MaxValue;
            var overlapping = await _db.EmployeeCodeConfigVersions
                .Where(v => v.TenantId == tenantId && v.EmployeeCodeConfigId == config.Id && v.IsActive)
                .ToListAsync(cancellationToken);
            var conflicting = overlapping.FirstOrDefault(v =>
                request.EffectiveFrom <= (v.EffectiveTo ?? DateOnly.MaxValue) &&
                v.EffectiveFrom <= requestedEnd);
            if (conflicting is not null)
            {
                return Result<EmployeeCodeConfigurationDto>.Conflict(
                    "Another Employee Code configuration is already effective for part of the selected date range.");
            }

            version = new EmployeeCodeConfigVersion
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeCodeConfigId = config.Id
            };
            _db.EmployeeCodeConfigVersions.Add(version);
        }
        else
        {
            var requestedEnd = request.EffectiveTo ?? DateOnly.MaxValue;
            var conflicting = await _db.EmployeeCodeConfigVersions
                .Where(v => v.TenantId == tenantId && v.EmployeeCodeConfigId == config.Id && v.Id != version.Id && v.IsActive)
                .AnyAsync(v => request.EffectiveFrom <= (v.EffectiveTo ?? DateOnly.MaxValue) &&
                               v.EffectiveFrom <= requestedEnd, cancellationToken);
            if (conflicting)
                return Result<EmployeeCodeConfigurationDto>.Conflict("Another active Employee Code configuration is already effective for part of the selected date range.");
        }

        if (request.AssignmentMode == EmployeeCodeAssignmentMode.Auto && request.GenerationMethod == EmployeeCodeGenerationMethod.Simple)
        {
            var candidate = FormatSimpleCode(request.Prefix, request.Separator, request.Padding, request.NextNumber);
            var duplicate = await _db.Employees.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(e => e.TenantId == tenantId && e.EmployeeCode != null && e.EmployeeCode.ToUpper() == candidate.ToUpper(), cancellationToken);
            if (duplicate)
                return InvalidField("NextNumber", "The next generated employee code is already assigned. Increase the sequence until the next code is unique.");
        }

        version.AutoGenerate = request.AssignmentMode == EmployeeCodeAssignmentMode.Auto;
        version.AssignmentMode = request.AssignmentMode;
        version.GenerationMethod = request.AssignmentMode == EmployeeCodeAssignmentMode.Manual ? null : request.GenerationMethod;
        version.Prefix = request.Prefix.Trim().ToUpperInvariant();
        version.NextNumber = request.NextNumber;
        version.Padding = request.Padding;
        version.Separator = request.Separator;
        version.EffectiveFrom = request.EffectiveFrom;
        version.EffectiveTo = request.EffectiveTo;
        version.IsActive = request.IsActive;

        var latestVersion = await _db.EmployeeCodeConfigVersions.AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.EmployeeCodeConfigId == config.Id && v.Id != version.Id)
            .OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.CreatedDate)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestVersion is null || version.EffectiveFrom >= latestVersion.EffectiveFrom)
            latestVersion = version;
        config.AssignmentMode = latestVersion.AssignmentMode;
        config.GenerationMethod = latestVersion.GenerationMethod;
        config.AutoGenerate = latestVersion.AutoGenerate;
        config.Prefix = latestVersion.Prefix;
        config.NextNumber = Math.Max(config.NextNumber, latestVersion.NextNumber);
        config.Padding = latestVersion.Padding;
        config.Separator = latestVersion.Separator;
        config.EffectiveFrom = latestVersion.EffectiveFrom;
        config.EffectiveTo = latestVersion.EffectiveTo;

        var newSnapshot = Snapshot(version);
        if (isNewVersion || !string.Equals(oldSnapshot, newSnapshot, StringComparison.Ordinal))
        {
            _db.EmployeeCodeConfigurationAuditEvents.Add(new EmployeeCodeConfigurationAuditEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCodeConfigId = config.Id,
                EmployeeCodeConfigVersionId = version.Id, ActorUserId = actorUserId,
                Action = isNewVersion ? "Created" : "Updated", OccurredAtUtc = DateTime.UtcNow,
                OldValue = oldSnapshot, NewValue = newSnapshot
            });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<EmployeeCodeConfigurationDto>.Conflict("The Employee Code sequence changed while saving. Reload the configuration and try again.");
        }
        return Result<EmployeeCodeConfigurationDto>.Success(Map(config, version), "Employee Code configuration saved.");
    }

    private static Result<EmployeeCodeConfigurationDto> InvalidField(string field, string message)
        => Result<EmployeeCodeConfigurationDto>.Invalid("Employee Code configuration cannot be saved safely.", [new ValidationError(field, message)]);

    private static string Snapshot(EmployeeCodeConfigVersion version)
        => JsonSerializer.Serialize(new
        {
            version.AssignmentMode, version.GenerationMethod, version.Prefix, version.Separator,
            version.NextNumber, version.Padding, version.EffectiveFrom, version.EffectiveTo, version.IsActive
        });

    private static string FormatSimpleCode(string prefix, string separator, int padding, long nextNumber)
    {
        var number = nextNumber.ToString(padding == 0 ? string.Empty : $"D{padding}");
        return $"{prefix.Trim().ToUpperInvariant()}{separator}{number}";
    }

    public async Task<Result<IReadOnlyList<EmployeeCodeRuleDto>>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid) return Result<IReadOnlyList<EmployeeCodeRuleDto>>.Unauthorized("No authenticated tenant.");
        var rules = await _db.EmployeeCodeRules.AsNoTracking().Where(r => r.TenantId == _tenant.TenantId && !r.IsDeleted).Include(r => r.Conditions).Include(r => r.Segments).OrderBy(r => r.Priority).ToListAsync(cancellationToken);
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
        if (request.Segments.Any(s => s.SegmentType == EmployeeCodeSegmentType.LocationCode))
            return Result<EmployeeCodeRuleDto>.Invalid("segment", "Location code segments are unavailable because this model has no separate Location master.");
        if (request.Segments.Any(s => s.SegmentType == EmployeeCodeSegmentType.SequentialNumber && (s.PaddingLength is < 0 or > 12)))
            return Result<EmployeeCodeRuleDto>.Invalid("Sequence padding must be between 0 and 12.");
        var config = await _db.EmployeeCodeConfigs.FirstOrDefaultAsync(cancellationToken);
        if (config is null) return Result<EmployeeCodeRuleDto>.Invalid("Create Employee Code configuration before adding rules.");
        var version = request.ConfigurationVersionId is Guid requestedVersionId
            ? await _db.EmployeeCodeConfigVersions.FirstOrDefaultAsync(v => v.Id == requestedVersionId && v.EmployeeCodeConfigId == config.Id, cancellationToken)
            : await _db.EmployeeCodeConfigVersions
                .Where(v => v.EmployeeCodeConfigId == config.Id && v.IsActive)
                .OrderByDescending(v => v.EffectiveFrom)
                .FirstOrDefaultAsync(cancellationToken);
        if (version is null) return Result<EmployeeCodeRuleDto>.NotFound("The selected Employee Code configuration version was not found.");
        if (request.Status == EmployeeCodeRuleStatus.Active && !version.IsActive)
            return Result<EmployeeCodeRuleDto>.Conflict("An active rule cannot be attached to an inactive Employee Code configuration version.");
        if (request.IsDefault && request.Status == EmployeeCodeRuleStatus.Active)
        {
            var existingDefault = await _db.EmployeeCodeRules.AnyAsync(r => r.Id != id && r.EmployeeCodeConfigVersionId == version.Id && !r.IsDeleted && r.IsDefault && r.Status == EmployeeCodeRuleStatus.Active, cancellationToken);
            if (existingDefault) return Result<EmployeeCodeRuleDto>.Conflict("Only one active default Employee Code rule is allowed.");
        }
        var rule = id.HasValue ? await _db.EmployeeCodeRules.Where(r => r.TenantId == tenantId && !r.IsDeleted).Include(r => r.Conditions).Include(r => r.Segments).FirstOrDefaultAsync(r => r.Id == id.Value, cancellationToken) : null;
        if (rule is null)
        {
            rule = new EmployeeCodeRule { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeCodeConfigId = config.Id, EmployeeCodeConfigVersionId = version.Id };
            _db.EmployeeCodeRules.Add(rule);
        }
        else if (rule.EmployeeCodeConfigVersionId != version.Id)
        {
            // A version is historical. Saving an existing rule while another version is selected creates a
            // version-local copy instead of moving or mutating the historical rule.
            var sourceConditions = rule.Conditions.ToList();
            var sourceSegments = rule.Segments.ToList();
            rule = new EmployeeCodeRule
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeCodeConfigId = config.Id,
                EmployeeCodeConfigVersionId = version.Id,
                Conditions = sourceConditions.Select(c => new EmployeeCodeRuleCondition
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, Field = c.Field, Operator = c.Operator,
                    ReferenceId = c.ReferenceId, Value = c.Value
                }).ToList(),
                Segments = sourceSegments.Select(s => new EmployeeCodeSegment
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, SequenceOrder = s.SequenceOrder,
                    SegmentType = s.SegmentType, FixedValue = s.FixedValue, PaddingLength = s.PaddingLength
                }).ToList()
            };
            _db.EmployeeCodeRules.Add(rule);
            _db.EmployeeCodeRuleConditions.AddRange(rule.Conditions);
            _db.EmployeeCodeSegments.AddRange(rule.Segments);
        }
        rule.Name = request.Name.Trim(); rule.Priority = request.Priority; rule.IsDefault = request.IsDefault; rule.Status = request.Status;
        var conditionEntities = new List<EmployeeCodeRuleCondition>();
        var requestedConditionIds = request.Conditions.Where(c => c.Id.HasValue).Select(c => c.Id!.Value).ToHashSet();
        _db.EmployeeCodeRuleConditions.RemoveRange(rule.Conditions.Where(c => !requestedConditionIds.Contains(c.Id)).ToList());
        foreach (var c in request.Conditions)
        {
            if (c.Field == EmployeeCodeConditionField.Location)
            {
                return Result<EmployeeCodeRuleDto>.Invalid("condition", "Location conditions are unavailable because this model has no separate Location master.");
            }

            var code = await ResolveConditionCodeAsync(c, tenantId, cancellationToken);
            if (c.ReferenceId.HasValue && code is null)
            {
                return Result<EmployeeCodeRuleDto>.Invalid("condition", "The selected master value is invalid or belongs to another tenant.");
            }
            if (c.ReferenceId is Guid referenceId && !await IsValidHierarchyAsync(c.Field, referenceId, request.Conditions, tenantId, cancellationToken))
            {
                return Result<EmployeeCodeRuleDto>.Invalid("condition", "The selected master value does not belong to the selected parent hierarchy.");
            }
            if (request.Status == EmployeeCodeRuleStatus.Active && string.IsNullOrWhiteSpace(code))
                return Result<EmployeeCodeRuleDto>.Invalid("condition", "Active rule conditions must select a master value or provide a value.");
            var condition = c.Id.HasValue ? rule.Conditions.FirstOrDefault(existing => existing.Id == c.Id.Value) : null;
            if (condition is null) { condition = new EmployeeCodeRuleCondition { Id = c.Id ?? Guid.NewGuid(), TenantId = tenantId, EmployeeCodeRuleId = rule.Id }; _db.EmployeeCodeRuleConditions.Add(condition); }
            condition.Field = c.Field; condition.Operator = c.Operator; condition.ReferenceId = c.ReferenceId; condition.Value = code;
            conditionEntities.Add(condition);
        }
        rule.Conditions = conditionEntities;
        var requestedSegmentIds = request.Segments.Where(s => s.Id.HasValue).Select(s => s.Id!.Value).ToHashSet();
        _db.EmployeeCodeSegments.RemoveRange(rule.Segments.Where(s => !requestedSegmentIds.Contains(s.Id)).ToList());
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
            if (!anotherFallback) return Result<EmployeeCodeRuleDto>.Conflict("Cannot delete the only active default fallback for this configuration version. Use an explicit replacement action first; no rule was changed.");
        }
        rule.IsDeleted = true;
        rule.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result<EmployeeCodeRuleDto>.Success(MapRule(rule), "Employee Code rule deleted.");
    }

    public async Task<Result<EmployeeCodePreviewDto>> PreviewAsync(EmployeeCodePreviewRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenant.TenantId is not Guid tenantId)
            return Result<EmployeeCodePreviewDto>.Unauthorized("No authenticated tenant.");

        var versions = await _db.EmployeeCodeConfigVersions.AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.IsActive && v.EffectiveFrom <= request.EffectiveFrom &&
                        (v.EffectiveTo == null || request.EffectiveFrom <= v.EffectiveTo))
            .OrderBy(v => v.EffectiveFrom)
            .ToListAsync(cancellationToken);
        if (versions.Count == 0)
            return Result<EmployeeCodePreviewDto>.Invalid("effectiveFrom", "No active Employee Code configuration is effective for the selected date.");
        if (versions.Count > 1)
            return Result<EmployeeCodePreviewDto>.Conflict("Multiple active Employee Code configurations are effective for the selected date.");

        var version = versions[0];
        if (version.AssignmentMode != EmployeeCodeAssignmentMode.Auto || version.GenerationMethod != EmployeeCodeGenerationMethod.RuleBased)
            return Result<EmployeeCodePreviewDto>.Invalid("generationMethod", "Preview is available only for an active Auto Rule-Based configuration.");

        var ids = new Dictionary<EmployeeCodeConditionField, Guid?>
        {
            [EmployeeCodeConditionField.HoldingCompany] = request.HoldingCompanyId,
            [EmployeeCodeConditionField.Lob] = request.LobId,
            [EmployeeCodeConditionField.Organisation] = request.OrganisationId,
            [EmployeeCodeConditionField.Department] = request.DepartmentId,
            [EmployeeCodeConditionField.SubDepartment] = request.SubDepartmentId,
            [EmployeeCodeConditionField.Section] = request.SectionId,
            [EmployeeCodeConditionField.SubSection] = request.SubSectionId,
            [EmployeeCodeConditionField.Function] = request.FunctionId,
            [EmployeeCodeConditionField.SubFunction] = request.SubFunctionId,
            [EmployeeCodeConditionField.Grade] = request.GradeId,
            [EmployeeCodeConditionField.Designation] = request.DesignationId,
            [EmployeeCodeConditionField.EmployeeType] = request.EmployeeTypeId,
            [EmployeeCodeConditionField.Country] = request.CountryLocationId,
            [EmployeeCodeConditionField.WorkLocation] = request.WorkLocationId,
            [EmployeeCodeConditionField.CostCenter] = request.CostCenterId
        };
        var values = new Dictionary<EmployeeCodeConditionField, string?>();
        foreach (var (field, id) in ids)
        {
            if (id is Guid referenceId)
            {
                var code = await ResolvePreviewCodeAsync(field, referenceId, tenantId, cancellationToken);
                if (code is null)
                    return Result<EmployeeCodePreviewDto>.Invalid(field.ToString(), "The selected master value is invalid, inactive, or belongs to another tenant.");
                values[field] = code;
            }
        }

        var segmentValues = new Dictionary<EmployeeCodeSegmentType, string?>
        {
            [EmployeeCodeSegmentType.HoldingCompanyCode] = Code(values, EmployeeCodeConditionField.HoldingCompany),
            [EmployeeCodeSegmentType.LobCode] = Code(values, EmployeeCodeConditionField.Lob),
            [EmployeeCodeSegmentType.OrganisationCode] = Code(values, EmployeeCodeConditionField.Organisation),
            [EmployeeCodeSegmentType.DepartmentCode] = Code(values, EmployeeCodeConditionField.Department),
            [EmployeeCodeSegmentType.SubDepartmentCode] = Code(values, EmployeeCodeConditionField.SubDepartment),
            [EmployeeCodeSegmentType.SectionCode] = Code(values, EmployeeCodeConditionField.Section),
            [EmployeeCodeSegmentType.SubSectionCode] = Code(values, EmployeeCodeConditionField.SubSection),
            [EmployeeCodeSegmentType.FunctionCode] = Code(values, EmployeeCodeConditionField.Function),
            [EmployeeCodeSegmentType.SubFunctionCode] = Code(values, EmployeeCodeConditionField.SubFunction),
            [EmployeeCodeSegmentType.GradeCode] = Code(values, EmployeeCodeConditionField.Grade),
            [EmployeeCodeSegmentType.DesignationCode] = Code(values, EmployeeCodeConditionField.Designation),
            [EmployeeCodeSegmentType.EmployeeTypeCode] = Code(values, EmployeeCodeConditionField.EmployeeType),
            [EmployeeCodeSegmentType.CountryCode] = Code(values, EmployeeCodeConditionField.Country),
            [EmployeeCodeSegmentType.WorkLocationCode] = Code(values, EmployeeCodeConditionField.WorkLocation),
            [EmployeeCodeSegmentType.CostCenterCode] = Code(values, EmployeeCodeConditionField.CostCenter)
        };
        var context = new EmployeeCodeGenerationContext(request.EffectiveFrom, values, segmentValues);
        var rules = await _db.EmployeeCodeRules.AsNoTracking()
            .Include(r => r.Conditions).Include(r => r.Segments)
            .Where(r => r.EmployeeCodeConfigVersionId == version.Id && !r.IsDeleted && r.Status == EmployeeCodeRuleStatus.Active)
            .ToListAsync(cancellationToken);
        var rule = new EmployeeCodeRuleMatcher().Match(rules, values, ids);
        if (rule is null)
            return Result<EmployeeCodePreviewDto>.Invalid("rule", "No active rule matches the supplied sample employment values.");

        var sequence = await _db.EmployeeCodeSequences.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeCodeRuleId == rule.Id
                        && s.Scope == EmployeeCodeSequenceScope.Tenant
                        && s.ScopeKey == $"RULE:{rule.Id}"
                        && s.PeriodKey == "NONE")
            .Select(s => (long?)s.NextNumber)
            .SingleOrDefaultAsync(cancellationToken) ?? 1;
        var rendered = new EmployeeCodeRenderer().Render(rule, context, sequence, version.Separator);
        if (rendered.Error is not null)
            return Result<EmployeeCodePreviewDto>.Invalid("rule", rendered.Error);

        return Result<EmployeeCodePreviewDto>.Success(
            new EmployeeCodePreviewDto(version.Id, version.EffectiveFrom, version.EffectiveTo, rule.Id, rule.Name, sequence, false, rendered.Code!),
            "Preview generated. The sequence number is unreserved; a later employee save may receive a different number.");
    }

    private static EmployeeCodeConfigurationDto Map(EmployeeCodeConfig config, EmployeeCodeConfigVersion? version) =>
        version is null
            ? new(config.Id, config.AutoGenerate, config.AssignmentMode, config.GenerationMethod, config.Prefix, config.NextNumber, config.Padding, config.Separator, config.EffectiveFrom, config.EffectiveTo)
            : new(config.Id, version.AutoGenerate, version.AssignmentMode, version.GenerationMethod, version.Prefix, version.NextNumber, version.Padding, version.Separator, version.EffectiveFrom, version.EffectiveTo, version.Id, version.IsActive);
    private static EmployeeCodeRuleDto MapRule(EmployeeCodeRule rule) => new(rule.Id, rule.Name, rule.Priority, rule.IsDefault, rule.Status, rule.Conditions.OrderBy(c => c.Id).Select(c => new EmployeeCodeConditionDto(c.Id, c.Field, c.Operator, c.ReferenceId, c.Value)).ToList(), rule.Segments.OrderBy(s => s.SequenceOrder).Select(s => new EmployeeCodeSegmentDto(s.Id, s.SequenceOrder, s.SegmentType, s.FixedValue, s.PaddingLength)).ToList(), rule.EmployeeCodeConfigVersionId);

    private async Task<string?> ResolvePreviewCodeAsync(EmployeeCodeConditionField field, Guid id, Guid tenantId, CancellationToken cancellationToken) =>
        field switch
        {
            EmployeeCodeConditionField.HoldingCompany => await _db.HoldingCompanies.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Lob => await _db.LinesOfBusiness.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Organisation => await _db.Organisations.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Department => await _db.Departments.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.SubDepartment => await _db.SubDepartments.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Section => await _db.Sections.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.SubSection => await _db.SubSections.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Function => await _db.Functions.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.SubFunction => await _db.SubFunctions.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Grade => await _db.Grades.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Designation => await _db.Designations.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.EmployeeType => await _db.EmployeeTypes.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Country => await _db.Countries.Where(x => x.Id == id && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.WorkLocation => await _db.WorkLocations.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.CostCenter => await _db.CostCenters.Where(x => x.Id == id && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            _ => null
        };

    private static string? Code(IReadOnlyDictionary<EmployeeCodeConditionField, string?> values, EmployeeCodeConditionField field) => values.GetValueOrDefault(field);

    private async Task<string?> ResolveConditionCodeAsync(
        EmployeeCodeConditionRequest condition,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (condition.ReferenceId is not Guid referenceId)
            return condition.Value?.Trim();

        return condition.Field switch
        {
            EmployeeCodeConditionField.HoldingCompany => await _db.HoldingCompanies.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Lob => await _db.LinesOfBusiness.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Organisation => await _db.Organisations.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Department => await _db.Departments.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.SubDepartment => await _db.SubDepartments.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Section => await _db.Sections.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.SubSection => await _db.SubSections.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Function => await _db.Functions.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.SubFunction => await _db.SubFunctions.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Grade => await _db.Grades.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Designation => await _db.Designations.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.EmployeeType => await _db.EmployeeTypes.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.Country => await _db.Countries.Where(x => x.Id == referenceId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.WorkLocation => await _db.WorkLocations.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            EmployeeCodeConditionField.CostCenter => await _db.CostCenters.Where(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive).Select(x => x.Code).SingleOrDefaultAsync(cancellationToken),
            _ => null
        };
    }

    private async Task<bool> IsValidHierarchyAsync(
        EmployeeCodeConditionField field,
        Guid referenceId,
        IReadOnlyCollection<EmployeeCodeConditionRequest> conditions,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        Guid? Parent(EmployeeCodeConditionField parentField) =>
            conditions.FirstOrDefault(c => c.Field == parentField)?.ReferenceId;

        var holdingCompanyId = Parent(EmployeeCodeConditionField.HoldingCompany);
        var departmentId = Parent(EmployeeCodeConditionField.Department);
        var subDepartmentId = Parent(EmployeeCodeConditionField.SubDepartment);
        var sectionId = Parent(EmployeeCodeConditionField.Section);
        var functionId = Parent(EmployeeCodeConditionField.Function);

        return field switch
        {
            EmployeeCodeConditionField.Lob => await _db.LinesOfBusiness.AnyAsync(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive && (!holdingCompanyId.HasValue || x.HoldingCompanyId == holdingCompanyId), cancellationToken),
            EmployeeCodeConditionField.SubDepartment => await _db.SubDepartments.AnyAsync(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive && (!departmentId.HasValue || x.DepartmentId == departmentId), cancellationToken),
            EmployeeCodeConditionField.Section => await _db.Sections.AnyAsync(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive && (!subDepartmentId.HasValue || x.SubDepartmentId == subDepartmentId), cancellationToken),
            EmployeeCodeConditionField.SubSection => await _db.SubSections.AnyAsync(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive && (!sectionId.HasValue || x.SectionId == sectionId), cancellationToken),
            EmployeeCodeConditionField.SubFunction => await _db.SubFunctions.AnyAsync(x => x.Id == referenceId && x.TenantId == tenantId && x.IsActive && (!functionId.HasValue || x.FunctionId == functionId), cancellationToken),
            _ => true
        };
    }
}
