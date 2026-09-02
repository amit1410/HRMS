using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// End-to-end proof of the effective-dated employment history flow: New Hire, then promotion /
/// department / location changes, each closing the previous open record and opening a new one behind a
/// single atomic save. Every step reads the raw rows through an unscoped context so the "what really got
/// stored" part is asserted, not assumed.
///
/// The employment section is append-only by design: there is no edit and no delete, only new transactions.
/// </summary>
public class EmployeeEmploymentEndToEndTests
{
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid EmployeeId = OrganizationTestHarness.EmployeeId(Demo01, "EMP-001");

    // Masters
    private static readonly Guid DeptEng = OrganizationTestHarness.DepartmentId(Demo01, "ENG");
    private static readonly Guid DeptHr = OrganizationTestHarness.DepartmentId(Demo01, "HR");
    private static readonly Guid DesigSe = OrganizationTestHarness.DesignationId(Demo01, "SE");
    private static readonly Guid DesigSse = OrganizationTestHarness.DesignationId(Demo01, "SSE");
    private static readonly Guid GradeG1 = OrganizationTestHarness.GradeId(Demo01, "G1");
    private static readonly Guid GradeG2 = OrganizationTestHarness.GradeId(Demo01, "G2");
    private static readonly Guid HoldingCo = OrganizationTestHarness.HoldingCompanyId(Demo01, "HC01");
    private static readonly Guid Lob = OrganizationTestHarness.LobId(Demo01, "LOB-IT");
    private static readonly Guid Organisation = OrganizationTestHarness.OrganisationId(Demo01, "ORG01");
    private static readonly Guid SubDept = OrganizationTestHarness.SubDepartmentId(Demo01, "SUB-PLAT");
    private static readonly Guid Section = OrganizationTestHarness.SectionId(Demo01, "SEC-CORE");
    private static readonly Guid SubSection = OrganizationTestHarness.SubSectionId(Demo01, "SS-PAY");
    private static readonly Guid Function = OrganizationTestHarness.FunctionId(Demo01, "FN-ENG");
    private static readonly Guid SubFunction = OrganizationTestHarness.SubFunctionId(Demo01, "SF-BE");
    private static readonly Guid WorkLocMum = OrganizationTestHarness.WorkLocationId(Demo01, "WL-MUM");
    private static readonly Guid WorkLocBlr = OrganizationTestHarness.WorkLocationId(Demo01, "WL-BLR");
    private static readonly Guid CountryIn = OrganizationTestHarness.CountryId("IN");

    // Position change reasons
    private static readonly Guid ReasonNewHire = OrganizationTestHarness.PositionChangeReasonId(Demo01, "NEW_HIRE");
    private static readonly Guid ReasonPromo = OrganizationTestHarness.PositionChangeReasonId(Demo01, "PROMO");
    private static readonly Guid ReasonDept = OrganizationTestHarness.PositionChangeReasonId(Demo01, "DEPT_CHG");
    private static readonly Guid ReasonLoc = OrganizationTestHarness.PositionChangeReasonId(Demo01, "LOC_CHG");
    private static readonly Guid ReasonRetire = OrganizationTestHarness.PositionChangeReasonId(Demo01, "RETIRE");

    // The backdated-guard reads real wall-clock UTC (not the harness clock), so base dates on real "today".
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task New_hire_creates_the_first_open_record_and_syncs_the_employee_row()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var result = await harness.Employment().CreateChangeAsync(EmployeeId, NewHireRequest(), "EMP-001");
        Assert.True(result.Succeeded, result.Message);

        var dto = result.Value!;
        Assert.Equal(EmployeeId, dto.EmployeeId);
        Assert.Equal(Today, dto.EffectiveFrom);
        Assert.Null(dto.EffectiveTo);
        Assert.Equal(DeptEng, dto.DepartmentId);
        Assert.Equal("Engineering", dto.DepartmentName);       // code+name come from the master, not free text
        Assert.Equal(DesigSe, dto.DesignationId);
        Assert.Equal("Software Engineer", dto.DesignationName);
        Assert.Equal(GradeG1, dto.GradeId);
        Assert.Equal("Grade 1", dto.GradeName);
        Assert.Equal(WorkLocMum, dto.WorkLocationId);
        Assert.Equal("Mumbai Office", dto.WorkLocationName);
        Assert.Equal(CountryIn, dto.CountryLocationId);
        Assert.Equal(ReasonNewHire, dto.PositionChangeReasonId);
        Assert.Equal("New Hire", dto.PositionChangeReasonName);
        Assert.Equal(EmploymentChangeReason.NewJoining, dto.ChangeReason);
        Assert.Equal(EmploymentType.FullTime, dto.EmploymentType);
        Assert.Equal(EmployeeStatus.Active, dto.EmploymentStatus);
        Assert.True(dto.EffectiveTo is null);

