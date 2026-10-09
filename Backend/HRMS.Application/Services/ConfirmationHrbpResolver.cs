using HRMS.Application.Abstractions;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Derives the HR/HRBP reviewer for an employee from role assignments (there is no dedicated HRBP field on
/// the employee model). Among users holding the <see cref="RoleNames.HRBP"/> role whose
/// <see cref="UserRoleAssignmentScope"/> rows cover the employee's effective employment, the single
/// most-specific assignment is chosen and mapped back to an employee via
/// <see cref="AccountEmployeeCurrentLink"/>. Scope semantics match <c>EmployeeAccessScopeService</c>:
/// OR within a dimension, AND across dimensions; an unscoped HRBP assignment covers everyone (specificity 0).
/// Zero or ambiguous (tied-specificity, distinct employees) matches resolve to no reviewer.
/// </summary>
public sealed class ConfirmationHrbpResolver : IConfirmationHrbpResolver
{
    private readonly IHrmsDbContext _db;
    private readonly IEffectiveEmploymentResolver _employmentResolver;
    private readonly ITenantContext? _tenantContext;

    public ConfirmationHrbpResolver(
        IHrmsDbContext db,
        IEffectiveEmploymentResolver? employmentResolver = null,
        ITenantContext? tenantContext = null)
    {
        _db = db;
        _employmentResolver = employmentResolver ?? new EffectiveEmploymentResolver(db, tenantContext);
        _tenantContext = tenantContext;
    }

    public async Task<ConfirmationHrbpResolutionResult> ResolveAsync(
        Guid tenantId, Guid employeeId, DateOnly effectiveDate, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || (_tenantContext?.TenantId is Guid current && current != tenantId))
            return new(ConfirmationHrbpResolutionStatus.InvalidTenant, null, "The requested tenant is not the authenticated tenant.");

        var employment = await _employmentResolver.ResolveAsync(tenantId, employeeId, effectiveDate, ct);
        if (employment.Status != EffectiveEmploymentResolutionStatus.Resolved || employment.Employment is null)
            return new(ConfirmationHrbpResolutionStatus.EffectiveEmploymentNotFound, null, employment.Message);
        var h = employment.Employment;

        var hrbpRoleId = await _db.Roles.AsNoTracking().Where(r => r.Name == RoleNames.HRBP).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
        if (hrbpRoleId is null)
            return new(ConfirmationHrbpResolutionStatus.NoHrbp, null, "No HRBP role is configured in this tenant.");

        var assignments = await _db.UserRoles.AsNoTracking().Include(x => x.Scopes)
            .Where(x => x.TenantId == tenantId && x.RoleId == hrbpRoleId
                && x.EffectiveFrom <= effectiveDate && (x.EffectiveTo == null || x.EffectiveTo >= effectiveDate))
            .ToListAsync(ct);
        if (assignments.Count == 0)
            return new(ConfirmationHrbpResolutionStatus.NoHrbp, null, "No HRBP assignment exists for this tenant.");

        // Candidate = (userId, specificity) where the assignment's scopes cover the employee.
        var candidates = new List<(Guid UserId, int Specificity)>();
        foreach (var a in assignments)
        {
            if (a.Scopes.Count == 0) { candidates.Add((a.UserId, 0)); continue; }
            if (ScopesCover(a.Scopes, h)) candidates.Add((a.UserId, a.Scopes.Count));
        }
        if (candidates.Count == 0)
            return new(ConfirmationHrbpResolutionStatus.NoHrbp, null, "No HRBP assignment covers this employee's organisation.");

        var bestSpecificity = candidates.Max(c => c.Specificity);
        var topUserIds = candidates.Where(c => c.Specificity == bestSpecificity).Select(c => c.UserId).Distinct().ToList();

        // Map users -> their linked employee; a user without a link cannot be the reviewer employee.
        var links = await _db.AccountEmployeeCurrentLinks.AsNoTracking()
            .Where(l => l.TenantId == tenantId && topUserIds.Contains(l.UserId))
            .Select(l => new { l.UserId, l.EmployeeId })
            .ToListAsync(ct);
        var employeeIds = links.Select(l => l.EmployeeId).Distinct().ToList();
        if (employeeIds.Count == 0)
            return new(ConfirmationHrbpResolutionStatus.NoHrbp, null, "The most-specific HRBP has no linked employee record.");
        if (employeeIds.Count > 1)
            return new(ConfirmationHrbpResolutionStatus.Ambiguous, null, "Multiple equally-specific HRBPs cover this employee; HR/HRBP review is left unassigned and authorized by permission and scope.");

        return new(ConfirmationHrbpResolutionStatus.Resolved, employeeIds[0], "HRBP reviewer resolved.");
    }

    private static bool ScopesCover(ICollection<UserRoleAssignmentScope> scopes, EffectiveEmploymentSnapshot h)
    {
        // AND across dimensions, OR within a dimension (same as EmployeeAccessScopeService).
        foreach (var dimension in scopes.GroupBy(s => s.ScopeType))
        {
            var employeeValue = ValueFor(dimension.Key, h);
            if (!dimension.Any(s => employeeValue is Guid v && s.ScopeEntityId == v))
                return false;
        }
        return true;
    }

    private static Guid? ValueFor(RoleScopeType type, EffectiveEmploymentSnapshot h) => type switch
    {
        RoleScopeType.HoldingCompany => h.HoldingCompanyId,
        RoleScopeType.Lob => h.LobId,
        RoleScopeType.Organisation => h.OrganisationId,
        RoleScopeType.Department => h.DepartmentId,
        RoleScopeType.SubDepartment => h.SubDepartmentId,
        RoleScopeType.Section => h.SectionId,
        RoleScopeType.SubSection => h.SubSectionId,
        RoleScopeType.Function => h.FunctionId,
        RoleScopeType.SubFunction => h.SubFunctionId,
        RoleScopeType.Country => h.CountryLocationId,
        RoleScopeType.Location or RoleScopeType.WorkLocation => h.WorkLocationId,
        RoleScopeType.CostCenter => h.CostCenterId,
        RoleScopeType.Grade => h.GradeId,
        RoleScopeType.Designation => h.DesignationId,
        RoleScopeType.EmployeeType => h.EmployeeTypeId,
        _ => null
    };
}
