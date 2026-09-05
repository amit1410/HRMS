using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class LeavePeriodResolver : ILeavePeriodResolver
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext? _tenantContext;

    public LeavePeriodResolver(IHrmsDbContext db, ITenantContext? tenantContext = null)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<LeavePeriodResolutionResult> ResolveAsync(Guid tenantId, DateOnly effectiveDate, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
            return new(LeavePeriodResolutionStatus.InvalidTenant, tenantId, effectiveDate, null, "A tenant is required to resolve a LeavePeriod.");
        if (_tenantContext?.TenantId is Guid currentTenantId && currentTenantId != tenantId)
            return new(LeavePeriodResolutionStatus.InvalidTenant, tenantId, effectiveDate, null, "The requested tenant is not the authenticated tenant.");

        var periods = await _db.LeavePeriods.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive && x.StartDate <= effectiveDate && effectiveDate <= x.EndDate)
            .OrderBy(x => x.Id)
            .ToListAsync(ct);

        return periods.Count switch
        {
            0 => new(LeavePeriodResolutionStatus.NotConfigured, tenantId, effectiveDate, null, "No active LeavePeriod contains the effective date."),
            1 => new(LeavePeriodResolutionStatus.Resolved, tenantId, effectiveDate, ToDto(periods[0]), "A unique active LeavePeriod was resolved."),
            _ => new(LeavePeriodResolutionStatus.ConfigurationAmbiguity, tenantId, effectiveDate, null, "Multiple active LeavePeriods contain the effective date.")
        };
    }

    private static LeavePeriodDto ToDto(LeavePeriod period) =>
        new(period.Id, period.Code, period.Name, period.StartDate, period.EndDate, period.IsActive, period.CreatedDate, period.ModifiedDate, (period.ModifiedDate ?? period.CreatedDate).ToString("O"));
}
