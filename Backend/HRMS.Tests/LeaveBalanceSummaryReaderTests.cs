using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
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
        var result = await new LeaveBalanceSummaryReader(context, new StubIdentity(Result<RuntimeEmployeeIdentity>.Success(new(tenant, Guid.NewGuid(), employee))), new StubPolicy(leaveType, null), TimeProvider.System).GetMineAsync();

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
        var result = await new LeaveBalanceSummaryReader(context, new StubIdentity(Result<RuntimeEmployeeIdentity>.NotFound("not linked")), new StubPolicy(Guid.Empty, null), TimeProvider.System).GetMineAsync();
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Returns_current_unlimited_entitlement_without_a_finite_balance()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var leaveType = Guid.NewGuid(); var policy = Guid.NewGuid(); var version = Guid.NewGuid(); var rule = Guid.NewGuid();
        using (var seed = database.CreateContext(new TestTenantContext()))
        {
            seed.Tenants.Add(new Tenant { Id = tenant, TenantCode = "UNLM", Host = "unlimited.local", ShardKey = "unlimited" });
            seed.Employees.Add(new Employee { Id = employee, TenantId = tenant, FirstName = "Sick", LastName = "Leave", Email = "sick@unlimited.local" });
            seed.LeaveTypes.Add(new LeaveType { Id = leaveType, TenantId = tenant, Code = "SL", Name = "Sick Leave", IsActive = true });
            seed.LeavePolicies.Add(new LeavePolicy { Id = policy, TenantId = tenant, Code = "UNL", Name = "Unlimited policy", IsActive = true });
            seed.LeavePolicyVersions.Add(new LeavePolicyVersion { Id = version, TenantId = tenant, LeavePolicyId = policy, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published });
            seed.LeavePolicyRules.Add(new LeavePolicyRule { Id = rule, TenantId = tenant, LeavePolicyVersionId = version, LeaveTypeId = leaveType, IsActive = true });
            seed.LeavePolicyEntitlementRules.Add(new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = tenant, LeavePolicyRuleId = rule, EntitlementMode = EntitlementMode.Unlimited });
            await seed.SaveChangesAsync();
        }
        using var context = database.CreateContext(new TestTenantContext(tenant));
        var result = await new LeaveBalanceSummaryReader(context, new StubIdentity(Result<RuntimeEmployeeIdentity>.Success(new(tenant, Guid.NewGuid(), employee))), new StubPolicy(leaveType, rule), TimeProvider.System).GetMineAsync();
        var item = Assert.Single(result.Value!);
        Assert.Equal(EntitlementMode.Unlimited, item.EntitlementMode);
        Assert.Null(item.AvailableQuantity);
        Assert.Empty(context.EmployeeLeaveBalances);
        Assert.Empty(context.LeaveBalanceTransactions);
    }

    [Fact]
    public async Task Does_not_treat_no_policy_as_unlimited()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var leaveType = Guid.NewGuid();
        using (var seed = database.CreateContext(new TestTenantContext()))
        {
            seed.Tenants.Add(new Tenant { Id = tenant, TenantCode = "NONE", Host = "none.local", ShardKey = "none" });
            seed.Employees.Add(new Employee { Id = employee, TenantId = tenant, FirstName = "No", LastName = "Policy", Email = "none@none.local" });
            seed.LeaveTypes.Add(new LeaveType { Id = leaveType, TenantId = tenant, Code = "NP", Name = "Not Configured", IsActive = true });
            await seed.SaveChangesAsync();
        }
        using var context = database.CreateContext(new TestTenantContext(tenant));
        var result = await new LeaveBalanceSummaryReader(context, new StubIdentity(Result<RuntimeEmployeeIdentity>.Success(new(tenant, Guid.NewGuid(), employee))), new StubPolicy(Guid.Empty, null), TimeProvider.System).GetMineAsync();
        Assert.Empty(result.Value!);
    }

    private sealed class StubIdentity(Result<RuntimeEmployeeIdentity> result) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class StubPolicy(Guid leaveTypeId, Guid? ruleId) : ILeavePolicyResolver
    {
        public Task<LeavePolicyResolutionResult> ResolveAsync(Guid tenantId, Guid employeeId, Guid requestedLeaveTypeId, DateOnly effectiveDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LeavePolicyResolutionResult(
                ruleId.HasValue && requestedLeaveTypeId == leaveTypeId ? LeavePolicyResolutionStatus.Resolved : LeavePolicyResolutionStatus.NoPolicy,
                tenantId, employeeId, requestedLeaveTypeId, effectiveDate, null, null, ruleId.HasValue && requestedLeaveTypeId == leaveTypeId ? ruleId : null, null, null,
                ruleId.HasValue && requestedLeaveTypeId == leaveTypeId ? "resolved" : "not configured"));
    }
}
