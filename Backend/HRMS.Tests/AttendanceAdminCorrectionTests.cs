using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceAdminCorrectionTests
{
    [Fact]
    public async Task Admin_correction_changes_effective_day_without_mutating_raw_punches_and_keeps_history_append_only()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var userId = fixture.TenantContext.UserId!.Value;
        fixture.Context.Users.Add(new User { Id = userId, TenantId = fixture.TenantId, Email = $"{userId:N}@example.test", FirstName = "HR", LastName = "Admin" });
        var shift = await fixture.AddShiftAsync("ADMIN-CORRECTION");
        await fixture.AddRuleAsync(shift.Id, employeeId: fixture.EmployeeId);
        var date = new DateOnly(2026, 9, 10);
        var rawIn = new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);
        var rawOut = new DateTime(2026, 9, 10, 17, 0, 0, DateTimeKind.Utc);
        fixture.Context.AttendancePunches.AddRange(
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = rawIn, CapturedAtUtc = rawIn, Direction = PunchDirection.In, Source = PunchSource.Biometric },
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = rawOut, CapturedAtUtc = rawOut, Direction = PunchDirection.Out, Source = PunchSource.Biometric });
        await fixture.Context.SaveChangesAsync();

        var tenant = new TestTenantContext(fixture.TenantId, userId);
        await using var db = fixture.CreateContext(fixture.TenantId, out _);
        var roster = new AttendanceFoundationService(db, tenant, new EffectiveEmploymentResolver(db, tenant));
        var dayProcessor = new AttendanceDayProcessor(db, tenant, roster, new FixedClock(new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.Zero)), new AttendancePeriodLockService(db, tenant));
        var service = new AttendanceAdminCorrectionService(db, tenant, new EffectiveEmploymentResolver(db, tenant), dayProcessor, new AttendancePeriodLockService(db, tenant), new FixedClock(new DateTimeOffset(2026, 9, 10, 20, 0, 0, TimeSpan.Zero)));
        var correctedIn = new DateTime(2026, 9, 10, 8, 30, 0, DateTimeKind.Utc);
        var created = await service.CreateAsync(new(fixture.EmployeeId, date, correctedIn, rawOut, "  HR correction  "));

        Assert.True(created.Succeeded, created.Message);
        var day = await db.EmployeeAttendanceDays.AsNoTracking().SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date);
        Assert.Equal(correctedIn, day.FirstPunchAtUtc);
        Assert.Equal(rawIn, await db.AttendancePunches.Where(x => x.EmployeeId == fixture.EmployeeId && x.Direction == PunchDirection.In).Select(x => x.PunchAtUtc).SingleAsync());
        Assert.Equal("HR correction", created.Value!.Reason);

        var second = await service.CreateAsync(new(fixture.EmployeeId, date, new DateTime(2026, 9, 10, 8, 15, 0, DateTimeKind.Utc), rawOut, "Second correction"));
        Assert.True(second.Succeeded, second.Message);
        Assert.Equal(2, await db.AttendanceAdminCorrections.CountAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date));
        Assert.Equal([1, 2], await db.AttendanceAdminCorrections.Where(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date).OrderBy(x => x.CorrectionVersion).Select(x => x.CorrectionVersion).ToListAsync());
    }

    [Fact]
    public async Task Admin_correction_is_denied_for_closed_period_and_reason_is_required()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var userId = fixture.TenantContext.UserId!.Value;
        fixture.Context.Users.Add(new User { Id = userId, TenantId = fixture.TenantId, Email = $"{userId:N}@example.test", FirstName = "HR", LastName = "Admin" });
        await fixture.Context.SaveChangesAsync();
        var date = new DateOnly(2026, 9, 10);
        fixture.Context.AttendancePeriods.Add(new AttendancePeriod { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Year = 2026, Month = 9, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), Status = AttendancePeriodStatus.Closed });
        await fixture.Context.SaveChangesAsync();
        var tenant = new TestTenantContext(fixture.TenantId, userId);
        await using var db = fixture.CreateContext(fixture.TenantId, out _);
        var service = new AttendanceAdminCorrectionService(db, tenant, new EffectiveEmploymentResolver(db, tenant), new ThrowingProcessor(), new AttendancePeriodLockService(db, tenant));
        var denied = await service.CreateAsync(new(fixture.EmployeeId, date, DateTime.UtcNow, null, "closed"));
        Assert.False(denied.Succeeded);
        Assert.Equal(ResultStatus.Conflict, denied.Status);
        Assert.Empty(await db.AttendanceAdminCorrections.ToListAsync());
        var invalid = await service.CreateAsync(new(fixture.EmployeeId, new(2026, 8, 10), DateTime.UtcNow, null, " "));
        Assert.False(invalid.Succeeded);
        Assert.Equal(ResultStatus.ValidationFailed, invalid.Status);
    }

    [Fact]
    public async Task Admin_correction_rolls_back_when_effective_day_processing_fails()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var userId = fixture.TenantContext.UserId!.Value;
        fixture.Context.Users.Add(new User { Id = userId, TenantId = fixture.TenantId, Email = $"{userId:N}@example.test", FirstName = "HR", LastName = "Admin" });
        await fixture.Context.SaveChangesAsync();
        var tenant = new TestTenantContext(fixture.TenantId, userId);
        await using var db = fixture.CreateContext(fixture.TenantId, out _);
        var service = new AttendanceAdminCorrectionService(db, tenant, new EffectiveEmploymentResolver(db, tenant), new FailingProcessor(), new AttendancePeriodLockService(db, tenant));

        var result = await service.CreateAsync(new(fixture.EmployeeId, new(2026, 9, 12), new DateTime(2026, 9, 12, 8, 30, 0, DateTimeKind.Utc), null, "rollback"));

        Assert.False(result.Succeeded);
        Assert.Empty(await db.AttendanceAdminCorrections.AsNoTracking().ToListAsync());
        Assert.Empty(await db.EmployeeAttendanceDays.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Admin_correction_list_is_paged_and_detail_is_tenant_scoped()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var userId = fixture.TenantContext.UserId!.Value;
        fixture.Context.Users.Add(new User { Id = userId, TenantId = fixture.TenantId, Email = $"{userId:N}@example.test", FirstName = "HR", LastName = "Admin" });
        await fixture.Context.SaveChangesAsync();
        var tenant = new TestTenantContext(fixture.TenantId, userId);
        await using var db = fixture.CreateContext(fixture.TenantId, out _);
        var service = new AttendanceAdminCorrectionService(db, tenant, new EffectiveEmploymentResolver(db, tenant), new NoOpProcessor(), new AttendancePeriodLockService(db, tenant));

        var first = await service.CreateAsync(new(fixture.EmployeeId, new(2026, 9, 13), new DateTime(2026, 9, 13, 8, 30, 0, DateTimeKind.Utc), null, "first"));
        var second = await service.CreateAsync(new(fixture.EmployeeId, new(2026, 9, 14), new DateTime(2026, 9, 14, 8, 30, 0, DateTimeKind.Utc), null, "second"));
        Assert.True(first.Succeeded, first.Message);
        Assert.True(second.Succeeded, second.Message);

        var page = await service.ListAsync(new() { Page = 2, PageSize = 1 });
        Assert.True(page.Succeeded, page.Message);
        Assert.Equal(2, page.Value!.TotalCount);
        Assert.Single(page.Value.Items);
        Assert.Equal(new DateOnly(2026, 9, 13), page.Value.Items[0].BusinessDate);
        var detail = await service.GetAsync(first.Value!.Id);
        Assert.True(detail.Succeeded, detail.Message);
        Assert.Equal("first", detail.Value!.Reason);

        var otherTenant = new TestTenantContext(Guid.NewGuid(), userId);
        await using var otherDb = fixture.CreateContext(otherTenant.TenantId!.Value, out _);
        var otherService = new AttendanceAdminCorrectionService(otherDb, otherTenant, new EffectiveEmploymentResolver(otherDb, otherTenant), new NoOpProcessor(), new AttendancePeriodLockService(otherDb, otherTenant));
        var foreign = await otherService.GetAsync(first.Value.Id);
        Assert.Equal(ResultStatus.NotFound, foreign.Status);
    }

    [Fact]
    public async Task Concurrent_admin_corrections_leave_a_deterministic_append_only_result()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var userId = fixture.TenantContext.UserId!.Value;
        fixture.Context.Users.Add(new User { Id = userId, TenantId = fixture.TenantId, Email = $"{userId:N}@example.test", FirstName = "HR", LastName = "Admin" });
        var shift = await fixture.AddShiftAsync("ADMIN-RACE");
        await fixture.AddRuleAsync(shift.Id, employeeId: fixture.EmployeeId);
        var date = new DateOnly(2026, 9, 15);
        fixture.Context.AttendancePunches.AddRange(
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = new(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc), CapturedAtUtc = new(2026, 9, 15, 9, 0, 0, DateTimeKind.Utc), Direction = PunchDirection.In, Source = PunchSource.Biometric },
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = new(2026, 9, 15, 17, 0, 0, DateTimeKind.Utc), CapturedAtUtc = new(2026, 9, 15, 17, 0, 0, DateTimeKind.Utc), Direction = PunchDirection.Out, Source = PunchSource.Biometric });
        await fixture.Context.SaveChangesAsync();

        var tenantA = new TestTenantContext(fixture.TenantId, userId);
        var tenantB = new TestTenantContext(fixture.TenantId, userId);
        await using var dbA = fixture.CreateContext(fixture.TenantId, out _);
        await using var dbB = fixture.CreateContext(fixture.TenantId, out _);
        var serviceA = CreateService(dbA, tenantA);
        var serviceB = CreateService(dbB, tenantB);
        var results = await Task.WhenAll(
            serviceA.CreateAsync(new(fixture.EmployeeId, date, new(2026, 9, 15, 8, 30, 0, DateTimeKind.Utc), null, "race A")),
            serviceB.CreateAsync(new(fixture.EmployeeId, date, new(2026, 9, 15, 8, 15, 0, DateTimeKind.Utc), null, "race B")));

        Assert.True(results.Any(x => x.Succeeded), string.Join("; ", results.Select(x => x.Message)));
        await using var verify = fixture.CreateContext(fixture.TenantId, out _);
        var rows = await verify.AttendanceAdminCorrections.AsNoTracking().Where(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date).OrderBy(x => x.CorrectionVersion).ToListAsync();
        Assert.NotEmpty(rows);
        Assert.Equal(Enumerable.Range(1, rows.Count), rows.Select(x => x.CorrectionVersion));
        var effective = await verify.EmployeeAttendanceDays.AsNoTracking().SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date);
        Assert.Equal(rows[^1].CorrectedInAtUtc, effective.FirstPunchAtUtc);
    }

    [Fact]
    public async Task Admin_correction_remains_above_approved_regularization_and_v2_wins_after_reprocessing()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var userId = fixture.TenantContext.UserId!.Value;
        fixture.Context.Users.Add(new User { Id = userId, TenantId = fixture.TenantId, Email = $"{userId:N}@example.test", FirstName = "HR", LastName = "Admin" });
        var shift = await fixture.AddShiftAsync("ADMIN-PRECEDENCE");
        await fixture.AddRuleAsync(shift.Id, employeeId: fixture.EmployeeId);
        var date = new DateOnly(2026, 9, 16);
        var regularizationId = Guid.NewGuid();
        fixture.Context.AttendanceRegularizationRequests.Add(new AttendanceRegularizationRequest
        {
            Id = regularizationId, TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date,
            RequestType = AttendanceRegularizationType.CorrectInOutTime, Reason = "approved", Status = AttendanceRequestStatus.Approved,
            SubmittedByUserId = userId, SubmittedAtUtc = DateTime.UtcNow, ReviewedByUserId = userId, ReviewedAtUtc = DateTime.UtcNow,
            ProposedInAtUtc = new(2026, 9, 16, 9, 15, 0, DateTimeKind.Utc), ProposedOutAtUtc = new(2026, 9, 16, 17, 15, 0, DateTimeKind.Utc)
        });
        fixture.Context.AttendanceAdjustments.Add(new AttendanceAdjustment
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date,
            AttendanceRegularizationRequestId = regularizationId, EffectiveInAtUtc = new(2026, 9, 16, 9, 15, 0, DateTimeKind.Utc),
            EffectiveOutAtUtc = new(2026, 9, 16, 17, 15, 0, DateTimeKind.Utc), ApprovedByUserId = userId, ApprovedAtUtc = DateTime.UtcNow
        });
        fixture.Context.AttendancePunches.AddRange(
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc), CapturedAtUtc = DateTime.UtcNow, Direction = PunchDirection.In, Source = PunchSource.Biometric },
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = new(2026, 9, 16, 18, 0, 0, DateTimeKind.Utc), CapturedAtUtc = DateTime.UtcNow, Direction = PunchDirection.Out, Source = PunchSource.Biometric });
        await fixture.Context.SaveChangesAsync();

        var tenant = new TestTenantContext(fixture.TenantId, userId);
        await using var db = fixture.CreateContext(fixture.TenantId, out _);
        var processor = new AttendanceDayProcessor(db, tenant, new AttendanceFoundationService(db, tenant, new EffectiveEmploymentResolver(db, tenant)), new FixedClock(new DateTimeOffset(2026, 9, 16, 20, 0, 0, TimeSpan.Zero)), new AttendancePeriodLockService(db, tenant));
        Assert.True((await processor.ProcessAsync(fixture.EmployeeId, date)).Succeeded);
        var service = new AttendanceAdminCorrectionService(db, tenant, new EffectiveEmploymentResolver(db, tenant), processor, new AttendancePeriodLockService(db, tenant));
        var v1 = await service.CreateAsync(new(fixture.EmployeeId, date, new DateTime(2026, 9, 16, 8, 30, 0, DateTimeKind.Utc), new DateTime(2026, 9, 16, 17, 30, 0, DateTimeKind.Utc), "admin v1"));
        Assert.True(v1.Succeeded, v1.Message);
        var v2 = await service.CreateAsync(new(fixture.EmployeeId, date, new DateTime(2026, 9, 16, 8, 15, 0, DateTimeKind.Utc), new DateTime(2026, 9, 16, 17, 45, 0, DateTimeKind.Utc), "admin v2"));
        Assert.True(v2.Succeeded, v2.Message);
        Assert.Equal(2, await db.AttendanceAdminCorrections.CountAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date));
        var history = await db.AttendanceAdminCorrections.AsNoTracking().Where(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date).OrderBy(x => x.CorrectionVersion).ToListAsync();
        Assert.Equal([1, 2], history.Select(x => x.CorrectionVersion));
        Assert.Equal("admin v1", history[0].Reason);
        Assert.True((await processor.ProcessAsync(fixture.EmployeeId, date)).Succeeded);
        var day = await db.EmployeeAttendanceDays.AsNoTracking().SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date);
        Assert.Equal(v2.Value!.CorrectedInAtUtc, day.FirstPunchAtUtc);
        Assert.Equal(v2.Value.CorrectedOutAtUtc, day.LastPunchAtUtc);
        Assert.Equal(AttendanceRequestStatus.Approved, await db.AttendanceRegularizationRequests.Where(x => x.Id == regularizationId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Concurrent_admin_correction_and_attendance_day_processing_leave_correction_effective()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var userId = fixture.TenantContext.UserId!.Value;
        fixture.Context.Users.Add(new User { Id = userId, TenantId = fixture.TenantId, Email = $"{userId:N}@example.test", FirstName = "HR", LastName = "Admin" });
        var shift = await fixture.AddShiftAsync("ADMIN-DAY-RACE");
        await fixture.AddRuleAsync(shift.Id, employeeId: fixture.EmployeeId);
        var date = new DateOnly(2026, 9, 17);
        var rawIn = new DateTime(2026, 9, 17, 9, 0, 0, DateTimeKind.Utc);
        var rawOut = new DateTime(2026, 9, 17, 17, 0, 0, DateTimeKind.Utc);
        fixture.Context.AttendancePunches.AddRange(
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = rawIn, CapturedAtUtc = rawIn, Direction = PunchDirection.In, Source = PunchSource.Biometric },
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = rawOut, CapturedAtUtc = rawOut, Direction = PunchDirection.Out, Source = PunchSource.Biometric });
        await fixture.Context.SaveChangesAsync();

        await using var correctionDb = fixture.CreateContext(fixture.TenantId, out _);
        await using var processingDb = fixture.CreateContext(fixture.TenantId, out _);
        var correctionTenant = new TestTenantContext(fixture.TenantId, userId);
        var processingTenant = new TestTenantContext(fixture.TenantId, userId);
        var correctionProcessor = new AttendanceDayProcessor(correctionDb, correctionTenant, new AttendanceFoundationService(correctionDb, correctionTenant, new EffectiveEmploymentResolver(correctionDb, correctionTenant)), new FixedClock(new DateTimeOffset(2026, 9, 17, 20, 0, 0, TimeSpan.Zero)), new AttendancePeriodLockService(correctionDb, correctionTenant));
        var processingProcessor = new AttendanceDayProcessor(processingDb, processingTenant, new AttendanceFoundationService(processingDb, processingTenant, new EffectiveEmploymentResolver(processingDb, processingTenant)), new FixedClock(new DateTimeOffset(2026, 9, 17, 20, 0, 0, TimeSpan.Zero)), new AttendancePeriodLockService(processingDb, processingTenant));
        var correctionService = new AttendanceAdminCorrectionService(correctionDb, correctionTenant, new EffectiveEmploymentResolver(correctionDb, correctionTenant), correctionProcessor, new AttendancePeriodLockService(correctionDb, correctionTenant));
        var correctedIn = new DateTime(2026, 9, 17, 8, 15, 0, DateTimeKind.Utc);

        var correctionTask = correctionService.CreateAsync(new(fixture.EmployeeId, date, correctedIn, rawOut, "concurrent correction"));
        var processingTask = processingProcessor.ProcessAsync(fixture.EmployeeId, date);
        await Task.WhenAll(correctionTask, processingTask);
        var correctionResult = await correctionTask;
        Assert.True(correctionResult.Succeeded, correctionResult.Message);
        await using var verify = fixture.CreateContext(fixture.TenantId, out _);
        var correction = await verify.AttendanceAdminCorrections.AsNoTracking().SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date);
        var day = await verify.EmployeeAttendanceDays.AsNoTracking().SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date);
        Assert.Equal(correctedIn, day.FirstPunchAtUtc);
        Assert.Equal(rawOut, day.LastPunchAtUtc);
        Assert.Equal(rawIn, await verify.AttendancePunches.Where(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date && x.Direction == PunchDirection.In).Select(x => x.PunchAtUtc).SingleAsync());
        Assert.Equal(correction.CorrectedInAtUtc, day.FirstPunchAtUtc);
    }

    [Fact]
    public async Task Concurrent_close_and_admin_correction_never_leave_closed_period_with_correction()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var userId = fixture.TenantContext.UserId!.Value;
        fixture.Context.Users.Add(new User { Id = userId, TenantId = fixture.TenantId, Email = $"{userId:N}@example.test", FirstName = "HR", LastName = "Admin" });
        var shift = await fixture.AddShiftAsync("ADMIN-CLOSE-RACE");
        await fixture.AddRuleAsync(shift.Id, employeeId: fixture.EmployeeId);
        var date = new DateOnly(2026, 9, 18);
        var rawIn = new DateTime(2026, 9, 18, 9, 0, 0, DateTimeKind.Utc);
        var rawOut = new DateTime(2026, 9, 18, 17, 0, 0, DateTimeKind.Utc);
        fixture.Context.AttendancePunches.AddRange(
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = rawIn, CapturedAtUtc = rawIn, Direction = PunchDirection.In, Source = PunchSource.Biometric },
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = rawOut, CapturedAtUtc = rawOut, Direction = PunchDirection.Out, Source = PunchSource.Biometric });
        await fixture.Context.SaveChangesAsync();
        var setupTenant = new TestTenantContext(fixture.TenantId, userId);
        await using (var setupDb = fixture.CreateContext(fixture.TenantId, out _))
        {
            var setupProcessor = new AttendanceDayProcessor(setupDb, setupTenant, new AttendanceFoundationService(setupDb, setupTenant, new EffectiveEmploymentResolver(setupDb, setupTenant)), new FixedClock(new DateTimeOffset(2026, 9, 18, 20, 0, 0, TimeSpan.Zero)), new AttendancePeriodLockService(setupDb, setupTenant));
            Assert.True((await setupProcessor.ProcessAsync(fixture.EmployeeId, date)).Succeeded);
            var monthly = new AttendanceMonthlyProcessor(setupDb, setupTenant);
            var period = (await monthly.CreatePeriodAsync(new(2026, 9))).Value!;
            Assert.True((await monthly.ProcessAsync(period.Id)).Succeeded);
        }
        await using var periodDb = fixture.CreateContext(fixture.TenantId, out _);
        var periodId = await periodDb.AttendancePeriods.Where(x => x.TenantId == fixture.TenantId && x.Year == 2026 && x.Month == 9).Select(x => x.Id).SingleAsync();
        await using var closeDb = fixture.CreateContext(fixture.TenantId, out _);
        await using var correctionDb = fixture.CreateContext(fixture.TenantId, out _);
        var closeTenant = new TestTenantContext(fixture.TenantId, userId);
        var correctionTenant = new TestTenantContext(fixture.TenantId, userId);
        var closeProcessor = new AttendanceMonthlyProcessor(closeDb, closeTenant);
        var correctionDayProcessor = new AttendanceDayProcessor(correctionDb, correctionTenant, new AttendanceFoundationService(correctionDb, correctionTenant, new EffectiveEmploymentResolver(correctionDb, correctionTenant)), new FixedClock(new DateTimeOffset(2026, 9, 18, 20, 0, 0, TimeSpan.Zero)), new AttendancePeriodLockService(correctionDb, correctionTenant));
        var correctionService = new AttendanceAdminCorrectionService(correctionDb, correctionTenant, new EffectiveEmploymentResolver(correctionDb, correctionTenant), correctionDayProcessor, new AttendancePeriodLockService(correctionDb, correctionTenant));
        var correctionTask = correctionService.CreateAsync(new(fixture.EmployeeId, date, new DateTime(2026, 9, 18, 8, 15, 0, DateTimeKind.Utc), rawOut, "close race"));
        var closeTask = closeProcessor.CloseAsync(periodId);
        await Task.WhenAll(correctionTask, closeTask);

        await using var verify = fixture.CreateContext(fixture.TenantId, out _);
        var periodState = await verify.AttendancePeriods.AsNoTracking().SingleAsync(x => x.Id == periodId);
        var correctionCount = await verify.AttendanceAdminCorrections.CountAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date);
        var closedEvents = await verify.AttendancePeriodEvents.CountAsync(x => x.AttendancePeriodId == periodId && x.EventType == AttendancePeriodEventType.Closed);
        Assert.InRange(closedEvents, 0, 1);
        if (periodState.Status == AttendancePeriodStatus.Closed)
        {
            Assert.Equal(0, correctionCount);
            var summaryVersion = await verify.EmployeeAttendanceMonthlySummaries.Where(x => x.AttendancePeriodId == periodId).Select(x => x.SourceDataVersion).SingleAsync();
            Assert.Equal(periodState.DataVersion, summaryVersion);
        }
        else
        {
            Assert.Equal(1, correctionCount);
            Assert.Equal(AttendancePeriodStatus.ReadyToClose, periodState.Status);
            Assert.Equal(1, await verify.EmployeeAttendanceDays.CountAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date));
        }
    }

    [Fact]
    public async Task Admin_correction_rejects_approved_leave_and_on_duty_without_side_effects()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var userId = fixture.TenantContext.UserId!.Value;
        fixture.Context.Users.Add(new User { Id = userId, TenantId = fixture.TenantId, Email = $"{userId:N}@example.test", FirstName = "HR", LastName = "Admin" });
        var employmentId = await fixture.Context.EmployeeEmploymentHistory.Where(x => x.EmployeeId == fixture.EmployeeId).Select(x => x.Id).SingleAsync();
        var leaveDate = new DateOnly(2026, 9, 19);
        var leaveTypeId = Guid.NewGuid(); var leavePeriodId = Guid.NewGuid(); var policyId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var ruleId = Guid.NewGuid(); var leaveId = Guid.NewGuid();
        fixture.Context.AddRange(
            new LeaveType { Id = leaveTypeId, TenantId = fixture.TenantId, Code = $"LT-{leaveId:N}"[..10], Name = "Test Leave" },
            new LeavePeriod { Id = leavePeriodId, TenantId = fixture.TenantId, Code = $"LP-{leaveId:N}"[..10], Name = "Test Period", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) },
            new LeavePolicy { Id = policyId, TenantId = fixture.TenantId, Code = $"POL-{leaveId:N}"[..12], Name = "Test Policy" },
            new LeavePolicyVersion { Id = versionId, TenantId = fixture.TenantId, LeavePolicyId = policyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published },
            new LeavePolicyRule { Id = ruleId, TenantId = fixture.TenantId, LeavePolicyVersionId = versionId, LeaveTypeId = leaveTypeId },
            new LeaveRequest { Id = leaveId, TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, LeaveTypeId = leaveTypeId, LeavePeriodId = leavePeriodId, LeavePolicyVersionId = versionId, LeavePolicyRuleId = ruleId, EmployeeEmploymentHistoryId = employmentId, PolicyGenderSnapshot = Gender.Unspecified, StartDate = leaveDate, EndDate = leaveDate, RequestedQuantity = 1, ChargeableQuantity = 1, Status = LeaveRequestStatus.Approved, SubmittedAtUtc = DateTime.UtcNow, IdempotencyKey = $"leave-{leaveId:N}", PayloadFingerprint = leaveId.ToString("N"), Days = [new LeaveRequestDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, LeaveRequestId = leaveId, Date = leaveDate, RequestedQuantity = 1, ChargeableQuantity = 1 }] });
        var odDate = new DateOnly(2026, 9, 20);
        var odId = Guid.NewGuid();
        fixture.Context.AttendanceOnDutyRequests.Add(new AttendanceOnDutyRequest { Id = odId, TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, StartDate = odDate, EndDate = odDate, Reason = "Approved OD", Status = AttendanceRequestStatus.Approved, SubmittedByUserId = userId, SubmittedAtUtc = DateTime.UtcNow, ReviewedByUserId = userId, ReviewedAtUtc = DateTime.UtcNow });
        await fixture.Context.SaveChangesAsync();
        var tenant = new TestTenantContext(fixture.TenantId, userId);
        await using var db = fixture.CreateContext(fixture.TenantId, out _);
        var service = new AttendanceAdminCorrectionService(db, tenant, new EffectiveEmploymentResolver(db, tenant), new NoOpProcessor(), new AttendancePeriodLockService(db, tenant));

        var leaveResult = await service.CreateAsync(new(fixture.EmployeeId, leaveDate, new DateTime(2026, 9, 19, 8, 0, 0, DateTimeKind.Utc), null, "leave conflict"));
        var odResult = await service.CreateAsync(new(fixture.EmployeeId, odDate, new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc), null, "od conflict"));

        Assert.Equal(ResultStatus.Conflict, leaveResult.Status);
        Assert.Equal(ResultStatus.Conflict, odResult.Status);
        Assert.Empty(await db.AttendanceAdminCorrections.AsNoTracking().ToListAsync());
        Assert.Equal(AttendanceRequestStatus.Approved, await db.AttendanceOnDutyRequests.Where(x => x.Id == odId).Select(x => x.Status).SingleAsync());
        Assert.Equal(LeaveRequestStatus.Approved, await db.LeaveRequests.Where(x => x.Id == leaveId).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Admin_correction_makes_monthly_summary_stale_and_supports_reopen_reprocess_reclose()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var userId = fixture.TenantContext.UserId!.Value;
        fixture.Context.Users.Add(new User { Id = userId, TenantId = fixture.TenantId, Email = $"{userId:N}@example.test", FirstName = "HR", LastName = "Admin" });
        var shift = await fixture.AddShiftAsync("ADMIN-MONTHLY");
        await fixture.AddRuleAsync(shift.Id, employeeId: fixture.EmployeeId);
        var date = new DateOnly(2026, 9, 21);
        var rawIn = new DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc);
        var rawOut = new DateTime(2026, 9, 21, 17, 0, 0, DateTimeKind.Utc);
        fixture.Context.AttendancePunches.AddRange(
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = rawIn, CapturedAtUtc = rawIn, Direction = PunchDirection.In, Source = PunchSource.Biometric },
            new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = rawOut, CapturedAtUtc = rawOut, Direction = PunchDirection.Out, Source = PunchSource.Biometric });
        for (var day = new DateOnly(2026, 9, 1); day <= new DateOnly(2026, 9, 30); day = day.AddDays(1))
            if (day != date) fixture.Context.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = day, Status = EmployeeAttendanceDayStatus.Holiday, ProcessedAtUtc = DateTime.UtcNow });
        await fixture.Context.SaveChangesAsync();

        var tenant = new TestTenantContext(fixture.TenantId, userId);
        await using var db = fixture.CreateContext(fixture.TenantId, out _);
        var dayProcessor = new AttendanceDayProcessor(db, tenant, new AttendanceFoundationService(db, tenant, new EffectiveEmploymentResolver(db, tenant)), new FixedClock(new DateTimeOffset(2026, 9, 21, 20, 0, 0, TimeSpan.Zero)), new AttendancePeriodLockService(db, tenant));
        Assert.True((await dayProcessor.ProcessAsync(fixture.EmployeeId, date)).Succeeded);
        var monthly = new AttendanceMonthlyProcessor(db, tenant);
        var period = (await monthly.CreatePeriodAsync(new(2026, 9))).Value!;
        Assert.True((await monthly.ProcessAsync(period.Id)).Succeeded);
        var original = await db.EmployeeAttendanceMonthlySummaries.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Id);
        var initialVersion = await db.AttendancePeriods.Where(x => x.Id == period.Id).Select(x => x.DataVersion).SingleAsync();
        Assert.Equal(initialVersion, original.SourceDataVersion);
        Assert.True((await monthly.CloseAsync(period.Id)).Succeeded);
        Assert.Equal(ResultStatus.Conflict, (await new AttendanceAdminCorrectionService(db, tenant, new EffectiveEmploymentResolver(db, tenant), new ThrowingProcessor(), new AttendancePeriodLockService(db, tenant)).CreateAsync(new(fixture.EmployeeId, date, new DateTime(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc), rawOut, "closed"))).Status);
        Assert.True((await monthly.ReopenAsync(period.Id, new("Admin correction required"))).Succeeded);

        var correctionService = new AttendanceAdminCorrectionService(db, tenant, new EffectiveEmploymentResolver(db, tenant), dayProcessor, new AttendancePeriodLockService(db, tenant));
        var correction = await correctionService.CreateAsync(new(fixture.EmployeeId, date, new DateTime(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 21, 15, 0, 0, DateTimeKind.Utc), "reopened correction"));
        Assert.True(correction.Succeeded, correction.Message);
        var periodAfterCorrection = await db.AttendancePeriods.AsNoTracking().SingleAsync(x => x.Id == period.Id);
        var stale = await db.EmployeeAttendanceMonthlySummaries.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Id);
        Assert.True(periodAfterCorrection.DataVersion > initialVersion);
        Assert.NotEqual(periodAfterCorrection.DataVersion, stale.SourceDataVersion);
        Assert.Equal(ResultStatus.Conflict, (await new AttendanceMonthlyProcessor(db, tenant).CloseAsync(period.Id)).Status);

        Assert.True((await new AttendanceMonthlyProcessor(db, tenant).ProcessAsync(period.Id)).Succeeded);
        var refreshed = await db.EmployeeAttendanceMonthlySummaries.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Id);
        var finalPeriod = await db.AttendancePeriods.AsNoTracking().SingleAsync(x => x.Id == period.Id);
        Assert.Equal(finalPeriod.DataVersion, refreshed.SourceDataVersion);
        Assert.NotEqual(original.ActualWorkMinutes, refreshed.ActualWorkMinutes);
        Assert.Equal(1, await db.EmployeeAttendanceMonthlySummaries.CountAsync(x => x.AttendancePeriodId == period.Id));
        Assert.True((await new AttendanceMonthlyProcessor(db, tenant).CloseAsync(period.Id)).Succeeded);
        Assert.Equal(AttendancePeriodStatus.Closed, await db.AttendancePeriods.Where(x => x.Id == period.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(2, await db.AttendancePeriodEvents.CountAsync(x => x.AttendancePeriodId == period.Id && x.EventType == AttendancePeriodEventType.Closed));
        Assert.Equal(1, await db.AttendancePeriodEvents.CountAsync(x => x.AttendancePeriodId == period.Id && x.EventType == AttendancePeriodEventType.Reopened));
    }

    private static AttendanceAdminCorrectionService CreateService(HRMS.Infrastructure.Persistence.HrmsDbContext db, TestTenantContext tenant)
        => new(db, tenant, new EffectiveEmploymentResolver(db, tenant), new AttendanceDayProcessor(db, tenant, new AttendanceFoundationService(db, tenant, new EffectiveEmploymentResolver(db, tenant)), new FixedClock(new DateTimeOffset(2026, 9, 15, 20, 0, 0, TimeSpan.Zero)), new AttendancePeriodLockService(db, tenant)), new AttendancePeriodLockService(db, tenant), new FixedClock(new DateTimeOffset(2026, 9, 15, 20, 0, 0, TimeSpan.Zero)));

    private sealed class ThrowingProcessor : IAttendanceDayProcessor
    { public Task<Result<EmployeeAttendanceDayDto>> ProcessAsync(Guid employeeId, DateOnly businessDate, CancellationToken cancellationToken = default) => throw new InvalidOperationException("must not process a closed date"); }

    private sealed class FailingProcessor : IAttendanceDayProcessor
    { public Task<Result<EmployeeAttendanceDayDto>> ProcessAsync(Guid employeeId, DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult(Result<EmployeeAttendanceDayDto>.Conflict("forced processing failure")); }

    private sealed class NoOpProcessor : IAttendanceDayProcessor
    {
        public Task<Result<EmployeeAttendanceDayDto>> ProcessAsync(Guid employeeId, DateOnly businessDate, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<EmployeeAttendanceDayDto>.Success(new(
                Guid.NewGuid(), employeeId, businessDate, null, null, null, null, null,
                RosterAssignmentSource.System, RosterDayType.Shift, EmployeeAttendanceDayStatus.NotProcessed,
                null, null, 0, 0, null, null, false, false, false, false, false, false, false, false, false,
                DateTime.UtcNow, "test")));
    }
}
