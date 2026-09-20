using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IPayrollStatutoryComplianceService
{
    Task<Result<PayrollCompliancePeriodDto>> CreatePeriodAsync(PayrollCompliancePeriodRequest request, CancellationToken ct = default);
    Task<Result<PayrollStatutoryReturnDto>> GenerateAsync(Guid periodId, CancellationToken ct = default);
    Task<Result<PayrollStatutoryReturnDto>> ValidateAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<PayrollStatutoryReturnDto>> ApproveAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<PayrollStatutoryReturnDto>> MarkFiledAsync(Guid batchId, string? externalReference, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportAsync(Guid batchId, CancellationToken ct = default);
    Task<Result<PayrollStatutoryReturnDto>> GetAsync(Guid batchId, CancellationToken ct = default);
}
