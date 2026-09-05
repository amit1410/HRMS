using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LeavePolicyFoundationService : ILeavePolicyFoundationService
{
    private readonly IHrmsDbContext _db;
    public LeavePolicyFoundationService(IHrmsDbContext db) => _db = db;

    public async Task<Result<LeavePolicyVersion>> CreateDraftVersionAsync(Guid tenantId, Guid policyId, DateOnly effectiveFrom, DateOnly? effectiveTo, int priority, string? actor, CancellationToken ct = default)
    {
        if (effectiveTo is not null && effectiveFrom > effectiveTo)
            return Result<LeavePolicyVersion>.Invalid("effectiveTo", "EffectiveTo must be on or after EffectiveFrom.");
        if (await _db.LeavePolicies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == policyId && x.TenantId == tenantId && x.IsActive, ct) is null)
            return Result<LeavePolicyVersion>.NotFound("Active LeavePolicy was not found in this tenant.");

        var versionNumber = (await _db.LeavePolicyVersions.Where(x => x.TenantId == tenantId && x.LeavePolicyId == policyId).Select(x => (int?)x.VersionNumber).MaxAsync(ct) ?? 0) + 1;
        var version = new LeavePolicyVersion { Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyId = policyId, VersionNumber = versionNumber, EffectiveFrom = effectiveFrom, EffectiveTo = effectiveTo, Priority = priority, Status = LeavePolicyVersionStatus.Draft, CreatedDate = DateTime.UtcNow, CreatedBy = actor };
        _db.LeavePolicyVersions.Add(version);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Result<LeavePolicyVersion>.Conflict("The policy version number was allocated concurrently. Retry by creating a new draft."); }
        return Result<LeavePolicyVersion>.Success(version);
    }

    public async Task<Result<bool>> ValidatePeriodAsync(Guid tenantId, LeavePeriod period, CancellationToken ct = default)
    {
        if (period.StartDate > period.EndDate) return Result<bool>.Invalid("startDate", "StartDate must be on or before EndDate.");
        var overlap = await _db.LeavePeriods.AnyAsync(x => x.TenantId == tenantId && x.IsActive && x.Id != period.Id && x.StartDate <= period.EndDate && period.StartDate <= x.EndDate, ct);
        return overlap ? Result<bool>.Conflict("The active LeavePeriod overlaps another active period in this tenant.") : Result<bool>.Success(true);
    }

    public async Task<Result<bool>> PublishAsync(Guid tenantId, Guid versionId, string? actor, CancellationToken ct = default)
    {
        var version = await _db.LeavePolicyVersions.Include(x => x.LeavePolicy).SingleOrDefaultAsync(x => x.Id == versionId && x.TenantId == tenantId, ct);
        if (version is null) return Result<bool>.NotFound("LeavePolicyVersion was not found in this tenant.");
        if (version.Status != LeavePolicyVersionStatus.Draft) return Result<bool>.Conflict("Only Draft policy versions can be published.");
        var errors = await ValidateForPublishAsync(version, tenantId, ct);
        if (errors.Count != 0) return Result<bool>.Invalid("LeavePolicyVersion cannot be published.", errors);
        version.Status = LeavePolicyVersionStatus.Published;
        version.ModifiedDate = DateTime.UtcNow;
        version.CreatedBy ??= actor;
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private async Task<List<ValidationError>> ValidateForPublishAsync(LeavePolicyVersion version, Guid tenantId, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        if (version.LeavePolicy is null || !version.LeavePolicy.IsActive || version.LeavePolicy.TenantId != tenantId) errors.Add(new("leavePolicyId", "Active LeavePolicy must belong to the tenant."));
        if (version.EffectiveTo is not null && version.EffectiveFrom > version.EffectiveTo) errors.Add(new("effectiveTo", "EffectiveTo must be on or after EffectiveFrom."));
        if (version.Priority < 0) errors.Add(new("priority", "Priority cannot be negative."));
        var rules = await _db.LeavePolicyRules.Where(x => x.TenantId == tenantId && x.LeavePolicyVersionId == version.Id).ToListAsync(ct);
        if (rules.Count == 0 || !rules.Any(x => x.IsActive)) errors.Add(new("rules", "At least one active LeavePolicyRule is required."));
        if (rules.Where(x => x.IsActive).GroupBy(x => x.LeaveTypeId).Any(g => g.Count() > 1)) errors.Add(new("rules", "A LeaveType may appear only once as an active rule in a policy version."));
        var typeIds = rules.Select(x => x.LeaveTypeId).Distinct().ToList();
        if (await _db.LeaveTypes.CountAsync(x => x.TenantId == tenantId && typeIds.Contains(x.Id)) != typeIds.Count) errors.Add(new("rules", "Every LeaveType must belong to the tenant."));
        var sets = await _db.LeavePolicyApplicabilitySets.Where(x => x.TenantId == tenantId && x.LeavePolicyVersionId == version.Id).ToListAsync(ct);
        var masterChecks = new (string, IEnumerable<Guid?>)[] {
            ("holdingCompanyId", sets.Select(x => x.HoldingCompanyId)), ("lobId", sets.Select(x => x.LobId)), ("organisationId", sets.Select(x => x.OrganisationId)),
            ("departmentId", sets.Select(x => x.DepartmentId)), ("subDepartmentId", sets.Select(x => x.SubDepartmentId)), ("sectionId", sets.Select(x => x.SectionId)),
            ("subSectionId", sets.Select(x => x.SubSectionId)), ("functionId", sets.Select(x => x.FunctionId)), ("subFunctionId", sets.Select(x => x.SubFunctionId)),
            ("gradeId", sets.Select(x => x.GradeId)), ("designationId", sets.Select(x => x.DesignationId)), ("employeeTypeId", sets.Select(x => x.EmployeeTypeId)),
            ("countryLocationId", sets.Select(x => x.CountryLocationId)), ("workLocationId", sets.Select(x => x.WorkLocationId)), ("costCenterId", sets.Select(x => x.CostCenterId)) };
        foreach (var (field, ids) in masterChecks)
            if (ids.Any(x => x is not null)) errors.AddRange(await MissingMasterErrorsAsync(field, ids.Where(x => x is not null).Select(x => x!.Value), tenantId, ct));
        var overlaps = await _db.LeavePolicyVersions.AnyAsync(x => x.TenantId == tenantId && x.LeavePolicyId == version.LeavePolicyId && x.Id != version.Id && x.Status == LeavePolicyVersionStatus.Published && x.EffectiveFrom <= (version.EffectiveTo ?? DateOnly.MaxValue) && (x.EffectiveTo == null || version.EffectiveFrom <= x.EffectiveTo), ct);
        if (overlaps) errors.Add(new("effectiveFrom", "Published versions of one policy may not have overlapping effective dates."));
        return errors;
    }

    private async Task<IEnumerable<ValidationError>> MissingMasterErrorsAsync(string field, IEnumerable<Guid> ids, Guid tenantId, CancellationToken ct)
    {
        var expected = ids.Distinct().Count();
        var found = field switch
        {
            "holdingCompanyId" => await _db.HoldingCompanies.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            "lobId" => await _db.LinesOfBusiness.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            "organisationId" => await _db.Organisations.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            "departmentId" => await _db.Departments.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            "subDepartmentId" => await _db.SubDepartments.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            "sectionId" => await _db.Sections.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            "subSectionId" => await _db.SubSections.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            "functionId" => await _db.Functions.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            "subFunctionId" => await _db.SubFunctions.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            "gradeId" => await _db.Grades.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            "designationId" => await _db.Designations.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            "employeeTypeId" => await _db.EmployeeTypes.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            "countryLocationId" => await _db.Countries.CountAsync(x => ids.Contains(x.Id), ct),
            "workLocationId" => await _db.WorkLocations.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            "costCenterId" => await _db.CostCenters.CountAsync(x => x.TenantId == tenantId && ids.Contains(x.Id), ct),
            _ => 0
        };
        return found == expected ? [] : [new ValidationError(field, "Every applicability master reference must belong to the tenant.")];
    }
}
