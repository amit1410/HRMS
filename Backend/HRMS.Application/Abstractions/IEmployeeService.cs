using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

/// <summary>
/// Employee management within the caller's own tenant.
/// <para>
/// The department, designation and reporting-manager ids on a write are the multi-tenant risk in this
/// module: a foreign key constraint alone is satisfied by <em>any</em> existing row, including another
/// tenant's. Implementations must resolve every one of them through the tenant-filtered data set, so a
/// foreign id is rejected exactly like one that does not exist.
/// </para>
/// </summary>
public interface IEmployeeService
{
    Task<Result<PagedResult<EmployeeListItemDto>>> GetAsync(EmployeeQuery query, CancellationToken cancellationToken = default);

    Task<Result<EmployeeDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<EmployeeDto>> CreateAsync(EmployeeRequest request, CancellationToken cancellationToken = default);

    Task<Result<EmployeeDto>> UpdateAsync(Guid id, EmployeeRequest request, CancellationToken cancellationToken = default);

    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Renders the filtered employee list as a CSV file.</summary>
    Task<Result<EmployeeExportDto>> ExportAsync(EmployeeQuery query, CancellationToken cancellationToken = default);
}
