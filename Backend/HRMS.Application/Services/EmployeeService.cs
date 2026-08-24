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

        var employees = ApplySort(ApplyFilters(_db.Employees.AsNoTracking(), query), query);

        var page = await employees
            .Select(e => new EmployeeListItemDto(
                e.Id,
                e.EmployeeCode,
                e.FirstName + " " + e.LastName,
                e.Email,
                e.Department!.Name,
                e.Designation!.Name,
                e.Status,
                e.DateOfJoining))
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

        return employee is null
            ? Result<EmployeeDto>.NotFound(NotFoundMessage)
            : Result<EmployeeDto>.Success(employee);
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
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            Phone = Normalize(request.Phone),
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            DateOfJoining = request.DateOfJoining,
            DateOfLeaving = request.DateOfLeaving,
            Status = request.Status,
            DepartmentId = request.DepartmentId,
            DesignationId = request.DesignationId,
            ReportingManagerId = request.ReportingManagerId,
            Address = Normalize(request.Address)
        };

        _db.Employees.Add(employee);

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

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
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
        employee.FirstName = request.FirstName.Trim();
        employee.LastName = request.LastName.Trim();
        employee.Email = email;
        employee.Phone = Normalize(request.Phone);
        employee.DateOfBirth = request.DateOfBirth;
        employee.Gender = request.Gender;
        employee.DateOfJoining = request.DateOfJoining;
        employee.DateOfLeaving = request.DateOfLeaving;
        employee.Status = request.Status;
        employee.DepartmentId = request.DepartmentId;
        employee.DesignationId = request.DesignationId;
        employee.ReportingManagerId = request.ReportingManagerId;
        employee.Address = Normalize(request.Address);

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
            "Employee Code", "First Name", "Last Name", "Email", "Phone", "Date of Birth", "Gender",
            "Date of Joining", "Date of Leaving", "Status", "Department", "Designation",
            "Reporting Manager", "Address");

        foreach (var row in rows)
        {
            csv.AppendRow(
                row.EmployeeCode,
                row.FirstName,
                row.LastName,
                row.Email,
                row.Phone,
                row.DateOfBirth?.ToString(DateFormat),
                row.Gender.ToString(),
                row.DateOfJoining.ToString(DateFormat),
                row.DateOfLeaving?.ToString(DateFormat),
                row.Status.ToString(),
                row.DepartmentName,
                row.DesignationName,
                row.ReportingManagerName,
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
                e.EmployeeCode.ToLower().Contains(search) ||
                e.FirstName.ToLower().Contains(search) ||
                e.LastName.ToLower().Contains(search) ||
                e.Email.ToLower().Contains(search));
        }

        return employees;
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
                ? employees.OrderByDescending(e => e.Email)
                : employees.OrderBy(e => e.Email),
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
            e.EmployeeCode,
            e.FirstName,
            e.LastName,
            e.FirstName + " " + e.LastName,
            e.Email,
            e.Phone,
            e.DateOfBirth,
            e.Gender,
            e.DateOfJoining,
            e.DateOfLeaving,
            e.Status,
            e.DepartmentId,
            e.Department!.Name,
            e.DesignationId,
            e.Designation!.Name,
            e.ReportingManagerId,
            e.ReportingManager == null ? null : e.ReportingManager.FirstName + " " + e.ReportingManager.LastName,
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

        return saved is null
            ? Result<EmployeeDto>.NotFound(NotFoundMessage)
            : Result<EmployeeDto>.Success(saved, message);
    }

    /// <summary>
    /// Checks that the department, designation and manager on the request exist <em>within the caller's
    /// tenant</em> — the queries run through the global filter, so another tenant's id is reported as
    /// nonexistent. Returns null when everything resolves.
    /// </summary>
    private async Task<Result<EmployeeDto>?> ValidateReferencesAsync(
        EmployeeRequest request, Employee? existing, CancellationToken cancellationToken)
    {
        var department = await _db.Departments.AsNoTracking()
            .Where(d => d.Id == request.DepartmentId)
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

        var designation = await _db.Designations.AsNoTracking()
            .Where(d => d.Id == request.DesignationId)
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
            .Where(e => e.EmployeeCode.ToLower() == normalizedCode || e.Email.ToLower() == normalizedEmail)
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
}
