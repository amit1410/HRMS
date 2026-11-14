using System.Globalization;
using System.Diagnostics;
using System.IO.Compression;
using ClosedXML.Excel;
using FluentValidation;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.EmployeeCodes;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace HRMS.Application.Services;

/// <summary>Read-only, tenant-scoped validation of an employee workbook. This service deliberately has no write path.</summary>
public sealed class EmployeeBulkUploadValidationService(
    IHrmsDbContext db,
    ITenantContext tenant,
    IValidator<EmployeePersonalDetailsRequest> personalValidator,
    IValidator<EmployeeContactRequest> contactValidator,
    IValidator<EmploymentChangeRequest> employmentValidator,
    EmployeeBulkUploadOptions? uploadOptions = null,
    IHostEnvironment? hostEnvironment = null) : IEmployeeBulkUploadValidationService
{
    private readonly int _maximumEmployeeRows = uploadOptions?.MaximumEmployeeRows ?? EmployeeBulkUploadOptions.MaximumAllowedEmployeeRows;
    private static readonly string[] Headers = [
        "Salutation", "First Name *", "Middle Name", "Last Name *", "Date of Birth", "Gender", "Blood Group", "Marital Status", "Birth Country Code", "Birth State Code", "Birth City Code", "Religion", "Caste", "Citizenship", "ESIC Applicable", "ESIC Number", "PF Number", "Mediclaim Number", "UAN Number", "Gratuity", "Pension", "Aadhaar Number *", "PAN *", "Date of Joining *", "Job Status *", "Official Email", "Personal Email", "Alternate Email", "Official Phone", "Personal Phone", "Emergency Number", "Same As Current Address", "Manual Employee Code", "Employment Effective From *", "Holding Company Code", "LOB Code", "Organization Code", "Department Code *", "Sub Department Code", "Section Code", "Sub Section Code", "Function Code", "Sub Function Code", "Grade Code", "Designation Code *", "Employee Type Code", "Country Location Code", "Work Location Code", "Cost Center Code", "Manager Employee Code", "Position Change Reason Code", "Employment Change Reason *", "Change Reason Description", "Business Role", "Grade Level", "Career Group", "Employment Type *", "Employment Status"];
    public async Task<Result<EmployeeBulkUploadPreviewDto>> ValidateAsync(Stream input, CancellationToken cancellationToken = default)
    {
        var validated = await ValidateCoreAsync(input, includeValidRowsInPreview: true, cancellationToken);
        return validated.Succeeded
            ? Result<EmployeeBulkUploadPreviewDto>.Success(validated.Value!.Preview, validated.Message)
            : Result<EmployeeBulkUploadPreviewDto>.Failure(validated.Status, validated.Message, validated.Errors);
    }

    public async Task<Result<EmployeeBulkUploadValidationResult>> ValidateForExecutionAsync(Stream input, CancellationToken cancellationToken = default)
        => await ValidateCoreAsync(input, includeValidRowsInPreview: false, cancellationToken);

    private async Task<Result<EmployeeBulkUploadValidationResult>> ValidateCoreAsync(Stream input, bool includeValidRowsInPreview, CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty) return Result<EmployeeBulkUploadValidationResult>.Unauthorized("No authenticated tenant.");
        if (!input.CanRead) return InvalidWorkbook("workbook", "Workbook stream is not readable.", "workbook.open", "workbook.unreadable");
        MemoryStream? buffered = null;
        var diagnosticStage = "workbook.open";
        var diagnosticOperation = "stream.inspect_capabilities";
        var diagnosticResourceType = input is MemoryStream ? "memory_stream" : "stream";
        int? diagnosticRow = null;
        try
        {
            Stream workbookStream;
            if (input.CanSeek)
            {
                if (input.Length > IEmployeeBulkUploadValidationService.MaximumFileBytes)
                    return InvalidWorkbook("file", "Workbook exceeds the 10 MB limit.", "workbook.size", "workbook.file_size_limit");
                diagnosticOperation = "stream.seek_to_start";
                input.Position = 0;
                workbookStream = input;
            }
            else
            {
                diagnosticOperation = "memory.create_buffer";
                buffered = new MemoryStream();
                diagnosticResourceType = "memory_stream";
                diagnosticOperation = "stream.copy_to_memory";
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    if (buffered.Length + read > IEmployeeBulkUploadValidationService.MaximumFileBytes)
                        return InvalidWorkbook("file", "Workbook exceeds the 10 MB limit.", "workbook.size", "workbook.file_size_limit");
                    await buffered.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (buffered.Length == 0) return InvalidWorkbook("file", "Workbook is empty.", "workbook.open", "workbook.empty");
                buffered.Position = 0;
                workbookStream = buffered;
            }
            if (workbookStream.CanSeek && workbookStream.Length == 0) return InvalidWorkbook("file", "Workbook is empty.", "workbook.open", "workbook.empty");
            diagnosticStage = "workbook.package";
            diagnosticOperation = "zip.inspect_stream";
            var packageIssue = InspectPackage(workbookStream);
            if (packageIssue is not null) return InvalidWorkbook("file", packageIssue, "workbook.package", packageIssue.Contains("resource limits", StringComparison.Ordinal) ? "workbook.package_limits" : packageIssue.Contains("External workbook", StringComparison.Ordinal) ? "workbook.external_links" : "workbook.invalid_xlsx");
            if (workbookStream.CanSeek)
            {
                diagnosticOperation = "stream.seek_before_excel_parse";
                workbookStream.Position = 0;
            }
            diagnosticStage = "workbook.structure.parser_load";
            diagnosticOperation = "excel_parser.load_stream";
            using var workbook = new XLWorkbook(workbookStream);
            diagnosticStage = "workbook.structure.sheet_lookup";
            diagnosticOperation = "excel_parser.find_employee_sheet";
            if (!workbook.Worksheets.TryGetWorksheet("Employees", out var sheet)) return InvalidWorkbook("Employees", "The Employees worksheet is required.", "workbook.structure", "workbook.worksheet_missing");
            diagnosticStage = "workbook.structure.header_read";
            diagnosticOperation = "excel_parser.read_header_cells";
            var headerRow = sheet.Row(1);
            var lastColumn = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
            if (lastColumn == 0) return InvalidWorkbook("Employees", "The Employees worksheet has no headers.", "workbook.structure", "workbook.headers_missing");
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var issuesByRow = new SortedDictionary<int, List<EmployeeBulkUploadIssue>>();
            for (var c = 1; c <= lastColumn; c++)
            {
                diagnosticOperation = "excel_parser.read_header_cell";
                if (headerRow.Cell(c).HasFormula) Add(issuesByRow, 1, "Employees", "Column " + c, c, "cell.formula", "Formula cells are not accepted.", "Replace the formula with a plain header value.");
                var name = headerRow.Cell(c).GetString().Trim();
                if (string.IsNullOrEmpty(name)) { Add(issuesByRow, 1, "Employees", $"Column {c}", c, "header.empty", "A column header is blank.", "Remove blank columns or provide the template header."); continue; }
                if (!columns.TryAdd(name, c)) Add(issuesByRow, 1, name, name, c, "header.duplicate", "A column header is duplicated.", "Keep exactly one copy of each template header.");
                else if (!Headers.Contains(name, StringComparer.OrdinalIgnoreCase)) Add(issuesByRow, 1, name, name, c, "header.unknown", "This column is not supported by the employee template.", "Use the current Employee Bulk Upload template.");
            }
            foreach (var required in Headers)
                if (!columns.ContainsKey(required)) Add(issuesByRow, 1, required, required, 0, "header.missing", "A required template column is missing.", "Download a current template and retain its headers.");
            if (issuesByRow.ContainsKey(1)) return Result<EmployeeBulkUploadValidationResult>.Success(new(new(0, 0, 0, 0, [ToRow(1, issuesByRow[1])]), []));

            var lastRow = sheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 1;
            var count = Math.Max(0, lastRow - 1);
            if (count == 0) return InvalidWorkbook("Employees", "The workbook contains no employee rows.", "workbook.rows", "workbook.no_employee_rows");
            if (count > _maximumEmployeeRows) return InvalidWorkbook("Employees", $"The workbook exceeds the {_maximumEmployeeRows:N0} employee row limit.", "workbook.rows", "workbook.employee_row_limit");

            diagnosticStage = "workbook.row_validation";
            diagnosticOperation = "excel_parser.enumerate_rows";
            for (var r = 2; r <= lastRow; r++)
            {
                diagnosticRow = r;
                diagnosticOperation = "excel_parser.read_row_cells";
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var cell in sheet.Row(r).CellsUsed())
                {
                    var header = HeaderAt(columns, cell.Address.ColumnNumber);
                    if (cell.HasFormula) Add(issuesByRow, r, header, header, cell.Address.ColumnNumber, "cell.formula", "Formula cells are not accepted.", "Replace the formula with a plain value.");
                    if (cell.DataType == XLDataType.Number && header is not "Date of Birth" and not "Date of Joining *" and not "Employment Effective From *")
                        Add(issuesByRow, r, header, header, cell.Address.ColumnNumber, "cell.type", "This field must be stored as text.", "Format the cell as Text and enter the value again.");
                    if (cell.DataType is XLDataType.Error or XLDataType.TimeSpan ||
                        cell.DataType == XLDataType.Boolean && header is not "ESIC Applicable" and not "Gratuity" and not "Pension" and not "Same As Current Address")
                        Add(issuesByRow, r, header, header, cell.Address.ColumnNumber, "cell.type", "This cell type is not supported for the selected field.", "Enter a plain text value or a date in the supported format.");
                }
                ValidateRow(sheet, r, columns, issuesByRow);
            }
            diagnosticStage = "master_data.references";
            diagnosticOperation = "tenant_master_data.lookup";
            diagnosticRow = null;
            var validatedRows = await ValidateReferencesAsync(sheet, lastRow, columns, issuesByRow, tenantId, cancellationToken);
            var invalid = issuesByRow.Keys.Count(row => row > 1);
            IEnumerable<int> previewRowNumbers = includeValidRowsInPreview
                ? Enumerable.Range(2, count)
                : issuesByRow.Keys.Where(row => row > 1);
            var rows = previewRowNumbers
                .Select(r => ToRow(r, issuesByRow.GetValueOrDefault(r) ?? [])).ToList();
            return Result<EmployeeBulkUploadValidationResult>.Success(new(new(count, count - invalid, invalid, 0, rows), validatedRows), "Workbook validation completed.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            var safeOperation = ClassifySafeOperation(exception, diagnosticOperation, diagnosticResourceType, hostEnvironment?.ContentRootPath);
            return Result<EmployeeBulkUploadValidationResult>.Failure(ResultStatus.ValidationFailed,
                "The workbook is malformed or cannot be read as a valid XLSX file.",
                [new ValidationError("file", "The workbook is malformed or cannot be read as a valid XLSX file.")],
                diagnosticStage, ClassifyDiagnostic(exception), diagnosticRow,
                safeOperation.OperationCategory, safeOperation.ResourceType, SafeExceptionType(exception), SafeHResult(exception),
                safeOperation.InsideApplicationWorkspace);
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    private static string? InspectPackage(Stream stream)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
            if (archive.Entries.Count > 2000 || archive.Entries.Sum(e => e.Length) > 80L * 1024 * 1024) return "Workbook package exceeds safe resource limits.";
            if (!archive.Entries.Any(e => e.FullName == "[Content_Types].xml") || !archive.Entries.Any(e => e.FullName == "xl/workbook.xml")) return "File is not a valid XLSX workbook.";
            if (archive.Entries.Any(e => e.FullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase))) return "External workbook links are not allowed.";
            foreach (var entry in archive.Entries.Where(e => e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
            {
                using var reader = new StreamReader(entry.Open()); var xml = reader.ReadToEnd();
                if (xml.Contains("TargetMode=\"External\"", StringComparison.OrdinalIgnoreCase)) return "External workbook links are not allowed.";
            }
            return null;
        }
        catch (InvalidDataException) { return "File is not a valid XLSX workbook."; }
    }

    private void ValidateRow(IXLWorksheet sheet, int row, Dictionary<string, int> c, SortedDictionary<int, List<EmployeeBulkUploadIssue>> issues)
    {
        string V(string h) => sheet.Cell(row, c[h]).GetString().Trim();
        void Required(string h) { if (string.IsNullOrWhiteSpace(V(h))) Add(issues, row, h, h, c[h], "field.required", "A required value is missing.", "Enter a value in this cell."); }
        Required("First Name *"); Required("Last Name *"); Required("Aadhaar Number *"); Required("PAN *"); Required("Date of Joining *"); Required("Job Status *"); Required("Employment Effective From *"); Required("Department Code *"); Required("Designation Code *"); Required("Employment Change Reason *"); Required("Employment Type *");
        foreach (var h in new[] { "Date of Birth", "Date of Joining *", "Employment Effective From *" })
            if (V(h).Length > 0 && !TryDate(sheet.Cell(row, c[h]), out _)) Add(issues, row, h, h, c[h], "cell.date", "The date value is invalid or stored as an unsupported cell type.", "Enter an Excel date or an ISO date such as 2026-04-01.");
        foreach (var h in new[] { "Aadhaar Number *", "PAN *", "ESIC Number", "PF Number", "UAN Number" })
            if (sheet.Cell(row, c[h]).DataType == XLDataType.Number && !string.IsNullOrWhiteSpace(V(h))) Add(issues, row, h, h, c[h], "cell.identifier_numeric", "Identifier cells must be stored as text.", "Format the cell as Text and re-enter the identifier so leading zeroes are preserved.");
        if (V("Aadhaar Number *").Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(V("Aadhaar Number *"), @"^\d{12}$")) Add(issues, row, "Aadhaar Number *", "Aadhaar Number *", c["Aadhaar Number *"], "aadhaar.invalid", "Aadhaar format is invalid.", "Enter exactly 12 digits; the value is masked in preview.");
        if (V("PAN *").Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(V("PAN *"), @"^[A-Z]{5}\d{4}[A-Z]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) Add(issues, row, "PAN *", "PAN *", c["PAN *"], "pan.invalid", "PAN format is invalid.", "Enter 5 letters, 4 digits, and 1 letter; the value is masked in preview.");
        if (V("Job Status *").Length > 100) Add(issues, row, "JobStatus", "Job Status *", c["Job Status *"], "job_status.length", "Job status must not exceed 100 characters.", "Shorten the job status to 100 characters or fewer.");
        var personal = new EmployeePersonalDetailsRequest {
            Salutation = V("Salutation"), FirstName = V("First Name *"), MiddleName = V("Middle Name"), LastName = V("Last Name *"),
            Gender = ParseEnum(V("Gender"), Gender.Unspecified), BloodGroup = ParseEnum(V("Blood Group"), BloodGroup.Unspecified),
            MaritalStatus = ParseEnum(V("Marital Status"), MaritalStatus.Unspecified), Religion = V("Religion"), Caste = V("Caste"), Citizenship = V("Citizenship"),
            EsicApplicable = ParseYesNo(V("ESIC Applicable"), "ESIC Applicable", row, c, issues), EsicNumber = V("ESIC Number"), PfNumber = V("PF Number"),
            MediclaimNumber = V("Mediclaim Number"), UanNumber = V("UAN Number"), Gratuity = ParseYesNo(V("Gratuity"), "Gratuity", row, c, issues),
            Pension = ParseYesNo(V("Pension"), "Pension", row, c, issues), AadhaarNumber = V("Aadhaar Number *"), PanNumber = V("PAN *"), JobStatus = V("Job Status *")
        };
        CheckLength("Salutation", 20); CheckLength("Middle Name", 100); CheckLength("Religion", 100); CheckLength("Caste", 100);
        CheckLength("Citizenship", 100); CheckLength("ESIC Number", 50); CheckLength("PF Number", 50); CheckLength("Mediclaim Number", 50);
        CheckLength("UAN Number", 50); CheckLength("Official Email", 256); CheckLength("Personal Email", 256); CheckLength("Alternate Email", 256);
        CheckLength("Official Phone", 30); CheckLength("Personal Phone", 30); CheckLength("Emergency Number", 30);
        if (TryDate(sheet.Cell(row, c["Date of Joining *"]), out var joining)) personal.DateOfJoining = joining;
        if (TryDate(sheet.Cell(row, c["Date of Birth"]), out var dob)) personal.DateOfBirth = dob;
        foreach (var failure in personalValidator.Validate(personal).Errors) Add(issues, row, failure.PropertyName, HeaderForProperty(failure.PropertyName), c.GetValueOrDefault(HeaderForProperty(failure.PropertyName)), "request.validation", SafeMessage(failure.PropertyName), "Correct the value according to the template guidance.");
        var contact = new EmployeeContactRequest { OfficialEmail = V("Official Email"), PersonalEmail = V("Personal Email"), AlternateEmail = V("Alternate Email"), OfficialPhone = V("Official Phone"), PersonalPhone = V("Personal Phone"), EmergencyNumber = V("Emergency Number"), SameAsCurrentAddress = ParseYesNo(V("Same As Current Address"), "Same As Current Address", row, c, issues) };
        foreach (var failure in contactValidator.Validate(contact).Errors) Add(issues, row, failure.PropertyName, HeaderForProperty(failure.PropertyName), c.GetValueOrDefault(HeaderForProperty(failure.PropertyName)), "request.validation", SafeMessage(failure.PropertyName), "Correct the value according to the template guidance.");
        if (TryDate(sheet.Cell(row, c["Employment Effective From *"]), out var effective))
        {
            if (personal.DateOfJoining != effective) Add(issues, row, "EffectiveFrom", "Employment Effective From *", c["Employment Effective From *"], "employment.date_mismatch", "Employment effective date must match the date of joining.", "Use the same date in both columns.");
        }
        if (!Enum.TryParse<EmploymentChangeReason>(V("Employment Change Reason *"), true, out var reason) || reason != EmploymentChangeReason.NewJoining) Add(issues, row, "ChangeReason", "Employment Change Reason *", c["Employment Change Reason *"], "employment.reason", "Initial onboarding must use NewJoining.", "Enter NewJoining.");
        if (!Enum.TryParse<EmploymentType>(V("Employment Type *"), true, out var employmentType) || employmentType == EmploymentType.Unspecified) Add(issues, row, "EmploymentType", "Employment Type *", c["Employment Type *"], "employment.type", "Employment type is invalid.", "Choose a supported employment type.");
        ValidateEnum<Gender>("Gender"); ValidateEnum<BloodGroup>("Blood Group"); ValidateEnum<MaritalStatus>("Marital Status");

        void ValidateEnum<T>(string header) where T : struct, Enum
        {
            var value = V(header);
            if (value.Length > 0 && (!Enum.TryParse<T>(value, true, out var parsed) || !Enum.IsDefined(parsed)))
                Add(issues, row, header, header, c[header], "enum.invalid", "The selected value is not supported.", "Choose a value from the template dropdown.");
        }
        void CheckLength(string header, int maximum)
        {
            if (V(header).Length > maximum)
                Add(issues, row, header, header, c[header], "field.length", "The value exceeds the maximum supported length.", $"Shorten this value to {maximum} characters or fewer.");
        }
    }

    private async Task<IReadOnlyList<ValidatedEmployeeOnboardingRow>> ValidateReferencesAsync(IXLWorksheet sheet, int lastRow, Dictionary<string, int> c, SortedDictionary<int, List<EmployeeBulkUploadIssue>> issues, Guid tenantId, CancellationToken ct)
    {
        var departments = await db.Departments.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code }).ToListAsync(ct);
        var designations = await db.Designations.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code }).ToListAsync(ct);
        var holding = await db.HoldingCompanies.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code }).ToListAsync(ct);
        var lobs = await db.LinesOfBusiness.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code, x.HoldingCompanyId }).ToListAsync(ct);
        var organisations = await db.Organisations.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code }).ToListAsync(ct);
        var subDepartments = await db.SubDepartments.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code, x.DepartmentId }).ToListAsync(ct);
        var sections = await db.Sections.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code, x.SubDepartmentId }).ToListAsync(ct);
        var subSections = await db.SubSections.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code, x.SectionId }).ToListAsync(ct);
        var functions = await db.Functions.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code }).ToListAsync(ct);
        var subFunctions = await db.SubFunctions.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code, x.FunctionId }).ToListAsync(ct);
        var grades = await db.Grades.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code }).ToListAsync(ct);
        var employeeTypes = await db.EmployeeTypes.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code }).ToListAsync(ct);
        var workLocations = await db.WorkLocations.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code }).ToListAsync(ct);
        var costCenters = await db.CostCenters.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code }).ToListAsync(ct);
        var positionReasons = await db.PositionChangeReasons.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).Select(x => new { x.Id, x.Code }).ToListAsync(ct);
        var countries = await db.Countries.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Id, x.Code }).ToListAsync(ct);
        var states = await db.States.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Id, x.Code, x.CountryId }).ToListAsync(ct);
        var cities = await db.Cities.AsNoTracking().Where(x => x.IsActive).Select(x => new { x.Id, x.Code, x.StateId }).ToListAsync(ct);
        var codes = await db.EmployeeCodeConfigVersions.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive).ToListAsync(ct);
        var versionIds = codes.Select(x => x.Id).ToArray();
        var activeRules = await db.EmployeeCodeRules.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Conditions.Where(c => c.TenantId == tenantId))
            .Include(x => x.Segments.Where(s => s.TenantId == tenantId))
            .Where(x => x.TenantId == tenantId && !x.IsDeleted && x.Status == EmployeeCodeRuleStatus.Active && x.EmployeeCodeConfigVersionId.HasValue && versionIds.Contains(x.EmployeeCodeConfigVersionId.Value))
            .ToListAsync(ct);
        var tenantEmployees = await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId)
            .Select(x => new { x.Id, x.EmployeeCode, x.Status }).ToListAsync(ct);
        var existingCodes = tenantEmployees.Where(x => x.EmployeeCode != null).Select(x => x.EmployeeCode!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tenantEmployeesByCode = tenantEmployees.Where(x => x.EmployeeCode != null).ToDictionary(x => x.EmployeeCode!, StringComparer.OrdinalIgnoreCase);
        var reservedEmployeeCodes = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        var nextSimpleCodeByVersion = new Dictionary<Guid, long>();
        var newCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var r = 2; r <= lastRow; r++)
        {
            string val(string h) => sheet.Cell(r, c[h]).GetString().Trim();
            void Check(string header, IEnumerable<string> allowed)
            {
                var code = val(header);
                if (code.Length > 0 && !allowed.Contains(code, StringComparer.OrdinalIgnoreCase)) Add(issues, r, header, header, c[header], "master.invalid", "The code is unavailable, inactive, or outside this tenant.", "Choose an active code from the current tenant template.");
            }
            var department = departments.FirstOrDefault(x => x.Code.Equals(val("Department Code *"), StringComparison.OrdinalIgnoreCase));
            var designation = designations.FirstOrDefault(x => x.Code.Equals(val("Designation Code *"), StringComparison.OrdinalIgnoreCase));
            if (val("Department Code *").Length > 0 && department is null) Add(issues, r, "DepartmentId", "Department Code *", c["Department Code *"], "master.invalid", "The code is unavailable, inactive, or outside this tenant.", "Choose an active code from the current tenant template.");
            if (val("Designation Code *").Length > 0 && designation is null) Add(issues, r, "DesignationId", "Designation Code *", c["Designation Code *"], "master.invalid", "The code is unavailable, inactive, or outside this tenant.", "Choose an active code from the current tenant template.");
            Check("Holding Company Code", holding.Select(x => x.Code)); Check("Organization Code", organisations.Select(x => x.Code)); Check("Function Code", functions.Select(x => x.Code)); Check("Grade Code", grades.Select(x => x.Code));
            Check("Employee Type Code", employeeTypes.Select(x => x.Code)); Check("Work Location Code", workLocations.Select(x => x.Code)); Check("Cost Center Code", costCenters.Select(x => x.Code)); Check("Position Change Reason Code", positionReasons.Select(x => x.Code));
            Check("Country Location Code", countries.Select(x => x.Code));
            var holdingRef = holding.FirstOrDefault(x => x.Code.Equals(val("Holding Company Code"), StringComparison.OrdinalIgnoreCase));
            var lobRef = lobs.FirstOrDefault(x => x.Code.Equals(val("LOB Code"), StringComparison.OrdinalIgnoreCase));
            var organisationRef = organisations.FirstOrDefault(x => x.Code.Equals(val("Organization Code"), StringComparison.OrdinalIgnoreCase));
            Check("LOB Code", lobs.Select(x => x.Code));
            if (lobRef is not null && lobRef.HoldingCompanyId != holdingRef?.Id) Add(issues, r, "LobId", "LOB Code", c["LOB Code"], "master.parent_mismatch", "The LOB does not belong to the selected holding company.", "Choose an LOB under the selected holding company.");
            var subDeptRef = subDepartments.FirstOrDefault(x => x.Code.Equals(val("Sub Department Code"), StringComparison.OrdinalIgnoreCase)); Check("Sub Department Code", subDepartments.Select(x => x.Code));
            if (subDeptRef is not null && department is not null && subDeptRef.DepartmentId != department.Id) Add(issues, r, "SubDepartmentId", "Sub Department Code", c["Sub Department Code"], "master.parent_mismatch", "The sub department does not belong to the selected department.", "Choose a sub department under the selected department.");
            var sectionRef = sections.FirstOrDefault(x => x.Code.Equals(val("Section Code"), StringComparison.OrdinalIgnoreCase)); Check("Section Code", sections.Select(x => x.Code));
            if (sectionRef is not null && sectionRef.SubDepartmentId != subDeptRef?.Id)
                Add(issues, r, "SectionId", "Section Code", c["Section Code"], "master.parent_mismatch", "The section does not match the selected sub department.", "Choose a section under the selected sub department.");
            var subSectionRef = subSections.FirstOrDefault(x => x.Code.Equals(val("Sub Section Code"), StringComparison.OrdinalIgnoreCase)); Check("Sub Section Code", subSections.Select(x => x.Code));
            if (subSectionRef is not null && subSectionRef.SectionId != sectionRef?.Id) Add(issues, r, "SubSectionId", "Sub Section Code", c["Sub Section Code"], "master.parent_mismatch", "The sub section does not belong to the selected section.", "Choose a sub section under the selected section.");
            var subFunctionRef = subFunctions.FirstOrDefault(x => x.Code.Equals(val("Sub Function Code"), StringComparison.OrdinalIgnoreCase)); Check("Sub Function Code", subFunctions.Select(x => x.Code));
            var functionRef = functions.FirstOrDefault(x => x.Code.Equals(val("Function Code"), StringComparison.OrdinalIgnoreCase));
            if (subFunctionRef is not null && subFunctionRef.FunctionId != functionRef?.Id) Add(issues, r, "SubFunctionId", "Sub Function Code", c["Sub Function Code"], "master.parent_mismatch", "The sub function does not belong to the selected function.", "Choose a sub function under the selected function.");
            Check("Birth Country Code", countries.Select(x => x.Code)); Check("Birth State Code", states.Select(x => x.Code)); Check("Birth City Code", cities.Select(x => x.Code));
            var countryRef = countries.FirstOrDefault(x => x.Code.Equals(val("Birth Country Code"), StringComparison.OrdinalIgnoreCase)); var stateRef = states.FirstOrDefault(x => x.Code.Equals(val("Birth State Code"), StringComparison.OrdinalIgnoreCase));
            if (stateRef is not null && (countryRef is null || stateRef.CountryId != countryRef.Id)) Add(issues, r, "BirthStateId", "Birth State Code", c["Birth State Code"], "master.parent_mismatch", "The state does not belong to the selected birth country.", "Choose a state under the selected birth country.");
            var cityRef = cities.FirstOrDefault(x => x.Code.Equals(val("Birth City Code"), StringComparison.OrdinalIgnoreCase));
            if (cityRef is not null && (stateRef is null || cityRef.StateId != stateRef.Id)) Add(issues, r, "BirthCityId", "Birth City Code", c["Birth City Code"], "master.parent_mismatch", "The city does not belong to the selected birth state.", "Choose a city under the selected birth state.");
            var gradeRef = grades.FirstOrDefault(x => x.Code.Equals(val("Grade Code"), StringComparison.OrdinalIgnoreCase));
            var employeeTypeRef = employeeTypes.FirstOrDefault(x => x.Code.Equals(val("Employee Type Code"), StringComparison.OrdinalIgnoreCase));
            var countryRefLocation = countries.FirstOrDefault(x => x.Code.Equals(val("Country Location Code"), StringComparison.OrdinalIgnoreCase));
            var workLocationRef = workLocations.FirstOrDefault(x => x.Code.Equals(val("Work Location Code"), StringComparison.OrdinalIgnoreCase));
            var costCenterRef = costCenters.FirstOrDefault(x => x.Code.Equals(val("Cost Center Code"), StringComparison.OrdinalIgnoreCase));
            var positionReasonRef = positionReasons.FirstOrDefault(x => x.Code.Equals(val("Position Change Reason Code"), StringComparison.OrdinalIgnoreCase));
            var effectiveHistory = new EmploymentChangeRequest {
                EffectiveFrom = TryDate(sheet.Cell(r, c["Employment Effective From *"]), out var effective) ? effective : default,
                HoldingCompanyId = holdingRef?.Id, LobId = lobRef?.Id,
                OrganisationId = organisationRef?.Id, DepartmentId = department?.Id, SubDepartmentId = subDeptRef?.Id,
                SectionId = sectionRef?.Id, SubSectionId = subSectionRef?.Id, FunctionId = functionRef?.Id, SubFunctionId = subFunctionRef?.Id,
                GradeId = gradeRef?.Id, DesignationId = designation?.Id, EmployeeTypeId = employeeTypeRef?.Id, CountryLocationId = countryRefLocation?.Id,
                WorkLocationId = workLocationRef?.Id, CostCenterId = costCenterRef?.Id, PositionChangeReasonId = positionReasonRef?.Id,
                ChangeReason = ParseEnum(val("Employment Change Reason *"), EmploymentChangeReason.Unspecified),
                EmploymentType = ParseEnum(val("Employment Type *"), EmploymentType.Unspecified), EmploymentStatus = ParseEnum(val("Employment Status"), EmployeeStatus.Active),
                EmployeeCode = val("Manual Employee Code"), ChangeReasonDescription = val("Change Reason Description"), BusinessRole = val("Business Role"), GradeLevel = val("Grade Level"), CareerGroup = val("Career Group")
            };
            if (val("Work Location Code").Length > 0 && val("Country Location Code").Length == 0)
                Add(issues, r, "CountryLocationId", "Country Location Code", c["Country Location Code"], "master.required", "Country is required when a work location is selected.", "Select a country together with the work location.");
            if (effectiveHistory.EffectiveFrom != default)
                foreach (var failure in employmentValidator.Validate(effectiveHistory).Errors)
                {
                    var header = HeaderForProperty(failure.PropertyName);
                    if (!issues.TryGetValue(r, out var rowIssues) || !rowIssues.Any(x => x.FieldName == failure.PropertyName))
                        Add(issues, r, failure.PropertyName, header, c.GetValueOrDefault(header), "request.validation", SafeMessage(failure.PropertyName), "Correct the value according to the template guidance.");
                }
            foreach (var header in new[] { "Employment Status" })
                if (val(header).Length > 0 && !Enum.TryParse<EmployeeStatus>(val(header), true, out _)) Add(issues, r, header, header, c[header], "enum.invalid", "Employment status is not supported.", "Choose a supported employment status.");
            var manual = val("Manual Employee Code");
            if (manual.Length > 0)
            {
                if (existingCodes.Contains(manual) || !newCodes.Add(manual)) Add(issues, r, "EmployeeCode", "Manual Employee Code", c["Manual Employee Code"], "employee_code.duplicate", "The employee code already exists or is duplicated in this file.", "Use a unique code or leave blank when automatic assignment is configured.");
            }
            var date = TryDate(sheet.Cell(r, c["Date of Joining *"]), out var d) ? d : DateOnly.MinValue;
            var config = codes.Where(x => date != DateOnly.MinValue && x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date)).ToList();
            if (config.Count != 1) Add(issues, r, "EmployeeCode", "Date of Joining *", c["Date of Joining *"], "employee_code.configuration", "No unique employee-code configuration applies on the joining date.", "Ask an administrator to configure employee-code assignment for this date.");
            else if (config[0].AssignmentMode == EmployeeCodeAssignmentMode.Manual && manual.Length == 0) Add(issues, r, "EmployeeCode", "Manual Employee Code", c["Manual Employee Code"], "employee_code.required", "Manual employee-code assignment is configured.", "Enter a unique manual employee code.");
            else if (config[0].AssignmentMode == EmployeeCodeAssignmentMode.Auto && config[0].GenerationMethod is null)
                Add(issues, r, "EmployeeCode", "Date of Joining *", c["Date of Joining *"], "employee_code.configuration", "Automatic employee-code configuration has no generation method.", "Ask an administrator to correct employee-code configuration.");
            else if (config[0].AssignmentMode == EmployeeCodeAssignmentMode.Auto && config[0].GenerationMethod == EmployeeCodeGenerationMethod.Simple && manual.Length > 0)
                Add(issues, r, "EmployeeCode", "Manual Employee Code", c["Manual Employee Code"], "employee_code.manual_not_allowed", "Manual employee code is ignored when automatic assignment is configured.", "Clear this cell; the employee code will be generated.");
            else if (!issues.ContainsKey(r) && config[0].AssignmentMode == EmployeeCodeAssignmentMode.Auto && config[0].GenerationMethod == EmployeeCodeGenerationMethod.Simple)
            {
                var nextNumber = nextSimpleCodeByVersion.GetValueOrDefault(config[0].Id, Math.Max(1L, config[0].NextNumber));
                var prefix = (config[0].Prefix ?? "EMP").Trim().ToUpperInvariant();
                var separator = config[0].Separator ?? string.Empty;
                var padding = Math.Clamp(config[0].Padding, 0, 10);
                var candidate = prefix + separator + nextNumber.ToString(padding == 0 ? string.Empty : $"D{padding}");
                nextSimpleCodeByVersion[config[0].Id] = nextNumber == long.MaxValue ? long.MaxValue : nextNumber + 1;
                if (!reservedEmployeeCodes.Add(candidate))
                    Add(issues, r, "EmployeeCode", "Date of Joining *", c["Date of Joining *"], "employee_code.generated_duplicate", "The next automatic employee code is already in use.", "Ask an administrator to advance the automatic employee-code sequence, then revalidate the workbook.");
            }
            else if (config[0].AssignmentMode == EmployeeCodeAssignmentMode.Auto && config[0].GenerationMethod == EmployeeCodeGenerationMethod.RuleBased)
            {
                if (manual.Length > 0) Add(issues, r, "EmployeeCode", "Manual Employee Code", c["Manual Employee Code"], "employee_code.manual_not_allowed", "Manual employee code is ignored when automatic assignment is configured.", "Clear this cell; the employee code will be generated.");
                var values = new Dictionary<EmployeeCodeConditionField, string?>
                {
                    [EmployeeCodeConditionField.HoldingCompany] = holdingRef?.Code, [EmployeeCodeConditionField.Lob] = lobRef?.Code,
                    [EmployeeCodeConditionField.Organisation] = organisationRef?.Code, [EmployeeCodeConditionField.Department] = department?.Code,
                    [EmployeeCodeConditionField.SubDepartment] = subDeptRef?.Code, [EmployeeCodeConditionField.Section] = sectionRef?.Code,
                    [EmployeeCodeConditionField.SubSection] = subSectionRef?.Code, [EmployeeCodeConditionField.Function] = functionRef?.Code,
                    [EmployeeCodeConditionField.SubFunction] = subFunctionRef?.Code, [EmployeeCodeConditionField.Grade] = gradeRef?.Code,
                    [EmployeeCodeConditionField.Designation] = designation?.Code, [EmployeeCodeConditionField.EmployeeType] = employeeTypeRef?.Code,
                    [EmployeeCodeConditionField.Country] = countryRefLocation?.Code, [EmployeeCodeConditionField.WorkLocation] = workLocationRef?.Code,
                    [EmployeeCodeConditionField.CostCenter] = costCenterRef?.Code
                };
                var ids = new Dictionary<EmployeeCodeConditionField, Guid?>
                {
                    [EmployeeCodeConditionField.HoldingCompany] = effectiveHistory.HoldingCompanyId, [EmployeeCodeConditionField.Lob] = effectiveHistory.LobId,
                    [EmployeeCodeConditionField.Organisation] = effectiveHistory.OrganisationId, [EmployeeCodeConditionField.Department] = effectiveHistory.DepartmentId,
                    [EmployeeCodeConditionField.SubDepartment] = effectiveHistory.SubDepartmentId, [EmployeeCodeConditionField.Section] = effectiveHistory.SectionId,
                    [EmployeeCodeConditionField.SubSection] = effectiveHistory.SubSectionId, [EmployeeCodeConditionField.Function] = effectiveHistory.FunctionId,
                    [EmployeeCodeConditionField.SubFunction] = effectiveHistory.SubFunctionId, [EmployeeCodeConditionField.Grade] = effectiveHistory.GradeId,
                    [EmployeeCodeConditionField.Designation] = effectiveHistory.DesignationId, [EmployeeCodeConditionField.EmployeeType] = effectiveHistory.EmployeeTypeId,
                    [EmployeeCodeConditionField.Country] = effectiveHistory.CountryLocationId,
                    [EmployeeCodeConditionField.WorkLocation] = effectiveHistory.WorkLocationId, [EmployeeCodeConditionField.CostCenter] = effectiveHistory.CostCenterId
                };
                var versionRules = activeRules.Where(x => x.EmployeeCodeConfigVersionId == config[0].Id).ToList();
                var matched = new EmployeeCodeRuleMatcher().Match(versionRules, values, ids);
                if (matched is null)
                    Add(issues, r, "EmployeeCode", "Department Code *", c["Department Code *"], "employee_code.rule_unmatched", "No active employee-code rule matches these employment values.", "Choose employment values covered by an active code rule or ask an administrator to configure one.");
                else
                {
                    var segments = Enum.GetValues<EmployeeCodeSegmentType>().ToDictionary(x => x, x => (string?)null);
                    foreach (var pair in values)
                    {
                        var segment = pair.Key switch
                        {
                            EmployeeCodeConditionField.HoldingCompany => EmployeeCodeSegmentType.HoldingCompanyCode, EmployeeCodeConditionField.Lob => EmployeeCodeSegmentType.LobCode,
                            EmployeeCodeConditionField.Organisation => EmployeeCodeSegmentType.OrganisationCode, EmployeeCodeConditionField.Department => EmployeeCodeSegmentType.DepartmentCode,
                            EmployeeCodeConditionField.SubDepartment => EmployeeCodeSegmentType.SubDepartmentCode, EmployeeCodeConditionField.Section => EmployeeCodeSegmentType.SectionCode,
                            EmployeeCodeConditionField.SubSection => EmployeeCodeSegmentType.SubSectionCode, EmployeeCodeConditionField.Function => EmployeeCodeSegmentType.FunctionCode,
                            EmployeeCodeConditionField.SubFunction => EmployeeCodeSegmentType.SubFunctionCode, EmployeeCodeConditionField.Grade => EmployeeCodeSegmentType.GradeCode,
                            EmployeeCodeConditionField.Designation => EmployeeCodeSegmentType.DesignationCode, EmployeeCodeConditionField.EmployeeType => EmployeeCodeSegmentType.EmployeeTypeCode,
                            EmployeeCodeConditionField.Country => EmployeeCodeSegmentType.CountryCode, EmployeeCodeConditionField.WorkLocation => EmployeeCodeSegmentType.WorkLocationCode,
                            EmployeeCodeConditionField.CostCenter => EmployeeCodeSegmentType.CostCenterCode, _ => (EmployeeCodeSegmentType?)null
                        };
                        if (segment.HasValue) segments[segment.Value] = pair.Value;
                    }
                    var rendered = new EmployeeCodeRenderer().Render(matched, new EmployeeCodeGenerationContext(date, values, segments), 1, config[0].Separator);
                    if (rendered.Error is not null) Add(issues, r, "EmployeeCode", "Department Code *", c["Department Code *"], "employee_code.rule_invalid", "The matched employee-code rule cannot render this row.", "Ask an administrator to complete the rule's required code segments.");
                }
            }
            else if (config[0].AssignmentMode == EmployeeCodeAssignmentMode.Auto && config[0].GenerationMethod != EmployeeCodeGenerationMethod.Simple)
                Add(issues, r, "EmployeeCode", "Date of Joining *", c["Date of Joining *"], "employee_code.configuration", "Employee-code generation method is unsupported.", "Ask an administrator to correct employee-code configuration.");

        }
        var sameFile = Enumerable.Range(2, lastRow - 1).Select(r => sheet.Cell(r, c["Manual Employee Code"]).GetString().Trim()).Where(x => x.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managerEmploymentCache = new Dictionary<(Guid EmployeeId, DateOnly EffectiveDate), bool>();
        foreach (var r in Enumerable.Range(2, lastRow - 1))
        {
            var manager = sheet.Cell(r, c["Manager Employee Code"]).GetString().Trim();
            if (manager.Length == 0) continue;
            if (sameFile.Contains(manager)) { Add(issues, r, "ManagerId", "Manager Employee Code", c["Manager Employee Code"], "manager.same_file", "Managers must be existing employees; same-file references are not supported.", "Use the employee code of an existing active employee."); continue; }
            if (!tenantEmployeesByCode.TryGetValue(manager, out var managerRecord) || managerRecord.Status != EmployeeStatus.Active)
            {
                Add(issues, r, "ManagerId", "Manager Employee Code", c["Manager Employee Code"], "manager.invalid", "Manager is not an active employee in this tenant.", "Enter an existing active employee code from this tenant.");
                continue;
            }
            var managerDate = TryDate(sheet.Cell(r, c["Date of Joining *"]), out var parsedJoining) ? parsedJoining : DateOnly.MinValue;
            var cacheKey = (managerRecord.Id, managerDate);
            if (!managerEmploymentCache.TryGetValue(cacheKey, out var managerIsEligible))
            {
                var managerHistory = managerDate == DateOnly.MinValue ? new List<EmployeeStatus>() : await db.EmployeeEmploymentHistory.IgnoreQueryFilters().AsNoTracking()
                    .Where(h => h.TenantId == tenantId && h.EmployeeId == managerRecord.Id && !h.IsSuperseded &&
                                h.EffectiveFrom <= managerDate && (h.EffectiveTo == null || h.EffectiveTo >= managerDate))
                    .Select(h => h.EmploymentStatus).ToListAsync(ct);
                managerIsEligible = managerHistory.Count == 1 && managerHistory[0] == EmployeeStatus.Active;
                managerEmploymentCache[cacheKey] = managerIsEligible;
            }
            if (!managerIsEligible)
                Add(issues, r, "ManagerId", "Manager Employee Code", c["Manager Employee Code"], "manager.inactive_employment", "Manager is not actively employed on the joining date.", "Choose an employee with one active employment record for this date.");
        }
        var existing = await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId).Select(x => new { x.AadhaarNumber, x.PanNumber, x.Email }).ToListAsync(ct);
        var existingContacts = await db.EmployeeContacts.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId)
            .Select(x => new { x.OfficialEmail, x.PersonalEmail, x.AlternateEmail }).ToListAsync(ct);
        var existingAadhaar = existing.Where(x => !string.IsNullOrEmpty(x.AadhaarNumber)).Select(x => x.AadhaarNumber!).ToHashSet(StringComparer.Ordinal);
        var existingPan = existing.Where(x => !string.IsNullOrEmpty(x.PanNumber)).Select(x => x.PanNumber!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingEmails = existing.Select(x => x.Email)
            .Concat(existingContacts.SelectMany(x => new[] { x.OfficialEmail, x.PersonalEmail, x.AlternateEmail }))
            .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenAadhaar = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var seenPan = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var seenEmail = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Enumerable.Range(2, lastRow - 1))
        {
            var aadhaar = sheet.Cell(r, c["Aadhaar Number *"]).GetString().Trim(); var pan = sheet.Cell(r, c["PAN *"]).GetString().Trim();
            if (aadhaar.Length > 0 && (existingAadhaar.Contains(aadhaar) || !seenAadhaar.Add(aadhaar))) Add(issues, r, "AadhaarNumber", "Aadhaar Number *", c["Aadhaar Number *"], "employee.duplicate", "This identifier is already used by an existing employee or another file row.", "Verify the employee record; the identifier is not shown in preview.");
            if (pan.Length > 0 && (existingPan.Contains(pan) || !seenPan.Add(pan))) Add(issues, r, "PanNumber", "PAN *", c["PAN *"], "employee.duplicate", "This identifier is already used by an existing employee or another file row.", "Verify the employee record; the identifier is not shown in preview.");
            foreach (var header in new[] { "Official Email", "Personal Email", "Alternate Email" })
            {
                var email = sheet.Cell(r, c[header]).GetString().Trim();
                if (email.Length == 0) continue;
                if (existingEmails.Contains(email) || !seenEmail.Add(email))
                    Add(issues, r, "OfficialEmail", header, c[header], "employee.duplicate", "This email is already used by an existing employee or another file row.", "Use a unique email address.");
            }
        }

        var canonicalRows = new List<ValidatedEmployeeOnboardingRow>();
        foreach (var r in Enumerable.Range(2, lastRow - 1))
        {
            if (issues.ContainsKey(r)) continue;
            string val(string h) => sheet.Cell(r, c[h]).GetString().Trim();
            TEnum enumValue<TEnum>(string header, TEnum fallback) where TEnum : struct, Enum => ParseEnum(val(header), fallback);
            Guid? id<TMaster>(IEnumerable<TMaster> records, string header, Func<TMaster, string> code, Func<TMaster, Guid> key)
            {
                var value = val(header);
                if (value.Length == 0) return null;
                var record = records.FirstOrDefault(x => code(x).Equals(value, StringComparison.OrdinalIgnoreCase));
                return record is null ? null : key(record);
            }

            var joining = TryDate(sheet.Cell(r, c["Date of Joining *"]), out var joinDate) ? joinDate : default;
            var personal = new EmployeePersonalDetailsRequest
            {
                Salutation = NullIfEmpty(val("Salutation")), FirstName = val("First Name *"), MiddleName = NullIfEmpty(val("Middle Name")), LastName = val("Last Name *"),
                DateOfBirth = TryDate(sheet.Cell(r, c["Date of Birth"]), out var birthDate) ? birthDate : null,
                Gender = enumValue("Gender", Gender.Unspecified), BloodGroup = enumValue("Blood Group", BloodGroup.Unspecified), MaritalStatus = enumValue("Marital Status", MaritalStatus.Unspecified),
                BirthCountryId = id(countries, "Birth Country Code", x => x.Code, x => x.Id),
                BirthStateId = id(states, "Birth State Code", x => x.Code, x => x.Id),
                BirthCityId = id(cities, "Birth City Code", x => x.Code, x => x.Id),
                Religion = NullIfEmpty(val("Religion")), Caste = NullIfEmpty(val("Caste")), Citizenship = NullIfEmpty(val("Citizenship")),
                EsicApplicable = val("ESIC Applicable").Equals("Yes", StringComparison.OrdinalIgnoreCase) || val("ESIC Applicable").Equals("True", StringComparison.OrdinalIgnoreCase),
                EsicNumber = NullIfEmpty(val("ESIC Number")), PfNumber = NullIfEmpty(val("PF Number")), MediclaimNumber = NullIfEmpty(val("Mediclaim Number")), UanNumber = NullIfEmpty(val("UAN Number")),
                Gratuity = val("Gratuity").Equals("Yes", StringComparison.OrdinalIgnoreCase) || val("Gratuity").Equals("True", StringComparison.OrdinalIgnoreCase),
                Pension = val("Pension").Equals("Yes", StringComparison.OrdinalIgnoreCase) || val("Pension").Equals("True", StringComparison.OrdinalIgnoreCase),
                AadhaarNumber = val("Aadhaar Number *"), PanNumber = val("PAN *"), DateOfJoining = joining, JobStatus = val("Job Status *")
            };
            var contact = new EmployeeContactRequest
            {
                OfficialEmail = NullIfEmpty(val("Official Email")), PersonalEmail = NullIfEmpty(val("Personal Email")), AlternateEmail = NullIfEmpty(val("Alternate Email")),
                OfficialPhone = NullIfEmpty(val("Official Phone")), PersonalPhone = NullIfEmpty(val("Personal Phone")), EmergencyNumber = NullIfEmpty(val("Emergency Number")),
                SameAsCurrentAddress = val("Same As Current Address").Equals("Yes", StringComparison.OrdinalIgnoreCase) || val("Same As Current Address").Equals("True", StringComparison.OrdinalIgnoreCase)
            };
            var initial = new EmploymentChangeRequest
            {
                EmployeeCode = NullIfEmpty(val("Manual Employee Code")), EffectiveFrom = TryDate(sheet.Cell(r, c["Employment Effective From *"]), out var effective) ? effective : default,
                HoldingCompanyId = id(holding, "Holding Company Code", x => x.Code, x => x.Id), LobId = id(lobs, "LOB Code", x => x.Code, x => x.Id),
                OrganisationId = id(organisations, "Organization Code", x => x.Code, x => x.Id), DepartmentId = id(departments, "Department Code *", x => x.Code, x => x.Id),
                SubDepartmentId = id(subDepartments, "Sub Department Code", x => x.Code, x => x.Id), SectionId = id(sections, "Section Code", x => x.Code, x => x.Id),
                SubSectionId = id(subSections, "Sub Section Code", x => x.Code, x => x.Id), FunctionId = id(functions, "Function Code", x => x.Code, x => x.Id),
                SubFunctionId = id(subFunctions, "Sub Function Code", x => x.Code, x => x.Id), GradeId = id(grades, "Grade Code", x => x.Code, x => x.Id),
                DesignationId = id(designations, "Designation Code *", x => x.Code, x => x.Id), EmployeeTypeId = id(employeeTypes, "Employee Type Code", x => x.Code, x => x.Id),
                CountryLocationId = id(countries, "Country Location Code", x => x.Code, x => x.Id), WorkLocationId = id(workLocations, "Work Location Code", x => x.Code, x => x.Id),
                CostCenterId = id(costCenters, "Cost Center Code", x => x.Code, x => x.Id), PositionChangeReasonId = id(positionReasons, "Position Change Reason Code", x => x.Code, x => x.Id),
                ManagerId = tenantEmployeesByCode.TryGetValue(val("Manager Employee Code"), out var manager) ? manager.Id : null,
                ChangeReason = enumValue("Employment Change Reason *", EmploymentChangeReason.Unspecified), ChangeReasonDescription = NullIfEmpty(val("Change Reason Description")),
                BusinessRole = NullIfEmpty(val("Business Role")), GradeLevel = NullIfEmpty(val("Grade Level")), CareerGroup = NullIfEmpty(val("Career Group")),
                EmploymentType = enumValue("Employment Type *", EmploymentType.Unspecified), EmploymentStatus = enumValue("Employment Status", EmployeeStatus.Active)
            };
            canonicalRows.Add(new(r, new EmployeeOnboardingRequest { PersonalDetails = personal, Contact = contact, InitialEmployment = initial }));
        }
        return canonicalRows;
    }

    private static bool TryDate(IXLCell cell, out DateOnly date)
    {
        date = default;
        if (cell.DataType == XLDataType.DateTime) { date = DateOnly.FromDateTime(cell.GetDateTime()); return true; }
        if (cell.DataType == XLDataType.Text && DateOnly.TryParseExact(cell.GetString().Trim(), ["yyyy-MM-dd", "yyyy/MM/dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        return false;
    }
    private static T ParseEnum<T>(string value, T fallback) where T : struct, Enum => Enum.TryParse<T>(value, true, out var parsed) ? parsed : fallback;
    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
    private static bool ParseYesNo(string value, string header, int row, Dictionary<string, int> columns, SortedDictionary<int, List<EmployeeBulkUploadIssue>> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("No", StringComparison.OrdinalIgnoreCase) || value.Equals("False", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Equals("Yes", StringComparison.OrdinalIgnoreCase) || value.Equals("True", StringComparison.OrdinalIgnoreCase)) return true;
        Add(issues, row, header, header, columns[header], "boolean.invalid", "Enter Yes or No.", "Choose Yes or No from the template dropdown."); return false;
    }
    private static string HeaderForProperty(string property) => property switch
    {
        "Salutation" => "Salutation", "FirstName" => "First Name *", "MiddleName" => "Middle Name", "LastName" => "Last Name *",
        "DateOfBirth" => "Date of Birth", "Gender" => "Gender", "BloodGroup" => "Blood Group", "MaritalStatus" => "Marital Status",
        "BirthCountryId" => "Birth Country Code", "BirthStateId" => "Birth State Code", "BirthCityId" => "Birth City Code",
        "Religion" => "Religion", "Caste" => "Caste", "Citizenship" => "Citizenship", "EsicApplicable" => "ESIC Applicable", "EsicNumber" => "ESIC Number",
        "PfNumber" => "PF Number", "MediclaimNumber" => "Mediclaim Number", "UanNumber" => "UAN Number", "AadhaarNumber" => "Aadhaar Number *", "PanNumber" => "PAN *",
        "DateOfJoining" => "Date of Joining *", "JobStatus" => "Job Status *", "OfficialEmail" => "Official Email", "PersonalEmail" => "Personal Email",
        "AlternateEmail" => "Alternate Email", "OfficialPhone" => "Official Phone", "PersonalPhone" => "Personal Phone", "EmergencyNumber" => "Emergency Number",
        "SameAsCurrentAddress" => "Same As Current Address", "EmployeeCode" => "Manual Employee Code", "EffectiveFrom" => "Employment Effective From *",
        "HoldingCompanyId" => "Holding Company Code", "LobId" => "LOB Code", "OrganisationId" => "Organization Code", "DepartmentId" => "Department Code *",
        "SubDepartmentId" => "Sub Department Code", "SectionId" => "Section Code", "SubSectionId" => "Sub Section Code", "FunctionId" => "Function Code",
        "SubFunctionId" => "Sub Function Code", "GradeId" => "Grade Code", "DesignationId" => "Designation Code *", "EmployeeTypeId" => "Employee Type Code",
        "CountryLocationId" => "Country Location Code", "WorkLocationId" => "Work Location Code", "CostCenterId" => "Cost Center Code", "ManagerId" => "Manager Employee Code",
        "PositionChangeReasonId" => "Position Change Reason Code", "ChangeReason" => "Employment Change Reason *", "EmploymentType" => "Employment Type *",
        "EmploymentStatus" => "Employment Status", "ChangeReasonDescription" => "Change Reason Description", "BusinessRole" => "Business Role", "GradeLevel" => "Grade Level", "CareerGroup" => "Career Group",
        _ => property
    };
    private static string SafeMessage(string property) => property.Contains("Aadhaar", StringComparison.OrdinalIgnoreCase) ? "Aadhaar format is invalid." : property.Contains("Pan", StringComparison.OrdinalIgnoreCase) ? "PAN format is invalid." : "The value does not satisfy the employee field rules.";
    private static string HeaderAt(Dictionary<string, int> c, int col) => c.FirstOrDefault(x => x.Value == col).Key ?? "Employees";
    private static void Add(SortedDictionary<int, List<EmployeeBulkUploadIssue>> errors, int row, string field, string column, int col, string code, string message, string guidance)
    { if (!errors.TryGetValue(row, out var list)) errors[row] = list = []; list.Add(new(field, column, col, code, message, guidance)); }
    private static EmployeeBulkUploadPreviewRow ToRow(int row, IReadOnlyList<EmployeeBulkUploadIssue> issues) => new(row, issues.Count == 0 ? "Valid" : "Invalid", issues);

    private static string ClassifyDiagnostic(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "workbook.access_denied",
        System.Data.Common.DbException => "master_data.database_error",
        TimeoutException => "master_data.timeout",
        InvalidDataException => "workbook.invalid_data",
        IOException => "workbook.io_error",
        ArgumentException => "workbook.invalid_structure",
        _ => "validation.unexpected_error"
    };

    private static string SafeExceptionType(Exception exception) => exception switch
    {
        UnauthorizedAccessException => nameof(UnauthorizedAccessException),
        System.Data.Common.DbException => "DbException",
        TimeoutException => nameof(TimeoutException),
        InvalidDataException => nameof(InvalidDataException),
        IOException => nameof(IOException),
        ArgumentException => nameof(ArgumentException),
        _ => "OtherException"
    };

    private static string SafeHResult(Exception exception) => $"0x{unchecked((uint)exception.HResult):X8}";

    private static (string OperationCategory, string ResourceType, bool? InsideApplicationWorkspace) ClassifySafeOperation(
        Exception exception, string fallbackOperation, string fallbackResourceType, string? applicationContentRoot)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            var frames = new StackTrace(current, false).GetFrames() ?? [];
            foreach (var frame in frames)
            {
                var method = frame.GetMethod();
                var typeName = method?.DeclaringType?.FullName;
                var methodName = method?.Name;
                if (typeName == "System.IO.Path" && methodName == "GetTempFileName")
                    return ("openxml.temp_file_create", "file", IsWithinApplicationContentRoot(Path.GetTempPath(), applicationContentRoot));
                if (typeName == "DocumentFormat.OpenXml.Packaging.WriteableStreamExtensions" && methodName == "EnableWriteableStream")
                    return ("openxml.enable_writable_stream", "file", IsWithinApplicationContentRoot(Path.GetTempPath(), applicationContentRoot));
                if (typeName == "DocumentFormat.OpenXml.Packaging.PackageUriHandlingExtensions" && methodName == "EnableUriHandling")
                    return ("openxml.enable_uri_handling", "file", IsWithinApplicationContentRoot(Path.GetTempPath(), applicationContentRoot));
            }
        }

        return (fallbackOperation, fallbackResourceType, fallbackResourceType is "file" or "directory"
            ? null
            : false);
    }

    private static bool? IsWithinApplicationContentRoot(string resourceDirectory, string? applicationContentRoot)
    {
        if (string.IsNullOrWhiteSpace(applicationContentRoot)) return null;
        try
        {
            var resource = Path.GetFullPath(resourceDirectory);
            var root = Path.GetFullPath(applicationContentRoot);
            var relative = Path.GetRelativePath(root, resource);
            return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Result<EmployeeBulkUploadValidationResult> InvalidWorkbook(string field, string message, string stage, string code) =>
        Result<EmployeeBulkUploadValidationResult>.Failure(ResultStatus.ValidationFailed, message,
            [new ValidationError(field, message)], stage, code);
}
