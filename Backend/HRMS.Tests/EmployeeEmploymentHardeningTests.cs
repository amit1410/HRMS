using HRMS.Application.Common;
using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public class EmployeeEmploymentHardeningTests
{
    private static readonly Guid Tenant = SeedData.TenantIds.Demo01;
    private static readonly Guid Employee = OrganizationTestHarness.EmployeeId(Tenant, "EMP-001");
    private static readonly Guid Employee004 = OrganizationTestHarness.EmployeeId(Tenant, "EMP-004");
    private static readonly Guid Manager002 = OrganizationTestHarness.EmployeeId(Tenant, "EMP-002");
    private static readonly Guid Manager003 = OrganizationTestHarness.EmployeeId(Tenant, "EMP-003");
    private static readonly Guid Department = OrganizationTestHarness.DepartmentId(Tenant, "ENG");
    private static readonly Guid HrDepartment = OrganizationTestHarness.DepartmentId(Tenant, "HR");
    private static readonly Guid Designation = OrganizationTestHarness.DesignationId(Tenant, "SE");
    private static readonly Guid Grade = OrganizationTestHarness.GradeId(Tenant, "G1");
    private static readonly Guid Holding = OrganizationTestHarness.HoldingCompanyId(Tenant, "HC01");
    private static readonly Guid Lob = OrganizationTestHarness.LobId(Tenant, "LOB-IT");
    private static readonly Guid SubDepartment = OrganizationTestHarness.SubDepartmentId(Tenant, "SUB-PLAT");
    private static readonly Guid Section = OrganizationTestHarness.SectionId(Tenant, "SEC-CORE");
    private static readonly Guid SubSection = OrganizationTestHarness.SubSectionId(Tenant, "SS-PAY");
    private static readonly Guid Function = OrganizationTestHarness.FunctionId(Tenant, "FN-ENG");
    private static readonly Guid SubFunction = OrganizationTestHarness.SubFunctionId(Tenant, "SF-BE");
    private static readonly Guid WorkLocation = OrganizationTestHarness.WorkLocationId(Tenant, "WL-MUM");
    private static readonly Guid Country = OrganizationTestHarness.CountryId("IN");
    private static readonly Guid Reason = OrganizationTestHarness.PositionChangeReasonId(Tenant, "NEW_HIRE");
    private static readonly DateOnly Today = new(2026, 3, 4);

    [Fact]
    public async Task Invalid_hierarchy_combination_is_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var request = Request();
        request.DepartmentId = HrDepartment;

        var result = await harness.Employment().CreateChangeAsync(Employee, request, "EMP-001");

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Equal("subDepartmentId", result.Errors!.Single().Field);
    }

    [Fact]
    public async Task Invalid_holding_and_lob_combination_is_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var otherHoldingId = Guid.NewGuid();
        using (var context = harness.CreateContext())
        {
            context.HoldingCompanies.Add(new HoldingCompany
            {
                Id = otherHoldingId,
                TenantId = Tenant,
                Code = "HC02",
                Name = "Other Holdings",
                IsActive = true
            });
            await context.SaveChangesAsync();
        }

        var request = Request();
        request.HoldingCompanyId = otherHoldingId;
        var result = await harness.Employment().CreateChangeAsync(Employee, request, "EMP-001");

        Assert.False(result.Succeeded);
        Assert.Equal("lobId", result.Errors!.Single().Field);
    }

    [Fact]
    public async Task Inactive_master_values_and_invalid_country_are_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        using (var context = harness.CreateContext())
        {
            var grade = await context.Grades.SingleAsync(g => g.Id == Grade);
            grade.IsActive = false;
            await context.SaveChangesAsync();
        }

        var inactive = await harness.Employment().CreateChangeAsync(Employee, Request(), "EMP-001");
        Assert.False(inactive.Succeeded);
        Assert.Equal("gradeId", inactive.Errors!.Single().Field);

        var invalidCountry = Request();
        invalidCountry.GradeId = null;
        invalidCountry.CountryLocationId = Guid.NewGuid();
        var country = await harness.Employment().CreateChangeAsync(Employee, invalidCountry, "EMP-001");
        Assert.False(country.Succeeded);
        Assert.Equal("countryLocationId", country.Errors!.Single().Field);
    }

    [Fact]
    public async Task Work_location_requires_a_country()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var request = Request();
        request.CountryLocationId = null;

        var result = await harness.Employment().CreateChangeAsync(Employee, request, "EMP-001");

        Assert.False(result.Succeeded);
        Assert.Equal("countryLocationId", result.Errors!.Single().Field);
    }

    [Fact]
    public async Task Self_direct_and_indirect_manager_cycles_are_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var self = Request();
        self.ManagerId = Employee;
        var selfResult = await harness.Employment().CreateChangeAsync(Employee, self, "EMP-001");
        Assert.False(selfResult.Succeeded);
        Assert.Equal("managerId", selfResult.Errors!.Single().Field);

        var direct = Request();
        direct.ManagerId = Manager002;
        var directResult = await harness.Employment().CreateChangeAsync(Employee, direct, "EMP-001");
        Assert.False(directResult.Succeeded);

        var indirect = Request();
        indirect.ManagerId = Manager003;
        var indirectResult = await harness.Employment().CreateChangeAsync(Employee, indirect, "EMP-001");
        Assert.False(indirectResult.Succeeded);
    }

    [Fact]
    public async Task Supervisor_manager_ids_are_tenant_scoped_and_cannot_self_reference()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var self = await harness.Supervisors().UpsertAsync(Employee, new EmployeeSupervisorRequest
        {
            L1ManagerId = Employee
        });
        Assert.False(self.Succeeded);
        Assert.Equal("l1ManagerId", self.Errors!.Single().Field);

        var otherTenantEmployee = OrganizationTestHarness.EmployeeId(SeedData.TenantIds.Demo02, "E-100");
        var crossTenant = await harness.Supervisors().UpsertAsync(Employee, new EmployeeSupervisorRequest
        {
            L1ManagerId = otherTenantEmployee
        });
        Assert.False(crossTenant.Succeeded);
        Assert.Equal("l1ManagerId", crossTenant.Errors!.Single().Field);
    }

    [Fact]
    public async Task Supervisor_read_uses_effective_employment_manager_and_rejects_l1_divergence()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        await SeedActiveEmploymentAsync(harness, Manager002);
        var request = Request();
        request.ManagerId = Manager002;
        var employment = await harness.Employment().CreateChangeAsync(Employee004, request, "EMP-001");
        Assert.True(employment.Succeeded, employment.Message);

        var resolved = await harness.Managers().ResolveAsync(Employee004, Today);
        Assert.True(resolved.Succeeded, resolved.Message);
        Assert.True(resolved.Value!.Status == EmployeeManagerResolutionStatus.Resolved, resolved.Value.Message);
        Assert.Equal(Manager002, resolved.Value.ManagerId);

        var allowed = await harness.Supervisors().UpsertAsync(Employee004, new EmployeeSupervisorRequest
        {
            L1ManagerId = Manager002,
            L2ManagerId = Manager003
        });
        Assert.True(allowed.Succeeded, allowed.Message);

        var read = await harness.Supervisors().GetAsync(Employee004);
        Assert.True(read.Succeeded, read.Message);
        Assert.Equal(Manager002, read.Value!.L1ManagerId);
        Assert.Equal("EMP-002", read.Value.L1ManagerCode);
        Assert.Equal("Resolved", read.Value.L1ResolutionStatus);

        var divergent = await harness.Supervisors().UpsertAsync(Employee004, new EmployeeSupervisorRequest
        {
            L1ManagerId = Manager003,
            L2ManagerId = Manager002
        });
        Assert.False(divergent.Succeeded);
        Assert.Equal("l1ManagerId", divergent.Errors!.Single().Field);
    }

    [Fact]
    public async Task Resolver_uses_the_manager_effective_before_on_and_after_a_scheduled_change()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        await SeedActiveEmploymentAsync(harness, Manager002, Today.AddDays(-30), Today.AddDays(9));
        await SeedActiveEmploymentAsync(harness, Manager003, Today.AddDays(-30));
        await AddEmploymentRecordAsync(harness, Employee004, Today.AddDays(-30), Today.AddDays(9), Manager002);
        await AddEmploymentRecordAsync(harness, Employee004, Today.AddDays(10), null, Manager003);

        var before = await harness.Managers().ResolveAsync(Employee004, Today.AddDays(9));
        var on = await harness.Managers().ResolveAsync(Employee004, Today.AddDays(10));
        var after = await harness.Managers().ResolveAsync(Employee004, Today.AddDays(11));

        Assert.Equal(Manager002, before.Value!.ManagerId);
        Assert.Equal(Manager003, on.Value!.ManagerId);
        Assert.Equal(Manager003, after.Value!.ManagerId);
    }

    [Fact]
    public async Task Resolver_treats_explicit_manager_clearing_as_unassigned_without_legacy_fallback()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        await SetLegacyManagerAsync(harness, Employee004, null);
        await AddEmploymentRecordAsync(harness, Employee004, Today, null, null);

        var result = await harness.Managers().ResolveAsync(Employee004, Today);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(EmployeeManagerResolutionStatus.NoAssignedManager, result.Value!.Status);
        Assert.Null(result.Value.ManagerId);
    }

    [Fact]
    public async Task Resolver_rejects_missing_future_joining_and_retired_managers_by_date()
    {
        using var missingHarness = await OrganizationTestHarness.CreateAsync();
        await SetLegacyManagerAsync(missingHarness, Employee004, null);
        await AddEmploymentRecordAsync(missingHarness, Employee004, Today, null, Manager002);
        var missing = await missingHarness.Managers().ResolveAsync(Employee004, Today.AddDays(1));
        Assert.Equal(EmployeeManagerResolutionStatus.ManagerNotEligible, missing.Value!.Status);

        using var futureHarness = await OrganizationTestHarness.CreateAsync();
        await SetLegacyManagerAsync(futureHarness, Employee004, null);
        await AddEmploymentRecordAsync(futureHarness, Employee004, Today, null, Manager002);
        await SeedActiveEmploymentAsync(futureHarness, Manager002, Today.AddDays(5));
        var beforeJoining = await futureHarness.Managers().ResolveAsync(Employee004, Today.AddDays(4));
        Assert.Equal(EmployeeManagerResolutionStatus.ManagerNotEligible, beforeJoining.Value!.Status);

        using var retirementHarness = await OrganizationTestHarness.CreateAsync();
        await SetLegacyManagerAsync(retirementHarness, Employee004, null);
        await AddEmploymentRecordAsync(retirementHarness, Employee004, Today, null, Manager002);
        await SeedActiveEmploymentAsync(retirementHarness, Manager002, Today.AddDays(-30), Today.AddDays(4));
        var beforeRetirement = await retirementHarness.Managers().ResolveAsync(Employee004, Today.AddDays(4));
        var onRetirement = await retirementHarness.Managers().ResolveAsync(Employee004, Today.AddDays(5));
        Assert.Equal(EmployeeManagerResolutionStatus.Resolved, beforeRetirement.Value!.Status);
        Assert.Equal(EmployeeManagerResolutionStatus.ManagerNotEligible, onRetirement.Value!.Status);
    }

    [Fact]
    public async Task Resolver_detects_a_reporting_cycle_introduced_at_a_future_boundary()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        await SetLegacyManagerAsync(harness, Employee004, null);
        await SetLegacyManagerAsync(harness, Manager002, null);
        await AddEmploymentRecordAsync(harness, Employee004, Today, null, null);
        await AddEmploymentRecordAsync(harness, Manager002, Today, Today.AddDays(9), null);
        await AddEmploymentRecordAsync(harness, Manager002, Today.AddDays(10), null, Employee004);

        Assert.True(await harness.Managers().WouldCreateCycleAsync(Employee004, Manager002, Today));
    }

    [Fact]
    public async Task Resolver_keeps_today_valid_when_a_cycle_is_only_scheduled_for_a_future_date()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        await AddEmploymentRecordAsync(harness, Employee004, Today, null, Manager002);
        await SeedActiveEmploymentAsync(harness, Manager002, Today.AddDays(-30), Today.AddDays(9));
        await AddEmploymentRecordAsync(harness, Manager002, Today.AddDays(10), null, Employee004);

        var today = await harness.Managers().ResolveAsync(Employee004, Today);

        Assert.True(today.Succeeded, today.Message);
        Assert.Equal(EmployeeManagerResolutionStatus.Resolved, today.Value!.Status);
        Assert.Equal(Manager002, today.Value.ManagerId);
        Assert.True(await harness.Managers().WouldCreateCycleAsync(Employee004, Manager002, Today));
    }

    [Fact]
    public async Task Supervisor_additional_roles_update_without_changing_authoritative_l1()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        await SeedActiveEmploymentAsync(harness, Manager002);
        await SeedActiveEmploymentAsync(harness, Manager003);
        await AddEmploymentRecordAsync(harness, Employee004, Today, null, Manager002);

        var first = await harness.Supervisors().UpsertAsync(Employee004, new EmployeeSupervisorRequest
        {
            L1ManagerId = Manager002,
            L2ManagerId = Manager003
        });
        Assert.True(first.Succeeded, first.Message);

        var second = await harness.Supervisors().UpsertAsync(Employee004, new EmployeeSupervisorRequest
        {
            L1ManagerId = Manager002,
            L2ManagerId = Manager002,
            TimeManagerId = Manager003
        });
        Assert.True(second.Succeeded, second.Message);
        Assert.Equal(Manager002, second.Value!.L1ManagerId);
        Assert.Equal(Manager002, second.Value.L2ManagerId);
        Assert.Equal(Manager003, second.Value.TimeManagerId);
    }

    [Fact]
    public async Task Manager_only_employment_change_preserves_position_fields_and_employee_code()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        await SeedActiveEmploymentAsync(harness, Manager002);

        var initial = Request();
        var created = await harness.Employment().CreateChangeAsync(Employee004, initial, "EMP-001");
        Assert.True(created.Succeeded, created.Message);
        string? employeeCode;
        await using (var context = harness.CreateContext())
        {
            employeeCode = await context.Employees
                .Where(e => e.Id == Employee004)
                .Select(e => e.EmployeeCode)
                .SingleAsync();
        }

        var managerOnly = Request();
        managerOnly.EffectiveFrom = Today.AddDays(1);
        managerOnly.ChangeReason = EmploymentChangeReason.Transfer;
        managerOnly.ManagerId = Manager002;
        var changed = await harness.Employment().CreateChangeAsync(Employee004, managerOnly, "EMP-001");

        Assert.True(changed.Succeeded, changed.Message);
        await using (var context = harness.CreateContext())
        {
            var employee = await context.Employees.SingleAsync(e => e.Id == Employee004);
            Assert.Equal(employeeCode, employee.EmployeeCode);
        }
        Assert.Equal(initial.DepartmentId, changed.Value!.DepartmentId);
        Assert.Equal(initial.DesignationId, changed.Value.DesignationId);
        Assert.Equal(initial.WorkLocationId, changed.Value.WorkLocationId);
        Assert.Equal(initial.GradeId, changed.Value.GradeId);
        Assert.Equal(Manager002, changed.Value.ManagerId);
    }

    [Fact]
    public async Task Employment_populates_snapshots_and_synchronizes_current_employee_fields()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        await SeedActiveEmploymentAsync(harness, Manager002);
        var request = Request();
        request.ManagerId = Manager002;
        request.EmployeeTypeId = OrganizationTestHarness.EmployeeTypeId(Tenant, "FT");
        request.CostCenterId = OrganizationTestHarness.CostCenterId(Tenant, "CC-ENG");
        request.GradeLevel = null;

        var result = await harness.Employment().CreateChangeAsync(Employee004, request, "EMP-001");
        Assert.True(result.Succeeded, result.Message);

        var raw = await harness.CreateUnscopedContext().EmployeeEmploymentHistory
            .IgnoreQueryFilters().SingleAsync(e => e.Id == result.Value!.Id);
        Assert.Equal("Engineering", raw.DepartmentName);
        Assert.Equal("Software Engineer", raw.DesignationName);
        Assert.Equal("EMP-002", raw.ManagerCode);
        Assert.Equal("Owen Brand", raw.ManagerName);
        Assert.Equal("G1", raw.GradeLevel);

        var employee = await harness.CreateUnscopedContext().Employees
            .IgnoreQueryFilters().SingleAsync(e => e.Id == Employee004);
        Assert.Equal(Department, employee.DepartmentId);
        Assert.Equal(Designation, employee.DesignationId);
        Assert.Equal(Manager002, employee.ReportingManagerId);
        Assert.Equal(request.EmployeeTypeId, employee.EmployeeTypeId);
        Assert.Equal("Full Time", employee.EmployeeType);
        Assert.Equal("CC-ENG", employee.CostCenterCode);
        Assert.Equal(EmployeeStatus.Active, employee.Status);
    }

    private static async Task SeedActiveEmploymentAsync(
        OrganizationTestHarness harness,
        Guid employeeId,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null)
    {
        await using var context = harness.CreateContext();
        context.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            EmployeeId = employeeId,
            EffectiveFrom = effectiveFrom ?? Today.AddDays(-30),
            EffectiveTo = effectiveTo,
            EmploymentStatus = EmployeeStatus.Active,
            EmploymentType = EmploymentType.FullTime,
            ChangeReason = EmploymentChangeReason.NewJoining
        });
        await context.SaveChangesAsync();
    }

    private static async Task AddEmploymentRecordAsync(
        OrganizationTestHarness harness,
        Guid employeeId,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        Guid? managerId)
    {
        await using var context = harness.CreateContext();
        context.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            EmployeeId = employeeId,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            ManagerId = managerId,
            EmploymentStatus = EmployeeStatus.Active,
            EmploymentType = EmploymentType.FullTime,
            ChangeReason = EmploymentChangeReason.NewJoining
        });
        await context.SaveChangesAsync();
    }

    private static async Task SetLegacyManagerAsync(
        OrganizationTestHarness harness,
        Guid employeeId,
        Guid? managerId)
    {
        await using var context = harness.CreateContext();
        var employee = await context.Employees.SingleAsync(e => e.Id == employeeId);
        employee.ReportingManagerId = managerId;
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Joining_details_validate_dates_units_and_referrer_tenant_state()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var invalid = new EmployeeEmploymentRequest
        {
            FirstHiredDate = Today,
            DateOfJoining = Today.AddDays(-1),
            ProbationPeriod = 6,
            ProbationPeriodUnit = "Weeks"
        };

        var result = await harness.Employment().UpsertEmploymentAsync(Employee, invalid);

        Assert.False(result.Succeeded);
        Assert.Equal("dateOfJoining", result.Errors!.Single().Field);

        invalid.DateOfJoining = Today;
        var unitResult = await harness.Employment().UpsertEmploymentAsync(Employee, invalid);
        Assert.False(unitResult.Succeeded);
        Assert.Equal("probationPeriodUnit", unitResult.Errors!.Single().Field);

        invalid.ProbationPeriodUnit = "Months";
        invalid.ReferredByEmployeeId = Employee;
        var referrerResult = await harness.Employment().UpsertEmploymentAsync(Employee, invalid);
        Assert.False(referrerResult.Succeeded);
        Assert.Equal("referredByEmployeeId", referrerResult.Errors!.Single().Field);
    }

    [Fact]
    public async Task History_names_are_snapshot_values_after_master_renames()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var result = await harness.Employment().CreateChangeAsync(Employee004, Request(), "EMP-001");
        Assert.True(result.Succeeded, result.Message);

        using (var context = harness.CreateContext())
        {
            (await context.Departments.SingleAsync(d => d.Id == Department)).Name = "Renamed Engineering";
            (await context.Designations.SingleAsync(d => d.Id == Designation)).Name = "Renamed Engineer";
            await context.SaveChangesAsync();
        }

        var history = await harness.Employment().GetHistoryAsync(Employee004);
        Assert.True(history.Succeeded);
        var row = Assert.Single(history.Value!);
        Assert.Equal("Engineering", row.DepartmentName);
        Assert.Equal("Software Engineer", row.DesignationName);
    }

    private static EmploymentChangeRequest Request() => new()
    {
        EffectiveFrom = Today,
        HoldingCompanyId = Holding,
        LobId = Lob,
        DepartmentId = Department,
        SubDepartmentId = SubDepartment,
        SectionId = Section,
        SubSectionId = SubSection,
        FunctionId = Function,
        SubFunctionId = SubFunction,
        DesignationId = Designation,
        GradeId = Grade,
        WorkLocationId = WorkLocation,
        CountryLocationId = Country,
        PositionChangeReasonId = Reason,
        ChangeReason = EmploymentChangeReason.NewJoining,
        EmploymentType = EmploymentType.FullTime,
        EmploymentStatus = EmployeeStatus.Active
    };
}
