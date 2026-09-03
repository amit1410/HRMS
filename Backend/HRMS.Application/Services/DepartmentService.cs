using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Departments;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

/// <summary>
/// Department business logic.
/// <para>
/// Every read and write goes through <c>_db.Departments</c>, which the DbContext has a tenant global query
/// filter on, so a row belonging to another organization simply is not there. Nothing in this class
/// accepts or compares a caller-supplied tenant id; the only tenant it knows is the one
/// <see cref="ITenantContext"/> resolved from the authenticated token.
/// </para>
/// <para>
/// Uniqueness is pre-checked here rather than left to the unique index. The index is still the real
/// guarantee — but a constraint violation surfaces as a <see cref="DbUpdateException"/>, which the API's
/// exception middleware turns into a 500. A duplicate code is a client mistake, so it is detected up front
/// and reported as a conflict, with the exception path kept as a backstop for the concurrent case.
/// </para>
/// </summary>
public class DepartmentService : IDepartmentService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Department not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DepartmentService> _logger;

    public DepartmentService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<DepartmentService> logger,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task<Result<PagedResult<DepartmentDto>>> GetAsync(
        DepartmentQuery query, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<PagedResult<DepartmentDto>>.Unauthorized(NoTenantMessage);
        }

        var departments = _db.Departments.AsNoTracking();

        if (query.IsActive.HasValue)
        {
            departments = departments.Where(d => d.IsActive == query.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // Lower-cased explicitly so the search behaves the same on SQL Server (case-insensitive by
            // default collation) and SQLite (case-sensitive by default).
            var search = query.Search.Trim().ToLowerInvariant();
            departments = departments.Where(d =>
                d.Code.ToLower().Contains(search) || d.Name.ToLower().Contains(search));
        }

        var businessDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var page = await Project(ApplySort(departments, query), businessDate)
            .ToPagedResultAsync(query, cancellationToken);

        return Result<PagedResult<DepartmentDto>>.Success(page);
    }

    public async Task<Result<DepartmentDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<DepartmentDto>.Unauthorized(NoTenantMessage);
        }

        var businessDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var department = await Project(_db.Departments.AsNoTracking().Where(d => d.Id == id), businessDate)
            .FirstOrDefaultAsync(cancellationToken);

        return department is null
            ? Result<DepartmentDto>.NotFound(NotFoundMessage)
            : Result<DepartmentDto>.Success(department);
    }

    public async Task<Result<DepartmentDto>> CreateAsync(
        DepartmentRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<DepartmentDto>.Unauthorized(NoTenantMessage);
        }

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        var conflict = await FindConflictAsync(code, name, excludeId: null, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        var department = new Department
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            Name = name,
            Description = Normalize(request.Description),
            IsActive = request.IsActive
        };

        _db.Departments.Add(department);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await FindConflictAsync(code, name, excludeId: null, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            _logger.LogWarning(
                "Department create for tenant {TenantId} lost a uniqueness race on the database index.", tenantId);
            return raced;
        }

        _logger.LogInformation(
            "Created department {DepartmentId} in tenant {TenantId}.", department.Id, tenantId);

        return Result<DepartmentDto>.Success(ToDto(department, employeeCount: 0), "Department created.");
    }

    public async Task<Result<DepartmentDto>> UpdateAsync(
        Guid id, DepartmentRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<DepartmentDto>.Unauthorized(NoTenantMessage);
        }

        // Tracked (not AsNoTracking) because it is about to be modified. The tenant filter still applies,
        // so another tenant's id reads as "not found" and can never be updated from here.
        var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (department is null)
        {
            return Result<DepartmentDto>.NotFound(NotFoundMessage);
        }

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        var conflict = await FindConflictAsync(code, name, excludeId: id, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        department.Code = code;
        department.Name = name;
        department.Description = Normalize(request.Description);
        department.IsActive = request.IsActive;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await FindConflictAsync(code, name, excludeId: id, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            _logger.LogWarning(
                "Department update for {DepartmentId} lost a uniqueness race on the database index.", id);
            return raced;
        }

        _logger.LogInformation("Updated department {DepartmentId} in tenant {TenantId}.", id, tenantId);

        var employeeCount = await _db.Employees.CountAsync(e => e.DepartmentId == id, cancellationToken);
        return Result<DepartmentDto>.Success(ToDto(department, employeeCount), "Department updated.");
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<bool>.Unauthorized(NoTenantMessage);
        }

        var department = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (department is null)
        {
            return Result<bool>.NotFound(NotFoundMessage);
        }

        // A department with people in it is not deleted, because the employees' history would lose the unit
        // they worked in. The caller is told to retire it (IsActive = false) instead.
        var employeeCount = await _db.Employees.CountAsync(e => e.DepartmentId == id, cancellationToken);
        if (employeeCount > 0)
        {
            return Result<bool>.Conflict(
                $"This department still has {employeeCount} employee(s) assigned. Reassign them, or mark the department inactive instead of deleting it.");
        }

        department.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Deactivated department {DepartmentId} in tenant {TenantId}.", id, tenantId);
        return Result<bool>.Success(true, "Department deactivated. Existing references were preserved.");
    }

    /// <summary>
    /// Projects to the DTO in SQL, so only the columns the client sees leave the database and the employee
    /// count is one aggregate rather than a loaded collection. The count reads through the Employee query
    /// filter, so it too is confined to the caller's tenant.
    /// </summary>
    private IQueryable<DepartmentDto> Project(IQueryable<Department> departments, DateOnly businessDate)
    {
        var currentHistory = _db.EmployeeEmploymentHistory
            .Where(h => h.EffectiveFrom <= businessDate && (h.EffectiveTo == null || h.EffectiveTo >= businessDate));

        return departments.Select(d => new DepartmentDto(
            d.Id,
            d.Code,
            d.Name,
            d.Description,
            d.IsActive,
            d.Employees.Count(e => !currentHistory.Any(h => h.EmployeeId == e.Id) && e.DateOfJoining <= businessDate && e.DepartmentId == d.Id) +
            currentHistory.Count(h => h.DepartmentId == d.Id &&
                !currentHistory.Any(other => other.EmployeeId == h.EmployeeId &&
                    (other.EffectiveFrom > h.EffectiveFrom ||
                     other.EffectiveFrom == h.EffectiveFrom && other.CreatedDate > h.CreatedDate))),
            d.CreatedDate,
            d.ModifiedDate));
    }

    /// <summary>
    /// Orders by one of <see cref="DepartmentQuery.SortFields"/>. The switch is exhaustive over that list —
    /// the validator rejects anything else, so the default arm only ever handles "code" and "no sort given".
    /// <para>
    /// Ordering is applied to the entity query and the projection happens afterwards, which is not a style
    /// choice: ordering a query that has already been projected asks the provider to order by a member of a
    /// constructed object, and it cannot see through the constructor to the column underneath.
    /// </para>
    /// <para>
    /// Every branch ends with a tiebreaker on the unique id. Without it, rows with equal sort keys have no
    /// defined relative order, and two requests for consecutive pages can return the same row twice while
    /// skipping another entirely.
    /// </para>
    /// </summary>
    private static IQueryable<Department> ApplySort(IQueryable<Department> departments, DepartmentQuery query)
    {
        var descending = query.SortDescending;

        var ordered = query.SortBy?.Trim().ToLowerInvariant() switch
        {
            "name" => descending
                ? departments.OrderByDescending(d => d.Name)
                : departments.OrderBy(d => d.Name),
            "employeecount" => descending
                ? departments.OrderByDescending(d => d.Employees.Count)
                : departments.OrderBy(d => d.Employees.Count),
            "isactive" => descending
                ? departments.OrderByDescending(d => d.IsActive)
                : departments.OrderBy(d => d.IsActive),
            "createddate" => descending
                ? departments.OrderByDescending(d => d.CreatedDate)
                : departments.OrderBy(d => d.CreatedDate),
            _ => descending
                ? departments.OrderByDescending(d => d.Code)
                : departments.OrderBy(d => d.Code)
        };

        return ordered.ThenBy(d => d.Id);
    }

    /// <summary>
    /// Returns a conflict result when another department in this tenant already uses the code or the name,
    /// or null when the values are free. Comparison is case-insensitive: "ENG" and "eng" are the same code
    /// to a human, and treating them as distinct would produce two departments nobody can tell apart.
    /// </summary>
    private async Task<Result<DepartmentDto>?> FindConflictAsync(
        string code, string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var normalizedCode = code.ToLowerInvariant();
        var normalizedName = name.ToLowerInvariant();

        var candidates = _db.Departments.AsNoTracking();
        if (excludeId is Guid exclude)
        {
            candidates = candidates.Where(d => d.Id != exclude);
        }

        var clashes = await candidates
            .Where(d => d.Code.ToLower() == normalizedCode || d.Name.ToLower() == normalizedName)
            .Select(d => new { d.Code, d.Name })
            .ToListAsync(cancellationToken);

        if (clashes.Count == 0)
        {
            return null;
        }

        if (clashes.Any(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<DepartmentDto>.Conflict(
                $"A department with code '{code}' already exists.",
                [new ValidationError("code", "This code is already in use.")]);
        }

        return Result<DepartmentDto>.Conflict(
            $"A department named '{name}' already exists.",
            [new ValidationError("name", "This name is already in use.")]);
    }

    private static DepartmentDto ToDto(Department department, int employeeCount) =>
        new(
            department.Id,
            department.Code,
            department.Name,
            department.Description,
            department.IsActive,
            employeeCount,
            department.CreatedDate,
            department.ModifiedDate);

    /// <summary>Trims optional text and turns whitespace-only input into null, so "empty" has one form.</summary>
    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
