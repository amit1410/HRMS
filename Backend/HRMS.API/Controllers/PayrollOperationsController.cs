using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll")]
public sealed class PayrollOperationsController(IPayrollOperationsService service) : ControllerBase
{
    [HttpGet("configuration-health"), HasPermission(Permissions.Payroll.ControlsView)]
    public async Task<ActionResult<ApiResponse<PayrollConfigurationHealthDto>>> ConfigurationHealth(CancellationToken ct) => (await service.GetConfigurationHealthAsync(ct)).ToActionResult();

    [HttpGet("dashboard/operations"), HasPermission(Permissions.Payroll.RunView)]
    public async Task<ActionResult<ApiResponse<PayrollOperationsDashboardDto>>> OperationsDashboard(CancellationToken ct) => (await service.GetOperationsDashboardAsync(ct)).ToActionResult();

    [HttpGet("production-health"), HasPermission(Permissions.Payroll.ControlsView)]
    public async Task<ActionResult<ApiResponse<PayrollProductionHealthDto>>> ProductionHealth(CancellationToken ct) => (await service.GetProductionHealthAsync(ct)).ToActionResult();

    [HttpGet("integrity"), HasPermission(Permissions.Payroll.ControlsView)]
    public async Task<ActionResult<ApiResponse<PayrollIntegrityDto>>> Integrity(CancellationToken ct) => (await service.GetIntegrityAsync(ct)).ToActionResult();
}
