using HRMS.API.Extensions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.API.Security;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/leave-requests"), Produces("application/json")]
[Authorize]
public sealed class LeaveRequestsController : ControllerBase
{
    private readonly ILeaveRequestValidationService _validationService;
    private readonly ILeaveRequestSubmissionService? _submissionService;
    private readonly ILeaveRequestReadService? _readService;
    private readonly ILeaveRequestApprovalService? _approvalService;
    private readonly ILeaveRequestWithdrawalService? _withdrawalService;
    private readonly ILeaveRequestCancellationService? _cancellationService;

    public LeaveRequestsController(ILeaveRequestValidationService validationService)
        : this(validationService, null, null, null, null, null)
    {
    }

    public LeaveRequestsController(
        ILeaveRequestValidationService validationService,
        ILeaveRequestSubmissionService? submissionService)
        : this(validationService, submissionService, null, null, null, null)
    {
    }

    public LeaveRequestsController(
        ILeaveRequestValidationService validationService,
        ILeaveRequestSubmissionService? submissionService,
        ILeaveRequestReadService? readService)
        : this(validationService, submissionService, readService, null, null, null)
    {
    }

    public LeaveRequestsController(
        ILeaveRequestValidationService validationService,
        ILeaveRequestSubmissionService? submissionService,
        ILeaveRequestReadService? readService,
        ILeaveRequestApprovalService? approvalService)
        : this(validationService, submissionService, readService, approvalService, null, null)
    {
    }

    public LeaveRequestsController(
        ILeaveRequestValidationService validationService,
        ILeaveRequestSubmissionService? submissionService,
        ILeaveRequestReadService? readService,
        ILeaveRequestApprovalService? approvalService,
        ILeaveRequestWithdrawalService? withdrawalService,
        ILeaveRequestCancellationService? cancellationService = null)
    {
        _validationService = validationService;
        _submissionService = submissionService;
        _readService = readService;
        _approvalService = approvalService;
        _withdrawalService = withdrawalService;
        _cancellationService = cancellationService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<LeaveRequestListItemDto>>>> GetMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (_readService is null) throw new InvalidOperationException("Leave request read service is not configured.");
        return (await _readService.GetMineAsync(page, pageSize, cancellationToken)).ToActionResult();
    }

    [HttpGet("{requestId:guid}")]
    public async Task<ActionResult<ApiResponse<LeaveRequestDetailDto>>> GetMineById(Guid requestId, CancellationToken cancellationToken)
    {
        if (_readService is null) throw new InvalidOperationException("Leave request read service is not configured.");
        return (await _readService.GetMineByIdAsync(requestId, cancellationToken)).ToActionResult();
    }

    [HttpPost("preview")]
    public async Task<ActionResult<ApiResponse<LeaveRequestPreviewResponse>>> Preview(
        [FromBody] LeaveRequestPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _validationService.ValidateAsync(
            new LeaveRequestValidationInput(request.LeaveTypeId, request.StartDate, request.EndDate, request.IdempotencyKey),
            cancellationToken);

        if (!result.Succeeded)
            return Result<LeaveRequestPreviewResponse>.Failure(
                result.Status,
                result.Message,
                result.Errors).ToActionResult();

        var validation = result.Value!;
        var response = new LeaveRequestPreviewResponse(
            validation.EmployeeId,
            validation.LeaveTypeId,
            validation.LeavePeriodId,
            validation.LeavePolicyVersionId,
            validation.LeavePolicyRuleId,
            validation.StartDate,
            validation.EndDate,
            validation.RequestedQuantity,
            validation.ChargeableQuantity,
            validation.RequestDays.Select(day => new LeaveRequestPreviewDay(
                day.Date,
                day.RequestedQuantity,
                day.ChargeableQuantity,
                day.DayClassification,
                day.CalculationReason,
                day.IsEmployeeRequested)).ToList(),
            validation.EntitlementMode,
            validation.BalanceReservationRequired,
            validation.AttachmentRequired,
            validation.PayloadFingerprint);

        return Result<LeaveRequestPreviewResponse>.Success(response).ToActionResult();
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<LeaveRequestSubmissionResponse>>> Submit(
        [FromBody] LeaveRequestSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        if (_submissionService is null)
            throw new InvalidOperationException("Leave request submission service is not configured.");

        var result = await _submissionService.SubmitAsync(
            new LeaveRequestSubmissionInput(request.LeaveTypeId, request.StartDate, request.EndDate, request.IdempotencyKey),
            cancellationToken);

        if (!result.Succeeded)
        {
            return Result<LeaveRequestSubmissionResponse>.Failure(
                result.Status,
                result.Message,
                result.Errors).ToErrorResult();
        }

        var submitted = result.Value!;
        var response = new LeaveRequestSubmissionResponse(
            submitted.RequestId,
            submitted.Status,
            submitted.EmployeeId,
            submitted.LeaveTypeId,
            submitted.LeavePeriodId,
            submitted.LeavePolicyVersionId,
            submitted.LeavePolicyRuleId,
            submitted.EmployeeEmploymentHistoryId,
            submitted.StartDate,
            submitted.EndDate,
            submitted.RequestedQuantity,
            submitted.ChargeableQuantity,
            submitted.SubmittedAtUtc,
            submitted.RequestDays.Select(day => new LeaveRequestSubmissionDayResponse(
                day.Date,
                day.RequestedQuantity,
                day.ChargeableQuantity,
                day.DayClassification,
                day.CalculationReason,
                day.IsEmployeeRequested)).ToList(),
            submitted.IdempotentReplay);

        var envelope = ApiResponse<LeaveRequestSubmissionResponse>.Ok(response, result.Message);
        return new ObjectResult(envelope)
        {
            StatusCode = submitted.IdempotentReplay ? StatusCodes.Status200OK : StatusCodes.Status201Created
        };
    }

    [HttpPost("{requestId:guid}/approve"), HasPermission(Permissions.Leave.Approve)]
    public async Task<ActionResult<ApiResponse<LeaveRequestApprovalResult>>> Approve(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (_approvalService is null)
            throw new InvalidOperationException("Leave request approval service is not configured.");
        return (await _approvalService.ApproveAsync(requestId, cancellationToken)).ToActionResult();
    }

    [HttpPost("{requestId:guid}/reject"), HasPermission(Permissions.Leave.Approve)]
    public async Task<ActionResult<ApiResponse<LeaveRequestApprovalResult>>> Reject(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (_approvalService is null)
            throw new InvalidOperationException("Leave request approval service is not configured.");
        return (await _approvalService.RejectAsync(requestId, cancellationToken)).ToActionResult();
    }

    [HttpPost("{requestId:guid}/withdraw")]
    public async Task<ActionResult<ApiResponse<LeaveRequestWithdrawalResult>>> Withdraw(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (_withdrawalService is null)
            throw new InvalidOperationException("Leave request withdrawal service is not configured.");
        return (await _withdrawalService.WithdrawAsync(requestId, cancellationToken)).ToActionResult();
    }

    [HttpPost("{requestId:guid}/cancel")]
    public async Task<ActionResult<ApiResponse<LeaveRequestCancellationResult>>> Cancel(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (_cancellationService is null)
            throw new InvalidOperationException("Leave request cancellation service is not configured.");
        return (await _cancellationService.CancelAsync(requestId, cancellationToken)).ToActionResult();
    }
}
