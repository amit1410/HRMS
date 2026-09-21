using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IPayrollLoanService
{
    Task<Result<IReadOnlyList<LoanProductDto>>> GetProductsAsync(CancellationToken ct = default);
    Task<Result<LoanProductDto>> CreateProductAsync(LoanProductRequest request, CancellationToken ct = default);
    Task<Result<LoanProductDto>> UpdateProductAsync(Guid id, LoanProductRequest request, CancellationToken ct = default);
    Task<Result<LoanProductDto>> CreateProductVersionAsync(Guid id, LoanProductVersionRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<EmployeeLoanDto>>> GetLoansAsync(Guid? employeeId = null, CancellationToken ct = default);
    Task<Result<PagedResult<LoanRegisterRowDto>>> GetRegisterAsync(LoanRegisterQuery query, CancellationToken ct = default);
    Task<Result<EmployeeLoanDto>> CreateLoanAsync(LoanRequest request, CancellationToken ct = default);
    Task<Result<EmployeeLoanDto>> SubmitAsync(Guid id, CancellationToken ct = default);
    Task<Result<EmployeeLoanDto>> ApproveAsync(Guid id, decimal? approvedAmount = null, int? approvedTenureMonths = null, CancellationToken ct = default);
    Task<Result<EmployeeLoanDto>> RejectAsync(Guid id, string reason, CancellationToken ct = default);
    Task<Result<EmployeeLoanDto>> RecordDisbursementAsync(Guid id, decimal? amount = null, CancellationToken ct = default);
    Task<Result<EmployeeLoanDto>> CancelAsync(Guid id, string? reason = null, CancellationToken ct = default);
    Task<Result<EmployeeLoanDto>> RecordRepaymentAsync(Guid id, LoanRepaymentRequest request, CancellationToken ct = default);
    Task<Result<EmployeeLoanDto>> PartialPrepayAsync(Guid id, LoanRepaymentRequest request, CancellationToken ct = default);
    Task<Result<EmployeeLoanDto>> CloseAsync(Guid id, LoanRepaymentRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<LoanInstallmentDto>>> GetScheduleAsync(Guid id, CancellationToken ct = default);
}
