using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

public sealed class LeaveRequestReadServiceTests
{
    [Fact]
    public async Task List_is_employee_and_tenant_scoped_and_paged_in_deterministic_order()
    {
        using var fixture = await ReadFixture.CreateAsync();
        var service = fixture.ServiceFor(ReadFixture.EmployeeA);

        var firstPage = await service.GetMineAsync(1, 2);
        var secondPage = await service.GetMineAsync(2, 2);

        Assert.True(firstPage.Succeeded);
        Assert.Equal(3, firstPage.Value!.TotalCount);
        Assert.Equal(2, firstPage.Value.Items.Count);
        Assert.Equal(new[] { ReadFixture.RequestA3, ReadFixture.RequestA2 }, firstPage.Value.Items.Select(x => x.RequestId));
        Assert.Single(secondPage.Value!.Items);
        Assert.Equal(ReadFixture.RequestA1, secondPage.Value.Items[0].RequestId);
        Assert.DoesNotContain(firstPage.Value.Items, item => item.RequestId == ReadFixture.RequestB);
        Assert.DoesNotContain(firstPage.Value.Items, item => item.RequestId == ReadFixture.RequestTenantB);
    }

    [Fact]
    public async Task Cross_employee_and_cross_tenant_details_are_not_disclosed()
    {
        using var fixture = await ReadFixture.CreateAsync();
        var service = fixture.ServiceFor(ReadFixture.EmployeeA);

        var otherEmployee = await service.GetMineByIdAsync(ReadFixture.RequestB);
        var otherTenant = await service.GetMineByIdAsync(ReadFixture.RequestTenantB);

        Assert.False(otherEmployee.Succeeded);
        Assert.Equal(ResultStatus.NotFound, otherEmployee.Status);
        Assert.False(otherTenant.Succeeded);
        Assert.Equal(ResultStatus.NotFound, otherTenant.Status);
    }

    [Fact]
    public async Task Empty_linked_employee_returns_empty_page_and_unlinked_account_is_rejected()
    {
        using var fixture = await ReadFixture.CreateAsync();
        var empty = await fixture.ServiceFor(ReadFixture.EmployeeEmpty).GetMineAsync(1, 25);
        var unlinked = await new LeaveRequestReadService(
            fixture.ContextFor(ReadFixture.TenantA),
            new StubIdentity(Result<RuntimeEmployeeIdentity>.NotFound("The authenticated account is not linked to an Employee.")))
            .GetMineAsync(1, 25);

        Assert.True(empty.Succeeded);
        Assert.Empty(empty.Value!.Items);
        Assert.True(unlinked.Status == ResultStatus.NotFound);
        Assert.False(unlinked.Succeeded);
    }

