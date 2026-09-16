namespace HRMS.Application.Abstractions;

/// <summary>Resolves the roles effective for an account on a tenant business date.</summary>
public interface IRoleResolutionService
{
    Task<IReadOnlyList<int>> GetEffectiveRoleIdsAsync(
        Guid tenantId,
        Guid userId,
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
}
