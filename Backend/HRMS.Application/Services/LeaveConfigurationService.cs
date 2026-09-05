using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Entities;
using HRMS.Domain.Common;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>Tenant-scoped administration service for the Phase 4C.1 Leave configuration surface.</summary>
public sealed class LeaveConfigurationService : ILeaveConfigurationService
{
    private const string NoTenant = "No authenticated tenant.";
    private const string NotFound = "The requested Leave configuration was not found.";
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;

    public LeaveConfigurationService(IHrmsDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Result<PagedResult<LeaveTypeDto>>> GetLeaveTypesAsync(LeaveTypeQuery query, CancellationToken ct = default)
    {
        if (!HasTenant()) return Result<PagedResult<LeaveTypeDto>>.Unauthorized(NoTenant);
        var source = _db.LeaveTypes.AsNoTracking();
        if (query.IsActive is bool active) source = source.Where(x => x.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            source = source.Where(x => x.Code.ToLower().Contains(search) || x.Name.ToLower().Contains(search));
        }
        var items = await source.OrderBy(x => x.Code).ThenBy(x => x.Id).ToListAsync(ct);
        var page = Page(items.Select(ToDto), query);
        return Result<PagedResult<LeaveTypeDto>>.Success(page);
    }

    public async Task<Result<LeaveTypeDto>> GetLeaveTypeAsync(Guid id, CancellationToken ct = default)
    {
        if (!HasTenant()) return Result<LeaveTypeDto>.Unauthorized(NoTenant);
        var entity = await _db.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        var item = entity is null ? null : ToDto(entity);
        return item is null ? Result<LeaveTypeDto>.NotFound(NotFound) : Result<LeaveTypeDto>.Success(item);
    }

    public async Task<Result<LeaveTypeDto>> CreateLeaveTypeAsync(LeaveTypeRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<LeaveTypeDto>.Unauthorized(NoTenant);
        var code = request.Code.Trim();
        if (await _db.LeaveTypes.AnyAsync(x => x.Code.ToLower() == code.ToLower(), ct)) return Duplicate<LeaveTypeDto>("code", $"LeaveType code '{code}' already exists.");
        var item = new LeaveType { Id = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = request.Name.Trim(), Description = Normalize(request.Description), DefaultUnit = request.DefaultUnit, IsPaid = request.IsPaid, IsActive = request.IsActive };
        _db.LeaveTypes.Add(item);
        try { await _db.SaveChangesAsync(ct); } catch (DbUpdateException) { return Duplicate<LeaveTypeDto>("code", $"LeaveType code '{code}' already exists."); }
        return Result<LeaveTypeDto>.Success(ToDto(item), "LeaveType created.");
    }

    public async Task<Result<LeaveTypeDto>> UpdateLeaveTypeAsync(Guid id, LeaveTypeRequest request, CancellationToken ct = default)
    {
        if (!HasTenant()) return Result<LeaveTypeDto>.Unauthorized(NoTenant);
        var item = await _db.LeaveTypes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Result<LeaveTypeDto>.NotFound(NotFound);
        if (!TokenMatches(item, request.ConcurrencyToken)) return Conflict<LeaveTypeDto>("Configuration changed by another user. Reload before saving.");
        var code = request.Code.Trim();
        if (!string.Equals(code, item.Code, StringComparison.OrdinalIgnoreCase) && await _db.LeavePolicyRules.AnyAsync(x => x.LeaveTypeId == id && x.LeavePolicyVersion!.Status == LeavePolicyVersionStatus.Published, ct))
            return Result<LeaveTypeDto>.Invalid("LeaveType code is immutable after published historical use.", [new("code", "LeaveType code cannot be changed after a Published policy references it.")]);
        if (await _db.LeaveTypes.AnyAsync(x => x.Id != id && x.Code.ToLower() == code.ToLower(), ct)) return Duplicate<LeaveTypeDto>("code", $"LeaveType code '{code}' already exists.");
        item.Code = code; item.Name = request.Name.Trim(); item.Description = Normalize(request.Description); item.DefaultUnit = request.DefaultUnit; item.IsPaid = request.IsPaid; item.IsActive = request.IsActive; item.ModifiedDate = DateTime.UtcNow;
        try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict<LeaveTypeDto>("Configuration changed by another user. Reload before saving."); } catch (DbUpdateException) { return Duplicate<LeaveTypeDto>("code", $"LeaveType code '{code}' already exists."); }
        return Result<LeaveTypeDto>.Success(ToDto(item), "LeaveType updated.");
    }

    public async Task<Result<PagedResult<LeavePeriodDto>>> GetLeavePeriodsAsync(LeavePeriodQuery query, CancellationToken ct = default)
    {
        if (!HasTenant()) return Result<PagedResult<LeavePeriodDto>>.Unauthorized(NoTenant);
        var source = _db.LeavePeriods.AsNoTracking();
        if (query.IsActive is bool active) source = source.Where(x => x.IsActive == active);
        if (query.OnDate is DateOnly date) source = source.Where(x => x.StartDate <= date && date <= x.EndDate);
        if (!string.IsNullOrWhiteSpace(query.Search)) { var search = query.Search.Trim().ToLowerInvariant(); source = source.Where(x => x.Code.ToLower().Contains(search) || x.Name.ToLower().Contains(search)); }
        var items = await source.OrderBy(x => x.StartDate).ThenBy(x => x.Code).ToListAsync(ct);
        var page = Page(items.Select(ToDto), query);
        return Result<PagedResult<LeavePeriodDto>>.Success(page);
    }

    public async Task<Result<LeavePeriodDto>> GetLeavePeriodAsync(Guid id, CancellationToken ct = default)
    {
        if (!HasTenant()) return Result<LeavePeriodDto>.Unauthorized(NoTenant);
        var entity = await _db.LeavePeriods.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        var item = entity is null ? null : ToDto(entity);
        return item is null ? Result<LeavePeriodDto>.NotFound(NotFound) : Result<LeavePeriodDto>.Success(item);
    }

    public async Task<Result<LeavePeriodDto>> CreateLeavePeriodAsync(LeavePeriodRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<LeavePeriodDto>.Unauthorized(NoTenant);
        var shape = ValidatePeriod(request.StartDate, request.EndDate); if (shape is not null) return Result<LeavePeriodDto>.Invalid("dates", shape);
        if (await _db.LeavePeriods.AnyAsync(x => x.Code.ToLower() == request.Code.Trim().ToLower(), ct)) return Duplicate<LeavePeriodDto>("code", "LeavePeriod code already exists.");
        if (request.IsActive && await HasPeriodOverlapAsync(request.StartDate, request.EndDate, null, ct)) return Result<LeavePeriodDto>.Conflict("The active LeavePeriod overlaps another active period in this tenant.");
        var item = new LeavePeriod { Id = Guid.NewGuid(), TenantId = tenantId, Code = request.Code.Trim(), Name = request.Name.Trim(), StartDate = request.StartDate, EndDate = request.EndDate, IsActive = request.IsActive };
        _db.LeavePeriods.Add(item); try { await _db.SaveChangesAsync(ct); } catch (DbUpdateException) { return Result<LeavePeriodDto>.Conflict("LeavePeriod code or date range conflicts with existing configuration."); }
        return Result<LeavePeriodDto>.Success(ToDto(item), "LeavePeriod created.");
    }

