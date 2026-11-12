using System.Reflection;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class EmployeeBulkUploadTemplateTests
{
    private static readonly Guid Tenant = SeedData.TenantIds.Demo01;

    [Fact]
    public async Task Workbook_has_required_sheets_columns_comments_formats_and_valid_xlsx_content()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var bytes = await GenerateAsync(harness);

        Assert.StartsWith("PK", System.Text.Encoding.ASCII.GetString(bytes, 0, 2));
        using var workbook = Open(bytes);
        var requiredSheets = new[]
        {
            "Instructions", "Employees", "Holding Companies", "Lines of Business", "Organizations", "Departments",
            "Sub Departments", "Sections", "Sub Sections", "Functions", "Sub Functions", "Grades", "Designations",
            "Employee Types", "Work Locations", "Cost Centers", "Position Change Reasons", "Countries", "States", "Cities"
        };
        var workbookSheetNames = workbook.Worksheets.Select(x => x.Name).ToArray();
        Assert.All(requiredSheets, name => Assert.Contains(name, workbookSheetNames));
        Assert.Equal(XLWorksheetVisibility.Hidden, workbook.Worksheet("_Lists").Visibility);

        var employees = workbook.Worksheet("Employees");
        Assert.Equal("First Name *", employees.Cell(1, HeaderColumn(employees, "First Name *")).GetString());
        Assert.NotNull(employees.Cell(1, HeaderColumn(employees, "First Name *")).GetComment());
        Assert.Contains("Department Code *", Headers(employees));
        Assert.Contains("Designation Code *", Headers(employees));
        Assert.DoesNotContain("Employee Code *", Headers(employees));
        Assert.Equal("yyyy-mm-dd", employees.Cell(2, HeaderColumn(employees, "Date of Joining *")).Style.DateFormat.Format);
        Assert.Equal("yyyy-mm-dd", employees.Cell(10001, HeaderColumn(employees, "Date of Joining *")).Style.DateFormat.Format);
        AssertFrozenHeader(bytes, "Employees");
        Assert.True(employees.AutoFilter.IsEnabled);
    }

    [Fact]
    public async Task Master_sheets_include_only_active_tenant_records_and_parent_codes_without_guids()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        using (var context = harness.CreateContext())
        {
            var inactive = await context.Departments.SingleAsync(x => x.TenantId == Tenant && x.Code == "ENG");
            inactive.IsActive = false;
            await context.SaveChangesAsync();
        }
        harness.ActAs(SeedData.TenantIds.Demo02);
        using (var foreignContext = harness.CreateContext())
        {
            foreignContext.HoldingCompanies.Add(new HoldingCompany
            {
                Id = Guid.NewGuid(), TenantId = SeedData.TenantIds.Demo02,
                Code = "FOREIGN-HOLDING-SHOULD-NOT-APPEAR", Name = "Foreign tenant master", IsActive = true
            });
            await foreignContext.SaveChangesAsync();
        }
        harness.ActAs(Tenant);

        using var workbook = Open(await GenerateAsync(harness));
        var departments = workbook.Worksheet("Departments");
        Assert.DoesNotContain("ENG", ColumnValues(departments, 1));
        Assert.Contains("Holding Companies", workbook.Worksheets.Select(x => x.Name));
        var lobs = workbook.Worksheet("Lines of Business");
        var lobRow = FindRow(lobs, "LOB-IT");
        Assert.Equal("HC01", lobs.Cell(lobRow, 4).GetString());
        Assert.DoesNotContain("FOREIGN-HOLDING-SHOULD-NOT-APPEAR", AllText(workbook));
        Assert.DoesNotContain("Foreign tenant master", AllText(workbook));
        var workbookValues = AllText(workbook)
            .Concat(workbook.Worksheet("_Lists").CellsUsed().Select(x => x.GetString())).ToArray();
        Assert.DoesNotContain("EMP-001", workbookValues);
        Assert.DoesNotContain(workbookValues, value => Regex.IsMatch(value, @"\b[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\b"));
    }

    [Fact]
    public async Task Employee_master_and_dependent_dropdowns_use_code_lists_and_parent_mappings()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        using var workbook = Open(await GenerateAsync(harness));
        var employees = workbook.Worksheet("Employees");

        AssertListValidation(employees, "Department Code *", "List_Departments");
        AssertListValidation(employees, "Gender", "List_Gender");
        AssertListValidation(employees, "LOB Code", "Map_Holding_Companies_Lines_of_Business");
        AssertListValidation(employees, "Sub Department Code", "Map_Departments_Sub_Departments");
        AssertListValidation(employees, "Section Code", "Map_Sub_Departments_Sections");
        AssertListValidation(employees, "Sub Section Code", "Map_Sections_Sub_Sections");
        AssertListValidation(employees, "Sub Function Code", "Map_Functions_Sub_Functions");
        AssertListValidation(employees, "Birth State Code", "Map_Countries_States");
        AssertListValidation(employees, "Birth City Code", "Map_States_Cities");

        var organization = HeaderColumn(employees, "Organization Code");
        Assert.Contains("List_Organizations", employees.Cell(2, organization).GetDataValidation().Value);
    }

    [Fact]
    public async Task Empty_tenant_master_lists_still_produce_a_valid_workbook_and_blank_dropdowns()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        using (var context = harness.CreateContext())
        {
            foreach (var row in await context.Departments.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.HoldingCompanies.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.LinesOfBusiness.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.Organisations.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.Designations.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.SubDepartments.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.Sections.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.SubSections.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.Functions.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.SubFunctions.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.Grades.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.EmployeeTypes.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.WorkLocations.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.CostCenters.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            foreach (var row in await context.PositionChangeReasons.Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
            await context.SaveChangesAsync();
        }

        using var workbook = Open(await GenerateAsync(harness));
        Assert.Empty(ColumnValues(workbook.Worksheet("Departments"), 1).Skip(1));
        AssertListValidation(workbook.Worksheet("Employees"), "Department Code *", "List_Empty");
        Assert.Contains("Employees", workbook.Worksheets.Select(x => x.Name));
    }

    [Fact]
    public async Task Large_master_lists_exceed_inline_validation_limits_and_remain_code_based()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        using (var context = harness.CreateContext())
        {
            for (var i = 0; i < 1200; i++)
                context.Departments.Add(new Department
                {
                    Id = Guid.NewGuid(), TenantId = Tenant, Code = $"D{i:0000}", Name = $"Department {i:0000}", IsActive = true
                });
            await context.SaveChangesAsync();
        }

        using var workbook = Open(await GenerateAsync(harness));
        var departments = workbook.Worksheet("Departments");
        Assert.True(departments.LastRowUsed()!.RowNumber() >= 1202);
        AssertListValidation(workbook.Worksheet("Employees"), "Department Code *", "List_Departments");
        Assert.Contains("D1199", ColumnValues(departments, 1));
    }

    [Fact]
    public void Download_endpoint_requires_employee_import_permission()
    {
        var method = typeof(EmployeeBulkUploadTemplateController).GetMethod(nameof(EmployeeBulkUploadTemplateController.Download));
        Assert.NotNull(method);
        var permission = method!.GetCustomAttribute<HasPermissionAttribute>();
        Assert.NotNull(permission);
        Assert.Equal(Permissions.Employee.Import, permission!.Permission);
    }

    [Fact]
    public async Task Template_requires_an_authenticated_tenant_context()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        harness.ActAs(null);
        var service = new EmployeeBulkUploadTemplateService(harness.CreateContext(), harness.TenantContext);

        var result = await service.GenerateAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task Template_row_validation_range_uses_configured_maximum()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var service = new EmployeeBulkUploadTemplateService(harness.CreateContext(), harness.TenantContext,
            new EmployeeBulkUploadOptions { MaximumEmployeeRows = 5000 });
        var result = await service.GenerateAsync();
        Assert.True(result.Succeeded, result.Message);
        using var workbook = Open(result.Value!);
        var employees = workbook.Worksheet("Employees");
        var dateColumn = HeaderColumn(employees, "Date of Joining *");
        Assert.Equal("yyyy-mm-dd", employees.Cell(5001, dateColumn).Style.DateFormat.Format);
        Assert.NotEqual("yyyy-mm-dd", employees.Cell(5002, dateColumn).Style.DateFormat.Format);
    }

    private static async Task<byte[]> GenerateAsync(OrganizationTestHarness harness)
    {
        var service = new EmployeeBulkUploadTemplateService(harness.CreateContext(), harness.TenantContext);
        var result = await service.GenerateAsync();
        Assert.True(result.Succeeded, result.Message);
        return result.Value!;
    }

    private static XLWorkbook Open(byte[] bytes) => new(new MemoryStream(bytes));

    private static IReadOnlyList<string> Headers(IXLWorksheet sheet) =>
        sheet.Row(1).CellsUsed().Select(x => x.GetString()).ToArray();

    private static int HeaderColumn(IXLWorksheet sheet, string header) =>
        Headers(sheet).Select((name, index) => (name, index)).Single(x => x.name == header).index + 1;

    private static IReadOnlyList<string> ColumnValues(IXLWorksheet sheet, int column) =>
        sheet.Column(column).CellsUsed().Skip(1).Select(x => x.GetString()).ToArray();

    private static int FindRow(IXLWorksheet sheet, string code)
    {
        var row = sheet.Column(1).CellsUsed().Single(x => x.GetString() == code);
        return row.Address.RowNumber;
    }

    private static IEnumerable<string> AllText(XLWorkbook workbook) => workbook.Worksheets
        .SelectMany(sheet => sheet.CellsUsed().Select(cell => cell.GetString()));

    private static void AssertListValidation(IXLWorksheet sheet, string header, string expectedText)
    {
        var validation = sheet.Cell(2, HeaderColumn(sheet, header)).GetDataValidation();
        Assert.Equal(XLAllowedValues.List, validation.AllowedValues);
        Assert.Contains(expectedText, validation.Value);
    }

    private static void AssertFrozenHeader(byte[] bytes, string sheetName)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var workbook = XDocument.Load(archive.GetEntry("xl/workbook.xml")!.Open());
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetIndex = workbook.Root!.Element(ns + "sheets")!.Elements(ns + "sheet")
            .Single(x => (string?)x.Attribute("name") == sheetName).Attribute("sheetId")!.Value;
        var entry = archive.GetEntry($"xl/worksheets/sheet{sheetIndex}.xml")!;
        var document = XDocument.Load(entry.Open());
        var pane = document.Root!.Element(ns + "sheetViews")!.Element(ns + "sheetView")!.Element(ns + "pane");
        Assert.Equal("1", (string?)pane?.Attribute("ySplit"));
    }
}
