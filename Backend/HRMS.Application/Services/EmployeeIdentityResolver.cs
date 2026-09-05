using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Resolves self-service identity only through the current account/Employee link. It intentionally
/// does not use UserId as an EmployeeId and never searches by email or employee code.
/// </summary>
public sealed class EmployeeIdentityResolver : IEmployeeIdentityResolver
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;

    public EmployeeIdentityResolver(IHrmsDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId || _tenantContext.UserId is not Guid userId)
            return Result<RuntimeEmployeeIdentity>.Unauthorized("An authenticated tenant and account are required.");

        var link = await _db.AccountEmployeeCurrentLinks.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, cancellationToken);
        if (link is null)
            return Result<RuntimeEmployeeIdentity>.NotFound("The authenticated account is not linked to an Employee.");

        var employeeExists = await _db.Employees.AsNoTracking()
            .AnyAsync(x => x.TenantId == tenantId && x.Id == link.EmployeeId, cancellationToken);
        if (!employeeExists)
            return Result<RuntimeEmployeeIdentity>.Conflict("The account's Employee link requires administrator review.");

        return Result<RuntimeEmployeeIdentity>.Success(new(tenantId, userId, link.EmployeeId));
    }
}
