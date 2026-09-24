using System.Diagnostics;
using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using SeparationReasonEntity = HRMS.Domain.Entities.Separation.SeparationReason;

namespace HRMS.Tests;

public sealed class SeparationExitLargeDataTests
{
    [Fact]
    public async Task Dashboard_and_one_hundred_real_exit_executions_scale_across_one_thousand_separations()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var hrUserId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var ids = await SeedAsync(database, tenantId, hrUserId, today, 1000);

        await using var dashboardDb = database.CreateContext(new TestTenantContext(tenantId, hrUserId));
        var service = new SeparationExitService(dashboardDb, new TestTenantContext(tenantId, hrUserId), TimeProvider.System);
        var dashboardClock = Stopwatch.StartNew();
        var readyPage = await service.DashboardAsync(new SeparationExitDashboardQuery { Page = 1, PageSize = 50 });
        var secondPage = await service.DashboardAsync(new SeparationExitDashboardQuery { Page = 2, PageSize = 50 });
        dashboardClock.Stop();
        Assert.True(readyPage.Succeeded, readyPage.Message);
        Assert.True(secondPage.Succeeded, secondPage.Message);
        Assert.Equal(1000, readyPage.Value!.TotalCount);
        Assert.Equal(50, readyPage.Value.Items.Count);
        Assert.Equal(50, secondPage.Value!.Items.Count);
        Assert.All(readyPage.Value.Items, x => Assert.True(x.Ready));

        var executionClock = Stopwatch.StartNew();
        foreach (var separationId in ids.Take(100))
        {
            await using var db = database.CreateIsolatedContext(new TestTenantContext(tenantId, hrUserId));
            var result = await new SeparationExitService(db, new TestTenantContext(tenantId, hrUserId), TimeProvider.System)
                .ExecuteAsync(separationId, new());
            Assert.True(result.Succeeded, result.Message);
        }
        executionClock.Stop();

        await using var verify = database.CreateIsolatedContext(new TestTenantContext(tenantId, hrUserId));
        Assert.Equal(100, await verify.EmployeeSeparations.CountAsync(x => x.TenantId == tenantId && x.Status == EmployeeSeparationStatus.Closed));
        Assert.Equal(100, await verify.SeparationExitExecutionEvents.CountAsync(x => x.TenantId == tenantId && x.EventType == SeparationExitExecutionEventType.SeparationClosed));
        Assert.Equal(100, await verify.Employees.CountAsync(x => x.TenantId == tenantId && x.Status == EmployeeStatus.Terminated));
        Assert.Equal(100, await verify.EmployeeEmploymentHistory.CountAsync(x => x.TenantId == tenantId && x.EmploymentStatus == EmployeeStatus.Terminated));
        Assert.Equal(100, await verify.Users.CountAsync(x => x.TenantId == tenantId && !x.IsActive));

        Console.WriteLine($"Phase 8H large data: Separations=1000, ActualExitExecutions=100, Ready=1000, Blocked=0, Closed=100, PageSize=50, Dashboard={dashboardClock.Elapsed.TotalSeconds:F2}s, Execution={executionClock.Elapsed.TotalSeconds:F2}s");
    }

    private static async Task<IReadOnlyList<Guid>> SeedAsync(SqliteInMemoryDatabase database, Guid tenantId, Guid hrUserId, DateOnly lwd, int count)
    {
        var reasonId = Guid.NewGuid();
        var separationIds = new List<Guid>(count);
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"H{tenantId:N}"[..12], Host = $"{tenantId:N}.exit.test", ShardKey = tenantId.ToString("N") });
        db.Users.Add(new User { Id = hrUserId, TenantId = tenantId, Email = $"hr-{tenantId:N}@test.local", PasswordHash = "test-hash", FirstName = "Exit", LastName = "HR", IsActive = true });
        db.SeparationReasons.Add(new SeparationReasonEntity { Id = reasonId, TenantId = tenantId, Code = "LARGE", Name = "Large data", Category = SeparationReasonCategory.Resignation, EffectiveFrom = lwd.AddYears(-1), IsActive = true });

        for (var i = 0; i < count; i++)
        {
            var employeeId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var separationId = Guid.NewGuid();
            separationIds.Add(separationId);
            db.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"employee-{i}@test.local", PasswordHash = "test-hash", FirstName = "Exit", LastName = $"Employee {i}", IsActive = true });
            db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"H-{i:0000}", FirstName = "Exit", LastName = $"Employee {i}", Email = $"employee-{i}@test.local", DateOfJoining = new(2020, 1, 1), Status = EmployeeStatus.Active });
            var linkId = Guid.NewGuid();
            db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = tenantId, UserId = userId, EmployeeId = employeeId });
            db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent { Id = linkId, TenantId = tenantId, SubjectUserId = userId, ActorUserId = hrUserId, Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = employeeId, OccurredAtUtc = DateTime.UtcNow, Reason = "Phase 8H large data", CorrelationId = linkId.ToString("N") });
            db.EmployeeEmployments.Add(new EmployeeEmployment { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, FirstHiredDate = new(2020, 1, 1), DateOfJoining = new(2020, 1, 1), NoticePeriod = 30, NoticePeriodUnit = "Days" });
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active });
            db.EmployeeSeparations.Add(new EmployeeSeparation { Id = separationId, TenantId = tenantId, EmployeeId = employeeId, ActiveEmployeeKey = employeeId, SeparationNumber = $"H-SEP-{i:0000}", SeparationType = SeparationType.EmployeeInitiated, ReasonId = reasonId, InitiatedBy = EmployeeSeparationInitiator.Employee, InitiatedByUserId = userId, RequestDate = lwd.AddDays(-30), ProposedLastWorkingDate = lwd, ApprovedLastWorkingDate = lwd, NoticeStartDate = lwd.AddDays(-30), NoticeEndDate = lwd, ExpectedNoticeEndDate = lwd, NoticePeriodDays = 30, NoticeServedDays = 30, NoticeShortfallDays = 0, Status = EmployeeSeparationStatus.ReadyForExit, CreatedByUserId = hrUserId });
            db.SeparationClearances.Add(new SeparationClearance { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeSeparationId = separationId, EmployeeId = employeeId, TemplateId = Guid.NewGuid(), Status = SeparationClearanceStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
            var settlementId = Guid.NewGuid();
            db.FinalSettlementCases.Add(new FinalSettlementCase { Id = settlementId, TenantId = tenantId, EmployeeId = employeeId, SeparationDate = lwd, LastWorkingDate = lwd, SettlementDate = lwd, Status = FinalSettlementStatus.Finalized, FinalizedAtUtc = DateTime.UtcNow });
            db.SeparationSettlementOrchestrations.Add(new SeparationSettlementOrchestration { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeSeparationId = separationId, EmployeeId = employeeId, PayrollFinalSettlementId = settlementId, Status = SeparationSettlementOrchestrationStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
            db.SeparationExitInterviews.Add(new SeparationExitInterview { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeSeparationId = separationId, EmployeeId = employeeId, TemplateVersionId = Guid.NewGuid(), Status = SeparationExitInterviewStatus.CompletedWithoutEmployeeResponse, CompletedWithoutEmployeeResponse = true, HrCompletedAtUtc = DateTime.UtcNow, FinalizedAtUtc = DateTime.UtcNow, AssignedAtUtc = DateTime.UtcNow });
        }

        await db.SaveChangesAsync();
        return separationIds;
    }
}
