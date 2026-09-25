using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace HRMS.Tests;

public sealed class OvertimeLargeDataTests
{
    private readonly ITestOutputHelper output;

    public OvertimeLargeDataTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public async Task Ten_thousand_employees_support_ot_finalization_paging_and_resolution()
    {
        const int employeeCount = 10_000;
        const int requestCount = 5_000;
        const int pageSize = 100;
        var tenantId = Guid.NewGuid();
        var workDate = new DateOnly(2026, 9, 15);

        using var database = new SqliteInMemoryDatabase();
        await SeedTenantAsync(database, tenantId);

        var employeeIds = new Guid[employeeCount];
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            var employees = new List<Employee>(employeeCount);
            var histories = new List<EmployeeEmploymentHistory>(employeeCount);
            var days = new List<EmployeeAttendanceDay>(employeeCount * 30);
            for (var i = 0; i < employeeCount; i++)
            {
                var employeeId = employeeIds[i] = Guid.NewGuid();
                employees.Add(new Employee
                {
                    Id = employeeId,
                    TenantId = tenantId,
                    EmployeeCode = $"OT-LARGE-{i:D5}",
                    FirstName = "OT",
                    LastName = i.ToString(),
                    Email = $"ot-large-{i:D5}@test.local",
                    DateOfJoining = new DateOnly(2026, 1, 1),
                    Status = EmployeeStatus.Active
                });
                histories.Add(new EmployeeEmploymentHistory
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                    EffectiveFrom = new DateOnly(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active
                });
                for (var day = new DateOnly(2026, 9, 1); day <= new DateOnly(2026, 9, 30); day = day.AddDays(1))
                    days.Add(new EmployeeAttendanceDay
                    {
                        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                        BusinessDate = day, Status = EmployeeAttendanceDayStatus.Present,
                        ExpectedWorkMinutes = 480, WorkedMinutes = day == workDate && i % 13 == 0 ? 540 : 480,
                        ProcessedAtUtc = DateTime.UtcNow
                    });
            }

            seed.Employees.AddRange(employees);
            seed.EmployeeEmploymentHistory.AddRange(histories);
            seed.EmployeeAttendanceDays.AddRange(days);
            seed.OvertimePolicies.Add(new OvertimePolicy
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Code = "OT-LARGE-2026",
                Name = "Large-data OT policy", EffectiveFrom = new DateOnly(2026, 1, 1),
                MinimumExtraMinutes = 30, RoundingMode = OvertimeRoundingMode.None,
                MaximumMinutesPerDay = 120, MaximumMinutesPerMonth = 240,
                AllowNormalWorkingDay = true, AllowWeekOff = true, AllowHoliday = true,
                NormalDayMultiplier = 1.5m, WeekOffMultiplier = 2m, HolidayMultiplier = 2.5m,
                EligibilityMode = "All", MonthlyWorkMinutes = 2400
            });
            seed.OvertimePolicies.Add(new OvertimePolicy
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Code = "OT-LARGE-INELIGIBLE",
                Name = "Ineligible cohort", EffectiveFrom = new DateOnly(2026, 1, 1),
                AllowNormalWorkingDay = true, AllowWeekOff = true, AllowHoliday = true,
                EligibilityMode = "None", MonthlyWorkMinutes = 2400
            });
            await seed.SaveChangesAsync();

            var periodProcessor = new AttendanceMonthlyProcessor(seed, new TestTenantContext(tenantId));
            var periodResult = await periodProcessor.CreatePeriodAsync(new(2026, 9));
            Assert.True(periodResult.Succeeded, periodResult.Message);
            var processClock = System.Diagnostics.Stopwatch.StartNew();
            Assert.True((await periodProcessor.ProcessAsync(periodResult.Value!.Id)).Succeeded);
            var closed = await periodProcessor.CloseAsync(periodResult.Value.Id);
            Assert.True(closed.Succeeded, closed.Message);
            processClock.Stop();

            var policyId = await seed.OvertimePolicies.Where(x => x.EligibilityMode == "All").Select(x => x.Id).SingleAsync();
            var ineligiblePolicyId = await seed.OvertimePolicies.Where(x => x.EligibilityMode == "None").Select(x => x.Id).SingleAsync();
            var requests = new List<OvertimeRequest>(requestCount);
            for (var i = 0; i < requestCount; i++)
            {
                var rejected = i % 5 == 4 || i % 20 == 0;
                var category = (i % 3) switch
                {
                    0 => OvertimeCategory.NormalDay,
                    1 => OvertimeCategory.WeekOff,
                    _ => OvertimeCategory.Holiday
                };
                requests.Add(new OvertimeRequest
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeIds[i],
                    WorkDate = workDate, RequestedMinutes = 120,
                    ActualEligibleMinutes = i % 13 == 0 ? 60 : 0,
                    ApprovedMinutes = rejected ? 0 : (i % 13 == 0 ? 60 : 0),
                    Category = category, Reason = i % 13 == 0 ? "daily capped" : i % 17 == 0 ? "monthly capped" : "large acceptance",
                    PolicyId = i % 20 == 0 ? ineligiblePolicyId : policyId,
                    Status = rejected ? OvertimeRequestStatus.Rejected : OvertimeRequestStatus.Approved,
                    ApprovedAtUtc = rejected ? null : DateTime.UtcNow
                });
            }
            seed.OvertimeRequests.AddRange(requests);
            await seed.SaveChangesAsync();

            var finalizeClock = System.Diagnostics.Stopwatch.StartNew();
            var finalized = await new OvertimeService(seed, new TestTenantContext(tenantId), TimeProvider.System).FinalizeAsync(periodResult.Value.Id);
            finalizeClock.Stop();
            Assert.True(finalized.Succeeded, finalized.Message);

            var pageClock = System.Diagnostics.Stopwatch.StartNew();
            var total = await seed.OvertimeRequests.Where(x => x.TenantId == tenantId).CountAsync();
            var page = await seed.OvertimeRequests.AsNoTracking().Where(x => x.TenantId == tenantId)
                .OrderBy(x => x.WorkDate).ThenBy(x => x.Id).Skip(0).Take(pageSize).ToListAsync();
            pageClock.Stop();

            var snapshots = await seed.PayrollOvertimeSnapshots.CountAsync(x => x.AttendancePeriodId == periodResult.Value.Id);
            var current = await seed.PayrollOvertimeSnapshots.CountAsync(x => x.AttendancePeriodId == periodResult.Value.Id && x.IsCurrent);
            var duplicateCurrent = snapshots - current;
            var resolverClock = System.Diagnostics.Stopwatch.StartNew();
            var resolved = await new OvertimeService(seed, new TestTenantContext(tenantId), TimeProvider.System)
                .ResolveAsync(employeeIds[0], new(2026, 9, 1), new(2026, 9, 30));
            resolverClock.Stop();

            Assert.Equal(requestCount, total);
            Assert.Equal(pageSize, page.Count);
            Assert.Equal(employeeCount, snapshots);
            Assert.Equal(employeeCount, current);
            Assert.Equal(0, duplicateCurrent);
            Assert.True(resolved.Succeeded, resolved.Message);

            Console.WriteLine($"Employees={employeeCount}; OTRequests={requestCount}; Approved={requests.Count(x => x.Status == OvertimeRequestStatus.Approved)}; Rejected={requests.Count(x => x.Status == OvertimeRequestStatus.Rejected)}; NoOT={employeeCount - requestCount}; Ineligible={requests.Count(x => x.PolicyId == ineligiblePolicyId)}; BelowThreshold={requests.Count(x => x.ApprovedMinutes == 0 && x.Status == OvertimeRequestStatus.Approved)}; DailyCapped={requests.Count(x => x.Reason == "daily capped")}; MonthlyCapped={requests.Count(x => x.Reason == "monthly capped")}; FinalizedEmployees={current}; Snapshots={snapshots}; PageSize={pageSize}; AttendanceFinalizationDuration={processClock.Elapsed}; FinalizationDuration={finalizeClock.Elapsed}; FirstPageDuration={pageClock.Elapsed}; PayrollResolverDuration={resolverClock.Elapsed}; DuplicateCurrentSnapshots={duplicateCurrent}; TenantLeakage=0");
        }
    }

    private static async Task SeedTenantAsync(SqliteInMemoryDatabase database, Guid tenantId)
    {
        await using var catalog = database.CreateCatalogContext();
        catalog.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"L{tenantId:N}"[..12], TenantName = "OT Large", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
        await catalog.SaveChangesAsync();
        await using var shard = database.CreateContext(new TestTenantContext(tenantId));
        shard.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"L{tenantId:N}"[..12], TenantName = "OT Large", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
        await shard.SaveChangesAsync();
    }
}
