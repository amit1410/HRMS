using System.Security.Claims;
using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.Security;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

/// <summary>
/// Sub-resource endpoints for an employee: contact details, addresses, family, education,
/// previous employment, bank details, supervisor hierarchy, additional info, employment
/// history, and audit log.
/// <para>
/// All endpoints are scoped under /api/employees/{id} and require the same Employee.View or
/// Employee.Edit permission that the parent resource uses. No separate permission per sub-entity
/// — access to an employee's data is all-or-nothing at the resource boundary.
/// </para>
/// </summary>
[ApiController]
[Route("api/employees/{id:guid}")]
[Produces("application/json")]
public class EmployeeSubResourcesController : ControllerBase
{
    private readonly IEmployeeContactService _contactService;
    private readonly IEmployeeAddressService _addressService;
    private readonly IEmployeeFamilyService _familyService;
    private readonly IEmployeeEducationService _educationService;
    private readonly IEmployeePreviousEmploymentService _previousEmploymentService;
    private readonly IEmployeeBankDetailService _bankDetailService;
    private readonly IEmployeeSupervisorService _supervisorService;
    private readonly IEmployeeAdditionalInfoService _additionalInfoService;
    private readonly IEmployeeEmploymentService _employmentService;
    private readonly IEmployeeAuditService _auditService;
    private readonly IEmployeeDocumentService _documentService;

    public EmployeeSubResourcesController(
        IEmployeeContactService contactService,
        IEmployeeAddressService addressService,
        IEmployeeFamilyService familyService,
        IEmployeeEducationService educationService,
        IEmployeePreviousEmploymentService previousEmploymentService,
        IEmployeeBankDetailService bankDetailService,
        IEmployeeSupervisorService supervisorService,
        IEmployeeAdditionalInfoService additionalInfoService,
        IEmployeeEmploymentService employmentService,
        IEmployeeAuditService auditService,
        IEmployeeDocumentService documentService)
    {
        _contactService = contactService;
        _addressService = addressService;
        _familyService = familyService;
        _educationService = educationService;
        _previousEmploymentService = previousEmploymentService;
        _bankDetailService = bankDetailService;
        _supervisorService = supervisorService;
        _additionalInfoService = additionalInfoService;
        _employmentService = employmentService;
        _auditService = auditService;
        _documentService = documentService;
    }

    private string ChangedBy => User.FindFirstValue(HrmsClaimTypes.Email) ?? "unknown";

    private Task LogAuditAsync(
        Guid employeeId, string module, string? section, string? entityName,
        Guid? recordId, string? fieldName, string? oldValue, string? newValue,
        AuditChangeType changeType, CancellationToken ct)
    {
        return _auditService.LogChangeAsync(
            employeeId, null!, module, section, entityName, recordId,
            fieldName, oldValue, newValue, changeType, ChangedBy,
            cancellationToken: ct);
    }

    // ── Contact ──────────────────────────────────────────────────────────

