using System.Reflection;
using HRMS.API.Controllers;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.Tests;

public sealed class LeaveRequestReadApiTests
{
    [Fact]
    public void Read_routes_require_authentication()
    {
        var controller = typeof(LeaveRequestsController);
        Assert.NotNull(controller.GetCustomAttribute<AuthorizeAttribute>());
        Assert.NotNull(controller.GetMethod(nameof(LeaveRequestsController.GetMine)));
        Assert.NotNull(controller.GetMethod(nameof(LeaveRequestsController.GetMineById)));
    }

    [Fact]
    public async Task List_returns_authoritative_page()
    {
        var item = new LeaveRequestListItemDto(Guid.NewGuid(), Guid.NewGuid(), "CL", "Casual Leave", new(2026, 9, 1), new(2026, 9, 1), 1m, 1m, LeaveRequestStatus.PendingApproval, DateTime.UtcNow, Guid.NewGuid(), Guid.NewGuid());
        var service = new RecordingReadService(Result<PagedResult<LeaveRequestListItemDto>>.Success(new([item], 1, 25, 1)));
        var action = await Controller(service).GetMine(1, 25, CancellationToken.None);
        var envelope = Assert.IsType<ApiResponse<PagedResult<LeaveRequestListItemDto>>>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Single(envelope.Data!.Items);
        Assert.Equal(item.RequestId, envelope.Data.Items[0].RequestId);
    }

    [Fact]
    public async Task Detail_not_found_uses_shared_mapping()
    {
        var action = await Controller(new RecordingReadService(Result<LeaveRequestDetailDto>.NotFound("Leave request was not found."))).GetMineById(Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(404, Assert.IsType<ObjectResult>(action.Result).StatusCode);
    }

    private static LeaveRequestsController Controller(RecordingReadService service) => new(new NoOpValidationService(), null, service);

    private sealed class RecordingReadService(Result<PagedResult<LeaveRequestListItemDto>> listResult) : ILeaveRequestReadService
    {
        private readonly Result<LeaveRequestDetailDto> _detailResult = Result<LeaveRequestDetailDto>.NotFound("Leave request was not found.");
        public RecordingReadService(Result<LeaveRequestDetailDto> detailResult) : this(Result<PagedResult<LeaveRequestListItemDto>>.Success(PagedResult<LeaveRequestListItemDto>.Empty(1, 25))) => _detailResult = detailResult;
        public Task<Result<PagedResult<LeaveRequestListItemDto>>> GetMineAsync(int page, int pageSize, CancellationToken cancellationToken = default) => Task.FromResult(listResult);
        public Task<Result<LeaveRequestDetailDto>> GetMineByIdAsync(Guid requestId, CancellationToken cancellationToken = default) => Task.FromResult(_detailResult);
    }

    private sealed class NoOpValidationService : ILeaveRequestValidationService
    {
        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(LeaveRequestValidationInput input, CancellationToken cancellationToken = default) => Task.FromResult(Result<LeaveRequestValidationResult>.Invalid("test", "not used"));
    }
}
