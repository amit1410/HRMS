using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.States;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

/// <summary>
/// State management. States are global reference data (not tenant-scoped).
/// </summary>
public class StateService : IStateService
{
    private const string NotFoundMessage = "State not found.";

    private readonly IHrmsDbContext _db;
    private readonly ILogger<StateService> _logger;

    public StateService(IHrmsDbContext db, ILogger<StateService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Result<PagedResult<StateDto>>> GetAsync(
        StateQuery query, CancellationToken cancellationToken = default)
    {
        var states = _db.States.AsNoTracking();

        if (query.CountryId.HasValue)
            states = states.Where(s => s.CountryId == query.CountryId.Value);

        if (query.IsActive.HasValue)
            states = states.Where(s => s.IsActive == query.IsActive.Value);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            states = states.Where(s =>
                s.Code.ToLower().Contains(search) || s.Name.ToLower().Contains(search));
        }

        var page = await Project(ApplySort(states, query))
            .ToPagedResultAsync(query, cancellationToken);

        return Result<PagedResult<StateDto>>.Success(page);
    }

    public async Task<Result<StateDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var state = await Project(_db.States.AsNoTracking().Where(s => s.Id == id))
            .FirstOrDefaultAsync(cancellationToken);

        return state is null
            ? Result<StateDto>.NotFound(NotFoundMessage)
            : Result<StateDto>.Success(state);
    }

    public async Task<Result<StateDto>> CreateAsync(
        StateRequest request, CancellationToken cancellationToken = default)
    {
        var country = await _db.Countries.AsNoTracking()
            .Where(c => c.Id == request.CountryId)
            .Select(c => new { c.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (country is null)
            return Result<StateDto>.Invalid("countryId", "The selected country does not exist.");

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        var conflict = await FindConflictAsync(request.CountryId, code, name, excludeId: null, cancellationToken);
        if (conflict is not null) return conflict;

        var state = new State
        {
            Id = Guid.NewGuid(),
            CountryId = request.CountryId,
            Code = code,
            Name = name,
            IsActive = request.IsActive
        };

        _db.States.Add(state);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await FindConflictAsync(request.CountryId, code, name, excludeId: null, cancellationToken);
            if (raced is null) throw;
            _logger.LogWarning("State create lost a uniqueness race on the database index.");
            return raced;
        }

        _logger.LogInformation("Created state {StateId}.", state.Id);
        return Result<StateDto>.Success(ToDto(state, country.Name, cityCount: 0), "State created.");
    }

    public async Task<Result<StateDto>> UpdateAsync(
        Guid id, StateRequest request, CancellationToken cancellationToken = default)
    {
        var state = await _db.States.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (state is null)
            return Result<StateDto>.NotFound(NotFoundMessage);

        var country = await _db.Countries.AsNoTracking()
            .Where(c => c.Id == request.CountryId)
            .Select(c => new { c.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (country is null)
            return Result<StateDto>.Invalid("countryId", "The selected country does not exist.");

        var code = request.Code.Trim();
        var name = request.Name.Trim();

        var conflict = await FindConflictAsync(request.CountryId, code, name, excludeId: id, cancellationToken);
        if (conflict is not null) return conflict;

        state.CountryId = request.CountryId;
        state.Code = code;
        state.Name = name;
        state.IsActive = request.IsActive;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var raced = await FindConflictAsync(request.CountryId, code, name, excludeId: id, cancellationToken);
            if (raced is null) throw;
            _logger.LogWarning("State update for {StateId} lost a uniqueness race.", id);
            return raced;
        }

        _logger.LogInformation("Updated state {StateId}.", id);
        var cityCount = await _db.Cities.CountAsync(c => c.StateId == id, cancellationToken);
        return Result<StateDto>.Success(ToDto(state, country.Name, cityCount), "State updated.");
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var state = await _db.States.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (state is null)
            return Result<bool>.NotFound(NotFoundMessage);

        var cityCount = await _db.Cities.CountAsync(c => c.StateId == id, cancellationToken);
        if (cityCount > 0)
        {
            return Result<bool>.Conflict(
                $"This state still has {cityCount} city(ies). Remove them first, or mark the state inactive.");
        }

        _db.States.Remove(state);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("State {StateId} could not be deleted: still referenced.", id);
            return Result<bool>.Conflict("This state is still referenced and cannot be deleted.");
        }

        _logger.LogInformation("Deleted state {StateId}.", id);
        return Result<bool>.Success(true, "State deleted.");
    }

    private static IQueryable<StateDto> Project(IQueryable<State> states) =>
        states.Select(s => new StateDto(
            s.Id,
            s.CountryId,
            s.Country!.Name,
            s.Code,
            s.Name,
            s.IsActive,
            s.Cities.Count,
            s.CreatedDate,
            s.ModifiedDate));

    private static IQueryable<State> ApplySort(IQueryable<State> states, StateQuery query)
    {
        var descending = query.SortDescending;
        var ordered = query.SortBy?.Trim().ToLowerInvariant() switch
        {
            "name" => descending ? states.OrderByDescending(s => s.Name) : states.OrderBy(s => s.Name),
            "countryid" => descending ? states.OrderByDescending(s => s.CountryId) : states.OrderBy(s => s.CountryId),
            "isactive" => descending ? states.OrderByDescending(s => s.IsActive) : states.OrderBy(s => s.IsActive),
            "createddate" => descending ? states.OrderByDescending(s => s.CreatedDate) : states.OrderBy(s => s.CreatedDate),
            _ => descending ? states.OrderByDescending(s => s.Code) : states.OrderBy(s => s.Code)
        };
        return ordered.ThenBy(s => s.Id);
    }

    private async Task<Result<StateDto>?> FindConflictAsync(
        Guid countryId, string code, string name, Guid? excludeId, CancellationToken cancellationToken)
    {
        var normalizedCode = code.ToLowerInvariant();
        var normalizedName = name.ToLowerInvariant();

        var candidates = _db.States.AsNoTracking();
        if (excludeId is Guid exclude)
            candidates = candidates.Where(s => s.Id != exclude);

        var clashes = await candidates
            .Where(s => s.CountryId == countryId &&
                (s.Code.ToLower() == normalizedCode || s.Name.ToLower() == normalizedName))
            .Select(s => new { s.Code, s.Name })
            .ToListAsync(cancellationToken);

        if (clashes.Count == 0) return null;

        if (clashes.Any(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase)))
            return Result<StateDto>.Conflict(
                $"A state with code '{code}' already exists in this country.",
                [new ValidationError("code", "This code is already in use.")]);

        return Result<StateDto>.Conflict(
            $"A state named '{name}' already exists in this country.",
            [new ValidationError("name", "This name is already in use.")]);
    }

    private static StateDto ToDto(State state, string countryName, int cityCount) =>
        new(state.Id, state.CountryId, countryName, state.Code, state.Name, state.IsActive, cityCount, state.CreatedDate, state.ModifiedDate);
}
