using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;

namespace HRMS.Application.Services;

public sealed class RoleScopeResolver(IEffectiveEmploymentResolver employment) : IRoleScopeResolver
{
    public async Task<bool> AppliesAsync(UserRole assignment, Guid employeeId, DateOnly effectiveDate, CancellationToken cancellationToken = default)
    {
        if (assignment.Scopes.Count == 0) return true;
        var resolved = await employment.ResolveAsync(assignment.TenantId, employeeId, effectiveDate, cancellationToken);
        if (resolved.Employment is null) return false;
        return assignment.Scopes.All(scope => ScopeMatches(scope.ScopeType, scope.ScopeEntityId, resolved.Employment));
    }

    private static bool ScopeMatches(RoleScopeType type, Guid id, EffectiveEmploymentSnapshot x) => type switch
    {
        RoleScopeType.HoldingCompany => x.HoldingCompanyId == id,
        RoleScopeType.Lob => x.LobId == id,
        RoleScopeType.Organisation => x.OrganisationId == id,
        RoleScopeType.Department => x.DepartmentId == id,
        RoleScopeType.SubDepartment => x.SubDepartmentId == id,
        RoleScopeType.Section => x.SectionId == id,
        RoleScopeType.SubSection => x.SubSectionId == id,
        RoleScopeType.Function => x.FunctionId == id,
        RoleScopeType.SubFunction => x.SubFunctionId == id,
        RoleScopeType.Country => x.CountryLocationId == id,
        RoleScopeType.Location or RoleScopeType.WorkLocation => x.WorkLocationId == id,
        RoleScopeType.CostCenter => x.CostCenterId == id,
        _ => false
    };
}
