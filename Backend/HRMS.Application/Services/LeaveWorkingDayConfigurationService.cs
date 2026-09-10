using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Common;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LeaveWorkingDayConfigurationService : ILeaveWorkingDayConfigurationService
{
    private const string NoTenant = "No authenticated tenant.";
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;

    public LeaveWorkingDayConfigurationService(IHrmsDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Result<IReadOnlyList<HolidayDto>>> GetHolidaysAsync(HolidayQuery query, CancellationToken cancellationToken = default)
    {
        if (!Tenant(out var tenantId)) return Result<IReadOnlyList<HolidayDto>>.Unauthorized(NoTenant);
        var source = _db.Holidays.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (query.From is DateOnly from) source = source.Where(x => x.Date >= from);
        if (query.To is DateOnly to) source = source.Where(x => x.Date <= to);
        if (query.CountryId is Guid country) source = source.Where(x => x.CountryLocationId == country);
        if (query.WorkLocationId is Guid workLocation) source = source.Where(x => x.WorkLocationId == workLocation);
        if (query.IsActive is bool active) source = source.Where(x => x.IsActive == active);
        var rows = await source.OrderBy(x => x.Date).ThenBy(x => x.Name).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<HolidayDto>>.Success(rows.Select(ToDto).ToList());
    }

    public async Task<Result<HolidayDto>> CreateHolidayAsync(HolidayRequest request, CancellationToken cancellationToken = default)
    {
        if (!Tenant(out var tenantId)) return Result<HolidayDto>.Unauthorized(NoTenant);
        var validation = await ValidateHolidayAsync(tenantId, request, null, cancellationToken);
        if (validation is not null) return validation;
        var item = new Holiday { Id = Guid.NewGuid(), TenantId = tenantId, Name = request.Name.Trim(), Date = request.Date, CountryLocationId = request.CountryLocationId, WorkLocationId = request.WorkLocationId, IsActive = request.IsActive };
        _db.Holidays.Add(item);
        try { await _db.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { return Result<HolidayDto>.Conflict("The Holiday conflicts with an existing configuration."); }
        return Result<HolidayDto>.Success(ToDto(item), "Holiday created.");
    }

    public async Task<Result<HolidayDto>> UpdateHolidayAsync(Guid id, HolidayRequest request, CancellationToken cancellationToken = default)
    {
        if (!Tenant(out var tenantId)) return Result<HolidayDto>.Unauthorized(NoTenant);
        var item = await _db.Holidays.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (item is null) return Result<HolidayDto>.NotFound("The requested Holiday was not found.");
        if (!TokenMatches(item, request.ConcurrencyToken)) return Result<HolidayDto>.Conflict("Configuration changed by another user. Reload before saving.");
        var validation = await ValidateHolidayAsync(tenantId, request, id, cancellationToken);
        if (validation is not null) return validation;
        item.Name = request.Name.Trim(); item.Date = request.Date; item.CountryLocationId = request.CountryLocationId; item.WorkLocationId = request.WorkLocationId; item.IsActive = request.IsActive; item.ModifiedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result<HolidayDto>.Success(ToDto(item), "Holiday updated.");
    }

    public async Task<Result<HolidayDto>> DeactivateHolidayAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!Tenant(out var tenantId)) return Result<HolidayDto>.Unauthorized(NoTenant);
        var item = await _db.Holidays.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);
        if (item is null) return Result<HolidayDto>.NotFound("The requested Holiday was not found.");
        item.IsActive = false; item.ModifiedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Result<HolidayDto>.Success(ToDto(item), "Holiday deactivated.");
    }

    public async Task<Result<IReadOnlyList<WeeklyOffConfigurationDto>>> GetWeeklyOffConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        if (!Tenant(out var tenantId)) return Result<IReadOnlyList<WeeklyOffConfigurationDto>>.Unauthorized(NoTenant);
        var rows = await _db.WeeklyOffConfigurations.AsNoTracking().Where(x => x.TenantId == tenantId).Include(x => x.Days).OrderByDescending(x => x.EffectiveFrom).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<WeeklyOffConfigurationDto>>.Success(rows.Select(ToDto).ToList());
    }

    public async Task<Result<WeeklyOffConfigurationDto>> CreateWeeklyOffConfigurationAsync(WeeklyOffConfigurationRequest request, CancellationToken cancellationToken = default) => await SaveWeeklyOffAsync(null, request, cancellationToken);

    public async Task<Result<WeeklyOffConfigurationDto>> UpdateWeeklyOffConfigurationAsync(Guid id, WeeklyOffConfigurationRequest request, CancellationToken cancellationToken = default) => await SaveWeeklyOffAsync(id, request, cancellationToken);

    private async Task<Result<WeeklyOffConfigurationDto>> SaveWeeklyOffAsync(Guid? id, WeeklyOffConfigurationRequest request, CancellationToken cancellationToken)
    {
        if (!Tenant(out var tenantId)) return Result<WeeklyOffConfigurationDto>.Unauthorized(NoTenant);
        if (request.EffectiveTo is DateOnly to && to < request.EffectiveFrom) return Result<WeeklyOffConfigurationDto>.Invalid("effectiveTo", "EffectiveTo must not be before EffectiveFrom.");
        var days = request.Days.Distinct().ToArray();
        if (days.Length == 0 || days.Any(day => !Enum.IsDefined(day))) return Result<WeeklyOffConfigurationDto>.Invalid("days", "At least one valid weekly-off day is required.");
        var countryValid = request.CountryLocationId is null || await _db.Countries.AnyAsync(x => x.Id == request.CountryLocationId && x.IsActive, cancellationToken);
        var locationValid = request.WorkLocationId is null || await _db.WorkLocations.AnyAsync(x => x.Id == request.WorkLocationId && x.IsActive, cancellationToken);
        if (!countryValid) return Result<WeeklyOffConfigurationDto>.Invalid("countryLocationId", "The selected Country was not found.");
        if (!locationValid) return Result<WeeklyOffConfigurationDto>.Invalid("workLocationId", "The selected WorkLocation was not found in the tenant.");
        var overlap = await _db.WeeklyOffConfigurations.AsNoTracking().Where(x => x.TenantId == tenantId && x.IsActive && x.Id != id && x.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) && (x.EffectiveTo == null || x.EffectiveTo >= request.EffectiveFrom) && x.CountryLocationId == request.CountryLocationId && x.WorkLocationId == request.WorkLocationId).AnyAsync(cancellationToken);
        if (overlap) return Result<WeeklyOffConfigurationDto>.Conflict("An overlapping weekly-off configuration already exists for this applicability scope.");
        WeeklyOffConfiguration item;
        if (id is Guid existingId)
        {
            item = await _db.WeeklyOffConfigurations.Include(x => x.Days).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == existingId, cancellationToken) ?? null!;
            if (item is null) return Result<WeeklyOffConfigurationDto>.NotFound("The requested weekly-off configuration was not found.");
            if (!TokenMatches(item, request.ConcurrencyToken)) return Result<WeeklyOffConfigurationDto>.Conflict("Configuration changed by another user. Reload before saving.");
            _db.WeeklyOffDays.RemoveRange(item.Days);
        }
        else { item = new WeeklyOffConfiguration { Id = Guid.NewGuid(), TenantId = tenantId }; _db.WeeklyOffConfigurations.Add(item); }
        item.EffectiveFrom = request.EffectiveFrom; item.EffectiveTo = request.EffectiveTo; item.CountryLocationId = request.CountryLocationId; item.WorkLocationId = request.WorkLocationId; item.IsActive = request.IsActive; item.ModifiedDate = id is null ? null : DateTime.UtcNow;
        item.Days = days.Select(day => new WeeklyOffDay { Id = Guid.NewGuid(), TenantId = tenantId, WeeklyOffConfigurationId = item.Id, DayOfWeek = day }).ToList();
        try { await _db.SaveChangesAsync(cancellationToken); } catch (DbUpdateException) { return Result<WeeklyOffConfigurationDto>.Conflict("The weekly-off configuration conflicts with existing data."); }
        return Result<WeeklyOffConfigurationDto>.Success(ToDto(item), id is null ? "Weekly-off configuration created." : "Weekly-off configuration updated.");
    }

    private async Task<Result<HolidayDto>?> ValidateHolidayAsync(Guid tenantId, HolidayRequest request, Guid? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200) return Result<HolidayDto>.Invalid("name", "Name is required and must be 200 characters or fewer.");
        if (request.CountryLocationId is Guid country && !await _db.Countries.AnyAsync(x => x.Id == country && x.IsActive, cancellationToken)) return Result<HolidayDto>.Invalid("countryLocationId", "The selected Country was not found.");
        if (request.WorkLocationId is Guid location && !await _db.WorkLocations.AnyAsync(x => x.TenantId == tenantId && x.Id == location && x.IsActive, cancellationToken)) return Result<HolidayDto>.Invalid("workLocationId", "The selected WorkLocation was not found in the tenant.");
        return null;
    }

    private bool Tenant(out Guid tenantId) { tenantId = _tenant.TenantId ?? Guid.Empty; return tenantId != Guid.Empty; }
    private static bool TokenMatches(BaseEntity entity, string? token) => !string.IsNullOrWhiteSpace(token) && string.Equals(token, Token(entity), StringComparison.Ordinal);
    private static string Token(BaseEntity entity) => (entity.ModifiedDate ?? entity.CreatedDate).ToString("O");
    private static HolidayDto ToDto(Holiday x) => new(x.Id, x.Name, x.Date, x.CountryLocationId, x.WorkLocationId, x.IsActive, x.CreatedDate, x.ModifiedDate, Token(x));
    private static WeeklyOffConfigurationDto ToDto(WeeklyOffConfiguration x) => new(x.Id, x.EffectiveFrom, x.EffectiveTo, x.CountryLocationId, x.WorkLocationId, x.Days.OrderBy(d => d.DayOfWeek).Select(d => d.DayOfWeek).ToList(), x.IsActive, x.CreatedDate, x.ModifiedDate, Token(x));
}
