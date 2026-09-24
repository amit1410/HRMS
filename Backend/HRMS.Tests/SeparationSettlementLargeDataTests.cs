using System.Diagnostics;
using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using SeparationReasonEntity = HRMS.Domain.Entities.Separation.SeparationReason;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationSettlementLargeDataTests
{
    [Fact]
    public async Task Dashboard_pages_1000_settlement_projections_server_side()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"S{tenantId:N}"[..12], Host = $"{tenantId:N}.settlement.test", ShardKey = tenantId.ToString("N") });
            var reason = new SeparationReasonEntity { Id = Guid.NewGuid(), TenantId = tenantId, Code = "LARGE", Name = "Large data", Category = SeparationReasonCategory.Resignation, EffectiveFrom = new(2020, 1, 1) };
            seed.SeparationReasons.Add(reason);
            for (var i = 0; i < 1000; i++)
            {
                var employeeId = Guid.NewGuid(); var separationId = Guid.NewGuid();
                seed.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"S-{i:0000}", FirstName = "Settlement", LastName = $"Employee {i}", Email = $"settlement{i}@test.local", DateOfJoining = new(2020, 1, 1), Status = EmployeeStatus.Active });
                seed.EmployeeSeparations.Add(new EmployeeSeparation { Id = separationId, TenantId = tenantId, EmployeeId = employeeId, SeparationNumber = $"SET-{i:0000}", SeparationType = SeparationType.EmployeeInitiated, ReasonId = reason.Id, InitiatedBy = EmployeeSeparationInitiator.Employee, RequestDate = new(2026, 9, 1), ProposedLastWorkingDate = new(2026, 10, 2), ApprovedLastWorkingDate = new(2026, 10, 2), NoticeStartDate = new(2026, 9, 3), NoticePeriodDays = 30, NoticeServedDays = 30, NoticeShortfallDays = 0, Status = EmployeeSeparationStatus.ReadyForExit });
                seed.SeparationSettlementOrchestrations.Add(new SeparationSettlementOrchestration { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeSeparationId = separationId, EmployeeId = employeeId, Status = SeparationSettlementOrchestrationStatus.NotReady, ApprovedLastWorkingDateSnapshot = new(2026, 10, 2) });
            }
            await seed.SaveChangesAsync();
        }
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var service = new SeparationSettlementOrchestrationService(db, new TestTenantContext(tenantId), new PayrollRetroSettlementService(db, new TestTenantContext(tenantId), TimeProvider.System), TimeProvider.System);
        var watch = Stopwatch.StartNew();
        var result = await service.GetDashboardAsync(new SettlementDashboardQuery { Page = 1, PageSize = 50 });
        watch.Stop();
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1000, result.Value!.TotalCount);
        Assert.Equal(50, result.Value.Items.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"Server-side dashboard projection exceeded bound: {watch.Elapsed}");
    }
}