    public async Task<Result<LeavePeriodDto>> UpdateLeavePeriodAsync(Guid id, LeavePeriodRequest request, CancellationToken ct = default)
    {
        if (!HasTenant()) return Result<LeavePeriodDto>.Unauthorized(NoTenant);
        var item = await _db.LeavePeriods.FirstOrDefaultAsync(x => x.Id == id, ct); if (item is null) return Result<LeavePeriodDto>.NotFound(NotFound);
        if (!TokenMatches(item, request.ConcurrencyToken)) return Conflict<LeavePeriodDto>("Configuration changed by another user. Reload before saving.");
        var shape = ValidatePeriod(request.StartDate, request.EndDate); if (shape is not null) return Result<LeavePeriodDto>.Invalid("dates", shape);
        if (await _db.LeavePeriods.AnyAsync(x => x.Id != id && x.Code.ToLower() == request.Code.Trim().ToLower(), ct)) return Duplicate<LeavePeriodDto>("code", "LeavePeriod code already exists.");
        if (request.IsActive && await HasPeriodOverlapAsync(request.StartDate, request.EndDate, id, ct)) return Result<LeavePeriodDto>.Conflict("The active LeavePeriod overlaps another active period in this tenant.");
        item.Code = request.Code.Trim(); item.Name = request.Name.Trim(); item.StartDate = request.StartDate; item.EndDate = request.EndDate; item.IsActive = request.IsActive; item.ModifiedDate = DateTime.UtcNow;
        try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict<LeavePeriodDto>("Configuration changed by another user. Reload before saving."); } catch (DbUpdateException) { return Result<LeavePeriodDto>.Conflict("LeavePeriod code conflicts with existing configuration."); }
        return Result<LeavePeriodDto>.Success(ToDto(item), "LeavePeriod updated.");
    }

    public async Task<Result<PagedResult<LeavePolicyDto>>> GetPoliciesAsync(LeavePolicyQuery query, CancellationToken ct = default)
    {
        if (!HasTenant()) return Result<PagedResult<LeavePolicyDto>>.Unauthorized(NoTenant);
        var source = _db.LeavePolicies.AsNoTracking(); if (query.IsActive is bool active) source = source.Where(x => x.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search)) { var search = query.Search.Trim().ToLowerInvariant(); source = source.Where(x => x.Code.ToLower().Contains(search) || x.Name.ToLower().Contains(search)); }
        var items = await source.OrderBy(x => x.Code).ThenBy(x => x.Id).ToListAsync(ct);
        return Result<PagedResult<LeavePolicyDto>>.Success(Page(items.Select(ToDto), query));
    }

    public async Task<Result<LeavePolicyDto>> GetPolicyAsync(Guid id, CancellationToken ct = default)
    {
        if (!HasTenant()) return Result<LeavePolicyDto>.Unauthorized(NoTenant);
        var item = await _db.LeavePolicies.AsNoTracking().Include(x => x.Versions).FirstOrDefaultAsync(x => x.Id == id, ct);
        return item is null ? Result<LeavePolicyDto>.NotFound(NotFound) : Result<LeavePolicyDto>.Success(ToDto(item));
    }

    public async Task<Result<LeavePolicyDto>> CreatePolicyAsync(LeavePolicyRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<LeavePolicyDto>.Unauthorized(NoTenant);
        var code = request.Code.Trim(); if (await _db.LeavePolicies.AnyAsync(x => x.Code.ToLower() == code.ToLower(), ct)) return Duplicate<LeavePolicyDto>("code", "LeavePolicy code already exists.");
        var item = new LeavePolicy { Id = Guid.NewGuid(), TenantId = tenantId, Code = code, Name = request.Name.Trim(), Description = Normalize(request.Description), IsActive = request.IsActive };
        _db.LeavePolicies.Add(item); try { await _db.SaveChangesAsync(ct); } catch (DbUpdateException) { return Duplicate<LeavePolicyDto>("code", "LeavePolicy code already exists."); }
        return Result<LeavePolicyDto>.Success(ToDto(item), "LeavePolicy created.");
    }

    public async Task<Result<LeavePolicyDto>> UpdatePolicyAsync(Guid id, LeavePolicyRequest request, CancellationToken ct = default)
    {
        if (!HasTenant()) return Result<LeavePolicyDto>.Unauthorized(NoTenant);
        var item = await _db.LeavePolicies.Include(x => x.Versions).FirstOrDefaultAsync(x => x.Id == id, ct); if (item is null) return Result<LeavePolicyDto>.NotFound(NotFound);
        if (!TokenMatches(item, request.ConcurrencyToken)) return Conflict<LeavePolicyDto>("Configuration changed by another user. Reload before saving.");
        var code = request.Code.Trim();
        if (!string.Equals(code, item.Code, StringComparison.OrdinalIgnoreCase) && item.Versions.Any(x => x.Status == LeavePolicyVersionStatus.Published)) return Result<LeavePolicyDto>.Invalid("LeavePolicy code is immutable after published historical use.", [new("code", "LeavePolicy code cannot be changed after publication.")]);
        if (await _db.LeavePolicies.AnyAsync(x => x.Id != id && x.Code.ToLower() == code.ToLower(), ct)) return Duplicate<LeavePolicyDto>("code", "LeavePolicy code already exists.");
        item.Code = code; item.Name = request.Name.Trim(); item.Description = Normalize(request.Description); item.IsActive = request.IsActive; item.ModifiedDate = DateTime.UtcNow;
        try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict<LeavePolicyDto>("Configuration changed by another user. Reload before saving."); } catch (DbUpdateException) { return Duplicate<LeavePolicyDto>("code", "LeavePolicy code already exists."); }
        return Result<LeavePolicyDto>.Success(ToDto(item), "LeavePolicy updated.");
    }

    public async Task<Result<PagedResult<LeavePolicyVersionDto>>> GetVersionsAsync(Guid policyId, CancellationToken ct = default)
    {
        var policy = await PolicyAsync(policyId, ct); if (policy is null) return Result<PagedResult<LeavePolicyVersionDto>>.NotFound(NotFound);
        var items = await _db.LeavePolicyVersions.AsNoTracking().Where(x => x.LeavePolicyId == policyId).Include(x => x.Rules).Include(x => x.ApplicabilitySets).OrderByDescending(x => x.VersionNumber).ToListAsync(ct);
        return Result<PagedResult<LeavePolicyVersionDto>>.Success(Page(items.Select(ToDto), new LeavePolicyQuery()));
    }

    public async Task<Result<LeavePolicyVersionDto>> GetVersionAsync(Guid policyId, Guid versionId, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); return version is null ? Result<LeavePolicyVersionDto>.NotFound(NotFound) : Result<LeavePolicyVersionDto>.Success(ToDto(version));
    }

    public async Task<Result<LeavePolicyEditorDto>> GetEditorAsync(Guid policyId, Guid? versionId, CancellationToken ct = default)
    {
        var policy = await _db.LeavePolicies.Include(x => x.Versions).FirstOrDefaultAsync(x => x.Id == policyId, ct); if (policy is null) return Result<LeavePolicyEditorDto>.NotFound(NotFound);
        var version = versionId is Guid id ? await _db.LeavePolicyVersions.Include(x => x.Rules).Include(x => x.ApplicabilitySets).FirstOrDefaultAsync(x => x.Id == id && x.LeavePolicyId == policyId, ct) : policy.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        var typeRows = version is null ? [] : await _db.LeaveTypes.AsNoTracking().Where(x => version.Rules.Select(r => r.LeaveTypeId).Contains(x.Id)).OrderBy(x => x.Code).ToListAsync(ct);
        return Result<LeavePolicyEditorDto>.Success(new(ToDto(policy), version is null ? null : ToDto(version), typeRows.Select(x => new LeaveTypeSelectionDto(x.Id, x.Code, x.Name, x.IsActive)).ToList(), version?.ApplicabilitySets.Select(ToDto).ToList() ?? []));
    }

    public async Task<Result<LeavePolicyVersionDto>> CreateVersionAsync(Guid policyId, LeavePolicyVersionRequest request, CancellationToken ct = default)
    {
        var policy = await PolicyAsync(policyId, ct); if (policy is null) return Result<LeavePolicyVersionDto>.NotFound(NotFound); if (!policy.IsActive) return Result<LeavePolicyVersionDto>.Conflict("An inactive LeavePolicy cannot create a new version.");
        var max = await _db.LeavePolicyVersions.Where(x => x.LeavePolicyId == policyId).Select(x => (int?)x.VersionNumber).MaxAsync(ct) ?? 0;
        var version = new LeavePolicyVersion { Id = Guid.NewGuid(), TenantId = _tenant.TenantId!.Value, LeavePolicyId = policyId, VersionNumber = max + 1, EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, Priority = request.Priority, Status = LeavePolicyVersionStatus.Draft };
        if (request.CopyFromVersionId is Guid copyId)
        {
            var source = await _db.LeavePolicyVersions.Include(x => x.Rules).ThenInclude(x => x.EligibilityRule).Include(x => x.Rules).ThenInclude(x => x.EntitlementRule).Include(x => x.Rules).ThenInclude(x => x.RequestRule).Include(x => x.Rules).ThenInclude(x => x.CalendarRule).Include(x => x.Rules).ThenInclude(x => x.AttachmentRule).Include(x => x.Rules).ThenInclude(x => x.CancellationRule).Include(x => x.ApplicabilitySets).FirstOrDefaultAsync(x => x.Id == copyId && x.LeavePolicyId == policyId, ct); if (source is null) return Result<LeavePolicyVersionDto>.NotFound("Copy source version was not found.");
            version.Rules = source.Rules.Where(x => x.IsActive).Select(x =>
            {
                var cloned = new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyVersionId = version.Id, LeaveTypeId = x.LeaveTypeId, IsActive = true };
                if (x.EligibilityRule is not null)
                {
                    cloned.EligibilityRule = new LeavePolicyEligibilityRule
                    {
                        Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyRuleId = cloned.Id,
                        EligibilityMode = x.EligibilityRule.EligibilityMode, MinimumServiceValue = x.EligibilityRule.MinimumServiceValue,
                        MinimumServiceUnit = x.EligibilityRule.MinimumServiceUnit, ProbationMode = x.EligibilityRule.ProbationMode,
                        NoticePeriodMode = x.EligibilityRule.NoticePeriodMode
                    };
                }
                if (x.EntitlementRule is not null)
                {
                    cloned.EntitlementRule = new LeavePolicyEntitlementRule
                    {
                        Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyRuleId = cloned.Id,
                        EntitlementMode = x.EntitlementRule.EntitlementMode, EntitlementSource = x.EntitlementRule.EntitlementSource,
                        EntitlementQuantity = x.EntitlementRule.EntitlementQuantity, AccrualFrequency = x.EntitlementRule.AccrualFrequency,
                        AccrualTiming = x.EntitlementRule.AccrualTiming
                    };
                }
                if (x.RequestRule is not null)
                {
                    cloned.RequestRule = new LeavePolicyRequestRule
                    {
                        Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyRuleId = cloned.Id,
                        MinimumRequestQuantity = x.RequestRule.MinimumRequestQuantity, MaximumRequestQuantity = x.RequestRule.MaximumRequestQuantity,
                        MaximumConsecutiveQuantity = x.RequestRule.MaximumConsecutiveQuantity, MinimumAdvanceNoticeDays = x.RequestRule.MinimumAdvanceNoticeDays,
                        BackdatedRequestMode = x.RequestRule.BackdatedRequestMode, MaximumBackdatedDays = x.RequestRule.MaximumBackdatedDays,
                        MaximumRequestsPerPeriod = x.RequestRule.MaximumRequestsPerPeriod, MaximumQuantityPerPeriod = x.RequestRule.MaximumQuantityPerPeriod,
                        RequestLimitPeriod = x.RequestRule.RequestLimitPeriod, PartialDayMode = x.RequestRule.PartialDayMode
                    };
                }
                if (x.CalendarRule is not null) cloned.CalendarRule = new LeavePolicyCalendarRule { Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyRuleId = cloned.Id, HolidayTreatment = x.CalendarRule.HolidayTreatment, WeekOffTreatment = x.CalendarRule.WeekOffTreatment, SandwichMode = x.CalendarRule.SandwichMode, ApplyToPrefix = x.CalendarRule.ApplyToPrefix, ApplyToSuffix = x.CalendarRule.ApplyToSuffix, ApplyToBetween = x.CalendarRule.ApplyToBetween };
                if (x.AttachmentRule is not null) cloned.AttachmentRule = new LeavePolicyAttachmentRule { Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyRuleId = cloned.Id, AttachmentRequirement = x.AttachmentRule.AttachmentRequirement, ThresholdQuantity = x.AttachmentRule.ThresholdQuantity, DocumentLabel = x.AttachmentRule.DocumentLabel };
                if (x.CancellationRule is not null) cloned.CancellationRule = new LeavePolicyCancellationRule { Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyRuleId = cloned.Id, WithdrawAllowed = x.CancellationRule.WithdrawAllowed, CancelAllowed = x.CancellationRule.CancelAllowed, ModifyAllowed = x.CancellationRule.ModifyAllowed };
                return cloned;
            }).ToList();
            version.ApplicabilitySets = source.ApplicabilitySets.Select(x => CloneSet(x, version.TenantId, version.Id)).ToList();
        }
        _db.LeavePolicyVersions.Add(version); try { await _db.SaveChangesAsync(ct); } catch (DbUpdateException) { return Result<LeavePolicyVersionDto>.Conflict("The version number was allocated concurrently. Retry creating the Draft."); }
        return Result<LeavePolicyVersionDto>.Success(ToDto(version), "Draft version created.");
    }

    public async Task<Result<LeavePolicyVersionDto>> UpdateVersionAsync(Guid policyId, Guid versionId, LeavePolicyVersionUpdateRequest request, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); if (version is null) return Result<LeavePolicyVersionDto>.NotFound(NotFound); if (version.Status != LeavePolicyVersionStatus.Draft) return Result<LeavePolicyVersionDto>.Conflict("Only Draft versions can be edited.");
        if (!TokenMatches(version, request.ConcurrencyToken)) return Conflict<LeavePolicyVersionDto>("Configuration changed by another user. Reload before saving.");
        if (request.EffectiveTo is DateOnly to && request.EffectiveFrom > to) return Result<LeavePolicyVersionDto>.Invalid("effectiveTo", "EffectiveFrom must be on or before EffectiveTo.");
        version.EffectiveFrom = request.EffectiveFrom; version.EffectiveTo = request.EffectiveTo; version.Priority = request.Priority; version.ModifiedDate = DateTime.UtcNow;
        try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict<LeavePolicyVersionDto>("Configuration changed by another user. Reload before saving."); }
        return Result<LeavePolicyVersionDto>.Success(ToDto(version), "Draft version updated.");
    }

    public async Task<Result<IReadOnlyList<LeaveTypeSelectionDto>>> GetVersionLeaveTypesAsync(Guid policyId, Guid versionId, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); if (version is null) return Result<IReadOnlyList<LeaveTypeSelectionDto>>.NotFound(NotFound);
        var ids = version.Rules.Where(x => x.IsActive).Select(x => x.LeaveTypeId).ToList(); var rows = await _db.LeaveTypes.AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Code).ToListAsync(ct);
        return Result<IReadOnlyList<LeaveTypeSelectionDto>>.Success(rows.Select(x => new LeaveTypeSelectionDto(x.Id, x.Code, x.Name, x.IsActive)).ToList());
    }

    public async Task<Result<IReadOnlyList<LeaveTypeSelectionDto>>> SetVersionLeaveTypesAsync(Guid policyId, Guid versionId, LeaveTypeSelectionRequest request, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); if (version is null) return Result<IReadOnlyList<LeaveTypeSelectionDto>>.NotFound(NotFound); if (version.Status != LeavePolicyVersionStatus.Draft) return Result<IReadOnlyList<LeaveTypeSelectionDto>>.Conflict("Published and Retired versions are immutable.");
        if (!TokenMatches(version, request.ConcurrencyToken)) return Conflict<IReadOnlyList<LeaveTypeSelectionDto>>("Configuration changed by another user. Reload before saving.");
        var ids = request.LeaveTypeIds.Distinct().ToList(); var types = await _db.LeaveTypes.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (types.Count != ids.Count || types.Any(x => !x.IsActive)) return Result<IReadOnlyList<LeaveTypeSelectionDto>>.Invalid("LeaveTypeIds", "Every selected LeaveType must be active and belong to this tenant.");
        var selected = ids.ToHashSet();
        _db.LeavePolicyRules.RemoveRange(version.Rules.Where(x => !selected.Contains(x.LeaveTypeId)));
        foreach (var type in types)
        {
            var existing = version.Rules.SingleOrDefault(x => x.LeaveTypeId == type.Id);
            if (existing is not null) existing.IsActive = true;
            else _db.LeavePolicyRules.Add(new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = _tenant.TenantId!.Value, LeavePolicyVersionId = version.Id, LeaveTypeId = type.Id, IsActive = true });
        }
        version.ModifiedDate = DateTime.UtcNow; await _db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<LeaveTypeSelectionDto>>.Success(types.OrderBy(x => x.Code).Select(x => new LeaveTypeSelectionDto(x.Id, x.Code, x.Name, x.IsActive)).ToList());
    }

    public async Task<Result<IReadOnlyList<LeaveApplicabilityGroupDto>>> GetApplicabilityAsync(Guid policyId, Guid versionId, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); if (version is null) return Result<IReadOnlyList<LeaveApplicabilityGroupDto>>.NotFound(NotFound);
        return Result<IReadOnlyList<LeaveApplicabilityGroupDto>>.Success(version.ApplicabilitySets.Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<LeaveApplicabilityGroupDto>>> SetApplicabilityAsync(Guid policyId, Guid versionId, LeaveApplicabilityRequest request, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); if (version is null) return Result<IReadOnlyList<LeaveApplicabilityGroupDto>>.NotFound(NotFound); if (version.Status != LeavePolicyVersionStatus.Draft) return Result<IReadOnlyList<LeaveApplicabilityGroupDto>>.Conflict("Published and Retired versions are immutable.");
        if (!TokenMatches(version, request.ConcurrencyToken)) return Conflict<IReadOnlyList<LeaveApplicabilityGroupDto>>("Configuration changed by another user. Reload before saving.");
        var errors = await ValidateGroupsAsync(request.Groups, ct); if (errors.Count != 0) return Result<IReadOnlyList<LeaveApplicabilityGroupDto>>.Invalid("Applicability groups are invalid.", errors);
        _db.LeavePolicyApplicabilitySets.RemoveRange(version.ApplicabilitySets); var groups = request.Groups.Select(x => ToEntity(x, version.Id)).ToList(); _db.LeavePolicyApplicabilitySets.AddRange(groups); version.ModifiedDate = DateTime.UtcNow; await _db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<LeaveApplicabilityGroupDto>>.Success(groups.Select(ToDto).ToList());
    }

    public async Task<Result<LeavePolicyValidationDto>> ValidateAsync(Guid policyId, Guid versionId, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); if (version is null) return Result<LeavePolicyValidationDto>.NotFound(NotFound);
        var errors = await ValidateForPublishAsync(version, ct); return Result<LeavePolicyValidationDto>.Success(new(errors.Count == 0, errors, []));
    }

    public async Task<Result<LeavePolicyEligibilityRuleDto?>> GetEligibilityAsync(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct);
        if (version is null) return Result<LeavePolicyEligibilityRuleDto?>.NotFound(NotFound);
        var rule = await _db.LeavePolicyRules.Include(x => x.EligibilityRule)
            .FirstOrDefaultAsync(x => x.LeavePolicyVersionId == version.Id && x.LeaveTypeId == leaveTypeId && x.IsActive, ct);
        return rule is null ? Result<LeavePolicyEligibilityRuleDto?>.NotFound("The selected LeaveType is not assigned to this policy version.")
            : Result<LeavePolicyEligibilityRuleDto?>.Success(rule.EligibilityRule is null ? null : ToDto(rule.EligibilityRule));
    }

    public async Task<Result<LeavePolicyEligibilityRuleDto?>> SaveEligibilityAsync(Guid policyId, Guid versionId, Guid leaveTypeId, LeavePolicyEligibilityRuleRequest request, CancellationToken ct = default)
    {
        var version = await _db.LeavePolicyVersions.Include(x => x.Rules).ThenInclude(x => x.EligibilityRule)
            .FirstOrDefaultAsync(x => x.Id == versionId && x.LeavePolicyId == policyId, ct);
        if (version is null) return Result<LeavePolicyEligibilityRuleDto?>.NotFound(NotFound);
        if (version.Status != LeavePolicyVersionStatus.Draft) return Result<LeavePolicyEligibilityRuleDto?>.Conflict("Published and Retired versions are immutable.");
        if (!TokenMatches(version, request.ConcurrencyToken)) return Conflict<LeavePolicyEligibilityRuleDto?>("Configuration changed by another user. Reload before saving.");
        var rule = version.Rules.SingleOrDefault(x => x.LeaveTypeId == leaveTypeId && x.IsActive);
        if (rule is null) return Result<LeavePolicyEligibilityRuleDto?>.NotFound("The selected LeaveType is not assigned to this policy version.");
        var errors = ValidateEligibility(request);
        if (errors.Count != 0) return Result<LeavePolicyEligibilityRuleDto?>.Invalid("Eligibility configuration is invalid.", errors);
        if (IsBaseline(request))
        {
            if (rule.EligibilityRule is not null) _db.LeavePolicyEligibilityRules.Remove(rule.EligibilityRule);
            version.ModifiedDate = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Result<LeavePolicyEligibilityRuleDto?>.Success(null, "Baseline eligibility saved.");
        }
        var entity = rule.EligibilityRule ?? new LeavePolicyEligibilityRule { Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyRuleId = rule.Id };
        entity.EligibilityMode = request.EligibilityMode;
        entity.MinimumServiceValue = request.EligibilityMode == EligibilityMode.MinimumService ? request.MinimumServiceValue : null;
        entity.MinimumServiceUnit = request.EligibilityMode == EligibilityMode.MinimumService ? request.MinimumServiceUnit : null;
        entity.ProbationMode = request.ProbationMode;
        entity.NoticePeriodMode = request.NoticePeriodMode;
        if (rule.EligibilityRule is null) _db.LeavePolicyEligibilityRules.Add(entity);
        version.ModifiedDate = DateTime.UtcNow;
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict<LeavePolicyEligibilityRuleDto?>("Configuration changed by another user. Reload before saving."); }
        return Result<LeavePolicyEligibilityRuleDto?>.Success(ToDto(entity), "Eligibility saved.");
    }

    public async Task<Result<LeavePolicyEntitlementRuleDto?>> GetEntitlementAsync(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct);
        if (version is null) return Result<LeavePolicyEntitlementRuleDto?>.NotFound(NotFound);
        var rule = await _db.LeavePolicyRules.Include(x => x.EntitlementRule)
            .FirstOrDefaultAsync(x => x.LeavePolicyVersionId == version.Id && x.LeaveTypeId == leaveTypeId && x.IsActive, ct);
        return rule is null ? Result<LeavePolicyEntitlementRuleDto?>.NotFound("The selected LeaveType is not assigned to this policy version.")
            : Result<LeavePolicyEntitlementRuleDto?>.Success(rule.EntitlementRule is null ? null : ToDto(rule.EntitlementRule));
    }

    public async Task<Result<LeavePolicyEntitlementRuleDto?>> SaveEntitlementAsync(Guid policyId, Guid versionId, Guid leaveTypeId, LeavePolicyEntitlementRuleRequest request, CancellationToken ct = default)
    {
        var version = await _db.LeavePolicyVersions.Include(x => x.Rules).ThenInclude(x => x.EntitlementRule)
            .FirstOrDefaultAsync(x => x.Id == versionId && x.LeavePolicyId == policyId, ct);
        if (version is null) return Result<LeavePolicyEntitlementRuleDto?>.NotFound(NotFound);
        if (version.Status != LeavePolicyVersionStatus.Draft) return Result<LeavePolicyEntitlementRuleDto?>.Conflict("Published and Retired versions are immutable.");
        if (!TokenMatches(version, request.ConcurrencyToken)) return Conflict<LeavePolicyEntitlementRuleDto?>("Configuration changed by another user. Reload before saving.");
        var rule = version.Rules.SingleOrDefault(x => x.LeaveTypeId == leaveTypeId && x.IsActive);
        if (rule is null) return Result<LeavePolicyEntitlementRuleDto?>.NotFound("The selected LeaveType is not assigned to this policy version.");
        var errors = ValidateEntitlement(request);
        if (errors.Count != 0) return Result<LeavePolicyEntitlementRuleDto?>.Invalid("Entitlement configuration is invalid.", errors);
        var entity = rule.EntitlementRule ?? new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyRuleId = rule.Id };
        entity.EntitlementMode = request.EntitlementMode;
        entity.EntitlementSource = request.EntitlementSource;
        entity.EntitlementQuantity = request.EntitlementMode == EntitlementMode.Allocated ? request.EntitlementQuantity : null;
        entity.AccrualFrequency = request.AccrualFrequency;
        entity.AccrualTiming = request.AccrualFrequency == AccrualFrequency.None ? null : request.AccrualTiming;
        if (rule.EntitlementRule is null) _db.LeavePolicyEntitlementRules.Add(entity);
        version.ModifiedDate = DateTime.UtcNow;
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict<LeavePolicyEntitlementRuleDto?>("Configuration changed by another user. Reload before saving."); }
        return Result<LeavePolicyEntitlementRuleDto?>.Success(ToDto(entity), "Entitlement saved.");
    }

    public async Task<Result<LeavePolicyRequestRuleDto?>> GetRequestRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); if (version is null) return Result<LeavePolicyRequestRuleDto?>.NotFound(NotFound);
        var rule = await _db.LeavePolicyRules.Include(x => x.RequestRule).FirstOrDefaultAsync(x => x.LeavePolicyVersionId == version.Id && x.LeaveTypeId == leaveTypeId && x.IsActive, ct);
        return rule is null ? Result<LeavePolicyRequestRuleDto?>.NotFound("The selected LeaveType is not assigned to this policy version.") : Result<LeavePolicyRequestRuleDto?>.Success(rule.RequestRule is null ? null : ToDto(rule.RequestRule));
    }

    public async Task<Result<LeavePolicyRequestRuleDto?>> SaveRequestRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, LeavePolicyRequestRuleRequest request, CancellationToken ct = default)
    {
        var version = await _db.LeavePolicyVersions.Include(x => x.Rules).ThenInclude(x => x.RequestRule).FirstOrDefaultAsync(x => x.Id == versionId && x.LeavePolicyId == policyId, ct);
        if (version is null) return Result<LeavePolicyRequestRuleDto?>.NotFound(NotFound);
        if (version.Status != LeavePolicyVersionStatus.Draft) return Result<LeavePolicyRequestRuleDto?>.Conflict("Published and Retired versions are immutable.");
        if (!TokenMatches(version, request.ConcurrencyToken)) return Conflict<LeavePolicyRequestRuleDto?>("Configuration changed by another user. Reload before saving.");
        var rule = version.Rules.SingleOrDefault(x => x.LeaveTypeId == leaveTypeId && x.IsActive);
        if (rule is null) return Result<LeavePolicyRequestRuleDto?>.NotFound("The selected LeaveType is not assigned to this policy version.");
        var errors = ValidateRequestRule(request); if (errors.Count != 0) return Result<LeavePolicyRequestRuleDto?>.Invalid("Request Rule configuration is invalid.", errors);
        if (IsRequestBaseline(request))
        {
            if (rule.RequestRule is not null) _db.LeavePolicyRequestRules.Remove(rule.RequestRule);
            version.ModifiedDate = DateTime.UtcNow; await _db.SaveChangesAsync(ct); return Result<LeavePolicyRequestRuleDto?>.Success(null, "Baseline Request Rules saved.");
        }
        var entity = rule.RequestRule ?? new LeavePolicyRequestRule { Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyRuleId = rule.Id };
        entity.MinimumRequestQuantity = request.MinimumRequestQuantity; entity.MaximumRequestQuantity = request.MaximumRequestQuantity; entity.MaximumConsecutiveQuantity = request.MaximumConsecutiveQuantity;
        entity.MinimumAdvanceNoticeDays = request.MinimumAdvanceNoticeDays; entity.BackdatedRequestMode = request.BackdatedRequestMode; entity.MaximumBackdatedDays = request.BackdatedRequestMode == BackdatedRequestMode.AllowedUpToDays ? request.MaximumBackdatedDays : null;
        entity.MaximumRequestsPerPeriod = request.MaximumRequestsPerPeriod; entity.MaximumQuantityPerPeriod = request.MaximumQuantityPerPeriod; entity.RequestLimitPeriod = request.MaximumRequestsPerPeriod is not null || request.MaximumQuantityPerPeriod is not null ? request.RequestLimitPeriod : null;
        entity.PartialDayMode = request.PartialDayMode;
        if (rule.RequestRule is null) _db.LeavePolicyRequestRules.Add(entity); version.ModifiedDate = DateTime.UtcNow;
        try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict<LeavePolicyRequestRuleDto?>("Configuration changed by another user. Reload before saving."); }
        return Result<LeavePolicyRequestRuleDto?>.Success(ToDto(entity), "Request Rules saved.");
    }

    public async Task<Result<LeavePolicyCalendarRuleDto?>> GetCalendarRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); if (version is null) return Result<LeavePolicyCalendarRuleDto?>.NotFound(NotFound);
        var rule = await _db.LeavePolicyRules.Include(x => x.CalendarRule).FirstOrDefaultAsync(x => x.LeavePolicyVersionId == version.Id && x.LeaveTypeId == leaveTypeId && x.IsActive, ct);
        return rule is null ? Result<LeavePolicyCalendarRuleDto?>.NotFound("The selected LeaveType is not assigned to this policy version.") : Result<LeavePolicyCalendarRuleDto?>.Success(rule.CalendarRule is null ? null : ToDto(rule.CalendarRule));
    }

    public async Task<Result<LeavePolicyCalendarRuleDto?>> SaveCalendarRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, LeavePolicyCalendarRuleRequest request, CancellationToken ct = default)
    {
        var version = await _db.LeavePolicyVersions.Include(x => x.Rules).ThenInclude(x => x.CalendarRule).FirstOrDefaultAsync(x => x.Id == versionId && x.LeavePolicyId == policyId, ct);
        if (version is null) return Result<LeavePolicyCalendarRuleDto?>.NotFound(NotFound);
        if (version.Status != LeavePolicyVersionStatus.Draft) return Result<LeavePolicyCalendarRuleDto?>.Conflict("Published and Retired versions are immutable.");
        if (!TokenMatches(version, request.ConcurrencyToken)) return Conflict<LeavePolicyCalendarRuleDto?>("Configuration changed by another user. Reload before saving.");
        var rule = version.Rules.SingleOrDefault(x => x.LeaveTypeId == leaveTypeId && x.IsActive); if (rule is null) return Result<LeavePolicyCalendarRuleDto?>.NotFound("The selected LeaveType is not assigned to this policy version.");
        var errors = ValidateCalendarRule(request); if (errors.Count != 0) return Result<LeavePolicyCalendarRuleDto?>.Invalid("Calendar Rule configuration is invalid.", errors);
        if (IsCalendarBaseline(request)) { if (rule.CalendarRule is not null) _db.LeavePolicyCalendarRules.Remove(rule.CalendarRule); version.ModifiedDate = DateTime.UtcNow; await _db.SaveChangesAsync(ct); return Result<LeavePolicyCalendarRuleDto?>.Success(null, "Baseline Calendar Rules saved."); }
        var entity = rule.CalendarRule ?? new LeavePolicyCalendarRule { Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyRuleId = rule.Id };
        entity.HolidayTreatment = request.HolidayTreatment; entity.WeekOffTreatment = request.WeekOffTreatment; entity.SandwichMode = request.SandwichMode; entity.ApplyToPrefix = request.SandwichMode != SandwichMode.Disabled && request.ApplyToPrefix; entity.ApplyToSuffix = request.SandwichMode != SandwichMode.Disabled && request.ApplyToSuffix; entity.ApplyToBetween = request.SandwichMode != SandwichMode.Disabled && request.ApplyToBetween;
        if (rule.CalendarRule is null) _db.LeavePolicyCalendarRules.Add(entity); version.ModifiedDate = DateTime.UtcNow; try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict<LeavePolicyCalendarRuleDto?>("Configuration changed by another user. Reload before saving."); }
        return Result<LeavePolicyCalendarRuleDto?>.Success(ToDto(entity), "Calendar Rules saved.");
    }

    public async Task<Result<LeavePolicyAttachmentRuleDto?>> GetAttachmentRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); if (version is null) return Result<LeavePolicyAttachmentRuleDto?>.NotFound(NotFound);
        var rule = await _db.LeavePolicyRules.Include(x => x.AttachmentRule).FirstOrDefaultAsync(x => x.LeavePolicyVersionId == version.Id && x.LeaveTypeId == leaveTypeId && x.IsActive, ct);
        return rule is null ? Result<LeavePolicyAttachmentRuleDto?>.NotFound("The selected LeaveType is not assigned to this policy version.") : Result<LeavePolicyAttachmentRuleDto?>.Success(rule.AttachmentRule is null ? null : ToDto(rule.AttachmentRule));
    }

    public async Task<Result<LeavePolicyAttachmentRuleDto?>> SaveAttachmentRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, LeavePolicyAttachmentRuleRequest request, CancellationToken ct = default)
    {
        var version = await _db.LeavePolicyVersions.Include(x => x.Rules).ThenInclude(x => x.AttachmentRule).FirstOrDefaultAsync(x => x.Id == versionId && x.LeavePolicyId == policyId, ct);
        if (version is null) return Result<LeavePolicyAttachmentRuleDto?>.NotFound(NotFound); if (version.Status != LeavePolicyVersionStatus.Draft) return Result<LeavePolicyAttachmentRuleDto?>.Conflict("Published and Retired versions are immutable."); if (!TokenMatches(version, request.ConcurrencyToken)) return Conflict<LeavePolicyAttachmentRuleDto?>("Configuration changed by another user. Reload before saving.");
        var rule = version.Rules.SingleOrDefault(x => x.LeaveTypeId == leaveTypeId && x.IsActive); if (rule is null) return Result<LeavePolicyAttachmentRuleDto?>.NotFound("The selected LeaveType is not assigned to this policy version.");
        var errors = ValidateAttachmentRule(request); if (errors.Count != 0) return Result<LeavePolicyAttachmentRuleDto?>.Invalid("Attachment configuration is invalid.", errors);
        if (IsAttachmentBaseline(request)) { if (rule.AttachmentRule is not null) _db.LeavePolicyAttachmentRules.Remove(rule.AttachmentRule); version.ModifiedDate = DateTime.UtcNow; await _db.SaveChangesAsync(ct); return Result<LeavePolicyAttachmentRuleDto?>.Success(null, "Baseline attachment rules saved."); }
        var entity = rule.AttachmentRule ?? new LeavePolicyAttachmentRule { Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyRuleId = rule.Id }; entity.AttachmentRequirement = request.AttachmentRequirement; entity.ThresholdQuantity = request.AttachmentRequirement == AttachmentRequirement.RequiredAboveQuantity ? request.ThresholdQuantity : null; entity.DocumentLabel = request.DocumentLabel;
        if (rule.AttachmentRule is null) _db.LeavePolicyAttachmentRules.Add(entity); version.ModifiedDate = DateTime.UtcNow; try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict<LeavePolicyAttachmentRuleDto?>("Configuration changed by another user. Reload before saving."); } return Result<LeavePolicyAttachmentRuleDto?>.Success(ToDto(entity), "Attachment rules saved.");
    }

    public async Task<Result<IReadOnlyList<LeavePolicyClubbingRuleDto>>> GetClubbingAsync(Guid policyId, Guid versionId, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); if (version is null) return Result<IReadOnlyList<LeavePolicyClubbingRuleDto>>.NotFound(NotFound);
        var rows = await _db.LeavePolicyClubbingRules.Where(x => x.LeavePolicyVersionId == version.Id).ToListAsync(ct);
        var rules = await _db.LeavePolicyRules.Where(x => x.LeavePolicyVersionId == version.Id).ToListAsync(ct); var types = await _db.LeaveTypes.Where(x => rules.Select(r => r.LeaveTypeId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return Result<IReadOnlyList<LeavePolicyClubbingRuleDto>>.Success(rows.Select(x => new LeavePolicyClubbingRuleDto(x.Id, x.LeavePolicyVersionId, types[rules.Single(r => r.Id == x.LowerLeavePolicyRuleId).LeaveTypeId].Id, types[rules.Single(r => r.Id == x.HigherLeavePolicyRuleId).LeaveTypeId].Id, x.Relation)).ToList());
    }

    public async Task<Result<IReadOnlyList<LeavePolicyClubbingRuleDto>>> SaveClubbingAsync(Guid policyId, Guid versionId, LeavePolicyClubbingRequest request, CancellationToken ct = default)
    {
        var version = await _db.LeavePolicyVersions.Include(x => x.Rules).FirstOrDefaultAsync(x => x.Id == versionId && x.LeavePolicyId == policyId, ct); if (version is null) return Result<IReadOnlyList<LeavePolicyClubbingRuleDto>>.NotFound(NotFound); if (version.Status != LeavePolicyVersionStatus.Draft) return Result<IReadOnlyList<LeavePolicyClubbingRuleDto>>.Conflict("Published and Retired versions are immutable."); if (!TokenMatches(version, request.ConcurrencyToken)) return Conflict<IReadOnlyList<LeavePolicyClubbingRuleDto>>("Configuration changed by another user. Reload before saving.");
        var selected = version.Rules.Where(x => x.IsActive).ToDictionary(x => x.LeaveTypeId); var errors = new List<ValidationError>(); var normalized = new HashSet<(Guid, Guid)>();
        foreach (var item in request.Rules)
        {
            if (item.LeaveTypeAId == item.LeaveTypeBId) { errors.Add(new("leaveTypeIds", "A Leave Type cannot be clubbed with itself.")); continue; }
            if (!selected.TryGetValue(item.LeaveTypeAId, out var a) || !selected.TryGetValue(item.LeaveTypeBId, out var b)) { errors.Add(new("leaveTypeIds", "Both Clubbing Leave Types must be selected in this Policy version.")); continue; }
            var pair = a.Id.CompareTo(b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id); if (!normalized.Add(pair)) errors.Add(new("leaveTypeIds", "This Leave Type pair is already configured.")); if (!Enum.IsDefined(item.Relation)) errors.Add(new("relation", "Clubbing relation is invalid."));
        }
        if (errors.Count != 0) return Result<IReadOnlyList<LeavePolicyClubbingRuleDto>>.Invalid("Clubbing configuration is invalid.", errors);
        var entities = request.Rules.Select(item => { var a = selected[item.LeaveTypeAId]; var b = selected[item.LeaveTypeBId]; var pair = a.Id.CompareTo(b.Id) < 0 ? (a.Id, b.Id) : (b.Id, a.Id); return new LeavePolicyClubbingRule { Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyVersionId = version.Id, LowerLeavePolicyRuleId = pair.Item1, HigherLeavePolicyRuleId = pair.Item2, Relation = item.Relation }; }).ToList();
        _db.LeavePolicyClubbingRules.RemoveRange(await _db.LeavePolicyClubbingRules.Where(x => x.LeavePolicyVersionId == version.Id).ToListAsync(ct)); _db.LeavePolicyClubbingRules.AddRange(entities); version.ModifiedDate = DateTime.UtcNow; try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict<IReadOnlyList<LeavePolicyClubbingRuleDto>>("Configuration changed by another user. Reload before saving."); }
        return Result<IReadOnlyList<LeavePolicyClubbingRuleDto>>.Success(request.Rules.Select(item => new LeavePolicyClubbingRuleDto(Guid.Empty, version.Id, item.LeaveTypeAId, item.LeaveTypeBId, item.Relation)).ToList(), "Clubbing rules saved.");
    }

    public async Task<Result<LeavePolicyCancellationRuleDto?>> GetCancellationRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); if (version is null) return Result<LeavePolicyCancellationRuleDto?>.NotFound(NotFound); var rule = await _db.LeavePolicyRules.Include(x => x.CancellationRule).FirstOrDefaultAsync(x => x.LeavePolicyVersionId == version.Id && x.LeaveTypeId == leaveTypeId && x.IsActive, ct); return rule is null ? Result<LeavePolicyCancellationRuleDto?>.NotFound("The selected LeaveType is not assigned to this policy version.") : Result<LeavePolicyCancellationRuleDto?>.Success(rule.CancellationRule is null ? null : ToDto(rule.CancellationRule));
    }

    public async Task<Result<LeavePolicyCancellationRuleDto?>> SaveCancellationRuleAsync(Guid policyId, Guid versionId, Guid leaveTypeId, LeavePolicyCancellationRuleRequest request, CancellationToken ct = default)
    {
        var version = await _db.LeavePolicyVersions.Include(x => x.Rules).ThenInclude(x => x.CancellationRule).FirstOrDefaultAsync(x => x.Id == versionId && x.LeavePolicyId == policyId, ct); if (version is null) return Result<LeavePolicyCancellationRuleDto?>.NotFound(NotFound); if (version.Status != LeavePolicyVersionStatus.Draft) return Result<LeavePolicyCancellationRuleDto?>.Conflict("Published and Retired versions are immutable."); if (!TokenMatches(version, request.ConcurrencyToken)) return Conflict<LeavePolicyCancellationRuleDto?>("Configuration changed by another user. Reload before saving."); var rule = version.Rules.SingleOrDefault(x => x.LeaveTypeId == leaveTypeId && x.IsActive); if (rule is null) return Result<LeavePolicyCancellationRuleDto?>.NotFound("The selected LeaveType is not assigned to this policy version.");
        if (!request.WithdrawAllowed && !request.CancelAllowed && !request.ModifyAllowed) { if (rule.CancellationRule is not null) _db.LeavePolicyCancellationRules.Remove(rule.CancellationRule); version.ModifiedDate = DateTime.UtcNow; await _db.SaveChangesAsync(ct); return Result<LeavePolicyCancellationRuleDto?>.Success(null, "Safe cancellation baseline saved."); }
        var entity = rule.CancellationRule ?? new LeavePolicyCancellationRule { Id = Guid.NewGuid(), TenantId = version.TenantId, LeavePolicyRuleId = rule.Id }; entity.WithdrawAllowed = request.WithdrawAllowed; entity.CancelAllowed = request.CancelAllowed; entity.ModifyAllowed = request.ModifyAllowed; if (rule.CancellationRule is null) _db.LeavePolicyCancellationRules.Add(entity); version.ModifiedDate = DateTime.UtcNow; try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Conflict<LeavePolicyCancellationRuleDto?>("Configuration changed by another user. Reload before saving."); } return Result<LeavePolicyCancellationRuleDto?>.Success(ToDto(entity), "Cancellation rules saved.");
    }

    public async Task<Result<LeavePolicyVersionDto>> PublishAsync(Guid policyId, Guid versionId, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); if (version is null) return Result<LeavePolicyVersionDto>.NotFound(NotFound); if (version.Status != LeavePolicyVersionStatus.Draft) return Result<LeavePolicyVersionDto>.Conflict("Only Draft versions can be published.");
        var errors = await ValidateForPublishAsync(version, ct); if (errors.Count != 0) return Result<LeavePolicyVersionDto>.Invalid("LeavePolicyVersion cannot be published.", errors);
        version.Status = LeavePolicyVersionStatus.Published; version.ModifiedDate = DateTime.UtcNow; await _db.SaveChangesAsync(ct); return Result<LeavePolicyVersionDto>.Success(ToDto(version), "Version published.");
    }

    public async Task<Result<LeavePolicyVersionDto>> RetireAsync(Guid policyId, Guid versionId, CancellationToken ct = default)
    {
        var version = await VersionAsync(policyId, versionId, ct); if (version is null) return Result<LeavePolicyVersionDto>.NotFound(NotFound); if (version.Status != LeavePolicyVersionStatus.Published) return Result<LeavePolicyVersionDto>.Conflict("Only Published versions can be retired.");
        version.Status = LeavePolicyVersionStatus.Retired; version.ModifiedDate = DateTime.UtcNow; await _db.SaveChangesAsync(ct); return Result<LeavePolicyVersionDto>.Success(ToDto(version), "Version retired.");
    }

    private async Task<List<ValidationError>> ValidateForPublishAsync(LeavePolicyVersion version, CancellationToken ct)
    {
        var errors = new List<ValidationError>(); var policy = await PolicyAsync(version.LeavePolicyId, ct);
        if (policy is null || !policy.IsActive) errors.Add(new("policyId", "The parent LeavePolicy must be active."));
        if (version.Status != LeavePolicyVersionStatus.Draft) errors.Add(new("status", "Only Draft versions can be published."));
        if (version.EffectiveTo is DateOnly to && version.EffectiveFrom > to) errors.Add(new("effectiveTo", "EffectiveFrom must be on or before EffectiveTo."));
        if (version.Priority < 0) errors.Add(new("priority", "Priority cannot be negative."));
        var rules = await _db.LeavePolicyRules.Include(x => x.EligibilityRule).Include(x => x.EntitlementRule).Include(x => x.RequestRule).Include(x => x.CalendarRule).Include(x => x.AttachmentRule).Include(x => x.CancellationRule).Where(x => x.LeavePolicyVersionId == version.Id).ToListAsync(ct); var active = rules.Where(x => x.IsActive).ToList();
        if (active.Count == 0) errors.Add(new("leaveTypeIds", "At least one active LeaveType rule is required."));
        if (active.GroupBy(x => x.LeaveTypeId).Any(x => x.Count() > 1)) errors.Add(new("leaveTypeIds", "A LeaveType may appear only once in a version."));
        var types = await _db.LeaveTypes.Where(x => active.Select(r => r.LeaveTypeId).Contains(x.Id)).ToListAsync(ct); if (types.Count != active.Select(x => x.LeaveTypeId).Distinct().Count() || types.Any(x => !x.IsActive)) errors.Add(new("leaveTypeIds", "Every active rule must reference an active LeaveType in this tenant."));
        foreach (var rule in active) errors.AddRange(ValidateEligibility(rule.EligibilityRule));
        foreach (var rule in active) errors.AddRange(ValidateEntitlement(rule.EntitlementRule));
        foreach (var rule in active) errors.AddRange(ValidateRequestRule(rule.RequestRule));
        foreach (var rule in active) errors.AddRange(ValidateCalendarRule(rule.CalendarRule));
        foreach (var rule in active) errors.AddRange(ValidateAttachmentRule(rule.AttachmentRule));
        var clubbing = await _db.LeavePolicyClubbingRules.Where(x => x.LeavePolicyVersionId == version.Id).ToListAsync(ct); errors.AddRange(ValidateClubbing(clubbing, version, active));
        var groups = await _db.LeavePolicyApplicabilitySets.Where(x => x.LeavePolicyVersionId == version.Id).ToListAsync(ct); errors.AddRange(await ValidateGroupsAsync(groups.Select(ToRequest).ToList(), ct));
        if (await _db.LeavePolicyVersions.AnyAsync(x => x.Id != version.Id && x.LeavePolicyId == version.LeavePolicyId && x.Status == LeavePolicyVersionStatus.Published && x.EffectiveFrom <= (version.EffectiveTo ?? DateOnly.MaxValue) && (x.EffectiveTo == null || version.EffectiveFrom <= x.EffectiveTo), ct)) errors.Add(new("effectiveFrom", "Published versions of one policy may not overlap.") );
        return errors;
    }

    private async Task<List<ValidationError>> ValidateGroupsAsync(IEnumerable<LeaveApplicabilityGroupRequest> groups, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        foreach (var group in groups)
        {
            if (group.Gender is not null && !Enum.IsDefined(group.Gender.Value)) errors.Add(new("gender", "Gender is invalid."));
            errors.AddRange(await MissingRefsAsync(group, ct));
            if (group.LobId is Guid lob && group.HoldingCompanyId is Guid holding && !await _db.LinesOfBusiness.AnyAsync(x => x.Id == lob && x.HoldingCompanyId == holding, ct)) errors.Add(new("holdingCompanyId", "The selected LOB does not belong to the selected Holding Company."));
            if (group.SubDepartmentId is Guid sub && group.DepartmentId is Guid dep && !await _db.SubDepartments.AnyAsync(x => x.Id == sub && x.DepartmentId == dep, ct)) errors.Add(new("departmentId", "The selected SubDepartment does not belong to the selected Department."));
            if (group.SectionId is Guid section && group.SubDepartmentId is Guid subDep && !await _db.Sections.AnyAsync(x => x.Id == section && x.SubDepartmentId == subDep, ct)) errors.Add(new("subDepartmentId", "The selected Section does not belong to the selected SubDepartment."));
            if (group.SubSectionId is Guid subSection && group.SectionId is Guid sectionId && !await _db.SubSections.AnyAsync(x => x.Id == subSection && x.SectionId == sectionId, ct)) errors.Add(new("sectionId", "The selected SubSection does not belong to the selected Section."));
            if (group.SubFunctionId is Guid subFunction && group.FunctionId is Guid function && !await _db.SubFunctions.AnyAsync(x => x.Id == subFunction && x.FunctionId == function, ct)) errors.Add(new("functionId", "The selected SubFunction does not belong to the selected Function."));
        }
        return errors;
    }

    private async Task<List<ValidationError>> MissingRefsAsync(LeaveApplicabilityGroupRequest x, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        async Task Check(string field, Guid? id, Func<Guid, Task<bool>> exists) { if (id is Guid value && !await exists(value)) errors.Add(new(field, "The selected master must belong to this tenant and be active.")); }
        await Check("holdingCompanyId", x.HoldingCompanyId, id => _db.HoldingCompanies.AnyAsync(y => y.Id == id && y.IsActive, ct)); await Check("lobId", x.LobId, id => _db.LinesOfBusiness.AnyAsync(y => y.Id == id && y.IsActive, ct)); await Check("organisationId", x.OrganisationId, id => _db.Organisations.AnyAsync(y => y.Id == id && y.IsActive, ct)); await Check("departmentId", x.DepartmentId, id => _db.Departments.AnyAsync(y => y.Id == id && y.IsActive, ct)); await Check("subDepartmentId", x.SubDepartmentId, id => _db.SubDepartments.AnyAsync(y => y.Id == id && y.IsActive, ct)); await Check("sectionId", x.SectionId, id => _db.Sections.AnyAsync(y => y.Id == id && y.IsActive, ct)); await Check("subSectionId", x.SubSectionId, id => _db.SubSections.AnyAsync(y => y.Id == id && y.IsActive, ct)); await Check("functionId", x.FunctionId, id => _db.Functions.AnyAsync(y => y.Id == id && y.IsActive, ct)); await Check("subFunctionId", x.SubFunctionId, id => _db.SubFunctions.AnyAsync(y => y.Id == id && y.IsActive, ct)); await Check("gradeId", x.GradeId, id => _db.Grades.AnyAsync(y => y.Id == id && y.IsActive, ct)); await Check("designationId", x.DesignationId, id => _db.Designations.AnyAsync(y => y.Id == id && y.IsActive, ct)); await Check("employeeTypeId", x.EmployeeTypeId, id => _db.EmployeeTypes.AnyAsync(y => y.Id == id && y.IsActive, ct)); await Check("countryLocationId", x.CountryLocationId, id => _db.Countries.AnyAsync(y => y.Id == id, ct)); await Check("workLocationId", x.WorkLocationId, id => _db.WorkLocations.AnyAsync(y => y.Id == id && y.IsActive, ct)); await Check("costCenterId", x.CostCenterId, id => _db.CostCenters.AnyAsync(y => y.Id == id && y.IsActive, ct));
        return errors;
    }

    private async Task<LeavePolicy?> PolicyAsync(Guid id, CancellationToken ct) => await _db.LeavePolicies.FirstOrDefaultAsync(x => x.Id == id, ct);
    private async Task<LeavePolicyVersion?> VersionAsync(Guid policyId, Guid id, CancellationToken ct) => await _db.LeavePolicyVersions.Include(x => x.Rules).Include(x => x.ApplicabilitySets).FirstOrDefaultAsync(x => x.Id == id && x.LeavePolicyId == policyId, ct);
    private bool HasTenant() => _tenant.TenantId is not null;
    private async Task<bool> HasPeriodOverlapAsync(DateOnly from, DateOnly to, Guid? exclude, CancellationToken ct) => await _db.LeavePeriods.AnyAsync(x => x.IsActive && x.Id != exclude && x.StartDate <= to && from <= x.EndDate, ct);
    private static string? ValidatePeriod(DateOnly from, DateOnly to) => from > to ? "StartDate must be on or before EndDate." : null;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool TokenMatches(BaseEntity entity, string? token) => !string.IsNullOrWhiteSpace(token) && string.Equals(token, Token(entity), StringComparison.Ordinal);
    private static string Token(BaseEntity x) => (x.ModifiedDate ?? x.CreatedDate).ToString("O");
    private static List<ValidationError> ValidateEligibility(LeavePolicyEligibilityRule? rule) => rule is null ? [] : ValidateEligibility(new LeavePolicyEligibilityRuleRequest { EligibilityMode = rule.EligibilityMode, MinimumServiceValue = rule.MinimumServiceValue, MinimumServiceUnit = rule.MinimumServiceUnit, ProbationMode = rule.ProbationMode, NoticePeriodMode = rule.NoticePeriodMode });
    private static List<ValidationError> ValidateEligibility(LeavePolicyEligibilityRuleRequest request)
    {
        var errors = new List<ValidationError>();
        if (!Enum.IsDefined(request.EligibilityMode)) errors.Add(new("eligibilityMode", "Eligibility mode is invalid."));
        if (!Enum.IsDefined(request.ProbationMode)) errors.Add(new("probationMode", "Probation mode is invalid."));
        if (!Enum.IsDefined(request.NoticePeriodMode)) errors.Add(new("noticePeriodMode", "Notice period mode is invalid."));
        if (request.EligibilityMode == EligibilityMode.MinimumService && (request.MinimumServiceValue is not > 0 || request.MinimumServiceUnit is null)) errors.Add(new("minimumService", "Minimum service value and unit must be provided and positive."));
        if (request.EligibilityMode == EligibilityMode.Immediate && (request.MinimumServiceValue is not null || request.MinimumServiceUnit is not null)) errors.Add(new("minimumService", "Immediate eligibility cannot include a minimum service restriction."));
        if (request.ProbationMode == ProbationMode.AfterConfirmation) errors.Add(new("probationMode", "AfterConfirmation is unavailable until an authoritative confirmation source is approved."));
        return errors;
    }
    private static bool IsBaseline(LeavePolicyEligibilityRuleRequest request) => request.EligibilityMode == EligibilityMode.Immediate && request.MinimumServiceValue is null && request.MinimumServiceUnit is null && request.ProbationMode == ProbationMode.Allowed && request.NoticePeriodMode == NoticePeriodMode.Allowed;
    private static List<ValidationError> ValidateEntitlement(LeavePolicyEntitlementRule? rule) => rule is null ? [] : ValidateEntitlement(new LeavePolicyEntitlementRuleRequest { EntitlementMode = rule.EntitlementMode, EntitlementSource = rule.EntitlementSource, EntitlementQuantity = rule.EntitlementQuantity, AccrualFrequency = rule.AccrualFrequency, AccrualTiming = rule.AccrualTiming });
    private static List<ValidationError> ValidateEntitlement(LeavePolicyEntitlementRuleRequest request)
    {
        var errors = new List<ValidationError>();
        if (!Enum.IsDefined(request.EntitlementMode)) errors.Add(new("entitlementMode", "Entitlement mode is invalid."));
        if (!Enum.IsDefined(request.EntitlementSource)) errors.Add(new("entitlementSource", "Entitlement source is invalid."));
        if (!Enum.IsDefined(request.AccrualFrequency)) errors.Add(new("accrualFrequency", "Accrual frequency is invalid."));
        if (request.AccrualTiming is not null && !Enum.IsDefined(request.AccrualTiming.Value)) errors.Add(new("accrualTiming", "Accrual timing is invalid."));
        if (request.EntitlementMode == EntitlementMode.Allocated && request.EntitlementQuantity is not > 0) errors.Add(new("entitlementQuantity", "Allocated entitlement requires a positive quantity."));
        if (request.EntitlementMode != EntitlementMode.Allocated && request.EntitlementQuantity is not null) errors.Add(new("entitlementQuantity", "A finite quantity is only valid for Allocated entitlement."));
        if (request.EntitlementMode == EntitlementMode.NoBalanceRequired && request.EntitlementSource != EntitlementSource.NoBalanceRequired) errors.Add(new("entitlementSource", "NoBalanceRequired entitlement must use the NoBalanceRequired source."));
        if (request.EntitlementMode == EntitlementMode.NoBalanceRequired && request.AccrualFrequency != AccrualFrequency.None) errors.Add(new("accrualFrequency", "NoBalanceRequired entitlement cannot accrue a normal balance."));
        if (request.EntitlementSource == EntitlementSource.ExternalGrant && request.AccrualFrequency != AccrualFrequency.None) errors.Add(new("accrualFrequency", "ExternalGrant entitlement cannot use policy accrual."));
        if (request.AccrualFrequency == AccrualFrequency.None && request.AccrualTiming is not null) errors.Add(new("accrualTiming", "Accrual timing is only valid when accrual is configured."));
        if (request.AccrualFrequency != AccrualFrequency.None && request.AccrualTiming is null) errors.Add(new("accrualTiming", "Accrual timing is required when accrual is configured."));
        if (request.AccrualFrequency == AccrualFrequency.Quarterly) errors.Add(new("accrualFrequency", "Quarterly accrual is unavailable until business approval is recorded."));
        if (request.AccrualFrequency != AccrualFrequency.None && request.EntitlementSource != EntitlementSource.PolicyAccrual) errors.Add(new("entitlementSource", "Scheduled accrual requires the PolicyAccrual source."));
        return errors;
    }
    private static List<ValidationError> ValidateRequestRule(LeavePolicyRequestRule? rule) => rule is null ? [] : ValidateRequestRule(new LeavePolicyRequestRuleRequest { MinimumRequestQuantity = rule.MinimumRequestQuantity, MaximumRequestQuantity = rule.MaximumRequestQuantity, MaximumConsecutiveQuantity = rule.MaximumConsecutiveQuantity, MinimumAdvanceNoticeDays = rule.MinimumAdvanceNoticeDays, BackdatedRequestMode = rule.BackdatedRequestMode, MaximumBackdatedDays = rule.MaximumBackdatedDays, MaximumRequestsPerPeriod = rule.MaximumRequestsPerPeriod, MaximumQuantityPerPeriod = rule.MaximumQuantityPerPeriod, RequestLimitPeriod = rule.RequestLimitPeriod, PartialDayMode = rule.PartialDayMode });
    private static List<ValidationError> ValidateRequestRule(LeavePolicyRequestRuleRequest request)
    {
        var errors = new List<ValidationError>();
        if (request.MinimumRequestQuantity is not null and <= 0) errors.Add(new("minimumRequestQuantity", "Minimum request quantity must be positive."));
        if (request.MaximumRequestQuantity is not null and <= 0) errors.Add(new("maximumRequestQuantity", "Maximum request quantity must be positive."));
        if (request.MaximumConsecutiveQuantity is not null and <= 0) errors.Add(new("maximumConsecutiveQuantity", "Maximum consecutive quantity must be positive."));
        if (request.MinimumRequestQuantity is decimal min && request.MaximumRequestQuantity is decimal max && max < min) errors.Add(new("maximumRequestQuantity", "Maximum request quantity must be at least the minimum."));
        if (request.MinimumAdvanceNoticeDays < 0) errors.Add(new("minimumAdvanceNoticeDays", "Advance notice cannot be negative."));
        if (!Enum.IsDefined(request.BackdatedRequestMode)) errors.Add(new("backdatedRequestMode", "Backdated request mode is invalid."));
        if (request.BackdatedRequestMode == BackdatedRequestMode.AllowedUpToDays && request.MaximumBackdatedDays is not > 0) errors.Add(new("maximumBackdatedDays", "A positive backdate limit is required."));
        if (request.BackdatedRequestMode != BackdatedRequestMode.AllowedUpToDays && request.MaximumBackdatedDays is not null) errors.Add(new("maximumBackdatedDays", "A backdate limit is only valid for bounded backdating."));
        if (request.MaximumRequestsPerPeriod is int count && count <= 0) errors.Add(new("maximumRequestsPerPeriod", "Request count limit must be positive."));
        if (request.MaximumQuantityPerPeriod is decimal quantity && quantity <= 0) errors.Add(new("maximumQuantityPerPeriod", "Period quantity limit must be positive."));
        if ((request.MaximumRequestsPerPeriod is not null || request.MaximumQuantityPerPeriod is not null) && request.RequestLimitPeriod is null) errors.Add(new("requestLimitPeriod", "A limit period is required when a period limit is configured."));
        if (request.MaximumRequestsPerPeriod is null && request.MaximumQuantityPerPeriod is null && request.RequestLimitPeriod is not null) errors.Add(new("requestLimitPeriod", "A limit period requires a request or quantity limit."));
        if (!Enum.IsDefined(request.PartialDayMode)) errors.Add(new("partialDayMode", "Partial-day mode is invalid."));
        return errors;
    }
    private static bool IsRequestBaseline(LeavePolicyRequestRuleRequest request) => request.MinimumRequestQuantity is null && request.MaximumRequestQuantity is null && request.MaximumConsecutiveQuantity is null && request.MinimumAdvanceNoticeDays == 0 && request.BackdatedRequestMode == BackdatedRequestMode.NotAllowed && request.MaximumBackdatedDays is null && request.MaximumRequestsPerPeriod is null && request.MaximumQuantityPerPeriod is null && request.RequestLimitPeriod is null && request.PartialDayMode == PartialDayMode.FullDayOnly;
    private static List<ValidationError> ValidateCalendarRule(LeavePolicyCalendarRule? rule) => rule is null ? [] : ValidateCalendarRule(new LeavePolicyCalendarRuleRequest { HolidayTreatment = rule.HolidayTreatment, WeekOffTreatment = rule.WeekOffTreatment, SandwichMode = rule.SandwichMode, ApplyToPrefix = rule.ApplyToPrefix, ApplyToSuffix = rule.ApplyToSuffix, ApplyToBetween = rule.ApplyToBetween });
    private static List<ValidationError> ValidateCalendarRule(LeavePolicyCalendarRuleRequest request)
    {
        var errors = new List<ValidationError>(); if (!Enum.IsDefined(request.HolidayTreatment)) errors.Add(new("holidayTreatment", "Holiday treatment is invalid.")); if (!Enum.IsDefined(request.WeekOffTreatment)) errors.Add(new("weekOffTreatment", "Week-off treatment is invalid.")); if (!Enum.IsDefined(request.SandwichMode)) errors.Add(new("sandwichMode", "Sandwich mode is invalid."));
        if (request.SandwichMode == SandwichMode.Disabled && (request.ApplyToPrefix || request.ApplyToSuffix || request.ApplyToBetween)) errors.Add(new("sandwichMode", "Disabled sandwich mode cannot have active positions."));
        return errors;
    }
    private static bool IsCalendarBaseline(LeavePolicyCalendarRuleRequest request) => request.HolidayTreatment == HolidayTreatment.Exclude && request.WeekOffTreatment == WeekOffTreatment.Exclude && request.SandwichMode == SandwichMode.Disabled && !request.ApplyToPrefix && !request.ApplyToSuffix && !request.ApplyToBetween;
    private static List<ValidationError> ValidateAttachmentRule(LeavePolicyAttachmentRule? rule) => rule is null ? [] : ValidateAttachmentRule(new LeavePolicyAttachmentRuleRequest { AttachmentRequirement = rule.AttachmentRequirement, ThresholdQuantity = rule.ThresholdQuantity, DocumentLabel = rule.DocumentLabel });
    private static List<ValidationError> ValidateAttachmentRule(LeavePolicyAttachmentRuleRequest request)
    {
        var errors = new List<ValidationError>(); if (!Enum.IsDefined(request.AttachmentRequirement)) errors.Add(new("attachmentRequirement", "Attachment requirement is invalid."));
        if (request.AttachmentRequirement == AttachmentRequirement.RequiredAboveQuantity && request.ThresholdQuantity is not > 0) errors.Add(new("thresholdQuantity", "A positive threshold is required when attachments are required above a quantity."));
        if (request.AttachmentRequirement != AttachmentRequirement.RequiredAboveQuantity && request.ThresholdQuantity is not null) errors.Add(new("thresholdQuantity", "A threshold is only valid for RequiredAboveQuantity."));
        return errors;
    }
    private static bool IsAttachmentBaseline(LeavePolicyAttachmentRuleRequest request) => request.AttachmentRequirement == AttachmentRequirement.None && request.ThresholdQuantity is null && string.IsNullOrWhiteSpace(request.DocumentLabel);
    private static List<ValidationError> ValidateClubbing(IEnumerable<LeavePolicyClubbingRule> rules, LeavePolicyVersion version, IReadOnlyCollection<LeavePolicyRule> selected)
    {
        var errors = new List<ValidationError>();
        var selectedIds = selected.Select(x => x.Id).ToHashSet();
        var pairs = new HashSet<(Guid, Guid)>();
        foreach (var rule in rules)
        {
            if (rule.TenantId != version.TenantId || rule.LeavePolicyVersionId != version.Id)
                errors.Add(new("clubbing", "Clubbing participants must belong to this tenant and Policy Version."));
            if (!selectedIds.Contains(rule.LowerLeavePolicyRuleId) || !selectedIds.Contains(rule.HigherLeavePolicyRuleId))
                errors.Add(new("clubbing", "Both Clubbing participants must be selected active Leave Types in this Policy Version."));
            if (rule.LowerLeavePolicyRuleId == rule.HigherLeavePolicyRuleId)
                errors.Add(new("clubbing", "A Leave Type cannot be clubbed with itself."));
            var pair = rule.LowerLeavePolicyRuleId.CompareTo(rule.HigherLeavePolicyRuleId) < 0
                ? (rule.LowerLeavePolicyRuleId, rule.HigherLeavePolicyRuleId)
                : (rule.HigherLeavePolicyRuleId, rule.LowerLeavePolicyRuleId);
            if (!pairs.Add(pair)) errors.Add(new("clubbing", "A Clubbing pair may be configured only once."));
            if (!Enum.IsDefined(rule.Relation)) errors.Add(new("clubbing", "Clubbing relation is invalid."));
        }
        return errors;
    }
    private static Result<T> Conflict<T>(string message) => Result<T>.Conflict(message);
    private static Result<T> Duplicate<T>(string field, string message) => Result<T>.Conflict(message, [new ValidationError(field, message)]);
    private static PagedResult<T> Page<T>(IEnumerable<T> source, PagedQuery query) { var page = Math.Max(1, query.Page); var size = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize); var list = source.ToList(); return new(list.Skip((page - 1) * size).Take(size).ToList(), page, size, list.Count); }
    private static LeaveTypeDto ToDto(LeaveType x) => new(x.Id, x.Code, x.Name, x.Description, x.DefaultUnit, x.IsPaid, x.IsActive, x.CreatedDate, x.ModifiedDate, Token(x));
    private static LeavePeriodDto ToDto(LeavePeriod x) => new(x.Id, x.Code, x.Name, x.StartDate, x.EndDate, x.IsActive, x.CreatedDate, x.ModifiedDate, Token(x));
    private static LeavePolicyDto ToDto(LeavePolicy x) { var current = x.Versions?.Where(v => v.Status == LeavePolicyVersionStatus.Published).OrderByDescending(v => v.EffectiveFrom).FirstOrDefault(); return new(x.Id, x.Code, x.Name, x.Description, x.IsActive, x.Versions?.Count ?? 0, current?.VersionNumber, x.CreatedDate, x.ModifiedDate, Token(x)); }
    private static LeavePolicyVersionDto ToDto(LeavePolicyVersion x) => new(x.Id, x.VersionNumber, x.EffectiveFrom, x.EffectiveTo, x.Status, x.Priority, x.Rules?.Count(r => r.IsActive) ?? 0, x.ApplicabilitySets?.Count ?? 0, x.CreatedDate, x.CreatedBy, x.ModifiedDate, Token(x), new(x.Status == LeavePolicyVersionStatus.Draft, x.Status == LeavePolicyVersionStatus.Draft, x.Status == LeavePolicyVersionStatus.Draft, x.Status == LeavePolicyVersionStatus.Published, true));
    private static LeaveApplicabilityGroupDto ToDto(LeavePolicyApplicabilitySet x) => new(x.Id, x.Gender, x.HoldingCompanyId, x.LobId, x.OrganisationId, x.DepartmentId, x.SubDepartmentId, x.SectionId, x.SubSectionId, x.FunctionId, x.SubFunctionId, x.GradeId, x.DesignationId, x.EmployeeTypeId, x.CountryLocationId, x.WorkLocationId, x.CostCenterId);
    private static LeavePolicyEligibilityRuleDto ToDto(LeavePolicyEligibilityRule x) => new(x.Id, x.LeavePolicyRuleId, x.EligibilityMode, x.MinimumServiceValue, x.MinimumServiceUnit, x.ProbationMode, x.NoticePeriodMode, Token(x));
    private static LeavePolicyEntitlementRuleDto ToDto(LeavePolicyEntitlementRule x) => new(x.Id, x.LeavePolicyRuleId, x.EntitlementMode, x.EntitlementSource, x.EntitlementQuantity, x.AccrualFrequency, x.AccrualTiming, Token(x));
    private static LeavePolicyRequestRuleDto ToDto(LeavePolicyRequestRule x) => new(x.Id, x.LeavePolicyRuleId, x.MinimumRequestQuantity, x.MaximumRequestQuantity, x.MaximumConsecutiveQuantity, x.MinimumAdvanceNoticeDays, x.BackdatedRequestMode, x.MaximumBackdatedDays, x.MaximumRequestsPerPeriod, x.MaximumQuantityPerPeriod, x.RequestLimitPeriod, x.PartialDayMode, Token(x));
    private static LeavePolicyCalendarRuleDto ToDto(LeavePolicyCalendarRule x) => new(x.Id, x.LeavePolicyRuleId, x.HolidayTreatment, x.WeekOffTreatment, x.SandwichMode, x.ApplyToPrefix, x.ApplyToSuffix, x.ApplyToBetween, Token(x));
    private static LeavePolicyAttachmentRuleDto ToDto(LeavePolicyAttachmentRule x) => new(x.Id, x.LeavePolicyRuleId, x.AttachmentRequirement, x.ThresholdQuantity, x.DocumentLabel, Token(x));
    private static LeavePolicyCancellationRuleDto ToDto(LeavePolicyCancellationRule x) => new(x.Id, x.LeavePolicyRuleId, x.WithdrawAllowed, x.CancelAllowed, x.ModifyAllowed, Token(x));
    private static LeaveApplicabilityGroupRequest ToRequest(LeavePolicyApplicabilitySet x) => new() { Gender = x.Gender, HoldingCompanyId = x.HoldingCompanyId, LobId = x.LobId, OrganisationId = x.OrganisationId, DepartmentId = x.DepartmentId, SubDepartmentId = x.SubDepartmentId, SectionId = x.SectionId, SubSectionId = x.SubSectionId, FunctionId = x.FunctionId, SubFunctionId = x.SubFunctionId, GradeId = x.GradeId, DesignationId = x.DesignationId, EmployeeTypeId = x.EmployeeTypeId, CountryLocationId = x.CountryLocationId, WorkLocationId = x.WorkLocationId, CostCenterId = x.CostCenterId };
    private LeavePolicyApplicabilitySet ToEntity(LeaveApplicabilityGroupRequest x, Guid versionId) => new() { Id = Guid.NewGuid(), TenantId = _tenant.TenantId!.Value, LeavePolicyVersionId = versionId, Gender = x.Gender, HoldingCompanyId = x.HoldingCompanyId, LobId = x.LobId, OrganisationId = x.OrganisationId, DepartmentId = x.DepartmentId, SubDepartmentId = x.SubDepartmentId, SectionId = x.SectionId, SubSectionId = x.SubSectionId, FunctionId = x.FunctionId, SubFunctionId = x.SubFunctionId, GradeId = x.GradeId, DesignationId = x.DesignationId, EmployeeTypeId = x.EmployeeTypeId, CountryLocationId = x.CountryLocationId, WorkLocationId = x.WorkLocationId, CostCenterId = x.CostCenterId };
    private static LeavePolicyApplicabilitySet CloneSet(LeavePolicyApplicabilitySet x, Guid tenantId, Guid versionId) => new() { Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyVersionId = versionId, Gender = x.Gender, HoldingCompanyId = x.HoldingCompanyId, LobId = x.LobId, OrganisationId = x.OrganisationId, DepartmentId = x.DepartmentId, SubDepartmentId = x.SubDepartmentId, SectionId = x.SectionId, SubSectionId = x.SubSectionId, FunctionId = x.FunctionId, SubFunctionId = x.SubFunctionId, GradeId = x.GradeId, DesignationId = x.DesignationId, EmployeeTypeId = x.EmployeeTypeId, CountryLocationId = x.CountryLocationId, WorkLocationId = x.WorkLocationId, CostCenterId = x.CostCenterId };
}
