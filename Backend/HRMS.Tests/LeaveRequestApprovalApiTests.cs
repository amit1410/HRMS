using System.Reflection;
using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.Tests;

public sealed class LeaveRequestApprovalApiTests
{
    private static readonly Guid RequestId = new("70000000-0000-0000-0000-000000000001");

    [Fact]
    public void Approval_routes_require_authentication_and_permission()
    {
        var controller = typeof(LeaveRequestsController);
        var approve = controller.GetMethod(nameof(LeaveRequestsController.Approve))!;
        var reject = controller.GetMethod(nameof(LeaveRequestsController.Reject))!;

        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/leave-requests", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        AssertPermission(approve);
        AssertPermission(reject);
    }

    [Fact]
    public void Approval_actions_are_post_route_only_and_have_no_public_body()
    {
        var approve = typeof(LeaveRequestsController).GetMethod(nameof(LeaveRequestsController.Approve))!;
        var reject = typeof(LeaveRequestsController).GetMethod(nameof(LeaveRequestsController.Reject))!;

        AssertRoute(approve, "{requestId:guid}/approve");
        AssertRoute(reject, "{requestId:guid}/reject");
        AssertBodyless(approve);
        AssertBodyless(reject);
    }

    [Fact]
    public async Task Successful_approval_uses_shared_success_mapping_and_delegates_request_id()
    {
        var expected = Result<LeaveRequestApprovalResult>.Success(
            new(RequestId, LeaveRequestStatus.Approved, LeaveRequestEventType.Approved, DateTime.UtcNow));
        var service = new RecordingApprovalService(expected);

        var action = await Controller(service).Approve(RequestId, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(action.Result);
        var envelope = Assert.IsType<ApiResponse<LeaveRequestApprovalResult>>(result.Value);
        Assert.True(envelope.Success);
        Assert.Equal(RequestId, service.ApproveRequestId);
        Assert.Equal(LeaveRequestStatus.Approved, envelope.Data!.Status);
        Assert.Equal(LeaveRequestEventType.Approved, envelope.Data.EventType);
    }

    [Fact]
    public async Task Successful_rejection_uses_shared_success_mapping_and_delegates_request_id()
    {
        var expected = Result<LeaveRequestApprovalResult>.Success(
            new(RequestId, LeaveRequestStatus.Rejected, LeaveRequestEventType.Rejected, DateTime.UtcNow));
        var service = new RecordingApprovalService(expected);

        var action = await Controller(service).Reject(RequestId, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(action.Result);
        var envelope = Assert.IsType<ApiResponse<LeaveRequestApprovalResult>>(result.Value);
        Assert.True(envelope.Success);
        Assert.Equal(RequestId, service.RejectRequestId);
        Assert.Equal(LeaveRequestStatus.Rejected, envelope.Data!.Status);
        Assert.Equal(LeaveRequestEventType.Rejected, envelope.Data.EventType);
    }

    [Theory]
    [InlineData(ResultStatus.Conflict, 409, "InvalidStatusTransition: Only PendingApproval requests can be changed.")]
    [InlineData(ResultStatus.Forbidden, 403, "ApproverNotAuthorized: The authenticated account is not authorized.")]
    [InlineData(ResultStatus.NotFound, 404, "Leave request was not found.")]
    public async Task Approval_failures_use_shared_error_mapping(ResultStatus status, int expectedStatusCode, string message)
    {
        var service = new RecordingApprovalService(Result<LeaveRequestApprovalResult>.Failure(status, message));

        var action = await Controller(service).Approve(RequestId, CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(expectedStatusCode, result.StatusCode);
        var envelope = Assert.IsType<ApiResponse<LeaveRequestApprovalResult>>(result.Value);
        Assert.Contains(message.Split(':')[0], envelope.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejection_authorization_failure_uses_shared_forbidden_mapping()
    {
        var service = new RecordingApprovalService(Result<LeaveRequestApprovalResult>.Forbidden(
            "ApproverNotAuthorized: The authenticated account is not the current manager."));

        var action = await Controller(service).Reject(RequestId, CancellationToken.None);

        Assert.Equal(403, Assert.IsType<ObjectResult>(action.Result).StatusCode);
    }

    private static LeaveRequestsController Controller(RecordingApprovalService service) =>
        new(new NoOpValidationService(), null, null, service);

    private static void AssertPermission(MethodInfo method)
    {
        var attribute = method.GetCustomAttributes<HasPermissionAttribute>().Single();
        Assert.Equal(Permissions.Leave.Approve, attribute.Permission);
        Assert.Equal(Permissions.Leave.Approve, attribute.Policy);
    }

    private static void AssertRoute(MethodInfo method, string expectedTemplate)
    {
        var post = method.GetCustomAttribute<HttpPostAttribute>();
        Assert.NotNull(post);
        Assert.Equal(expectedTemplate, post!.Template);
    }

    private static void AssertBodyless(MethodInfo method)
    {
        var parameters = method.GetParameters();
        Assert.Equal([typeof(Guid), typeof(CancellationToken)], parameters.Select(x => x.ParameterType).ToArray());
        Assert.DoesNotContain(parameters, x => x.GetCustomAttribute<FromBodyAttribute>() is not null);
        Assert.DoesNotContain(parameters, x => x.ParameterType == typeof(string) || x.ParameterType.IsClass);
    }

    private sealed class RecordingApprovalService(Result<LeaveRequestApprovalResult> result) : ILeaveRequestApprovalService
    {
        public Guid? ApproveRequestId { get; private set; }
        public Guid? RejectRequestId { get; private set; }

        public Task<Result<LeaveRequestApprovalResult>> ApproveAsync(Guid requestId, CancellationToken cancellationToken = default)
        {
            ApproveRequestId = requestId;
            return Task.FromResult(result);
        }

        public Task<Result<LeaveRequestApprovalResult>> RejectAsync(Guid requestId, CancellationToken cancellationToken = default)
        {
            RejectRequestId = requestId;
            return Task.FromResult(result);
        }
    }

    private sealed class NoOpValidationService : ILeaveRequestValidationService
    {
        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(LeaveRequestValidationInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<LeaveRequestValidationResult>.Invalid("test", "not used"));
    }
}
