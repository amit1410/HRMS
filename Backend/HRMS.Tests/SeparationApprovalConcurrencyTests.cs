using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using SeparationReasonEntity = HRMS.Domain.Entities.Separation.SeparationReason;

namespace HRMS.Tests;

public sealed class SeparationApprovalConcurrencyTests
{
    [Fact] public Task Manager_approve_vs_employee_withdraw() => RunAsync(Race.ManagerWithdraw);
    [Fact] public Task Manager_approve_vs_manager_reject() => RunAsync(Race.ManagerReject);
    [Fact] public Task Hr_approve_vs_hr_reject() => RunAsync(Race.HrReject);
    [Fact] public Task Hr_approve_vs_lwd_revision() => RunAsync(Race.LwdRevision);
    [Fact] public Task Hr_approve_vs_employee_withdraw() => RunAsync(Race.HrWithdraw);
    [Fact] public Task Hr_approve_vs_hr_approve() => RunAsync(Race.HrApprove);

    private enum Race { ManagerWithdraw, ManagerReject, HrReject, LwdRevision, HrWithdraw, HrApprove }

    private static async Task RunAsync(Race race)
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var managerId = Guid.NewGuid(); var employeeUser = Guid.NewGuid(); var managerUser = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"T{tenantId:N}"[..20], Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
            seed.Users.AddRange(new User { Id = employeeUser, TenantId = tenantId, Email = $"{employeeUser:N}@test.local", PasswordHash = "test", FirstName = "Employee", LastName = "One", IsActive = true }, new User { Id = managerUser, TenantId = tenantId, Email = $"{managerUser:N}@test.local", PasswordHash = "test", FirstName = "Manager", LastName = "One", IsActive = true });
            seed.Employees.AddRange(new Employee { Id = managerId, TenantId = tenantId, EmployeeCode = "MGR-1", FirstName = "Manager", LastName = "One", Email = $"{managerId:N}@test.local", DateOfJoining = new(2020, 1, 1) }, new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "EMP-1", FirstName = "Employee", LastName = "One", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2020, 1, 1), ReportingManagerId = managerId });
            var employeeLink = Guid.NewGuid(); var managerLink = Guid.NewGuid();
            seed.AccountEmployeeCurrentLinks.AddRange(new AccountEmployeeCurrentLink { LinkId = employeeLink, TenantId = tenantId, UserId = employeeUser, EmployeeId = employeeId }, new AccountEmployeeCurrentLink { LinkId = managerLink, TenantId = tenantId, UserId = managerUser, EmployeeId = managerId });
            seed.AccountEmployeeLinkEvents.AddRange(new AccountEmployeeLinkEvent { Id = employeeLink, TenantId = tenantId, SubjectUserId = employeeUser, ActorUserId = employeeUser, Sequence = 1, Operation = "Link", NewLinkId = employeeLink, AfterEmployeeId = employeeId, OccurredAtUtc = DateTime.UtcNow, Reason = "test", CorrelationId = employeeLink.ToString("N") }, new AccountEmployeeLinkEvent { Id = managerLink, TenantId = tenantId, SubjectUserId = managerUser, ActorUserId = managerUser, Sequence = 1, Operation = "Link", NewLinkId = managerLink, AfterEmployeeId = managerId, OccurredAtUtc = DateTime.UtcNow, Reason = "test", CorrelationId = managerLink.ToString("N") });
            seed.EmployeeEmployments.Add(new EmployeeEmployment { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, FirstHiredDate = new(2020, 1, 1), DateOfJoining = new(2020, 1, 1), NoticePeriod = 30, NoticePeriodUnit = "Days" });
            seed.EmployeeEmploymentHistory.AddRange(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, EffectiveFrom = new(2020, 1, 1), ManagerId = managerId, EmploymentStatus = EmployeeStatus.Active }, new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = managerId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active });
            var reason = new SeparationReasonEntity { Id = Guid.NewGuid(), TenantId = tenantId, Code = "CONC", Name = "Concurrency", Category = SeparationReasonCategory.Resignation, EmployeeInitiatedAllowed = true, EffectiveFrom = new(2020, 1, 1), IsActive = true }; seed.SeparationReasons.Add(reason);
            seed.EmployeeSeparations.Add(new EmployeeSeparation { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, ActiveEmployeeKey = employeeId, SeparationNumber = "SEP-CONC", SeparationType = SeparationType.EmployeeInitiated, ReasonId = reason.Id, InitiatedBy = EmployeeSeparationInitiator.Employee, InitiatedByUserId = employeeUser, RequestDate = new(2026, 9, 23), ProposedLastWorkingDate = new(2026, 10, 23), Status = race is Race.ManagerWithdraw or Race.ManagerReject ? EmployeeSeparationStatus.ManagerReview : EmployeeSeparationStatus.HrReview });
            await seed.SaveChangesAsync();
        }
        var id = await database.CreateContext(new TestTenantContext(tenantId)).EmployeeSeparations.Select(x => x.Id).SingleAsync();
        var leftTenant = new TestTenantContext(tenantId, race is Race.ManagerWithdraw or Race.ManagerReject ? managerUser : Guid.NewGuid());
        var rightTenant = new TestTenantContext(tenantId, race switch { Race.ManagerWithdraw or Race.HrWithdraw => employeeUser, Race.ManagerReject => managerUser, _ => Guid.NewGuid() });
        await using var leftDb = database.CreateContext(leftTenant);
        await using var rightDb = database.CreateContext(rightTenant);
        var left = Create(leftDb, leftTenant);
        var right = Create(rightDb, rightTenant);
        Task<Result<EmployeeSeparationDto>> leftTask = race switch { Race.ManagerWithdraw or Race.ManagerReject => left.ManagerApproveAsync(id), _ => left.HrApproveAsync(id) };
        Task<Result<EmployeeSeparationDto>> rightTask = race switch
        {
            Race.ManagerWithdraw or Race.HrWithdraw => right.WithdrawAsync(id),
            Race.ManagerReject => right.ManagerRejectAsync(id, "Rejected concurrently"),
            Race.HrReject => right.HrRejectAsync(id, "Rejected concurrently"),
            Race.LwdRevision => right.ReviseLwdAsync(id, new(new(2026, 11, 1), "Revised concurrently")),
            _ => right.HrApproveAsync(id)
        };
        var results = await Task.WhenAll(leftTask, rightTask);
        Assert.Equal(1, results.Count(x => x.Succeeded));
        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        var row = await verify.EmployeeSeparations.SingleAsync(x => x.Id == id);
        Assert.Contains(row.Status, new[] { EmployeeSeparationStatus.HrReview, EmployeeSeparationStatus.Approved, EmployeeSeparationStatus.Rejected, EmployeeSeparationStatus.Withdrawn });
        Assert.InRange(await verify.EmployeeSeparationEvents.CountAsync(x => x.EmployeeSeparationId == id), 0, 3);
    }

    private static SeparationService Create(HRMS.Infrastructure.Persistence.HrmsDbContext db, TestTenantContext tenant) => new(db, tenant, new EmployeeIdentityResolver(db, tenant), new EmployeeManagerResolver(db, tenant), TimeProvider.System);
}
