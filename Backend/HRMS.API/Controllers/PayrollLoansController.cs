using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.DTOs.AccountEmployeeLinks;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll")]
public sealed class PayrollLoansController(IPayrollLoanService service, IAccountEmployeeLinkService links, ITenantContext tenant) : ControllerBase
{
    [HttpGet("loan-products"), HasPermission(Permissions.Payroll.LoansView)] public async Task<ActionResult<ApiResponse<IReadOnlyList<LoanProductDto>>>> Products(CancellationToken ct) => (await service.GetProductsAsync(ct)).ToActionResult();
    [HttpPost("loan-products"), HasPermission(Permissions.Payroll.LoansManageProducts)] public async Task<ActionResult<ApiResponse<LoanProductDto>>> CreateProduct(LoanProductRequest request, CancellationToken ct) => (await service.CreateProductAsync(request, ct)).ToActionResult();
    [HttpPut("loan-products/{id:guid}"), HasPermission(Permissions.Payroll.LoansManageProducts)] public async Task<ActionResult<ApiResponse<LoanProductDto>>> UpdateProduct(Guid id, LoanProductRequest request, CancellationToken ct) => (await service.UpdateProductAsync(id, request, ct)).ToActionResult();
    [HttpPost("loan-products/{id:guid}/versions"), HasPermission(Permissions.Payroll.LoansManageProducts)] public async Task<ActionResult<ApiResponse<LoanProductDto>>> CreateProductVersion(Guid id, LoanProductVersionRequest request, CancellationToken ct) => (await service.CreateProductVersionAsync(id, request, ct)).ToActionResult();
    [HttpGet("loans"), HasPermission(Permissions.Payroll.LoansView)] public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeLoanDto>>>> Loans([FromQuery] Guid? employeeId, CancellationToken ct) => (await service.GetLoansAsync(employeeId, ct)).ToActionResult();
    [HttpGet("loans/register"), HasPermission(Permissions.Payroll.LoansView)] public async Task<ActionResult<ApiResponse<PagedResult<LoanRegisterRowDto>>>> Register([FromQuery] LoanRegisterQuery query, CancellationToken ct) => (await service.GetRegisterAsync(query, ct)).ToActionResult();
    [HttpPost("loans"), HasPermission(Permissions.Payroll.LoansRequest)] public async Task<ActionResult<ApiResponse<EmployeeLoanDto>>> CreateLoan(LoanRequest request, CancellationToken ct) => (await service.CreateLoanAsync(request, ct)).ToActionResult();
    [HttpPost("loans/{id:guid}/submit"), HasPermission(Permissions.Payroll.LoansRequest)] public async Task<ActionResult<ApiResponse<EmployeeLoanDto>>> Submit(Guid id, CancellationToken ct) => (await service.SubmitAsync(id, ct)).ToActionResult();
    [HttpPost("loans/{id:guid}/approve"), HasPermission(Permissions.Payroll.LoansApprove)] public async Task<ActionResult<ApiResponse<EmployeeLoanDto>>> Approve(Guid id, [FromQuery] decimal? amount, [FromQuery] int? tenureMonths, CancellationToken ct) => (await service.ApproveAsync(id, amount, tenureMonths, ct)).ToActionResult();
    [HttpPost("loans/{id:guid}/reject"), HasPermission(Permissions.Payroll.LoansApprove)] public async Task<ActionResult<ApiResponse<EmployeeLoanDto>>> Reject(Guid id, [FromQuery] string reason, CancellationToken ct) => (await service.RejectAsync(id, reason, ct)).ToActionResult();
    [HttpPost("loans/{id:guid}/record-disbursement"), HasPermission(Permissions.Payroll.LoansDisburse)] public async Task<ActionResult<ApiResponse<EmployeeLoanDto>>> Disburse(Guid id, [FromQuery] decimal? amount, CancellationToken ct) => (await service.RecordDisbursementAsync(id, amount, ct)).ToActionResult();
    [HttpPost("loans/{id:guid}/cancel"), HasPermission(Permissions.Payroll.LoansCancel)] public async Task<ActionResult<ApiResponse<EmployeeLoanDto>>> Cancel(Guid id, [FromQuery] string? reason, CancellationToken ct) => (await service.CancelAsync(id, reason, ct)).ToActionResult();
    [HttpGet("loans/{id:guid}/schedule"), HasPermission(Permissions.Payroll.LoansView)] public async Task<ActionResult<ApiResponse<IReadOnlyList<LoanInstallmentDto>>>> Schedule(Guid id, CancellationToken ct) => (await service.GetScheduleAsync(id, ct)).ToActionResult();
    [HttpPost("loans/{id:guid}/repayments"), HasPermission(Permissions.Payroll.LoansRecover)] public async Task<ActionResult<ApiResponse<EmployeeLoanDto>>> Repayment(Guid id, LoanRepaymentRequest request, CancellationToken ct) => (await service.RecordRepaymentAsync(id, request, ct)).ToActionResult();
    [HttpPost("loans/{id:guid}/prepay"), HasPermission(Permissions.Payroll.LoansRecover)] public async Task<ActionResult<ApiResponse<EmployeeLoanDto>>> Prepay(Guid id, LoanRepaymentRequest request, CancellationToken ct) => (await service.PartialPrepayAsync(id, request, ct)).ToActionResult();
    [HttpPost("loans/{id:guid}/close"), HasPermission(Permissions.Payroll.LoansClose)] public async Task<ActionResult<ApiResponse<EmployeeLoanDto>>> Close(Guid id, LoanRepaymentRequest request, CancellationToken ct) => (await service.CloseAsync(id, request, ct)).ToActionResult();

