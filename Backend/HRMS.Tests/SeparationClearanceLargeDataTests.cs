using System.Diagnostics;
using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationClearanceLargeDataTests
{
    [Fact]
    public async Task One_thousand_clearance_cases_support_generation_and_paged_operational_queries()
    {
        using var database = new SqliteInMemoryDatabase(); var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2)); var sw = Stopwatch.StartNew();
        Guid reasonId;
        await using (var seed = database.CreateContext(new TestTenantContext(fixture.TenantId)))
        {
            reasonId = await seed.SeparationReasons.Select(x => x.Id).SingleAsync();
            var employees = Enumerable.Range(1, 1000).Select(i => new Employee { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeCode = $"LD-{i:0000}", FirstName = "Large", LastName = $"Employee {i}", Email = $"ld-{i}@test.local", DateOfJoining = new(2024, 1, 1), Status = EmployeeStatus.Active, ReportingManagerId = fixture.EmployeeId }).ToList();
            seed.Employees.AddRange(employees);
            seed.EmployeeSeparations.AddRange(employees.Select((e, i) => new EmployeeSeparation { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = e.Id, SeparationNumber = $"LD-{i:0000}", SeparationType = SeparationType.EmployeeInitiated, ReasonId = reasonId, InitiatedBy = EmployeeSeparationInitiator.Employee, RequestDate = new(2026, 9, 23), ProposedLastWorkingDate = new(2026, 10, 2), ApprovedLastWorkingDate = new(2026, 10, 2), NoticeStartDate = new(2026, 9, 23), NoticeEndDate = new(2026, 10, 2), Status = EmployeeSeparationStatus.Approved, CreatedByUserId = fixture.HrUserId }));
            await seed.SaveChangesAsync();
        }
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)); var service = new ClearanceService(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId), new EmployeeIdentityResolver(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId)), new EmployeeManagerResolver(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId)), TimeProvider.System);
        var template = await service.CreateTemplateAsync(new("LARGE", "Large clearance", null, new(2020, 1, 1), null, null, null, Enumerable.Range(1, 5).Select(i => new ClearanceTemplateItemRequest($"ITEM-{i}", $"Item {i}", null, i == 3 ? SeparationClearanceTaskCategory.Asset : SeparationClearanceTaskCategory.Hr, ClearanceOwnerType.Hr, null, true, false, false, false, i, null)).ToArray())); Assert.True(template.Succeeded, template.Message);
        var separations = await db.EmployeeSeparations.Where(x => x.TenantId == fixture.TenantId && x.SeparationNumber.StartsWith("LD-")).Select(x => x.Id).ToListAsync();
        foreach (var separationId in separations) { var started = await service.StartAsync(separationId); Assert.True(started.Succeeded, started.Message); }
        var page = await service.GetFunctionalInboxAsync(new() { Page = 1, PageSize = 50 }); var dashboard = await service.GetHrDashboardAsync(new() { Page = 1, PageSize = 50 });
        var cases = await db.SeparationClearances.CountAsync(x => x.TenantId == fixture.TenantId); var tasks = await db.SeparationClearanceTasks.CountAsync(x => x.TenantId == fixture.TenantId); sw.Stop(); Assert.Equal(1000, cases); Assert.Equal(5000, tasks); Assert.True(page.Succeeded && page.Value!.Items.Count <= 50); Assert.True(dashboard.Succeeded && dashboard.Value!.Items.Count <= 50); Assert.True(sw.Elapsed < TimeSpan.FromMinutes(8));
    }
}
