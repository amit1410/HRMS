using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// Wires the real department, designation and employee services against the shared in-memory SQLite
/// database. Only the ambient tenant and the clock are substitutable.
/// <para>
/// Every accessor hands back a service on a <em>fresh</em> DbContext, matching the scoped lifetime a
/// request gets. That is not just tidiness: a service whose save is rejected leaves the failed entity in
/// the change tracker, and reusing one context across calls would let one test's rejected write leak into
/// the next assertion.
/// </para>
/// </summary>
public sealed class OrganizationTestHarness : IDisposable
{
    private readonly List<HrmsDbContext> _contexts = [];

    private OrganizationTestHarness(SqliteInMemoryDatabase database)
    {
        Database = database;
        TenantContext = new TestTenantContext();
        Clock = new FixedClock(new DateTimeOffset(2026, 3, 4, 9, 7, 8, TimeSpan.Zero));
    }

    public SqliteInMemoryDatabase Database { get; }

    /// <summary>The ambient tenant. Null represents a request with no authenticated tenant.</summary>
    public TestTenantContext TenantContext { get; }

    /// <summary>The clock the services read, stopped at a known instant.</summary>
    public FixedClock Clock { get; }

    public static async Task<OrganizationTestHarness> CreateAsync()
    {
        var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();

        var harness = new OrganizationTestHarness(database);
        harness.ActAs(SeedData.TenantIds.Demo01);
        return harness;
    }

    /// <summary>Switches the ambient tenant, the way a request from another organization would.</summary>
    public OrganizationTestHarness ActAs(Guid? tenantId)
    {
        TenantContext.TenantId = tenantId;
        return this;
    }

    public IDepartmentService Departments() =>
        new DepartmentService(TrackContext(), TenantContext, NullLogger<DepartmentService>.Instance);

    public IDesignationService Designations() =>
        new DesignationService(TrackContext(), TenantContext, NullLogger<DesignationService>.Instance);

    public IEmployeeService Employees() =>
        new EmployeeService(TrackContext(), TenantContext, Clock, NullLogger<EmployeeService>.Instance);

    /// <summary>A context scoped to the current ambient tenant, for arranging or asserting directly.</summary>
    public HrmsDbContext CreateContext() => TrackContext();

    /// <summary>A context that sees every tenant's rows, for asserting what was really persisted.</summary>
    public HrmsDbContext CreateUnscopedContext()
    {
        var context = Database.CreateContext(new TestTenantContext());
        _contexts.Add(context);
        return context;
    }

    /// <summary>Id of a seeded department, looked up by tenant and code.</summary>
    public static Guid DepartmentId(Guid tenantId, string code) =>
        SeedData.Departments.Single(d => d.TenantId == tenantId && d.Code == code).Id;

    /// <summary>Id of a seeded designation, looked up by tenant and code.</summary>
    public static Guid DesignationId(Guid tenantId, string code) =>
        SeedData.Designations.Single(d => d.TenantId == tenantId && d.Code == code).Id;

    /// <summary>Id of a seeded employee, looked up by tenant and employee code.</summary>
    public static Guid EmployeeId(Guid tenantId, string employeeCode) =>
        SeedData.Employees.Single(e => e.TenantId == tenantId && e.EmployeeCode == employeeCode).Id;

    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Dispose();
        }

        Database.Dispose();
    }

    private HrmsDbContext TrackContext()
    {
        var context = Database.CreateContext(TenantContext);
        _contexts.Add(context);
        return context;
    }
}
