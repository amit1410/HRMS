using HRMS.Application.Common;
using HRMS.Application.DTOs.Departments;
using HRMS.Application.DTOs.Designations;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// Sorting and paging for all three list endpoints.
/// <para>
/// Each endpoint publishes the fields it can be ordered by (<c>SortFields</c>), and both the validator and
/// the service read that list — so a field could be advertised without being implemented, and the only
/// symptom would be a list that quietly comes back in the default order. These tests iterate the published
/// list itself: a new entry with no branch in <c>ApplySort</c> falls back to code order and fails here, and
/// a new entry with no key extractor in this file throws by name.
/// </para>
/// <para>
/// The fixture is a third organization built for the purpose, arranged so that ordering by code — the
/// fallback — is <em>wrong</em> for every other field. Without that, a missing sort branch would still
/// produce a plausibly ordered page and the test would pass.
/// </para>
/// </summary>
public class OrganizationSortingTests
{
    [Fact]
    public async Task Every_declared_department_sort_field_actually_orders_the_list()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var fixture = await SortFixture.CreateAsync(harness);
        var ids = fixture.Departments.Select(d => d.Id).ToList();

        foreach (var field in DepartmentQuery.SortFields)
        {
            var key = DepartmentKey(field, fixture.DepartmentsById);

            foreach (var descending in new[] { false, true })
            {
                var page = await harness.Departments().GetAsync(new DepartmentQuery
                {
                    PageSize = PagedQuery.MaxPageSize,
                    SortBy = field,
                    SortDescending = descending
                });

                AssertOrdered("Departments", field, descending, Ids(page.Value!.Items, d => d.Id), ids, key);
            }
        }
    }

    [Fact]
    public async Task Every_declared_designation_sort_field_actually_orders_the_list()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var fixture = await SortFixture.CreateAsync(harness);
        var ids = fixture.Designations.Select(d => d.Id).ToList();

        foreach (var field in DesignationQuery.SortFields)
        {
            var key = DesignationKey(field, fixture.DesignationsById);

            foreach (var descending in new[] { false, true })
            {
                var page = await harness.Designations().GetAsync(new DesignationQuery
                {
                    PageSize = PagedQuery.MaxPageSize,
                    SortBy = field,
                    SortDescending = descending
                });

                AssertOrdered("Designations", field, descending, Ids(page.Value!.Items, d => d.Id), ids, key);
            }
        }
    }

    [Fact]
    public async Task Every_declared_employee_sort_field_actually_orders_the_list()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var fixture = await SortFixture.CreateAsync(harness);
        var ids = fixture.Employees.Select(e => e.Id).ToList();

        foreach (var field in EmployeeQuery.SortFields)
        {
            var key = EmployeeKey(field, fixture.EmployeesById);

            foreach (var descending in new[] { false, true })
            {
                var page = await harness.Employees().GetAsync(new EmployeeQuery
                {
                    PageSize = PagedQuery.MaxPageSize,
                    SortBy = field,
                    SortDescending = descending
                });

                AssertOrdered("Employees", field, descending, Ids(page.Value!.Items, e => e.Id), ids, key);
            }
        }
    }

    /// <summary>
    /// Paging over a sort field with duplicate values. Without the id as a final tiebreaker the database is
    /// free to order tied rows differently between the two queries, and a row would be repeated on one page
    /// and missing from another — the classic silent pagination bug.
    /// </summary>
    [Fact]
    public async Task Paging_a_tie_heavy_sort_neither_repeats_nor_skips_a_row()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var fixture = await SortFixture.CreateAsync(harness);

        var seen = new List<Guid>();
        for (var pageNumber = 1; pageNumber <= 3; pageNumber++)
        {
            var page = await harness.Employees().GetAsync(new EmployeeQuery
            {
                Page = pageNumber,
                PageSize = 2,
                SortBy = "status" // three distinct values across six employees
            });

            Assert.Equal(6, page.Value!.TotalCount);
            Assert.Equal(3, page.Value.TotalPages);
            Assert.Equal(2, page.Value.Items.Count);
            seen.AddRange(page.Value.Items.Select(e => e.Id));
        }

        Assert.Equal(6, seen.Distinct().Count());
        Assert.Empty(fixture.Employees.Select(e => e.Id).Except(seen));
    }

    [Fact]
    public async Task Paging_reports_whether_neighbouring_pages_exist()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        await SortFixture.CreateAsync(harness);

        var first = await harness.Employees().GetAsync(new EmployeeQuery { Page = 1, PageSize = 4 });
        var last = await harness.Employees().GetAsync(new EmployeeQuery { Page = 2, PageSize = 4 });

        Assert.False(first.Value!.HasPreviousPage);
        Assert.True(first.Value.HasNextPage);
        Assert.Equal(2, last.Value!.Items.Count);
        Assert.True(last.Value.HasPreviousPage);
        Assert.False(last.Value.HasNextPage);
    }

    /// <summary>
    /// Out-of-range paging is clamped rather than trusted. Validators reject these values on the HTTP path,
    /// but the query itself must not depend on that: a negative skip throws and an unbounded take would
    /// hand back the whole table.
    /// </summary>
    [Fact]
    public async Task Page_and_page_size_are_clamped_rather_than_trusted()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        await SortFixture.CreateAsync(harness);

        var negativePage = await harness.Employees().GetAsync(new EmployeeQuery { Page = -5, PageSize = 2 });
        var zeroSize = await harness.Employees().GetAsync(new EmployeeQuery { PageSize = 0 });
        var hugeSize = await harness.Employees().GetAsync(new EmployeeQuery { PageSize = 5_000 });
        var pastTheEnd = await harness.Employees().GetAsync(new EmployeeQuery { Page = 99, PageSize = 2 });

        Assert.Equal(1, negativePage.Value!.Page);
        Assert.Equal(2, negativePage.Value.Items.Count);

        Assert.Equal(1, zeroSize.Value!.PageSize);
        Assert.Single(zeroSize.Value.Items);

        Assert.Equal(PagedQuery.MaxPageSize, hugeSize.Value!.PageSize);
        Assert.Equal(6, hugeSize.Value.Items.Count);

        // Past the last page: an empty page, but the totals still describe the full result set.
        Assert.Empty(pastTheEnd.Value!.Items);
        Assert.Equal(6, pastTheEnd.Value.TotalCount);
    }

    /// <summary>
    /// An unrecognized sort field is ignored, not interpolated. The HTTP layer rejects it outright (see the
    /// endpoint tests); this covers the service in isolation, where the value must still never reach an
    /// ORDER BY clause.
    /// </summary>
    [Fact]
    public async Task An_unrecognized_sort_field_falls_back_to_the_default_order()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var fixture = await SortFixture.CreateAsync(harness);

        var injected = await harness.Employees().GetAsync(new EmployeeQuery
        {
            PageSize = PagedQuery.MaxPageSize,
            SortBy = "Salary; DROP TABLE Employees; --"
        });

        Assert.True(injected.Succeeded);
        AssertOrdered(
            "Employees",
            "employeeCode (fallback)",
            descending: false,
            Ids(injected.Value!.Items, e => e.Id),
            fixture.Employees.Select(e => e.Id).ToList(),
            EmployeeKey("employeeCode", fixture.EmployeesById));

        // And the table it tried to drop is still there.
        using var unscoped = harness.CreateUnscopedContext();
        Assert.Equal(14, await unscoped.Employees.IgnoreQueryFilters().CountAsync());
    }

    private static List<Guid> Ids<T>(IReadOnlyList<T> items, Func<T, Guid> id) => items.Select(id).ToList();

    /// <summary>
    /// Asserts the returned page contains every fixture row exactly once and that the sort key never moves
    /// the wrong way. Monotonicity rather than an exact expected sequence: fields with duplicate values
    /// (status, department) have no single correct order, and asserting one would bake in whatever the
    /// database happens to do with the id tiebreaker.
    /// </summary>
    private static void AssertOrdered(
        string entity,
        string field,
        bool descending,
        IReadOnlyList<Guid> actual,
        IReadOnlyList<Guid> expected,
        Func<Guid, string> key)
    {
        var direction = descending ? "descending" : "ascending";

        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Count, actual.Distinct().Count());
        Assert.Empty(expected.Except(actual));

        for (var i = 1; i < actual.Count; i++)
        {
            var previous = key(actual[i - 1]);
            var current = key(actual[i]);
            var comparison = string.CompareOrdinal(previous, current);

            Assert.True(
                descending ? comparison >= 0 : comparison <= 0,
                $"{entity} sorted by '{field}' {direction} is out of order at position {i}: " +
                $"'{previous}' came before '{current}'.");
        }
    }

    private static Func<Guid, string> DepartmentKey(
        string field, IReadOnlyDictionary<Guid, DepartmentRow> rows) => field.ToLowerInvariant() switch
        {
            "code" => id => rows[id].Code,
            "name" => id => rows[id].Name,
            "employeecount" => id => Key(rows[id].EmployeeCount),
            "isactive" => id => Key(rows[id].IsActive),
            "createddate" => id => Key(rows[id].CreatedDate),
            _ => throw new InvalidOperationException(
                $"DepartmentQuery.SortFields declares '{field}', but this test has no key for it. " +
                "Add one here (and a branch in DepartmentService.ApplySort) or stop advertising the field.")
        };

    private static Func<Guid, string> DesignationKey(
        string field, IReadOnlyDictionary<Guid, DesignationRow> rows) => field.ToLowerInvariant() switch
        {
            "code" => id => rows[id].Code,
            "name" => id => rows[id].Name,
            "employeecount" => id => Key(rows[id].EmployeeCount),
            "isactive" => id => Key(rows[id].IsActive),
            "createddate" => id => Key(rows[id].CreatedDate),
            _ => throw new InvalidOperationException(
                $"DesignationQuery.SortFields declares '{field}', but this test has no key for it. " +
                "Add one here (and a branch in DesignationService.ApplySort) or stop advertising the field.")
        };

    private static Func<Guid, string> EmployeeKey(
        string field, IReadOnlyDictionary<Guid, EmployeeRow> rows) => field.ToLowerInvariant() switch
        {
            "employeecode" => id => rows[id].Code,
            "firstname" => id => rows[id].FirstName,
            "lastname" => id => rows[id].LastName,
            "email" => id => rows[id].Email,
            "department" => id => rows[id].DepartmentName,
            "designation" => id => rows[id].DesignationName,
            "status" => id => Key(rows[id].Status),
            "dateofjoining" => id => Key(rows[id].DateOfJoining),
            "createddate" => id => Key(rows[id].CreatedDate),
            _ => throw new InvalidOperationException(
                $"EmployeeQuery.SortFields declares '{field}', but this test has no key for it. " +
                "Add one here (and a branch in EmployeeService.ApplySort) or stop advertising the field.")
        };

    // Keys are compared as strings so one comparer covers every field type; each conversion has to preserve
    // ordering, which is why numbers are zero-padded and dates use a sortable format.
    private static string Key(int value) => value.ToString("D9");
    private static string Key(bool value) => value ? "1" : "0";
    private static string Key(EmployeeStatus value) => ((int)value).ToString("D9");
    private static string Key(DateOnly value) => value.ToString("yyyy-MM-dd");
    private static string Key(DateTime value) => value.ToString("O");

    private sealed record DepartmentRow(
        Guid Id, string Code, string Name, bool IsActive, int EmployeeCount, DateTime CreatedDate);

    private sealed record DesignationRow(
        Guid Id, string Code, string Name, bool IsActive, int EmployeeCount, DateTime CreatedDate);

    private sealed record EmployeeRow(
        Guid Id,
        string Code,
        string FirstName,
        string LastName,
        string Email,
        string DepartmentName,
        string DesignationName,
        EmployeeStatus Status,
        DateOnly DateOfJoining,
        DateTime CreatedDate);

    /// <summary>
    /// A third organization whose rows exist only to be sorted. Written straight through a tenant-less
    /// context so that the created dates — which the audit stamp would otherwise fill in with a single
    /// timestamp — are distinct and known.
    /// <para>
    /// The arrangement is the point: codes run <em>opposite</em> to name, employee count, created date and
    /// every employee field, and alternate against the active flag. Ordering by code (what the services fall
    /// back to when a sort field has no branch) therefore violates every other field's ordering.
    /// </para>
    /// </summary>
    private sealed class SortFixture
    {
        private static readonly Guid TenantId = new("33333333-3333-3333-3333-333333333333");

        private SortFixture(
            IReadOnlyList<DepartmentRow> departments,
            IReadOnlyList<DesignationRow> designations,
            IReadOnlyList<EmployeeRow> employees)
        {
            Departments = departments;
            Designations = designations;
            Employees = employees;
            DepartmentsById = departments.ToDictionary(d => d.Id);
            DesignationsById = designations.ToDictionary(d => d.Id);
            EmployeesById = employees.ToDictionary(e => e.Id);
        }

        public IReadOnlyList<DepartmentRow> Departments { get; }
        public IReadOnlyList<DesignationRow> Designations { get; }
        public IReadOnlyList<EmployeeRow> Employees { get; }
        public IReadOnlyDictionary<Guid, DepartmentRow> DepartmentsById { get; }
        public IReadOnlyDictionary<Guid, DesignationRow> DesignationsById { get; }
        public IReadOnlyDictionary<Guid, EmployeeRow> EmployeesById { get; }

        /// <summary>Writes the fixture and leaves the harness acting as its tenant.</summary>
        public static async Task<SortFixture> CreateAsync(OrganizationTestHarness harness)
        {
            // Names ascend with the index; codes descend with it.
            var departments = new[]
            {
                Department(1, "D-4", "Alpha", isActive: true, day: 1),
                Department(2, "D-3", "Bravo", isActive: false, day: 2),
                Department(3, "D-2", "Cedar", isActive: true, day: 3),
                Department(4, "D-1", "Delta", isActive: false, day: 4)
            };

            var designations = new[]
            {
                Designation(1, "G-6", "Analyst", isActive: true, day: 11),
                Designation(2, "G-5", "Buyer", isActive: false, day: 12),
                Designation(3, "G-4", "Chef", isActive: true, day: 13),
                Designation(4, "G-3", "Driver", isActive: false, day: 14),
                Designation(5, "G-2", "Editor", isActive: true, day: 15),
                Designation(6, "G-1", "Foreman", isActive: false, day: 16)
            };

            // Department and designation are assigned unevenly on purpose: it gives the two employee-count
            // sorts something other than a column of ones to order, and leaves three job titles unheld.
            var employees = new[]
            {
                Employee(1, "E-60", "Ana", "Alder", "Bravo", "Analyst", EmployeeStatus.Active, 2018, 1),
                Employee(2, "E-50", "Bea", "Brook", "Cedar", "Buyer", EmployeeStatus.Active, 2019, 2),
                Employee(3, "E-40", "Cyd", "Chase", "Cedar", "Buyer", EmployeeStatus.Resigned, 2020, 3),
                Employee(4, "E-30", "Dev", "Dunn", "Delta", "Chef", EmployeeStatus.Resigned, 2021, 4),
                Employee(5, "E-20", "Eli", "Evans", "Delta", "Chef", EmployeeStatus.Terminated, 2022, 5),
                Employee(6, "E-10", "Fay", "Frost", "Delta", "Chef", EmployeeStatus.Terminated, 2023, 6)
            };

            var departmentIds = departments.ToDictionary(d => d.Name, d => d.Id);
            var designationIds = designations.ToDictionary(d => d.Name, d => d.Id);

            using (var context = harness.CreateUnscopedContext())
            {
                context.Tenants.Add(new Tenant
                {
                    Id = TenantId,
                    TenantCode = "SORT01",
                    TenantName = "Sort Fixture Organization",
                    Status = TenantStatus.Active
                });

                context.Departments.AddRange(departments.Select(d => new Department
                {
                    Id = d.Id,
                    TenantId = TenantId,
                    Code = d.Code,
                    Name = d.Name,
                    IsActive = d.IsActive,
                    CreatedDate = d.CreatedDate
                }));

                context.Designations.AddRange(designations.Select(d => new Designation
                {
                    Id = d.Id,
                    TenantId = TenantId,
                    Code = d.Code,
                    Name = d.Name,
                    IsActive = d.IsActive,
                    CreatedDate = d.CreatedDate
                }));

                context.Employees.AddRange(employees.Select(e => new Employee
                {
                    Id = e.Id,
                    TenantId = TenantId,
                    EmployeeCode = e.Code,
                    FirstName = e.FirstName,
                    LastName = e.LastName,
                    Email = e.Email,
                    DateOfJoining = e.DateOfJoining,
                    DateOfLeaving = e.Status == EmployeeStatus.Active ? null : new DateOnly(2026, 1, 31),
                    Status = e.Status,
                    DepartmentId = departmentIds[e.DepartmentName],
                    DesignationId = designationIds[e.DesignationName],
                    CreatedDate = e.CreatedDate
                }));

                await context.SaveChangesAsync();
            }

            harness.ActAs(TenantId);

            var employeeRows = employees.ToList();
            return new SortFixture(
                departments.Select(d => d with
                {
                    EmployeeCount = employeeRows.Count(e => e.DepartmentName == d.Name)
                }).ToList(),
                designations.Select(d => d with
                {
                    EmployeeCount = employeeRows.Count(e => e.DesignationName == d.Name)
                }).ToList(),
                employeeRows);
        }

        private static DepartmentRow Department(int index, string code, string name, bool isActive, int day) =>
            new(FixtureId('3', 'd', index), code, name, isActive, EmployeeCount: 0, Created(1, day));

        private static DesignationRow Designation(int index, string code, string name, bool isActive, int day) =>
            new(FixtureId('3', 'e', index), code, name, isActive, EmployeeCount: 0, Created(1, day));

        private static EmployeeRow Employee(
            int index,
            string code,
            string first,
            string last,
            string department,
            string designation,
            EmployeeStatus status,
            int joinedYear,
            int createdDay) =>
            new(
                FixtureId('3', 'f', index),
                code,
                first,
                last,
                $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@sort01.test",
                department,
                designation,
                status,
                new DateOnly(joinedYear, createdDay, createdDay),
                Created(2, createdDay));

        private static DateTime Created(int month, int day) =>
            new(2026, month, day, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>Readable, collision-proof ids: the leading digits name the tenant and the entity.</summary>
        private static Guid FixtureId(char tenant, char entity, int index) =>
            new($"{tenant}{entity}000000-0000-0000-0000-{index:D12}");
    }
}
