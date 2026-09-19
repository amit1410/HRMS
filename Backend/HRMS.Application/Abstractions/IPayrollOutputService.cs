using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IPayrollOutputService
{
    Task<Result<IReadOnlyList<PayslipDto>>> GenerateAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayslipDto>>> PublishAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<PagedResult<PayslipDto>>> GetRunPayslipsAsync(Guid payrollRunId, PayrollOutputQuery query, CancellationToken ct = default);
    Task<Result<PayslipDto>> GetAsync(Guid payslipId, bool publishedOnly = false, CancellationToken ct = default);
    Task<Result<string>> GetDocumentAsync(Guid payslipId, bool publishedOnly = false, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollRegisterRowDto>>> GetRegisterAsync(Guid payrollRunId, PayrollOutputQuery query, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportRegisterAsync(Guid payrollRunId, PayrollOutputQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<PayslipDto>>> GetOwnAsync(PayrollOutputQuery query, CancellationToken ct = default);
}
