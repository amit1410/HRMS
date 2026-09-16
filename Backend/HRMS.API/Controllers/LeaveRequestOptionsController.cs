using HRMS.API.Extensions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.API.Security;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

/// <summary>Self-service lookup data required to create a leave request.</summary>
[ApiController, Route("api/leave-types"), Produces("application/json"), Authorize]
public sealed class LeaveRequestOptionsController(
    IEmployeeIdentityResolver identityResolver,
    ILeaveConfigurationService leaveConfiguration) : ControllerBase
{
    [HttpGet("available"), HasPermission(Permissions.Leave.TypeViewAvailable)]
    public async Task<ActionResult<ApiResponse<PagedResult<LeaveTypeDto>>>> GetLeaveTypes(
        CancellationToken cancellationToken)
    {
        var identity = await identityResolver.ResolveCurrentAsync(cancellationToken);
        if (!identity.Succeeded)
        {
            return Result<PagedResult<LeaveTypeDto>>.Failure(
                identity.Status, identity.Message, identity.Errors).ToActionResult();
        }

        return (await leaveConfiguration.GetLeaveTypesAsync(
            new LeaveTypeQuery { Page = 1, PageSize = 100, IsActive = true },
            cancellationToken)).ToActionResult();
    }
}
