using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class WorkingDayCalendarResolverTests
{
    [Fact]
    public async Task Excludes_weekly_offs_and_overlapping_holiday_once_with_holiday_precedence()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        await SeedEmployeeAsync(database, tenant, employee);
        await using (var seed = database.CreateContext(new TestTenantContext(tenant)))
        {
            seed.Holidays.Add(new Holiday { Id = Guid.NewGuid(), TenantId = tenant, Name = "Founders Day", Date = new(2026, 10, 4) });
            var weekly = new WeeklyOffConfiguration { Id = Guid.NewGuid(), TenantId = tenant, EffectiveFrom = new(2026, 1, 1) };
            weekly.Days.Add(new WeeklyOffDay { Id = Guid.NewGuid(), TenantId = tenant, WeeklyOffConfigurationId = weekly.Id, DayOfWeek = DayOfWeek.Saturday });
            weekly.Days.Add(new WeeklyOffDay { Id = Guid.NewGuid(), TenantId = tenant, WeeklyOffConfigurationId = weekly.Id, DayOfWeek = DayOfWeek.Sunday });
            seed.WeeklyOffConfigurations.Add(weekly);
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext(new TestTenantContext(tenant));
        var result = await new WorkingDayCalendarResolver(context, new TestTenantContext(tenant), new EffectiveEmploymentResolver(context, new TestTenantContext(tenant)))
            .CalculateAsync(employee, new(2026, 10, 2), new(2026, 10, 5));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(4, result.Value!.TotalCalendarDays);
        Assert.Equal(2, result.Value.WorkingLeaveDays);
        Assert.Equal(1, result.Value.ExcludedHolidayCount);
        Assert.Equal(1, result.Value.ExcludedWeeklyOffCount);
        Assert.Equal(WorkingDayExclusionReason.Holiday, result.Value.Days.Single(x => x.Date == new DateOnly(2026, 10, 4)).ExclusionReason);
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, 1)]
    [InlineData(DayOfWeek.Friday, 2)]
    public async Task Configured_weekly_off_pattern_is_applied_without_hard_coded_weekends(DayOfWeek offDay, int expectedWorkingDays)
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        await SeedEmployeeAsync(database, tenant, employee);
        await using (var seed = database.CreateContext(new TestTenantContext(tenant)))
        {
            var weekly = new WeeklyOffConfiguration { Id = Guid.NewGuid(), TenantId = tenant, EffectiveFrom = new(2026, 1, 1) };
            weekly.Days.Add(new WeeklyOffDay { Id = Guid.NewGuid(), TenantId = tenant, WeeklyOffConfigurationId = weekly.Id, DayOfWeek = offDay });
            seed.WeeklyOffConfigurations.Add(weekly);
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext(new TestTenantContext(tenant));
        var result = await new WorkingDayCalendarResolver(context, new TestTenantContext(tenant), new EffectiveEmploymentResolver(context, new TestTenantContext(tenant)))
            .CalculateAsync(employee, new(2026, 10, 3), new(2026, 10, 4));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(expectedWorkingDays, result.Value!.WorkingLeaveDays);
    }

    [Fact]
    public async Task Location_specific_holiday_does_not_apply_to_another_effective_location()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var locationA = Guid.NewGuid(); var locationB = Guid.NewGuid();
        await SeedEmployeeAsync(database, tenant, employee);
        await using (var seed = database.CreateContext(new TestTenantContext(tenant)))
        {
            seed.WorkLocations.AddRange(new WorkLocation { Id = locationA, TenantId = tenant, Code = "A", Name = "A" }, new WorkLocation { Id = locationB, TenantId = tenant, Code = "B", Name = "B" });
            seed.EmployeeEmploymentHistory.Single(x => x.EmployeeId == employee).WorkLocationId = locationB;
            seed.Holidays.Add(new Holiday { Id = Guid.NewGuid(), TenantId = tenant, Name = "A only", Date = new(2026, 10, 2), WorkLocationId = locationA });
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext(new TestTenantContext(tenant));
        var result = await new WorkingDayCalendarResolver(context, new TestTenantContext(tenant), new EffectiveEmploymentResolver(context, new TestTenantContext(tenant))).CalculateAsync(employee, new(2026, 10, 2), new(2026, 10, 2));
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1m, result.Value!.WorkingLeaveDays);
    }

    private static async Task SeedEmployeeAsync(SqliteInMemoryDatabase database, Guid tenant, Guid employee, Guid? workLocationId = null)
    {
        await using var seed = database.CreateContext(new TestTenantContext());
        seed.Tenants.Add(new Tenant { Id = tenant, TenantCode = "CAL", Host = "calendar.local", ShardKey = "calendar" });
        seed.Employees.Add(new Employee { Id = employee, TenantId = tenant, FirstName = "Calendar", LastName = "Tester", Email = "calendar@test.local" });
        seed.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, EffectiveFrom = new(2020, 1, 1), WorkLocationId = workLocationId });
        await seed.SaveChangesAsync();
    }
}
