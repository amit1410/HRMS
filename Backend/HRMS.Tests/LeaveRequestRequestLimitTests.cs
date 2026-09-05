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

public sealed class LeaveRequestRequestLimitTests
{
    [Theory]
    [InlineData(1, ResultStatus.ValidationFailed)]
    [InlineData(2, ResultStatus.Success)]
    public async Task Minimum_request_quantity_uses_chargeable_days(int dayCount, ResultStatus expected)
    {
        using var fixture = await LimitFixture.CreateAsync(minimum: 2m);

        var result = await fixture.ValidateAsync(dayCount);

        Assert.Equal(expected, result.Status);
        if (expected != ResultStatus.Success)
            Assert.Contains("MinimumRequestQuantityNotMet", result.Message);
    }

    [Theory]
    [InlineData(2, ResultStatus.Success)]
    [InlineData(3, ResultStatus.ValidationFailed)]
    public async Task Maximum_request_quantity_is_inclusive(int dayCount, ResultStatus expected)
    {
        using var fixture = await LimitFixture.CreateAsync(maximum: 2m);

        var result = await fixture.ValidateAsync(dayCount);

        Assert.Equal(expected, result.Status);
        if (expected != ResultStatus.Success)
            Assert.Contains("MaximumRequestQuantityExceeded", result.Message);
    }

    [Fact]
    public async Task Maximum_consecutive_quantity_rejects_an_excessive_run()
    {
        using var fixture = await LimitFixture.CreateAsync(maximumConsecutive: 2m);

        var result = await fixture.ValidateAsync(3);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains("MaximumConsecutiveLeaveExceeded", result.Message);
    }

    [Fact]
    public async Task Leave_period_request_count_includes_pending_and_approved_but_excludes_terminal_requests()
    {
        using var fixture = await LimitFixture.CreateAsync(maximumRequests: 2, period: RequestLimitPeriod.LeavePeriod);
        await fixture.AddHistoricalRequestAsync(LeaveRequestStatus.PendingApproval, 1m, fixture.LeaveTypeId, fixture.EmployeeId);
        await fixture.AddHistoricalRequestAsync(LeaveRequestStatus.Rejected, 1m, fixture.LeaveTypeId, fixture.EmployeeId);

        var first = await fixture.ValidateAsync(1);
        Assert.Equal(ResultStatus.Success, first.Status);

        await fixture.AddHistoricalRequestAsync(LeaveRequestStatus.Approved, 1m, fixture.LeaveTypeId, fixture.EmployeeId);
        var second = await fixture.ValidateAsync(1, "count-limit");
        Assert.Equal(ResultStatus.ValidationFailed, second.Status);
        Assert.Contains("RequestCountLimitExceeded", second.Message);
    }

    [Fact]
    public async Task Leave_period_quantity_limit_uses_persisted_chargeable_quantity()
    {
        using var fixture = await LimitFixture.CreateAsync(maximumQuantity: 3m, period: RequestLimitPeriod.LeavePeriod);
        await fixture.AddHistoricalRequestAsync(LeaveRequestStatus.Approved, 2m, fixture.LeaveTypeId, fixture.EmployeeId);

        var result = await fixture.ValidateAsync(2);

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains("RequestQuantityLimitExceeded", result.Message);
    }

    [Fact]
    public async Task Monthly_limits_are_evaluated_per_gregorian_request_day_month()
    {
        using var fixture = await LimitFixture.CreateAsync(maximumQuantity: 1m, period: RequestLimitPeriod.Month);
        await fixture.AddHistoricalRequestAsync(LeaveRequestStatus.Approved, 1m, fixture.LeaveTypeId, fixture.EmployeeId, new DateOnly(2027, 1, 31));

        var result = await fixture.ValidateAsync(2, "month-limit", new(2027, 1, 31), new(2027, 2, 1));

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains("RequestQuantityLimitExceeded", result.Message);
    }

    [Fact]
    public async Task Period_history_is_scoped_to_employee_and_leave_type()
    {
        using var fixture = await LimitFixture.CreateAsync(maximumRequests: 1, period: RequestLimitPeriod.LeavePeriod);
        await fixture.AddHistoricalRequestAsync(LeaveRequestStatus.Approved, 1m, fixture.OtherLeaveTypeId, fixture.EmployeeId);
        await fixture.AddHistoricalRequestAsync(LeaveRequestStatus.Approved, 1m, fixture.LeaveTypeId, fixture.OtherEmployeeId);

        var result = await fixture.ValidateAsync(1);

        Assert.Equal(ResultStatus.Success, result.Status);
    }

    [Fact]
    public async Task Same_idempotency_key_is_excluded_from_period_history_for_replay()
    {
        using var fixture = await LimitFixture.CreateAsync(maximumRequests: 1, period: RequestLimitPeriod.LeavePeriod);
        await fixture.AddHistoricalRequestAsync(LeaveRequestStatus.Approved, 1m, fixture.LeaveTypeId, fixture.EmployeeId, idempotencyKey: "same-key");

        var result = await fixture.ValidateAsync(1, "same-key");

        Assert.Equal(ResultStatus.Success, result.Status);
    }

