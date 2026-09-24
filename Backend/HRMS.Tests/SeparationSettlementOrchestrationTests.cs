using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationSettlementOrchestrationTests
{
    [Fact]
    public async Task Readiness_returns_structured_clearance_and_interview_blockers()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var result = await Service(db, fixture).GetReadinessAsync(fixture.SeparationId);
        Assert.False(result.Value!.IsReady);
        Assert.Contains(result.Value.Blockers, x => x.Code == "ClearanceIncomplete");
        Assert.Contains(result.Value.Blockers, x => x.Code == "ExitInterviewIncomplete");
    }

    [Fact]
    public async Task Initiation_is_idempotent_and_links_one_payroll_settlement()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await SeedReadyState(database, fixture);
        await using var first = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var service = Service(first, fixture);
        var initiated = await service.InitiateAsync(fixture.SeparationId, new() { IdempotencyKey = "phase8f-test" });
        Assert.True(initiated.Succeeded, initiated.Message);
        await using var second = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var replay = await Service(second, fixture).InitiateAsync(fixture.SeparationId, new() { IdempotencyKey = "phase8f-test" });
        Assert.True(replay.Succeeded, replay.Message);
        Assert.Equal(1, await second.SeparationSettlementOrchestrations.CountAsync());
        Assert.Equal(1, await second.FinalSettlementCases.CountAsync(x => x.EmployeeId == fixture.EmployeeId));
        Assert.NotNull(replay.Value!.PayrollFinalSettlementId);
    }

    [Fact]
    public async Task Finalized_payroll_status_projects_to_ready_for_final_exit_closure()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await NoticeTestData.CreateApprovedAsync(database, new(2026, 10, 2));
        await SeedReadyState(database, fixture);
        await using (var seed = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)))
        {
            var settlement = new FinalSettlementCase { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, SeparationDate = new(2026, 10, 2), LastWorkingDate = new(2026, 10, 2), SettlementDate = new(2026, 10, 2), Status = FinalSettlementStatus.Finalized, FinalizedAtUtc = DateTime.UtcNow };
            seed.FinalSettlementCases.Add(settlement);
            seed.SeparationSettlementOrchestrations.Add(new SeparationSettlementOrchestration { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, PayrollFinalSettlementId = settlement.Id, Status = SeparationSettlementOrchestrationStatus.Initiated });
            await seed.SaveChangesAsync();
        }
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        var result = await Service(db, fixture).GetStatusAsync(fixture.SeparationId);
        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.Value!.ReadyForFinalExitClosure);
        Assert.Equal(SeparationSettlementOrchestrationStatus.Completed, result.Value.OrchestrationStatus);
    }

    private static SeparationSettlementOrchestrationService Service(HrmsDbContext db, NoticeFixture fixture) => new(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId), new PayrollRetroSettlementService(db, new TestTenantContext(fixture.TenantId, fixture.HrUserId), TimeProvider.System), TimeProvider.System);

    private static async Task SeedReadyState(SqliteInMemoryDatabase database, NoticeFixture fixture)
    {
        await using var db = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId));
        db.SeparationClearances.Add(new SeparationClearance { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, TemplateId = Guid.NewGuid(), Status = SeparationClearanceStatus.Completed, CompletedAtUtc = DateTime.UtcNow });
        db.SeparationExitInterviews.Add(new SeparationExitInterview { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeSeparationId = fixture.SeparationId, EmployeeId = fixture.EmployeeId, TemplateVersionId = Guid.NewGuid(), Status = SeparationExitInterviewStatus.Completed, AssignedAtUtc = DateTime.UtcNow, HrCompletedAtUtc = DateTime.UtcNow, FinalizedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
}
