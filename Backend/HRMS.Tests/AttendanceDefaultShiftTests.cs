using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Attendance;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceDefaultShiftTests
{
    [Fact]
    public async Task Valid_default_resolves_when_no_more_specific_assignment_exists()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        var shift = await fixture.AddShiftAsync("DEFAULT", isDefault: true);

        var result = await fixture.Service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 10));

        Assert.True(result.Succeeded);
        Assert.Equal(shift.Id, result.Value!.ShiftId);
        Assert.Contains("default", result.Value.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inactive_default_is_ignored()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("INACTIVE", isDefault: true, isActive: false);

        var result = await fixture.Service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 10));

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.ShiftId);
        Assert.Contains("NotConfigured", result.Value.Message);
    }

    [Fact]
    public async Task Expired_default_is_ignored()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("EXPIRED", isDefault: true, effectiveTo: new(2026, 10, 9));

        var result = await fixture.Service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 10));

        Assert.Null(result.Value!.ShiftId);
        Assert.Contains("NotConfigured", result.Value.Message);
    }

    [Fact]
    public async Task Future_default_is_ignored()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("FUTURE", isDefault: true, effectiveFrom: new(2026, 10, 11));

        var result = await fixture.Service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 10));

        Assert.Null(result.Value!.ShiftId);
        Assert.Contains("NotConfigured", result.Value.Message);
    }

    [Fact]
    public async Task No_default_returns_NotConfigured_instead_of_an_arbitrary_shift()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("ORDINARY");

        var result = await fixture.Service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 10));

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.ShiftId);
        Assert.Contains("NotConfigured", result.Value.Message);
    }

    [Fact]
    public async Task Overlapping_default_is_rejected_on_save()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("DEFAULT-A", isDefault: true, effectiveFrom: new(2026, 1, 1), effectiveTo: new(2026, 12, 31));

        var result = await fixture.Service.CreateShiftAsync(fixture.Request("DEFAULT-B", isDefault: true, effectiveFrom: new(2026, 6, 1), effectiveTo: new(2026, 6, 30)));

        Assert.False(result.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, result.Status);
        Assert.Contains("overlaps", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Persisted_overlapping_defaults_return_configuration_ambiguity()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("DEFAULT-A", isDefault: true, effectiveFrom: new(2026, 1, 1), effectiveTo: new(2026, 12, 31));
        await fixture.AddShiftAsync("DEFAULT-B", isDefault: true, effectiveFrom: new(2026, 6, 1), effectiveTo: new(2026, 6, 30), bypassDefaultValidation: true);

        var result = await fixture.Service.ResolveAsync(fixture.EmployeeId, new(2026, 6, 15));

        Assert.False(result.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, result.Status);
        Assert.Contains("Multiple active default", result.Message);
    }

    [Fact]
    public async Task Applicability_beats_default()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("DEFAULT", isDefault: true);
        var applicable = await fixture.AddShiftAsync("MORNING");
        fixture.Context.ShiftApplicabilityRules.Add(new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftId = applicable.Id, DepartmentId = fixture.DepartmentId, Priority = 10, EffectiveFrom = new(2026, 1, 1) });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 10));

        Assert.Equal(applicable.Id, result.Value!.ShiftId);
    }

    [Fact]
    public async Task Manual_roster_beats_default()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("DEFAULT", isDefault: true);
        var manual = await fixture.AddShiftAsync("EVENING");
        fixture.Context.EmployeeRosterDays.Add(new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 10, 10), ShiftId = manual.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Manual, IsOverride = true });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 10));

        Assert.Equal(manual.Id, result.Value!.ShiftId);
        Assert.Equal(RosterAssignmentSource.Manual, result.Value.Source);
    }

    [Fact]
    public async Task Uploaded_roster_beats_default()
    {
        using var fixture = await AttendanceTestFixture.CreateAsync();
        await fixture.AddShiftAsync("DEFAULT", isDefault: true);
        var uploaded = await fixture.AddShiftAsync("NIGHT");
        fixture.Context.EmployeeRosterDays.Add(new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, RosterDate = new(2026, 10, 10), ShiftId = uploaded.Id, DayType = RosterDayType.Shift, AssignmentSource = RosterAssignmentSource.Upload, IsOverride = true });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.ResolveAsync(fixture.EmployeeId, new(2026, 10, 10));

        Assert.Equal(uploaded.Id, result.Value!.ShiftId);
        Assert.Equal(RosterAssignmentSource.Upload, result.Value.Source);
    }
}

