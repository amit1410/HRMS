using HRMS.Application.Common;
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
    public async Task Employment_populates_snapshots_and_synchronizes_current_employee_fields()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
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
