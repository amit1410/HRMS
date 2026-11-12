using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeeBulkUploadValidationService
{
    const int MaximumFileBytes = 10 * 1024 * 1024;
    Task<Result<EmployeeBulkUploadPreviewDto>> ValidateAsync(Stream workbook, CancellationToken cancellationToken = default);
    Task<Result<EmployeeBulkUploadValidationResult>> ValidateForExecutionAsync(Stream workbook, CancellationToken cancellationToken = default);
}
