using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlHrLeaveDashboardIntegrationTests
{
    [Fact]
    public async Task MySql_hr_dashboard_aggregates_operational_data_and_keeps_tenant_scope()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL HR dashboard test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            var unlimitedTypeId = Guid.NewGuid();
            var unlimitedRuleId = Guid.NewGuid();
            var departmentId = Guid.NewGuid();
            var workLocationId = Guid.NewGuid();
            await using (var setup = fixture.CreateContext())
            {
                setup.Departments.Add(new Department { Id = departmentId, TenantId = fixture.TenantId, Code = "OPS", Name = "Operations", IsActive = true });
                setup.WorkLocations.Add(new WorkLocation { Id = workLocationId, TenantId = fixture.TenantId, Code = "HQ", Name = "Head office", IsActive = true });
                var history = await setup.EmployeeEmploymentHistory.SingleAsync(x => x.Id == fixture.EmployeeHistoryId);
                history.DepartmentId = departmentId;
                history.WorkLocationId = workLocationId;
                setup.LeaveTypes.Add(new LeaveType { Id = unlimitedTypeId, TenantId = fixture.TenantId, Code = "UL", Name = "Unlimited PTO", IsActive = true });
                setup.LeavePolicyRules.Add(new LeavePolicyRule { Id = unlimitedRuleId, TenantId = fixture.TenantId, LeavePolicyVersionId = fixture.PolicyVersionId, LeaveTypeId = unlimitedTypeId, IsActive = true });
                setup.LeavePolicyEntitlementRules.Add(new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, LeavePolicyRuleId = unlimitedRuleId, EntitlementMode = EntitlementMode.Unlimited });
                setup.LeaveRequests.AddRange(
                    new LeaveRequest { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, LeaveTypeId = fixture.LeaveTypeId, LeavePeriodId = fixture.LeavePeriodId, LeavePolicyVersionId = fixture.PolicyVersionId, LeavePolicyRuleId = fixture.PolicyRuleId, EmployeeEmploymentHistoryId = fixture.EmployeeHistoryId, StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 2), RequestedQuantity = 2, ChargeableQuantity = 2, Status = LeaveRequestStatus.Approved, SubmittedAtUtc = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc), IdempotencyKey = "dashboard-approved", PayloadFingerprint = "dashboard-approved" },
                    new LeaveRequest { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, LeaveTypeId = fixture.LeaveTypeId, LeavePeriodId = fixture.LeavePeriodId, LeavePolicyVersionId = fixture.PolicyVersionId, LeavePolicyRuleId = fixture.PolicyRuleId, EmployeeEmploymentHistoryId = fixture.EmployeeHistoryId, StartDate = new(2026, 10, 5), EndDate = new(2026, 10, 5), RequestedQuantity = 1, ChargeableQuantity = 1, Status = LeaveRequestStatus.PendingApproval, SubmittedAtUtc = new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc), IdempotencyKey = "dashboard-pending", PayloadFingerprint = "dashboard-pending" });
                await setup.SaveChangesAsync();
            }

            await using (var db = fixture.CreateContext())
            {
                var result = await new HrLeaveDashboardService(db, fixture.EmployeeTenant, new HRMS.Application.Services.LeavePeriodResolver(db, fixture.EmployeeTenant), new FixedTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero))).GetSummaryAsync(new());
                Assert.True(result.Succeeded, result.Message);
                Assert.Equal(1, result.Value!.Kpis.EmployeesOnLeaveToday);
                Assert.Equal(1, result.Value.Kpis.PendingApprovalRequests);
                Assert.Equal(1, result.Value.Kpis.UpcomingApprovedRequests);
                Assert.Equal(2m, Assert.Single(result.Value.LeaveTypeUsage).Quantity);
                var balance = Assert.Single(result.Value.Balances, x => x.LeaveTypeId == fixture.LeaveTypeId);
                Assert.Equal(10m, balance.Granted);
                Assert.Equal(10m, balance.Available);
                var unlimited = Assert.Single(result.Value.Balances, x => x.LeaveTypeId == unlimitedTypeId);
                Assert.Equal(EntitlementMode.Unlimited, unlimited.EntitlementMode);
                Assert.Null(unlimited.Granted);
                Assert.Equal("Operations", Assert.Single(result.Value.Departments).Name);
                Assert.Equal("Head office", Assert.Single(result.Value.WorkLocations).Name);
                Assert.Equal(1, Assert.Single(result.Value.ApprovalAging, x => x.Bucket == "4-7 days").RequestCount);
            }

            await using (var other = fixture.CreateContext(new HRMS.Tests.TestSupport.TestTenantContext(fixture.OtherTenantId)))
            {
                var otherTenant = new HRMS.Tests.TestSupport.TestTenantContext(fixture.OtherTenantId);
                var result = await new HrLeaveDashboardService(other, otherTenant, new HRMS.Application.Services.LeavePeriodResolver(other, otherTenant), new FixedTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero))).GetSummaryAsync(new());
                Assert.True(result.Succeeded, result.Message);
                Assert.Equal(0, result.Value!.Kpis.PendingApprovalRequests);
                Assert.Empty(result.Value.LeaveTypeUsage);
            }
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new HRMS.Tests.TestSupport.TestTenantContext());
            await cleanup.EmployeeEmploymentHistory.Where(x => x.TenantId == fixture.TenantId).ExecuteUpdateAsync(x => x.SetProperty(v => v.DepartmentId, (Guid?)null).SetProperty(v => v.WorkLocationId, (Guid?)null));
            await cleanup.Departments.IgnoreQueryFilters().Where(x => x.TenantId == fixture.TenantId).ExecuteDeleteAsync();
            await cleanup.WorkLocations.IgnoreQueryFilters().Where(x => x.TenantId == fixture.TenantId).ExecuteDeleteAsync();
            await fixture.CleanupAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