    [HttpGet("/api/me/loan-products"), HasPermission(Permissions.Payroll.LoansRequest)] public async Task<IActionResult> MyProducts(CancellationToken ct) => (await service.GetProductsAsync(ct)).ToActionResult().Result!;
    [HttpGet("/api/me/loans"), HasPermission(Permissions.Payroll.LoansRequest)] public async Task<IActionResult> MyLoans(CancellationToken ct) => (await LinkedEmployeeLoansAsync(ct)).ToActionResult().Result!;
    [HttpGet("/api/me/loans/{id:guid}"), HasPermission(Permissions.Payroll.LoansRequest)] public async Task<IActionResult> MyLoan(Guid id, CancellationToken ct) { var result = await LinkedEmployeeLoansAsync(ct); if (!result.Succeeded) return result.ToActionResult().Result!; var loan = result.Value!.SingleOrDefault(x => x.Id == id); return loan is null ? NotFound() : Result<EmployeeLoanDto>.Success(loan).ToActionResult().Result!; }
    [HttpPost("/api/me/loans"), HasPermission(Permissions.Payroll.LoansRequest)] public async Task<IActionResult> MyCreate(LoanRequest request, CancellationToken ct) { var identity = await LinkedEmployeeAsync(ct); if (!identity.Succeeded) return identity.ToActionResult().Result!; request.EmployeeId = identity.Value!.EmployeeId; return (await service.CreateLoanAsync(request, ct)).ToActionResult().Result!; }
    [HttpPost("/api/me/loans/{id:guid}/submit"), HasPermission(Permissions.Payroll.LoansRequest)] public async Task<IActionResult> MySubmit(Guid id, CancellationToken ct) => (await service.SubmitAsync(id, ct)).ToActionResult().Result!;

    private async Task<Result<IReadOnlyList<EmployeeLoanDto>>> LinkedEmployeeLoansAsync(CancellationToken ct) { var employee = await LinkedEmployeeAsync(ct); return !employee.Succeeded ? Result<IReadOnlyList<EmployeeLoanDto>>.Failure(employee.Status, employee.Message, employee.Errors) : await service.GetLoansAsync(employee.Value!.EmployeeId, ct); }
    private async Task<Result<AccountEmployeeCurrentDto>> LinkedEmployeeAsync(CancellationToken ct) { if (tenant.UserId is not Guid userId) return Result<AccountEmployeeCurrentDto>.Unauthorized("No authenticated user."); var state = await links.GetUserAsync(userId, ct); if (!state.Succeeded) return Result<AccountEmployeeCurrentDto>.Failure(state.Status, state.Message, state.Errors); return state.Value?.CurrentLink is null ? Result<AccountEmployeeCurrentDto>.Unauthorized("No linked employee identity.") : Result<AccountEmployeeCurrentDto>.Success(state.Value.CurrentLink); }
}
