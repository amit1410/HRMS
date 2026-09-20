using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll")]
public sealed class PayrollOutputsController(IPayrollOutputService service) : ControllerBase
{
    [HttpPost("runs/{runId:guid}/payslips/generate"), HasPermission(Permissions.Payroll.PayslipGenerate)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayslipDto>>>> Generate(Guid runId, CancellationToken ct) => (await service.GenerateAsync(runId, ct)).ToActionResult();

    [HttpPost("runs/{runId:guid}/payslips/publish"), HasPermission(Permissions.Payroll.PayslipPublish)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayslipDto>>>> Publish(Guid runId, CancellationToken ct) => (await service.PublishAsync(runId, ct)).ToActionResult();

    [HttpGet("runs/{runId:guid}/payslips"), HasPermission(Permissions.Payroll.PayslipViewAll)]
    public async Task<ActionResult<ApiResponse<PagedResult<PayslipDto>>>> GetRunPayslips(Guid runId, [FromQuery] PayrollOutputQuery query, CancellationToken ct) => (await service.GetRunPayslipsAsync(runId, query, ct)).ToActionResult();

    [HttpGet("payslips/{id:guid}"), HasPermission(Permissions.Payroll.PayslipViewAll)]
    public async Task<ActionResult<ApiResponse<PayslipDto>>> Get(Guid id, CancellationToken ct) => (await service.GetAsync(id, false, ct)).ToActionResult();

    [HttpGet("payslips/{id:guid}/document"), HasPermission(Permissions.Payroll.PayslipViewAll)]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct)
    {
        var result = await service.GetDocumentAsync(id, false, ct); if (!result.Succeeded) return result.ToErrorResult(); return Content(result.Value!, "text/html; charset=utf-8");
    }

    [HttpGet("runs/{runId:guid}/register"), HasPermission(Permissions.Payroll.RegisterView)]
    public async Task<ActionResult<ApiResponse<PagedResult<PayrollRegisterRowDto>>>> Register(Guid runId, [FromQuery] PayrollOutputQuery query, CancellationToken ct) => (await service.GetRegisterAsync(runId, query, ct)).ToActionResult();

    [HttpGet("runs/{runId:guid}/register/export"), HasPermission(Permissions.Payroll.RegisterExport)]
    public async Task<IActionResult> Export(Guid runId, [FromQuery] PayrollOutputQuery query, CancellationToken ct)
    {
        var result = await service.ExportRegisterAsync(runId, query, ct); if (!result.Succeeded) return result.ToErrorResult(); return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }
}

[ApiController, Route("api/me/payslips")]
public sealed class MyPayslipsController(IPayrollOutputService service) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Payroll.PayslipViewOwn)]
    public async Task<ActionResult<ApiResponse<PagedResult<PayslipDto>>>> Get([FromQuery] PayrollOutputQuery query, CancellationToken ct) => (await service.GetOwnAsync(query, ct)).ToActionResult();

    [HttpGet("{id:guid}"), HasPermission(Permissions.Payroll.PayslipViewOwn)]
    public async Task<ActionResult<ApiResponse<PayslipDto>>> GetById(Guid id, CancellationToken ct) => (await service.GetAsync(id, true, ct)).ToActionResult();

    [HttpGet("{id:guid}/document"), HasPermission(Permissions.Payroll.PayslipViewOwn)]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct)
    {
        var result = await service.GetDocumentAsync(id, true, ct); if (!result.Succeeded) return result.ToErrorResult(); return Content(result.Value!, "text/html; charset=utf-8");
    }
}

