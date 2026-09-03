using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

/// <summary>
/// Employee business logic.
/// <para>
/// Multi-tenancy. Reads are covered by the tenant global query filter, but writes are where this module
/// could leak: a department, designation or manager id arrives from the client, and a foreign key alone is
/// satisfied by any existing row — including another tenant's. Every one of those ids is therefore resolved
/// through the tenant-filtered <c>DbSet</c> before use, so a foreign id fails exactly like a nonexistent
/// one. The composite <c>(TenantId, …)</c> foreign keys in <c>EmployeeConfiguration</c> are the second,
/// independent layer: even a bug here cannot persist a cross-tenant reference.
/// </para>
/// <para>
/// Two rules are intentionally narrow. A referenced department/designation/manager must be active only when
/// the reference <em>changes</em> — otherwise editing someone's phone number would be impossible once their
/// department is retired. And a manager who resigns is not automatically detached from their reports:
/// re-parenting an org chart is a deliberate act, not a side effect of a status change.
/// </para>
/// </summary>
public class EmployeeService : IEmployeeService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Employee not found.";

    /// <summary>
    /// Upper bound on an export. Beyond this the request is refused with an explanation rather than
    /// truncated: a spreadsheet that is silently missing rows is worse than one that was never produced.
    /// </summary>
    private const int MaxExportRows = 10_000;

    /// <summary>
    /// Hop limit when walking a reporting line. A real hierarchy is a few levels deep; the limit only
    /// exists so that already-cyclic data (from a direct database edit, say) cannot spin this forever.
    /// </summary>
    private const int MaxReportingDepth = 100;

    private const string DateFormat = "yyyy-MM-dd";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmployeeService> _logger;

    public EmployeeService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        ILogger<EmployeeService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<PagedResult<EmployeeListItemDto>>> GetAsync(
        EmployeeQuery query, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<PagedResult<EmployeeListItemDto>>.Unauthorized(NoTenantMessage);
        }

        var businessDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var employees = ApplyEffectiveSort(ApplyEffectiveFilters(_db.Employees.AsNoTracking(), query, businessDate), query, businessDate);

        var currentHistory = _db.EmployeeEmploymentHistory
            .Where(h => h.EffectiveFrom <= businessDate && (h.EffectiveTo == null || h.EffectiveTo >= businessDate));

        var page = await employees
            .Select(e => new
            {
                Employee = e,
                Current = currentHistory
                    .Where(h => h.EmployeeId == e.Id)
                    .OrderByDescending(h => h.EffectiveFrom)
                    .ThenByDescending(h => h.CreatedDate)
                    .FirstOrDefault()
            })
            .Select(x => new EmployeeListItemDto(
                x.Employee.Id,
                x.Employee.EmployeeCode ?? string.Empty,
                x.Employee.FirstName + " " + x.Employee.LastName,
                x.Employee.Contact != null && x.Employee.Contact.OfficialEmail != null ? x.Employee.Contact.OfficialEmail : x.Employee.Email,
                x.Current != null && x.Current.Department != null ? x.Current.Department.Name : x.Employee.Department != null ? x.Employee.Department.Name : null,
                x.Current != null && x.Current.Designation != null ? x.Current.Designation.Name : x.Employee.Designation != null ? x.Employee.Designation.Name : null,
                x.Current != null ? x.Current.EmploymentStatus : x.Employee.Status,
                x.Employee.DateOfJoining,
                x.Current != null))
            .ToPagedResultAsync(query, cancellationToken);

        return Result<PagedResult<EmployeeListItemDto>>.Success(page);
    }

    public async Task<Result<EmployeeDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<EmployeeDto>.Unauthorized(NoTenantMessage);
        }

        var employee = await ProjectDetail(_db.Employees.AsNoTracking().Where(e => e.Id == id))
            .FirstOrDefaultAsync(cancellationToken);

        if (employee is not null)
        {
            var effective = await ApplyCurrentEmploymentAsync(employee, cancellationToken);
            if (!effective.Succeeded)
                return effective;
            employee = effective.Value!;
        }

        return employee is null
            ? Result<EmployeeDto>.NotFound(NotFoundMessage)
            : Result<EmployeeDto>.Success(employee);
    }

    public async Task<Result<EmployeeSensitiveDetailsDto>> GetSensitiveDetailsAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<EmployeeSensitiveDetailsDto>.Unauthorized(NoTenantMessage);
        }

        var details = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new EmployeeSensitiveDetailsDto(
                e.Id,
                e.AadhaarNumber,
                e.PanNumber,
                e.UanNumber,
                e.PfNumber,
                e.EsicNumber,
                e.MediclaimNumber))
            .FirstOrDefaultAsync(cancellationToken);

        return details is null
            ? Result<EmployeeSensitiveDetailsDto>.NotFound(NotFoundMessage)
            : Result<EmployeeSensitiveDetailsDto>.Success(details);
    }

    public async Task<Result<EmployeeDto>> CreateAsync(
        EmployeeRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeDto>.Unauthorized(NoTenantMessage);
        }

        var employeeCode = request.EmployeeCode.Trim();
        var email = request.Email.Trim();

        // Enforce default employee status at the backend level.
        var employeeStatus = request.Status == default ? EmployeeStatus.Active : request.Status;

        var referenceProblem = await ValidateReferencesAsync(request, existing: null, cancellationToken);
        if (referenceProblem is not null)
        {
            return referenceProblem;
        }

        var conflict = await FindConflictAsync(employeeCode, email, excludeId: null, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeCode = employeeCode,
            Salutation = Normalize(request.Salutation),
            FirstName = request.FirstName.Trim(),
            MiddleName = Normalize(request.MiddleName),
            LastName = request.LastName.Trim(),
            Email = email,
            Phone = Normalize(request.Phone),
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            BloodGroup = request.BloodGroup,
            MaritalStatus = request.MaritalStatus,
            BirthCountry = Normalize(request.BirthCountry),
            BirthState = Normalize(request.BirthState),
            BirthCity = Normalize(request.BirthCity),
            BirthCountryId = request.BirthCountryId,
            BirthStateId = request.BirthStateId,
            BirthCityId = request.BirthCityId,
            Religion = Normalize(request.Religion),
            Caste = Normalize(request.Caste),
            EmployeeType = Normalize(request.EmployeeType),
            DateOfJoining = request.DateOfJoining,
            GroupDateOfJoining = request.GroupDateOfJoining,
            DateOfLeaving = request.DateOfLeaving,
            Status = employeeStatus,
            JobStatus = Normalize(request.JobStatus),
            GroupId = Normalize(request.GroupId),
            DepartmentId = request.DepartmentId,
            DesignationId = request.DesignationId,
            ReportingManagerId = request.ReportingManagerId,
            AadhaarNumber = Normalize(request.AadhaarNumber),
            PanNumber = Normalize(request.PanNumber),
            PfNumber = Normalize(request.PfNumber),
            UanNumber = Normalize(request.UanNumber),
            EsicNumber = Normalize(request.EsicNumber),
            MediclaimNumber = Normalize(request.MediclaimNumber),
            Gratuity = request.Gratuity,
            Pension = request.Pension,
            CostCenterCode = Normalize(request.CostCenterCode),
            PayrollLocation = Normalize(request.PayrollLocation),
            EsicApplicable = request.EsicApplicable,
            Citizenship = Normalize(request.Citizenship),
            LanguageKnown = Normalize(request.LanguageKnown),
            ProfilePictureUrl = Normalize(request.ProfilePictureUrl),
            Address = Normalize(request.Address)
        };

        _db.Employees.Add(employee);
        _db.EmployeeContacts.Add(new EmployeeContact
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employee.Id,
            OfficialEmail = email,
            OfficialPhone = employee.Phone
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await FindConflictAsync(employeeCode, email, excludeId: null, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            _logger.LogWarning(
                "Employee create for tenant {TenantId} lost a uniqueness race on the database index.", tenantId);
            return raced;
        }

        _logger.LogInformation("Created employee {EmployeeId} in tenant {TenantId}.", employee.Id, tenantId);

        return await ReloadAsync(employee.Id, "Employee created.", cancellationToken);
    }

    public async Task<Result<EmployeeDto>> UpdateAsync(
        Guid id, EmployeeRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeDto>.Unauthorized(NoTenantMessage);
        }

        var employee = await _db.Employees
            .Include(e => e.Contact)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return Result<EmployeeDto>.NotFound(NotFoundMessage);
        }

        if (request.ReportingManagerId == id)
        {
            return Result<EmployeeDto>.Invalid("reportingManagerId", "An employee cannot report to themselves.");
        }

        var referenceProblem = await ValidateReferencesAsync(request, employee, cancellationToken);
        if (referenceProblem is not null)
        {
            return referenceProblem;
        }

        // Only walk the reporting line when the manager actually changes: it costs one query per level, and
        // an unchanged manager cannot have introduced a cycle that was not already there.
        if (request.ReportingManagerId is Guid managerId && managerId != employee.ReportingManagerId)
        {
            if (await WouldCreateReportingCycleAsync(id, managerId, cancellationToken))
            {
                return Result<EmployeeDto>.Invalid(
                    "reportingManagerId",
                    "That manager reports (directly or indirectly) to this employee, which would create a loop in the reporting line.");
            }
        }

        var employeeCode = request.EmployeeCode.Trim();
        var email = request.Email.Trim();

        var conflict = await FindConflictAsync(employeeCode, email, excludeId: id, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        employee.EmployeeCode = employeeCode;
        employee.Salutation = Normalize(request.Salutation);
        employee.FirstName = request.FirstName.Trim();
        employee.MiddleName = Normalize(request.MiddleName);
        employee.LastName = request.LastName.Trim();
        employee.Email = email;
        employee.Phone = Normalize(request.Phone);
        employee.DateOfBirth = request.DateOfBirth;
        employee.Gender = request.Gender;
        employee.BloodGroup = request.BloodGroup;
        employee.MaritalStatus = request.MaritalStatus;
        employee.BirthCountry = Normalize(request.BirthCountry);
        employee.BirthState = Normalize(request.BirthState);
        employee.BirthCity = Normalize(request.BirthCity);
        employee.BirthCountryId = request.BirthCountryId;
        employee.BirthStateId = request.BirthStateId;
        employee.BirthCityId = request.BirthCityId;
        employee.Religion = Normalize(request.Religion);
        employee.Caste = Normalize(request.Caste);
        employee.EmployeeType = Normalize(request.EmployeeType);
        employee.DateOfJoining = request.DateOfJoining;
        employee.GroupDateOfJoining = request.GroupDateOfJoining;
        employee.DateOfLeaving = request.DateOfLeaving;
        employee.Status = request.Status;
        employee.JobStatus = Normalize(request.JobStatus);
        employee.GroupId = Normalize(request.GroupId);
        employee.DepartmentId = request.DepartmentId;
        employee.DesignationId = request.DesignationId;
        employee.ReportingManagerId = request.ReportingManagerId;
        employee.AadhaarNumber = Normalize(request.AadhaarNumber);
        employee.PanNumber = Normalize(request.PanNumber);
        employee.PfNumber = Normalize(request.PfNumber);
        employee.UanNumber = Normalize(request.UanNumber);
        employee.EsicNumber = Normalize(request.EsicNumber);
        employee.MediclaimNumber = Normalize(request.MediclaimNumber);
        employee.Gratuity = request.Gratuity;
        employee.Pension = request.Pension;
        employee.CostCenterCode = Normalize(request.CostCenterCode);
        employee.PayrollLocation = Normalize(request.PayrollLocation);
        employee.EsicApplicable = request.EsicApplicable;
        employee.Citizenship = Normalize(request.Citizenship);
        employee.LanguageKnown = Normalize(request.LanguageKnown);
        employee.ProfilePictureUrl = Normalize(request.ProfilePictureUrl);
        employee.Address = Normalize(request.Address);

        // Keep the legacy full Employee edit path working while maintaining EmployeeContact as the
        // employee-facing contact record. Alternate contact values are deliberately left untouched.
        if (employee.Contact is null)
        {
            employee.Contact = new EmployeeContact
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employee.Id,
                OfficialEmail = email,
                OfficialPhone = employee.Phone
            };
            _db.EmployeeContacts.Add(employee.Contact);
        }
        else
        {
            employee.Contact.OfficialEmail = email;
            employee.Contact.OfficialPhone = employee.Phone;
            employee.Contact.ModifiedDate = DateTime.UtcNow;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await FindConflictAsync(employeeCode, email, excludeId: id, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            _logger.LogWarning(
                "Employee update for {EmployeeId} lost a uniqueness race on the database index.", id);
            return raced;
        }

        _logger.LogInformation("Updated employee {EmployeeId} in tenant {TenantId}.", id, tenantId);

        return await ReloadAsync(id, "Employee updated.", cancellationToken);
    }

    public async Task<Result<EmployeeDto>> CreatePersonalDetailsAsync(
        EmployeePersonalDetailsRequest request,
        CancellationToken cancellationToken = default,
        bool canEditSensitive = true)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeDto>.Unauthorized(NoTenantMessage);
        }

        var birthProblem = await ValidateBirthLocationAsync(request, cancellationToken);
        if (birthProblem is not null)
        {
            return birthProblem;
        }

        return await CreatePersonalDetailsCoreAsync(
            request, tenantId, cancellationToken, canEditSensitive);
    }

    public async Task<Result<EmployeeDto>> UpdatePersonalDetailsAsync(
        Guid id,
        EmployeePersonalDetailsRequest request,
        CancellationToken cancellationToken = default,
        bool canEditSensitive = true)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeDto>.Unauthorized(NoTenantMessage);
        }

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return Result<EmployeeDto>.NotFound(NotFoundMessage);
        }

        var birthProblem = await ValidateBirthLocationAsync(request, cancellationToken);
        if (birthProblem is not null)
        {
            return birthProblem;
        }

        ApplyPersonalDetails(employee, request, preserveSensitive: true, canEditSensitive: canEditSensitive);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated Personal Details for employee {EmployeeId} in tenant {TenantId}.", id, tenantId);

        return await ReloadAsync(id, "Personal Details updated.", cancellationToken);
    }

    /// <summary>
    /// Removes an employee record outright. This is for correcting a mistake (a duplicate, a person who
    /// never actually joined) — someone who leaves the organization is marked Resigned or Terminated, which
    /// is why <see cref="EmployeeStatus"/> exists.
    /// </summary>
    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<bool>.Unauthorized(NoTenantMessage);
        }

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return Result<bool>.NotFound(NotFoundMessage);
        }

        var directReports = await _db.Employees.CountAsync(e => e.ReportingManagerId == id, cancellationToken);
        if (directReports > 0)
        {
            return Result<bool>.Conflict(
                $"{directReports} employee(s) report to this person. Reassign them to another manager first.");
        }

        _db.Employees.Remove(employee);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("Employee {EmployeeId} could not be deleted: the record is still referenced.", id);
            return Result<bool>.Conflict("This employee record is still referenced and cannot be deleted.");
        }

        _logger.LogInformation("Deleted employee {EmployeeId} from tenant {TenantId}.", id, tenantId);

        return Result<bool>.Success(true, "Employee deleted.");
    }

    public async Task<Result<EmployeeExportDto>> ExportAsync(
        EmployeeQuery query, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeExportDto>.Unauthorized(NoTenantMessage);
        }

        var filtered = ApplyFilters(_db.Employees.AsNoTracking(), query);

        var total = await filtered.CountAsync(cancellationToken);
        if (total > MaxExportRows)
        {
            return Result<EmployeeExportDto>.Invalid(
                $"This export would contain {total} rows, above the limit of {MaxExportRows}. Narrow the filter (by department, designation or status) and try again.");
        }

        // Same ordering as the list endpoint, so the file matches what the user was looking at. Take() is a
        // belt-and-braces bound in case rows are inserted between the count and this read.
        var rows = await ProjectDetail(ApplySort(filtered, query))
            .Take(MaxExportRows)
            .ToListAsync(cancellationToken);

        var csv = new CsvBuilder(
            "Employee Code", "Salutation", "First Name", "Middle Name", "Last Name", "Email", "Phone",
            "Date of Birth", "Gender", "Blood Group", "Marital Status",
            "Date of Joining", "Group Date of Joining", "Date of Leaving", "Status", "Job Status",
            "Employee Type", "Department", "Designation", "Reporting Manager",
            "Aadhaar", "PAN", "PF", "UAN",
            "ESIC Number", "Mediclaim Number", "Gratuity", "Pension",
            "Cost Center", "Payroll Location", "ESIC Applicable",
            "Citizenship", "Language Known", "Address");

        foreach (var row in rows)
        {
            csv.AppendRow(
                row.EmployeeCode,
                row.Salutation,
                row.FirstName,
                row.MiddleName,
                row.LastName,
                row.Email,
                row.Phone,
                row.DateOfBirth?.ToString(DateFormat),
                row.Gender.ToString(),
                row.BloodGroup.ToString(),
                row.MaritalStatus.ToString(),
                row.DateOfJoining.ToString(DateFormat),
                row.GroupDateOfJoining?.ToString(DateFormat),
                row.DateOfLeaving?.ToString(DateFormat),
                row.Status.ToString(),
                row.JobStatus,
                row.EmployeeType,
                row.DepartmentName,
                row.DesignationName,
                row.ReportingManagerName,
                row.MaskedAadhaarNumber,
                row.MaskedPanNumber,
                row.MaskedPfNumber,
                row.MaskedUanNumber,
                row.MaskedEsicNumber,
                row.MaskedMediclaimNumber,
                row.Gratuity.ToString(),
                row.Pension.ToString(),
                row.CostCenterCode,
                row.PayrollLocation,
                row.EsicApplicable.ToString(),
                row.Citizenship,
                row.LanguageKnown,
                row.Address);
        }

        var fileName = $"employees-{_timeProvider.GetUtcNow():yyyyMMdd-HHmmss}.csv";

        // Row count only — the log must never carry the exported personal data itself.
        _logger.LogInformation(
            "Exported {RowCount} employee(s) for tenant {TenantId}.", csv.RowCount, tenantId);

        return Result<EmployeeExportDto>.Success(
            new EmployeeExportDto(fileName, "text/csv; charset=utf-8", csv.ToUtf8Bytes(), csv.RowCount));
    }

    private static IQueryable<Employee> ApplyFilters(IQueryable<Employee> employees, EmployeeQuery query)
    {
        if (query.DepartmentId is Guid departmentId)
        {
            employees = employees.Where(e => e.DepartmentId == departmentId);
        }

        if (query.DesignationId is Guid designationId)
        {
            employees = employees.Where(e => e.DesignationId == designationId);
        }

        if (query.Status is EmployeeStatus status)
        {
            employees = employees.Where(e => e.Status == status);
        }

        if (query.ReportingManagerId is Guid managerId)
        {
            employees = employees.Where(e => e.ReportingManagerId == managerId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            employees = employees.Where(e =>
                (e.EmployeeCode ?? string.Empty).ToLower().Contains(search) ||
                e.FirstName.ToLower().Contains(search) ||
                e.LastName.ToLower().Contains(search) ||
                (e.Contact != null && e.Contact.OfficialEmail != null
                    ? e.Contact.OfficialEmail
                    : e.Email).ToLower().Contains(search));
        }

        return employees;
    }

    private IQueryable<Employee> ApplyEffectiveFilters(
        IQueryable<Employee> employees, EmployeeQuery query, DateOnly businessDate)
    {
        var currentHistory = _db.EmployeeEmploymentHistory
            .Where(h => h.EffectiveFrom <= businessDate && (h.EffectiveTo == null || h.EffectiveTo >= businessDate));

        if (query.DepartmentId is Guid departmentId)
        {
            employees = employees.Where(e =>
                currentHistory.Any(h => h.EmployeeId == e.Id && h.DepartmentId == departmentId) ||
                !currentHistory.Any(h => h.EmployeeId == e.Id) && e.DateOfJoining <= businessDate && e.DepartmentId == departmentId);
        }

        if (query.DesignationId is Guid designationId)
        {
            employees = employees.Where(e =>
                currentHistory.Any(h => h.EmployeeId == e.Id && h.DesignationId == designationId) ||
                !currentHistory.Any(h => h.EmployeeId == e.Id) && e.DateOfJoining <= businessDate && e.DesignationId == designationId);
        }

        if (query.Status is EmployeeStatus status)
        {
            employees = employees.Where(e =>
                currentHistory.Any(h => h.EmployeeId == e.Id && h.EmploymentStatus == status) ||
                !currentHistory.Any(h => h.EmployeeId == e.Id) && e.DateOfJoining <= businessDate && e.Status == status);
        }

        if (query.ReportingManagerId is Guid managerId)
        {
            employees = employees.Where(e =>
                currentHistory.Any(h => h.EmployeeId == e.Id && h.ManagerId == managerId) ||
                !currentHistory.Any(h => h.EmployeeId == e.Id) && e.DateOfJoining <= businessDate && e.ReportingManagerId == managerId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            employees = employees.Where(e =>
                (e.EmployeeCode ?? string.Empty).ToLower().Contains(search) ||
                e.FirstName.ToLower().Contains(search) ||
                e.LastName.ToLower().Contains(search) ||
                (e.Contact != null && e.Contact.OfficialEmail != null
                    ? e.Contact.OfficialEmail
                    : e.Email).ToLower().Contains(search));
        }

        return employees;
    }

    private IQueryable<Employee> ApplyEffectiveSort(
        IQueryable<Employee> employees, EmployeeQuery query, DateOnly businessDate)
    {
        var descending = query.SortDescending;
        var sortBy = query.SortBy?.Trim().ToLowerInvariant();
        var currentHistory = _db.EmployeeEmploymentHistory
            .Where(h => h.EffectiveFrom <= businessDate && (h.EffectiveTo == null || h.EffectiveTo >= businessDate));

        if (sortBy == "department")
        {
            var ordered = descending
                ? employees.OrderByDescending(e => currentHistory.Where(h => h.EmployeeId == e.Id).Select(h => h.Department!.Name).FirstOrDefault() ?? e.Department!.Name)
                : employees.OrderBy(e => currentHistory.Where(h => h.EmployeeId == e.Id).Select(h => h.Department!.Name).FirstOrDefault() ?? e.Department!.Name);
            return ordered.ThenBy(e => e.Id);
        }

        if (sortBy == "designation")
        {
            var ordered = descending
                ? employees.OrderByDescending(e => currentHistory.Where(h => h.EmployeeId == e.Id).Select(h => h.Designation!.Name).FirstOrDefault() ?? e.Designation!.Name)
                : employees.OrderBy(e => currentHistory.Where(h => h.EmployeeId == e.Id).Select(h => h.Designation!.Name).FirstOrDefault() ?? e.Designation!.Name);
            return ordered.ThenBy(e => e.Id);
        }

        if (sortBy == "status")
        {
            var ordered = descending
                ? employees.OrderByDescending(e => currentHistory.Where(h => h.EmployeeId == e.Id).Select(h => (EmployeeStatus?)h.EmploymentStatus).FirstOrDefault() ?? e.Status)
                : employees.OrderBy(e => currentHistory.Where(h => h.EmployeeId == e.Id).Select(h => (EmployeeStatus?)h.EmploymentStatus).FirstOrDefault() ?? e.Status);
            return ordered.ThenBy(e => e.Id);
        }

        return ApplySort(employees, query);
    }

    /// <summary>
    /// Orders by one of <see cref="EmployeeQuery.SortFields"/>. Sorting happens on the entity rather than
    /// the DTO because two of the fields (first/last name, created date) are not on the list DTO at all.
    /// Every branch ends with the unique id so paging cannot repeat or skip rows.
    /// </summary>
    private static IQueryable<Employee> ApplySort(IQueryable<Employee> employees, EmployeeQuery query)
    {
        var descending = query.SortDescending;

        var ordered = query.SortBy?.Trim().ToLowerInvariant() switch
        {
            "firstname" => descending
                ? employees.OrderByDescending(e => e.FirstName)
                : employees.OrderBy(e => e.FirstName),
            "lastname" => descending
                ? employees.OrderByDescending(e => e.LastName)
                : employees.OrderBy(e => e.LastName),
            "email" => descending
                ? employees.OrderByDescending(e =>
                    e.Contact != null && e.Contact.OfficialEmail != null ? e.Contact.OfficialEmail : e.Email)
                : employees.OrderBy(e =>
                    e.Contact != null && e.Contact.OfficialEmail != null ? e.Contact.OfficialEmail : e.Email),
            "department" => descending
                ? employees.OrderByDescending(e => e.Department!.Name)
                : employees.OrderBy(e => e.Department!.Name),
            "designation" => descending
                ? employees.OrderByDescending(e => e.Designation!.Name)
                : employees.OrderBy(e => e.Designation!.Name),
            "status" => descending
                ? employees.OrderByDescending(e => e.Status)
                : employees.OrderBy(e => e.Status),
            "dateofjoining" => descending
                ? employees.OrderByDescending(e => e.DateOfJoining)
                : employees.OrderBy(e => e.DateOfJoining),
            "createddate" => descending
                ? employees.OrderByDescending(e => e.CreatedDate)
                : employees.OrderBy(e => e.CreatedDate),
            _ => descending
                ? employees.OrderByDescending(e => e.EmployeeCode)
                : employees.OrderBy(e => e.EmployeeCode)
        };

        return ordered.ThenBy(e => e.Id);
    }

    private static IQueryable<EmployeeDto> ProjectDetail(IQueryable<Employee> employees) =>
        employees.Select(e => new EmployeeDto(
            e.Id,
            e.EmployeeCode ?? string.Empty,
            e.Salutation,
            e.FirstName,
            e.MiddleName,
            e.LastName,
            e.FirstName + " " + e.LastName,
            e.Contact != null && e.Contact.OfficialEmail != null ? e.Contact.OfficialEmail : e.Email,
            e.Contact != null && e.Contact.OfficialPhone != null ? e.Contact.OfficialPhone : e.Phone,
            e.DateOfBirth,
            e.Gender,
            e.BloodGroup,
            e.MaritalStatus,
            e.BirthCountry,
            e.BirthState,
            e.BirthCity,
            e.BirthCountryId,
            e.BirthCountryRef != null ? e.BirthCountryRef.Name : null,
            e.BirthStateId,
            e.BirthStateRef != null ? e.BirthStateRef.Name : null,
            e.BirthCityId,
            e.BirthCityRef != null ? e.BirthCityRef.Name : null,
            e.Religion,
            e.Caste,
            e.DateOfJoining,
            e.GroupDateOfJoining,
            e.DateOfLeaving,
            e.Status,
            e.JobStatus,
            e.GroupId,
            e.DepartmentId,
            e.Department != null ? e.Department.Name : null,
            e.DesignationId,
            e.Designation != null ? e.Designation.Name : null,
            e.ReportingManagerId,
            e.ReportingManager == null ? null : e.ReportingManager.FirstName + " " + e.ReportingManager.LastName,
            e.EmployeeType,
            EmployeeDto.MaskAadhaar(e.AadhaarNumber),
            EmployeeDto.MaskPan(e.PanNumber),
            EmployeeDto.MaskNumericId(e.PfNumber),
            EmployeeDto.MaskNumericId(e.UanNumber),
            EmployeeDto.MaskNumericId(e.EsicNumber),
            EmployeeDto.MaskNumericId(e.MediclaimNumber),
            e.Gratuity,
            e.Pension,
            e.CostCenterCode,
            e.PayrollLocation,
            e.EsicApplicable,
            e.Citizenship,
            e.LanguageKnown,
            e.ProfilePictureUrl,
            e.Address,
            e.CreatedDate,
            e.ModifiedDate));

    /// <summary>
    /// Reads the saved row back through the same projection the GET endpoint uses, so a create/update
    /// response and a subsequent fetch can never disagree — and so the joined department, designation and
    /// manager names come from one place instead of being assembled by hand.
    /// </summary>
    private async Task<Result<EmployeeDto>> ReloadAsync(Guid id, string message, CancellationToken cancellationToken)
    {
        var saved = await ProjectDetail(_db.Employees.AsNoTracking().Where(e => e.Id == id))
            .FirstOrDefaultAsync(cancellationToken);

        if (saved is not null)
        {
            var effective = await ApplyCurrentEmploymentAsync(saved, cancellationToken);
            if (!effective.Succeeded)
                return effective;
            saved = effective.Value!;
        }

        return saved is null
            ? Result<EmployeeDto>.NotFound(NotFoundMessage)
            : Result<EmployeeDto>.Success(saved, message);
    }

    private async Task<Result<EmployeeDto>> ApplyCurrentEmploymentAsync(
        EmployeeDto employee, CancellationToken cancellationToken)
    {
        var businessDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var current = await _db.EmployeeEmploymentHistory.AsNoTracking()
            .Where(h => h.EmployeeId == employee.Id && h.EffectiveFrom <= businessDate &&
                        (h.EffectiveTo == null || h.EffectiveTo >= businessDate))
            .OrderByDescending(h => h.EffectiveFrom)
            .ThenByDescending(h => h.CreatedDate)
            .Select(h => new
            {
                h.EffectiveFrom,
                h.EmploymentStatus,
                h.DepartmentId,
                DepartmentName = h.Department == null ? null : h.Department.Name,
                h.DesignationId,
                DesignationName = h.Designation == null ? null : h.Designation.Name,
                h.ManagerId,
                ManagerName = h.Manager == null ? null : h.Manager.FirstName + " " + h.Manager.LastName,
                EmployeeType = h.EmployeeType == null ? null : h.EmployeeType.Name,
                CostCenterCode = h.CostCenter == null ? null : h.CostCenter.Code
            })
            .Take(2)
            .ToListAsync(cancellationToken);

        if (current.Count > 1)
        {
            return Result<EmployeeDto>.Conflict("Employment history contains multiple current records.");
        }

        var record = current.SingleOrDefault();
        if (record is null)
        {
            return Result<EmployeeDto>.Success(employee);
        }

        return Result<EmployeeDto>.Success(employee with
        {
            DateOfLeaving = record.EmploymentStatus == EmployeeStatus.Active ? null : record.EffectiveFrom,
            Status = record.EmploymentStatus,
            DepartmentId = record.DepartmentId,
            DepartmentName = record.DepartmentName,
            DesignationId = record.DesignationId,
            DesignationName = record.DesignationName,
            ReportingManagerId = record.ManagerId,
            ReportingManagerName = record.ManagerName,
            EmployeeType = record.EmployeeType,
            CostCenterCode = record.CostCenterCode
        });
    }

    /// <summary>
    /// Checks that a department, designation and/or manager on the request exist <em>within the caller's
    /// tenant</em> — the queries run through the global filter, so another tenant's id is reported as
    /// nonexistent. Department, designation and manager are all optional (they belong to later Employee
    /// sections), so each is validated only when a value is supplied. Returns null when everything resolves.
    /// </summary>
    private async Task<Result<EmployeeDto>?> ValidateReferencesAsync(
        EmployeeRequest request, Employee? existing, CancellationToken cancellationToken)
    {
        if (request.DepartmentId is Guid departmentId)
        {
            var department = await _db.Departments.AsNoTracking()
                .Where(d => d.Id == departmentId)
                .Select(d => new { d.IsActive })
                .FirstOrDefaultAsync(cancellationToken);

            if (department is null)
            {
                return Result<EmployeeDto>.Invalid("departmentId", "The selected department does not exist.");
            }

            if (!department.IsActive && existing?.DepartmentId != request.DepartmentId)
            {
                return Result<EmployeeDto>.Invalid(
                    "departmentId", "The selected department is no longer active and cannot be assigned.");
            }
        }

        if (request.DesignationId is Guid designationId)
        {
            var designation = await _db.Designations.AsNoTracking()
                .Where(d => d.Id == designationId)
                .Select(d => new { d.IsActive })
                .FirstOrDefaultAsync(cancellationToken);

            if (designation is null)
            {
                return Result<EmployeeDto>.Invalid("designationId", "The selected designation does not exist.");
            }

            if (!designation.IsActive && existing?.DesignationId != request.DesignationId)
            {
                return Result<EmployeeDto>.Invalid(
                    "designationId", "The selected designation is no longer active and cannot be assigned.");
            }
        }

        if (request.ReportingManagerId is Guid managerId)
        {
            var manager = await _db.Employees.AsNoTracking()
                .Where(e => e.Id == managerId)
                .Select(e => new { e.Status })
                .FirstOrDefaultAsync(cancellationToken);

            if (manager is null)
            {
                return Result<EmployeeDto>.Invalid("reportingManagerId", "The selected manager does not exist.");
            }

            if (manager.Status != EmployeeStatus.Active && existing?.ReportingManagerId != managerId)
            {
                return Result<EmployeeDto>.Invalid(
                    "reportingManagerId", "The selected manager has left the organization and cannot be assigned.");
            }
        }

        // Birth location cascading validation.
        if (request.BirthStateId.HasValue)
        {
            if (!request.BirthCountryId.HasValue)
            {
                return Result<EmployeeDto>.Invalid("birthCountryId", "Birth country is required when a birth state is selected.");
            }

            var stateInCountry = await _db.States.AsNoTracking()
                .AnyAsync(s => s.Id == request.BirthStateId.Value && s.CountryId == request.BirthCountryId.Value, cancellationToken);

            if (!stateInCountry)
            {
                return Result<EmployeeDto>.Invalid("birthStateId", "The selected state does not belong to the selected country.");
            }
        }

        if (request.BirthCityId.HasValue)
        {
            if (!request.BirthStateId.HasValue)
            {
                return Result<EmployeeDto>.Invalid("birthStateId", "Birth state is required when a birth city is selected.");
            }

            var cityInState = await _db.Cities.AsNoTracking()
                .AnyAsync(c => c.Id == request.BirthCityId.Value && c.StateId == request.BirthStateId.Value, cancellationToken);

            if (!cityInState)
            {
                return Result<EmployeeDto>.Invalid("birthCityId", "The selected city does not belong to the selected state.");
            }
        }

        return null;
    }

    /// <summary>
    /// Walks up from the proposed manager to see whether the employee already appears in that reporting
    /// line, which would make the hierarchy circular. One query per level: reporting lines are shallow, and
    /// loading a tenant's whole org chart to answer this would cost far more.
    /// </summary>
    private async Task<bool> WouldCreateReportingCycleAsync(
        Guid employeeId, Guid proposedManagerId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid> { employeeId };
        var cursor = (Guid?)proposedManagerId;

        for (var depth = 0; cursor is Guid currentId; depth++)
        {
            if (depth >= MaxReportingDepth)
            {
                _logger.LogWarning(
                    "Reporting-line walk from employee {EmployeeId} exceeded {MaxDepth} levels; treating as a cycle.",
                    employeeId, MaxReportingDepth);
                return true;
            }

            // Adding fails either because we reached the employee itself (a genuine cycle) or because the
            // existing data is already circular. Both mean this reference must not be accepted.
            if (!visited.Add(currentId))
            {
                return true;
            }

            cursor = await _db.Employees.AsNoTracking()
                .Where(e => e.Id == currentId)
                .Select(e => e.ReportingManagerId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Returns a conflict when another employee in this tenant already uses the code or the work email, or
    /// null when both are free. Matched case-insensitively so "EMP-1" and "emp-1" cannot both be created.
    /// </summary>
    private async Task<Result<EmployeeDto>?> FindConflictAsync(
        string employeeCode, string email, Guid? excludeId, CancellationToken cancellationToken)
    {
        var normalizedCode = employeeCode.ToLowerInvariant();
        var normalizedEmail = email.ToLowerInvariant();

        var candidates = _db.Employees.AsNoTracking();
        if (excludeId is Guid exclude)
        {
            candidates = candidates.Where(e => e.Id != exclude);
        }

        var clashes = await candidates
            .Where(e => (e.EmployeeCode ?? string.Empty).ToLower() == normalizedCode || e.Email.ToLower() == normalizedEmail)
            .Select(e => new { e.EmployeeCode, e.Email })
            .ToListAsync(cancellationToken);

        if (clashes.Count == 0)
        {
            return null;
        }

        if (clashes.Any(c => string.Equals(c.EmployeeCode, employeeCode, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<EmployeeDto>.Conflict(
                $"An employee with code '{employeeCode}' already exists.",
                [new ValidationError("employeeCode", "This employee code is already in use.")]);
        }

        return Result<EmployeeDto>.Conflict(
            "An employee with this email address already exists.",
            [new ValidationError("email", "This email address is already in use.")]);
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Creates an employee from Personal Details, assigning the employee code and a uniqueness-safe
    /// placeholder email (the real work email is a separate Contact concern). The employee is created with
    /// no department, designation or reporting manager — those are captured by later sections.
    /// </summary>
    private async Task<Result<EmployeeDto>> CreatePersonalDetailsCoreAsync(
        EmployeePersonalDetailsRequest request,
        Guid tenantId,
        CancellationToken cancellationToken,
        bool canEditSensitive)
    {
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            // Employee Code is assigned atomically with Initial Employment, once its
            // organizational context is available for the selected generation method.
            EmployeeCode = null,
            // A uniqueness-safe placeholder until the Contact section supplies the real work email.
            Email = $"emp-{Guid.NewGuid():N}@placeholder.local",
            DateOfJoining = request.DateOfJoining
        };

        ApplyPersonalDetails(employee, request, canEditSensitive: canEditSensitive);

        _db.Employees.Add(employee);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created employee {EmployeeId} in tenant {TenantId} from Personal Details.", employee.Id, tenantId);

        return await ReloadAsync(employee.Id, "Employee created.", cancellationToken);
    }

    /// <summary>Applies only the Personal Details fields of a request onto an employee record.</summary>
    private void ApplyPersonalDetails(
        Employee employee,
        EmployeePersonalDetailsRequest request,
        bool preserveSensitive = false,
        bool canEditSensitive = true)
    {
        employee.Salutation = Normalize(request.Salutation);
        employee.FirstName = request.FirstName.Trim();
        employee.MiddleName = Normalize(request.MiddleName);
        employee.LastName = request.LastName.Trim();
        employee.DateOfBirth = request.DateOfBirth;
        employee.Gender = request.Gender;
        employee.BloodGroup = request.BloodGroup;
        employee.MaritalStatus = request.MaritalStatus;
        employee.BirthCountryId = request.BirthCountryId;
        employee.BirthStateId = request.BirthStateId;
        employee.BirthCityId = request.BirthCityId;
        employee.Religion = Normalize(request.Religion);
        employee.Caste = Normalize(request.Caste);
        employee.Citizenship = Normalize(request.Citizenship);
        if (canEditSensitive)
        {
            employee.EsicApplicable = request.EsicApplicable;
            employee.EsicNumber = Normalize(request.EsicNumber);
            employee.MediclaimNumber = Normalize(request.MediclaimNumber);
        }
        employee.Gratuity = request.Gratuity;
        employee.Pension = request.Pension;
        // Aadhaar, PAN, PF and UAN are returned only masked on read, so an edit form cannot resend the
        // originals. Leave-blank-means-unchanged here — a create always carries the full values.
        if (canEditSensitive)
        {
            employee.PfNumber = OverwriteOrKeep(employee.PfNumber, request.PfNumber, preserveSensitive);
            employee.UanNumber = OverwriteOrKeep(employee.UanNumber, request.UanNumber, preserveSensitive);
            employee.AadhaarNumber = OverwriteOrKeep(employee.AadhaarNumber, request.AadhaarNumber, preserveSensitive);
            employee.PanNumber = OverwriteOrKeep(employee.PanNumber, request.PanNumber, preserveSensitive);
        }
        employee.DateOfJoining = request.DateOfJoining;
        employee.JobStatus = Normalize(request.JobStatus);
    }

    /// <summary>
    /// Applies a value that may be masked on read. When <paramref name="preserveSensitive"/> is set and the
    /// incoming value is blank, the stored value is kept; otherwise the incoming value (normalized) wins.
    /// </summary>
    private static string? OverwriteOrKeep(string? current, string? incoming, bool preserveSensitive) =>
        preserveSensitive && string.IsNullOrWhiteSpace(incoming) ? current : Normalize(incoming);

    /// <summary>
    /// Birth location cascade for the Personal Details DTO: a selected state must belong to the selected
    /// country, and a selected city must belong to the selected state (the same rule the shared employee
    /// write enforces).
    /// </summary>
    private async Task<Result<EmployeeDto>?> ValidateBirthLocationAsync(
        EmployeePersonalDetailsRequest request, CancellationToken cancellationToken)
    {
        if (request.BirthStateId.HasValue)
        {
            if (!request.BirthCountryId.HasValue)
            {
                return Result<EmployeeDto>.Invalid("birthCountryId", "Birth country is required when a birth state is selected.");
            }

            var stateInCountry = await _db.States.AsNoTracking()
                .AnyAsync(s => s.Id == request.BirthStateId.Value && s.CountryId == request.BirthCountryId.Value, cancellationToken);

            if (!stateInCountry)
            {
                return Result<EmployeeDto>.Invalid("birthStateId", "The selected state does not belong to the selected country.");
            }
        }

        if (request.BirthCityId.HasValue)
        {
            if (!request.BirthStateId.HasValue)
            {
                return Result<EmployeeDto>.Invalid("birthStateId", "Birth state is required when a birth city is selected.");
            }

            var cityInState = await _db.Cities.AsNoTracking()
                .AnyAsync(c => c.Id == request.BirthCityId.Value && c.StateId == request.BirthStateId.Value, cancellationToken);

            if (!cityInState)
            {
                return Result<EmployeeDto>.Invalid("birthCityId", "The selected city does not belong to the selected state.");
            }
        }

        return null;
    }

    /// <summary>
    /// Produces the employee code for a new hire according to the tenant's configuration. When the tenant
    /// auto-generates codes the code is <c>Prefix + NextNumber</c> (padded to <see cref="EmployeeCodeConfig.Padding"/>
    /// digits) and <c>advanceCounter</c> tells the caller to advance the counter; when it does not, a
    /// client-supplied code is required and no counter is touched. Returns the resolved code (or an error).
    /// </summary>
    private async Task<(Result<EmployeeDto>? Error, string? Code, bool AdvanceCounter, string Prefix, int Padding)>
        ResolveEmployeeCodeAsync(string? clientCode, CancellationToken cancellationToken)
    {
        var config = await _db.EmployeeCodeConfigs.AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        // A tenant with no configuration row defaults to auto-generation.
        var autoGenerate = config?.AutoGenerate ?? true;
        var prefix = string.IsNullOrWhiteSpace(config?.Prefix) ? "EMP" : config.Prefix.Trim().ToUpperInvariant();
        var padding = Math.Clamp(config?.Padding ?? 0, 0, 10);
        var nextNumber = Math.Max(1, config?.NextNumber ?? 1);

        if (autoGenerate)
        {
            var code = prefix + nextNumber.ToString(padding == 0 ? string.Empty : $"D{padding}");

            return (null, code, true, prefix, padding);
        }

        // Manual-code configuration: the client supplies the code.
        if (string.IsNullOrWhiteSpace(clientCode))
        {
            return (
                Result<EmployeeDto>.Invalid(
                    "employeeCode", "Employee code is required because this organization assigns codes manually."),
                null, false, string.Empty, 0);
        }

        var normalized = clientCode.Trim();
        var conflict = await FindConflictAsync(normalized, Guid.NewGuid().ToString("N") + "@placeholder.local", excludeId: null, cancellationToken);
        if (conflict is not null)
        {
            return (conflict, null, false, string.Empty, 0);
        }

        return (null, normalized, false, string.Empty, 0);
    }

    /// <summary>
    /// Advances the tenant's employee-code counter once an auto-generated code has been consumed.
    /// </summary>
    private async Task PersistEmployeeCodeCounterAsync(
        string prefix, int padding, CancellationToken cancellationToken)
    {
        var config = await _db.EmployeeCodeConfigs.FirstOrDefaultAsync(cancellationToken);
        if (config is null)
        {
            config = new EmployeeCodeConfig
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.TenantId!.Value,
                AutoGenerate = true,
                Prefix = prefix,
                Padding = padding,
                NextNumber = 2
            };
            _db.EmployeeCodeConfigs.Add(config);
        }
        else
        {
            config.NextNumber = Math.Max(config.NextNumber + 1, 2);
        }
    }
}
