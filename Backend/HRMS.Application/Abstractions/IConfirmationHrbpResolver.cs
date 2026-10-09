namespace HRMS.Application.Abstractions;

public enum ConfirmationHrbpResolutionStatus
{
    /// <summary>Exactly one HRBP was derived and mapped to an employee.</summary>
    Resolved,
    /// <summary>No HRBP role assignment covers the employee's organisation.</summary>
    NoHrbp,
    /// <summary>More than one equally-specific HRBP covers the employee — left unassigned, resolved by permission+scope instead.</summary>
    Ambiguous,
    InvalidTenant,
    EffectiveEmploymentNotFound
}

public sealed record ConfirmationHrbpResolutionResult(
    ConfirmationHrbpResolutionStatus Status,
    Guid? HrbpEmployeeId,
    string Message);

/// <summary>
/// Derives the HR/HRBP reviewer for an employee from role assignments: among users holding the HRBP role
/// whose <c>UserRoleAssignmentScope</c> dimensions cover the employee's effective employment (an unscoped
/// HRBP assignment is tenant-wide, specificity 0), the single most specific is selected and mapped to an
/// employee via <c>AccountEmployeeCurrentLinks</c>. Zero or ambiguous matches resolve to no reviewer; the
/// case is still created and HR/HRBP submission is then authorized by permission + organisational scope.
/// </summary>
public interface IConfirmationHrbpResolver
{
    Task<ConfirmationHrbpResolutionResult> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly effectiveDate,
        CancellationToken ct = default);
}
