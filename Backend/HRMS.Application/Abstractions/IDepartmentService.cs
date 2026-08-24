using HRMS.Application.Common;
using HRMS.Application.DTOs.Departments;

namespace HRMS.Application.Abstractions;

/// <summary>
/// Department management within the caller's own tenant. Every method operates through the authenticated
/// tenant context: no method accepts a tenant id, and an id belonging to another tenant is reported as
/// "not found" rather than "forbidden", so the API never confirms that a record exists elsewhere.
/// </summary>
public interface IDepartmentService
{
    Task<Result<PagedResult<DepartmentDto>>> GetAsync(DepartmentQuery query, CancellationToken cancellationToken = default);

    Task<Result<DepartmentDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<DepartmentDto>> CreateAsync(DepartmentRequest request, CancellationToken cancellationToken = default);

    Task<Result<DepartmentDto>> UpdateAsync(Guid id, DepartmentRequest request, CancellationToken cancellationToken = default);

    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
