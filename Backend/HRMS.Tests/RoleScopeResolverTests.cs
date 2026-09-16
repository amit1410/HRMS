using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;

namespace HRMS.Tests;

public sealed class RoleScopeResolverTests
{
    private static readonly Guid Tenant = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Employee = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Finance = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid It = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task Department_scope_uses_the_effective_employment_snapshot_for_the_requested_date()
    {
        var employment = new DateAwareEmploymentResolver();
        var resolver = new RoleScopeResolver(employment);
        var assignment = new UserRole
        {
            TenantId = Tenant,
            UserId = Guid.NewGuid(),
            RoleId = 1,
            EffectiveFrom = DateOnly.MinValue,
            Scopes = [new UserRoleAssignmentScope { Id = Guid.NewGuid(), TenantId = Tenant, ScopeType = RoleScopeType.Department, ScopeEntityId = It }]
        };

        Assert.False(await resolver.AppliesAsync(assignment, Employee, new(2026, 8, 31)));
        Assert.True(await resolver.AppliesAsync(assignment, Employee, new(2026, 9, 1)));
    }

    [Fact]
    public async Task Non_matching_department_scope_is_rejected_and_no_scope_is_tenant_wide()
    {
        var resolver = new RoleScopeResolver(new DateAwareEmploymentResolver());
        var scoped = new UserRole { TenantId = Tenant, Scopes = [new UserRoleAssignmentScope { ScopeType = RoleScopeType.Department, ScopeEntityId = It }] };
        Assert.False(await resolver.AppliesAsync(scoped, Employee, new(2026, 8, 31)));
        Assert.True(await resolver.AppliesAsync(scoped, Employee, new(2026, 9, 1)));

        var global = new UserRole { TenantId = Tenant };
        Assert.True(await resolver.AppliesAsync(global, Employee, new(2026, 9, 1)));
    }

    private sealed class DateAwareEmploymentResolver : IEffectiveEmploymentResolver
    {
        public Task<EffectiveEmploymentResolutionResult> ResolveAsync(Guid tenantId, Guid employeeId, DateOnly effectiveDate, CancellationToken cancellationToken = default)
        {
            var department = effectiveDate < new DateOnly(2026, 9, 1) ? Finance : It;
            var snapshot = new EffectiveEmploymentSnapshot(
                HistoryId: Guid.NewGuid(), TenantId: tenantId, EmployeeId: employeeId,
                EffectiveFrom: effectiveDate, EffectiveTo: null, HoldingCompanyId: null, LobId: null,
                OrganisationId: null, DepartmentId: department, SubDepartmentId: null, SectionId: null,
                SubSectionId: null, FunctionId: null, SubFunctionId: null, GradeId: null, DesignationId: null,
                EmployeeTypeId: null, CountryLocationId: null, WorkLocationId: null, CostCenterId: null,
                ManagerId: null, EmploymentType: EmploymentType.FullTime, EmploymentStatus: EmployeeStatus.Active,
                DateOfJoining: new(2020, 1, 1), GroupDateOfJoining: null, DateOfLeaving: null, Gender: Gender.Unspecified);
            return Task.FromResult(new EffectiveEmploymentResolutionResult(EffectiveEmploymentResolutionStatus.Resolved, tenantId, employeeId, effectiveDate, snapshot, "test"));
        }
    }
}
