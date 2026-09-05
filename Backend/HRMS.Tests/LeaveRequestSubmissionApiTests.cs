using System.Reflection;
using HRMS.API.Controllers;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.Tests;

public sealed class LeaveRequestSubmissionApiTests
{
    [Fact]
    public void Submission_requires_authentication_and_has_the_public_route()
    {
        var controller = typeof(LeaveRequestsController);
        var authorize = controller.GetCustomAttribute<AuthorizeAttribute>();
        var submit = controller.GetMethod(nameof(LeaveRequestsController.Submit));

        Assert.NotNull(authorize);
        Assert.Null(authorize!.Policy);
        Assert.Equal("api/leave-requests", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Null(submit!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void Public_request_contains_only_server_authorized_submission_inputs()
    {
        Assert.Equal(
            ["LeaveTypeId", "StartDate", "EndDate", "IdempotencyKey"],
            typeof(LeaveRequestSubmissionRequest).GetProperties().Select(x => x.Name).ToArray());
        Assert.Null(typeof(LeaveRequestSubmissionRequest).GetProperty("EmployeeId"));
        Assert.Null(typeof(LeaveRequestSubmissionRequest).GetProperty("TenantId"));
        Assert.Null(typeof(LeaveRequestSubmissionRequest).GetProperty("Status"));
        Assert.Null(typeof(LeaveRequestSubmissionRequest).GetProperty("RequestDays"));
    }

    [Fact]
    public async Task Successful_submission_returns_201_and_authoritative_response()
    {
        var expected = Submission(replay: false);
        var service = new RecordingSubmissionService(Result<LeaveRequestSubmissionResult>.Success(expected));
        var action = await Controller(service).Submit(Request(), CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(201, result.StatusCode);
        var envelope = Assert.IsType<ApiResponse<LeaveRequestSubmissionResponse>>(result.Value);
        Assert.True(envelope.Success);
        Assert.Equal(expected.RequestId, envelope.Data!.RequestId);
        Assert.Equal(LeaveRequestStatus.PendingApproval, envelope.Data.Status);
        Assert.Equal(expected.EmployeeId, envelope.Data.EmployeeId);
        Assert.Equal(expected.EmployeeEmploymentHistoryId, envelope.Data.EmployeeEmploymentHistoryId);
        Assert.Equal(expected.LeavePeriodId, envelope.Data.LeavePeriodId);
        Assert.Single(envelope.Data.RequestDays);
        Assert.False(envelope.Data.IsReplay);
    }

    [Fact]
    public async Task Idempotent_replay_returns_200_without_creating_a_second_request()
    {
        var expected = Submission(replay: true);
        var service = new RecordingSubmissionService(Result<LeaveRequestSubmissionResult>.Success(expected));
        var action = await Controller(service).Submit(Request(), CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(200, result.StatusCode);
        var envelope = Assert.IsType<ApiResponse<LeaveRequestSubmissionResponse>>(result.Value);
        Assert.True(envelope.Data!.IsReplay);
        Assert.Equal(expected.RequestId, envelope.Data.RequestId);
    }

    [Theory]
    [InlineData(ResultStatus.ValidationFailed, 400)]
    [InlineData(ResultStatus.Unauthorized, 401)]
    [InlineData(ResultStatus.Forbidden, 403)]
    [InlineData(ResultStatus.NotFound, 404)]
    [InlineData(ResultStatus.Conflict, 409)]
    public async Task Submission_preserves_shared_error_mapping(ResultStatus status, int expectedStatusCode)
    {
        var service = new RecordingSubmissionService(Result<LeaveRequestSubmissionResult>.Failure(
            status,
            status == ResultStatus.ValidationFailed
                ? "AllocatedBalanceReservationNotReady: balance reservation is not implemented."
                : "IdempotencyConflict: the key was already used."));

        var action = await Controller(service).Submit(Request(), CancellationToken.None);

        Assert.Equal(expectedStatusCode, Assert.IsType<ObjectResult>(action.Result).StatusCode);
    }

    [Fact]
    public async Task Submission_passes_exact_input_and_cancellation_token_to_service()
    {
        using var cancellation = new CancellationTokenSource();
        var request = Request();
        var service = new RecordingSubmissionService(Result<LeaveRequestSubmissionResult>.Success(Submission(false)));

        await Controller(service).Submit(request, cancellation.Token);

        Assert.Equal(new LeaveRequestSubmissionInput(
            request.LeaveTypeId,
            request.StartDate,
            request.EndDate,
            request.IdempotencyKey), service.Input);
        Assert.Equal(cancellation.Token, service.CancellationToken);
    }

    [Fact]
    public async Task Conflict_messages_remain_client_safe_and_do_not_expose_sql_details()
    {
        var service = new RecordingSubmissionService(Result<LeaveRequestSubmissionResult>.Conflict(
            "ConcurrencyConflict: The request could not be submitted after the maximum deadlock retry attempts."));

        var action = await Controller(service).Submit(Request(), CancellationToken.None);
        var envelope = Assert.IsType<ApiResponse<LeaveRequestSubmissionResponse>>(
            Assert.IsType<ObjectResult>(action.Result).Value);

        Assert.Equal(409, ((ObjectResult)action.Result!).StatusCode);
        Assert.DoesNotContain("SqlException", envelope.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection string", envelope.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Allocated_reservation_not_ready_is_a_deterministic_bad_request()
    {
        var action = await Controller(new RecordingSubmissionService(Result<LeaveRequestSubmissionResult>.Invalid(
            "entitlement",
            "AllocatedBalanceReservationNotReady: balance reservation is not implemented.")))
            .Submit(Request(), CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("AllocatedBalanceReservationNotReady", Assert.IsType<ApiResponse<LeaveRequestSubmissionResponse>>(result.Value).Message);
    }

    [Fact]
    public async Task Unsupported_configuration_is_a_deterministic_bad_request()
    {
        var action = await Controller(new RecordingSubmissionService(Result<LeaveRequestSubmissionResult>.Invalid(
            "configuration",
            "UnsupportedConfiguration: runtime rule is not supported.")))
            .Submit(Request(), CancellationToken.None);

        Assert.Equal(400, Assert.IsType<ObjectResult>(action.Result).StatusCode);
    }

    [Fact]
    public async Task Idempotency_conflict_is_a_conflict_response()
    {
        var action = await Controller(new RecordingSubmissionService(Result<LeaveRequestSubmissionResult>.Conflict(
            "IdempotencyConflict: the key was already used for a different request.")))
            .Submit(Request(), CancellationToken.None);

        Assert.Equal(409, Assert.IsType<ObjectResult>(action.Result).StatusCode);
    }

    [Fact]
    public async Task Overlap_is_a_conflict_response()
    {
        var action = await Controller(new RecordingSubmissionService(Result<LeaveRequestSubmissionResult>.Conflict(
            "Overlap: the request overlaps an active Leave request.")))
            .Submit(Request(), CancellationToken.None);

        Assert.Equal(409, Assert.IsType<ObjectResult>(action.Result).StatusCode);
    }

    [Fact]
    public async Task Unlinked_account_failure_is_an_unauthorized_response()
    {
        var action = await Controller(new RecordingSubmissionService(Result<LeaveRequestSubmissionResult>.Failure(
            ResultStatus.Unauthorized,
            "The authenticated account is not linked to an Employee.")))
            .Submit(Request(), CancellationToken.None);

        Assert.Equal(401, Assert.IsType<ObjectResult>(action.Result).StatusCode);
    }

    private static LeaveRequestsController Controller(RecordingSubmissionService service) =>
        new(new NoOpValidationService(), service);

    private static LeaveRequestSubmissionRequest Request() =>
        new(Guid.Parse("10000000-0000-0000-0000-000000000001"), new(2026, 9, 1), new(2026, 9, 1), "api-key");

    private static LeaveRequestSubmissionResult Submission(bool replay) =>
        new(
            Guid.Parse("70000000-0000-0000-0000-000000000001"),
            LeaveRequestStatus.PendingApproval,
            Guid.Parse("a4000000-0000-0000-0000-000000000101"),
            Guid.Parse("60000000-0000-0000-0000-000000000101"),
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            new(2026, 9, 1),
            new(2026, 9, 1),
            1m,
            1m,
            DateTime.UtcNow,
            [new LeaveRequestSubmissionDay(new(2026, 9, 1), 1m, 1m, null, null, true)],
            replay);

    private sealed class RecordingSubmissionService(Result<LeaveRequestSubmissionResult> result) : ILeaveRequestSubmissionService
    {
        public LeaveRequestSubmissionInput? Input { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<Result<LeaveRequestSubmissionResult>> SubmitAsync(
            LeaveRequestSubmissionInput input,
            CancellationToken cancellationToken = default)
        {
            Input = input;
            CancellationToken = cancellationToken;
            return Task.FromResult(result);
        }
    }

    private sealed class NoOpValidationService : ILeaveRequestValidationService
    {
        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(
            LeaveRequestValidationInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<LeaveRequestValidationResult>.Invalid("test", "not used"));
    }
}
