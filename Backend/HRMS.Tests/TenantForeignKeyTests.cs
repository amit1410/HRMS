using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// The isolation layer underneath the services. Query filters and service checks stop a cross-tenant
/// reference from being <em>requested</em>; these tests go under both and write straight to the database, so
/// they cover what would happen if a future code path forgot to check — a bug, a raw SQL statement, a bulk
/// import.
/// <para>
/// Employee references carry the tenant in the key itself: the foreign key is (TenantId, DepartmentId) onto
/// (TenantId, Id), not DepartmentId onto Id. A plain single-column key would happily accept another tenant's
/// department, because that row does exist — just not in this organization.
/// </para>
/// </summary>
public class TenantForeignKeyTests
{
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid Demo02 = SeedData.TenantIds.Demo02;

    /// <summary>
    /// Guards every other test in this file. SQLite only enforces foreign keys when the pragma is on, and if
    /// it were ever off the cross-tenant writes below would succeed — turning four passing tests into four
    /// tests that prove nothing.
    /// </summary>
    [Fact]
    public async Task Foreign_keys_are_enforced_on_the_test_connection()
    {
        using var db = new SqliteInMemoryDatabase();
        using var context = db.CreateContext(new TestTenantContext());

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        var enabled = Convert.ToInt64(await command.ExecuteScalarAsync());

        Assert.Equal(1L, enabled);
    }

    /// <summary>The positive control: the same write, kept inside one tenant, is accepted.</summary>
    [Fact]
    public async Task An_employee_referencing_its_own_tenants_rows_is_accepted()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());
        context.Employees.Add(NewEmployee(
            Demo01,
            departmentId: DepartmentId(Demo01, "ENG"),
            designationId: DesignationId(Demo01, "SE"),
            managerId: EmployeeId(Demo01, "EMP-002")));

        Assert.Equal(1, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task An_employee_cannot_reference_another_tenants_department()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        // Null tenant so the explicit TenantId below is honoured (no server stamping).
        using var context = db.CreateContext(new TestTenantContext());
        context.Employees.Add(NewEmployee(
            Demo01,
            departmentId: DepartmentId(Demo02, "OPS"), // a real department, in the wrong organization
            designationId: DesignationId(Demo01, "SE"),
            managerId: null));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task An_employee_cannot_reference_another_tenants_designation()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());
        context.Employees.Add(NewEmployee(
            Demo01,
            departmentId: DepartmentId(Demo01, "ENG"),
            designationId: DesignationId(Demo02, "OPSM"),
            managerId: null));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task An_employee_cannot_report_to_another_tenants_employee()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());
        context.Employees.Add(NewEmployee(
            Demo01,
            departmentId: DepartmentId(Demo01, "ENG"),
            designationId: DesignationId(Demo01, "SE"),
            managerId: EmployeeId(Demo02, "E-100")));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    /// A tenant cannot be pointed at either: the row would be invisible to every query filter, which is a
    /// worse outcome than an error.
    /// </summary>
    [Fact]
    public async Task An_employee_cannot_belong_to_a_tenant_that_does_not_exist()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());
        context.Employees.Add(NewEmployee(
            new Guid("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            departmentId: DepartmentId(Demo01, "ENG"),
            designationId: DesignationId(Demo01, "SE"),
            managerId: null));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    /// Deletes are restricted in the schema, not only in the service. A department that still has staff
    /// cannot be removed even by a statement that skips the service entirely, so no employee is ever left
    /// pointing at a department that is gone.
    /// </summary>
    [Fact]
    public async Task A_department_that_still_has_employees_cannot_be_deleted()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());
        var engineering = await context.Departments.IgnoreQueryFilters()
            .SingleAsync(d => d.TenantId == Demo01 && d.Code == "ENG");

        context.Departments.Remove(engineering);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task An_employee_who_still_has_direct_reports_cannot_be_deleted()
    {
        using var db = new SqliteInMemoryDatabase();
        await db.SeedAsync();

        using var context = db.CreateContext(new TestTenantContext());
        var manager = await context.Employees.IgnoreQueryFilters()
            .SingleAsync(e => e.Id == EmployeeId(Demo01, "EMP-002"));

        context.Employees.Remove(manager);

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static Employee NewEmployee(
        Guid tenantId, Guid departmentId, Guid designationId, Guid? managerId) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeCode = "EMP-900",
            FirstName = "Direct",
            LastName = "Insert",
            Email = "direct.insert@example.com",
            DateOfJoining = new DateOnly(2024, 1, 1),
            Status = EmployeeStatus.Active,
            DepartmentId = departmentId,
            DesignationId = designationId,
            ReportingManagerId = managerId
        };

    private static Guid DepartmentId(Guid tenantId, string code) =>
        OrganizationTestHarness.DepartmentId(tenantId, code);

    private static Guid DesignationId(Guid tenantId, string code) =>
        OrganizationTestHarness.DesignationId(tenantId, code);

    private static Guid EmployeeId(Guid tenantId, string employeeCode) =>
        OrganizationTestHarness.EmployeeId(tenantId, employeeCode);
}
