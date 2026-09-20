using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll/controls")]
public sealed class PayrollControlsController(IPayrollControlService service) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Payroll.ControlsView)] public async Task<ActionResult<ApiResponse<PayrollControlConfigurationDto>>> Get(CancellationToken ct) => (await service.GetAsync(ct)).ToActionResult();
    [HttpPut, HasPermission(Permissions.Payroll.ControlsManage)] public async Task<ActionResult<ApiResponse<PayrollControlConfigurationDto>>> Update(PayrollControlConfigurationRequest request, CancellationToken ct) => (await service.UpdateAsync(request, ct)).ToActionResult();
}