internal sealed class AttendanceTestFixture : IDisposable
{
    private readonly SqliteInMemoryDatabase _database;
    private readonly TestTenantContext _tenant;
    public HRMS.Infrastructure.Persistence.HrmsDbContext Context { get; }
    public AttendanceFoundationService Service { get; }
    public ITenantContext TenantContext => _tenant;
    public Shift? Shift { get; set; }
    public AttendanceFoundationService CalendarService => new(Context, _tenant, new EffectiveEmploymentResolver(Context, _tenant), new WorkingDayCalendarResolver(Context, _tenant, new EffectiveEmploymentResolver(Context, _tenant)));
    public Guid TenantId { get; private set; }
    public Guid EmployeeId { get; } = Guid.NewGuid();
    public Guid DepartmentId { get; } = Guid.NewGuid();
    public Guid OtherDepartmentId { get; } = Guid.NewGuid();
    public Guid WorkLocationId { get; } = Guid.NewGuid();
    public Guid OtherWorkLocationId { get; } = Guid.NewGuid();
    public Guid EmployeeTypeId { get; } = Guid.NewGuid();
    public Guid GradeId { get; } = Guid.NewGuid();
    public Guid DesignationId { get; } = Guid.NewGuid();
    public Guid CostCenterId { get; } = Guid.NewGuid();

    private AttendanceTestFixture(SqliteInMemoryDatabase database, TestTenantContext tenant, HRMS.Infrastructure.Persistence.HrmsDbContext context)
    {
        _database = database; _tenant = tenant; Context = context;
        Service = new AttendanceFoundationService(context, tenant, new EffectiveEmploymentResolver(context, tenant));
    }

