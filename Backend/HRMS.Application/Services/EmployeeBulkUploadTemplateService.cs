using ClosedXML.Excel;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class EmployeeBulkUploadTemplateService(
    IHrmsDbContext db,
    ITenantContext tenantContext,
    EmployeeBulkUploadOptions? uploadOptions = null)
    : IEmployeeBulkUploadTemplateService
{
    private readonly int _lastEmployeeInputRow = (uploadOptions?.MaximumEmployeeRows ?? EmployeeBulkUploadOptions.MaximumAllowedEmployeeRows) + 1;
    private const string ListsSheetName = "_Lists";

    public async Task<Result<byte[]>> GenerateAsync(CancellationToken cancellationToken = default)
    {
        if (tenantContext.TenantId is not Guid tenantId || tenantId == Guid.Empty)
            return Result<byte[]>.Unauthorized("No authenticated tenant.");

        var masters = await LoadMastersAsync(tenantId, cancellationToken);
        using var workbook = new XLWorkbook();
        var instructions = workbook.Worksheets.Add("Instructions");
        var employees = workbook.Worksheets.Add("Employees");
        var lists = workbook.Worksheets.Add(ListsSheetName);
        var listWriter = new ValidationListWriter(workbook, lists);
        listWriter.AddStaticList("Gender", Enum.GetNames<Gender>());
        listWriter.AddStaticList("BloodGroup", Enum.GetNames<BloodGroup>());
        listWriter.AddStaticList("MaritalStatus", Enum.GetNames<MaritalStatus>());
        listWriter.AddStaticList("EmploymentChangeReason", Enum.GetNames<EmploymentChangeReason>().Where(x => x != nameof(EmploymentChangeReason.Unspecified)));
        listWriter.AddStaticList("EmploymentType", Enum.GetNames<EmploymentType>().Where(x => x != nameof(EmploymentType.Unspecified)));
        listWriter.AddStaticList("EmployeeStatus", Enum.GetNames<EmployeeStatus>());
        listWriter.AddStaticList("YesNo", ["Yes", "No"]);
        listWriter.AddStaticList("Empty", [string.Empty]);

        foreach (var master in masters)
            WriteMasterSheet(workbook, master, listWriter);

        var columns = EmployeeColumns();
        WriteEmployeeSheet(employees, columns, _lastEmployeeInputRow);
        AddDropdowns(employees, columns, masters, listWriter, _lastEmployeeInputRow);
        WriteInstructions(instructions);
        lists.Visibility = XLWorksheetVisibility.Hidden;

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return Result<byte[]>.Success(output.ToArray(), "Employee template generated.");
    }

    private async Task<IReadOnlyList<MasterSheet>> LoadMastersAsync(Guid tenantId, CancellationToken ct)
    {
        var holdingCodes = await db.HoldingCompanies.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var departmentCodes = await db.Departments.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var subDepartmentCodes = await db.SubDepartments.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var sectionCodes = await db.Sections.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var functionCodes = await db.Functions.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId).ToDictionaryAsync(x => x.Id, x => x.Code, ct);

        var holdingCompanies = await db.HoldingCompanies.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new MasterValue(x.Code, x.Name)).ToListAsync(ct);
        var lobs = await db.LinesOfBusiness.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new { x.Code, x.Name, x.HoldingCompanyId }).ToListAsync(ct);
        var organisations = await db.Organisations.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new MasterValue(x.Code, x.Name)).ToListAsync(ct);
        var departments = await db.Departments.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new MasterValue(x.Code, x.Name)).ToListAsync(ct);
        var subDepartments = await db.SubDepartments.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new { x.Code, x.Name, x.DepartmentId }).ToListAsync(ct);
        var sections = await db.Sections.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new { x.Code, x.Name, x.SubDepartmentId }).ToListAsync(ct);
        var subSections = await db.SubSections.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new { x.Code, x.Name, x.SectionId }).ToListAsync(ct);
        var functions = await db.Functions.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new MasterValue(x.Code, x.Name)).ToListAsync(ct);
        var subFunctions = await db.SubFunctions.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new { x.Code, x.Name, x.FunctionId }).ToListAsync(ct);
        var grades = await db.Grades.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new MasterValue(x.Code, x.Name)).ToListAsync(ct);
        var designations = await db.Designations.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new MasterValue(x.Code, x.Name)).ToListAsync(ct);
        var employeeTypes = await db.EmployeeTypes.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new MasterValue(x.Code, x.Name)).ToListAsync(ct);
        var workLocations = await db.WorkLocations.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new MasterValue(x.Code, x.Name)).ToListAsync(ct);
        var costCenters = await db.CostCenters.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new MasterValue(x.Code, x.Name)).ToListAsync(ct);
        var positionReasons = await db.PositionChangeReasons.IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.IsActive).OrderBy(x => x.Code)
            .Select(x => new MasterValue(x.Code, x.Name)).ToListAsync(ct);

        var activeCountries = await db.Countries.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code)
            .Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct);
        var activeCountryIds = activeCountries.Select(x => x.Id).ToHashSet();
        var activeStates = await db.States.AsNoTracking().Where(x => x.IsActive && activeCountryIds.Contains(x.CountryId))
            .OrderBy(x => x.Code).Select(x => new { x.Id, x.Code, x.Name, x.CountryId }).ToListAsync(ct);
        var activeStateIds = activeStates.Select(x => x.Id).ToHashSet();
        var activeCities = await db.Cities.AsNoTracking().Where(x => x.IsActive && activeStateIds.Contains(x.StateId))
            .OrderBy(x => x.Code).Select(x => new { x.Code, x.Name, x.StateId }).ToListAsync(ct);

        return
        [
            new("Holding Companies", holdingCompanies),
            new("Lines of Business", lobs.Select(x => new MasterValue(x.Code, x.Name, x.HoldingCompanyId is Guid id && holdingCodes.TryGetValue(id, out var code) ? code : null)).ToList(), "Holding Company Code"),
            new("Organizations", organisations),
            new("Departments", departments),
            new("Sub Departments", subDepartments.Select(x => new MasterValue(x.Code, x.Name, departmentCodes.GetValueOrDefault(x.DepartmentId))).ToList(), "Department Code"),
            new("Sections", sections.Select(x => new MasterValue(x.Code, x.Name, x.SubDepartmentId is Guid id ? subDepartmentCodes.GetValueOrDefault(id) : null)).ToList(), "Sub Department Code"),
            new("Sub Sections", subSections.Select(x => new MasterValue(x.Code, x.Name, x.SectionId is Guid id ? sectionCodes.GetValueOrDefault(id) : null)).ToList(), "Section Code"),
            new("Functions", functions),
            new("Sub Functions", subFunctions.Select(x => new MasterValue(x.Code, x.Name, x.FunctionId is Guid id ? functionCodes.GetValueOrDefault(id) : null)).ToList(), "Function Code"),
            new("Grades", grades),
            new("Designations", designations),
            new("Employee Types", employeeTypes),
            new("Work Locations", workLocations),
            new("Cost Centers", costCenters),
            new("Position Change Reasons", positionReasons),
            new("Countries", activeCountries.Select(x => new MasterValue(x.Code, x.Name)).ToList()),
            new("States", activeStates.Select(x => new MasterValue(x.Code, x.Name, activeCountries.FirstOrDefault(c => c.Id == x.CountryId)?.Code)).ToList(), "Country Code"),
            new("Cities", activeCities.Select(x => new MasterValue(x.Code, x.Name, activeStates.FirstOrDefault(s => s.Id == x.StateId)?.Code)).ToList(), "State Code")
        ];
    }

    private static List<EmployeeColumn> EmployeeColumns() =>
    [
        new("PersonalDetails.Salutation", "Salutation", false, "Example: Ms."),
        new("PersonalDetails.FirstName", "First Name *", true, "Example: Asha. Required; maximum 100 characters."),
        new("PersonalDetails.MiddleName", "Middle Name", false, "Example: Rani."),
        new("PersonalDetails.LastName", "Last Name *", true, "Example: Rao. Required; maximum 100 characters."),
        new("PersonalDetails.DateOfBirth", "Date of Birth", false, "Example: 1995-04-23.", "yyyy-mm-dd"),
        new("PersonalDetails.Gender", "Gender", false, "Select an enum value."),
        new("PersonalDetails.BloodGroup", "Blood Group", false, "Select an enum value."),
        new("PersonalDetails.MaritalStatus", "Marital Status", false, "Select an enum value."),
        new("PersonalDetails.BirthCountryId", "Birth Country Code", false, "Enter a Country Code from the Countries sheet."),
        new("PersonalDetails.BirthStateId", "Birth State Code", false, "Enter a State Code from the States sheet; filtered by Birth Country Code."),
        new("PersonalDetails.BirthCityId", "Birth City Code", false, "Enter a City Code from the Cities sheet; filtered by Birth State Code."),
        new("PersonalDetails.Religion", "Religion", false, "Example: Hindu."),
        new("PersonalDetails.Caste", "Caste", false, "Optional free text."),
        new("PersonalDetails.Citizenship", "Citizenship", false, "Example: Indian. This request field is free text."),
        new("PersonalDetails.EsicApplicable", "ESIC Applicable", false, "Enter Yes or No."),
        new("PersonalDetails.EsicNumber", "ESIC Number", false, "Required when ESIC Applicable is Yes."),
        new("PersonalDetails.PfNumber", "PF Number", false, "Optional free text."),
        new("PersonalDetails.MediclaimNumber", "Mediclaim Number", false, "Optional free text."),
        new("PersonalDetails.UanNumber", "UAN Number", false, "Optional; if supplied, exactly 12 digits."),
        new("PersonalDetails.Gratuity", "Gratuity", false, "Enter Yes or No."),
        new("PersonalDetails.Pension", "Pension", false, "Enter Yes or No."),
        new("PersonalDetails.AadhaarNumber", "Aadhaar Number *", true, "Required; exactly 12 digits."),
        new("PersonalDetails.PanNumber", "PAN *", true, "Required; format ABCDE1234F."),
        new("PersonalDetails.DateOfJoining", "Date of Joining *", true, "Required. Example: 2026-04-01.", "yyyy-mm-dd"),
        new("PersonalDetails.JobStatus", "Job Status *", true, "Required free-text status (maximum 100 characters). Example: On Probation."),
        new("Contact.OfficialEmail", "Official Email", false, "Optional; must be a valid email when supplied."),
        new("Contact.PersonalEmail", "Personal Email", false, "Optional; must be a valid email when supplied."),
        new("Contact.AlternateEmail", "Alternate Email", false, "Optional; must be a valid email when supplied."),
        new("Contact.OfficialPhone", "Official Phone", false, "Optional; maximum 30 characters."),
        new("Contact.PersonalPhone", "Personal Phone", false, "Optional; maximum 30 characters."),
        new("Contact.EmergencyNumber", "Emergency Number", false, "Optional; maximum 30 characters."),
        new("Contact.SameAsCurrentAddress", "Same As Current Address", false, "Enter Yes or No."),
        new("InitialEmployment.EmployeeCode", "Manual Employee Code", false, "Only enter a code when the tenant is configured for manual assignment. Leave blank for generated codes."),
        new("InitialEmployment.EffectiveFrom", "Employment Effective From *", true, "Required and must equal Date of Joining.", "yyyy-mm-dd"),
        new("InitialEmployment.HoldingCompanyId", "Holding Company Code", false, "Select a code from the Holding Companies sheet."),
        new("InitialEmployment.LobId", "LOB Code", false, "Filtered by Holding Company Code."),
        new("InitialEmployment.OrganisationId", "Organization Code", false, "Select a code from the Organizations sheet; this master is not parented."),
        new("InitialEmployment.DepartmentId", "Department Code *", true, "Required; select a code from the Departments sheet."),
        new("InitialEmployment.SubDepartmentId", "Sub Department Code", false, "Filtered by Department Code."),
        new("InitialEmployment.SectionId", "Section Code", false, "Filtered by Sub Department Code."),
        new("InitialEmployment.SubSectionId", "Sub Section Code", false, "Filtered by Section Code."),
        new("InitialEmployment.FunctionId", "Function Code", false, "Select a code from the Functions sheet."),
        new("InitialEmployment.SubFunctionId", "Sub Function Code", false, "Filtered by Function Code."),
        new("InitialEmployment.GradeId", "Grade Code", false, "Select a code from the Grades sheet."),
        new("InitialEmployment.DesignationId", "Designation Code *", true, "Required; select a code from the Designations sheet."),
        new("InitialEmployment.EmployeeTypeId", "Employee Type Code", false, "Select a code from the Employee Types sheet."),
        new("InitialEmployment.CountryLocationId", "Country Location Code", false, "Select a country code from the Countries sheet."),
        new("InitialEmployment.WorkLocationId", "Work Location Code", false, "Select a code from the Work Locations sheet."),
        new("InitialEmployment.CostCenterId", "Cost Center Code", false, "Select a code from the Cost Centers sheet."),
        new("InitialEmployment.ManagerId", "Manager Employee Code", false, "Optional. Enter an existing employee code; no employee directory is included in this workbook."),
        new("InitialEmployment.PositionChangeReasonId", "Position Change Reason Code", false, "Select a code from the Position Change Reasons sheet."),
        new("InitialEmployment.ChangeReason", "Employment Change Reason *", true, "Initial onboarding uses NewJoining."),
        new("InitialEmployment.ChangeReasonDescription", "Change Reason Description", false, "Optional; maximum 500 characters."),
        new("InitialEmployment.BusinessRole", "Business Role", false, "Optional; maximum 200 characters."),
        new("InitialEmployment.GradeLevel", "Grade Level", false, "Optional snapshot text; maximum 50 characters."),
        new("InitialEmployment.CareerGroup", "Career Group", false, "Optional; maximum 100 characters."),
        new("InitialEmployment.EmploymentType", "Employment Type *", true, "Required enum value."),
        new("InitialEmployment.EmploymentStatus", "Employment Status", false, "Enum value; defaults to Active."),
    ];

    private static void WriteEmployeeSheet(IXLWorksheet ws, IReadOnlyList<EmployeeColumn> columns, int employeeInputRows)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var header = ws.Cell(1, i + 1);
            header.Value = column.Header;
            header.Style.Font.Bold = true;
            header.Style.Font.FontColor = XLColor.White;
            header.Style.Fill.BackgroundColor = column.Required ? XLColor.FromHtml("#9C2F2F") : XLColor.FromHtml("#234E70");
            header.Style.Alignment.WrapText = true;
            header.CreateComment().AddText($"{column.Help}\nRequest property: {column.PropertyPath}");
            ws.Column(i + 1).Width = Math.Clamp(column.Header.Length + 3, 16, 32);
            if (column.DateFormat is not null)
                ws.Range(2, i + 1, employeeInputRows, i + 1).Style.DateFormat.Format = column.DateFormat;
        }

        ws.Row(1).Height = 42;
        ws.SheetView.FreezeRows(1);
        ws.Range(1, 1, employeeInputRows, columns.Count).SetAutoFilter();
        ws.TabColor = XLColor.FromHtml("#1F6E43");
    }

    private static void WriteMasterSheet(XLWorkbook workbook, MasterSheet master, ValidationListWriter lists)
    {
        var ws = workbook.Worksheets.Add(master.Name);
        var headers = master.ParentHeader is null
            ? new[] { "Code", "Name", "Active Status" }
            : ["Code", "Name", "Active Status", master.ParentHeader];
        WriteTableHeader(ws, headers);

        for (var i = 0; i < master.Rows.Count; i++)
        {
            var row = i + 2;
            WriteText(ws.Cell(row, 1), master.Rows[i].Code);
            WriteText(ws.Cell(row, 2), master.Rows[i].Name);
            ws.Cell(row, 3).Value = "Active";
            if (master.ParentHeader is not null)
                WriteText(ws.Cell(row, 4), master.Rows[i].ParentCode ?? string.Empty);
        }

        var last = Math.Max(master.Rows.Count + 1, 2);
        ws.Range(1, 1, last, headers.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Column(1).Width = 22;
        ws.Column(2).Width = 40;
        ws.Column(3).Width = 16;
        if (master.ParentHeader is not null)
            ws.Column(4).Width = 26;
        lists.AddMasterList(master.Name, ws, master.Rows);
    }

    private static void AddDropdowns(IXLWorksheet ws, IReadOnlyList<EmployeeColumn> columns,
        IReadOnlyList<MasterSheet> masters, ValidationListWriter lists, int employeeInputRows)
    {
        var index = columns.Select((x, i) => (x.Header, Column: i + 1)).ToDictionary(x => x.Header, x => x.Column);
        var direct = new Dictionary<string, string>
        {
            ["Gender"] = "Gender", ["Blood Group"] = "BloodGroup", ["Marital Status"] = "MaritalStatus",
            ["ESIC Applicable"] = "YesNo", ["Gratuity"] = "YesNo", ["Pension"] = "YesNo",
            ["Same As Current Address"] = "YesNo", ["Employment Change Reason *"] = "EmploymentChangeReason",
            ["Employment Type *"] = "EmploymentType", ["Employment Status"] = "EmployeeStatus"
        };
        foreach (var pair in direct)
            lists.ApplyDirect(ws, index[pair.Key], pair.Value, employeeInputRows);

        foreach (var masterName in new[]
        {
            "Holding Companies", "Organizations", "Departments", "Grades", "Designations",
            "Employee Types", "Work Locations", "Cost Centers", "Position Change Reasons", "Countries"
        })
        {
            var header = masterName switch
            {
                "Holding Companies" => "Holding Company Code",
                "Organizations" => "Organization Code",
                "Departments" => "Department Code *",
                "Functions" => "Function Code",
                "Grades" => "Grade Code",
                "Designations" => "Designation Code *",
                "Employee Types" => "Employee Type Code",
                "Work Locations" => "Work Location Code",
                "Cost Centers" => "Cost Center Code",
                "Position Change Reasons" => "Position Change Reason Code",
                _ => "Country Location Code"
            };
            lists.ApplyDirect(ws, index[header], masterName, employeeInputRows);
        }

        lists.ApplyDirect(ws, index["Birth Country Code"], "Countries", employeeInputRows);
        lists.ApplyDependent(ws, index["Birth State Code"], index["Birth Country Code"], "Countries", "States", employeeInputRows);
        lists.ApplyDependent(ws, index["Birth City Code"], index["Birth State Code"], "States", "Cities", employeeInputRows);
        lists.ApplyDependent(ws, index["LOB Code"], index["Holding Company Code"], "Holding Companies", "Lines of Business", employeeInputRows);
        lists.ApplyDependent(ws, index["Sub Department Code"], index["Department Code *"], "Departments", "Sub Departments", employeeInputRows);
        lists.ApplyDependent(ws, index["Section Code"], index["Sub Department Code"], "Sub Departments", "Sections", employeeInputRows);
        lists.ApplyDependent(ws, index["Sub Section Code"], index["Section Code"], "Sections", "Sub Sections", employeeInputRows);
        lists.ApplyDirect(ws, index["Function Code"], "Functions", employeeInputRows);
        lists.ApplyDependent(ws, index["Sub Function Code"], index["Function Code"], "Functions", "Sub Functions", employeeInputRows);
        lists.ApplyDirect(ws, index["Country Location Code"], "Countries", employeeInputRows);
    }

    private static void WriteInstructions(IXLWorksheet ws)
    {
        ws.Cell("A1").Value = "Employee Bulk Upload Template";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 18;
        var lines = new[]
        {
            "Use one employee per row on the Employees sheet. Do not rename sheets or column headings.",
            "Red headings with * are mandatory. Values marked as codes must match the Code column on the matching reference sheet.",
            "Employee code is generated according to tenant configuration. Only enter Manual Employee Code when the tenant is configured for manual assignment; otherwise leave it blank.",
            "Date of Joining and Employment Effective From must match. Use dates in yyyy-mm-dd format.",
            "Initial onboarding requires Employment Change Reason = NewJoining. Department, Designation, Job Status, Aadhaar, PAN, names, joining date, and employment type are required by current validation.",
            "Manager Employee Code is free entry because the workbook deliberately contains no employee directory or sensitive employee records.",
            "Active master data is tenant-specific except Countries, States, and Cities, which are shared geographic references. Internal database IDs are not included.",
            "Dropdowns use workbook-defined names. Dependent dropdowns use INDIRECT/VLOOKUP formulas and are best supported in desktop Excel; some spreadsheet viewers may not evaluate them. Server-side import validation remains authoritative.",
            "This workbook is a data-entry template only. It does not upload or validate employee data."
        };
        for (var i = 0; i < lines.Length; i++)
        {
            ws.Cell(i + 3, 1).Value = lines[i];
            ws.Cell(i + 3, 1).Style.Alignment.WrapText = true;
        }
        ws.Column(1).Width = 115;
        ws.SheetView.FreezeRows(2);
        ws.TabColor = XLColor.FromHtml("#264653");
    }

    private static void WriteTableHeader(IXLWorksheet ws, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#234E70");
        }
        ws.Row(1).Height = 24;
    }

    private static void WriteText(IXLCell cell, string value)
    {
        cell.Value = value;
    }

    private sealed record EmployeeColumn(string PropertyPath, string Header, bool Required, string Help, string? DateFormat = null);
    private sealed record MasterValue(string Code, string Name, string? ParentCode = null);
    private sealed record MasterSheet(string Name, IReadOnlyList<MasterValue> Rows, string? ParentHeader = null);

    private sealed class ValidationListWriter(XLWorkbook workbook, IXLWorksheet ws)
    {
        private int nextListColumn = 1;
        private int nextRangeNumber;
        private readonly Dictionary<string, string> masterRanges = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<MasterValue>> masterRowsBySheet = new(StringComparer.Ordinal);

        public void AddStaticList(string name, IEnumerable<string> values)
        {
            var column = nextListColumn++;
            var list = values.ToArray();
            if (list.Length == 0)
                list = [string.Empty];
            for (var i = 0; i < list.Length; i++)
                WriteText(ws.Cell(i + 2, column), list[i]);
            var safeName = $"List_{Sanitize(name)}";
            workbook.DefinedNames.Add(safeName, ws.Range(2, column, list.Length + 1, column));
            masterRanges[name] = safeName;
        }

        public void AddMasterList(string sheetName, IXLWorksheet masterSheet, IReadOnlyList<MasterValue> rows)
        {
            masterRowsBySheet[sheetName] = rows;
            var name = ListName(sheetName);
            if (rows.Count == 0)
            {
                masterRanges[sheetName] = "List_Empty";
                return;
            }
            workbook.DefinedNames.Add(name, masterSheet.Range(2, 1, rows.Count + 1, 1));
            masterRanges[sheetName] = name;
        }

        public void ApplyDirect(IXLWorksheet sheet, int column, string listKey, int finalRow)
        {
            var name = masterRanges.TryGetValue(listKey, out var masterName) ? masterName : ListName(listKey);
            var validation = sheet.Range(2, column, finalRow, column).CreateDataValidation();
            validation.List($"={name}");
            validation.IgnoreBlanks = true;
            validation.InCellDropdown = true;
            validation.ShowErrorMessage = true;
            validation.ErrorTitle = "Select a listed value";
            validation.ErrorMessage = "Use a value from this tenant's current reference list.";
        }

        public void ApplyDependent(IXLWorksheet sheet, int column, int parentColumn, string parentSheet,
            string childSheet, int finalRow)
        {
            var child = masterRowsBySheet[childSheet];
            var parentGroups = child
                .GroupBy(x => x.ParentCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
            var mapColumn = nextListColumn++;
            var valueColumn = nextListColumn++;
            var mapStart = 2;
            ws.Cell(1, mapColumn).Value = $"{parentSheet} to {childSheet}";
            ws.Cell(1, valueColumn).Value = "Defined Name";
            var mapRow = mapStart;

            foreach (var group in parentGroups)
            {
                var rangeName = $"ChildList_{++nextRangeNumber}";
                var listColumn = nextListColumn++;
                var start = 2;
                var valueRow = start;
                foreach (var item in group.OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase))
                    WriteText(ws.Cell(valueRow++, listColumn), item.Code);
                if (valueRow == start)
                    WriteText(ws.Cell(valueRow++, listColumn), string.Empty);
                workbook.DefinedNames.Add(rangeName, ws.Range(start, listColumn, valueRow - 1, listColumn));
                WriteText(ws.Cell(mapRow, mapColumn), group.Key);
                WriteText(ws.Cell(mapRow, valueColumn), rangeName);
                mapRow++;
            }

            if (parentGroups.Count > 0)
            {
                var mapName = $"Map_{Sanitize(parentSheet)}_{Sanitize(childSheet)}";
                workbook.DefinedNames.Add(mapName, ws.Range(mapStart, mapColumn, mapRow - 1, valueColumn));
                var parentLetter = XLHelper.GetColumnLetterFromNumber(parentColumn);
                var formula = $"=INDIRECT(IFERROR(VLOOKUP(${parentLetter}2,{mapName},2,FALSE),\"List_Empty\"))";
                var validation = sheet.Range(2, column, finalRow, column).CreateDataValidation();
                validation.List(formula);
                validation.IgnoreBlanks = true;
                validation.InCellDropdown = true;
                validation.ShowErrorMessage = true;
                validation.ErrorTitle = "Select a dependent value";
                validation.ErrorMessage = "Select a parent code first, then choose a matching child code.";
            }
            else
            {
                ApplyDirect(sheet, column, childSheet, finalRow);
            }
        }

        private static string ListName(string name) => $"List_{Sanitize(name)}";
        private static string Sanitize(string value)
        {
            var result = new string(value.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
            if (result.Length == 0 || char.IsDigit(result[0])) result = "L_" + result;
            return result;
        }
    }
}
