using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IBankAdviceService
{
    Task<Result<BankAdviceBatchDto>> GenerateAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<BankAdviceBatchDto>> ValidateAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<BankAdviceBatchDto>> PrepareAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<BankAdviceBatchDto>> ApproveAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<BankAdviceBatchDto>> CancelAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<PagedResult<BankAdviceBatchDto>>> GetAsync(BankAdviceQuery query, CancellationToken ct = default);
    Task<Result<BankAdviceBatchDto>> GetByIdAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportAsync(Guid batchId, CancellationToken ct = default);
}
