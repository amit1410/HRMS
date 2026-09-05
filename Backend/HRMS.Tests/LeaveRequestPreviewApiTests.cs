using System.Reflection;
using HRMS.API.Controllers;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.Tests;

public sealed class LeaveRequestPreviewApiTests
{
    [Fact]
    public void Preview_endpoint_requires_authentication_without_an_admin_permission()
    {
        var controller = typeof(LeaveRequestsController);
        var authorize = controller.GetCustomAttribute<AuthorizeAttribute>();
        var preview = controller.GetMethod(nameof(LeaveRequestsController.Preview));

        Assert.NotNull(authorize);
        Assert.Null(authorize!.Policy);
        Assert.NotNull(preview?.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal("preview", preview!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public async Task Preview_maps_authoritative_validation_result_without_persistence()
    {
        const string payloadFingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var expected = new LeaveRequestValidationResult(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Gender.Unspecified, new(2026, 9, 1), new(2026, 9, 2), 2m, 2m,
            [
                new(new(2026, 9, 1), 1m, 1m, null, null, true),
                new(new(2026, 9, 2), 1m, 1m, null, null, true)
            ],
            EntitlementMode.Allocated, true, false,
            IdempotencyKey: "preview-test-key",
            PayloadFingerprint: payloadFingerprint,
            PolicyPriority: 2,
            PolicySpecificity: 1);
        var service = new RecordingValidationService(expected);
        var controller = new LeaveRequestsController(service);

        var action = await controller.Preview(
            new LeaveRequestPreviewRequest(expected.LeaveTypeId, expected.StartDate, expected.EndDate, "client-key"),
            CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(action.Result);
        var envelope = Assert.IsType<ApiResponse<LeaveRequestPreviewResponse>>(result.Value);
        Assert.Equal(expected.EmployeeId, envelope.Data!.EmployeeId);
        Assert.Equal(2m, envelope.Data.RequestedQuantity);
        Assert.Equal(2, envelope.Data.RequestDays.Count);
        Assert.True(envelope.Data.BalanceReservationRequired);
        Assert.Equal(payloadFingerprint, envelope.Data.PayloadFingerprint);
        Assert.Null(typeof(LeaveRequestPreviewResponse).GetProperty("PolicyPriority"));
        Assert.Null(typeof(LeaveRequestPreviewResponse).GetProperty("PolicySpecificity"));
        Assert.Equal("client-key", service.Input!.IdempotencyKey);
    }

    [Fact]
    public void Preview_request_contains_only_client_input_fields()
    {
        var properties = typeof(LeaveRequestPreviewRequest).GetProperties().Select(x => x.Name).ToArray();

        Assert.Equal(
            ["LeaveTypeId", "StartDate", "EndDate", "IdempotencyKey"],
            properties);
    }

    [Fact]
    public void Preview_route_remains_separate_from_submission_action()
    {
        var preview = typeof(LeaveRequestsController).GetMethod(nameof(LeaveRequestsController.Preview));
        var submit = typeof(LeaveRequestsController).GetMethod(nameof(LeaveRequestsController.Submit));

        Assert.NotNull(preview);
        Assert.Equal("preview", preview!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(typeof(LeaveRequestPreviewRequest), preview.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(Task<ActionResult<ApiResponse<LeaveRequestPreviewResponse>>>), preview.ReturnType);

        Assert.NotNull(submit);
        Assert.Null(submit!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(typeof(LeaveRequestSubmissionRequest), submit.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(Task<ActionResult<ApiResponse<LeaveRequestSubmissionResponse>>>), submit.ReturnType);
        Assert.NotSame(preview, submit);
    }

    [Fact]
    public async Task Preview_preserves_nullable_day_snapshots()
    {
        var day = new LeaveRequestDayValidationResult(new(2026, 9, 1), 1m, 1m, null, null, true);
        var expected = CreateResult([day], EntitlementMode.Unlimited, false);
        var action = await new LeaveRequestsController(new RecordingValidationService(expected))
            .Preview(new(expected.LeaveTypeId, expected.StartDate, expected.EndDate, "key"), CancellationToken.None);

        var envelope = Assert.IsType<ApiResponse<LeaveRequestPreviewResponse>>(
            Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Null(envelope.Data!.RequestDays[0].DayClassification);
        Assert.Null(envelope.Data.RequestDays[0].CalculationReason);
        Assert.False(envelope.Data.BalanceReservationRequired);
    }

    [Theory]
    [InlineData(ResultStatus.ValidationFailed, 400)]
    [InlineData(ResultStatus.Unauthorized, 401)]
    [InlineData(ResultStatus.Forbidden, 403)]
    [InlineData(ResultStatus.NotFound, 404)]
    [InlineData(ResultStatus.Conflict, 409)]
    public async Task Preview_preserves_expected_result_status_mapping(ResultStatus status, int httpStatus)
    {
        var failure = Result<LeaveRequestValidationResult>.Failure(status, "expected failure");
        var action = await new LeaveRequestsController(new RecordingValidationService(failure))
            .Preview(new(Guid.NewGuid(), new(2026, 9, 1), new(2026, 9, 1), "key"), CancellationToken.None);

        Assert.Equal(httpStatus, Assert.IsType<ObjectResult>(action.Result).StatusCode);
    }

    private static LeaveRequestValidationResult CreateResult(
        IReadOnlyList<LeaveRequestDayValidationResult> days,
        EntitlementMode entitlement,
        bool reservationRequired) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Gender.Unspecified, days[0].Date, days[^1].Date, days.Count, days.Count, days,
        entitlement, reservationRequired, false,
        IdempotencyKey: "preview-test-key",
        PayloadFingerprint: "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        PolicyPriority: 1,
        PolicySpecificity: 1);

    [Fact]
    public async Task Preview_maps_unsupported_configuration_without_persistence()
    {
        var service = new RecordingValidationService(Result<LeaveRequestValidationResult>.Invalid(
            "configuration", "UnsupportedConfiguration: calendar runtime is unavailable."));
        var controller = new LeaveRequestsController(service);

        var action = await controller.Preview(
            new LeaveRequestPreviewRequest(Guid.NewGuid(), new(2026, 9, 1), new(2026, 9, 1), "key"),
            CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(400, result.StatusCode);
        Assert.False(((ApiResponse<LeaveRequestPreviewResponse>)result.Value!).Success);
        Assert.Contains("UnsupportedConfiguration", ((ApiResponse<LeaveRequestPreviewResponse>)result.Value!).Message);
    }

    private sealed class RecordingValidationService : ILeaveRequestValidationService
    {
        private readonly Result<LeaveRequestValidationResult> _result;
        public LeaveRequestValidationInput? Input { get; private set; }

        public RecordingValidationService(LeaveRequestValidationResult result) =>
            _result = Result<LeaveRequestValidationResult>.Success(result);

        public RecordingValidationService(Result<LeaveRequestValidationResult> result) => _result = result;

        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(LeaveRequestValidationInput input, CancellationToken cancellationToken = default)
        {
            Input = input;
            return Task.FromResult(_result);
        }
    }
}