    public static async Task<AttendanceTestFixture> CreateAsync()
    {
        var database = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
        var context = database.CreateContext(tenant);
        var fixture = new AttendanceTestFixture(database, tenant, context) { TenantId = tenant.TenantId!.Value };
        context.Tenants.Add(new Tenant { Id = fixture.TenantId, TenantCode = $"T-{fixture.TenantId:N}"[..20], Host = $"{fixture.TenantId:N}.test", ShardKey = fixture.TenantId.ToString("N"), TenantName = "Attendance Test" });
        context.Departments.Add(new Department { Id = fixture.DepartmentId, TenantId = fixture.TenantId, Code = "IT", Name = "IT" });
        context.Departments.Add(new Department { Id = fixture.OtherDepartmentId, TenantId = fixture.TenantId, Code = "HR", Name = "HR" });
        context.WorkLocations.AddRange(new WorkLocation { Id = fixture.WorkLocationId, TenantId = fixture.TenantId, Code = "NOI", Name = "Noida" }, new WorkLocation { Id = fixture.OtherWorkLocationId, TenantId = fixture.TenantId, Code = "GUR", Name = "Gurgaon" });
        context.EmployeeTypes.Add(new EmployeeType { Id = fixture.EmployeeTypeId, TenantId = fixture.TenantId, Code = "PERM", Name = "Permanent" });
        context.Grades.Add(new Grade { Id = fixture.GradeId, TenantId = fixture.TenantId, Code = "G5", Name = "G5" });
        context.Designations.Add(new Designation { Id = fixture.DesignationId, TenantId = fixture.TenantId, Code = "MGR", Name = "Manager" });
        context.CostCenters.Add(new CostCenter { Id = fixture.CostCenterId, TenantId = fixture.TenantId, Code = "CC01", Name = "CC01" });
        context.Employees.Add(new Employee { Id = fixture.EmployeeId, TenantId = fixture.TenantId, EmployeeCode = "E001", FirstName = "Test", LastName = "Employee", Email = $"{fixture.EmployeeId:N}@example.test", DateOfJoining = new(2026, 1, 1) });
        context.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2026, 1, 1), DepartmentId = fixture.DepartmentId, WorkLocationId = fixture.WorkLocationId, EmployeeTypeId = fixture.EmployeeTypeId, GradeId = fixture.GradeId, DesignationId = fixture.DesignationId, CostCenterId = fixture.CostCenterId, EmploymentStatus = EmployeeStatus.Active });
        await context.SaveChangesAsync();
        return fixture;
    }

    public async Task AddEmployeeAsync(Guid employeeId, string code, Guid? departmentId = null, Guid? workLocationId = null, Guid? employeeTypeId = null, Guid? gradeId = null, Guid? designationId = null, Guid? costCenterId = null, DateOnly? effectiveFrom = null, DateOnly? effectiveTo = null)
    {
        Context.Employees.Add(new Employee { Id = employeeId, TenantId = TenantId, EmployeeCode = code, FirstName = "Test", LastName = code, Email = $"{employeeId:N}@example.test", DateOfJoining = effectiveFrom ?? new(2026, 1, 1) });
        Context.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, EffectiveFrom = effectiveFrom ?? new(2026, 1, 1), EffectiveTo = effectiveTo, DepartmentId = departmentId, WorkLocationId = workLocationId, EmployeeTypeId = employeeTypeId, GradeId = gradeId, DesignationId = designationId, CostCenterId = costCenterId, EmploymentStatus = EmployeeStatus.Active });
        await Context.SaveChangesAsync();
    }

    public async Task AddRuleAsync(Guid? shiftId, Guid? departmentId = null, Guid? patternId = null, Guid? employeeId = null, Guid? workLocationId = null, int priority = 10, DateOnly? effectiveFrom = null, DateOnly? effectiveTo = null, string? ruleName = null)
    {
        Context.ShiftApplicabilityRules.Add(new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = TenantId, RuleName = ruleName ?? $"Rule-{Guid.NewGuid():N}"[..13], ShiftId = shiftId, ShiftPatternId = patternId, EmployeeId = employeeId, DepartmentId = departmentId, WorkLocationId = workLocationId, Priority = priority, EffectiveFrom = effectiveFrom ?? new(2026, 1, 1), EffectiveTo = effectiveTo });
        await Context.SaveChangesAsync();
    }

    public void SwitchTenant(Guid tenantId) => _tenant.TenantId = tenantId;

    public HRMS.Infrastructure.Persistence.HrmsDbContext CreateContext(Guid tenantId, out TestTenantContext tenant)
    {
        tenant = new TestTenantContext(tenantId);
        return _database.CreateContext(tenant);
    }

    public async Task AddHolidayAsync(DateOnly date)
    {
        Context.Holidays.Add(new Holiday { Id = Guid.NewGuid(), TenantId = TenantId, Name = "Test Holiday", Date = date, IsActive = true });
        await Context.SaveChangesAsync();
    }

    public async Task AddWeeklyOffAsync(DayOfWeek day)
    {
        var configuration = new WeeklyOffConfiguration { Id = Guid.NewGuid(), TenantId = TenantId, EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31), IsActive = true };
        configuration.Days.Add(new WeeklyOffDay { Id = Guid.NewGuid(), TenantId = TenantId, WeeklyOffConfigurationId = configuration.Id, DayOfWeek = day });
        Context.WeeklyOffConfigurations.Add(configuration);
        await Context.SaveChangesAsync();
    }

    public ShiftRequest Request(string code, bool isDefault = false, DateOnly? effectiveFrom = null, DateOnly? effectiveTo = null, bool isActive = true) => new() { ShiftCode = code, ShiftName = code, IsDefault = isDefault, IsActive = isActive, EffectiveFrom = effectiveFrom ?? new(2026, 1, 1), EffectiveTo = effectiveTo, StartTime = new(9, 0), EndTime = new(18, 0), FullDayWorkMinutes = 480, MinimumWorkMinutes = 480, CaptureMode = AttendanceCaptureMode.BiometricOnly };

    public async Task<Shift> AddShiftAsync(string code, bool isDefault = false, bool isActive = true, DateOnly? effectiveFrom = null, DateOnly? effectiveTo = null, bool bypassDefaultValidation = false)
    {
        if (bypassDefaultValidation)
        {
            var item = new Shift { Id = Guid.NewGuid(), TenantId = TenantId, ShiftCode = code, ShiftName = code, IsDefault = isDefault, IsActive = isActive, EffectiveFrom = effectiveFrom ?? new(2026, 1, 1), EffectiveTo = effectiveTo, StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 480 };
            Context.Shifts.Add(item); await Context.SaveChangesAsync(); return item;
        }
        var result = await Service.CreateShiftAsync(Request(code, isDefault, effectiveFrom, effectiveTo, isActive));
        Assert.True(result.Succeeded, result.Message);
        return await Context.Shifts.SingleAsync(x => x.Id == result.Value!.Id);
    }

    public void Dispose() { Context.Dispose(); _database.Dispose(); }
}
