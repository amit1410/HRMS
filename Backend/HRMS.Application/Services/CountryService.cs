using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Countries;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

/// <summary>
/// Country management. Countries are global reference data (not tenant-scoped), so every method
/// reads and writes through the unfiltered <c>_db.Countries</c> DbSet.
/// </summary>
public class CountryService : ICountryService
{
    private const string NotFoundMessage = "Country not found.";

    private readonly IHrmsDbContext _db;
    private readonly ILogger<CountryService> _logger;

    public CountryService(IHrmsDbContext db, ILogger<CountryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<PagedResult<CountryDto>>> GetAsync(
        CountryQuery query, CancellationToken cancellationToken = default)
    {
        var countries = _db.Countries.AsNoTracking();

        if (query.IsActive.HasValue)
            countries = countries.Where(c => c.IsActive == query.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            countries = countries.Where(c =>
                c.Code.ToLower().Contains(search) || c.Name.ToLower().Contains(search));
        }

        var page = await Project(ApplySort(countries, query))
            .ToPagedResultAsync(query, cancellationToken);

        return Result<PagedResult<CountryDto>>.Success(page);
    }

    public async Task<Result<CountryDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var country = await Project(_db.Countries.AsNoTracking().Where(c => c.Id == id))
            .FirstOrDefaultAsync(cancellationToken);

        return country is null
            ? Result<CountryDto>.NotFound(NotFoundMessage)
            : Result<CountryDto>.Success(country);
    }

    public async Task<Result<CountryDto>> CreateAsync(
        CountryRequest request, CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim();
        var name = request.Name.Trim();

        var conflict = await FindConflictAsync(code, name, excludeId: null, cancellationToken);
        if (conflict is not null) return conflict;

        var country = new Country
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = name,
            IsActive = request.IsActive
        };

        _db.Countries.Add(country);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await FindConflictAsync(code, name, excludeId: null, cancellationToken);
            if (raced is null) throw;
            _logger.LogWarning("Country create lost a uniqueness race on the database index.");
            return raced;
        }

        _logger.LogInformation("Created country {CountryId}.", country.Id);
        return Result<CountryDto>.Success(ToDto(country, stateCount: 0), "Country created.");
    }

    public async Task<Result<CountryDto>> UpdateAsync(
        Guid id, CountryRequest request, CancellationToken cancellationToken = default)
    {
        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (country is null)
            return Result<CountryDto>.NotFound(NotFoundMessage);

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        var conflict = await FindConflictAsync(code, name, excludeId: id, cancellationToken);
        if (conflict is not null) return conflict;

        country.Code = code;
        country.Name = name;
        country.IsActive = request.IsActive;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await FindConflictAsync(code, name, excludeId: id, cancellationToken);
            if (raced is null) throw;
            _logger.LogWarning("Country update for {CountryId} lost a uniqueness race.", id);
            return raced;
        }

        _logger.LogInformation("Updated country {CountryId}.", id);
        var stateCount = await _db.States.CountAsync(s => s.CountryId == id, cancellationToken);
        return Result<CountryDto>.Success(ToDto(country, stateCount), "Country updated.");
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var country = await _db.Countries.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (country is null)
            return Result<bool>.NotFound(NotFoundMessage);

        var stateCount = await _db.States.CountAsync(s => s.CountryId == id, cancellationToken);
        if (stateCount > 0)
        {
            return Result<bool>.Conflict(
                $"This country still has {stateCount} state(s). Remove them first, or mark the country inactive.");
        }

        _db.Countries.Remove(country);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("Country {CountryId} could not be deleted: still referenced.", id);
            return Result<bool>.Conflict("This country is still referenced and cannot be deleted.");
        }

        _logger.LogInformation("Deleted country {CountryId}.", id);
        return Result<bool>.Success(true, "Country deleted.");
    }

    private static IQueryable<CountryDto> Project(IQueryable<Country> countries) =>
        countries.Select(c => new CountryDto(
            c.Id,
            c.Code,
            c.Name,
            c.IsActive,
            c.States.Count,
            c.CreatedDate,
            c.ModifiedDate));

    private static IQueryable<Country> ApplySort(IQueryable<Country> countries, CountryQuery query)
    {
        var descending = query.SortDescending;
        var ordered = query.SortBy?.Trim().ToLowerInvariant() switch
        {
            "name" => descending ? countries.OrderByDescending(c => c.Name) : countries.OrderBy(c => c.Name),
            "isactive" => descending ? countries.OrderByDescending(c => c.IsActive) : countries.OrderBy(c => c.IsActive),
            "createddate" => descending ? countries.OrderByDescending(c => c.CreatedDate) : countries.OrderBy(c => c.CreatedDate),
            _ => descending ? countries.OrderByDescending(c => c.Code) : countries.OrderBy(c => c.Code)
        };
        return ordered.ThenBy(c => c.Id);
    }

    private async Task<Result<CountryDto>?> FindConflictAsync(
        string code, string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var normalizedCode = code.ToLowerInvariant();
        var normalizedName = name.ToLowerInvariant();

        var candidates = _db.Countries.AsNoTracking();
        if (excludeId is Guid exclude)
            candidates = candidates.Where(c => c.Id != exclude);

        var clashes = await candidates
            .Where(c => c.Code.ToLower() == normalizedCode || c.Name.ToLower() == normalizedName)
            .Select(c => new { c.Code, c.Name })
            .ToListAsync(cancellationToken);

        if (clashes.Count == 0) return null;

        if (clashes.Any(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
            return Result<CountryDto>.Conflict(
                $"A country with code '{code}' already exists.",
                [new ValidationError("code", "This code is already in use.")]);

        return Result<CountryDto>.Conflict(
            $"A country named '{name}' already exists.",
            [new ValidationError("name", "This name is already in use.")]);
    }

    private static CountryDto ToDto(Country country, int stateCount) =>
        new(country.Id, country.Code, country.Name, country.IsActive, stateCount, country.CreatedDate, country.ModifiedDate);
}
