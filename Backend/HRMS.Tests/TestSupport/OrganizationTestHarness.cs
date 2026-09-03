using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Application.EmployeeCodes;
using HRMS.Application.Validators.Employees;
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
        new DepartmentService(TrackContext(), TenantContext, NullLogger<DepartmentService>.Instance, Clock);

    public IDesignationService Designations() =>
        new DesignationService(TrackContext(), TenantContext, NullLogger<DesignationService>.Instance);

    public IEmployeeService Employees() =>
        new EmployeeService(TrackContext(), TenantContext, Clock, NullLogger<EmployeeService>.Instance);

    public IEmployeeContactService Contacts() =>
        new EmployeeContactService(TrackContext(), TenantContext, NullLogger<EmployeeContactService>.Instance);

    public IEmployeeBankDetailService BankDetails() =>
        new EmployeeBankDetailService(TrackContext(), TenantContext, NullLogger<EmployeeBankDetailService>.Instance);

    public IEmployeeEmploymentService Employment()
    {
        var context = TrackContext();
        return new EmployeeEmploymentService(
            context,
            TenantContext,
            NullLogger<EmployeeEmploymentService>.Instance,
            new EmployeeCodeRuleMatcher(),
            new EmployeeCodeRenderer(),
            new EmployeeCodeSequenceService(context, TenantContext),
            Clock);
    }

    public IEmployeeCodeConfigurationService CodeConfiguration() =>
        new EmployeeCodeConfigurationService(
            TrackContext(), TenantContext, new EmployeeCodeConfigurationRequestValidator());

    public IEmployeeCodeSequenceService EmployeeCodeSequences() =>
        new EmployeeCodeSequenceService(TrackContext(), TenantContext);

    public IEmployeeSupervisorService Supervisors() =>
        new EmployeeSupervisorService(TrackContext(), TenantContext, NullLogger<EmployeeSupervisorService>.Instance, Clock);

    public IEmployeeManagerResolver Managers() =>
        new EmployeeManagerResolver(TrackContext(), TenantContext, Clock);

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

    /// <summary>Id of a seeded bank, looked up by tenant and code.</summary>
    public static Guid BankId(Guid tenantId, string code) =>
        SeedData.Banks.Single(b => b.TenantId == tenantId && b.Code == code).Id;

    /// <summary>Id of a seeded holding company, looked up by tenant and code.</summary>
    public static Guid HoldingCompanyId(Guid tenantId, string code) =>
        SeedData.HoldingCompanies.Single(x => x.TenantId == tenantId && x.Code == code).Id;

    /// <summary>Id of a seeded line of business, looked up by tenant and code.</summary>
    public static Guid LobId(Guid tenantId, string code) =>
        SeedData.LinesOfBusiness.Single(x => x.TenantId == tenantId && x.Code == code).Id;

    /// <summary>Id of a seeded organisation, looked up by tenant and code.</summary>
    public static Guid OrganisationId(Guid tenantId, string code) =>
        SeedData.Organisations.Single(x => x.TenantId == tenantId && x.Code == code).Id;

    /// <summary>Id of a seeded sub-department, looked up by tenant and code.</summary>
    public static Guid SubDepartmentId(Guid tenantId, string code) =>
        SeedData.SubDepartments.Single(x => x.TenantId == tenantId && x.Code == code).Id;

    /// <summary>Id of a seeded section, looked up by tenant and code.</summary>
    public static Guid SectionId(Guid tenantId, string code) =>
        SeedData.Sections.Single(x => x.TenantId == tenantId && x.Code == code).Id;

    /// <summary>Id of a seeded sub-section, looked up by tenant and code.</summary>
    public static Guid SubSectionId(Guid tenantId, string code) =>
        SeedData.SubSections.Single(x => x.TenantId == tenantId && x.Code == code).Id;

    /// <summary>Id of a seeded function, looked up by tenant and code.</summary>
    public static Guid FunctionId(Guid tenantId, string code) =>
        SeedData.Functions.Single(x => x.TenantId == tenantId && x.Code == code).Id;

    /// <summary>Id of a seeded sub-function, looked up by tenant and code.</summary>
    public static Guid SubFunctionId(Guid tenantId, string code) =>
        SeedData.SubFunctions.Single(x => x.TenantId == tenantId && x.Code == code).Id;

    /// <summary>Id of a seeded grade, looked up by tenant and code.</summary>
    public static Guid GradeId(Guid tenantId, string code) =>
        SeedData.Grades.Single(x => x.TenantId == tenantId && x.Code == code).Id;

    /// <summary>Id of a seeded employee type, looked up by tenant and code.</summary>
    public static Guid EmployeeTypeId(Guid tenantId, string code) =>
        SeedData.EmployeeTypes.Single(x => x.TenantId == tenantId && x.Code == code).Id;

    /// <summary>Id of a seeded work location, looked up by tenant and code.</summary>
    public static Guid WorkLocationId(Guid tenantId, string code) =>
        SeedData.WorkLocations.Single(x => x.TenantId == tenantId && x.Code == code).Id;

    /// <summary>Id of a seeded cost center, looked up by tenant and code.</summary>
    public static Guid CostCenterId(Guid tenantId, string code) =>
        SeedData.CostCenters.Single(x => x.TenantId == tenantId && x.Code == code).Id;

    /// <summary>Id of a seeded position change reason, looked up by tenant and code.</summary>
    public static Guid PositionChangeReasonId(Guid tenantId, string code) =>
        SeedData.PositionChangeReasons.Single(x => x.TenantId == tenantId && x.Code == code).Id;

    /// <summary>Id of a seeded country, looked up by code.</summary>
    public static Guid CountryId(string code) =>
        SeedData.Countries.Single(c => c.Code == code).Id;

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
