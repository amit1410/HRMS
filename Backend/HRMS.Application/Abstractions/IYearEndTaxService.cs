using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IYearEndTaxService
{
    Task<Result<PagedResult<YearEndTaxRunDto>>> GetRunsAsync(PagedQuery query, CancellationToken ct = default);
    Task<Result<YearEndTaxRunDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<YearEndTaxRunDto>> CreateAsync(YearEndTaxRunRequest request, CancellationToken ct = default);
    Task<Result<YearEndTaxRunDto>> CalculateAsync(Guid id, CancellationToken ct = default);
    Task<Result<PagedResult<YearEndTaxEmployeeDto>>> GetEmployeesAsync(Guid id, YearEndTaxEmployeeQuery query, CancellationToken ct = default);
    Task<Result<YearEndTaxEmployeeDto>> GetEmployeeAsync(Guid id, Guid employeeId, CancellationToken ct = default);
    Task<Result<YearEndTaxPreviousEmployerDto>> AddPreviousEmployerAsync(Guid runId, YearEndTaxPreviousEmployerRequest request, CancellationToken ct = default);
    Task<Result<YearEndTaxPreviousEmployerDto>> UpdatePreviousEmployerAsync(Guid runId, Guid inputId, YearEndTaxPreviousEmployerUpdateRequest request, CancellationToken ct = default);
    Task<Result<YearEndTaxRunDto>> SubmitAsync(Guid id, CancellationToken ct = default);
    Task<Result<YearEndTaxRunDto>> ApproveAsync(Guid id, CancellationToken ct = default);
    Task<Result<YearEndTaxRunDto>> CloseAsync(Guid id, CancellationToken ct = default);
    Task<Result<YearEndTaxRunDto>> CancelAsync(Guid id, CancellationToken ct = default);
    Task<Result<YearEndTaxAdjustmentDto>> HandoffAdjustmentAsync(Guid runId, YearEndTaxAdjustmentHandoffRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<YearEndTaxHistoryDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportAsync(Guid id, string kind, CancellationToken ct = default);
}