[ApiController, Route("api/payroll/accounting")]
public sealed class PayrollAccountingController(IPayrollAccountingService service) : ControllerBase
{
    [HttpGet("accounts"), HasPermission(Permissions.Payroll.AccountingView)]
    public async Task<ActionResult<ApiResponse<PagedResult<PayrollGLAccountDto>>>> Accounts([FromQuery] PagedQuery query, CancellationToken ct) => (await service.ListAccountsAsync(query, ct)).ToActionResult();
    [HttpPost("accounts"), HasPermission(Permissions.Payroll.AccountingManageConfiguration)]
    public async Task<ActionResult<ApiResponse<PayrollGLAccountDto>>> CreateAccount(PayrollGLAccountRequest request, CancellationToken ct) => (await service.CreateAccountAsync(request, ct)).ToActionResult();
    [HttpPut("accounts/{id:guid}"), HasPermission(Permissions.Payroll.AccountingManageConfiguration)]
    public async Task<ActionResult<ApiResponse<PayrollGLAccountDto>>> UpdateAccount(Guid id, PayrollGLAccountRequest request, CancellationToken ct) => (await service.UpdateAccountAsync(id, request, ct)).ToActionResult();
    [HttpGet("configurations"), HasPermission(Permissions.Payroll.AccountingView)]
    public async Task<ActionResult<ApiResponse<PagedResult<PayrollAccountingConfigurationDto>>>> Configurations([FromQuery] PagedQuery query, CancellationToken ct) => (await service.ListConfigurationsAsync(query, ct)).ToActionResult();
    [HttpPost("configurations"), HasPermission(Permissions.Payroll.AccountingManageConfiguration)]
    public async Task<ActionResult<ApiResponse<PayrollAccountingConfigurationDto>>> CreateConfiguration(PayrollAccountingConfigurationRequest request, CancellationToken ct) => (await service.CreateConfigurationAsync(request, ct)).ToActionResult();
    [HttpPost("configurations/{id:guid}/versions"), HasPermission(Permissions.Payroll.AccountingManageConfiguration)]
    public async Task<ActionResult<ApiResponse<PayrollAccountingConfigurationVersionDto>>> CreateVersion(Guid id, PayrollAccountingConfigurationVersionRequest request, CancellationToken ct) => (await service.CreateVersionAsync(id, request, ct)).ToActionResult();
    [HttpPost("versions/{id:guid}/mappings"), HasPermission(Permissions.Payroll.AccountingManageConfiguration)]
    public async Task<ActionResult<ApiResponse<PayrollGLMappingDto>>> CreateMapping(Guid id, PayrollGLMappingRequest request, CancellationToken ct) => (await service.CreateMappingAsync(id, request, ct)).ToActionResult();
    [HttpPut("mappings/{id:guid}"), HasPermission(Permissions.Payroll.AccountingManageConfiguration)]
    public async Task<ActionResult<ApiResponse<PayrollGLMappingDto>>> UpdateMapping(Guid id, PayrollGLMappingRequest request, CancellationToken ct) => (await service.UpdateMappingAsync(id, request, ct)).ToActionResult();
    [HttpDelete("mappings/{id:guid}"), HasPermission(Permissions.Payroll.AccountingManageConfiguration)]
    public async Task<ActionResult<ApiResponse<bool>>> DeactivateMapping(Guid id, CancellationToken ct) => (await service.DeactivateMappingAsync(id, ct)).ToActionResult();
    [HttpPost("/api/payroll/runs/{runId:guid}/accounting/generate"), HasPermission(Permissions.Payroll.AccountingGenerate)]
    public async Task<ActionResult<ApiResponse<PayrollJournalDto>>> Generate(Guid runId, CancellationToken ct) => (await service.GenerateAsync(runId, ct)).ToActionResult();
    [HttpGet, HasPermission(Permissions.Payroll.AccountingView)]
    public async Task<ActionResult<ApiResponse<PagedResult<PayrollJournalDto>>>> List([FromQuery] PagedQuery query, CancellationToken ct) => (await service.ListAsync(query, ct)).ToActionResult();
    [HttpGet("{id:guid}"), HasPermission(Permissions.Payroll.AccountingView)]
    public async Task<ActionResult<ApiResponse<PayrollJournalDto>>> Get(Guid id, CancellationToken ct) => (await service.GetAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/validate"), HasPermission(Permissions.Payroll.AccountingValidate)]
    public async Task<ActionResult<ApiResponse<PayrollJournalDto>>> Validate(Guid id, CancellationToken ct) => (await service.ValidateAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/approve"), HasPermission(Permissions.Payroll.AccountingApprove)]
    public async Task<ActionResult<ApiResponse<PayrollJournalDto>>> Approve(Guid id, CancellationToken ct) => (await service.ApproveAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/post"), HasPermission(Permissions.Payroll.AccountingPost)]
    public async Task<ActionResult<ApiResponse<PayrollJournalDto>>> Post(Guid id, CancellationToken ct) => (await service.PostAsync(id, ct)).ToActionResult();
    [HttpGet("{id:guid}/export"), HasPermission(Permissions.Payroll.AccountingExport)]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct) { var result = await service.ExportAsync(id, ct); if (!result.Succeeded) return result.ToErrorResult(); return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName); }
}

[ApiController, Route("api/payroll/bank-advice")]
public sealed class BankAdviceController(IBankAdviceService service) : ControllerBase
{
    [HttpPost("/api/payroll/runs/{runId:guid}/bank-advice"), HasPermission(Permissions.Payroll.BankAdviceGenerate)]
    public async Task<ActionResult<ApiResponse<BankAdviceBatchDto>>> Generate(Guid runId, CancellationToken ct) => (await service.GenerateAsync(runId, ct)).ToActionResult();

    [HttpGet, HasPermission(Permissions.Payroll.BankAdviceView)]
    public async Task<ActionResult<ApiResponse<PagedResult<BankAdviceBatchDto>>>> List([FromQuery] BankAdviceQuery query, CancellationToken ct) => (await service.GetAsync(query, ct)).ToActionResult();

    [HttpGet("{id:guid}"), HasPermission(Permissions.Payroll.BankAdviceView)]
    public async Task<ActionResult<ApiResponse<BankAdviceBatchDto>>> Get(Guid id, CancellationToken ct) => (await service.GetByIdAsync(id, ct)).ToActionResult();

    [HttpPost("{id:guid}/validate"), HasPermission(Permissions.Payroll.BankAdviceValidate)]
    public async Task<ActionResult<ApiResponse<BankAdviceBatchDto>>> Validate(Guid id, CancellationToken ct) => (await service.ValidateAsync(id, ct)).ToActionResult();

    [HttpPost("{id:guid}/prepare"), HasPermission(Permissions.Payroll.BankAdviceValidate)]
    public async Task<ActionResult<ApiResponse<BankAdviceBatchDto>>> Prepare(Guid id, CancellationToken ct) => (await service.PrepareAsync(id, ct)).ToActionResult();

    [HttpPost("{id:guid}/approve"), HasPermission(Permissions.Payroll.BankAdviceApprove)]
    public async Task<ActionResult<ApiResponse<BankAdviceBatchDto>>> Approve(Guid id, CancellationToken ct) => (await service.ApproveAsync(id, ct)).ToActionResult();

    [HttpGet("{id:guid}/export"), HasPermission(Permissions.Payroll.BankAdviceExport)]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var result = await service.ExportAsync(id, ct); if (!result.Succeeded) return result.ToErrorResult(); return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost("{id:guid}/cancel"), HasPermission(Permissions.Payroll.BankAdviceCancel)]
    public async Task<ActionResult<ApiResponse<BankAdviceBatchDto>>> Cancel(Guid id, CancellationToken ct) => (await service.CancelAsync(id, ct)).ToActionResult();
}
