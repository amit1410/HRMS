using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlAttendanceShiftApplicabilityIntegrationTests
{
    [Fact]
    public async Task Real_mysql_proves_default_and_applicability_resolution_scenarios()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("MySQL Attendance Shift/Applicability tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using var setup = fixture.CreateContext();
            var department = new Department { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Code = $"IT{fixture.TenantId:N}"[..8], Name = "IT" };
            var noida = new WorkLocation { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Code = $"N{fixture.TenantId:N}"[..8], Name = "Noida" };
            var gurgaon = new WorkLocation { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Code = $"G{fixture.TenantId:N}"[..8], Name = "Gurgaon" };
            setup.Departments.Add(department); setup.WorkLocations.AddRange(noida, gurgaon);
            var oldHistory = await setup.EmployeeEmploymentHistory.SingleAsync(x => x.Id == fixture.EmployeeHistoryId);
            oldHistory.EffectiveFrom = new(2026, 10, 1); oldHistory.EffectiveTo = new(2026, 10, 15); oldHistory.DepartmentId = department.Id; oldHistory.WorkLocationId = noida.Id;
            setup.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2026, 10, 16), WorkLocationId = gurgaon.Id, EmploymentStatus = EmployeeStatus.Active });
            await setup.SaveChangesAsync();

            var morning = await AddShiftAsync(setup, fixture.TenantId, "MORNING", false, true);
            var evening = await AddShiftAsync(setup, fixture.TenantId, "EVENING", false, true);
            var defaultShift = await AddShiftAsync(setup, fixture.TenantId, "DEFAULT", true, true);
            await using var db = fixture.CreateContext();
            var service = new AttendanceFoundationService(db, fixture.EmployeeTenant, new EffectiveEmploymentResolver(db, fixture.EmployeeTenant));

            var fallback = await service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 10));
            Assert.Equal(defaultShift.Id, fallback.Value!.ShiftId); // valid default fallback

            var inactive = await AddShiftAsync(db, fixture.TenantId, "INACTIVE", true, false);
            var expired = await AddShiftAsync(db, fixture.TenantId, "EXPIRED", true, true, new(2026, 1, 1), new(2026, 9, 30));
            var excluded = await service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 10));
            Assert.Equal(defaultShift.Id, excluded.Value!.ShiftId); // inactive/expired excluded
            Assert.NotEqual(inactive.Id, excluded.Value.ShiftId); Assert.NotEqual(expired.Id, excluded.Value.ShiftId);

            db.Shifts.Add(new Shift { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = "CORRUPT", ShiftName = "CORRUPT", IsDefault = true, IsActive = true, EffectiveFrom = new(2026, 10, 1), EffectiveTo = new(2026, 10, 31), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540 });
            await db.SaveChangesAsync();
            var ambiguousDefault = await service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 10));
            Assert.False(ambiguousDefault.Succeeded);
            Assert.Contains("Multiple active default", ambiguousDefault.Message);

            // Applicability tests use a date outside the corrupt-default range where necessary.
            db.ShiftApplicabilityRules.AddRange(
                new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftId = morning.Id, DepartmentId = department.Id, Priority = 10, EffectiveFrom = new(2026, 1, 1) },
                new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftId = evening.Id, WorkLocationId = gurgaon.Id, Priority = 20, EffectiveFrom = new(2026, 1, 1) },
                new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftId = evening.Id, EmployeeId = fixture.EmployeeId, Priority = 30, EffectiveFrom = new(2026, 10, 1), EffectiveTo = new(2026, 10, 14) });
            await db.SaveChangesAsync();
            var direct = await service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 15));
            var specific = await service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 10));
            var transfer = await service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 16));
            Assert.Equal(morning.Id, direct.Value!.ShiftId); // direct Shift applicability
            Assert.Equal(evening.Id, specific.Value!.ShiftId); // employee specificity
            Assert.Equal(evening.Id, transfer.Value!.ShiftId); // effective employment transfer and priority

            var pattern = new ShiftPattern { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Code = $"PAT{fixture.TenantId:N}"[..8], Name = "Pattern", CycleLengthDays = 2, EffectiveFrom = new(2026, 11, 1) };
            pattern.Days.Add(new ShiftPatternDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, SequenceDay = 1, ShiftId = morning.Id, DayType = ShiftPatternDayType.Shift });
            pattern.Days.Add(new ShiftPatternDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, SequenceDay = 2, ShiftId = evening.Id, DayType = ShiftPatternDayType.Shift });
            db.ShiftPatterns.Add(pattern);
            db.ShiftApplicabilityRules.Add(new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftPatternId = pattern.Id, EmployeeId = fixture.EmployeeId, Priority = 100, EffectiveFrom = new(2026, 11, 1) });
            await db.SaveChangesAsync();
            Assert.Equal(morning.Id, (await service.ResolveAsync(fixture.EmployeeId, new(2026, 11, 1))).Value!.ShiftId); // Pattern day 1
            Assert.Equal(evening.Id, (await service.ResolveAsync(fixture.EmployeeId, new(2026, 11, 2))).Value!.ShiftId); // Pattern day 2

            db.ShiftApplicabilityRules.AddRange(
                new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftId = morning.Id, EmployeeId = fixture.EmployeeId, Priority = 200, EffectiveFrom = new(2026, 12, 1) },
                new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftId = evening.Id, EmployeeId = fixture.EmployeeId, Priority = 200, EffectiveFrom = new(2026, 12, 1) });
            await db.SaveChangesAsync();
            var ambiguousRule = await service.ResolveAsync(fixture.EmployeeId, new(2026, 12, 15));
            Assert.False(ambiguousRule.Succeeded); // equal-best applicability ambiguity
            Assert.Contains("same best priority and specificity", ambiguousRule.Message);

            var tenantB = new TestTenantContext(fixture.OtherTenantId);
            await using var other = fixture.CreateContext(tenantB);
            var crossTenant = await new AttendanceFoundationService(other, tenantB, new EffectiveEmploymentResolver(other, tenantB)).ResolveAsync(fixture.EmployeeId, new(2026, 10, 15));
            Assert.False(crossTenant.Succeeded); // tenant isolation
        }
        finally
        {
            await fixture.CleanupAttendanceAsync();
            await fixture.CleanupAsync();
        }
    }

    private static async Task<Shift> AddShiftAsync(HrmsDbContext db, Guid tenantId, string code, bool isDefault, bool isActive, DateOnly? from = null, DateOnly? to = null)
    {
        var shift = new Shift { Id = Guid.NewGuid(), TenantId = tenantId, ShiftCode = code, ShiftName = code, IsDefault = isDefault, IsActive = isActive, EffectiveFrom = from ?? new(2026, 1, 1), EffectiveTo = to, StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
        db.Shifts.Add(shift); await db.SaveChangesAsync(); return shift;
    }
}