    [Fact]
    public async Task Detail_returns_authoritative_days_and_persisted_events_in_order()
    {
        using var fixture = await ReadFixture.CreateAsync();
        var result = await fixture.ServiceFor(ReadFixture.EmployeeA).GetMineByIdAsync(ReadFixture.RequestA1);

        Assert.True(result.Succeeded);
        Assert.Equal(
            new[] { new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6) },
            result.Value!.RequestDays.Select(day => day.Date));
        Assert.Equal(1m, result.Value.RequestDays[0].RequestedQuantity);
        Assert.Equal(0.5m, result.Value.RequestDays[0].ChargeableQuantity);
        Assert.Equal("WorkingDay", result.Value.RequestDays[0].DayClassification);
        Assert.Equal("Authoritative fixture value", result.Value.RequestDays[0].CalculationReason);
        Assert.Equal(new[] { LeaveRequestEventType.Created, LeaveRequestEventType.Submitted }, result.Value.Events.Select(@event => @event.EventType));
        Assert.Equal(ReadFixture.RequestA1, result.Value.RequestId);
    }

    [Fact]
    public async Task Historical_allocated_request_remains_readable_without_submission_gates()
    {
        using var fixture = await ReadFixture.CreateAsync();
        var result = await fixture.ServiceFor(ReadFixture.EmployeeA).GetMineByIdAsync(ReadFixture.RequestA1);

        Assert.True(result.Succeeded);
        Assert.Equal(ReadFixture.RequestA1, result.Value!.RequestId);
    }

    private sealed class StubIdentity(Result<RuntimeEmployeeIdentity> result) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class ReadFixture : IDisposable
    {
        public static readonly Guid TenantA = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public static readonly Guid TenantB = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public static readonly Guid EmployeeA = new("aaaaaaaa-0000-0000-0000-000000000001");
        public static readonly Guid EmployeeB = new("aaaaaaaa-0000-0000-0000-000000000002");
        public static readonly Guid EmployeeEmpty = new("aaaaaaaa-0000-0000-0000-000000000003");
        public static readonly Guid EmployeeTenantB = new("bbbbbbbb-0000-0000-0000-000000000001");
        public static readonly Guid RequestA1 = new("aaaaaaaa-1111-1111-1111-000000000001");
        public static readonly Guid RequestA2 = new("aaaaaaaa-1111-1111-1111-000000000002");
        public static readonly Guid RequestA3 = new("aaaaaaaa-1111-1111-1111-000000000003");
        public static readonly Guid RequestB = new("aaaaaaaa-2222-2222-2222-000000000001");
        public static readonly Guid RequestTenantB = new("bbbbbbbb-2222-2222-2222-000000000001");

        private readonly SqliteInMemoryDatabase _database;
        private readonly Dictionary<Guid, Guid> _userByEmployee;

        private ReadFixture(SqliteInMemoryDatabase database, Dictionary<Guid, Guid> userByEmployee)
        {
            _database = database;
            _userByEmployee = userByEmployee;
        }

        public static async Task<ReadFixture> CreateAsync()
        {
            var database = new SqliteInMemoryDatabase();
            var users = new Dictionary<Guid, Guid>
            {
                [EmployeeA] = new("11111111-0000-0000-0000-000000000001"),
                [EmployeeB] = new("11111111-0000-0000-0000-000000000002"),
                [EmployeeEmpty] = new("11111111-0000-0000-0000-000000000003"),
                [EmployeeTenantB] = new("22222222-0000-0000-0000-000000000001")
            };

            await using (var catalog = database.CreateContext(new TestTenantContext()))
            {
                catalog.Tenants.AddRange(
                    new Tenant { Id = TenantA, TenantCode = "TESTA", Host = "testa.localhost", ShardKey = "testa", TenantName = "Test A" },
                    new Tenant { Id = TenantB, TenantCode = "TESTB", Host = "testb.localhost", ShardKey = "testb", TenantName = "Test B" });
                await catalog.SaveChangesAsync();
            }

            await SeedTenantAsync(database, TenantA, new[] { EmployeeA, EmployeeB, EmployeeEmpty });
            await SeedTenantAsync(database, TenantB, new[] { EmployeeTenantB });
            return new ReadFixture(database, users);
        }

        public LeaveRequestReadService ServiceFor(Guid employeeId)
        {
            var tenantId = employeeId == EmployeeTenantB ? TenantB : TenantA;
            return new LeaveRequestReadService(
                ContextFor(tenantId),
                new StubIdentity(Result<RuntimeEmployeeIdentity>.Success(new(tenantId, _userByEmployee[employeeId], employeeId))));
        }

        public HrmsDbContext ContextFor(Guid tenantId) => _database.CreateContext(new TestTenantContext(tenantId));

        private static async Task SeedTenantAsync(SqliteInMemoryDatabase database, Guid tenantId, IReadOnlyList<Guid> employeeIds)
        {
            await using var context = database.CreateContext(new TestTenantContext(tenantId));
            var leaveTypeId = Guid.NewGuid();
            var periodId = Guid.NewGuid();
            var policyId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            var ruleId = Guid.NewGuid();
            var historyByEmployee = employeeIds.ToDictionary(id => id, _ => Guid.NewGuid());

            context.AddRange(
                new LeaveType { Id = leaveTypeId, TenantId = tenantId, Code = "CL", Name = "Casual Leave" },
                new LeavePeriod { Id = periodId, TenantId = tenantId, Code = "FY26", Name = "Financial Year 2026", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) },
                new LeavePolicy { Id = policyId, TenantId = tenantId, Code = "DEFAULT", Name = "Default Policy" },
                new LeavePolicyVersion { Id = versionId, TenantId = tenantId, LeavePolicyId = policyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published },
                new LeavePolicyRule { Id = ruleId, TenantId = tenantId, LeavePolicyVersionId = versionId, LeaveTypeId = leaveTypeId },
                new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyRuleId = ruleId, EntitlementMode = EntitlementMode.Allocated });
            context.AddRange(employeeIds.Select((id, index) => new Employee { Id = id, TenantId = tenantId, EmployeeCode = $"EMP-{index + 1}", FirstName = "Test", LastName = $"Employee {index + 1}", Email = $"employee{index + 1}@test.local", DateOfJoining = new(2020, 1, 1) }));
            context.AddRange(employeeIds.Select(id => new EmployeeEmploymentHistory { Id = historyByEmployee[id], TenantId = tenantId, EmployeeId = id, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active }));
            await context.SaveChangesAsync();

            if (tenantId == TenantA)
            {
                AddRequest(context, RequestA1, TenantA, EmployeeA, leaveTypeId, periodId, versionId, ruleId, historyByEmployee[EmployeeA], new(2026, 10, 1), new(2026, 10, 5), includeDaysAndEvents: true);
                AddRequest(context, RequestA2, TenantA, EmployeeA, leaveTypeId, periodId, versionId, ruleId, historyByEmployee[EmployeeA], new(2026, 10, 2), new(2026, 10, 7));
                AddRequest(context, RequestA3, TenantA, EmployeeA, leaveTypeId, periodId, versionId, ruleId, historyByEmployee[EmployeeA], new(2026, 10, 2), new(2026, 10, 8));
                AddRequest(context, RequestB, TenantA, EmployeeB, leaveTypeId, periodId, versionId, ruleId, historyByEmployee[EmployeeB], new(2026, 10, 4), new(2026, 10, 9));
            }
            else
            {
                AddRequest(context, RequestTenantB, TenantB, EmployeeTenantB, leaveTypeId, periodId, versionId, ruleId, historyByEmployee[EmployeeTenantB], new(2026, 10, 5), new(2026, 10, 10));
            }
            await context.SaveChangesAsync();
        }

        private static void AddRequest(HrmsDbContext context, Guid id, Guid tenantId, Guid employeeId, Guid leaveTypeId, Guid periodId, Guid versionId, Guid ruleId, Guid historyId, DateTime submittedAt, DateOnly startDate, bool includeDaysAndEvents = false)
        {
            context.LeaveRequests.Add(new LeaveRequest { Id = id, TenantId = tenantId, EmployeeId = employeeId, LeaveTypeId = leaveTypeId, LeavePeriodId = periodId, LeavePolicyVersionId = versionId, LeavePolicyRuleId = ruleId, EmployeeEmploymentHistoryId = historyId, StartDate = startDate, EndDate = startDate.AddDays(1), RequestedQuantity = 2, ChargeableQuantity = 2, SubmittedAtUtc = submittedAt, IdempotencyKey = id.ToString(), PayloadFingerprint = new string('a', 64) });
            if (!includeDaysAndEvents) return;
            context.LeaveRequestDays.AddRange(
                new LeaveRequestDay { Id = Guid.NewGuid(), TenantId = tenantId, LeaveRequestId = id, Date = new(2026, 10, 6), RequestedQuantity = 1, ChargeableQuantity = 0.5m, DayClassification = "Weekend", CalculationReason = "Later persisted day" },
                new LeaveRequestDay { Id = Guid.NewGuid(), TenantId = tenantId, LeaveRequestId = id, Date = new(2026, 10, 5), RequestedQuantity = 1, ChargeableQuantity = 0.5m, DayClassification = "WorkingDay", CalculationReason = "Authoritative fixture value" });
            context.LeaveRequestEvents.AddRange(
                new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = tenantId, LeaveRequestId = id, EventType = LeaveRequestEventType.Submitted, OccurredAtUtc = submittedAt.AddMinutes(1), ActorType = LeaveBalanceActorType.System },
                new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = tenantId, LeaveRequestId = id, EventType = LeaveRequestEventType.Created, OccurredAtUtc = submittedAt, ActorType = LeaveBalanceActorType.System });
        }

        public void Dispose() => _database.Dispose();
    }
}
