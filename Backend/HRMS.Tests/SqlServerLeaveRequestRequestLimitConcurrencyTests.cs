using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

[Collection("SQL Server Leave Request Concurrency")]
public sealed class SqlServerLeaveRequestRequestLimitConcurrencyTests
{
    private readonly SqlServerLeaveRequestConcurrencyFixture _fixture;

    public SqlServerLeaveRequestRequestLimitConcurrencyTests(SqlServerLeaveRequestConcurrencyFixture fixture) => _fixture = fixture;

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Request_count_limit_serializes_same_employee_submissions()
    {
        var scenario = await SeedAsync("count-limit-existing-request", new(2027, 1, 1), new(2027, 6, 30), new(2027, 6, 10), maximumRequests: 2, existingQuantity: 1m);
        var results = await Task.WhenAll(
            SubmitAsync(scenario, "request-count-a", new(2027, 6, 11), new(2027, 6, 11), 1m),
            SubmitAsync(scenario, "request-count-b", new(2027, 6, 12), new(2027, 6, 12), 1m));

        Assert.Single(results, x => x.Succeeded);
        Assert.Single(results, x => !x.Succeeded && x.Message.Contains("RequestCountLimitExceeded", StringComparison.Ordinal));
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(2, await db.LeaveRequests.CountAsync(x =>
            x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA &&
            x.LeaveTypeId == scenario.LeaveTypeId && x.LeavePeriodId == scenario.LeavePeriodId &&
            (x.Status == LeaveRequestStatus.PendingApproval || x.Status == LeaveRequestStatus.Approved)));
        Assert.Equal(0, await db.LeaveBalanceTransactions.CountAsync(x => x.LeaveRequestId != null && x.LeaveRequestId != Guid.Empty && x.LeaveTypeId == scenario.LeaveTypeId));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Period_quantity_limit_serializes_same_employee_submissions()
    {
        var scenario = await SeedAsync("quantity-limit-existing-request", new(2027, 7, 1), new(2027, 12, 31), new(2027, 7, 10), maximumQuantity: 5m, existingQuantity: 1m);
        var results = await Task.WhenAll(
            SubmitAsync(scenario, "request-quantity-a", new(2027, 7, 11), new(2027, 7, 13), 3m),
            SubmitAsync(scenario, "request-quantity-b", new(2027, 7, 15), new(2027, 7, 17), 3m));

        Assert.Single(results, x => x.Succeeded);
        Assert.Single(results, x => !x.Succeeded && x.Message.Contains("RequestQuantityLimitExceeded", StringComparison.Ordinal));
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        var requests = await db.LeaveRequests.Where(x =>
            x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA &&
            x.LeaveTypeId == scenario.LeaveTypeId && x.LeavePeriodId == scenario.LeavePeriodId &&
            (x.Status == LeaveRequestStatus.PendingApproval || x.Status == LeaveRequestStatus.Approved)).ToListAsync();
        Assert.Equal(2, requests.Count);
        Assert.Equal(4m, requests.Sum(x => x.ChargeableQuantity));
    }

    private async Task<Scenario> SeedAsync(
        string existingKey,
        DateOnly periodStart,
        DateOnly periodEnd,
        DateOnly historicalDate,
        int? maximumRequests = null,
        decimal? maximumQuantity = null,
        decimal existingQuantity = 1m)
    {
        var scenario = new Scenario(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _fixture.PolicyVersionId);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        db.AddRange(
            new LeaveType { Id = scenario.LeaveTypeId, TenantId = _fixture.TenantA, Code = $"LIMIT-{scenario.LeaveTypeId:N}"[..14], Name = "Request Limit SQL", DefaultUnit = LeaveUnit.Day, IsActive = true },
            new LeavePeriod { Id = scenario.LeavePeriodId, TenantId = _fixture.TenantA, Code = $"PER-{scenario.LeavePeriodId:N}"[..14], Name = "Request Limit Period", StartDate = periodStart, EndDate = periodEnd, IsActive = true },
            new LeavePolicyRule { Id = scenario.PolicyRuleId, TenantId = _fixture.TenantA, LeavePolicyVersionId = _fixture.PolicyVersionId, LeaveTypeId = scenario.LeaveTypeId, IsActive = true },
            new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = _fixture.TenantA, LeavePolicyRuleId = scenario.PolicyRuleId, EntitlementMode = EntitlementMode.Unlimited },
            new LeavePolicyRequestRule { Id = Guid.NewGuid(), TenantId = _fixture.TenantA, LeavePolicyRuleId = scenario.PolicyRuleId, MaximumRequestsPerPeriod = maximumRequests, MaximumQuantityPerPeriod = maximumQuantity, RequestLimitPeriod = RequestLimitPeriod.LeavePeriod });
        await db.SaveChangesAsync();

        await SeedHistoricalRequestAsync(scenario, existingQuantity, historicalDate, existingKey);
        return scenario;
    }

    private async Task SeedHistoricalRequestAsync(Scenario scenario, decimal quantity, DateOnly date, string key)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        var requestId = Guid.NewGuid();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = requestId,
            TenantId = _fixture.TenantA,
            EmployeeId = _fixture.EmployeeA,
            LeaveTypeId = scenario.LeaveTypeId,
            LeavePeriodId = scenario.LeavePeriodId,
            LeavePolicyVersionId = _fixture.PolicyVersionId,
            LeavePolicyRuleId = scenario.PolicyRuleId,
            EmployeeEmploymentHistoryId = _fixture.EmploymentA,
            PolicyGenderSnapshot = Gender.Unspecified,
            StartDate = date,
            EndDate = date,
            RequestedQuantity = quantity,
            ChargeableQuantity = quantity,
            Status = LeaveRequestStatus.PendingApproval,
            SubmittedAtUtc = DateTime.UtcNow,
            IdempotencyKey = key,
            PayloadFingerprint = new string('h', 64),
            Days = [new LeaveRequestDay { Id = Guid.NewGuid(), TenantId = _fixture.TenantA, Date = date, RequestedQuantity = quantity, ChargeableQuantity = quantity, IsEmployeeRequested = true }]
        });
        await db.SaveChangesAsync();
    }

    private async Task<Result<LeaveRequestSubmissionResult>> SubmitAsync(Scenario scenario, string key, DateOnly start, DateOnly end, decimal quantity)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA, _fixture.UserA);
        var tenantContext = new TestTenantContext(_fixture.TenantA, _fixture.UserA);
        var identity = new FixedIdentity(_fixture.TenantA, _fixture.UserA, _fixture.EmployeeA);
        var employment = new EffectiveEmploymentResolver(db, tenantContext);
        var period = new LeavePeriodResolver(db, tenantContext);
        var policy = new LeavePolicyResolver(db, null, tenantContext);
        var validation = new LeaveRequestValidationService(db, identity, employment, period, policy);
        var input = new LeaveRequestSubmissionInput(scenario.LeaveTypeId, start, end, key);
        return await new LeaveRequestSubmissionService(
            db,
            identity,
            validation,
            new SqlServerLeaveRequestSubmissionLock(db),
            TimeProvider.System)
            .SubmitAsync(input);
    }

    private sealed class FixedIdentity(Guid tenantId, Guid userId, Guid employeeId) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenantId, userId, employeeId)));
    }

    private sealed record Scenario(Guid LeaveTypeId, Guid LeavePeriodId, Guid PolicyRuleId, Guid PolicyVersionId);
}
