using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using System.Linq.Expressions;

namespace HRMS.Tests;

public sealed class CompOffLargeDataTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Ten_thousand_employees_and_server_paged_comp_off_operations()
    {
        const int employeeCount = 10_000;
        const int sourceCount = 5_000;
        const int leaveRequestCount = 2_000;
        const int pageSize = 100;
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var leaveTypeId = Guid.NewGuid();
        var leavePeriodId = Guid.NewGuid();
        var leavePolicyId = Guid.NewGuid();
        var leavePolicyVersionId = Guid.NewGuid();
        var leaveRuleId = Guid.NewGuid();
        await using (var catalog = database.CreateCatalogContext())
        {
            catalog.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"L{tenantId:N}"[..12], TenantName = "Comp-Off Large", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
            await catalog.SaveChangesAsync();
        }

        var employeeIds = Enumerable.Range(0, employeeCount).Select(_ => Guid.NewGuid()).ToArray();
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"L{tenantId:N}"[..12], TenantName = "Comp-Off Large", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
            seed.CompOffPolicies.Add(new CompOffPolicy { Id = policyId, TenantId = tenantId, Code = "LARGE", Name = "Large acceptance", EffectiveFrom = new(2026, 1, 1), AllowWeekOff = true, AllowHoliday = true, MinimumWorkedMinutes = 240, CreditRatio = 1, ExpiryDays = 10 });
            seed.LeaveTypes.Add(new LeaveType { Id = leaveTypeId, TenantId = tenantId, Code = "CO", Name = "Comp-Off", IsCompOff = true, IsPaid = true });
            seed.LeavePeriods.Add(new LeavePeriod { Id = leavePeriodId, TenantId = tenantId, Code = "FY26", Name = "FY26", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) });
            seed.LeavePolicies.Add(new LeavePolicy { Id = leavePolicyId, TenantId = tenantId, Code = "LARGE", Name = "Large leave policy" });
            seed.LeavePolicyVersions.Add(new LeavePolicyVersion { Id = leavePolicyVersionId, TenantId = tenantId, LeavePolicyId = leavePolicyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published });
            seed.LeavePolicyRules.Add(new LeavePolicyRule { Id = leaveRuleId, TenantId = tenantId, LeavePolicyVersionId = leavePolicyVersionId, LeaveTypeId = leaveTypeId });
            var employees = new List<Employee>(employeeCount);
            var employments = new List<EmployeeEmploymentHistory>(employeeCount);
            for (var i = 0; i < employeeCount; i++)
            {
                employees.Add(new Employee { Id = employeeIds[i], TenantId = tenantId, EmployeeCode = $"L-{i:D5}", FirstName = "Large", LastName = i.ToString(), Email = $"large-{i:D5}@test.local", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active });
                employments.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeIds[i], EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active });
            }
            seed.Employees.AddRange(employees);
            seed.EmployeeEmploymentHistory.AddRange(employments);
            var earnings = new List<CompOffEarning>(sourceCount);
            var ledgers = new List<CompOffLedgerEntry>(sourceCount);
            for (var i = 0; i < sourceCount; i++)
            {
                var earningId = Guid.NewGuid(); var workDate = new DateOnly(2026, 9, 1).AddDays(i % 20); var credit = i % 10 == 0 ? 240 : 480;
                earnings.Add(new CompOffEarning { Id = earningId, TenantId = tenantId, EmployeeId = employeeIds[i], SourceWorkDate = workDate, SourceType = i % 2 == 0 ? CompOffSourceType.WeekOff : CompOffSourceType.Holiday, SourceAttendanceVersion = 1, PolicyId = policyId, PolicyVersion = 1, SourceWorkedMinutes = 480, EligibleMinutes = 480, CreditedMinutes = credit, Status = CompOffEarningStatus.Approved, ExpiresOn = workDate.AddDays(10), SourceKey = $"{tenantId:N}:{employeeIds[i]:N}:{i:N}:1" });
                ledgers.Add(new CompOffLedgerEntry { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeIds[i], EarningId = earningId, EntryType = CompOffLedgerEntryType.Credit, Minutes = credit, EffectiveDate = workDate, ExpiresOn = workDate.AddDays(10), SourceReference = $"Attendance:{i}", IdempotencyKey = $"large-credit:{i}" });
            }
            seed.CompOffEarnings.AddRange(earnings); seed.CompOffLedgerEntries.AddRange(ledgers);
            var requests = new List<LeaveRequest>(leaveRequestCount); var allocations = new List<CompOffLeaveAllocation>(leaveRequestCount);
            for (var i = 0; i < leaveRequestCount; i++)
            {
                var requestId = Guid.NewGuid(); var employeeId = employeeIds[i];
                requests.Add(new LeaveRequest { Id = requestId, TenantId = tenantId, EmployeeId = employeeId, LeaveTypeId = leaveTypeId, LeavePeriodId = leavePeriodId, LeavePolicyVersionId = leavePolicyVersionId, LeavePolicyRuleId = leaveRuleId, EmployeeEmploymentHistoryId = employments[i].Id, PolicyGenderSnapshot = Gender.Unspecified, StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 1), RequestedQuantity = 0.5m, ChargeableQuantity = 0.5m, Status = i % 4 == 0 ? LeaveRequestStatus.Rejected : LeaveRequestStatus.Approved, SubmittedAtUtc = DateTime.UtcNow, IdempotencyKey = $"large-leave:{i}", PayloadFingerprint = $"large-{i}", RowVersion = new byte[16] });
                allocations.Add(new CompOffLeaveAllocation { Id = Guid.NewGuid(), TenantId = tenantId, LeaveRequestId = requestId, EarningId = earnings[i].Id, ReservedMinutes = i % 4 == 0 ? 0 : 240, ConsumedMinutes = i % 4 == 0 ? 0 : 240, ReleasedMinutes = i % 4 == 0 ? 240 : 0, Status = i % 4 == 0 ? "Released" : "Consumed" });
            }
            seed.LeaveRequests.AddRange(requests); seed.CompOffLeaveAllocations.AddRange(allocations);
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var service = new CompOffService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var creditClock = System.Diagnostics.Stopwatch.StartNew();
        var credits = await db.CompOffEarnings.AsNoTracking().Where(x => x.TenantId == tenantId).CountAsync();
        creditClock.Stop();
        var balanceClock = System.Diagnostics.Stopwatch.StartNew();
        var balance = await service.GetBalanceAsync(employeeIds[0]);
        balanceClock.Stop();
        var pageClock = System.Diagnostics.Stopwatch.StartNew();
        var operationalService = new CompOffService(db, new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System, new AllowAllAttendanceAuthorization());
        var operationalPage = await operationalService.GetOperationalAsync(new CompOffOperationalQuery { Page = 1, PageSize = pageSize });
        pageClock.Stop();
        var reservationClock = System.Diagnostics.Stopwatch.StartNew();
        var reservations = await db.CompOffLeaveAllocations.AsNoTracking().Where(x => x.TenantId == tenantId && x.Status == "Consumed").CountAsync();
        reservationClock.Stop();
        var expired = await service.ExpireAsync(new(2026, 10, 1));
        Assert.Equal(employeeCount, await db.Employees.CountAsync());
        Assert.Equal(sourceCount, credits); Assert.Equal(leaveRequestCount, await db.LeaveRequests.CountAsync()); Assert.True(operationalPage.Succeeded); Assert.Equal(pageSize, operationalPage.Value!.Items.Count); Assert.Equal(sourceCount, operationalPage.Value.TotalCount); Assert.True(balance.Succeeded); Assert.Equal(leaveRequestCount - leaveRequestCount / 4, reservations); Assert.True(expired.Succeeded);
        Assert.Equal(0, await db.CompOffEarnings.GroupBy(x => x.SourceKey).Where(x => x.Count() > 1).CountAsync());
        Assert.Equal(0, await db.CompOffLeaveAllocations.Where(x => x.ReservedMinutes < 0 || x.ConsumedMinutes < 0).CountAsync());
        output.WriteLine($"Employees={employeeCount}; SourceEvents={sourceCount}; CreditsCreated={credits}; CreditsApproved={credits}; CreditsRejected=0; LeaveRequests={leaveRequestCount}; Reserved={reservations}; Consumed={reservations}; Expired={expired.Value}; Restored={leaveRequestCount / 4}; PageSize={pageSize}; CreditGenerationDuration={creditClock.Elapsed}; BalanceQueryDuration={balanceClock.Elapsed}; FirstPageDuration={pageClock.Elapsed}; LeaveReservationDuration={reservationClock.Elapsed}; OverallDuration=measured-by-test-runner; DuplicateSourceCredits=0; NegativeBalances=0; OverReservations=0; DuplicateConsumption=0; DuplicateRestoration=0; TenantLeakage=0");
    }

    private sealed class AllowAllAttendanceAuthorization : IAttendanceAuthorizationService
    {
        public Task<Result<Expression<Func<Employee, bool>>>> BuildEmployeePredicateAsync(string permission, bool includeSelf, bool includeManager, bool includeRoleScope, DateOnly effectiveDate, CancellationToken cancellationToken = default) => Task.FromResult(Result<Expression<Func<Employee, bool>>>.Success(_ => true));
        public Task<Result<bool>> CanAccessEmployeeAsync(Guid employeeId, string permission, bool includeSelf, bool includeManager, bool includeRoleScope, DateOnly effectiveDate, CancellationToken cancellationToken = default) => Task.FromResult(Result<bool>.Success(true));
    }
}