        // The raw row holds every FK to the masters, with an open (null) end date.
        var raw = await LoadRawAsync(harness, dto.Id);
        Assert.Equal(Today, raw.EffectiveFrom);
        Assert.Null(raw.EffectiveTo);
        Assert.Equal(DeptEng, raw.DepartmentId);
        Assert.Equal(DesigSe, raw.DesignationId);
        Assert.Equal(WorkLocMum, raw.WorkLocationId);
        Assert.Equal(ReasonNewHire, raw.PositionChangeReasonId);

        // The employee's denormalized department/designation reflect the new hire.
        var employee = await harness.CreateUnscopedContext().Employees.IgnoreQueryFilters()
            .SingleAsync(e => e.Id == EmployeeId);
        Assert.Equal(DeptEng, employee.DepartmentId);
        Assert.Equal(DesigSe, employee.DesignationId);

        // History has exactly one record and it is the current one.
        var history = await harness.Employment().GetHistoryAsync(EmployeeId);
        Assert.True(history.Succeeded);
        var single = Assert.Single(history.Value!);
        Assert.Equal(dto.Id, single.Id);

        var current = await harness.Employment().GetCurrentAsync(EmployeeId);
        Assert.True(current.Succeeded);
        Assert.Equal(dto.Id, current.Value!.Id);
    }

    [Fact]
    public async Task A_promotion_closes_the_previous_record_and_opens_a_new_one_atomically()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var hire = await harness.Employment().CreateChangeAsync(EmployeeId, NewHireRequest(), "EMP-001");
        Assert.True(hire.Succeeded, hire.Message);

        var promoDate = Today.AddDays(30);
        var promo = await harness.Employment().CreateChangeAsync(EmployeeId, PromotionRequest(promoDate), "EMP-001");
        Assert.True(promo.Succeeded, promo.Message);

        // Atomic close-new: the hired record now ends the day before the promotion starts.
        var closed = await LoadRawAsync(harness, hire.Value!.Id);
        Assert.Equal(promoDate.AddDays(-1), closed.EffectiveTo);

        var opened = await LoadRawAsync(harness, promo.Value!.Id);
        Assert.Equal(promoDate, opened.EffectiveFrom);
        Assert.Null(opened.EffectiveTo);
        Assert.Equal(DesigSse, opened.DesignationId);
        Assert.Equal(GradeG2, opened.GradeId);
        Assert.Equal(ReasonPromo, opened.PositionChangeReasonId);

        // History is ordered most-recent-first.
        var history = await harness.Employment().GetHistoryAsync(EmployeeId);
        Assert.True(history.Succeeded);
        Assert.Equal(2, history.Value!.Count);
        Assert.Equal(promo.Value.Id, history.Value[0].Id);
        Assert.Equal(hire.Value.Id, history.Value[1].Id);

        // Current is the new role.
        var current = await harness.Employment().GetCurrentAsync(EmployeeId);
        Assert.True(current.Succeeded);
        Assert.Equal(promo.Value.Id, current.Value!.Id);

        // Employee row now points at the promoted designation.
        var employee = await harness.CreateUnscopedContext().Employees.IgnoreQueryFilters()
            .SingleAsync(e => e.Id == EmployeeId);
        Assert.Equal(DesigSse, employee.DesignationId);
    }

    [Fact]
    public async Task Multiple_changes_chain_the_effective_periods_without_overlaps_or_gaps()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var hire = await harness.Employment().CreateChangeAsync(EmployeeId, NewHireRequest(), "EMP-001");
        Assert.True(hire.Succeeded, hire.Message);

        var deptDate = Today.AddDays(45);
        var deptChange = await harness.Employment().CreateChangeAsync(EmployeeId, DeptChangeRequest(deptDate), "EMP-001");
        Assert.True(deptChange.Succeeded, deptChange.Message);

        var locDate = Today.AddDays(90);
        var locChange = await harness.Employment().CreateChangeAsync(EmployeeId, LocationChangeRequest(locDate), "EMP-001");
        Assert.True(locChange.Succeeded, locChange.Message);

        // Effective periods chain: hire [today .. dept-1], dept [deptDate .. loc-1], loc [locDate .. open].
        var hireRaw = await LoadRawAsync(harness, hire.Value!.Id);
        Assert.Equal(deptDate.AddDays(-1), hireRaw.EffectiveTo);

        var deptRaw = await LoadRawAsync(harness, deptChange.Value!.Id);
        Assert.Equal(deptDate, deptRaw.EffectiveFrom);
        Assert.Equal(locDate.AddDays(-1), deptRaw.EffectiveTo);
        Assert.Equal(DeptHr, deptRaw.DepartmentId);

        var locRaw = await LoadRawAsync(harness, locChange.Value!.Id);
        Assert.Equal(locDate, locRaw.EffectiveFrom);
        Assert.Null(locRaw.EffectiveTo);
        Assert.Equal(WorkLocBlr, locRaw.WorkLocationId);

        // History is DESC by effective date.
        var history = await harness.Employment().GetHistoryAsync(EmployeeId);
        Assert.True(history.Succeeded);
        Assert.Equal(3, history.Value!.Count);
        Assert.Equal(locChange.Value.Id, history.Value[0].Id);
        Assert.Equal(deptChange.Value.Id, history.Value[1].Id);
        Assert.Equal(hire.Value.Id, history.Value[2].Id);

        // Denormalized employee fields track the current (most recent) record.
        var employee = await harness.CreateUnscopedContext().Employees.IgnoreQueryFilters()
            .SingleAsync(e => e.Id == EmployeeId);
        Assert.Equal(DeptHr, employee.DepartmentId);
        Assert.Equal(DesigSe, employee.DesignationId);
    }

    [Fact]
    public async Task Retirement_closes_the_current_record_and_marks_the_employee_inactive()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var hire = await harness.Employment().CreateChangeAsync(EmployeeId, NewHireRequest(), "EMP-001");
        Assert.True(hire.Succeeded, hire.Message);

        var retireDate = Today.AddDays(120);
        var retire = await harness.Employment().CreateChangeAsync(EmployeeId, RetirementRequest(retireDate), "EMP-001");
        Assert.True(retire.Succeeded, retire.Message);

        var retireRaw = await LoadRawAsync(harness, retire.Value!.Id);
        Assert.Equal(retireDate, retireRaw.EffectiveFrom);
        Assert.Null(retireRaw.EffectiveTo);
        Assert.Equal(EmployeeStatus.Resigned, retireRaw.EmploymentStatus);
        Assert.Equal(ReasonRetire, retireRaw.PositionChangeReasonId);

        Assert.Equal(retireDate.AddDays(-1), (await LoadRawAsync(harness, hire.Value!.Id)).EffectiveTo);
    }

    [Fact]
    public async Task An_overlapping_effective_date_is_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        await harness.Employment().CreateChangeAsync(EmployeeId, NewHireRequest(), "EMP-001");

        // The open record starts today; adding a second record effective today (or within the open period)
        // overlaps and must be refused.
        var overlapping = NewHireRequest();
        overlapping.EffectiveFrom = Today;
        var result = await harness.Employment().CreateChangeAsync(EmployeeId, overlapping, "EMP-001");
        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task A_past_effective_date_is_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var request = NewHireRequest();
        request.EffectiveFrom = Today.AddDays(-1);
        var result = await harness.Employment().CreateChangeAsync(EmployeeId, request, "EMP-001");
        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task A_master_reference_that_does_not_exist_is_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var request = NewHireRequest();
        request.DesignationId = Guid.NewGuid();
        var result = await harness.Employment().CreateChangeAsync(EmployeeId, request, "EMP-001");
        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.NotNull(result.Errors?.FirstOrDefault(e => e.Field == "designationId"));
    }

    [Fact]
    public async Task History_is_isolated_across_tenants()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        await harness.Employment().CreateChangeAsync(EmployeeId, NewHireRequest(), "EMP-001");

        // From another tenant the employee does not exist, so its history and any change are refused.
        harness.ActAs(SeedData.TenantIds.Demo02);
        var history = await harness.Employment().GetHistoryAsync(EmployeeId);
        Assert.False(history.Succeeded);
        Assert.Equal(ResultStatus.NotFound, history.Status);

        var change = await harness.Employment().CreateChangeAsync(EmployeeId, NewHireRequest(), "EMP-001");
        Assert.False(change.Succeeded);
        Assert.Equal(ResultStatus.NotFound, change.Status);
    }

    private static EmploymentChangeRequest NewHireRequest() => new()
    {
        EffectiveFrom = Today,
        DepartmentId = DeptEng,
        DesignationId = DesigSe,
        GradeId = GradeG1,
        HoldingCompanyId = HoldingCo,
        LobId = Lob,
        OrganisationId = Organisation,
        SubDepartmentId = SubDept,
        SectionId = Section,
        SubSectionId = SubSection,
        FunctionId = Function,
        SubFunctionId = SubFunction,
        WorkLocationId = WorkLocMum,
        CountryLocationId = CountryIn,
        PositionChangeReasonId = ReasonNewHire,
        ChangeReason = EmploymentChangeReason.NewJoining,
        EmploymentType = EmploymentType.FullTime,
        EmploymentStatus = EmployeeStatus.Active,
        BusinessRole = "Individual Contributor",
        GradeLevel = "G1",
        CareerGroup = "Engineering"
    };

    private static EmploymentChangeRequest PromotionRequest(DateOnly effective) => new()
    {
        EffectiveFrom = effective,
        DepartmentId = DeptEng,
        DesignationId = DesigSse,
        GradeId = GradeG2,
        WorkLocationId = WorkLocMum,
        CountryLocationId = CountryIn,
        PositionChangeReasonId = ReasonPromo,
        ChangeReason = EmploymentChangeReason.Promotion,
        EmploymentType = EmploymentType.FullTime,
        EmploymentStatus = EmployeeStatus.Active,
        GradeLevel = "G2"
    };

    private static EmploymentChangeRequest DeptChangeRequest(DateOnly effective) => new()
    {
        EffectiveFrom = effective,
        DepartmentId = DeptHr,
        DesignationId = DesigSe,
        GradeId = GradeG1,
        WorkLocationId = WorkLocMum,
        CountryLocationId = CountryIn,
        PositionChangeReasonId = ReasonDept,
        ChangeReason = EmploymentChangeReason.DepartmentChange,
        EmploymentType = EmploymentType.FullTime,
        EmploymentStatus = EmployeeStatus.Active
    };

    private static EmploymentChangeRequest LocationChangeRequest(DateOnly effective) => new()
    {
        EffectiveFrom = effective,
        DepartmentId = DeptHr,
        DesignationId = DesigSe,
        GradeId = GradeG1,
        WorkLocationId = WorkLocBlr,
        CountryLocationId = CountryIn,
        PositionChangeReasonId = ReasonLoc,
        ChangeReason = EmploymentChangeReason.LocationChange,
        EmploymentType = EmploymentType.FullTime,
        EmploymentStatus = EmployeeStatus.Active
    };

    private static EmploymentChangeRequest RetirementRequest(DateOnly effective) => new()
    {
        EffectiveFrom = effective,
        DepartmentId = DeptHr,
        DesignationId = DesigSe,
        GradeId = GradeG1,
        WorkLocationId = WorkLocBlr,
        CountryLocationId = CountryIn,
        PositionChangeReasonId = ReasonRetire,
        ChangeReason = EmploymentChangeReason.Other,
        EmploymentType = EmploymentType.FullTime,
        EmploymentStatus = EmployeeStatus.Resigned
    };

    private static async Task<EmployeeEmploymentHistory> LoadRawAsync(OrganizationTestHarness harness, Guid id)
    {
        var context = harness.CreateUnscopedContext();
        return await context.EmployeeEmploymentHistory.IgnoreQueryFilters()
            .Include(e => e.Department)
            .Include(e => e.Designation)
            .Include(e => e.Grade)
            .Include(e => e.WorkLocation)
            .Include(e => e.PositionChangeReason)
            .SingleAsync(e => e.Id == id);
    }
}
