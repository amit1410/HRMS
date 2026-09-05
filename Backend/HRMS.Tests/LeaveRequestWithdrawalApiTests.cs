using System.Reflection;
using HRMS.API.Controllers;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.Tests;

public sealed class LeaveRequestWithdrawalApiTests
{
    private static readonly Guid RequestId = new("70000000-0000-0000-0000-000000000001");

    [Fact]
    public void Withdraw_requires_authentication_and_has_no_permission_requirement()
    {
        var controller = typeof(LeaveRequestsController);
        var withdraw = controller.GetMethod(nameof(LeaveRequestsController.Withdraw))!;

        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/leave-requests", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Null(withdraw.GetCustomAttribute<HRMS.API.Security.HasPermissionAttribute>());
    }

    [Fact]
    public void Withdraw_is_post_route_only_and_bodyless()
    {
        var withdraw = typeof(LeaveRequestsController).GetMethod(nameof(LeaveRequestsController.Withdraw))!;
        Assert.Equal("{requestId:guid}/withdraw", withdraw.GetCustomAttribute<HttpPostAttribute>()!.Template);
        var parameters = withdraw.GetParameters();
        Assert.Equal([typeof(Guid), typeof(CancellationToken)], parameters.Select(x => x.ParameterType).ToArray());
        Assert.DoesNotContain(parameters, x => x.GetCustomAttribute<FromBodyAttribute>() is not null);
    }

    [Fact]
    public async Task Successful_withdrawal_delegates_request_id_and_maps_success()
    {
        var expected = Result<LeaveRequestWithdrawalResult>.Success(
            new(RequestId, LeaveRequestStatus.Withdrawn, LeaveRequestEventType.Withdrawn, DateTime.UtcNow));
        var service = new RecordingWithdrawalService(expected);

        var action = await Controller(service).Withdraw(RequestId, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(action.Result);
        var envelope = Assert.IsType<ApiResponse<LeaveRequestWithdrawalResult>>(result.Value);
        Assert.True(envelope.Success);
        Assert.Equal(RequestId, service.RequestId);
        Assert.Equal(LeaveRequestStatus.Withdrawn, envelope.Data!.Status);
        Assert.Equal(LeaveRequestEventType.Withdrawn, envelope.Data.EventType);
    }

    [Theory]
    [InlineData(ResultStatus.Conflict, 409, "InvalidStatusTransition")]
    [InlineData(ResultStatus.NotFound, 404, "Leave request was not found")]
    [InlineData(ResultStatus.Unauthorized, 401, "authentication")]
    public async Task Withdrawal_failures_use_shared_mapping(ResultStatus status, int expectedStatusCode, string messageFragment)
    {
        var service = new RecordingWithdrawalService(Result<LeaveRequestWithdrawalResult>.Failure(status, messageFragment));

        var action = await Controller(service).Withdraw(RequestId, CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(expectedStatusCode, result.StatusCode);
        var envelope = Assert.IsType<ApiResponse<LeaveRequestWithdrawalResult>>(result.Value);
        Assert.Contains(messageFragment, envelope.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static LeaveRequestsController Controller(RecordingWithdrawalService service) =>
        new(new NoOpValidationService(), null, null, null, service);

    private sealed class RecordingWithdrawalService(Result<LeaveRequestWithdrawalResult> result) : ILeaveRequestWithdrawalService
    {
        public Guid? RequestId { get; private set; }

        public Task<Result<LeaveRequestWithdrawalResult>> WithdrawAsync(Guid requestId, CancellationToken cancellationToken = default)
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
