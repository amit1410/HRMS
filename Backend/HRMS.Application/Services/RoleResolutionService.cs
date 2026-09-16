using HRMS.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class RoleResolutionService(IHrmsDbContext db) : IRoleResolutionService
{
    public async Task<IReadOnlyList<int>> GetEffectiveRoleIdsAsync(
        Guid tenantId,
        Guid userId,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
        => await db.UserRoles
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.UserId == userId &&
                        x.EffectiveFrom <= businessDate &&
                        (x.EffectiveTo == null || x.EffectiveTo >= businessDate))
            .Select(x => x.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);
}
