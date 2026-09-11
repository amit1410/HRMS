using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceShiftApplicabilityTests
{
    [Fact]
    public async Task Department_rule_resolves_a_direct_shift()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var shift = await f.AddShiftAsync("MORNING");
        await f.AddRuleAsync(shift.Id, departmentId: f.DepartmentId);

        var result = await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 10));

        Assert.Equal(shift.Id, result.Value!.ShiftId);
        Assert.Null(result.Value.ShiftPatternId);
    }

    [Fact]
    public async Task Department_rule_resolves_actual_N_day_pattern_days()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var morning = await f.AddShiftAsync("MORNING");
        var evening = await f.AddShiftAsync("EVENING");
        var pattern = new ShiftPattern { Id = Guid.NewGuid(), TenantId = f.TenantId, Code = "PAT-A", Name = "PAT-A", CycleLengthDays = 3, EffectiveFrom = new(2026, 10, 1) };
        pattern.Days.Add(new ShiftPatternDay { Id = Guid.NewGuid(), TenantId = f.TenantId, SequenceDay = 1, ShiftId = morning.Id, DayType = ShiftPatternDayType.Shift });
        pattern.Days.Add(new ShiftPatternDay { Id = Guid.NewGuid(), TenantId = f.TenantId, SequenceDay = 2, ShiftId = evening.Id, DayType = ShiftPatternDayType.Shift });
        pattern.Days.Add(new ShiftPatternDay { Id = Guid.NewGuid(), TenantId = f.TenantId, SequenceDay = 3, DayType = ShiftPatternDayType.WeeklyOff });
        f.Context.ShiftPatterns.Add(pattern); await f.Context.SaveChangesAsync();
        await f.AddRuleAsync(null, f.DepartmentId, pattern.Id);

        var day1 = await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 1));
        var day2 = await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 2));
        var day3 = await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 3));

        Assert.Equal(morning.Id, day1.Value!.ShiftId);
        Assert.Equal(evening.Id, day2.Value!.ShiftId);
        Assert.Equal(ShiftPatternDayType.WeeklyOff, day3.Value!.PatternDayType);
        Assert.Null(day3.Value.ShiftId);
    }

    [Fact]
    public async Task Higher_priority_rule_wins()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var morning = await f.AddShiftAsync("MORNING"); var evening = await f.AddShiftAsync("EVENING");
        await f.AddRuleAsync(morning.Id, f.DepartmentId, priority: 10);
        await f.AddRuleAsync(evening.Id, f.DepartmentId, priority: 20);

        var result = await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 10));

        Assert.Equal(evening.Id, result.Value!.ShiftId);
    }

    [Fact]
    public async Task Employee_specific_rule_wins_on_specificity_at_equal_priority()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var morning = await f.AddShiftAsync("MORNING"); var evening = await f.AddShiftAsync("EVENING");
        await f.AddRuleAsync(morning.Id, workLocationId: f.WorkLocationId);
        await f.AddRuleAsync(evening.Id, employeeId: f.EmployeeId);
        var other = Guid.NewGuid(); await f.AddEmployeeAsync(other, "E002", workLocationId: f.WorkLocationId);

        var employee = await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 10));
        var otherEmployee = await f.Service.ResolveAsync(other, new(2026, 10, 10));

        Assert.True(employee.Succeeded, employee.Message);
        Assert.True(otherEmployee.Succeeded, otherEmployee.Message);
        Assert.Equal(evening.Id, employee.Value!.ShiftId);
        Assert.Equal(morning.Id, otherEmployee.Value!.ShiftId);
    }

    [Fact]
    public async Task Supported_employment_dimensions_match_and_nonmatching_employees_do_not()
    {
        var cases = new (string Name, Action<ShiftApplicabilityRule, AttendanceTestFixture> SetRule)[]
        {
            ("WorkLocation", (r, f) => r.WorkLocationId = f.WorkLocationId),
            ("EmployeeType", (r, f) => r.EmployeeTypeId = f.EmployeeTypeId),
            ("Grade", (r, f) => r.GradeId = f.GradeId),
            ("Designation", (r, f) => r.DesignationId = f.DesignationId),
            ("CostCenter", (r, f) => r.CostCenterId = f.CostCenterId),
            ("Department", (r, f) => r.DepartmentId = f.DepartmentId)
        };

        foreach (var testCase in cases)
        {
            using var f = await AttendanceTestFixture.CreateAsync();
            var shift = await f.AddShiftAsync(testCase.Name);
            var rule = new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = f.TenantId, ShiftId = shift.Id, Priority = 10, EffectiveFrom = new(2026, 1, 1) };
            testCase.SetRule(rule, f); f.Context.ShiftApplicabilityRules.Add(rule);
            await f.Context.SaveChangesAsync();

            var matched = await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 10));
            Assert.Equal(shift.Id, matched.Value!.ShiftId);
            var other = Guid.NewGuid(); await f.AddEmployeeAsync(other, $"N-{testCase.Name}");
            var nonMatching = await f.Service.ResolveAsync(other, new(2026, 10, 10));
            Assert.True(nonMatching.Succeeded, nonMatching.Message);
            Assert.Null(nonMatching.Value!.ShiftId);
        }
    }

    [Fact]
    public async Task Applicability_effective_dates_are_inclusive()
    {
        using var f = await AttendanceTestFixture.CreateAsync(); var shift = await f.AddShiftAsync("WINDOW");
        await f.AddRuleAsync(shift.Id, departmentId: f.DepartmentId, effectiveFrom: new(2026, 10, 10), effectiveTo: new(2026, 10, 20));

        Assert.Null((await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 9))).Value!.ShiftId);
        Assert.Equal(shift.Id, (await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 10))).Value!.ShiftId);
        Assert.Equal(shift.Id, (await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 20))).Value!.ShiftId);
        Assert.Null((await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 21))).Value!.ShiftId);
    }

    [Fact]
    public async Task Applicability_uses_effective_employment_on_the_requested_date()
    {
        using var f = await AttendanceTestFixture.CreateAsync();
        var morning = await f.AddShiftAsync("MORNING"); var evening = await f.AddShiftAsync("EVENING");
        var current = await f.Context.EmployeeEmploymentHistory.SingleAsync(x => x.EmployeeId == f.EmployeeId);
        current.EffectiveFrom = new(2026, 10, 1); current.EffectiveTo = new(2026, 10, 15); current.WorkLocationId = f.WorkLocationId;
        f.Context.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, EffectiveFrom = new(2026, 10, 16), WorkLocationId = f.OtherWorkLocationId, EmploymentStatus = EmployeeStatus.Active });
        await f.Context.SaveChangesAsync();
        await f.AddRuleAsync(morning.Id, workLocationId: f.WorkLocationId); await f.AddRuleAsync(evening.Id, workLocationId: f.OtherWorkLocationId);

        Assert.Equal(morning.Id, (await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 15))).Value!.ShiftId);
        Assert.Equal(evening.Id, (await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 16))).Value!.ShiftId);
    }

    [Fact]
    public async Task Equal_best_rules_return_explicit_ambiguity()
    {
        using var f = await AttendanceTestFixture.CreateAsync(); var a = await f.AddShiftAsync("A"); var b = await f.AddShiftAsync("B");
        await f.AddRuleAsync(a.Id, departmentId: f.DepartmentId); await f.AddRuleAsync(b.Id, departmentId: f.DepartmentId);

        var result = await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 10));

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("same best priority and specificity", result.Message);
    }

    [Fact]
    public async Task Tenant_rules_do_not_cross_tenant_boundaries()
    {
        using var f = await AttendanceTestFixture.CreateAsync(); var shift = await f.AddShiftAsync("TENANT-A"); await f.AddRuleAsync(shift.Id, departmentId: f.DepartmentId);
        var tenantB = Guid.NewGuid();
        f.Context.Tenants.Add(new Tenant { Id = tenantB, TenantCode = $"T-{tenantB:N}"[..20], Host = $"{tenantB:N}.test", ShardKey = tenantB.ToString("N"), TenantName = "Tenant B" });
        await f.Context.SaveChangesAsync(); f.SwitchTenant(tenantB);

        var result = await f.Service.ResolveAsync(f.EmployeeId, new(2026, 10, 10));

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