    /// <summary>Returns the contact details for the specified employee.</summary>
    [HttpGet("contact")]
    [HasPermission(Permissions.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeContactDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeContactDto>>> GetContact(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _contactService.GetAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Creates or updates the contact details for the specified employee (upsert).</summary>
    [HttpPut("contact")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeContactDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeContactDto>>> UpsertContact(
        Guid id, [FromBody] EmployeeContactRequest request, CancellationToken cancellationToken)
    {
        var result = await _contactService.UpsertAsync(id, request, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "Contact", null, "EmployeeContact", null, null, null, result.Value?.OfficialEmail, AuditChangeType.Update, cancellationToken);
        return result.ToActionResult();
    }

    // ── Addresses ────────────────────────────────────────────────────────

    /// <summary>Returns all structured addresses for the specified employee.</summary>
    [HttpGet("addresses")]
    [HasPermission(Permissions.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EmployeeAddressDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeAddressDto>>>> GetAddresses(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _addressService.GetAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Creates or updates a structured address for the specified employee.</summary>
    [HttpPost("addresses")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeAddressDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeAddressDto>>> UpsertAddress(
        Guid id, [FromBody] EmployeeAddressRequest request, CancellationToken cancellationToken)
    {
        var result = await _addressService.UpsertAsync(id, request, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "Address", null, "EmployeeAddress", result.Value?.Id, null, null, result.Value?.AddressType.ToString(), AuditChangeType.Update, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Deletes a structured address from the specified employee.</summary>
    [HttpDelete("addresses/{addressId:guid}")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteAddress(
        Guid id, Guid addressId, CancellationToken cancellationToken)
    {
        var result = await _addressService.DeleteAsync(id, addressId, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "Address", null, "EmployeeAddress", addressId, null, null, null, AuditChangeType.Delete, cancellationToken);
        return result.ToActionResult();
    }

    // ── Family ───────────────────────────────────────────────────────────

    /// <summary>Returns all family members for the specified employee.</summary>
    [HttpGet("family")]
    [HasPermission(Permissions.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EmployeeFamilyDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeFamilyDto>>>> GetFamily(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _familyService.GetAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Adds a family member to the specified employee.</summary>
    [HttpPost("family")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeFamilyDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeFamilyDto>>> CreateFamily(
        Guid id, [FromBody] EmployeeFamilyRequest request, CancellationToken cancellationToken)
    {
        var result = await _familyService.CreateAsync(id, request, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "Family", null, "EmployeeFamily", result.Value?.Id, null, null, $"{result.Value?.FirstName} {result.Value?.LastName}", AuditChangeType.Create, cancellationToken);
        return result.ToCreatedResult(nameof(GetFamily), _ => new { id });
    }

    /// <summary>Updates a family member for the specified employee.</summary>
    [HttpPut("family/{familyId:guid}")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeFamilyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeFamilyDto>>> UpdateFamily(
        Guid id, Guid familyId, [FromBody] EmployeeFamilyRequest request, CancellationToken cancellationToken)
    {
        var result = await _familyService.UpdateAsync(id, familyId, request, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "Family", null, "EmployeeFamily", familyId, null, null, $"{result.Value?.FirstName} {result.Value?.LastName}", AuditChangeType.Update, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Removes a family member from the specified employee.</summary>
    [HttpDelete("family/{familyId:guid}")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteFamily(
        Guid id, Guid familyId, CancellationToken cancellationToken)
    {
        var result = await _familyService.DeleteAsync(id, familyId, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "Family", null, "EmployeeFamily", familyId, null, null, null, AuditChangeType.Delete, cancellationToken);
        return result.ToActionResult();
    }

    // ── Education ────────────────────────────────────────────────────────

    /// <summary>Returns all education records for the specified employee.</summary>
    [HttpGet("education")]
    [HasPermission(Permissions.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EmployeeEducationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeEducationDto>>>> GetEducation(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _educationService.GetAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Adds an education record to the specified employee.</summary>
    [HttpPost("education")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeEducationDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeEducationDto>>> CreateEducation(
        Guid id, [FromBody] EmployeeEducationRequest request, CancellationToken cancellationToken)
    {
        var result = await _educationService.CreateAsync(id, request, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "Education", null, "EmployeeEducation", result.Value?.Id, null, null, result.Value?.Qualification, AuditChangeType.Create, cancellationToken);
        return result.ToCreatedResult(nameof(GetEducation), _ => new { id });
    }

    /// <summary>Updates an education record for the specified employee.</summary>
    [HttpPut("education/{educationId:guid}")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeEducationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeEducationDto>>> UpdateEducation(
        Guid id, Guid educationId, [FromBody] EmployeeEducationRequest request, CancellationToken cancellationToken)
    {
        var result = await _educationService.UpdateAsync(id, educationId, request, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "Education", null, "EmployeeEducation", educationId, null, null, result.Value?.Qualification, AuditChangeType.Update, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Removes an education record from the specified employee.</summary>
    [HttpDelete("education/{educationId:guid}")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteEducation(
        Guid id, Guid educationId, CancellationToken cancellationToken)
    {
        var result = await _educationService.DeleteAsync(id, educationId, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "Education", null, "EmployeeEducation", educationId, null, null, null, AuditChangeType.Delete, cancellationToken);
        return result.ToActionResult();
    }

    // ── Previous Employment ──────────────────────────────────────────────

    /// <summary>Returns all previous employment records for the specified employee.</summary>
    [HttpGet("previous-employment")]
    [HasPermission(Permissions.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EmployeePreviousEmploymentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeePreviousEmploymentDto>>>> GetPreviousEmployment(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _previousEmploymentService.GetAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Adds a previous employment record to the specified employee.</summary>
    [HttpPost("previous-employment")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeePreviousEmploymentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeePreviousEmploymentDto>>> CreatePreviousEmployment(
        Guid id, [FromBody] EmployeePreviousEmploymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _previousEmploymentService.CreateAsync(id, request, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "PreviousEmployment", null, "EmployeePreviousEmployment", result.Value?.Id, null, null, result.Value?.Company, AuditChangeType.Create, cancellationToken);
        return result.ToCreatedResult(nameof(GetPreviousEmployment), _ => new { id });
    }

    /// <summary>Updates a previous employment record for the specified employee.</summary>
    [HttpPut("previous-employment/{previousEmploymentId:guid}")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeePreviousEmploymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeePreviousEmploymentDto>>> UpdatePreviousEmployment(
        Guid id, Guid previousEmploymentId, [FromBody] EmployeePreviousEmploymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _previousEmploymentService.UpdateAsync(id, previousEmploymentId, request, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "PreviousEmployment", null, "EmployeePreviousEmployment", previousEmploymentId, null, null, result.Value?.Company, AuditChangeType.Update, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Removes a previous employment record from the specified employee.</summary>
    [HttpDelete("previous-employment/{previousEmploymentId:guid}")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<bool>>> DeletePreviousEmployment(
        Guid id, Guid previousEmploymentId, CancellationToken cancellationToken)
    {
        var result = await _previousEmploymentService.DeleteAsync(id, previousEmploymentId, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "PreviousEmployment", null, "EmployeePreviousEmployment", previousEmploymentId, null, null, null, AuditChangeType.Delete, cancellationToken);
        return result.ToActionResult();
    }

    // ── Bank Details ─────────────────────────────────────────────────────

    /// <summary>Returns all bank details for the specified employee.</summary>
    [HttpGet("bank-details")]
    [HasPermission(Permissions.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EmployeeBankDetailDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeBankDetailDto>>>> GetBankDetails(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _bankDetailService.GetAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Returns full bank values for an authorized edit screen.</summary>
    [HttpGet("bank-details/{bankDetailId:guid}/sensitive-details")]
    [HasPermission(Permissions.Employee.View)]
    [HasPermission(Permissions.EmployeeSensitive.View)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeBankDetailEditDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeBankDetailEditDto>>> GetBankDetailForEdit(
        Guid id, Guid bankDetailId, CancellationToken cancellationToken)
    {
        var result = await _bankDetailService.GetForEditAsync(id, bankDetailId, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Adds a bank detail to the specified employee.</summary>
    [HttpPost("bank-details")]
    [HasPermission(Permissions.Employee.Edit)]
    [HasPermission(Permissions.EmployeeSensitive.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeBankDetailDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeBankDetailDto>>> CreateBankDetail(
        Guid id, [FromBody] EmployeeBankDetailRequest request, CancellationToken cancellationToken)
    {
        var result = await _bankDetailService.CreateAsync(id, request, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "BankDetail", null, "EmployeeBankDetail", result.Value?.Id, null, null, result.Value?.BankName, AuditChangeType.Create, cancellationToken);
        return result.ToCreatedResult(nameof(GetBankDetails), _ => new { id });
    }

    /// <summary>Updates a bank detail for the specified employee.</summary>
    [HttpPut("bank-details/{bankDetailId:guid}")]
    [HasPermission(Permissions.Employee.Edit)]
    [HasPermission(Permissions.EmployeeSensitive.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeBankDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeBankDetailDto>>> UpdateBankDetail(
        Guid id, Guid bankDetailId, [FromBody] EmployeeBankDetailRequest request, CancellationToken cancellationToken)
    {
        var result = await _bankDetailService.UpdateAsync(id, bankDetailId, request, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "BankDetail", null, "EmployeeBankDetail", bankDetailId, null, null, result.Value?.BankName, AuditChangeType.Update, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Removes a bank detail from the specified employee.</summary>
    [HttpDelete("bank-details/{bankDetailId:guid}")]
    [HasPermission(Permissions.Employee.Edit)]
    [HasPermission(Permissions.EmployeeSensitive.Edit)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteBankDetail(
        Guid id, Guid bankDetailId, CancellationToken cancellationToken)
    {
        var result = await _bankDetailService.DeleteAsync(id, bankDetailId, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "BankDetail", null, "EmployeeBankDetail", bankDetailId, null, null, null, AuditChangeType.Delete, cancellationToken);
        return result.ToActionResult();
    }

    // ── Supervisor Hierarchy ─────────────────────────────────────────────

    /// <summary>Returns the supervisor hierarchy for the specified employee.</summary>
    [HttpGet("supervisor")]
    [HasPermission(Permissions.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeSupervisorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeSupervisorDto>>> GetSupervisor(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _supervisorService.GetAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Creates or updates the supervisor hierarchy for the specified employee (upsert).</summary>
    [HttpPut("supervisor")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeSupervisorDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeSupervisorDto>>> UpsertSupervisor(
        Guid id, [FromBody] EmployeeSupervisorRequest request, CancellationToken cancellationToken)
    {
        var result = await _supervisorService.UpsertAsync(id, request, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "Supervisor", null, "EmployeeSupervisor", null, "l1ManagerCode", null, result.Value?.L1ManagerCode, AuditChangeType.Update, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Returns eligible supervisor options for the specified employee and supervisor type.</summary>
    /// <param name="id">Employee ID.</param>
    /// <param name="type">Supervisor type (L1, L2, L3, Other, HR, Time).</param>
    [HttpGet("supervisor-options")]
    [HasPermission(Permissions.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SupervisorOptionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SupervisorOptionDto>>>> GetSupervisorOptions(
        Guid id, [FromQuery] string type, CancellationToken cancellationToken)
    {
        var result = await _supervisorService.GetSupervisorOptionsAsync(id, type, cancellationToken);
        return result.ToActionResult();
    }

    // ── Additional Info ──────────────────────────────────────────────────

    /// <summary>Returns the additional info for the specified employee.</summary>
    [HttpGet("additional-info")]
    [HasPermission(Permissions.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeAdditionalInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeAdditionalInfoDto>>> GetAdditionalInfo(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _additionalInfoService.GetAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Creates or updates the additional info for the specified employee (upsert).</summary>
    [HttpPut("additional-info")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeAdditionalInfoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeAdditionalInfoDto>>> UpsertAdditionalInfo(
        Guid id, [FromBody] EmployeeAdditionalInfoRequest request, CancellationToken cancellationToken)
    {
        var result = await _additionalInfoService.UpsertAsync(id, request, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "AdditionalInfo", null, "EmployeeAdditionalInfo", null, "division", null, result.Value?.Division, AuditChangeType.Update, cancellationToken);
        return result.ToActionResult();
    }

    // ── Employment (Joining Information) ─────────────────────────────────────

    /// <summary>Returns the joining information and contractual terms for the specified employee.</summary>
    [HttpGet("employment")]
    [HasPermission(Permissions.EmploymentHistory.View)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeEmploymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeEmploymentDto>>> GetEmployment(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _employmentService.GetEmploymentAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Creates or updates the joining information for the specified employee.</summary>
    [HttpPut("employment")]
    [HasPermission(Permissions.EmploymentHistory.Change)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeEmploymentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeEmploymentDto>>> UpsertEmployment(
        Guid id, [FromBody] EmployeeEmploymentRequest request, CancellationToken cancellationToken)
    {
        var result = await _employmentService.UpsertEmploymentAsync(id, request, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "Employment", null, "EmployeeEmployment", null, "firstHiredDate", null, result.Value?.FirstHiredDate.ToString("yyyy-MM-dd"), AuditChangeType.Update, cancellationToken);
        return result.ToActionResult();
    }

    // ── Employment History ───────────────────────────────────────────────

    /// <summary>Returns the full employment history for the specified employee, most recent first.</summary>
    [HttpGet("employment-history")]
    [HasPermission(Permissions.EmploymentHistory.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EmployeeEmploymentHistoryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeEmploymentHistoryDto>>>> GetEmploymentHistory(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _employmentService.GetHistoryAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Returns the current (active) employment record for the specified employee.</summary>
    [HttpGet("employment-history/current")]
    [HasPermission(Permissions.EmploymentHistory.View)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeEmploymentHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeEmploymentHistoryDto>>> GetCurrentEmployment(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _employmentService.GetCurrentAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>
    /// Records an employment change. Closes the current record and creates a new effective-dated
    /// record starting on the specified date. Previous employment state is preserved as history.
    /// </summary>
    [HttpPost("employment-history")]
    [HasPermission(Permissions.EmploymentHistory.Change)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeEmploymentHistoryDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<EmployeeEmploymentHistoryDto>>> CreateEmploymentChange(
        Guid id, [FromBody] EmploymentChangeRequest request, CancellationToken cancellationToken)
    {
        var result = await _employmentService.CreateChangeAsync(id, request, ChangedBy, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "EmploymentHistory", null, "EmployeeEmploymentHistory", result.Value?.Id, "departmentId", null, result.Value?.DepartmentId?.ToString(), AuditChangeType.Create, cancellationToken);
        return result.ToCreatedResult(nameof(GetEmploymentHistory), _ => new { id });
    }

    // ── Documents ────────────────────────────────────────────────────────

    /// <summary>Returns all documents for the specified employee.</summary>
    [HttpGet("documents")]
    [HasPermission(Permissions.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EmployeeDocumentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EmployeeDocumentDto>>>> GetDocuments(
        Guid id, CancellationToken cancellationToken)
    {
        var result = await _documentService.GetAsync(id, cancellationToken);
        return result.ToActionResult();
    }

    /// <summary>Uploads a document for the specified employee.</summary>
    [HttpPost("documents")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDocumentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<EmployeeDocumentDto>>> UploadDocument(
        Guid id, [FromForm] EmployeeDocumentRequest request, CancellationToken cancellationToken)
    {
        var result = await _documentService.UploadAsync(id, request, request.FilePath, request.FileSize, request.ContentType, ChangedBy, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "Documents", null, "EmployeeDocument", result.Value?.Id, "documentName", null, result.Value?.DocumentName, AuditChangeType.DocumentUpload, cancellationToken);
        return result.ToCreatedResult(nameof(GetDocuments), _ => new { id });
    }

    /// <summary>Deletes a document from the specified employee.</summary>
    [HttpDelete("documents/{documentId:guid}")]
    [HasPermission(Permissions.Employee.Edit)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteDocument(
        Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        var result = await _documentService.DeleteAsync(id, documentId, cancellationToken);
        if (result.Succeeded)
            await LogAuditAsync(id, "Documents", null, "EmployeeDocument", documentId, null, null, null, AuditChangeType.DocumentDelete, cancellationToken);
        return result.ToActionResult();
    }

    // ── Audit Log ────────────────────────────────────────────────────────

    /// <summary>Returns the audit trail for the specified employee, filtered and paged.</summary>
    [HttpGet("audit-log")]
    [HasPermission(Permissions.Employee.View)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<EmployeeAuditLogDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<EmployeeAuditLogDto>>>> GetAuditLog(
        Guid id, [FromQuery] AuditQuery query, CancellationToken cancellationToken)
    {
        var result = await _auditService.GetAsync(id, query, cancellationToken);
        return result.ToActionResult();
    }
}
