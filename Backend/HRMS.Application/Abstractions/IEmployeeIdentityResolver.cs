using HRMS.Application.Common;

namespace HRMS.Application.Abstractions;

/// <summary>Resolves the Employee subject linked to the authenticated account.</summary>
public interface IEmployeeIdentityResolver
{
    Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default);
}

public sealed record RuntimeEmployeeIdentity(Guid TenantId, Guid UserId, Guid EmployeeId);
