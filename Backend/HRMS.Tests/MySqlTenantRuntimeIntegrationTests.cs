using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Application.Security;
using HRMS.Domain.Entities;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using MySql.Data.MySqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using HRMS.Infrastructure.Sharding;
using System.Runtime.ExceptionServices;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlTenantRuntimeIntegrationTests
{
    [Fact]
    public async Task MySql_tracked_insert_persists_a_sixteen_byte_row_version()
    {
        await RunScenarioAsync(async scenario =>
        {
            await using var context = scenario.CreateContext();
            var balance = scenario.NewBalance();
            context.EmployeeLeaveBalances.Add(balance);

            await context.SaveChangesAsync();

            Assert.Equal(16, balance.RowVersion.Length);
            await using var verification = scenario.CreateContext();
            var stored = await verification.EmployeeLeaveBalances
                .AsNoTracking()
                .SingleAsync(x => x.Id == balance.Id);
            Assert.Equal(16, stored.RowVersion.Length);
            Assert.Equal(balance.RowVersion, stored.RowVersion);
        });
    }

    [Fact]
    public async Task MySql_tracked_update_rotates_the_row_version()
    {
        await RunScenarioAsync(async scenario =>
        {
            await using var setup = scenario.CreateContext();
            var balance = scenario.NewBalance();
            setup.EmployeeLeaveBalances.Add(balance);
            await setup.SaveChangesAsync();
            var previous = balance.RowVersion.ToArray();

            await using var context = scenario.CreateContext();
            var persisted = await context.EmployeeLeaveBalances.SingleAsync(x => x.Id == balance.Id);
            persisted.GrantedQuantity = 12;
            await context.SaveChangesAsync();

            Assert.Equal(16, persisted.RowVersion.Length);
            Assert.NotEqual(previous, persisted.RowVersion);
            await using var verification = scenario.CreateContext();
            var stored = await verification.EmployeeLeaveBalances.AsNoTracking().SingleAsync(x => x.Id == balance.Id);
            Assert.Equal(persisted.RowVersion, stored.RowVersion);
        });
    }

    [Fact]
    public async Task MySql_dateonly_and_nullable_dateonly_round_trip_as_dates()
    {
        await RunScenarioAsync(async scenario =>
        {
            await using (var setup = scenario.CreateContext())
            {
                var employee = await setup.Employees.SingleAsync(x => x.Id == scenario.EmployeeId);
                employee.DateOfLeaving = new DateOnly(2026, 12, 31);
                await setup.SaveChangesAsync();
            }

            await using var reload = scenario.CreateContext();
            var persisted = await reload.Employees.AsNoTracking().SingleAsync(x => x.Id == scenario.EmployeeId);
            Assert.Equal(new DateOnly(2020, 1, 1), persisted.DateOfJoining);
            Assert.Equal(new DateOnly(2026, 12, 31), persisted.DateOfLeaving);

            var second = await reload.Employees.AsNoTracking().SingleAsync(x => x.Id == scenario.EmployeeBId);
            Assert.Null(second.DateOfLeaving);
        });
    }

    [Fact]
    public async Task MySql_stale_tracked_token_raises_concurrency_exception()
    {
        await RunScenarioAsync(async scenario =>
        {
            await using var setup = scenario.CreateContext();
            var balance = scenario.NewBalance();
            setup.EmployeeLeaveBalances.Add(balance);
            await setup.SaveChangesAsync();

            await using var first = scenario.CreateContext();
            await using var second = scenario.CreateContext();
            var firstBalance = await first.EmployeeLeaveBalances.SingleAsync(x => x.Id == balance.Id);
            var secondBalance = await second.EmployeeLeaveBalances.SingleAsync(x => x.Id == balance.Id);
            firstBalance.GrantedQuantity = 11;
            await first.SaveChangesAsync();

            secondBalance.GrantedQuantity = 12;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        });
    }

    [Fact]
    public async Task MySql_employee_code_sequence_cas_rotates_token_and_rejects_stale_expected_value()
    {
        await RunScenarioAsync(async scenario =>
        {
            var tenant = new TestTenantContext(scenario.TenantId);
            await using (var context = scenario.CreateContext(tenant))
            {
                var service = new EmployeeCodeSequenceService(
                    context,
                    tenant,
                    new MySqlEmployeeCodeSequenceUpdater(context, new MySqlConcurrencyTokenGenerator()));

                var first = await service.AllocateAsync(
                    scenario.EmployeeCodeRuleId, EmployeeCodeSequenceScope.Tenant, "MYSQL-TEST", EmployeeCodeResetPeriod.Never, "NONE", 100, 1);
                Assert.True(first.Succeeded, first.Message);
                Assert.Equal(100, first.Value);
            }

            byte[] firstToken;
            await using (var read = scenario.CreateContext(tenant))
            {
                var sequence = await read.EmployeeCodeSequences.AsNoTracking().SingleAsync(x => x.TenantId == scenario.TenantId);
                firstToken = sequence.RowVersion;
                Assert.Equal(101, sequence.NextNumber);
            }

            await using (var context = scenario.CreateContext(tenant))
            {
                var service = new EmployeeCodeSequenceService(
                    context,
                    tenant,
                    new MySqlEmployeeCodeSequenceUpdater(context, new MySqlConcurrencyTokenGenerator()));

                var second = await service.AllocateAsync(
                    scenario.EmployeeCodeRuleId, EmployeeCodeSequenceScope.Tenant, "MYSQL-TEST", EmployeeCodeResetPeriod.Never, "NONE");
                Assert.True(second.Succeeded, second.Message);
                Assert.Equal(101, second.Value);
            }

            await using var verification = scenario.CreateContext(tenant);
            var current = await verification.EmployeeCodeSequences.AsNoTracking().SingleAsync(x => x.TenantId == scenario.TenantId);
            Assert.Equal(102, current.NextNumber);
            Assert.Equal(16, current.RowVersion.Length);
            Assert.NotEqual(firstToken, current.RowVersion);

            var staleUpdater = new MySqlEmployeeCodeSequenceUpdater(verification, new MySqlConcurrencyTokenGenerator());
            var staleRows = await staleUpdater.AdvanceAsync(scenario.TenantId, current.Id, 101, 999);
            Assert.Equal(0, staleRows);
        });
    }

    [Fact]
    public async Task MySql_employee_serialization_lock_executes_inside_the_active_transaction()
    {
        await RunScenarioAsync(async scenario =>
        {
            await using var context = scenario.CreateContext(new TestTenantContext(scenario.TenantId));
            await using var transaction = await context.Database.BeginTransactionAsync();
            Assert.Contains("MySql", context.Database.ProviderName, StringComparison.OrdinalIgnoreCase);

            var adapter = new MySqlLeaveRequestSubmissionLock(context);
            await adapter.AcquireAsync(scenario.TenantId, scenario.EmployeeId);

            Assert.NotNull(context.Database.CurrentTransaction);
            Assert.Same(transaction.GetDbTransaction(), context.Database.CurrentTransaction!.GetDbTransaction());
            await transaction.RollbackAsync();
        });
    }

    [Fact]
    public async Task MySql_real_innodb_deadlock_is_1213_and_retryable()
    {
        await RunScenarioAsync(async scenario =>
        {
            var firstHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondHeld = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var first = DeadlockParticipantAsync(
                scenario,
                scenario.EmployeeId,
                scenario.EmployeeBId,
                firstHeld,
                secondHeld);
            var second = DeadlockParticipantAsync(
                scenario,
                scenario.EmployeeBId,
                scenario.EmployeeId,
                secondHeld,
                firstHeld);

            var outcomes = await Task.WhenAll(first, second);
            var victims = outcomes.Where(x => x.Exception is not null).ToArray();
            var survivors = outcomes.Where(x => x.Exception is null).ToArray();

            Assert.Single(victims);
            Assert.Single(survivors);
            var victimException = victims[0].Exception!;
            var mysqlException = FindMySqlException(victimException);
            Assert.NotNull(mysqlException);
            Assert.Equal(1213, mysqlException!.Number);
            Assert.True(new MySqlTransientErrorClassifier().IsDeadlock(victimException));
        });
    }

    [Fact]
    public async Task MySql_leave_policy_resolver_collection_predicates_execute_on_real_provider()
    {
        await RunScenarioAsync(async scenario =>
        {
            await using var context = scenario.CreateContext(new TestTenantContext(scenario.TenantId));
            var resolver = new LeavePolicyResolver(context, new FixedEmploymentResolver(scenario.TenantId, scenario.EmployeeId));

            var one = await resolver.ResolveAsync(scenario.TenantId, scenario.EmployeeId, scenario.LeaveTypeId, new DateOnly(2026, 6, 1));
            Assert.Equal(LeavePolicyResolutionStatus.Resolved, one.Status);

            var many = await resolver.ResolveAsync(scenario.TenantId, scenario.EmployeeId, scenario.LeaveTypeId, new DateOnly(2026, 6, 1));
            Assert.Equal(LeavePolicyResolutionStatus.Resolved, many.Status);

            var empty = await resolver.ResolveAsync(scenario.TenantId, scenario.EmployeeId, scenario.LeaveTypeId, new DateOnly(2025, 6, 1));
            Assert.Equal(LeavePolicyResolutionStatus.NoPolicy, empty.Status);
        });
    }

    [Fact]
    public async Task MySql_leave_balance_summary_returns_finite_and_current_unlimited_entitlements_without_fake_balance()
    {
        await RunScenarioAsync(async scenario =>
        {
            await using var context = scenario.CreateContext(new TestTenantContext(scenario.TenantId));
            var tenantContext = new TestTenantContext(scenario.TenantId);
            context.LeavePolicyEntitlementRules.Add(new LeavePolicyEntitlementRule
            {
                Id = Guid.NewGuid(), TenantId = scenario.TenantId, LeavePolicyRuleId = scenario.MultiRuleId,
                EntitlementMode = EntitlementMode.Unlimited, EntitlementSource = EntitlementSource.PolicyAccrual
            });
            context.EmployeeLeaveBalances.Add(new EmployeeLeaveBalance
            {
                Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId,
                LeaveTypeId = scenario.SecondLeaveTypeId, LeavePeriodId = scenario.LeavePeriodId,
                GrantedQuantity = 10, ReservedQuantity = 2, ConsumedQuantity = 3
            });
            await context.SaveChangesAsync();

            var resolver = new LeavePolicyResolver(context, new EffectiveEmploymentResolver(context, tenantContext), tenantContext);
            var reader = new LeaveBalanceSummaryReader(context, new FixedIdentity(scenario.TenantId, Guid.NewGuid(), scenario.EmployeeId), resolver, new FixedClock(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)));
            var result = await reader.GetMineAsync();

            Assert.True(result.Succeeded, result.Message);
            var rows = result.Value!;
            var leaveTypeCode = await context.LeaveTypes.Where(t => t.Id == scenario.LeaveTypeId).Select(t => t.Code).SingleAsync();
            var secondLeaveTypeCode = await context.LeaveTypes.Where(t => t.Id == scenario.SecondLeaveTypeId).Select(t => t.Code).SingleAsync();
            var unlimited = Assert.Single(rows, x => x.LeaveTypeCode == leaveTypeCode);
            Assert.Equal(EntitlementMode.Unlimited, unlimited.EntitlementMode);
            Assert.Null(unlimited.AvailableQuantity);
            var finite = Assert.Single(rows, x => x.LeaveTypeCode == secondLeaveTypeCode);
            Assert.Equal(EntitlementMode.Allocated, finite.EntitlementMode);
            Assert.Equal(5m, finite.AvailableQuantity);
            Assert.Equal(0, await context.LeaveBalanceTransactions.CountAsync(x => x.EmployeeId == scenario.EmployeeId));
        });
    }

    [Fact]
    public async Task MySql_leave_configuration_collection_predicates_execute_on_real_provider()
    {
        await RunScenarioAsync(async scenario =>
        {
            await using var context = scenario.CreateContext(new TestTenantContext(scenario.TenantId));
            var service = new LeaveConfigurationService(context, new TestTenantContext(scenario.TenantId));

            var many = await service.GetVersionLeaveTypesAsync(scenario.PolicyId, scenario.PolicyVersionId);
            Assert.True(many.Succeeded, many.Message);
            Assert.Equal(2, many.Value!.Count);

            var empty = await service.GetVersionLeaveTypesAsync(scenario.PolicyId, scenario.EmptyPolicyVersionId);
            Assert.True(empty.Succeeded, empty.Message);
            Assert.Empty(empty.Value!);
        });
    }

    [Fact]
    public async Task MySql_leave_policy_foundation_collection_predicates_execute_on_real_provider()
    {
        await RunScenarioAsync(async scenario =>
        {
            await using var context = scenario.CreateContext(new TestTenantContext(scenario.TenantId));
            var service = new LeavePolicyFoundationService(context);
            var draft = await service.CreateDraftVersionAsync(scenario.TenantId, scenario.PolicyId, new DateOnly(2028, 1, 1), null, 1, "mysql-test");
            Assert.True(draft.Succeeded, draft.Message);

            context.LeavePolicyRules.AddRange(
                new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = scenario.TenantId, LeavePolicyVersionId = draft.Value!.Id, LeaveTypeId = scenario.LeaveTypeId, IsActive = true },
                new LeavePolicyRule { Id = Guid.NewGuid(), TenantId = scenario.TenantId, LeavePolicyVersionId = draft.Value.Id, LeaveTypeId = scenario.SecondLeaveTypeId, IsActive = true });
            await context.SaveChangesAsync();

            var published = await service.PublishAsync(scenario.TenantId, draft.Value.Id, "mysql-test");
            Assert.True(published.Succeeded, published.Message);
        });
    }

    [Fact]
    public async Task MySql_leave_approval_collection_predicate_executes_on_real_provider()
    {
        await RunScenarioAsync(async scenario =>
        {
            await using var context = scenario.CreateContext(new TestTenantContext(scenario.TenantId));
            var service = new LeaveApprovalReadService(
                context,
                new FixedIdentity(scenario.TenantId, scenario.ApprovalUserId, scenario.EmployeeCId),
                new FixedManagerResolver(scenario.EmployeeCId),
                TimeProvider.System);

            var result = await service.GetInboxAsync(1, 25);
            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(2, result.Value!.TotalCount);
        }, includeApprovalData: true);
    }

    [Fact]
    public async Task MySql_authorization_collection_predicates_execute_on_real_provider()
    {
        await RunScenarioAsync(async scenario =>
        {
            await using var context = scenario.CreateContext(new TestTenantContext(scenario.TenantId, scenario.ApprovalUserId));
            var service = new AuthService(
                context,
                null!,
                null!,
                new TestTenantContext(scenario.TenantId, scenario.ApprovalUserId),
                new ShardContext(),
                Options.Create(new JwtSettings()),
                TimeProvider.System,
                NullLogger<AuthService>.Instance);

            var result = await service.GetCurrentUserAsync();
            Assert.True(result.Succeeded, result.Message);
            Assert.Contains($"mysql-approval-{scenario.TenantId:N}", result.Value!.Roles);
            Assert.Contains(Permissions.Leave.Approve, result.Value.Permissions);
        }, includeApprovalData: true);
    }

    private static async Task<DeadlockOutcome> DeadlockParticipantAsync(
        Scenario scenario,
        Guid firstEmployeeId,
        Guid secondEmployeeId,
        TaskCompletionSource<bool> firstHeld,
        TaskCompletionSource<bool> secondHeld)
    {
        await using var context = scenario.CreateContext(new TestTenantContext(scenario.TenantId));
        await using var transaction = await context.Database.BeginTransactionAsync();
        await LockEmployeeAsync(context, scenario.TenantId, firstEmployeeId);
        firstHeld.TrySetResult(true);
        await secondHeld.Task.WaitAsync(TimeSpan.FromSeconds(15));

        try
        {
            await LockEmployeeAsync(context, scenario.TenantId, secondEmployeeId);
            await transaction.CommitAsync();
            return new DeadlockOutcome(null);
        }
        catch (Exception exception)
        {
            try { await transaction.RollbackAsync(); } catch { }
            return new DeadlockOutcome(exception);
        }
    }

    private static async Task LockEmployeeAsync(HrmsDbContext context, Guid tenantId, Guid employeeId)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM `Employees` WHERE `TenantId` = @tenantId AND `Id` = @employeeId FOR UPDATE";
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();

        var tenantParameter = command.CreateParameter();
        tenantParameter.ParameterName = "@tenantId";
        tenantParameter.DbType = System.Data.DbType.Guid;
        tenantParameter.Value = tenantId;
        command.Parameters.Add(tenantParameter);

        var employeeParameter = command.CreateParameter();
        employeeParameter.ParameterName = "@employeeId";
        employeeParameter.DbType = System.Data.DbType.Guid;
        employeeParameter.Value = employeeId;
        command.Parameters.Add(employeeParameter);

        await command.ExecuteScalarAsync();
    }

    private static MySqlException? FindMySqlException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is MySqlException mysqlException)
                return mysqlException;
        return null;
    }

    private sealed record DeadlockOutcome(Exception? Exception);

    private sealed class FixedEmploymentResolver(Guid tenantId, Guid employeeId) : IEffectiveEmploymentResolver
    {
        public Task<EffectiveEmploymentResolutionResult> ResolveAsync(Guid requestedTenantId, Guid requestedEmployeeId, DateOnly effectiveDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EffectiveEmploymentResolutionResult(
                EffectiveEmploymentResolutionStatus.Resolved,
                tenantId,
                employeeId,
                effectiveDate,
                new EffectiveEmploymentSnapshot(
                    HistoryId: Guid.NewGuid(), TenantId: tenantId, EmployeeId: employeeId, EffectiveFrom: new DateOnly(2020, 1, 1), EffectiveTo: null,
                    HoldingCompanyId: null, LobId: null, OrganisationId: null, DepartmentId: null, SubDepartmentId: null, SectionId: null,
                    SubSectionId: null, FunctionId: null, SubFunctionId: null, GradeId: null, DesignationId: null, EmployeeTypeId: null,
                    CountryLocationId: null, WorkLocationId: null, CostCenterId: null, ManagerId: null, EmploymentType: EmploymentType.FullTime,
                    EmploymentStatus: EmployeeStatus.Active, DateOfJoining: new DateOnly(2020, 1, 1), GroupDateOfJoining: null,
                    DateOfLeaving: null, Gender: Gender.Unspecified),
                "resolved"));
    }

    private sealed class FixedIdentity(Guid tenantId, Guid userId, Guid employeeId) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new RuntimeEmployeeIdentity(tenantId, userId, employeeId)));
    }

    private sealed class FixedManagerResolver(Guid managerId) : IEmployeeManagerResolver
    {
        public Task<Result<EmployeeManagerResolution>> ResolveAsync(Guid employeeId, DateOnly asOfDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<EmployeeManagerResolution>.Success(new EmployeeManagerResolution(EmployeeManagerResolutionStatus.Resolved, employeeId, managerId, "MGR", "Manager", "resolved")));

        public Task<bool> WouldCreateCycleAsync(Guid employeeId, Guid proposedManagerId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private static async Task RunScenarioAsync(Func<Scenario, Task> test, bool includeApprovalData = false)
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("MySQL integration tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var scenario = new Scenario(MySqlApiFactory.NormalizeConnectionString(connection), includeApprovalData);
        await scenario.SeedAsync();
        Exception? testFailure = null;
        try
        {
            await test(scenario);
        }
        catch (Exception exception)
        {
            testFailure = exception;
        }
        finally
        {
            Exception? cleanupFailure = null;
            try
            {
                await scenario.CleanupAsync();
            }
            catch (Exception exception)
            {
                cleanupFailure = exception;
            }

            if (testFailure is not null && cleanupFailure is not null)
                throw new AggregateException("The MySQL test body and its cleanup both failed.", testFailure, cleanupFailure);

            if (testFailure is not null)
                ExceptionDispatchInfo.Capture(testFailure).Throw();

            if (cleanupFailure is not null)
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

    private sealed class Scenario(string connection, bool includeApprovalData = false)
    {
        private readonly string _connection = connection;
        private readonly bool _includeApprovalData = includeApprovalData;
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid EmployeeBId { get; } = Guid.NewGuid();
        public Guid LeaveTypeId { get; } = Guid.NewGuid();
        public Guid LeavePeriodId { get; } = Guid.NewGuid();
        public Guid PolicyId { get; } = Guid.NewGuid();
        public Guid PolicyVersionId { get; } = Guid.NewGuid();
        public Guid RuleId { get; } = Guid.NewGuid();
        public Guid SecondLeaveTypeId { get; } = Guid.NewGuid();
        public Guid SecondRuleId { get; } = Guid.NewGuid();
        public Guid MultiPolicyVersionId { get; } = Guid.NewGuid();
        public Guid MultiRuleId { get; } = Guid.NewGuid();
        public Guid EmptyPolicyVersionId { get; } = Guid.NewGuid();
        public Guid EmployeeCId { get; } = Guid.NewGuid();
        public Guid ApprovalUserId { get; } = Guid.NewGuid();
        public int ApprovalRoleId { get; } = Random.Shared.Next(100000, 2000000000);
        public int ApprovalPermissionId { get; private set; }
        public Guid ApprovalRequestId { get; } = Guid.NewGuid();
        public Guid ApprovalSecondRequestId { get; } = Guid.NewGuid();
        public Guid EmployeeHistoryId { get; } = Guid.NewGuid();
        public Guid EmployeeBHistoryId { get; } = Guid.NewGuid();
        public Guid EmployeeCHistoryId { get; } = Guid.NewGuid();
        public Guid EmployeeCodeConfigId { get; } = Guid.NewGuid();
        public Guid EmployeeCodeRuleId { get; } = Guid.NewGuid();

        public HrmsDbContext CreateContext(ITenantContext? tenant = null)
        {
            var options = new DbContextOptionsBuilder<HrmsDbContext>()
                .UseMySQL(_connection)
                .AddInterceptors(new MySqlConcurrencyTokenInterceptor(new MySqlConcurrencyTokenGenerator()))
                .Options;
            return new HrmsDbContext(options, tenant ?? new TestTenantContext(TenantId));
        }

        public async Task SeedAsync()
        {
            await using var context = CreateContext(new TestTenantContext());
            context.Tenants.Add(new Tenant
            {
                Id = TenantId, TenantCode = $"MY{TenantId:N}"[..10], Host = $"{TenantId:N}.mysql.test",
                ShardKey = $"mysql-{TenantId:N}", TenantName = "MySQL runtime test", Status = TenantStatus.Active
            });
            context.Employees.Add(new Employee
            {
                Id = EmployeeId, TenantId = TenantId, Email = $"{TenantId:N}@mysql.test",
                FirstName = "MySQL", LastName = "Runtime", DateOfJoining = new DateOnly(2020, 1, 1), Status = EmployeeStatus.Active
            });
            context.Employees.Add(new Employee
            {
                Id = EmployeeBId, TenantId = TenantId, Email = $"{TenantId:N}.second@mysql.test",
                FirstName = "MySQL", LastName = "Second", DateOfJoining = new DateOnly(2020, 1, 1), Status = EmployeeStatus.Active
            });
            context.Employees.Add(new Employee
            {
                Id = EmployeeCId, TenantId = TenantId, Email = $"{TenantId:N}.third@mysql.test",
                FirstName = "MySQL", LastName = "Manager", DateOfJoining = new DateOnly(2020, 1, 1), Status = EmployeeStatus.Active
            });
            context.EmployeeEmploymentHistory.AddRange(
                new EmployeeEmploymentHistory { Id = EmployeeHistoryId, TenantId = TenantId, EmployeeId = EmployeeId, EffectiveFrom = new DateOnly(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active },
                new EmployeeEmploymentHistory { Id = EmployeeBHistoryId, TenantId = TenantId, EmployeeId = EmployeeBId, EffectiveFrom = new DateOnly(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active },
                new EmployeeEmploymentHistory { Id = EmployeeCHistoryId, TenantId = TenantId, EmployeeId = EmployeeCId, EffectiveFrom = new DateOnly(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active });
            context.LeaveTypes.Add(new LeaveType
            {
                Id = LeaveTypeId, TenantId = TenantId, Code = $"M{TenantId:N}"[..8], Name = "MySQL test leave",
                DefaultUnit = LeaveUnit.Day, IsActive = true
            });
            context.LeaveTypes.Add(new LeaveType
            {
                Id = SecondLeaveTypeId, TenantId = TenantId, Code = $"N{TenantId:N}"[..8], Name = "MySQL second leave",
                DefaultUnit = LeaveUnit.Day, IsActive = true
            });
            context.LeavePeriods.Add(new LeavePeriod
            {
                Id = LeavePeriodId, TenantId = TenantId, Code = $"P{TenantId:N}"[..8], Name = "MySQL test period",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), IsActive = true
            });
            context.LeavePolicies.Add(new LeavePolicy
            {
                Id = PolicyId, TenantId = TenantId, Code = $"P{TenantId:N}"[..8], Name = "MySQL test policy", IsActive = true
            });
            context.LeavePolicyVersions.Add(new LeavePolicyVersion
            {
                Id = PolicyVersionId, TenantId = TenantId, LeavePolicyId = PolicyId, VersionNumber = 1,
                EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 12, 31), Status = LeavePolicyVersionStatus.Published, Priority = 1
            });
            context.LeavePolicyRules.Add(new LeavePolicyRule
            {
                Id = RuleId, TenantId = TenantId, LeavePolicyVersionId = PolicyVersionId, LeaveTypeId = LeaveTypeId, IsActive = true
            });
            context.LeavePolicyRules.Add(new LeavePolicyRule
            {
                Id = SecondRuleId, TenantId = TenantId, LeavePolicyVersionId = PolicyVersionId, LeaveTypeId = SecondLeaveTypeId, IsActive = true
            });
            context.LeavePolicyVersions.Add(new LeavePolicyVersion
            {
                Id = MultiPolicyVersionId, TenantId = TenantId, LeavePolicyId = PolicyId, VersionNumber = 2,
                EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 12, 31), Status = LeavePolicyVersionStatus.Published, Priority = 2
            });
            context.LeavePolicyRules.Add(new LeavePolicyRule
            {
                Id = MultiRuleId, TenantId = TenantId, LeavePolicyVersionId = MultiPolicyVersionId, LeaveTypeId = LeaveTypeId, IsActive = true
            });
            context.LeavePolicyVersions.Add(new LeavePolicyVersion
            {
                Id = EmptyPolicyVersionId, TenantId = TenantId, LeavePolicyId = PolicyId, VersionNumber = 3,
                EffectiveFrom = new DateOnly(2027, 1, 1), Status = LeavePolicyVersionStatus.Draft, Priority = 1
            });
            if (_includeApprovalData)
            {
                context.Users.Add(new User { Id = ApprovalUserId, TenantId = TenantId, Email = $"{TenantId:N}@approval.mysql.test", PasswordHash = "test", FirstName = "Approval", LastName = "User", IsActive = true });
                var approvePermission = await context.Permissions.SingleAsync(x => x.Name == Permissions.Leave.Approve);
                ApprovalPermissionId = approvePermission.Id;
                context.Roles.Add(new Role { Id = ApprovalRoleId, Name = $"mysql-approval-{TenantId:N}" });
                context.UserRoles.Add(new UserRole { UserId = ApprovalUserId, RoleId = ApprovalRoleId, TenantId = TenantId });
                context.RolePermissions.Add(new RolePermission { RoleId = ApprovalRoleId, PermissionId = ApprovalPermissionId });
                context.LeaveRequests.AddRange(
                    NewRequest(ApprovalRequestId, EmployeeId, EmployeeHistoryId, new DateOnly(2026, 6, 1)),
                    NewRequest(ApprovalSecondRequestId, EmployeeBId, EmployeeBHistoryId, new DateOnly(2026, 6, 2)));
            }
            context.EmployeeCodeConfigs.Add(new EmployeeCodeConfig
            {
                Id = EmployeeCodeConfigId, TenantId = TenantId, AutoGenerate = true, Prefix = "MYSQL", NextNumber = 100,
                Padding = 0, Separator = "-", EffectiveFrom = new DateOnly(2026, 1, 1)
            });
            context.EmployeeCodeRules.Add(new EmployeeCodeRule
            {
                Id = EmployeeCodeRuleId, TenantId = TenantId, EmployeeCodeConfigId = EmployeeCodeConfigId,
                Name = "MySQL runtime test rule", Priority = 1, IsDefault = true, Status = EmployeeCodeRuleStatus.Active
            });
            await context.SaveChangesAsync();
        }

        public EmployeeLeaveBalance NewBalance() => new()
        {
            Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, LeaveTypeId = LeaveTypeId,
            LeavePeriodId = LeavePeriodId, GrantedQuantity = 10, ReservedQuantity = 0, ConsumedQuantity = 0
        };

        public async Task CleanupAsync()
        {
            await using var context = CreateContext(new TestTenantContext());
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveRequestEvents` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveRequestDays` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveRequests` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `UserRoles` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `RolePermissions` WHERE `RoleId` = {ApprovalRoleId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Users` WHERE `Id` = {ApprovalUserId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Roles` WHERE `Id` = {ApprovalRoleId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `EmployeeCodeSequences` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `EmployeeCodeRules` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `EmployeeCodeConfigs` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `EmployeeLeaveBalances` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicyEntitlementRules` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicyRules` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicyVersions` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicies` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePeriods` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveTypes` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `EmployeeEmploymentHistory` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Employees` WHERE `TenantId` = {TenantId}");
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Tenants` WHERE `Id` = {TenantId}");
        }

        private LeaveRequest NewRequest(Guid id, Guid employeeId, Guid historyId, DateOnly date) => new()
        {
            Id = id, TenantId = TenantId, EmployeeId = employeeId, LeaveTypeId = LeaveTypeId, LeavePeriodId = LeavePeriodId,
            LeavePolicyVersionId = PolicyVersionId, LeavePolicyRuleId = RuleId, EmployeeEmploymentHistoryId = historyId,
            StartDate = date, EndDate = date, RequestedQuantity = 1, ChargeableQuantity = 1, Status = LeaveRequestStatus.PendingApproval,
            SubmittedAtUtc = date.ToDateTime(new TimeOnly(9, 0)), IdempotencyKey = id.ToString("N"), PayloadFingerprint = new string('a', 64)
        };
    }
}
