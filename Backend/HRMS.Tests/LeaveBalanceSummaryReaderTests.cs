using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class LeaveBalanceSummaryReaderTests
{
    [Fact]
    public async Task Returns_only_the_current_employees_balances_with_authoritative_projection_values()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var otherEmployee = Guid.NewGuid();
        var leaveType = Guid.NewGuid();
        var period = Guid.NewGuid();
        using (var seed = database.CreateContext(new TestTenantContext()))
        {
            seed.Tenants.Add(new Tenant { Id = tenant, TenantCode = "SUMM", Host = "summ.local", ShardKey = "summ" });
            seed.Employees.AddRange(
                new Employee { Id = employee, TenantId = tenant, FirstName = "Priya", LastName = "Raman", Email = "priya@summ.local" },
                new Employee { Id = otherEmployee, TenantId = tenant, FirstName = "Other", LastName = "Employee", Email = "other@summ.local" });
            seed.LeaveTypes.Add(new LeaveType { Id = leaveType, TenantId = tenant, Code = "AL", Name = "Annual Leave" });
            seed.LeavePeriods.Add(new LeavePeriod { Id = period, TenantId = tenant, Code = "FY26", Name = "FY 2026", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) });
            await seed.SaveChangesAsync();
        }

        using var context = database.CreateContext(new TestTenantContext(tenant));
        var poster = new LeaveBalanceTransactionPoster(context, new TestTenantContext(tenant), TimeProvider.System);
        await poster.PostCreditAsync(new(tenant, employee, leaveType, period, HRMS.Domain.Enums.LeaveBalanceTransactionType.Opening, 20m, new(2026, 1, 1), null, null, HRMS.Domain.Enums.LeaveBalanceSourceType.Policy, null, HRMS.Domain.Enums.LeaveBalanceActorType.System, null, null, "summary-opening", null));
        var result = await new LeaveBalanceSummaryReader(context, new StubIdentity(Result<RuntimeEmployeeIdentity>.Success(new(tenant, Guid.NewGuid(), employee)))).GetMineAsync();

        Assert.True(result.Succeeded);
        var item = Assert.Single(result.Value!);
        Assert.Equal("Annual Leave", item.LeaveTypeName);
        Assert.Equal(20m, item.AvailableQuantity);
        Assert.Equal(0m, item.ReservedQuantity);
    }

    [Fact]
    public async Task Propagates_missing_employee_identity_without_reading_balance_data()
    {
        using var database = new SqliteInMemoryDatabase();
        using var context = database.CreateContext(new TestTenantContext());
        var result = await new LeaveBalanceSummaryReader(context, new StubIdentity(Result<RuntimeEmployeeIdentity>.NotFound("not linked"))).GetMineAsync();
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    private sealed class StubIdentity(Result<RuntimeEmployeeIdentity> result) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
