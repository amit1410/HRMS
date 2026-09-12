using System.Net;
using System.Net.Http.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

public sealed class AttendanceAdminCorrectionHttpTests : IClassFixture<HrmsApiFactory>
{
    private readonly HrmsApiFactory factory;

    public AttendanceAdminCorrectionHttpTests(HrmsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Manage_permission_can_create_correction_through_http()
    {
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateAsync(
            $"ADMIN-CORR-{Guid.NewGuid():N}", [Permissions.Attendance.AdminCorrectionManage]);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HrmsDbContext>();
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
            {
                Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId,
                EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active,
                CreatedBy = "admin-correction-http-test"
            });
            await db.SaveChangesAsync();
            var resolver = new HRMS.Application.Services.EffectiveEmploymentResolver(db, new TestTenantContext(scenario.TenantId, scenario.UserId));
            var resolved = await resolver.ResolveAsync(scenario.TenantId, scenario.EmployeeId, new(2026, 9, 16));
            Assert.Equal(EffectiveEmploymentResolutionStatus.Resolved, resolved.Status);
        });
        var request = new AdminAttendanceCorrectionRequest(
            scenario.EmployeeId, new(2026, 9, 16),
            new DateTime(2026, 9, 16, 8, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 16, 17, 0, 0, DateTimeKind.Utc), "HTTP correction");
        using var response = await scenario.EmployeeClient.PostAsJsonAsync("/api/attendance/admin-corrections", request);

        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {responseBody}");
        var envelope = System.Text.Json.JsonSerializer.Deserialize<ApiResponse<AdminAttendanceCorrectionDto>>(responseBody,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(envelope?.Data);
        Assert.Equal(scenario.EmployeeId, envelope!.Data!.EmployeeId);
        Assert.Equal("HTTP correction", envelope.Data.Reason);
        Assert.Equal(scenario.UserId, envelope.Data.CreatedByUserId);
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HrmsDbContext>();
            Assert.True(await db.AttendanceAdminCorrections.AnyAsync(x => x.Id == envelope.Data.Id));
            var day = await db.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == scenario.EmployeeId && x.BusinessDate == request.BusinessDate);
            Assert.Equal(request.CorrectedInAtUtc, day.FirstPunchAtUtc);
            Assert.Equal(request.CorrectedOutAtUtc, day.LastPunchAtUtc);
        });
    }

    [Fact]
    public async Task Missing_manage_permission_and_foreign_employee_are_denied()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var denied = await builder.CreateAsync($"ADMIN-CORR-DENY-{Guid.NewGuid():N}");
        var foreign = await builder.CreateAsync($"ADMIN-CORR-FOREIGN-{Guid.NewGuid():N}", [Permissions.Attendance.AdminCorrectionManage]);
        await AddEmploymentHistoryAsync(denied);
        using (var response = await denied.EmployeeClient.GetAsync("/api/attendance/admin-corrections"))
        {
            Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        }

        var request = new AdminAttendanceCorrectionRequest(
            denied.EmployeeId, new(2026, 9, 17), new DateTime(2026, 9, 17, 8, 30, 0, DateTimeKind.Utc), null, "denied");
        using (var response = await denied.EmployeeClient.PostAsJsonAsync("/api/attendance/admin-corrections", request))
        {
            Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        }

        using var foreignFilter = await foreign.EmployeeClient.GetAsync($"/api/attendance/admin-corrections?employeeId={denied.EmployeeId}");
        Assert.Equal(HttpStatusCode.OK, foreignFilter.StatusCode);
    }

    [Fact]
    public async Task Manager_without_manage_permission_cannot_create_correction_for_direct_report()
    {
        var scenario = await new AttendanceHttpEmployeeScenarioBuilder(factory).CreateManagerAsync(
            $"ADMIN-CORR-MANAGER-{Guid.NewGuid():N}",
            managerPermissions: [Permissions.Attendance.View, Permissions.Attendance.MonthlyViewTeam]);
        var request = new AdminAttendanceCorrectionRequest(
            scenario.EmployeeId, new(2026, 9, 10), new DateTime(2026, 9, 10, 8, 30, 0, DateTimeKind.Utc), null, "manager attempt");

        using var response = await scenario.ManagerClient.PostAsJsonAsync("/api/attendance/admin-corrections", request);

        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        await new AttendanceHttpEmployeeScenarioBuilder(factory).ExecuteTenantAsync(scenario, async db =>
        {
            Assert.Empty(await db.AttendanceAdminCorrections.Where(x => x.EmployeeId == scenario.EmployeeId).ToListAsync());
        });
    }

    [Fact]
    public async Task Foreign_correction_detail_is_not_visible_through_http()
    {
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var owner = await builder.CreateAsync($"ADMIN-CORR-DETAIL-A-{Guid.NewGuid():N}", [Permissions.Attendance.AdminCorrectionManage]);
        var other = await builder.CreateAsync($"ADMIN-CORR-DETAIL-B-{Guid.NewGuid():N}", [Permissions.Attendance.AdminCorrectionManage]);
        await AddEmploymentHistoryAsync(owner);
        var request = new AdminAttendanceCorrectionRequest(
            owner.EmployeeId, new(2026, 9, 18), new DateTime(2026, 9, 18, 8, 30, 0, DateTimeKind.Utc), null, "owner");
        using var created = await owner.EmployeeClient.PostAsJsonAsync("/api/attendance/admin-corrections", request);
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.IsSuccessStatusCode, $"{created.StatusCode}: {createdBody}");
        var envelope = System.Text.Json.JsonSerializer.Deserialize<ApiResponse<AdminAttendanceCorrectionDto>>(createdBody,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(envelope?.Data);

        using var response = await other.EmployeeClient.GetAsync($"/api/attendance/admin-corrections/{envelope!.Data!.Id}");
        Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
    }

    private Task AddEmploymentHistoryAsync(AttendanceHttpEmployeeScenario scenario) =>
        factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HrmsDbContext>();
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
            {
                Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId,
                EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active,
                CreatedBy = "admin-correction-http-test"
            });
            await db.SaveChangesAsync();
        });

}
