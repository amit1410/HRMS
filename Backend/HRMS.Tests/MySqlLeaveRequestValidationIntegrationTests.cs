using HRMS.Application.Services;
using HRMS.Application.Common;
using HRMS.Application.Abstractions;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlLeaveRequestValidationIntegrationTests
{
    [Fact]
    public async Task MySql_active_overlap_is_rejected_without_reservation_mutation_and_terminal_overlap_is_allowed()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL request validation test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using var context = fixture.CreateContext(fixture.EmployeeTenant);
            var submission = CreateSubmission(context, fixture);

            var first = await submission.SubmitAsync(new(fixture.LeaveTypeId, new(2026, 10, 10), new(2026, 10, 12), "mysql-overlap-first"));
            Assert.True(first.Succeeded, first.Message);

            var overlapping = await submission.SubmitAsync(new(fixture.LeaveTypeId, new(2026, 10, 12), new(2026, 10, 14), "mysql-overlap-active"));
            Assert.False(overlapping.Succeeded);
            Assert.Contains(LeaveRequestSubmissionErrorCodes.Overlap, overlapping.Message);
            Assert.Equal(1, await context.LeaveRequests.CountAsync(x => x.TenantId == fixture.TenantId));
            Assert.Equal(1, await context.LeaveBalanceTransactions.CountAsync(x => x.TenantId == fixture.TenantId && x.TransactionType == LeaveBalanceTransactionType.Reservation));
            Assert.Equal(3m, await context.EmployeeLeaveBalances.Where(x => x.Id == fixture.BalanceId).Select(x => x.ReservedQuantity).SingleAsync());

            var approval = new LeaveRequestApprovalService(
                context,
                new EmployeeIdentityResolver(context, fixture.ManagerTenant),
                new EmployeeManagerResolver(context, fixture.ManagerTenant),
                new MySqlLeaveRequestSubmissionLock(context),
                TimeProvider.System,
                balanceAccountingService: new LeaveBalanceAccountingService(context, fixture.ManagerTenant, TimeProvider.System));
            var rejected = await approval.RejectAsync(first.Value!.RequestId);
            Assert.True(rejected.Succeeded, rejected.Message);
            Assert.Equal(0m, await context.EmployeeLeaveBalances.Where(x => x.Id == fixture.BalanceId).Select(x => x.ReservedQuantity).SingleAsync());

            var terminalOverlap = await submission.SubmitAsync(new(fixture.LeaveTypeId, new(2026, 10, 12), new(2026, 10, 14), "mysql-overlap-terminal-allowed"));
            Assert.True(terminalOverlap.Succeeded, terminalOverlap.Message);
            Assert.Equal(2, await context.LeaveRequests.CountAsync(x => x.TenantId == fixture.TenantId));
            Assert.Equal(3m, await context.EmployeeLeaveBalances.Where(x => x.Id == fixture.BalanceId).Select(x => x.ReservedQuantity).SingleAsync());
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_request_during_an_effective_employment_gap_is_rejected_without_persistence()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL employment-gap validation test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var history = await setup.EmployeeEmploymentHistory.SingleAsync(x => x.Id == fixture.EmployeeHistoryId);
                history.EffectiveTo = new(2026, 10, 9);
                setup.EmployeeEmploymentHistory.Add(new HRMS.Domain.Entities.EmployeeEmploymentHistory
                {
                    Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId,
                    EffectiveFrom = new(2026, 10, 15), EmploymentStatus = EmployeeStatus.Active
                });
                await setup.SaveChangesAsync();
            }

            await using var context = fixture.CreateContext(fixture.EmployeeTenant);
            var validation = new LeaveRequestValidationService(
                context,
                new EmployeeIdentityResolver(context, fixture.EmployeeTenant),
                new EffectiveEmploymentResolver(context, fixture.EmployeeTenant),
                new LeavePeriodResolver(context, fixture.EmployeeTenant),
                new LeavePolicyResolver(context, new EffectiveEmploymentResolver(context, fixture.EmployeeTenant), fixture.EmployeeTenant));
            var result = await validation.ValidateAsync(new(fixture.LeaveTypeId, new(2026, 10, 10), new(2026, 10, 12), "mysql-employment-gap"));
            Assert.False(result.Succeeded);
            Assert.Equal(ResultStatus.NotFound, result.Status);
            Assert.Equal(0, await context.LeaveRequests.CountAsync(x => x.TenantId == fixture.TenantId));
            Assert.Equal(0, await context.LeaveBalanceTransactions.CountAsync(x => x.TenantId == fixture.TenantId && x.TransactionType == LeaveBalanceTransactionType.Reservation));
        }
        finally { await fixture.CleanupAsync(); }
    }

    private static LeaveRequestSubmissionService CreateSubmission(HRMS.Infrastructure.Persistence.HrmsDbContext db, MySqlLeaveLifecycleIntegrationTests.Fixture fixture)
    {
        var employment = new EffectiveEmploymentResolver(db, fixture.EmployeeTenant);
        var validation = new LeaveRequestValidationService(db, new EmployeeIdentityResolver(db, fixture.EmployeeTenant), employment, new LeavePeriodResolver(db, fixture.EmployeeTenant), new LeavePolicyResolver(db, employment, fixture.EmployeeTenant));
        return new LeaveRequestSubmissionService(db, new EmployeeIdentityResolver(db, fixture.EmployeeTenant), validation, new MySqlLeaveRequestSubmissionLock(db), TimeProvider.System, balanceAccountingService: new LeaveBalanceAccountingService(db, fixture.EmployeeTenant, TimeProvider.System));
    }
}
