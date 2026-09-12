using HRMS.Application.DTOs.Attendance;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

[Collection("Attendance MySQL")]
public sealed class MySqlAttendanceApplicabilityApiIntegrationTests
{
    [Fact]
    public async Task Real_mysql_persists_and_reads_a_unique_applicability_rule()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Attendance applicability tests not executed: connection is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using var db = fixture.CreateContext();
            var shift = new Shift { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = $"API-{Guid.NewGuid():N}"[..12], ShiftName = "API", EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
            var rule = new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, RuleName = $"Rule-{Guid.NewGuid():N}"[..18], ShiftId = shift.Id, Priority = 10, EffectiveFrom = new(2026, 1, 1), WorkLocationId = Guid.NewGuid() };
            db.Shifts.Add(shift); db.ShiftApplicabilityRules.Add(rule); await db.SaveChangesAsync();
            var service = new AttendanceFoundationService(db, fixture.EmployeeTenant, new EffectiveEmploymentResolver(db, fixture.EmployeeTenant));
            var page = await service.GetApplicabilityAsync(new ShiftApplicabilityQuery { PageSize = 100 });
            var loaded = Assert.Single(page.Value!.Items, x => x.Id == rule.Id);
            Assert.Equal(rule.RuleName, loaded.RuleName);
            Assert.Equal(rule.ShiftId, loaded.ShiftId);
            Assert.Equal(rule.WorkLocationId, loaded.Conditions["WorkLocation"]);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId));
            await cleanup.ShiftApplicabilityRules.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync();
            await cleanup.Shifts.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync();
            await fixture.CleanupAttendanceAsync(); await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task Real_mysql_applicability_detail_and_rule_name_update_round_trip()
    {
        await WithFixture(async fixture =>
        {
            await using var db = fixture.CreateContext();
            var shift = new Shift { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = $"D-{Guid.NewGuid():N}"[..10], ShiftName = "Detail", EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
            var rule = new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, RuleName = "Before", ShiftId = shift.Id, Priority = 1, EffectiveFrom = new(2026, 1, 1) };
            db.Shifts.Add(shift); db.ShiftApplicabilityRules.Add(rule); await db.SaveChangesAsync();
            var service = new AttendanceFoundationService(db, fixture.EmployeeTenant, new EffectiveEmploymentResolver(db, fixture.EmployeeTenant));
            var updated = await service.UpdateApplicabilityAsync(rule.Id, new ShiftApplicabilityRequest { RuleName = "  After  ", ShiftId = shift.Id, Priority = 5, EffectiveFrom = new(2026, 2, 1) });
            Assert.True(updated.Succeeded, updated.Message);
            var detail = await service.GetApplicabilityByIdAsync(rule.Id);
            Assert.Equal("After", detail.Value!.RuleName); Assert.Equal(5, detail.Value.Priority); Assert.Equal(new DateOnly(2026, 2, 1), detail.Value.EffectiveFrom);
        });
    }

    [Fact]
    public async Task Real_mysql_applicability_delete_is_tenant_scoped()
    {
        await WithFixture(async fixture =>
        {
            await using var db = fixture.CreateContext();
            var rule = new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, RuleName = "Delete", ShiftId = null, ShiftPatternId = null, Priority = 1, EffectiveFrom = new(2026, 1, 1) };
            var shift = new Shift { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = $"X-{Guid.NewGuid():N}"[..10], ShiftName = "Delete", EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
            rule.ShiftId = shift.Id; db.Shifts.Add(shift); db.ShiftApplicabilityRules.Add(rule); await db.SaveChangesAsync();
            var service = new AttendanceFoundationService(db, fixture.EmployeeTenant, new EffectiveEmploymentResolver(db, fixture.EmployeeTenant));
            var deleted = await service.DeleteApplicabilityAsync(rule.Id);
            Assert.True(deleted.Succeeded, deleted.Message); Assert.Null(await db.ShiftApplicabilityRules.FindAsync(rule.Id));
        });
    }

    [Fact]
    public async Task Real_mysql_applicability_equal_best_rules_return_explicit_ambiguity()
    {
        await WithFixture(async fixture =>
        {
            await using var db = fixture.CreateContext();
            var a = new Shift { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = $"A-{Guid.NewGuid():N}"[..10], ShiftName = "A", EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
            var b = new Shift { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = $"B-{Guid.NewGuid():N}"[..10], ShiftName = "B", EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
            db.Shifts.AddRange(a, b);
            db.ShiftApplicabilityRules.AddRange(new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, RuleName = "A", ShiftId = a.Id, EmployeeId = fixture.EmployeeId, Priority = 10, EffectiveFrom = new(2026, 1, 1) }, new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, RuleName = "B", ShiftId = b.Id, EmployeeId = fixture.EmployeeId, Priority = 10, EffectiveFrom = new(2026, 1, 1) });
            await db.SaveChangesAsync();
            var service = new AttendanceFoundationService(db, fixture.EmployeeTenant, new EffectiveEmploymentResolver(db, fixture.EmployeeTenant));
            var result = await service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 1));
            Assert.False(result.Succeeded); Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, result.Status);
        });
    }

    [Fact]
    public async Task Real_mysql_applicability_tenant_crud_isolation_preserves_other_tenant_rule()
    {
        await WithFixture(async fixture =>
        {
            await using var setup = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId));
            var shift = new Shift { Id = Guid.NewGuid(), TenantId = fixture.OtherTenantId, ShiftCode = $"B-{Guid.NewGuid():N}"[..10], ShiftName = "B", EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
            var rule = new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.OtherTenantId, RuleName = "Tenant B Rule", ShiftId = shift.Id, Priority = 7, EffectiveFrom = new(2026, 1, 1) };
            setup.Shifts.Add(shift); setup.ShiftApplicabilityRules.Add(rule); await setup.SaveChangesAsync();
            await using var tenantA = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            var serviceA = new AttendanceFoundationService(tenantA, fixture.EmployeeTenant, new EffectiveEmploymentResolver(tenantA, fixture.EmployeeTenant));
            Assert.DoesNotContain((await serviceA.GetApplicabilityAsync(new ShiftApplicabilityQuery { PageSize = 100 })).Value!.Items, x => x.Id == rule.Id);
            Assert.False((await serviceA.GetApplicabilityByIdAsync(rule.Id)).Succeeded);
            Assert.False((await serviceA.UpdateApplicabilityAsync(rule.Id, new ShiftApplicabilityRequest { RuleName = "Hacked", ShiftId = shift.Id, Priority = 999, EffectiveFrom = new(2026, 1, 1) })).Succeeded);
            Assert.False((await serviceA.DeleteApplicabilityAsync(rule.Id)).Succeeded);
            await using var tenantB = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId));
            var serviceB = new AttendanceFoundationService(tenantB, new TestTenantContext(fixture.OtherTenantId), new EffectiveEmploymentResolver(tenantB, new TestTenantContext(fixture.OtherTenantId)));
            var loaded = Assert.Single((await serviceB.GetApplicabilityAsync(new ShiftApplicabilityQuery { PageSize = 100 })).Value!.Items, x => x.Id == rule.Id);
            Assert.Equal("Tenant B Rule", loaded.RuleName); Assert.Equal(7, loaded.Priority);
        });
    }

    [Fact]
    public async Task Real_mysql_applicability_condition_mutation_round_trip()
    {
        await WithFixture(async fixture =>
        {
            await using var db = fixture.CreateContext();
            var shift = new Shift { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = $"C-{Guid.NewGuid():N}"[..10], ShiftName = "Condition", EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
            var rule = new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, RuleName = "Conditions", ShiftId = shift.Id, DepartmentId = Guid.NewGuid(), Priority = 1, EffectiveFrom = new(2026, 1, 1) };
            db.Shifts.Add(shift); db.ShiftApplicabilityRules.Add(rule); await db.SaveChangesAsync();
            var service = new AttendanceFoundationService(db, fixture.EmployeeTenant, new EffectiveEmploymentResolver(db, fixture.EmployeeTenant));
            var update = await service.UpdateApplicabilityAsync(rule.Id, new ShiftApplicabilityRequest { RuleName = "Conditions", ShiftId = shift.Id, GradeId = Guid.NewGuid(), Priority = 1, EffectiveFrom = new(2026, 1, 1) });
            Assert.True(update.Succeeded, update.Message);
            var loaded = await service.GetApplicabilityByIdAsync(rule.Id);
            Assert.Null(loaded.Value!.Conditions["Department"]); Assert.NotNull(loaded.Value.Conditions["Grade"]);
        });
    }

    [Fact]
    public async Task Real_mysql_applicability_target_switching_clears_stale_target()
    {
        await WithFixture(async fixture =>
        {
            await using var db = fixture.CreateContext();
            var shift = new Shift { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = $"T-{Guid.NewGuid():N}"[..10], ShiftName = "Target", EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
            var pattern = new ShiftPattern { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Code = $"P-{Guid.NewGuid():N}"[..10], Name = "Pattern", CycleLengthDays = 1, EffectiveFrom = new(2026, 1, 1), IsActive = true };
            pattern.Days.Add(new ShiftPatternDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftPatternId = pattern.Id, SequenceDay = 1, ShiftId = shift.Id, DayType = ShiftPatternDayType.Shift });
            var rule = new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, RuleName = "Target switching", ShiftId = shift.Id, Priority = 1, EffectiveFrom = new(2026, 1, 1) };
            db.Shifts.Add(shift); db.ShiftPatterns.Add(pattern); db.ShiftApplicabilityRules.Add(rule); await db.SaveChangesAsync();
            var service = new AttendanceFoundationService(db, fixture.EmployeeTenant, new EffectiveEmploymentResolver(db, fixture.EmployeeTenant));

            var toPattern = await service.UpdateApplicabilityAsync(rule.Id, new ShiftApplicabilityRequest { RuleName = rule.RuleName, ShiftPatternId = pattern.Id, Priority = rule.Priority, EffectiveFrom = rule.EffectiveFrom });
            Assert.True(toPattern.Succeeded, toPattern.Message);
            var patternState = await service.GetApplicabilityByIdAsync(rule.Id);
            Assert.Equal(pattern.Id, patternState.Value!.ShiftPatternId); Assert.Null(patternState.Value.ShiftId);

            var toShift = await service.UpdateApplicabilityAsync(rule.Id, new ShiftApplicabilityRequest { RuleName = rule.RuleName, ShiftId = shift.Id, Priority = rule.Priority, EffectiveFrom = rule.EffectiveFrom });
            Assert.True(toShift.Succeeded, toShift.Message);
            var shiftState = await service.GetApplicabilityByIdAsync(rule.Id);
            Assert.Equal(shift.Id, shiftState.Value!.ShiftId); Assert.Null(shiftState.Value.ShiftPatternId);
        });
    }

    private static async Task WithFixture(Func<MySqlLeaveLifecycleIntegrationTests.Fixture, Task> action)
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Attendance applicability tests not executed: connection is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try { await fixture.SeedAsync(); await action(fixture); }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext(fixture.OtherTenantId));
            await cleanup.ShiftApplicabilityRules.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync();
            await cleanup.Shifts.Where(x => x.TenantId == fixture.OtherTenantId).ExecuteDeleteAsync();
            await fixture.CleanupAttendanceAsync(); await fixture.CleanupAsync();
        }
    }
}
