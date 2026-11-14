using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IEmployeeBulkImportExecutionService
{
    Task<Result<EmployeeBulkImportPreparationDto>> PrepareAsync(Stream workbook, string? fileName, CancellationToken cancellationToken = default);
    Task<Result<EmployeeBulkImportAcceptedDto>> ConfirmAsync(Guid operationId, string confirmationToken, CancellationToken cancellationToken = default);
    Task<Result<EmployeeBulkImportStatusDto>> GetStatusAsync(Guid operationId, int resultPage = 1, int resultPageSize = 10_000, CancellationToken cancellationToken = default);
    Task<Result<EmployeeBulkImportHistoryDto>> GetHistoryAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<EmployeeBulkImportFailedRowsExport>> ExportFailedRowsAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task<bool> ProcessQueuedOperationAsync(Guid operationId, CancellationToken cancellationToken = default);
}