    private sealed class LimitFixture : IDisposable
    {
        private readonly SqliteInMemoryDatabase _database;
        private readonly HrmsDbContext _context;
        private readonly Guid _policyId = Guid.NewGuid();
        private readonly Guid _policyVersionId = Guid.NewGuid();
        private readonly Guid _policyRuleId = Guid.NewGuid();
        private readonly Guid _employmentId = Guid.NewGuid();
        private readonly Guid _otherEmploymentId = Guid.NewGuid();
        private LeaveRequestValidationService _service;

        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid LeaveTypeId { get; } = Guid.NewGuid();
        public Guid OtherLeaveTypeId { get; } = Guid.NewGuid();
        public Guid OtherEmployeeId { get; } = Guid.NewGuid();
        public Guid LeavePeriodId { get; } = Guid.NewGuid();

        private LimitFixture(SqliteInMemoryDatabase database, HrmsDbContext context, LeaveRequestValidationService service) =>
            (_database, _context, _service) = (database, context, service);

        public static async Task<LimitFixture> CreateAsync(
            decimal? minimum = null,
            decimal? maximum = null,
            decimal? maximumConsecutive = null,
            int? maximumRequests = null,
            decimal? maximumQuantity = null,
            RequestLimitPeriod? period = null)
        {
            var database = new SqliteInMemoryDatabase();
            var tenantContext = new TestTenantContext();
            var seed = database.CreateContext(tenantContext);
            var fixture = new LimitFixture(database, seed, null!);
            tenantContext.TenantId = fixture.TenantId;
            tenantContext.UserId = fixture.UserId;
            seed.Tenants.Add(new Tenant { Id = fixture.TenantId, TenantCode = "LIMIT" + fixture.TenantId.ToString("N")[..4], Host = "limit.local", ShardKey = fixture.TenantId.ToString("N"), TenantName = "Limits" });
            seed.LeaveTypes.Add(new LeaveType { Id = fixture.LeaveTypeId, TenantId = fixture.TenantId, Code = "ANNUAL", Name = "Annual", DefaultUnit = LeaveUnit.Day, IsActive = true });
            seed.LeaveTypes.Add(new LeaveType { Id = fixture.OtherLeaveTypeId, TenantId = fixture.TenantId, Code = "OTHER", Name = "Other", DefaultUnit = LeaveUnit.Day, IsActive = true });
            seed.Employees.Add(new Employee { Id = fixture.EmployeeId, TenantId = fixture.TenantId, FirstName = "Limit", LastName = "Employee", Email = fixture.EmployeeId + "@test.local", DateOfJoining = new(2026, 1, 1), Gender = Gender.Unspecified });
            seed.Employees.Add(new Employee { Id = fixture.OtherEmployeeId, TenantId = fixture.TenantId, FirstName = "Other", LastName = "Employee", Email = fixture.OtherEmployeeId + "@test.local", DateOfJoining = new(2026, 1, 1), Gender = Gender.Unspecified });
            seed.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = fixture._employmentId, TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, EffectiveFrom = new(2026, 1, 1) });
            seed.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = fixture._otherEmploymentId, TenantId = fixture.TenantId, EmployeeId = fixture.OtherEmployeeId, EffectiveFrom = new(2026, 1, 1) });
            seed.LeavePeriods.Add(new LeavePeriod { Id = fixture.LeavePeriodId, TenantId = fixture.TenantId, Code = "2027", Name = "2027", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31), IsActive = true });
            seed.LeavePolicies.Add(new LeavePolicy { Id = fixture._policyId, TenantId = fixture.TenantId, Code = "LIMIT", Name = "Limit policy", IsActive = true });
            seed.LeavePolicyVersions.Add(new LeavePolicyVersion { Id = fixture._policyVersionId, TenantId = fixture.TenantId, LeavePolicyId = fixture._policyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published, Priority = 1 });
            seed.LeavePolicyRules.Add(new LeavePolicyRule { Id = fixture._policyRuleId, TenantId = fixture.TenantId, LeavePolicyVersionId = fixture._policyVersionId, LeaveTypeId = fixture.LeaveTypeId, IsActive = true });
            seed.LeavePolicyEntitlementRules.Add(new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, LeavePolicyRuleId = fixture._policyRuleId, EntitlementMode = EntitlementMode.Unlimited });
            seed.LeavePolicyRequestRules.Add(new LeavePolicyRequestRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, LeavePolicyRuleId = fixture._policyRuleId, MinimumRequestQuantity = minimum, MaximumRequestQuantity = maximum, MaximumConsecutiveQuantity = maximumConsecutive, MaximumRequestsPerPeriod = maximumRequests, MaximumQuantityPerPeriod = maximumQuantity, RequestLimitPeriod = period });
            await seed.SaveChangesAsync();

            fixture._service = new LeaveRequestValidationService(
                seed,
                new FixedIdentity(fixture.TenantId, fixture.UserId, fixture.EmployeeId),
                new FixedEmployment(fixture.TenantId, fixture.EmployeeId, fixture._employmentId),
                new FixedPeriod(fixture.TenantId, fixture.LeavePeriodId),
                new FixedPolicy(fixture.TenantId, fixture.EmployeeId, fixture.LeaveTypeId, fixture._policyId, fixture._policyVersionId, fixture._policyRuleId));
            return fixture;
        }

        public async Task<Result<LeaveRequestValidationResult>> ValidateAsync(int dayCount, string key = "key", DateOnly? start = null, DateOnly? end = null)
        {
            var first = start ?? new(2027, 1, 10);
            var last = end ?? first.AddDays(dayCount - 1);
            return await _service.ValidateAsync(new(LeaveTypeId, first, last, key));
        }

        public async Task AddHistoricalRequestAsync(LeaveRequestStatus status, decimal quantity, Guid leaveTypeId, Guid employeeId, string? idempotencyKey = null) =>
            await AddHistoricalRequestAsync(status, quantity, leaveTypeId, employeeId, null, null, idempotencyKey);

        public async Task AddHistoricalRequestAsync(LeaveRequestStatus status, decimal quantity, Guid leaveTypeId, Guid employeeId, DateOnly? day, DateOnly? end = null, string? idempotencyKey = null)
        {
            var requestDay = day ?? new(2027, 3, 10);
            var request = new LeaveRequest { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, LeaveTypeId = leaveTypeId, LeavePeriodId = LeavePeriodId, LeavePolicyVersionId = _policyVersionId, LeavePolicyRuleId = _policyRuleId, EmployeeEmploymentHistoryId = employeeId == EmployeeId ? _employmentId : _otherEmploymentId, StartDate = requestDay, EndDate = end ?? requestDay, RequestedQuantity = quantity, ChargeableQuantity = quantity, Status = status, SubmittedAtUtc = DateTime.UtcNow, IdempotencyKey = idempotencyKey ?? Guid.NewGuid().ToString("N"), PayloadFingerprint = Guid.NewGuid().ToString("N") };
            _context.LeaveRequests.Add(request);
            _context.LeaveRequestDays.Add(new LeaveRequestDay { Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = request.Id, Date = requestDay, RequestedQuantity = quantity, ChargeableQuantity = quantity, IsEmployeeRequested = true });
            await _context.SaveChangesAsync();
        }

        public void Dispose() { _context.Dispose(); _database.Dispose(); }

        private sealed class FixedIdentity(Guid tenantId, Guid userId, Guid employeeId) : IEmployeeIdentityResolver
        { public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken ct = default) => Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenantId, userId, employeeId))); }
        private sealed class FixedEmployment(Guid tenantId, Guid employeeId, Guid historyId) : IEffectiveEmploymentResolver
        { public Task<EffectiveEmploymentResolutionResult> ResolveAsync(Guid tenantId, Guid employeeId, DateOnly date, CancellationToken ct = default) => Task.FromResult(new EffectiveEmploymentResolutionResult(EffectiveEmploymentResolutionStatus.Resolved, tenantId, employeeId, date, new EffectiveEmploymentSnapshot(HistoryId: historyId, TenantId: tenantId, EmployeeId: employeeId, EffectiveFrom: new(2026, 1, 1), EffectiveTo: null, HoldingCompanyId: null, LobId: null, OrganisationId: null, DepartmentId: null, SubDepartmentId: null, SectionId: null, SubSectionId: null, FunctionId: null, SubFunctionId: null, GradeId: null, DesignationId: null, EmployeeTypeId: null, CountryLocationId: null, WorkLocationId: null, CostCenterId: null, ManagerId: null, EmploymentType: EmploymentType.FullTime, EmploymentStatus: EmployeeStatus.Active, DateOfJoining: new(2026, 1, 1), GroupDateOfJoining: null, DateOfLeaving: null, Gender: Gender.Unspecified), "resolved")); }
        private sealed class FixedPeriod(Guid tenantId, Guid periodId) : ILeavePeriodResolver
        { public Task<LeavePeriodResolutionResult> ResolveAsync(Guid tenantId, DateOnly date, CancellationToken ct = default) => Task.FromResult(new LeavePeriodResolutionResult(LeavePeriodResolutionStatus.Resolved, tenantId, date, new(periodId, "2027", "2027", new(2027, 1, 1), new(2027, 12, 31), true, DateTime.UtcNow, null, "token"), "resolved")); }
        private sealed class FixedPolicy(Guid tenantId, Guid employeeId, Guid leaveTypeId, Guid policyId, Guid versionId, Guid ruleId) : ILeavePolicyResolver
        { public Task<LeavePolicyResolutionResult> ResolveAsync(Guid tenantId, Guid employeeId, Guid leaveTypeId, DateOnly date, CancellationToken ct = default) => Task.FromResult(new LeavePolicyResolutionResult(LeavePolicyResolutionStatus.Resolved, tenantId, employeeId, leaveTypeId, date, policyId, versionId, ruleId, 1, 0, "resolved")); }
    }
}
