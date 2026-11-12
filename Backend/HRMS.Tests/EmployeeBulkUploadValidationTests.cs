using ClosedXML.Excel;
using System.IO.Compression;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Application.Validators.Employees;
using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class EmployeeBulkUploadValidationTests
{
    private static readonly Guid Tenant = SeedData.TenantIds.Demo01;

    [Fact]
    public async Task Valid_workbook_returns_preview_without_persisting_or_allocating_employee_code()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        await ConfigureCode(harness);
        using var context = harness.CreateContext();
        using var template = new MemoryStream(await new EmployeeBulkUploadTemplateService(context, harness.TenantContext).GenerateAsync().ContinueWith(x => x.Result.Value!));
        using var workbook = new XLWorkbook(template);
        var sheet = workbook.Worksheet("Employees");
        var headers = sheet.Row(1).CellsUsed().ToDictionary(x => x.GetString(), x => x.Address.ColumnNumber);
        var designation = await context.Designations.AsNoTracking().Where(x => x.TenantId == Tenant && x.IsActive).Select(x => x.Code).FirstAsync();
        Set("First Name *", "Asha"); Set("Last Name *", "Rao"); Set("Aadhaar Number *", "999988887777"); Set("PAN *", "ABCDE1234F");
        Set("Date of Joining *", new DateTime(2026, 3, 4)); Set("Job Status *", "On Probation"); Set("Employment Effective From *", new DateTime(2026, 3, 4));
        Set("Department Code *", "ENG"); Set("Designation Code *", designation); Set("Employment Change Reason *", "NewJoining"); Set("Employment Type *", "FullTime");
        using var bytes = new MemoryStream(); workbook.SaveAs(bytes); bytes.Position = 0;
        var beforeEmployees = await context.Employees.IgnoreQueryFilters().CountAsync();
        var beforeSequences = await context.EmployeeCodeSequences.IgnoreQueryFilters().CountAsync();
        var result = await Service(context, harness).ValidateAsync(bytes);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal((1, 1, 0), (result.Value!.TotalRows, result.Value.ValidRows, result.Value.InvalidRows));
        Assert.Empty(result.Value.Rows[0].Issues);
        Assert.Equal(beforeEmployees, await context.Employees.IgnoreQueryFilters().CountAsync());
        Assert.Equal(beforeSequences, await context.EmployeeCodeSequences.IgnoreQueryFilters().CountAsync());

        void Set(string name, object value) => sheet.Cell(2, headers[name]).Value = XLCellValue.FromObject(value);
    }

    [Fact]
    public async Task Bulk_upload_selects_manual_or_automatic_configuration_by_each_employees_joining_date()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var manualStart = new DateOnly(2026, 3, 1);
        var manualEnd = new DateOnly(2026, 3, 3);
        var manual = await harness.CodeConfiguration().SaveAsync(new HRMS.Application.DTOs.Employees.EmployeeCodeConfigurationRequest
        {
            AutoGenerate = false, AssignmentMode = EmployeeCodeAssignmentMode.Manual,
            GenerationMethod = null, Prefix = "MAN", EffectiveFrom = manualStart, EffectiveTo = manualEnd
        });
        Assert.True(manual.Succeeded, manual.Message);
        var automatic = await harness.CodeConfiguration().SaveAsync(new HRMS.Application.DTOs.Employees.EmployeeCodeConfigurationRequest
        {
            AutoGenerate = true, AssignmentMode = EmployeeCodeAssignmentMode.Auto,
            GenerationMethod = EmployeeCodeGenerationMethod.Simple, Prefix = "AUTO", Separator = "-",
            NextNumber = 1, Padding = 5, EffectiveFrom = manualEnd.AddDays(1)
        });
        Assert.True(automatic.Succeeded, automatic.Message);

        using var context = harness.CreateContext();
        using var workbook = await Template(context, harness);
        var sheet = workbook.Worksheet("Employees");
        var columns = sheet.Row(1).CellsUsed().ToDictionary(x => x.GetString(), x => x.Address.ColumnNumber);
        var designation = await context.Designations.AsNoTracking().Where(x => x.TenantId == Tenant && x.IsActive).Select(x => x.Code).FirstAsync();
        FillRow(2, new DateOnly(2026, 3, 2), "Manual", "MAN-2026-001", "manual-upload@example.test", "999988887777", "ABCDE1234F");
        FillRow(3, new DateOnly(2026, 3, 4), "Automatic", "", "automatic-upload@example.test", "999988887778", "ABCDE1235F");

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        var result = await Service(context, harness).ValidateAsync(stream);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, result.Value!.ValidRows);
        Assert.Contains(result.Value.Rows, row => row.RowNumber == 2 && row.Issues.Count == 0);
        Assert.Contains(result.Value.Rows, row => row.RowNumber == 3 && row.Issues.Count == 0);

        void FillRow(int row, DateOnly joining, string firstName, string employeeCode, string email, string aadhaar, string pan)
        {
            sheet.Cell(row, columns["First Name *"]).Value = firstName;
            sheet.Cell(row, columns["Last Name *"]).Value = "Tester";
            sheet.Cell(row, columns["Aadhaar Number *"]).Value = aadhaar;
            sheet.Cell(row, columns["PAN *"]).Value = pan;
            sheet.Cell(row, columns["Date of Joining *"]).Value = joining.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, columns["Job Status *"]).Value = "On Probation";
            sheet.Cell(row, columns["Employment Effective From *"]).Value = joining.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, columns["Department Code *"]).Value = "ENG";
            sheet.Cell(row, columns["Designation Code *"]).Value = designation;
            sheet.Cell(row, columns["Employment Change Reason *"]).Value = "NewJoining";
            sheet.Cell(row, columns["Employment Type *"]).Value = "FullTime";
            sheet.Cell(row, columns["Official Email"]).Value = email;
            sheet.Cell(row, columns["Manual Employee Code"]).Value = employeeCode;
        }
    }

    [Fact]
    public async Task Historical_joining_date_is_valid_when_effective_dates_match_and_invalid_when_they_differ()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var configured = await harness.CodeConfiguration().SaveAsync(new HRMS.Application.DTOs.Employees.EmployeeCodeConfigurationRequest
        {
            AutoGenerate = false, AssignmentMode = EmployeeCodeAssignmentMode.Manual, GenerationMethod = null,
            Prefix = "EMP", EffectiveFrom = new DateOnly(2017, 1, 2), EffectiveTo = new DateOnly(2031, 9, 30)
        });
        Assert.True(configured.Succeeded, configured.Message);
        using var context = harness.CreateContext();
        using var workbook = await ValidWorkbook(context, harness);
        var sheet = workbook.Worksheet("Employees");
        var columns = sheet.Row(1).CellsUsed().ToDictionary(x => x.GetString(), x => x.Address.ColumnNumber);
        var historical = new DateTime(2018, 1, 20);
        sheet.Cell(2, columns["Date of Joining *"]).Value = historical;
        sheet.Cell(2, columns["Employment Effective From *"]).Value = historical;
        sheet.Cell(2, columns["Manual Employee Code"]).Value = "EMP000001";

        var valid = await Validate(workbook, context, harness);

        Assert.Equal((1, 0), (valid.ValidRows, valid.InvalidRows));
        Assert.Empty(valid.Rows.Single().Issues);

        using var mismatchedWorkbook = await ValidWorkbook(context, harness);
        var mismatchedSheet = mismatchedWorkbook.Worksheet("Employees");
        var mismatchedColumns = mismatchedSheet.Row(1).CellsUsed().ToDictionary(x => x.GetString(), x => x.Address.ColumnNumber);
        mismatchedSheet.Cell(2, mismatchedColumns["Date of Joining *"]).Value = historical;
        mismatchedSheet.Cell(2, mismatchedColumns["Employment Effective From *"]).Value = historical.AddDays(1);
        mismatchedSheet.Cell(2, mismatchedColumns["Manual Employee Code"]).Value = "EMP000001";
        var mismatched = await Validate(mismatchedWorkbook, context, harness);
        Assert.Equal((0, 1), (mismatched.ValidRows, mismatched.InvalidRows));
        Assert.Contains(mismatched.Rows.Single().Issues, issue => issue.ErrorCode == "employment.date_mismatch");
    }

    [Fact]
    public async Task Missing_and_duplicate_headers_are_reported_at_header_row_and_column()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        using var context = harness.CreateContext();
        using var workbook = await Template(context, harness);
        var sheet = workbook.Worksheet("Employees"); sheet.Cell(1, 1).Value = "First Name *";
        var result = await Validate(workbook, context, harness);
        Assert.Equal(1, result.Rows[0].RowNumber);
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "header.duplicate" && x.ColumnNumber == 2);
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "header.missing" && x.ColumnNumber == 0);
    }

    [Fact]
    public async Task Formula_cells_are_rejected_with_exact_excel_coordinates()
    {
        using var harness = await OrganizationTestHarness.CreateAsync(); using var context = harness.CreateContext(); using var workbook = await Template(context, harness);
        workbook.Worksheet("Employees").Cell(2, 2).FormulaA1 = "1+1";
        var result = await Validate(workbook, context, harness);
        Assert.Equal(2, result.Rows[0].RowNumber);
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "cell.formula" && x.ColumnNumber == 2 && x.ColumnName == "First Name *");
    }

    [Fact]
    public async Task Invalid_sensitive_values_are_not_returned_in_preview()
    {
        using var harness = await OrganizationTestHarness.CreateAsync(); using var context = harness.CreateContext(); using var workbook = await Template(context, harness);
        var s = workbook.Worksheet("Employees"); var h = s.Row(1).CellsUsed().ToDictionary(x => x.GetString(), x => x.Address.ColumnNumber);
        s.Cell(2, h["Aadhaar Number *"]).Value = "12345-private"; s.Cell(2, h["PAN *"]).Value = "secret-pan-value";
        var result = await Validate(workbook, context, harness);
        var response = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain("12345-private", response); Assert.DoesNotContain("secret-pan-value", response);
        Assert.Equal(2, result.Rows[0].RowNumber);
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "aadhaar.invalid" && x.ColumnNumber == h["Aadhaar Number *"]);
    }

    [Fact]
    public async Task Malformed_workbook_is_rejected_without_exception_details()
    {
        using var harness = await OrganizationTestHarness.CreateAsync(); using var context = harness.CreateContext();
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("not a workbook"));
        var result = await Service(context, harness).ValidateAsync(stream);
        Assert.False(result.Succeeded); Assert.DoesNotContain("Exception", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_tenant_is_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync(); harness.ActAs(null); using var context = harness.CreateContext(); using var stream = new MemoryStream([1, 2, 3]);
        var result = await Service(context, harness).ValidateAsync(stream);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task Empty_and_oversized_files_are_rejected_and_endpoint_requires_import_permission()
    {
        using var harness = await OrganizationTestHarness.CreateAsync(); using var context = harness.CreateContext();
        using var empty = new MemoryStream();
        var emptyResult = await Service(context, harness).ValidateAsync(empty);
        Assert.False(emptyResult.Succeeded);
        using var oversized = new MemoryStream(new byte[IEmployeeBulkUploadValidationService.MaximumFileBytes + 1]);
        var largeResult = await Service(context, harness).ValidateAsync(oversized);
        Assert.False(largeResult.Succeeded);
        var action = typeof(EmployeeBulkUploadValidationController).GetMethod(nameof(EmployeeBulkUploadValidationController.Validate))!;
        var permissions = action.GetCustomAttributes(typeof(HasPermissionAttribute), true).Cast<HasPermissionAttribute>().Select(x => x.Permission).ToArray();
        Assert.Contains(Permissions.Employee.Import, permissions);
        Assert.Contains(Permissions.EmployeeSensitive.Edit, permissions);
    }

    [Fact]
    public async Task Inactive_or_cross_tenant_master_codes_are_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        using (var demo2 = harness.Database.CreateContext(new TestTenantContext(SeedData.TenantIds.Demo02)))
        {
            demo2.Departments.Add(new Department { Id = Guid.NewGuid(), TenantId = SeedData.TenantIds.Demo02, Code = "FOREIGN-ONLY", Name = "Foreign", IsActive = true });
            await demo2.SaveChangesAsync();
        }
        using (var context = harness.CreateContext())
        {
            var inactive = await context.Departments.SingleAsync(x => x.TenantId == Tenant && x.Code == "ENG");
            inactive.IsActive = false; await context.SaveChangesAsync();
            using var workbook = await ValidWorkbook(context, harness);
            Set(workbook, "Department Code *", "ENG");
            var result = await Validate(workbook, context, harness);
            Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "master.invalid" && x.ColumnName == "Department Code *");
            using var foreignWorkbook = await ValidWorkbook(context, harness);
            Set(foreignWorkbook, "Department Code *", "FOREIGN-ONLY");
            result = await Validate(foreignWorkbook, context, harness);
            Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "master.invalid" && x.ColumnName == "Department Code *");
        }
    }

    [Fact]
    public async Task Parent_child_mismatch_and_missing_parent_references_are_reported()
    {
        using var harness = await OrganizationTestHarness.CreateAsync(); using var context = harness.CreateContext();
        var otherDepartmentId = await context.Departments.Where(x => x.TenantId == Tenant && x.Code != "ENG").Select(x => x.Id).FirstAsync();
        context.SubDepartments.Add(new SubDepartment { Id = Guid.NewGuid(), TenantId = Tenant, Code = "WRONG-PARENT", Name = "Wrong parent", IsActive = true, DepartmentId = otherDepartmentId });
        await context.SaveChangesAsync();
        using var workbook = await ValidWorkbook(context, harness);
        Set(workbook, "Sub Department Code", "WRONG-PARENT");
        var result = await Validate(workbook, context, harness);
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "master.parent_mismatch" && x.ColumnName == "Sub Department Code");
    }

    [Fact]
    public async Task Empty_master_reference_lists_make_required_codes_invalid()
    {
        using var harness = await OrganizationTestHarness.CreateAsync(); using var context = harness.CreateContext();
        using var workbook = await ValidWorkbook(context, harness);
        foreach (var row in await context.Departments.IgnoreQueryFilters().Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
        foreach (var row in await context.Designations.IgnoreQueryFilters().Where(x => x.TenantId == Tenant).ToListAsync()) row.IsActive = false;
        await context.SaveChangesAsync();
        var result = await Validate(workbook, context, harness);
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "master.invalid" && x.ColumnName == "Department Code *");
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "master.invalid" && x.ColumnName == "Designation Code *");
    }

    [Fact]
    public async Task Conditional_esic_requirement_and_unknown_manager_are_validated()
    {
        using var harness = await OrganizationTestHarness.CreateAsync(); await ConfigureCode(harness); using var context = harness.CreateContext();
        using var workbook = await ValidWorkbook(context, harness);
        Set(workbook, "ESIC Applicable", "Yes"); Set(workbook, "Manager Employee Code", "NO-SUCH-MANAGER");
        var result = await Validate(workbook, context, harness);
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "request.validation" && x.ColumnName == "ESIC Number");
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "manager.invalid" && x.ColumnName == "Manager Employee Code");
    }

    [Fact]
    public async Task Same_file_inactive_and_cross_tenant_manager_references_are_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        await ConfigureCode(harness);
        using (var tenantDb = harness.CreateContext())
        {
            tenantDb.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(), TenantId = Tenant, EmployeeCode = "INACTIVE-MANAGER", FirstName = "Inactive",
                LastName = "Manager", Email = "inactive.manager@example.test", DateOfJoining = new DateOnly(2020, 1, 1),
                Status = EmployeeStatus.Resigned
            });
            await tenantDb.SaveChangesAsync();
        }
        using (var foreignDb = harness.Database.CreateContext(new TestTenantContext(SeedData.TenantIds.Demo02)))
        {
            foreignDb.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(), TenantId = SeedData.TenantIds.Demo02, EmployeeCode = "FOREIGN-MANAGER", FirstName = "Foreign",
                LastName = "Manager", Email = "foreign.manager@example.test", DateOfJoining = new DateOnly(2020, 1, 1),
                Status = EmployeeStatus.Active
            });
            await foreignDb.SaveChangesAsync();
        }

        using var context = harness.CreateContext();
        using var sameFile = await ValidWorkbook(context, harness);
        Set(sameFile, "Manual Employee Code", "NEW-IN-FILE");
        Set(sameFile, "Manager Employee Code", "NEW-IN-FILE");
        var sameFileResult = await Validate(sameFile, context, harness);
        Assert.Contains(sameFileResult.Rows[0].Issues, x => x.ErrorCode == "manager.same_file" && x.ColumnName == "Manager Employee Code");

        using var inactive = await ValidWorkbook(context, harness);
        Set(inactive, "Manager Employee Code", "INACTIVE-MANAGER");
        var inactiveResult = await Validate(inactive, context, harness);
        Assert.Contains(inactiveResult.Rows[0].Issues, x => x.ErrorCode == "manager.invalid" && x.ColumnName == "Manager Employee Code");

        using var foreign = await ValidWorkbook(context, harness);
        Set(foreign, "Manager Employee Code", "FOREIGN-MANAGER");
        var foreignResult = await Validate(foreign, context, harness);
        Assert.Contains(foreignResult.Rows[0].Issues, x => x.ErrorCode == "manager.invalid" && x.ColumnName == "Manager Employee Code");
    }

    [Fact]
    public async Task Birth_state_and_city_must_belong_to_selected_parent_codes()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        using var context = harness.CreateContext();
        var state = await context.States.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Code, x.CountryId }).FirstAsync();
        var otherCountry = await context.Countries.AsNoTracking().Where(x => x.IsActive && x.Id != state.CountryId).Select(x => x.Code).FirstAsync();
        var city = await context.Cities.AsNoTracking().Where(x => x.IsActive && x.StateId != context.States.Where(s => s.Code == state.Code).Select(s => s.Id).First()).Select(x => x.Code).FirstOrDefaultAsync();
        Assert.NotNull(city);

        using var workbook = await ValidWorkbook(context, harness);
        Set(workbook, "Birth Country Code", otherCountry);
        Set(workbook, "Birth State Code", state.Code);
        Set(workbook, "Birth City Code", city!);
        var result = await Validate(workbook, context, harness);
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "master.parent_mismatch" && x.ColumnName == "Birth State Code");
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "master.parent_mismatch" && x.ColumnName == "Birth City Code");
    }

    [Fact]
    public async Task Existing_employee_identifier_and_contact_conflicts_are_reported_without_echoing_values()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        using (var context = harness.CreateContext())
        {
            context.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(), TenantId = Tenant, EmployeeCode = "EXISTING-CONFLICT", FirstName = "Existing",
                LastName = "Person", Email = "existing.conflict@example.test", AadhaarNumber = "999988887777",
                PanNumber = "ABCDE1234F", DateOfJoining = new DateOnly(2020, 1, 1), Status = EmployeeStatus.Active
            });
            await context.SaveChangesAsync();
        }
        using var db = harness.CreateContext();
        using var workbook = await ValidWorkbook(db, harness);
        var result = await Validate(workbook, db, harness);
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "employee.duplicate" && x.ColumnName == "Aadhaar Number *");
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "employee.duplicate" && x.ColumnName == "PAN *");
        var response = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain("999988887777", response);
        Assert.DoesNotContain("ABCDE1234F", response);
    }

    [Fact]
    public async Task Header_only_workbook_is_a_zero_row_error()
    {
        using var harness = await OrganizationTestHarness.CreateAsync(); using var context = harness.CreateContext(); using var workbook = await Template(context, harness);
        using var stream = new MemoryStream(); workbook.SaveAs(stream); using var input = new MemoryStream(stream.ToArray());
        var result = await Service(context, harness).ValidateAsync(input);
        Assert.False(result.Succeeded); Assert.Contains("no employee rows", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Duplicate_personal_identifiers_and_emails_across_rows_are_detected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync(); await ConfigureCode(harness); using var context = harness.CreateContext();
        using var workbook = await ValidWorkbook(context, harness);
        var sheet = workbook.Worksheet("Employees");
        foreach (var cell in sheet.Row(2).CellsUsed()) sheet.Cell(3, cell.Address.ColumnNumber).Value = cell.Value;
        var result = await Validate(workbook, context, harness);
        Assert.Equal(2, result.TotalRows); Assert.Equal(1, result.ValidRows); Assert.Equal(1, result.InvalidRows);
        Assert.Contains(result.Rows[1].Issues, x => x.ErrorCode == "employee.duplicate" && x.ColumnName == "Aadhaar Number *");
        Assert.Contains(result.Rows[1].Issues, x => x.ErrorCode == "employee.duplicate" && x.ColumnName == "Official Email");
    }

    [Fact]
    public async Task Rule_based_employee_code_configuration_must_match_and_render_without_allocating_sequence()
    {
        using var harness = await OrganizationTestHarness.CreateAsync();
        var otherDepartment = OrganizationTestHarness.DepartmentId(Tenant, await ReadOtherDepartmentCode(harness));
        var config = await harness.CodeConfiguration().SaveAsync(new HRMS.Application.DTOs.Employees.EmployeeCodeConfigurationRequest
        {
            AutoGenerate = true, AssignmentMode = EmployeeCodeAssignmentMode.Auto, GenerationMethod = EmployeeCodeGenerationMethod.RuleBased,
            Prefix = "EMP", Separator = "-", NextNumber = 1, EffectiveFrom = new DateOnly(2026, 3, 4)
        });
        Assert.True(config.Succeeded, config.Message);
        var rule = await harness.CodeConfiguration().SaveRuleAsync(null, new HRMS.Application.DTOs.Employees.EmployeeCodeRuleRequest
        {
            Name = "Only another department", Priority = 1, Status = EmployeeCodeRuleStatus.Active,
            Conditions = [new() { Field = EmployeeCodeConditionField.Department, ReferenceId = otherDepartment }],
            Segments = [new() { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.DepartmentCode }, new() { SequenceOrder = 2, SegmentType = EmployeeCodeSegmentType.SequentialNumber, PaddingLength = 4 }]
        });
        Assert.True(rule.Succeeded, rule.Message);
        using var context = harness.CreateContext(); using var workbook = await ValidWorkbook(context, harness);
        var before = await context.EmployeeCodeSequences.IgnoreQueryFilters().CountAsync();
        var result = await Validate(workbook, context, harness);
        Assert.Contains(result.Rows[0].Issues, x => x.ErrorCode == "employee_code.rule_unmatched");
        Assert.Equal(before, await context.EmployeeCodeSequences.IgnoreQueryFilters().CountAsync());

        static async Task<string> ReadOtherDepartmentCode(OrganizationTestHarness f)
        { using var db = f.CreateContext(); return await db.Departments.Where(x => x.TenantId == Tenant && x.Code != "ENG").Select(x => x.Code).FirstAsync(); }
    }

    [Fact]
    public async Task External_link_and_unsupported_cell_values_are_rejected()
    {
        using var harness = await OrganizationTestHarness.CreateAsync(); using var context = harness.CreateContext();
        using var workbook = await Template(context, harness); using var original = new MemoryStream(); workbook.SaveAs(original);
        using var linked = new MemoryStream();
        using (var archive = new ZipArchive(new MemoryStream(original.ToArray()), ZipArchiveMode.Read))
        using (var output = new ZipArchive(linked, ZipArchiveMode.Create, true))
        {
            foreach (var entry in archive.Entries)
            {
                var copy = output.CreateEntry(entry.FullName);
                using var source = entry.Open(); using var destination = copy.Open(); source.CopyTo(destination);
            }
            output.CreateEntry("xl/externalLinks/externalLink1.xml");
        }
        linked.Position = 0;
        var external = await Service(context, harness).ValidateAsync(linked);
        Assert.False(external.Succeeded); Assert.Contains("External", external.Message);

        using var invalid = await ValidWorkbook(context, harness);
        var cells = invalid.Worksheet("Employees").Row(1).CellsUsed().ToDictionary(x => x.GetString(), x => x.Address.ColumnNumber);
        invalid.Worksheet("Employees").Cell(2, cells["First Name *"]).Value = true;
        invalid.Worksheet("Employees").Cell(2, cells["Date of Joining *"]).Value = "not-a-date";
        var preview = await Validate(invalid, context, harness);
        Assert.Contains(preview.Rows[0].Issues, x => x.ErrorCode == "cell.type" && x.ColumnName == "First Name *");
        Assert.Contains(preview.Rows[0].Issues, x => x.ErrorCode == "cell.date" && x.ColumnName == "Date of Joining *");
    }

    [Fact]
    public async Task Row_limits_accept_5000_5001_and_10000_rows_and_reject_10001()
    {
        using var harness = await OrganizationTestHarness.CreateAsync(); using var context = harness.CreateContext();
        foreach (var acceptedRows in new[] { 5000, 5001, 10000 })
        {
            using var workbook = await Template(context, harness); var sheet = workbook.Worksheet("Employees");
            for (var r = 2; r <= acceptedRows + 1; r++) sheet.Cell(r, 2).Value = "Row";
            using var acceptedBytes = new MemoryStream(); workbook.SaveAs(acceptedBytes);
            using var acceptedStream = new MemoryStream(acceptedBytes.ToArray());
            var accepted = await Service(context, harness).ValidateAsync(acceptedStream);
            Assert.True(accepted.Succeeded, accepted.Message);
            Assert.Equal(acceptedRows, accepted.Value!.TotalRows);
        }

        using var overWorkbook = await Template(context, harness); var overSheet = overWorkbook.Worksheet("Employees");
        for (var r = 2; r <= 10002; r++) overSheet.Cell(r, 2).Value = "Row";
        using var bytes = new MemoryStream(); overWorkbook.SaveAs(bytes);
        using var overStream = new MemoryStream(bytes.ToArray());
        var over = await Service(context, harness).ValidateAsync(overStream);
        Assert.False(over.Succeeded); Assert.Contains("10,000", over.Message);
    }

    [Fact]
    public async Task Configured_employee_row_limit_is_enforced()
    {
        using var harness = await OrganizationTestHarness.CreateAsync(); using var context = harness.CreateContext();
        using var workbook = await Template(context, harness); var sheet = workbook.Worksheet("Employees");
        for (var r = 2; r <= 5002; r++) sheet.Cell(r, 2).Value = "Row";
        using var bytes = new MemoryStream(); workbook.SaveAs(bytes); bytes.Position = 0;
        var limited = new EmployeeBulkUploadValidationService(context, harness.TenantContext,
            new EmployeePersonalDetailsRequestValidator(), new EmployeeContactRequestValidator(), new EmploymentChangeRequestValidator(),
            new EmployeeBulkUploadOptions { MaximumEmployeeRows = 5000 });
        var result = await limited.ValidateAsync(bytes);
        Assert.False(result.Succeeded);
        Assert.Contains("5,000", result.Message);
    }

    private static async Task ConfigureCode(OrganizationTestHarness harness)
    {
        var result = await harness.CodeConfiguration().SaveAsync(new HRMS.Application.DTOs.Employees.EmployeeCodeConfigurationRequest { AutoGenerate = true, AssignmentMode = EmployeeCodeAssignmentMode.Auto, GenerationMethod = EmployeeCodeGenerationMethod.Simple, Prefix = "EMP", Separator = "-", NextNumber = 1, Padding = 5, EffectiveFrom = new DateOnly(2026, 3, 4) });
        Assert.True(result.Succeeded, result.Message);
    }
    private static EmployeeBulkUploadValidationService Service(HRMS.Infrastructure.Persistence.HrmsDbContext db, OrganizationTestHarness h) => new(db, h.TenantContext, new EmployeePersonalDetailsRequestValidator(), new EmployeeContactRequestValidator(), new EmploymentChangeRequestValidator());
    private static async Task<XLWorkbook> Template(HRMS.Infrastructure.Persistence.HrmsDbContext db, OrganizationTestHarness h) => new(new MemoryStream(await new EmployeeBulkUploadTemplateService(db, h.TenantContext).GenerateAsync().ContinueWith(x => x.Result.Value!)));
    private static async Task<XLWorkbook> ValidWorkbook(HRMS.Infrastructure.Persistence.HrmsDbContext db, OrganizationTestHarness h)
    {
        var workbook = await Template(db, h); var sheet = workbook.Worksheet("Employees");
        var headers = sheet.Row(1).CellsUsed().ToDictionary(x => x.GetString(), x => x.Address.ColumnNumber);
        var designation = await db.Designations.AsNoTracking().Where(x => x.TenantId == Tenant && x.IsActive).Select(x => x.Code).FirstAsync();
        sheet.Cell(2, headers["First Name *"]).Value = "Asha"; sheet.Cell(2, headers["Last Name *"]).Value = "Rao";
        sheet.Cell(2, headers["Aadhaar Number *"]).Value = "999988887777"; sheet.Cell(2, headers["PAN *"]).Value = "ABCDE1234F";
        sheet.Cell(2, headers["Date of Joining *"]).Value = new DateTime(2026, 3, 4); sheet.Cell(2, headers["Job Status *"]).Value = "On Probation";
        sheet.Cell(2, headers["Employment Effective From *"]).Value = new DateTime(2026, 3, 4); sheet.Cell(2, headers["Department Code *"]).Value = "ENG";
        sheet.Cell(2, headers["Designation Code *"]).Value = designation; sheet.Cell(2, headers["Employment Change Reason *"]).Value = "NewJoining";
        sheet.Cell(2, headers["Employment Type *"]).Value = "FullTime"; sheet.Cell(2, headers["Official Email"]).Value = "asha.work@example.test";
        return workbook;
    }
    private static void Set(XLWorkbook workbook, string header, string value)
    { var sheet = workbook.Worksheet("Employees"); var column = sheet.Row(1).CellsUsed().Single(x => x.GetString() == header).Address.ColumnNumber; sheet.Cell(2, column).Value = value; }
    private static async Task<HRMS.Application.DTOs.Employees.EmployeeBulkUploadPreviewDto> Validate(XLWorkbook workbook, HRMS.Infrastructure.Persistence.HrmsDbContext db, OrganizationTestHarness h)
    { using var stream = new MemoryStream(); workbook.SaveAs(stream); using var input = new MemoryStream(stream.ToArray()); var result = await Service(db, h).ValidateAsync(input); Assert.True(result.Succeeded, result.Message); return result.Value!; }
}
