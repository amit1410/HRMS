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

public sealed class LeaveApprovalReadApiTests
{
    private static readonly Guid RequestId = new("70000000-0000-0000-0000-000000000001");

    [Fact]
    public void Approval_read_routes_require_authentication_and_permission()
    {
        var controller = typeof(LeaveApprovalsController);
        var list = controller.GetMethod(nameof(LeaveApprovalsController.GetInbox))!;
        var detail = controller.GetMethod(nameof(LeaveApprovalsController.GetById))!;

        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("api/leave-approvals", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        AssertPermission(list);
        AssertPermission(detail);
    }

    [Fact]
    public void Approval_read_contract_has_only_server_authorized_route_inputs()
    {
        var list = typeof(LeaveApprovalsController).GetMethod(nameof(LeaveApprovalsController.GetInbox))!;
        var detail = typeof(LeaveApprovalsController).GetMethod(nameof(LeaveApprovalsController.GetById))!;
        var postBodyTypes = new[] { typeof(string), typeof(Guid) };

        Assert.Equal("{requestId:guid}", detail.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal([typeof(int), typeof(int), typeof(CancellationToken)], list.GetParameters().Select(x => x.ParameterType).ToArray());
        Assert.Equal([typeof(Guid), typeof(CancellationToken)], detail.GetParameters().Select(x => x.ParameterType).ToArray());
        Assert.DoesNotContain(list.GetParameters(), x => x.GetCustomAttribute<FromBodyAttribute>() is not null);
        Assert.DoesNotContain(detail.GetParameters(), x => x.GetCustomAttribute<FromBodyAttribute>() is not null);
        Assert.DoesNotContain(postBodyTypes, type => type == typeof(Guid) && detail.GetParameters().Count(x => x.ParameterType == type) > 1);
        Assert.Null(typeof(LeaveApprovalListItemDto).GetProperty("TenantId"));
        Assert.Null(typeof(LeaveApprovalListItemDto).GetProperty("ManagerId"));
        Assert.Null(typeof(LeaveApprovalListItemDto).GetProperty("ApproverEmployeeId"));
    }

    [Fact]
    public async Task List_binds_paging_delegates_and_returns_authoritative_page()
    {
        var item = new LeaveApprovalListItemDto(RequestId, Guid.NewGuid(), "EMP-1", "Request Employee", Guid.NewGuid(), "CL", "Casual Leave", new(2026, 12, 1), new(2026, 12, 1), 1, 1, LeaveRequestStatus.PendingApproval, DateTime.UtcNow);
        var service = new RecordingService(Result<PagedResult<LeaveApprovalListItemDto>>.Success(new([item], 2, 10, 11)));
        var action = await Controller(service).GetInbox(2, 10, CancellationToken.None);

        var envelope = Assert.IsType<ApiResponse<PagedResult<LeaveApprovalListItemDto>>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal(2, service.Page);
        Assert.Equal(10, service.PageSize);
        Assert.Equal(RequestId, envelope.Data!.Items[0].RequestId);
    }

    [Fact]
    public async Task Detail_delegates_request_id_and_returns_authoritative_detail()
    {
        var detail = new LeaveApprovalDetailDto(RequestId, Guid.NewGuid(), "EMP-1", "Request Employee", Guid.NewGuid(), "CL", "Casual Leave", new(2026, 12, 1), new(2026, 12, 1), 1, 1, LeaveRequestStatus.PendingApproval, DateTime.UtcNow, Guid.NewGuid(), "FY26", "Financial Year 2026", Guid.NewGuid(), [], [new(LeaveRequestEventType.Submitted, DateTime.UtcNow)]);
        var service = new RecordingService(Result<LeaveApprovalDetailDto>.Success(detail));
        var action = await Controller(service).GetById(RequestId, CancellationToken.None);

        var envelope = Assert.IsType<ApiResponse<LeaveApprovalDetailDto>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal(RequestId, service.RequestId);
        Assert.Equal(RequestId, envelope.Data!.RequestId);
    }

    [Theory]
    [InlineData(ResultStatus.NotFound, 404)]
    [InlineData(ResultStatus.Forbidden, 403)]
    public async Task Detail_failures_use_shared_mapping(ResultStatus status, int expectedStatus)
    {
        var service = new RecordingService(Result<LeaveApprovalDetailDto>.Failure(status, "request is not available"));
        var action = await Controller(service).GetById(RequestId, CancellationToken.None);

        Assert.Equal(expectedStatus, Assert.IsType<ObjectResult>(action.Result).StatusCode);
    }

    private static LeaveApprovalsController Controller(RecordingService service) => new(service);

    private static void AssertPermission(MethodInfo method)
    {
        var permission = method.GetCustomAttributes<HasPermissionAttribute>().Single();
        Assert.Equal(Permissions.Leave.Approve, permission.Permission);
        Assert.Equal(Permissions.Leave.Approve, permission.Policy);
    }

    private sealed class RecordingService : ILeaveApprovalReadService
    {
        private readonly Result<PagedResult<LeaveApprovalListItemDto>> _listResult;
        private readonly Result<LeaveApprovalDetailDto> _detailResult;
        public int Page { get; private set; }
        public int PageSize { get; private set; }
        public Guid RequestId { get; private set; }

        public RecordingService(Result<PagedResult<LeaveApprovalListItemDto>> listResult)
        {
            _listResult = listResult;
            _detailResult = Result<LeaveApprovalDetailDto>.NotFound("not used");
        }

        public RecordingService(Result<LeaveApprovalDetailDto> detailResult)
        {
            _detailResult = detailResult;
            _listResult = Result<PagedResult<LeaveApprovalListItemDto>>.Success(PagedResult<LeaveApprovalListItemDto>.Empty(1, 25));
        }

        public Task<Result<PagedResult<LeaveApprovalListItemDto>>> GetInboxAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        {
            Page = page;
            PageSize = pageSize;
            return Task.FromResult(_listResult);
        }

        public Task<Result<LeaveApprovalDetailDto>> GetByIdAsync(Guid requestId, CancellationToken cancellationToken = default)
        {
            RequestId = requestId;
            return Task.FromResult(_detailResult);
        }
    }
}
