using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class OvertimeAcceptanceTests
{
    [Fact]
    public async Task Normal_day_ot_uses_resolved_attendance_and_preserves_units()
    {
        await using var fixture = await OvertimeFixture.CreateAsync();
        var policy = await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest());
        Assert.True(policy.Succeeded, policy.Message);
        var request = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 120, OvertimeCategory.NormalDay, "release work"));
        Assert.True(request.Succeeded, request.Message);
        Assert.Equal(60, request.Value!.ActualEligibleMinutes);
        Assert.True((await fixture.Service.SubmitAsync(request.Value.Id)).Succeeded);
        var approved = await fixture.Service.ApproveAsync(request.Value.Id, new());
        Assert.True(approved.Succeeded, approved.Message);
        Assert.Equal(60, approved.Value!.ApprovedMinutes);
    }

    [Fact]
    public async Task Threshold_rounding_and_daily_cap_are_policy_owned()
    {
        await using var fixture = await OvertimeFixture.CreateAsync(workedMinutes: 557);
        var policy = await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest(minimum: 30, rounding: 15, mode: OvertimeRoundingMode.Floor, dailyCap: 45));
        Assert.True(policy.Succeeded, policy.Message);
        var request = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 120, OvertimeCategory.NormalDay, null));
        Assert.True(request.Succeeded, request.Message);
        Assert.Equal(45, request.Value!.ActualEligibleMinutes);
    }

    [Fact]
    public async Task Finalization_creates_immutable_versioned_payroll_snapshot()
    {
        await using var fixture = await OvertimeFixture.CreateAsync();
        var policy = await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest()); Assert.True(policy.Succeeded, policy.Message);
        var request = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 60, OvertimeCategory.NormalDay, null)); Assert.True(request.Succeeded, request.Message);
        await fixture.Service.SubmitAsync(request.Value!.Id); await fixture.Service.ApproveAsync(request.Value.Id, new());
        var first = await fixture.Service.FinalizeAsync(fixture.PeriodId); Assert.True(first.Succeeded, first.Message); Assert.Equal(60, first.Value!.Single().TotalApprovedMinutes);
        var second = await fixture.Service.FinalizeAsync(fixture.PeriodId); Assert.True(second.Succeeded, second.Message);
        Assert.Equal(2, await fixture.Db.PayrollOvertimeSnapshots.CountAsync()); Assert.Equal(1, await fixture.Db.PayrollOvertimeSnapshots.CountAsync(x => x.IsCurrent)); Assert.Equal(1, await fixture.Db.PayrollOvertimeSnapshots.CountAsync(x => x.Version == 1)); Assert.Equal(2, await fixture.Db.PayrollOvertimeSnapshots.Where(x => x.IsCurrent).Select(x => x.Version).SingleAsync());
    }

    [Fact]
    public async Task Unfinalized_attendance_blocks_ot_finalization()
    {
        await using var fixture = await OvertimeFixture.CreateAsync(closed: false);
        var result = await fixture.Service.FinalizeAsync(fixture.PeriodId);
        Assert.False(result.Succeeded); Assert.Contains("OvertimeNotFinalized", result.Message);
    }

    [Fact]
    public async Task Effective_dated_policy_is_selected_by_work_date()
    {
        await using var fixture = await OvertimeFixture.CreateAsync();
        var v1 = await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest(code: "OT-V1", effectiveTo: new(2026, 9, 14)));
        var v2 = await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest(code: "OT-V2", effectiveFrom: new(2026, 9, 15), normalMultiplier: 1.75m));
        Assert.True(v1.Succeeded, v1.Message);
        Assert.True(v2.Succeeded, v2.Message);

        var request = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 60, OvertimeCategory.NormalDay, "effective policy"));

        Assert.True(request.Succeeded, request.Message);
        Assert.Equal(v2.Value!.Id, request.Value!.PolicyId);
    }

    [Fact]
    public async Task Ineligible_employee_cannot_create_payable_ot()
    {
        await using var fixture = await OvertimeFixture.CreateAsync();
        var policy = await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest(eligibilityMode: "None"));
        Assert.True(policy.Succeeded, policy.Message);

        var request = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 60, OvertimeCategory.NormalDay, null));

        Assert.False(request.Succeeded);
        Assert.Contains("OvertimeNotEligible", request.Message);
    }

    [Fact]
    public async Task Week_off_and_holiday_requests_preserve_category_and_multiplier_metadata()
    {
        await using var weekOffFixture = await OvertimeFixture.CreateAsync();
        var weekPolicy = await weekOffFixture.Service.CreatePolicyAsync(weekOffFixture.PolicyRequest(weekOffMultiplier: 2.25m));
        Assert.True(weekPolicy.Succeeded, weekPolicy.Message);
        var weekRequest = await weekOffFixture.Service.CreateRequestAsync(new(weekOffFixture.EmployeeId, weekOffFixture.WorkDate, 120, OvertimeCategory.WeekOff, "week off work"));
        Assert.True(weekRequest.Succeeded, weekRequest.Message);
        await weekOffFixture.Service.SubmitAsync(weekRequest.Value!.Id);
        Assert.True((await weekOffFixture.Service.ApproveAsync(weekRequest.Value.Id, new())).Succeeded);
        var weekSnapshot = await weekOffFixture.Service.FinalizeAsync(weekOffFixture.PeriodId);
        Assert.True(weekSnapshot.Succeeded, weekSnapshot.Message);
        Assert.Equal(120, weekSnapshot.Value!.Single().WeekOffMinutes);
        Assert.Equal(2.25m, weekSnapshot.Value.Single().WeekOffMultiplier);

        await using var holidayFixture = await OvertimeFixture.CreateAsync();
        var holidayPolicy = await holidayFixture.Service.CreatePolicyAsync(holidayFixture.PolicyRequest(holidayMultiplier: 2.5m));
        Assert.True(holidayPolicy.Succeeded, holidayPolicy.Message);
        var holidayRequest = await holidayFixture.Service.CreateRequestAsync(new(holidayFixture.EmployeeId, holidayFixture.WorkDate, 120, OvertimeCategory.Holiday, "holiday work"));
        Assert.True(holidayRequest.Succeeded, holidayRequest.Message);
        await holidayFixture.Service.SubmitAsync(holidayRequest.Value!.Id);
        Assert.True((await holidayFixture.Service.ApproveAsync(holidayRequest.Value.Id, new())).Succeeded);
        var holidaySnapshot = await holidayFixture.Service.FinalizeAsync(holidayFixture.PeriodId);
        Assert.True(holidaySnapshot.Succeeded, holidaySnapshot.Message);
        Assert.Equal(120, holidaySnapshot.Value!.Single().HolidayMinutes);
        Assert.Equal(2.5m, holidaySnapshot.Value.Single().HolidayMultiplier);
    }

    [Fact]
    public async Task Overlapping_or_duplicate_work_date_requests_cannot_become_two_payable_requests()
    {
        await using var fixture = await OvertimeFixture.CreateAsync();
        var policy = await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest());
        Assert.True(policy.Succeeded, policy.Message);
        var first = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 120, OvertimeCategory.NormalDay, "18:00-20:00"));
        Assert.True(first.Succeeded, first.Message);
        Assert.True((await fixture.Service.SubmitAsync(first.Value!.Id)).Succeeded);

        var overlapping = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 120, OvertimeCategory.NormalDay, "19:00-21:00"));

        Assert.False(overlapping.Succeeded);
        Assert.Contains("active OT request", overlapping.Message);
    }

    [Fact]
    public async Task Payroll_resolution_rejects_ot_snapshot_from_stale_attendance_version()
    {
        await using var fixture = await OvertimeFixture.CreateAsync();
        var policy = await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest());
        Assert.True(policy.Succeeded, policy.Message);
        var request = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 60, OvertimeCategory.NormalDay, null));
        Assert.True(request.Succeeded, request.Message);
        await fixture.Service.SubmitAsync(request.Value!.Id);
        Assert.True((await fixture.Service.ApproveAsync(request.Value.Id, new())).Succeeded);
        var finalized = await fixture.Service.FinalizeAsync(fixture.PeriodId);
        Assert.True(finalized.Succeeded, finalized.Message);

        var attendance = await fixture.Db.PayrollAttendanceSnapshots.SingleAsync(x => x.EmployeeId == fixture.EmployeeId);
        attendance.Version = 2;
        await fixture.Db.SaveChangesAsync();

        var resolved = await fixture.Service.ResolveAsync(fixture.EmployeeId, new(2026, 9, 1), new(2026, 9, 30));

        Assert.False(resolved.Succeeded);
        Assert.Contains("OvertimeVersionConflict", resolved.Message);
    }

    [Fact]
    public async Task Requested_actual_and_approved_minutes_remain_distinct_and_bounded()
    {
        await using var fixture = await OvertimeFixture.CreateAsync(workedMinutes: 570);
        Assert.True((await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest())).Succeeded);
        var request = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 120, OvertimeCategory.NormalDay, null));
        Assert.True(request.Succeeded, request.Message);
        Assert.Equal(120, request.Value!.RequestedMinutes);
        Assert.Equal(90, request.Value.ActualEligibleMinutes);
        Assert.Equal(0, request.Value.ApprovedMinutes);
        Assert.True((await fixture.Service.SubmitAsync(request.Value.Id)).Succeeded);
        var approved = await fixture.Service.ApproveAsync(request.Value.Id, new());
        Assert.True(approved.Succeeded, approved.Message);
        Assert.Equal(120, approved.Value!.RequestedMinutes);
        Assert.Equal(90, approved.Value.ActualEligibleMinutes);
        Assert.Equal(90, approved.Value.ApprovedMinutes);
    }

    [Fact]
    public async Task Monthly_cap_is_applied_across_categories_during_finalization()
    {
        await using var fixture = await OvertimeFixture.CreateAsync();
        Assert.True((await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest(monthlyCap: 90))).Succeeded);
        fixture.Db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = new(2026, 9, 16), Status = EmployeeAttendanceDayStatus.Present, ExpectedWorkMinutes = 480, WorkedMinutes = 540, ProcessedAtUtc = DateTime.UtcNow });
        await fixture.Db.SaveChangesAsync();
        var first = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 60, OvertimeCategory.NormalDay, null));
        var second = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, new(2026, 9, 16), 60, OvertimeCategory.WeekOff, null));
        Assert.True(first.Succeeded, first.Message); Assert.True(second.Succeeded, second.Message);
        await fixture.Service.SubmitAsync(first.Value!.Id); await fixture.Service.SubmitAsync(second.Value!.Id);
        Assert.True((await fixture.Service.ApproveAsync(first.Value.Id, new())).Succeeded);
        Assert.True((await fixture.Service.ApproveAsync(second.Value!.Id, new())).Succeeded);
        var finalized = await fixture.Service.FinalizeAsync(fixture.PeriodId);
        Assert.True(finalized.Succeeded, finalized.Message);
        var snapshot = finalized.Value!.Single();
        Assert.Equal(90, snapshot.TotalApprovedMinutes);
        Assert.Equal(60, snapshot.NormalDayMinutes);
        Assert.Equal(30, snapshot.WeekOffMinutes);
    }

    [Fact]
    public async Task Reopen_preserves_version_one_and_refinalization_creates_version_two()
    {
        await using var fixture = await OvertimeFixture.CreateAsync();
        Assert.True((await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest())).Succeeded);
        var request = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 60, OvertimeCategory.NormalDay, null));
        Assert.True(request.Succeeded, request.Message); await fixture.Service.SubmitAsync(request.Value!.Id); await fixture.Service.ApproveAsync(request.Value.Id, new());
        Assert.True((await fixture.Service.FinalizeAsync(fixture.PeriodId)).Succeeded);
        Assert.True((await fixture.Service.ReopenAsync(fixture.PeriodId, "test correction")).Succeeded);
        Assert.Equal(1, await fixture.Db.PayrollOvertimeSnapshots.CountAsync(x => x.Version == 1));
        Assert.Equal(0, await fixture.Db.PayrollOvertimeSnapshots.CountAsync(x => x.IsCurrent));
        Assert.True((await fixture.Service.FinalizeAsync(fixture.PeriodId)).Succeeded);
        Assert.Equal(1, await fixture.Db.PayrollOvertimeSnapshots.CountAsync(x => x.IsCurrent && x.Version == 2));
        Assert.Equal(2, await fixture.Db.PayrollOvertimeSnapshots.CountAsync());
    }

    [Fact]
    public async Task Overnight_shift_uses_business_date_without_midnight_double_counting()
    {
        await using var fixture = await OvertimeFixture.CreateAsync(workedMinutes: 540);
        var day = await fixture.Db.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == fixture.WorkDate);
        day.ScheduledStartUtc = new DateTime(2026, 9, 15, 22, 0, 0, DateTimeKind.Utc);
        day.ScheduledEndUtc = new DateTime(2026, 9, 16, 6, 0, 0, DateTimeKind.Utc);
        day.ExpectedWorkMinutes = 480;
        await fixture.Db.SaveChangesAsync();
        Assert.True((await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest(minimum: 30, rounding: 15, mode: OvertimeRoundingMode.Floor, dailyCap: 90))).Succeeded);

        var request = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 120, OvertimeCategory.NormalDay, "overnight extension"));

        Assert.True(request.Succeeded, request.Message);
        Assert.Equal(fixture.WorkDate, request.Value!.WorkDate);
        Assert.Equal(480, day.ExpectedWorkMinutes);
        Assert.Equal(540, day.WorkedMinutes);
        Assert.Equal(60, request.Value.ActualEligibleMinutes);
        Assert.True((await fixture.Service.SubmitAsync(request.Value.Id)).Succeeded);
        var approved = await fixture.Service.ApproveAsync(request.Value.Id, new());
        Assert.True(approved.Succeeded, approved.Message);
        Assert.Equal(60, approved.Value!.ApprovedMinutes);
        Assert.Equal(OvertimeCategory.NormalDay, request.Value.Category);
    }

    [Fact]
    public async Task Partial_day_attendance_does_not_inflate_eligible_overtime()
    {
        await using var fixture = await OvertimeFixture.CreateAsync(workedMinutes: 330);
        var day = await fixture.Db.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == fixture.WorkDate);
        day.ExpectedWorkMinutes = 300;
        await fixture.Db.SaveChangesAsync();
        Assert.True((await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest())).Succeeded);

        var request = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 120, OvertimeCategory.NormalDay, "partial day"));

        Assert.True(request.Succeeded, request.Message);
        Assert.Equal(300, day.ExpectedWorkMinutes);
        Assert.Equal(330, day.WorkedMinutes);
        Assert.Equal(30, request.Value!.ActualEligibleMinutes);
    }

    [Fact]
    public async Task Approved_full_day_leave_with_no_authoritative_work_has_no_payable_overtime()
    {
        await using var fixture = await OvertimeFixture.CreateAsync(workedMinutes: null);
        var day = await fixture.Db.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == fixture.WorkDate);
        day.Status = EmployeeAttendanceDayStatus.OnLeave;
        day.WorkedMinutes = null;
        await fixture.Db.SaveChangesAsync();
        Assert.True((await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest())).Succeeded);

        var request = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 120, OvertimeCategory.NormalDay, "leave interaction"));

        Assert.True(request.Succeeded, request.Message);
        Assert.Equal(0, request.Value!.ActualEligibleMinutes);
        Assert.True((await fixture.Service.SubmitAsync(request.Value.Id)).Succeeded);
        var approved = await fixture.Service.ApproveAsync(request.Value.Id, new());
        Assert.True(approved.Succeeded, approved.Message);
        Assert.Equal(0, approved.Value!.ApprovedMinutes);
    }

    [Fact]
    public async Task On_duty_status_does_not_automatically_become_overtime()
    {
        await using var fixture = await OvertimeFixture.CreateAsync(workedMinutes: 480);
        var day = await fixture.Db.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == fixture.WorkDate);
        day.Status = EmployeeAttendanceDayStatus.OnDuty;
        await fixture.Db.SaveChangesAsync();
        Assert.True((await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest())).Succeeded);

        var request = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 120, OvertimeCategory.NormalDay, "on duty"));

        Assert.True(request.Succeeded, request.Message);
        Assert.Equal(0, request.Value!.ActualEligibleMinutes);
    }

    [Fact]
    public async Task Approved_regularization_changes_authoritative_work_time_but_pending_does_not()
    {
        await using var fixture = await OvertimeFixture.CreateAsync(workedMinutes: 480);
        var day = await fixture.Db.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == fixture.WorkDate);
        day.WorkedMinutes = 540;
        await fixture.Db.SaveChangesAsync();
        Assert.True((await fixture.Service.CreatePolicyAsync(fixture.PolicyRequest())).Succeeded);

        var approved = await fixture.Service.CreateRequestAsync(new(fixture.EmployeeId, fixture.WorkDate, 120, OvertimeCategory.NormalDay, "regularized work"));

        Assert.True(approved.Succeeded, approved.Message);
        Assert.Equal(60, approved.Value!.ActualEligibleMinutes);

        day.WorkedMinutes = 480;
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(480, day.WorkedMinutes);
    }

    internal sealed class OvertimeFixture : IAsyncDisposable
    {
        private readonly SqliteInMemoryDatabase database;
        public HRMS.Infrastructure.Persistence.HrmsDbContext Db { get; }
        public IOvertimeService Service { get; }
        public Guid TenantId { get; }
        public Guid EmployeeId { get; }
        public Guid PeriodId { get; private set; }
        public Guid RequestId { get; set; }
        public DateOnly WorkDate { get; } = new(2026, 9, 15);
        private OvertimeFixture(SqliteInMemoryDatabase database, HRMS.Infrastructure.Persistence.HrmsDbContext db, IOvertimeService service, Guid tenantId, Guid employeeId) { this.database = database; Db = db; Service = service; TenantId = tenantId; EmployeeId = employeeId; }
        public static async Task<OvertimeFixture> CreateAsync(int? workedMinutes = 540, bool closed = true, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
        {
            var database = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
            await using (var catalog = database.CreateCatalogContext()) { catalog.Tenants.Add(new Tenant { Id = tenant, TenantCode = $"OT{tenant:N}"[..12], TenantName = "OT", Host = $"{tenant:N}.test", ShardKey = tenant.ToString("N") }); await catalog.SaveChangesAsync(); }
            Guid periodId;
            await using (var seed = database.CreateContext(new TestTenantContext(tenant)))
            {
                seed.Tenants.Add(new Tenant { Id = tenant, TenantCode = $"OT{tenant:N}"[..12], TenantName = "OT", Host = $"{tenant:N}.test", ShardKey = tenant.ToString("N") }); seed.Employees.Add(new Employee { Id = employee, TenantId = tenant, EmployeeCode = "OT-001", FirstName = "OT", LastName = "Tester", Email = $"{employee:N}@test.local", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active });
                seed.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active }); seed.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, BusinessDate = new(2026, 9, 15), Status = EmployeeAttendanceDayStatus.Present, ExpectedWorkMinutes = 480, WorkedMinutes = workedMinutes, ProcessedAtUtc = DateTime.UtcNow });
                var period = new AttendancePeriod { Id = Guid.NewGuid(), TenantId = tenant, Year = 2026, Month = 9, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), Status = closed ? AttendancePeriodStatus.Closed : AttendancePeriodStatus.Open, DataVersion = 1, ConcurrencyVersion = 1 }; periodId = period.Id; seed.AttendancePeriods.Add(period); seed.PayrollAttendanceSnapshots.Add(new PayrollAttendanceSnapshot { Id = Guid.NewGuid(), TenantId = tenant, AttendancePeriodId = period.Id, EmployeeId = employee, Version = 1, IsCurrent = true, PeriodStart = period.StartDate, PeriodEnd = period.EndDate, EligibleDays = 30, PayableDays = 30, LopDays = 0, FinalizedAtUtc = DateTime.UtcNow }); await seed.SaveChangesAsync();
            }
            var db = database.CreateContext(new TestTenantContext(tenant), interceptors); return new OvertimeFixture(database, db, new OvertimeService(db, new TestTenantContext(tenant), TimeProvider.System), tenant, employee) { PeriodId = periodId };
        }
        public (HRMS.Infrastructure.Persistence.HrmsDbContext Db, IOvertimeService Service) CreateIndependentService()
        {
            var scope = new TestTenantContext(TenantId);
            var db = database.CreateIsolatedContext(scope);
            return (db, new OvertimeService(db, scope, TimeProvider.System));
        }
        public OvertimePolicyRequest PolicyRequest(int minimum = 0, int rounding = 0, OvertimeRoundingMode mode = OvertimeRoundingMode.None, int? dailyCap = null, string code = "OT-DEFAULT", DateOnly? effectiveFrom = null, DateOnly? effectiveTo = null, string eligibilityMode = "All", decimal normalMultiplier = 1.5m, decimal weekOffMultiplier = 2m, decimal holidayMultiplier = 2m, int? monthlyCap = null) => new(code, "Default overtime", effectiveFrom ?? new(2026, 1, 1), effectiveTo, minimum, rounding, mode, dailyCap, monthlyCap, false, true, true, true, true, normalMultiplier, weekOffMultiplier, holidayMultiplier, eligibilityMode);
        public ValueTask DisposeAsync() { Db.Dispose(); database.Dispose(); return ValueTask.CompletedTask; }
    }
}
