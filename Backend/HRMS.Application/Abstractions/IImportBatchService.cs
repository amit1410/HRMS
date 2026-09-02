using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Abstractions;

public interface IImportBatchService
{
    Task<Result<IReadOnlyList<ImportBatchDto>>> GetAsync(CancellationToken cancellationToken = default);
    Task<Result<ImportBatchDto>> GetByIdAsync(Guid batchId, CancellationToken cancellationToken = default);
    Task<Result<ImportBatchDto>> ImportAsync(string fileName, string importedBy, CancellationToken cancellationToken = default);
    Task<Result<bool>> DeleteAsync(Guid batchId, CancellationToken cancellationToken = default);
}
