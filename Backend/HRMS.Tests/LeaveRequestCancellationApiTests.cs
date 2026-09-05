using System.Reflection;
using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.Tests;

public sealed class LeaveRequestCancellationApiTests
{
    private static readonly Guid RequestId = new("70000000-0000-0000-0000-000000000001");

    [Fact]
    public void Cancel_requires_authentication_and_no_permission()
    {
        var controller = typeof(LeaveRequestsController);
        var cancel = controller.GetMethod(nameof(LeaveRequestsController.Cancel))!;
        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Null(cancel.GetCustomAttribute<HasPermissionAttribute>());
    }

    [Fact]
    public void Cancel_is_post_route_only_and_bodyless()
    {
        var cancel = typeof(LeaveRequestsController).GetMethod(nameof(LeaveRequestsController.Cancel))!;
        Assert.Equal("{requestId:guid}/cancel", cancel.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal([typeof(Guid), typeof(CancellationToken)], cancel.GetParameters().Select(x => x.ParameterType).ToArray());
        Assert.DoesNotContain(cancel.GetParameters(), x => x.GetCustomAttribute<FromBodyAttribute>() is not null);
    }

    [Fact]
    public async Task Successful_cancellation_delegates_and_maps_result()
    {
        var service = new RecordingCancellationService(Result<LeaveRequestCancellationResult>.Success(
            new(RequestId, LeaveRequestStatus.Cancelled, LeaveRequestEventType.Cancelled, DateTime.UtcNow)));
        var action = await Controller(service).Cancel(RequestId, CancellationToken.None);
        var result = Assert.IsType<OkObjectResult>(action.Result);
        var envelope = Assert.IsType<ApiResponse<LeaveRequestCancellationResult>>(result.Value);
        Assert.True(envelope.Success);
        Assert.Equal(RequestId, service.RequestId);
        Assert.Equal(LeaveRequestStatus.Cancelled, envelope.Data!.Status);
        Assert.Equal(LeaveRequestEventType.Cancelled, envelope.Data.EventType);
    }

    [Theory]
    [InlineData(ResultStatus.Conflict, 409, "InvalidStatusTransition")]
    [InlineData(ResultStatus.Conflict, 409, "CancellationNotAllowed")]
    [InlineData(ResultStatus.Conflict, 409, "AllocatedCancellationBalanceReleaseNotReady")]
    [InlineData(ResultStatus.NotFound, 404, "Leave request was not found")]
    [InlineData(ResultStatus.Unauthorized, 401, "authentication")]
    public async Task Cancellation_failures_use_shared_mapping(ResultStatus status, int expectedStatus, string message)
    {
        var service = new RecordingCancellationService(Result<LeaveRequestCancellationResult>.Failure(status, message));
        var action = await Controller(service).Cancel(RequestId, CancellationToken.None);
        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(expectedStatus, result.StatusCode);
        var envelope = Assert.IsType<ApiResponse<LeaveRequestCancellationResult>>(result.Value);
        Assert.Contains(message.Split(':')[0], envelope.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static LeaveRequestsController Controller(RecordingCancellationService service) =>
        new(new NoOpValidationService(), null, null, null, null, service);

    private sealed class RecordingCancellationService(Result<LeaveRequestCancellationResult> result) : ILeaveRequestCancellationService
    {
        public Guid? RequestId { get; private set; }
        public Task<Result<LeaveRequestCancellationResult>> CancelAsync(Guid requestId, CancellationToken cancellationToken = default)
        {
            RequestId = requestId;
            return Task.FromResult(result);
        }
    }

    private sealed class NoOpValidationService : ILeaveRequestValidationService
    {
        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(LeaveRequestValidationInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<LeaveRequestValidationResult>.Invalid("test", "not used"));
    }
}
