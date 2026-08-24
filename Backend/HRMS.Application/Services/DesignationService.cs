using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Designations;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

/// <summary>
/// Designation (job title) business logic. Deliberately a mirror of <see cref="DepartmentService"/> rather
/// than a shared generic base: the two are independent concepts that happen to look alike today, and a
/// generic "code + name lookup service" would make each one harder to read to save a page of code. If a
/// third such entity appears, that is the point to reconsider.
/// <para>
/// The tenant rules are identical: all access goes through the tenant-filtered <c>_db.Designations</c>, no
/// method takes a tenant id, and uniqueness is scoped per tenant.
/// </para>
/// </summary>
public class DesignationService : IDesignationService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Designation not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<DesignationService> _logger;

    public DesignationService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<DesignationService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<PagedResult<DesignationDto>>> GetAsync(
        DesignationQuery query, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<PagedResult<DesignationDto>>.Unauthorized(NoTenantMessage);
        }

        var designations = _db.Designations.AsNoTracking();

        if (query.IsActive.HasValue)
        {
            designations = designations.Where(d => d.IsActive == query.IsActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            designations = designations.Where(d =>
                d.Code.ToLower().Contains(search) || d.Name.ToLower().Contains(search));
        }

        var page = await Project(ApplySort(designations, query))
            .ToPagedResultAsync(query, cancellationToken);

        return Result<PagedResult<DesignationDto>>.Success(page);
    }

    public async Task<Result<DesignationDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<DesignationDto>.Unauthorized(NoTenantMessage);
        }

        var designation = await Project(_db.Designations.AsNoTracking().Where(d => d.Id == id))
            .FirstOrDefaultAsync(cancellationToken);

        return designation is null
            ? Result<DesignationDto>.NotFound(NotFoundMessage)
            : Result<DesignationDto>.Success(designation);
    }

    public async Task<Result<DesignationDto>> CreateAsync(
        DesignationRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<DesignationDto>.Unauthorized(NoTenantMessage);
        }

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        var conflict = await FindConflictAsync(code, name, excludeId: null, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        var designation = new Designation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            Name = name,
            Description = Normalize(request.Description),
            IsActive = request.IsActive
        };

        _db.Designations.Add(designation);

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
                "Designation create for tenant {TenantId} lost a uniqueness race on the database index.", tenantId);
            return raced;
        }

        _logger.LogInformation(
            "Created designation {DesignationId} in tenant {TenantId}.", designation.Id, tenantId);

        return Result<DesignationDto>.Success(ToDto(designation, employeeCount: 0), "Designation created.");
    }

    public async Task<Result<DesignationDto>> UpdateAsync(
        Guid id, DesignationRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<DesignationDto>.Unauthorized(NoTenantMessage);
        }

        var designation = await _db.Designations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (designation is null)
        {
            return Result<DesignationDto>.NotFound(NotFoundMessage);
        }

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        var conflict = await FindConflictAsync(code, name, excludeId: id, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        designation.Code = code;
        designation.Name = name;
        designation.Description = Normalize(request.Description);
        designation.IsActive = request.IsActive;

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
                "Designation update for {DesignationId} lost a uniqueness race on the database index.", id);
            return raced;
        }

        _logger.LogInformation("Updated designation {DesignationId} in tenant {TenantId}.", id, tenantId);

        var employeeCount = await _db.Employees.CountAsync(e => e.DesignationId == id, cancellationToken);
        return Result<DesignationDto>.Success(ToDto(designation, employeeCount), "Designation updated.");
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<bool>.Unauthorized(NoTenantMessage);
        }

        var designation = await _db.Designations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (designation is null)
        {
            return Result<bool>.NotFound(NotFoundMessage);
        }

        var employeeCount = await _db.Employees.CountAsync(e => e.DesignationId == id, cancellationToken);
        if (employeeCount > 0)
        {
            return Result<bool>.Conflict(
                $"This designation is held by {employeeCount} employee(s). Reassign them, or mark the designation inactive instead of deleting it.");
        }

        _db.Designations.Remove(designation);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("Designation {DesignationId} could not be deleted: it is still referenced.", id);
            return Result<bool>.Conflict(
                "This designation is still held by at least one employee and cannot be deleted.");
        }

        _logger.LogInformation("Deleted designation {DesignationId} from tenant {TenantId}.", id, tenantId);

        return Result<bool>.Success(true, "Designation deleted.");
    }

    private static IQueryable<DesignationDto> Project(IQueryable<Designation> designations) =>
        designations.Select(d => new DesignationDto(
            d.Id,
            d.Code,
            d.Name,
            d.Description,
            d.IsActive,
            d.Employees.Count,
            d.CreatedDate,
            d.ModifiedDate));

    /// <summary>
    /// Orders by one of <see cref="DesignationQuery.SortFields"/>, always finishing with the unique id so
    /// that paging over equal sort keys stays stable. Applied to the entity query before projection, so the
    /// provider can see the column rather than a constructor argument.
    /// </summary>
    private static IQueryable<Designation> ApplySort(IQueryable<Designation> designations, DesignationQuery query)
    {
        var descending = query.SortDescending;

        var ordered = query.SortBy?.Trim().ToLowerInvariant() switch
        {
            "name" => descending
                ? designations.OrderByDescending(d => d.Name)
                : designations.OrderBy(d => d.Name),
            "employeecount" => descending
                ? designations.OrderByDescending(d => d.Employees.Count)
                : designations.OrderBy(d => d.Employees.Count),
            "isactive" => descending
                ? designations.OrderByDescending(d => d.IsActive)
                : designations.OrderBy(d => d.IsActive),
            "createddate" => descending
                ? designations.OrderByDescending(d => d.CreatedDate)
                : designations.OrderBy(d => d.CreatedDate),
            _ => descending
                ? designations.OrderByDescending(d => d.Code)
                : designations.OrderBy(d => d.Code)
        };

        return ordered.ThenBy(d => d.Id);
    }

    private async Task<Result<DesignationDto>?> FindConflictAsync(
        string code, string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var normalizedCode = code.ToLowerInvariant();
        var normalizedName = name.ToLowerInvariant();

        var candidates = _db.Designations.AsNoTracking();
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
            return Result<DesignationDto>.Conflict(
                $"A designation with code '{code}' already exists.",
                [new ValidationError("code", "This code is already in use.")]);
        }

        return Result<DesignationDto>.Conflict(
            $"A designation named '{name}' already exists.",
            [new ValidationError("name", "This name is already in use.")]);
    }

    private static DesignationDto ToDto(Designation designation, int employeeCount) =>
        new(
            designation.Id,
            designation.Code,
            designation.Name,
            designation.Description,
            designation.IsActive,
            employeeCount,
            designation.CreatedDate,
            designation.ModifiedDate);

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
