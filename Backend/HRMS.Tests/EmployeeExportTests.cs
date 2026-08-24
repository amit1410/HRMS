using System.Text;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// The CSV export. A download is the one place where employee-entered text leaves the system as a file
/// somebody opens in a spreadsheet, so the format tests here are as much about safety as about shape: the
/// byte-order mark, the quoting rules, and the neutralization of values a spreadsheet would otherwise
/// execute. The tenant scoping and the row cap are checked for the same reason they exist — an export is
/// the largest single disclosure the API can produce.
/// </summary>
public class EmployeeExportTests
{
    private static readonly Guid Demo01 = SeedData.TenantIds.Demo01;
    private static readonly Guid Demo02 = SeedData.TenantIds.Demo02;

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private const string Header =
        "Employee Code,First Name,Last Name,Email,Phone,Date of Birth,Gender,Date of Joining," +
        "Date of Leaving,Status,Department,Designation,Reporting Manager,Address";

    [Fact]
    public async Task Export_leads_with_a_byte_order_mark_then_the_header_row()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var file = (await harness.Employees().ExportAsync(new EmployeeQuery())).Value!;

        // Without the BOM, Excel reads the file in the local code page and mangles every non-ASCII name.
        Assert.Equal(Utf8Bom, file.Content.Take(3));

