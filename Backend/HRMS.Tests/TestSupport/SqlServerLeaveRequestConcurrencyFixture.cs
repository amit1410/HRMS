using System.Data;
using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests.TestSupport;

/// <summary>Owns one disposable, single-database SQL Server run for leave-request concurrency tests.</summary>
public sealed class SqlServerLeaveRequestConcurrencyFixture : IAsyncLifetime
{
    public const string ServerEnvironmentVariable = SqlServerAcceptanceRun.ServerEnvironmentVariable;
    public const string AuthEnvironmentVariable = SqlServerAcceptanceRun.AuthEnvironmentVariable;
    private const string Prefix = "HRMS_LeaveRequestConcurrency_";
    private static readonly string[] ProtectedNames = ["master", "model", "msdb", "tempdb", "HRMS", "HRMS_Catalog"];

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ServerEnvironmentVariable)) &&
        string.Equals(Environment.GetEnvironmentVariable(AuthEnvironmentVariable), "Integrated", StringComparison.OrdinalIgnoreCase);

    public string DatabaseName { get; private set; } = string.Empty;
    public string Server { get; private set; } = string.Empty;
    public Guid TenantA { get; } = Guid.Parse("a4000000-0000-0000-0000-000000000001");
    public Guid TenantB { get; } = Guid.Parse("b4000000-0000-0000-0000-000000000001");
    public Guid EmployeeA { get; } = Guid.Parse("a4000000-0000-0000-0000-000000000101");
    public Guid EmployeeB { get; } = Guid.Parse("a4000000-0000-0000-0000-000000000102");
    public Guid EmployeeC { get; } = Guid.Parse("b4000000-0000-0000-0000-000000000101");
    public Guid UserA { get; } = Guid.Parse("a4000000-0000-0000-0000-000000000201");
    public Guid UserB { get; } = Guid.Parse("a4000000-0000-0000-0000-000000000202");
    public Guid UserC { get; } = Guid.Parse("b4000000-0000-0000-0000-000000000201");
    public Guid LeaveTypeId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public Guid LeavePeriodId { get; } = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public Guid PolicyVersionId { get; } = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public Guid PolicyRuleId { get; } = Guid.Parse("40000000-0000-0000-0000-000000000001");
    public Guid PolicyId { get; } = Guid.Parse("50000000-0000-0000-0000-000000000001");
    public Guid LeaveTypeB { get; } = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public Guid LeavePeriodB { get; } = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public Guid PolicyVersionB { get; } = Guid.Parse("30000000-0000-0000-0000-000000000002");
    public Guid PolicyRuleB { get; } = Guid.Parse("40000000-0000-0000-0000-000000000002");
    public Guid PolicyB { get; } = Guid.Parse("50000000-0000-0000-0000-000000000002");
    public Guid EmploymentA { get; } = Guid.Parse("60000000-0000-0000-0000-000000000101");
    public Guid EmploymentB { get; } = Guid.Parse("60000000-0000-0000-0000-000000000102");
    public Guid EmploymentC { get; } = Guid.Parse("60000000-0000-0000-0000-000000000201");

    public async Task InitializeAsync()
    {
        if (!IsConfigured) return;
        Server = Environment.GetEnvironmentVariable(ServerEnvironmentVariable)!.Trim();
        SqlServerAcceptanceRun.ValidateServer(Server);
        if (!string.Equals(Environment.GetEnvironmentVariable(AuthEnvironmentVariable), "Integrated", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{AuthEnvironmentVariable} must be 'Integrated'. Password authentication is not supported.");
        DatabaseName = Prefix + DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'") + "_" + Random.Shared.Next(100000, 999999);
        ValidateOwnedName(DatabaseName);
        await CreateDatabaseAsync();
        await using (var db = CreateContext()) await db.Database.MigrateAsync();
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (!IsConfigured || string.IsNullOrEmpty(DatabaseName)) return;
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(MasterConnection().ConnectionString);
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = $"DROP DATABASE [{DatabaseName.Replace("]", "]]", StringComparison.Ordinal)}]";
        await command.ExecuteNonQueryAsync();
    }

    public HrmsDbContext CreateContext(Guid? tenantId = null, Guid? userId = null) =>
        new(new DbContextOptionsBuilder<HrmsDbContext>().UseSqlServer(Connection().ConnectionString, sql => sql.CommandTimeout(10)).Options,
            new TestTenantContext(tenantId, userId));

    public async Task<Guid> SeedRequestAsync(
        Guid tenantId,
        Guid employeeId,
        Guid actorUserId,
        Guid employmentHistoryId,
        Guid leaveTypeId,
        Guid leavePeriodId,
        Guid policyVersionId,
        Guid policyRuleId,
        DateOnly date,
        LeaveRequestStatus status,
        string idempotencyKey)
    {
        var requestId = Guid.NewGuid();
        await using var db = CreateContext(tenantId, actorUserId);
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = requestId, TenantId = tenantId, EmployeeId = employeeId, LeaveTypeId = leaveTypeId,
            LeavePeriodId = leavePeriodId, LeavePolicyVersionId = policyVersionId, LeavePolicyRuleId = policyRuleId,
            EmployeeEmploymentHistoryId = employmentHistoryId, PolicyGenderSnapshot = Gender.Unspecified,
            StartDate = date, EndDate = date, RequestedQuantity = 1, ChargeableQuantity = 1, Status = status,
            SubmittedAtUtc = DateTime.UtcNow, IdempotencyKey = idempotencyKey, PayloadFingerprint = new string('s', 64),
            Days = [new LeaveRequestDay
            {
                Id = Guid.NewGuid(), TenantId = tenantId, Date = date, RequestedQuantity = 1,
                ChargeableQuantity = 1, IsEmployeeRequested = true
            }],
            Events = [new LeaveRequestEvent
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EventType = LeaveRequestEventType.Submitted,
                OccurredAtUtc = DateTime.UtcNow, ActorType = LeaveBalanceActorType.User,
                ActorUserId = actorUserId, ActorEmployeeId = employeeId
            }]
        });
        await db.SaveChangesAsync();
        return requestId;
    }

    private SqlConnectionStringBuilder Connection() => new()
    {
        DataSource = Server, InitialCatalog = DatabaseName, IntegratedSecurity = true,
        Encrypt = true, TrustServerCertificate = true, ConnectTimeout = 10, CommandTimeout = 30,
        ApplicationName = "HRMS Leave Request Concurrency Tests"
    };

    private SqlConnectionStringBuilder MasterConnection() => new(Connection().ConnectionString) { InitialCatalog = "master" };

    private async Task CreateDatabaseAsync()
    {
        await using var master = new SqlConnection(MasterConnection().ConnectionString);
        await master.OpenAsync();
        await using var command = master.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{DatabaseName.Replace("]", "]]", StringComparison.Ordinal)}]";
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedAsync()
    {
        await using var db = CreateContext();
        db.Tenants.AddRange(
            new Tenant { Id = TenantA, TenantCode = "LRA", Host = "leave-a.test", ShardKey = "leave-a", TenantName = "Leave A", Status = TenantStatus.Active },
            new Tenant { Id = TenantB, TenantCode = "LRB", Host = "leave-b.test", ShardKey = "leave-b", TenantName = "Leave B", Status = TenantStatus.Active });
        db.Users.AddRange(User(TenantA, UserA, "a"), User(TenantA, UserB, "b"), User(TenantB, UserC, "c"));
        db.Employees.AddRange(Employee(TenantA, EmployeeA, "a"), Employee(TenantA, EmployeeB, "b"), Employee(TenantB, EmployeeC, "c"));
        db.EmployeeEmploymentHistory.AddRange(History(TenantA, EmployeeA, EmploymentA), History(TenantA, EmployeeB, EmploymentB), History(TenantB, EmployeeC, EmploymentC));
        db.LeaveTypes.AddRange(new LeaveType { Id = LeaveTypeId, TenantId = TenantA, Code = "ANNUAL", Name = "Annual", DefaultUnit = LeaveUnit.Day, IsActive = true }, new LeaveType { Id = LeaveTypeB, TenantId = TenantB, Code = "ANNUAL", Name = "Annual", DefaultUnit = LeaveUnit.Day, IsActive = true });
        db.LeavePeriods.AddRange(Period(TenantA, LeavePeriodId), Period(TenantB, LeavePeriodB));
        db.LeavePolicies.AddRange(new LeavePolicy { Id = PolicyId, TenantId = TenantA, Code = "DEFAULT", Name = "Default", IsActive = true }, new LeavePolicy { Id = PolicyB, TenantId = TenantB, Code = "DEFAULT", Name = "Default", IsActive = true });
        db.LeavePolicyVersions.AddRange(Version(TenantA, PolicyId, PolicyVersionId), Version(TenantB, PolicyB, PolicyVersionB));
        db.LeavePolicyRules.AddRange(Rule(TenantA, PolicyVersionId, LeaveTypeId, PolicyRuleId), Rule(TenantB, PolicyVersionB, LeaveTypeB, PolicyRuleB));
        db.LeavePolicyEntitlementRules.AddRange(
            new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = TenantA, LeavePolicyRuleId = PolicyRuleId, EntitlementMode = EntitlementMode.Unlimited },
            new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = TenantB, LeavePolicyRuleId = PolicyRuleB, EntitlementMode = EntitlementMode.Unlimited });
        db.LeavePolicyCancellationRules.AddRange(
            new LeavePolicyCancellationRule { Id = Guid.NewGuid(), TenantId = TenantA, LeavePolicyRuleId = PolicyRuleId, CancelAllowed = true },
            new LeavePolicyCancellationRule { Id = Guid.NewGuid(), TenantId = TenantB, LeavePolicyRuleId = PolicyRuleB, CancelAllowed = true });
        await db.SaveChangesAsync();
    }

    private static User User(Guid tenant, Guid id, string suffix) => new() { Id = id, TenantId = tenant, Email = $"{suffix}@leave.test", FirstName = "Leave", LastName = suffix, PasswordHash = "synthetic" };
    private static Employee Employee(Guid tenant, Guid id, string suffix) => new() { Id = id, TenantId = tenant, Email = $"employee-{suffix}@leave.test", FirstName = "Leave", LastName = suffix, DateOfJoining = new DateOnly(2020, 1, 1), Status = EmployeeStatus.Active };
    private static EmployeeEmploymentHistory History(Guid tenant, Guid employee, Guid id) => new() { Id = id, TenantId = tenant, EmployeeId = employee, EffectiveFrom = new DateOnly(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active };
    private static LeavePeriod Period(Guid tenant, Guid id) => new() { Id = id, TenantId = tenant, Code = "2026", Name = "2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), IsActive = true };
    private static LeavePolicyVersion Version(Guid tenant, Guid policy, Guid id) => new() { Id = id, TenantId = tenant, LeavePolicyId = policy, VersionNumber = 1, EffectiveFrom = new DateOnly(2026, 1, 1), Status = LeavePolicyVersionStatus.Published, Priority = 1 };
    private static LeavePolicyRule Rule(Guid tenant, Guid version, Guid type, Guid id) => new() { Id = id, TenantId = tenant, LeavePolicyVersionId = version, LeaveTypeId = type, IsActive = true };

    private static void ValidateOwnedName(string database)
    {
        if (ProtectedNames.Contains(database, StringComparer.OrdinalIgnoreCase) || !database.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing a non-owned SQL Server test database.");
    }
}

public sealed class SqlServerLeaveRequestConcurrencyFactAttribute : FactAttribute
{
    public SqlServerLeaveRequestConcurrencyFactAttribute() => Skip = SqlServerLeaveRequestConcurrencyFixture.IsConfigured ? null : $"SQL Server leave-request tests not executed: {SqlServerLeaveRequestConcurrencyFixture.ServerEnvironmentVariable} is absent or {SqlServerLeaveRequestConcurrencyFixture.AuthEnvironmentVariable} is not Integrated.";
}

public sealed class SqlServerLeaveRequestConcurrencyTheoryAttribute : TheoryAttribute
{
    public SqlServerLeaveRequestConcurrencyTheoryAttribute() => Skip = SqlServerLeaveRequestConcurrencyFixture.IsConfigured ? null : $"SQL Server leave-request tests not executed: {SqlServerLeaveRequestConcurrencyFixture.ServerEnvironmentVariable} is absent or {SqlServerLeaveRequestConcurrencyFixture.AuthEnvironmentVariable} is not Integrated.";
}
