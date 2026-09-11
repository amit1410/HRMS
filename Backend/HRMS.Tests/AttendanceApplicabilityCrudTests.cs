using HRMS.Application.Common;
using HRMS.Application.DTOs.Attendance;

namespace HRMS.Tests;

public sealed class AttendanceApplicabilityCrudTests
{
    [Fact]
    public async Task Create_list_detail_and_update_preserve_rule_name_and_target()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("MORNING");
        var request = new ShiftApplicabilityRequest
        {
            RuleName = "  Noida rule  ", ShiftId = shift.Id, DepartmentId = fixture.DepartmentId,
            Priority = 10, EffectiveFrom = new(2026, 1, 1)
        };

        var created = await fixture.Service.AddApplicabilityAsync(request);
        Assert.True(created.Succeeded, created.Message);
        var listed = await fixture.Service.GetApplicabilityAsync(new());
        var rule = Assert.Single(listed.Value!.Items);

        Assert.Equal("Noida rule", rule.RuleName);
        Assert.Equal(shift.Id, rule.ShiftId);
        Assert.Equal(fixture.DepartmentId, rule.Conditions["Department"]);

        var update = new ShiftApplicabilityRequest
        {
            RuleName = "  Updated rule ", ShiftId = null, DepartmentId = fixture.DepartmentId,
            Priority = 20, EffectiveFrom = new(2026, 1, 1)
        };
        var pattern = new HRMS.Domain.Entities.ShiftPattern { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Code = "PAT-CRUD", Name = "PAT-CRUD", CycleLengthDays = 1, EffectiveFrom = new(2026, 1, 1) };
        pattern.Days.Add(new HRMS.Domain.Entities.ShiftPatternDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, SequenceDay = 1, ShiftId = shift.Id, DayType = HRMS.Domain.Enums.ShiftPatternDayType.Shift });
        fixture.Context.ShiftPatterns.Add(pattern);
        await fixture.Context.SaveChangesAsync();
        update.ShiftPatternId = pattern.Id;

        var updated = await fixture.Service.UpdateApplicabilityAsync(rule.Id, update);
        Assert.True(updated.Succeeded, updated.Message);
        var detail = await fixture.Service.GetApplicabilityByIdAsync(rule.Id);
        Assert.Equal("Updated rule", detail.Value!.RuleName);
        Assert.Null(detail.Value.ShiftId);
        Assert.Equal(pattern.Id, detail.Value.ShiftPatternId);
        Assert.Equal(20, detail.Value.Priority);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rule_name_is_required(string? name)
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("SHIFT");
        var result = await fixture.Service.AddApplicabilityAsync(new() { RuleName = name!, ShiftId = shift.Id, EffectiveFrom = new(2026, 1, 1) });
        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task Delete_removes_the_rule_within_the_tenant_scope()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("SHIFT");
        await fixture.AddRuleAsync(shift.Id, ruleName: "To delete");
        var rule = Assert.Single((await fixture.Service.GetApplicabilityAsync(new())).Value!.Items);

        var deleted = await fixture.Service.DeleteApplicabilityAsync(rule.Id);
        var detail = await fixture.Service.GetApplicabilityByIdAsync(rule.Id);

        Assert.True(deleted.Succeeded);
        Assert.False(detail.Succeeded);
        Assert.Equal(ResultStatus.NotFound, detail.Status);
    }
}
