using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

internal static class OvertimeProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, Guid tenantId, TestTenantContext tenantContext)
    {
        var employeeId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var date = new DateOnly(2026, 9, 15);
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"OT{tenantId:N}"[..12], TenantName = "Overtime Provider Test", Host = $"{tenantId:N}.provider.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"OT-{employeeId:N}"[..10], FirstName = "Provider", LastName = "Overtime", Email = $"{employeeId:N}@provider.test", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active });
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active });
        db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, BusinessDate = date, Status = EmployeeAttendanceDayStatus.Present, ExpectedWorkMinutes = 480, WorkedMinutes = 540, ProcessedAtUtc = DateTime.UtcNow });
        db.AttendancePeriods.Add(new AttendancePeriod { Id = periodId, TenantId = tenantId, Year = 2026, Month = 9, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), Status = AttendancePeriodStatus.Closed, DataVersion = 1, ConcurrencyVersion = 1 });
        db.PayrollAttendanceSnapshots.Add(new PayrollAttendanceSnapshot { Id = Guid.NewGuid(), TenantId = tenantId, AttendancePeriodId = periodId, EmployeeId = employeeId, Version = 1, IsCurrent = true, PeriodStart = new(2026, 9, 1), PeriodEnd = new(2026, 9, 30), EligibleDays = 30, PayableDays = 30, LopDays = 0, FinalizedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        tenantContext.TenantId = tenantId;
        var service = new OvertimeService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var policy = await service.CreatePolicyAsync(new("OT-PROVIDER", "Provider OT", new(2026, 1, 1), null, 0, 0, OvertimeRoundingMode.None, null, null, false, true, true, true, true, 1.5m, 2m, 2m, "All"));
        Assert.True(policy.Succeeded, policy.Message);
        var request = await service.CreateRequestAsync(new(employeeId, date, 60, OvertimeCategory.NormalDay, "provider"));
        Assert.True(request.Succeeded, request.Message);
        Assert.True((await service.SubmitAsync(request.Value!.Id)).Succeeded);
        Assert.True((await service.ApproveAsync(request.Value.Id, new())).Succeeded);
        var first = await service.FinalizeAsync(periodId);
        Assert.True(first.Succeeded, first.Message);
        Assert.Equal(1, await db.PayrollOvertimeSnapshots.CountAsync(x => x.TenantId == tenantId && x.IsCurrent));
        Assert.True((await service.ReopenAsync(periodId, "provider correction")).Succeeded);
        Assert.True((await service.FinalizeAsync(periodId)).Succeeded);
        Assert.Equal(1, await db.PayrollOvertimeSnapshots.CountAsync(x => x.TenantId == tenantId && x.IsCurrent));
        Assert.Equal(2, await db.PayrollOvertimeSnapshots.CountAsync(x => x.TenantId == tenantId));
        var resolved = await service.ResolveAsync(employeeId, new(2026, 9, 1), new(2026, 9, 30));
        Assert.True(resolved.Succeeded, resolved.Message);
        Assert.Equal(2, resolved.Value!.Version);
    }
}
