using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Cities;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

/// <summary>
/// City management. Cities are global reference data (not tenant-scoped).
/// </summary>
public class CityService : ICityService
{
    private const string NotFoundMessage = "City not found.";

    private readonly IHrmsDbContext _db;
    private readonly ILogger<CityService> _logger;

    public CityService(IHrmsDbContext db, ILogger<CityService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<PagedResult<CityDto>>> GetAsync(
        CityQuery query, CancellationToken cancellationToken = default)
    {
        var cities = _db.Cities.AsNoTracking();

        if (query.StateId.HasValue)
            cities = cities.Where(c => c.StateId == query.StateId.Value);

        if (query.IsActive.HasValue)
            cities = cities.Where(c => c.IsActive == query.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            cities = cities.Where(c =>
                c.Code.ToLower().Contains(search) || c.Name.ToLower().Contains(search));
        }

        var page = await Project(ApplySort(cities, query))
            .ToPagedResultAsync(query, cancellationToken);

        return Result<PagedResult<CityDto>>.Success(page);
    }

    public async Task<Result<CityDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var city = await Project(_db.Cities.AsNoTracking().Where(c => c.Id == id))
            .FirstOrDefaultAsync(cancellationToken);

        return city is null
            ? Result<CityDto>.NotFound(NotFoundMessage)
            : Result<CityDto>.Success(city);
    }

    public async Task<Result<CityDto>> CreateAsync(
        CityRequest request, CancellationToken cancellationToken = default)
    {
        var state = await _db.States.AsNoTracking()
            .Where(s => s.Id == request.StateId)
            .Select(s => new { s.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (state is null)
            return Result<CityDto>.Invalid("stateId", "The selected state does not exist.");

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        var conflict = await FindConflictAsync(request.StateId, code, name, excludeId: null, cancellationToken);
        if (conflict is not null) return conflict;

        var city = new City
        {
            Id = Guid.NewGuid(),
            StateId = request.StateId,
            Code = code,
            Name = name,
            IsActive = request.IsActive
        };

        _db.Cities.Add(city);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await FindConflictAsync(request.StateId, code, name, excludeId: null, cancellationToken);
            if (raced is null) throw;
            _logger.LogWarning("City create lost a uniqueness race on the database index.");
            return raced;
        }

        _logger.LogInformation("Created city {CityId}.", city.Id);
        return Result<CityDto>.Success(ToDto(city, state.Name), "City created.");
    }

    public async Task<Result<CityDto>> UpdateAsync(
        Guid id, CityRequest request, CancellationToken cancellationToken = default)
    {
        var city = await _db.Cities.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (city is null)
            return Result<CityDto>.NotFound(NotFoundMessage);

        var state = await _db.States.AsNoTracking()
            .Where(s => s.Id == request.StateId)
            .Select(s => new { s.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (state is null)
            return Result<CityDto>.Invalid("stateId", "The selected state does not exist.");

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        var conflict = await FindConflictAsync(request.StateId, code, name, excludeId: id, cancellationToken);
        if (conflict is not null) return conflict;

        city.StateId = request.StateId;
        city.Code = code;
        city.Name = name;
        city.IsActive = request.IsActive;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await FindConflictAsync(request.StateId, code, name, excludeId: id, cancellationToken);
            if (raced is null) throw;
            _logger.LogWarning("City update for {CityId} lost a uniqueness race.", id);
            return raced;
        }

        _logger.LogInformation("Updated city {CityId}.", id);
        return Result<CityDto>.Success(ToDto(city, state.Name), "City updated.");
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var city = await _db.Cities.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (city is null)
            return Result<bool>.NotFound(NotFoundMessage);

        _db.Cities.Remove(city);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("City {CityId} could not be deleted: still referenced.", id);
            return Result<bool>.Conflict("This city is still referenced and cannot be deleted.");
        }

        _logger.LogInformation("Deleted city {CityId}.", id);
        return Result<bool>.Success(true, "City deleted.");
    }

    private static IQueryable<CityDto> Project(IQueryable<City> cities) =>
        cities.Select(c => new CityDto(
            c.Id,
            c.StateId,
            c.State!.Name,
            c.Code,
            c.Name,
            c.IsActive,
            c.CreatedDate,
            c.ModifiedDate));

    private static IQueryable<City> ApplySort(IQueryable<City> cities, CityQuery query)
    {
        var descending = query.SortDescending;
        var ordered = query.SortBy?.Trim().ToLowerInvariant() switch
        {
            "name" => descending ? cities.OrderByDescending(c => c.Name) : cities.OrderBy(c => c.Name),
            "stateid" => descending ? cities.OrderByDescending(c => c.StateId) : cities.OrderBy(c => c.StateId),
            "isactive" => descending ? cities.OrderByDescending(c => c.IsActive) : cities.OrderBy(c => c.IsActive),
            "createddate" => descending ? cities.OrderByDescending(c => c.CreatedDate) : cities.OrderBy(c => c.CreatedDate),
            _ => descending ? cities.OrderByDescending(c => c.Code) : cities.OrderBy(c => c.Code)
        };
        return ordered.ThenBy(c => c.Id);
    }

    private async Task<Result<CityDto>?> FindConflictAsync(
        Guid stateId, string code, string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var normalizedCode = code.ToLowerInvariant();
        var normalizedName = name.ToLowerInvariant();

        var candidates = _db.Cities.AsNoTracking();
        if (excludeId is Guid exclude)
            candidates = candidates.Where(c => c.Id != exclude);

        var clashes = await candidates
            .Where(c => c.StateId == stateId &&
                (c.Code.ToLower() == normalizedCode || c.Name.ToLower() == normalizedName))
            .Select(c => new { c.Code, c.Name })
            .ToListAsync(cancellationToken);

        if (clashes.Count == 0) return null;

        if (clashes.Any(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
            return Result<CityDto>.Conflict(
                $"A city with code '{code}' already exists in this state.",
                [new ValidationError("code", "This code is already in use.")]);

        return Result<CityDto>.Conflict(
            $"A city named '{name}' already exists in this state.",
            [new ValidationError("name", "This name is already in use.")]);
    }

    private static CityDto ToDto(City city, string stateName) =>
        new(city.Id, city.StateId, stateName, city.Code, city.Name, city.IsActive, city.CreatedDate, city.ModifiedDate);
}
