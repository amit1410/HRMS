using HRMS.Application.Common;
using HRMS.Application.DTOs.Banks;

namespace HRMS.Application.Abstractions;

/// <summary>
/// Bank master management within the caller's own tenant. Every method operates through the authenticated
/// tenant context: no method accepts a tenant id, and an id belonging to another tenant is reported as
/// "not found" rather than "forbidden", so the API never confirms that a record exists elsewhere.
/// </summary>
public interface IBankService
{
    Task<Result<PagedResult<BankDto>>> GetAsync(BankQuery query, CancellationToken cancellationToken = default);

    Task<Result<BankDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<BankDto>> CreateAsync(BankRequest request, CancellationToken cancellationToken = default);

    Task<Result<BankDto>> UpdateAsync(Guid id, BankRequest request, CancellationToken cancellationToken = default);

    /// <summary>Activates or deactivates a bank. Deactivation preserves existing employee bank references.</summary>
    Task<Result<BankDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a bank. Refused with a conflict when the bank is referenced by any employee bank record —
    /// deactivate it instead so the referencing history stays intact.
    /// </summary>
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns every bank matching the filters, unpaged, for export.</summary>
    Task<Result<IReadOnlyList<BankDto>>> GetAllForExportAsync(BankQuery query, CancellationToken cancellationToken = default);
}