        var lines = Lines(file);
        Assert.Equal(Header, lines[0]);
        Assert.Equal(14, lines[0].Split(',').Length);
    }

    [Fact]
    public async Task Export_separates_rows_with_a_carriage_return_and_line_feed()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var text = TextOf((await harness.Employees().ExportAsync(new EmployeeQuery())).Value!);

        Assert.EndsWith("\r\n", text);
        Assert.DoesNotContain("\n\n", text);

        // Every line feed in this data is part of a CRLF pair — no bare newline anywhere.
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                Assert.True(i > 0 && text[i - 1] == '\r', $"Bare line feed at index {i}.");
            }
        }
    }

    [Fact]
    public async Task Export_carries_the_expected_row_and_the_joined_names()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var file = (await harness.Employees().ExportAsync(new EmployeeQuery())).Value!;
        var lines = Lines(file);

        Assert.Equal(6, file.RowCount); // the header is not a row
        Assert.Equal(7, lines.Count);

        var priya = lines.Single(l => l.StartsWith("EMP-003,", StringComparison.Ordinal));
        Assert.Equal(
            "EMP-003,Priya,Raman,priya.raman@demo01.com,555-0103,1991-02-18,Female,2019-07-01," +
            ",Active,Engineering,Senior Software Engineer,Owen Brand,44 Orchard Street",
            priya);
    }

    /// <summary>
    /// The decisive one. An export is a bulk read, so if the tenant filter were missing anywhere this is
    /// where an entire other organization's staff list would walk out in a single file.
    /// </summary>
    [Fact]
    public async Task Export_contains_only_the_callers_own_employees()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var file = (await harness.ActAs(Demo02).Employees().ExportAsync(new EmployeeQuery())).Value!;
        var text = TextOf(file);

        Assert.Equal(2, file.RowCount);
        Assert.Contains("Grace", text, StringComparison.Ordinal);
        Assert.Contains("Liam", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Nadia", text, StringComparison.Ordinal);
        Assert.DoesNotContain("demo01.com", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_applies_the_same_filters_as_the_list()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var engineering = await harness.Employees().ExportAsync(new EmployeeQuery
        {
            DepartmentId = OrganizationTestHarness.DepartmentId(Demo01, "ENG")
        });
        var searched = await harness.Employees().ExportAsync(new EmployeeQuery { Search = "kovac" });

        Assert.Equal(4, engineering.Value!.RowCount);
        Assert.Single(Lines(searched.Value!).Skip(1));
        Assert.Contains("Mira", TextOf(searched.Value!), StringComparison.Ordinal);
    }

    /// <summary>An export is the whole filtered set, not the page the user happens to be looking at.</summary>
    [Fact]
    public async Task Export_ignores_paging()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var file = (await harness.Employees().ExportAsync(new EmployeeQuery { Page = 2, PageSize = 2 })).Value!;

        Assert.Equal(6, file.RowCount);
    }

    [Fact]
    public async Task Export_uses_the_same_ordering_as_the_list()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var query = new EmployeeQuery { SortBy = "employeeCode", SortDescending = true };

        var exported = Lines((await harness.Employees().ExportAsync(query)).Value!)
            .Skip(1)
            .Select(l => l[..l.IndexOf(',')])
            .ToList();
        var listed = (await harness.Employees().GetAsync(query)).Value!.Items
            .Select(e => e.EmployeeCode)
            .ToList();

        Assert.Equal(new[] { "EMP-006", "EMP-005", "EMP-004", "EMP-003", "EMP-002", "EMP-001" }, exported);
        Assert.Equal(listed, exported.Take(listed.Count));
    }

    [Fact]
    public async Task Export_is_named_for_the_moment_it_was_generated()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.Clock.Now = new DateTimeOffset(2026, 7, 19, 14, 5, 42, TimeSpan.Zero);

        var file = (await harness.Employees().ExportAsync(new EmployeeQuery())).Value!;

        Assert.Equal("employees-20260719-140542.csv", file.FileName);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
    }

    /// <summary>
    /// A comma would end the field early, a quote would end the quoting early, and a line break would end
    /// the record early — all three are escaped rather than trusted, so a free-text address cannot forge
    /// extra columns or rows in the file.
    /// </summary>
    [Fact]
    public async Task Export_quotes_values_containing_a_comma_a_quote_or_a_line_break()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var request = NewRequest();
        request.Address = "12 Main St, Suite \"4\"\nGate 7";
        Assert.True((await harness.Employees().CreateAsync(request)).Succeeded);

        var line = Lines((await harness.Employees().ExportAsync(new EmployeeQuery { Search = "EMP-777" })).Value!)[1];

        Assert.EndsWith(",\"12 Main St, Suite \"\"4\"\"\nGate 7\"", line);
    }

    /// <summary>
    /// CSV injection. A cell beginning with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> is a formula when the
    /// file is opened, which would turn a field an employee controls into code running on whoever opens the
    /// export. Each is prefixed with an apostrophe so the spreadsheet treats it as text.
    /// </summary>
    [Fact]
    public async Task Export_neutralizes_values_a_spreadsheet_would_execute()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var request = NewRequest();
        request.FirstName = "=SUM(A1:A9)";
        request.LastName = "@Reference";
        request.Phone = "+1-555-0199";
        request.Address = "-Wing 3";
        Assert.True((await harness.Employees().CreateAsync(request)).Succeeded);

        var line = Lines((await harness.Employees().ExportAsync(new EmployeeQuery { Search = "EMP-777" })).Value!)[1];
        var fields = line.Split(',');

        Assert.Equal("'=SUM(A1:A9)", fields[1]);
        Assert.Equal("'@Reference", fields[2]);
        Assert.Equal("'+1-555-0199", fields[4]);
        Assert.Equal("'-Wing 3", fields[^1]);
    }

    [Fact]
    public async Task Export_writes_an_empty_field_where_a_value_is_absent()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var request = NewRequest();
        request.Phone = null;
        request.DateOfBirth = null;
        request.Address = null;
        Assert.True((await harness.Employees().CreateAsync(request)).Succeeded);

        var line = Lines((await harness.Employees().ExportAsync(new EmployeeQuery { Search = "EMP-777" })).Value!)[1];

        // Code, names, email, then two empty fields for phone and date of birth; no manager and no address.
        Assert.Equal(
            "EMP-777,Sam,Okafor,sam.okafor@demo01.com,,,Other,2023-05-01,,Active,Engineering,Software Engineer,,",
            line);
    }

    [Fact]
    public async Task Export_of_an_empty_result_is_a_header_and_nothing_else()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();

        var file = (await harness.Employees().ExportAsync(new EmployeeQuery
        {
            Status = EmployeeStatus.Terminated
        })).Value!;

        Assert.Equal(0, file.RowCount);
        Assert.Equal(Header + "\r\n", TextOf(file));
    }

    /// <summary>
    /// The cap is a memory bound: the whole file is built in memory before it is handed to the client, so an
    /// unbounded export is a way to take the API down with one request. At the limit it still succeeds —
    /// refusing early would be a silently truncated file, which is worse than an error.
    /// </summary>
    [Fact]
    public async Task Export_refuses_a_result_set_above_the_row_limit()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        await AddBulkEmployeesAsync(harness, 9_994); // 6 seeded + 9,994 = exactly the limit

        var atTheLimit = await harness.Employees().ExportAsync(new EmployeeQuery());
        Assert.True(atTheLimit.Succeeded);
        Assert.Equal(10_000, atTheLimit.Value!.RowCount);

        await AddBulkEmployeesAsync(harness, 1, startAt: 9_994);

        var overTheLimit = await harness.Employees().ExportAsync(new EmployeeQuery());
        Assert.Equal(ResultStatus.ValidationFailed, overTheLimit.Status);
        Assert.Null(overTheLimit.Value);
        Assert.Contains("10001", overTheLimit.Message, StringComparison.Ordinal);
        Assert.Contains("10000", overTheLimit.Message, StringComparison.Ordinal);

        // A narrower filter still works, which is what the refusal tells the caller to do.
        var narrowed = await harness.Employees().ExportAsync(new EmployeeQuery
        {
            DepartmentId = OrganizationTestHarness.DepartmentId(Demo01, "HR")
        });
        Assert.True(narrowed.Succeeded);
        Assert.Equal(1, narrowed.Value!.RowCount);
    }

    /// <summary>Bulk rows written straight to the context; the audit stamp fills in tenant and dates.</summary>
    private static async Task AddBulkEmployeesAsync(
        OrganizationTestHarness harness, int count, int startAt = 0)
    {
        using var context = harness.CreateContext();
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        var departmentId = OrganizationTestHarness.DepartmentId(Demo01, "ENG");
        var designationId = OrganizationTestHarness.DesignationId(Demo01, "SE");

        for (var i = startAt; i < startAt + count; i++)
        {
            context.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(),
                EmployeeCode = $"BULK-{i:D5}",
                FirstName = "Bulk",
                LastName = $"Row{i:D5}",
                Email = $"bulk{i:D5}@demo01.com",
                DateOfJoining = new DateOnly(2024, 1, 1),
                Status = EmployeeStatus.Active,
                DepartmentId = departmentId,
                DesignationId = designationId
            });
        }

        await context.SaveChangesAsync();
    }

    private static EmployeeRequest NewRequest() => new()
    {
        EmployeeCode = "EMP-777",
        FirstName = "Sam",
        LastName = "Okafor",
        Email = "sam.okafor@demo01.com",
        Phone = "555-0177",
        DateOfBirth = new DateOnly(1994, 3, 3),
        Gender = Gender.Other,
        DateOfJoining = new DateOnly(2023, 5, 1),
        Status = EmployeeStatus.Active,
        DepartmentId = OrganizationTestHarness.DepartmentId(Demo01, "ENG"),
        DesignationId = OrganizationTestHarness.DesignationId(Demo01, "SE")
    };

    /// <summary>The document with its byte-order mark removed, after asserting the mark is there.</summary>
    private static string TextOf(EmployeeExportDto file)
    {
        Assert.Equal(Utf8Bom, file.Content.Take(3));
        return Encoding.UTF8.GetString(file.Content, 3, file.Content.Length - 3);
    }

    /// <summary>Physical lines, header first, with the trailing record separator dropped.</summary>
    private static List<string> Lines(EmployeeExportDto file) =>
        TextOf(file).Split("\r\n").SkipLast(1).ToList();
}
