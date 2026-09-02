using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.EmployeeCodes;
using HRMS.Application.Validators.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace HRMS.Application.Services;

/// <summary>
/// Employment details (joining information) and effective-dated position history.
/// Every position change creates a new record rather than overwriting the previous one.
/// The current position is derived from the record where <see cref="EmployeeEmploymentHistory.EffectiveTo"/> is null.
/// </summary>
public class EmployeeEmploymentService : IEmployeeEmploymentService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Employee not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EmployeeEmploymentService> _logger;
    private readonly EmployeeCodeRuleMatcher? _codeRuleMatcher;
    private readonly EmployeeCodeRenderer? _codeRenderer;
    private readonly IEmployeeCodeSequenceService? _codeSequence;

    public EmployeeEmploymentService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<EmployeeEmploymentService> logger,
        EmployeeCodeRuleMatcher? codeRuleMatcher = null,
        EmployeeCodeRenderer? codeRenderer = null,
        IEmployeeCodeSequenceService? codeSequence = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
        _codeRuleMatcher = codeRuleMatcher;
        _codeRenderer = codeRenderer;
        _codeSequence = codeSequence;
    }

    // ── Joining Information (EmployeeEmployment — 1:1 with Employee) ──────────

    public async Task<Result<EmployeeEmploymentDto>> GetEmploymentAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
            return Result<EmployeeEmploymentDto>.Unauthorized(NoTenantMessage);

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
            return Result<EmployeeEmploymentDto>.NotFound(NotFoundMessage);

        var emp = await _db.EmployeeEmployments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId, cancellationToken);

        if (emp is null)
            return Result<EmployeeEmploymentDto>.NotFound("No employment record found for this employee.");

        string? referredByName = null;
        if (emp.ReferredByEmployeeId.HasValue)
        {
            var refEmp = await _db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(
                    e => e.Id == emp.ReferredByEmployeeId.Value && e.TenantId == _tenantContext.TenantId,
                    cancellationToken);
            referredByName = refEmp is not null ? $"{refEmp.FirstName} {refEmp.LastName}" : null;
        }

        var dto = new EmployeeEmploymentDto(
            emp.Id, emp.EmployeeId,
            emp.FirstHiredDate, emp.DateOfJoining, emp.GroupDateOfJoining,
            emp.ConfirmationDate, emp.JobStatus,
            emp.ProbationPeriod, emp.ProbationPeriodUnit,
            emp.ReferredByEmployeeId, referredByName,
            emp.NoticePeriod, emp.NoticePeriodUnit,
            emp.CreatedDate, emp.ModifiedDate);

        return Result<EmployeeEmploymentDto>.Success(dto);
    }

    public async Task<Result<EmployeeEmploymentDto>> UpsertEmploymentAsync(
        Guid employeeId, EmployeeEmploymentRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
            return Result<EmployeeEmploymentDto>.Unauthorized(NoTenantMessage);

        var employee = await _db.Employees.FirstOrDefaultAsync(
            e => e.Id == employeeId && e.TenantId == tenantId,
            cancellationToken);
        if (employee is null)
            return Result<EmployeeEmploymentDto>.NotFound(NotFoundMessage);

        var requestValidation = ValidateEmploymentDetailsRequest(request);
        if (requestValidation is not null)
            return requestValidation;

        Employee? referredByEmployee = null;
        if (request.ReferredByEmployeeId is Guid referredById)
        {
            if (referredById == employeeId)
            {
                return Result<EmployeeEmploymentDto>.Invalid(
                    "referredByEmployeeId", "An employee cannot refer themselves.");
            }

            referredByEmployee = await _db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(
                    e => e.Id == referredById && e.TenantId == tenantId && e.Status == EmployeeStatus.Active,
                    cancellationToken);

            if (referredByEmployee is null)
            {
                return Result<EmployeeEmploymentDto>.Invalid(
                    "referredByEmployeeId",
                    "Referring employee does not exist, is inactive, or belongs to another tenant.");
            }
        }

        var existing = await _db.EmployeeEmployments
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId, cancellationToken);

        if (existing is null)
        {
            existing = new EmployeeEmployment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId
            };
            _db.EmployeeEmployments.Add(existing);
        }

        existing.FirstHiredDate = request.FirstHiredDate;
        existing.DateOfJoining = request.DateOfJoining;
        existing.GroupDateOfJoining = request.GroupDateOfJoining;
        existing.ConfirmationDate = request.ConfirmationDate;
        existing.JobStatus = Normalize(request.JobStatus);
        existing.ProbationPeriod = request.ProbationPeriod;
        existing.ProbationPeriodUnit = NormalizePeriodUnit(request.ProbationPeriodUnit);
        existing.ReferredByEmployeeId = request.ReferredByEmployeeId;
        existing.NoticePeriod = request.NoticePeriod;
        existing.NoticePeriodUnit = NormalizePeriodUnit(request.NoticePeriodUnit);
        existing.ModifiedDate = DateTime.UtcNow;

        // Keep Employee.DateOfJoining and Employee.JobStatus in sync
        employee.DateOfJoining = request.DateOfJoining;
        employee.JobStatus = Normalize(request.JobStatus);
        employee.GroupDateOfJoining = request.GroupDateOfJoining;
        employee.ModifiedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var referredByName = referredByEmployee is not null
            ? $"{referredByEmployee.FirstName} {referredByEmployee.LastName}"
            : null;

        var dto = new EmployeeEmploymentDto(
            existing.Id, existing.EmployeeId,
            existing.FirstHiredDate, existing.DateOfJoining, existing.GroupDateOfJoining,
            existing.ConfirmationDate, existing.JobStatus,
            existing.ProbationPeriod, existing.ProbationPeriodUnit,
            existing.ReferredByEmployeeId, referredByName,
            existing.NoticePeriod, existing.NoticePeriodUnit,
            existing.CreatedDate, existing.ModifiedDate);

        _logger.LogInformation("Upserted employment record for employee {EmployeeId} in tenant {TenantId}.", employeeId, tenantId);
        return Result<EmployeeEmploymentDto>.Success(dto, existing.CreatedDate == existing.ModifiedDate ? "Employment record created." : "Employment record updated.");
    }

    // ── Effective-dated Position History ──────────────────────────────────────

    public async Task<Result<IReadOnlyList<EmployeeEmploymentHistoryDto>>> GetHistoryAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
            return Result<IReadOnlyList<EmployeeEmploymentHistoryDto>>.Unauthorized(NoTenantMessage);

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
            return Result<IReadOnlyList<EmployeeEmploymentHistoryDto>>.NotFound(NotFoundMessage);

        var history = await IncludeWithMasters(
                _db.EmployeeEmploymentHistory.AsNoTracking().Where(e => e.EmployeeId == employeeId))
            .OrderByDescending(e => e.EffectiveFrom)
            .ThenByDescending(e => e.CreatedDate)
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<EmployeeEmploymentHistoryDto>>.Success(history.Select(MapToDto).ToList());
    }

    public async Task<Result<EmployeeEmploymentHistoryDto>> GetCurrentAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
            return Result<EmployeeEmploymentHistoryDto>.Unauthorized(NoTenantMessage);

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
            return Result<EmployeeEmploymentHistoryDto>.NotFound(NotFoundMessage);

        var currentRecords = await IncludeWithMasters(
                _db.EmployeeEmploymentHistory.AsNoTracking().Where(e => e.EmployeeId == employeeId && e.EffectiveTo == null))
            .OrderByDescending(e => e.EffectiveFrom)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (currentRecords.Count > 1)
        {
            return Result<EmployeeEmploymentHistoryDto>.Conflict(
                "Employment history contains multiple current records.");
        }

        var current = currentRecords.SingleOrDefault();

        return current is null
            ? Result<EmployeeEmploymentHistoryDto>.NotFound("No current employment record found.")
            : Result<EmployeeEmploymentHistoryDto>.Success(MapToDto(current));
    }

    public async Task<Result<EmployeeEmploymentHistoryDto>> CreateChangeAsync(
        Guid employeeId,
        EmploymentChangeRequest request,
        string changedBy,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
            return Result<EmployeeEmploymentHistoryDto>.Unauthorized(NoTenantMessage);

        var employee = await _db.Employees.FirstOrDefaultAsync(
            e => e.Id == employeeId && e.TenantId == tenantId,
            cancellationToken);
        if (employee is null)
            return Result<EmployeeEmploymentHistoryDto>.NotFound(NotFoundMessage);

        var commandValidation = ValidateEmploymentChangeCommand(request);
        if (commandValidation is not null)
            return commandValidation;

        var referenceValidation = await ValidateEmploymentReferencesAsync(
            employeeId, tenantId, request, cancellationToken);
        if (referenceValidation.Error is not null)
            return referenceValidation.Error;

        var references = referenceValidation.References;

        // ── Validate all master references exist, are active, and belong to the tenant ──

        if (request.DepartmentId.HasValue)
        {
            var valid = await _db.Departments.AnyAsync(
                d => d.Id == request.DepartmentId.Value && d.TenantId == tenantId && d.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("departmentId", "Department does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.DesignationId.HasValue)
        {
            var valid = await _db.Designations.AnyAsync(
                d => d.Id == request.DesignationId.Value && d.TenantId == tenantId && d.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("designationId", "Designation does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.HoldingCompanyId.HasValue)
        {
            var valid = await _db.HoldingCompanies.AnyAsync(
                h => h.Id == request.HoldingCompanyId.Value && h.TenantId == tenantId && h.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("holdingCompanyId", "Holding Company does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.LobId.HasValue)
        {
            var valid = await _db.LinesOfBusiness.AnyAsync(
                l => l.Id == request.LobId.Value && l.TenantId == tenantId && l.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("lobId", "Line of Business does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.OrganisationId.HasValue)
        {
            var valid = await _db.Organisations.AnyAsync(
                o => o.Id == request.OrganisationId.Value && o.TenantId == tenantId && o.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("organisationId", "Organisation does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.SubDepartmentId.HasValue)
        {
            var valid = await _db.SubDepartments.AnyAsync(
                s => s.Id == request.SubDepartmentId.Value && s.TenantId == tenantId && s.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("subDepartmentId", "Sub Department does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.SectionId.HasValue)
        {
            var valid = await _db.Sections.AnyAsync(
                s => s.Id == request.SectionId.Value && s.TenantId == tenantId && s.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("sectionId", "Section does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.SubSectionId.HasValue)
        {
            var valid = await _db.SubSections.AnyAsync(
                s => s.Id == request.SubSectionId.Value && s.TenantId == tenantId && s.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("subSectionId", "Sub Section does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.FunctionId.HasValue)
        {
            var valid = await _db.Functions.AnyAsync(
                f => f.Id == request.FunctionId.Value && f.TenantId == tenantId && f.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("functionId", "Function does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.SubFunctionId.HasValue)
        {
            var valid = await _db.SubFunctions.AnyAsync(
                s => s.Id == request.SubFunctionId.Value && s.TenantId == tenantId && s.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("subFunctionId", "Sub Function does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.GradeId.HasValue)
        {
            var valid = await _db.Grades.AnyAsync(
                g => g.Id == request.GradeId.Value && g.TenantId == tenantId && g.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("gradeId", "Grade does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.EmployeeTypeId.HasValue)
        {
            var valid = await _db.EmployeeTypes.AnyAsync(
                e => e.Id == request.EmployeeTypeId.Value && e.TenantId == tenantId && e.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("employeeTypeId", "Employee Type does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.WorkLocationId.HasValue)
        {
            var valid = await _db.WorkLocations.AnyAsync(
                w => w.Id == request.WorkLocationId.Value && w.TenantId == tenantId && w.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("workLocationId", "Work Location does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.CostCenterId.HasValue)
        {
            var valid = await _db.CostCenters.AnyAsync(
                c => c.Id == request.CostCenterId.Value && c.TenantId == tenantId && c.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("costCenterId", "Cost Center does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.PositionChangeReasonId.HasValue)
        {
            var valid = await _db.PositionChangeReasons.AnyAsync(
                p => p.Id == request.PositionChangeReasonId.Value && p.TenantId == tenantId && p.IsActive, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("positionChangeReasonId", "Position Change Reason does not exist, is inactive, or belongs to another tenant.");
        }

        if (request.ManagerId.HasValue)
        {
            var valid = await _db.Employees.AnyAsync(
                m => m.Id == request.ManagerId.Value && m.TenantId == tenantId, cancellationToken);
            if (!valid)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("managerId", "Manager employee does not exist or belongs to another tenant.");
        }

        // ── Overlap detection ───────────────────────────────────────────────────

        var existingRecords = await _db.EmployeeEmploymentHistory
            .Where(e => e.EmployeeId == employeeId)
            .ToListAsync(cancellationToken);

        if (existingRecords.Count == 0 && request.ChangeReason != EmploymentChangeReason.NewJoining)
        {
            return Result<EmployeeEmploymentHistoryDto>.Invalid(
                "changeReason", "The first employment record must use the New Hire/Initial Position reason.");
        }

        // A new transaction must not fall inside the period of a record that is already closed. A later date
        // than the current open record is legitimate (it closes the open record and opens a new one — handled
        // below), so the open record itself is deliberately not treated as an overlap here.
        foreach (var record in existingRecords.Where(r => r.EffectiveTo is not null))
        {
            bool overlaps = request.EffectiveFrom >= record.EffectiveFrom && request.EffectiveFrom <= record.EffectiveTo;

            if (overlaps)
            {
                return Result<EmployeeEmploymentHistoryDto>.Invalid(
                    "effectiveFrom",
                    $"The effective date overlaps with an existing position record effective from {record.EffectiveFrom:yyyy-MM-dd}.");
            }
        }

        // ── Close the current open record ──────────────────────────────────────

        var openRecords = existingRecords.Where(e => e.EffectiveTo is null).ToList();
        if (openRecords.Count > 1)
        {
            return Result<EmployeeEmploymentHistoryDto>.Conflict(
                "Employment history contains multiple current records and must be repaired before another change can be recorded.");
        }

        var currentRecord = openRecords.SingleOrDefault();

        if (currentRecord is not null)
        {
            if (request.EffectiveFrom <= currentRecord.EffectiveFrom)
            {
                return Result<EmployeeEmploymentHistoryDto>.Invalid(
                    "effectiveFrom",
                    $"The effective date must be after the current record's effective date ({currentRecord.EffectiveFrom:yyyy-MM-dd}).");
            }

            currentRecord.EffectiveTo = request.EffectiveFrom.AddDays(-1);
            currentRecord.ModifiedDate = DateTime.UtcNow;
        }

        // ── Create the new position record ─────────────────────────────────────

        // Assign a pending Employee Code only after every employment validation has passed. This
        // prevents rejected commands from consuming a sequence number or mutating the employee row.
        await using var transaction = await _db.BeginTransactionAsync(cancellationToken);
        if (employee.EmployeeCode is null)
        {
            var codeError = await AssignPendingEmployeeCodeAsync(employee, request, references, cancellationToken);
            if (codeError is not null)
                return codeError;
        }

        var newRecord = new EmployeeEmploymentHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = null,

            // Organizational FK references
            HoldingCompanyId = request.HoldingCompanyId,
            LobId = request.LobId,
            OrganisationId = request.OrganisationId,
            DepartmentId = request.DepartmentId,
            SubDepartmentId = request.SubDepartmentId,
            SectionId = request.SectionId,
            SubSectionId = request.SubSectionId,
            FunctionId = request.FunctionId,
            SubFunctionId = request.SubFunctionId,

            // Job classification
            GradeId = request.GradeId,
            DesignationId = request.DesignationId,
            EmployeeTypeId = request.EmployeeTypeId,

            // Location
            CountryLocationId = request.CountryLocationId,
            WorkLocationId = request.WorkLocationId,

            // Cost center
            CostCenterId = request.CostCenterId,

            // Reporting
            ManagerId = request.ManagerId,

            // Change metadata
            PositionChangeReasonId = request.PositionChangeReasonId,
            ChangeReason = request.ChangeReason,
            ChangeReasonDescription = Normalize(request.ChangeReasonDescription),

            // Snapshot fields
            BusinessRole = Normalize(request.BusinessRole),
            GradeLevel = Normalize(request.GradeLevel) ?? references.Grade?.Code,
            CareerGroup = Normalize(request.CareerGroup),
            EmploymentType = request.EmploymentType,
            EmploymentStatus = request.EmploymentStatus,
            DepartmentName = references.Department?.Name,
            DesignationName = references.Designation?.Name,
            ManagerCode = references.Manager?.EmployeeCode,
            ManagerName = references.Manager is null ? null : EmployeeFullName(references.Manager),
            CreatedBy = Normalize(changedBy)
        };

        _db.EmployeeEmploymentHistory.Add(newRecord);

        // ── Sync denormalized fields on Employee ───────────────────────────────

        employee.DepartmentId = request.DepartmentId;
        employee.DesignationId = request.DesignationId;
        employee.ReportingManagerId = request.ManagerId;
        employee.EmployeeTypeId = request.EmployeeTypeId;
        employee.EmployeeType = references.EmployeeType?.Name ?? request.EmploymentType.ToString();
        employee.CostCenterId = request.CostCenterId;
        employee.CostCenterCode = references.CostCenter?.Code;
        employee.Status = request.EmploymentStatus;
        employee.DateOfLeaving = request.EmploymentStatus == EmployeeStatus.Active
            ? null
            : request.EffectiveFrom;
        employee.ModifiedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Reload the saved record with its master navigations so the response carries the display names
        // (department, designation, grade, …) the client shows in the history table. It is still tracked, so
        // this returns the same instance with the navigations populated.
        var saved = await IncludeWithMasters(
                _db.EmployeeEmploymentHistory.Where(e => e.Id == newRecord.Id))
            .FirstAsync(cancellationToken);

        _logger.LogInformation(
            "Created position change for employee {EmployeeId} effective {EffectiveFrom} in tenant {TenantId}.",
            employeeId, request.EffectiveFrom, tenantId);

        return Result<EmployeeEmploymentHistoryDto>.Success(
            MapToDto(saved), "Position change recorded.");
    }

    private async Task<Result<EmployeeEmploymentHistoryDto>?> AssignPendingEmployeeCodeAsync(
        Employee employee, EmploymentChangeRequest request, EmploymentReferences references, CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId!.Value;
        var matchingVersions = await _db.EmployeeCodeConfigVersions
            .Where(v => v.TenantId == tenantId && v.IsActive &&
                        v.EffectiveFrom <= request.EffectiveFrom &&
                        (v.EffectiveTo == null || request.EffectiveFrom <= v.EffectiveTo))
            .OrderBy(v => v.EffectiveFrom)
            .ToListAsync(cancellationToken);
        if (matchingVersions.Count == 0)
            return Result<EmployeeEmploymentHistoryDto>.Invalid("employeeCode", $"No active Employee Code configuration is effective for {request.EffectiveFrom:dd-MMM-yyyy}.");
        if (matchingVersions.Count > 1)
            return Result<EmployeeEmploymentHistoryDto>.Conflict("Multiple active Employee Code configurations are effective for the selected date.");
        var version = matchingVersions[0];

        if (version.AssignmentMode == EmployeeCodeAssignmentMode.Manual)
        {
            var code = request.EmployeeCode?.Trim();
            if (string.IsNullOrWhiteSpace(code))
                return Result<EmployeeEmploymentHistoryDto>.Invalid("employeeCode", "Employee code is required for manual assignment.");
            if (await _db.Employees.AnyAsync(e => e.Id != employee.Id && e.EmployeeCode != null && e.EmployeeCode.ToLower() == code.ToLower(), cancellationToken))
                return Result<EmployeeEmploymentHistoryDto>.Conflict("This employee code is already in use.");
            employee.EmployeeCode = code;
            return null;
        }

        if (version.AssignmentMode != EmployeeCodeAssignmentMode.Auto || version.GenerationMethod is null)
            return Result<EmployeeEmploymentHistoryDto>.Invalid("employeeCode", "Employee Code configuration has an invalid generation method.");

        var values = new Dictionary<EmployeeCodeConditionField, string?>
        {
            [EmployeeCodeConditionField.HoldingCompany] = references.HoldingCompany?.Code,
            [EmployeeCodeConditionField.Lob] = references.Lob?.Code,
            [EmployeeCodeConditionField.Organisation] = references.Organisation?.Code,
            [EmployeeCodeConditionField.Department] = references.Department?.Code,
            [EmployeeCodeConditionField.SubDepartment] = references.SubDepartment?.Code,
            [EmployeeCodeConditionField.Section] = references.Section?.Code,
            [EmployeeCodeConditionField.SubSection] = references.SubSection?.Code,
            [EmployeeCodeConditionField.Function] = references.Function?.Code,
            [EmployeeCodeConditionField.SubFunction] = references.SubFunction?.Code,
            [EmployeeCodeConditionField.Grade] = references.Grade?.Code,
            [EmployeeCodeConditionField.Designation] = references.Designation?.Code,
            [EmployeeCodeConditionField.EmployeeType] = references.EmployeeType?.Code,
            [EmployeeCodeConditionField.Country] = references.Country?.Code,
            [EmployeeCodeConditionField.Location] = references.WorkLocation?.Code,
            [EmployeeCodeConditionField.WorkLocation] = references.WorkLocation?.Code,
            [EmployeeCodeConditionField.CostCenter] = references.CostCenter?.Code
        };
        var referenceIds = new Dictionary<EmployeeCodeConditionField, Guid?>
        {
            [EmployeeCodeConditionField.HoldingCompany] = references.HoldingCompany?.Id,
            [EmployeeCodeConditionField.Lob] = references.Lob?.Id,
            [EmployeeCodeConditionField.Organisation] = references.Organisation?.Id,
            [EmployeeCodeConditionField.Department] = references.Department?.Id,
            [EmployeeCodeConditionField.SubDepartment] = references.SubDepartment?.Id,
            [EmployeeCodeConditionField.Section] = references.Section?.Id,
            [EmployeeCodeConditionField.SubSection] = references.SubSection?.Id,
            [EmployeeCodeConditionField.Function] = references.Function?.Id,
            [EmployeeCodeConditionField.SubFunction] = references.SubFunction?.Id,
            [EmployeeCodeConditionField.Grade] = references.Grade?.Id,
            [EmployeeCodeConditionField.Designation] = references.Designation?.Id,
            [EmployeeCodeConditionField.EmployeeType] = references.EmployeeType?.Id,
            [EmployeeCodeConditionField.Country] = references.Country?.Id,
            [EmployeeCodeConditionField.Location] = references.WorkLocation?.Id,
            [EmployeeCodeConditionField.WorkLocation] = references.WorkLocation?.Id,
            [EmployeeCodeConditionField.CostCenter] = references.CostCenter?.Id
        };
        // These enums intentionally do not share numeric values. Keep the mapping explicit so a
        // condition for Department cannot accidentally render as a different segment type.
        var segmentValues = new Dictionary<EmployeeCodeSegmentType, string?>
        {
            [EmployeeCodeSegmentType.HoldingCompanyCode] = values[EmployeeCodeConditionField.HoldingCompany],
            [EmployeeCodeSegmentType.LobCode] = values[EmployeeCodeConditionField.Lob],
            [EmployeeCodeSegmentType.OrganisationCode] = values[EmployeeCodeConditionField.Organisation],
            [EmployeeCodeSegmentType.DepartmentCode] = values[EmployeeCodeConditionField.Department],
            [EmployeeCodeSegmentType.SubDepartmentCode] = values[EmployeeCodeConditionField.SubDepartment],
            [EmployeeCodeSegmentType.SectionCode] = values[EmployeeCodeConditionField.Section],
            [EmployeeCodeSegmentType.SubSectionCode] = values[EmployeeCodeConditionField.SubSection],
            [EmployeeCodeSegmentType.FunctionCode] = values[EmployeeCodeConditionField.Function],
            [EmployeeCodeSegmentType.SubFunctionCode] = values[EmployeeCodeConditionField.SubFunction],
            [EmployeeCodeSegmentType.GradeCode] = values[EmployeeCodeConditionField.Grade],
            [EmployeeCodeSegmentType.DesignationCode] = values[EmployeeCodeConditionField.Designation],
            [EmployeeCodeSegmentType.EmployeeTypeCode] = values[EmployeeCodeConditionField.EmployeeType],
            [EmployeeCodeSegmentType.CountryCode] = values[EmployeeCodeConditionField.Country],
            [EmployeeCodeSegmentType.LocationCode] = values[EmployeeCodeConditionField.WorkLocation],
            [EmployeeCodeSegmentType.WorkLocationCode] = values[EmployeeCodeConditionField.WorkLocation],
            [EmployeeCodeSegmentType.CostCenterCode] = values[EmployeeCodeConditionField.CostCenter]
        };
        var context = new EmployeeCodeGenerationContext(request.EffectiveFrom, values, segmentValues);

        if (version.GenerationMethod == EmployeeCodeGenerationMethod.RuleBased)
        {
            var rules = await _db.EmployeeCodeRules
                .Include(r => r.Conditions)
                .Include(r => r.Segments)
                .Where(r => r.EmployeeCodeConfigVersionId == version.Id && !r.IsDeleted && r.Status == EmployeeCodeRuleStatus.Active)
                .ToListAsync(cancellationToken);
                var specific = rules.Where(r => !r.IsDefault && r.Conditions.Count > 0)
                .Where(r => r.Conditions.All(c => ConditionMatches(c, values, referenceIds)))
                .OrderBy(r => r.Priority).ThenBy(r => r.Id).FirstOrDefault();
            var rule = specific ?? rules.SingleOrDefault(r => r.IsDefault);
            if (rule is null)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("employeeCode", "No valid active Employee Code rule matches the selected Initial Employment values.");
            if (_codeSequence is null || _codeRenderer is null)
                return Result<EmployeeEmploymentHistoryDto>.Conflict("Employee Code generation services are unavailable.");
            var sequence = await _codeSequence.AllocateAsync(rule.Id, EmployeeCodeSequenceScope.Tenant, $"RULE:{rule.Id}", EmployeeCodeResetPeriod.Never, "NONE", cancellationToken: cancellationToken);
            if (!sequence.Succeeded)
                return Result<EmployeeEmploymentHistoryDto>.Conflict(sequence.Message ?? "Unable to allocate an Employee Code sequence.");
            var rendered = _codeRenderer.Render(rule, context, sequence.Value, version.Separator);
            if (rendered.Error is not null)
                return Result<EmployeeEmploymentHistoryDto>.Invalid("employeeCode", rendered.Error);
            if (await _db.Employees.AnyAsync(e => e.Id != employee.Id && e.EmployeeCode == rendered.Code, cancellationToken))
                return Result<EmployeeEmploymentHistoryDto>.Conflict("Generated Employee Code is already in use; please retry.");
            employee.EmployeeCode = rendered.Code;
            return null;
        }

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var number = Math.Max(1, version.NextNumber);
            var prefix = (version.Prefix ?? "EMP").Trim().ToUpperInvariant();
            var padding = Math.Clamp(version.Padding, 0, 10);
            var code = prefix + (version.Separator ?? string.Empty) + number.ToString(padding == 0 ? string.Empty : $"D{padding}");
            var rows = await _db.EmployeeCodeConfigVersions.Where(v => v.Id == version.Id && v.NextNumber == number)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.NextNumber, number + 1), cancellationToken);
            if (rows == 1) { employee.EmployeeCode = code; return null; }
            version = await _db.EmployeeCodeConfigVersions.FirstAsync(v => v.Id == version.Id, cancellationToken);
        }
        return Result<EmployeeEmploymentHistoryDto>.Conflict("Employee Code sequence is busy; please retry.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static Result<EmployeeEmploymentDto>? ValidateEmploymentDetailsRequest(
        EmployeeEmploymentRequest request)
    {
        if (request.FirstHiredDate == default)
            return Result<EmployeeEmploymentDto>.Invalid("firstHiredDate", "First hired date is required.");

        if (request.DateOfJoining == default)
            return Result<EmployeeEmploymentDto>.Invalid("dateOfJoining", "Date of joining is required.");

        if (request.DateOfJoining < request.FirstHiredDate)
            return Result<EmployeeEmploymentDto>.Invalid("dateOfJoining", "Date of joining cannot be before the first hired date.");

        if (request.GroupDateOfJoining > request.DateOfJoining)
            return Result<EmployeeEmploymentDto>.Invalid("groupDateOfJoining", "Group date of joining cannot be after the date of joining.");

        if (request.ConfirmationDate < request.DateOfJoining)
            return Result<EmployeeEmploymentDto>.Invalid("confirmationDate", "Confirmation date cannot be before the date of joining.");

        if (Normalize(request.JobStatus)?.Length > 100)
            return Result<EmployeeEmploymentDto>.Invalid("jobStatus", "Job status must not exceed 100 characters.");

        var probationUnit = NormalizePeriodUnit(request.ProbationPeriodUnit);
        if (request.ProbationPeriod is <= 0)
            return Result<EmployeeEmploymentDto>.Invalid("probationPeriod", "Probation period must be greater than zero.");

        if (request.ProbationPeriod.HasValue != (probationUnit is not null))
            return Result<EmployeeEmploymentDto>.Invalid("probationPeriodUnit", "Probation period and unit must be supplied together.");

        if (probationUnit is not null && probationUnit is not ("Days" or "Months" or "Years"))
            return Result<EmployeeEmploymentDto>.Invalid("probationPeriodUnit", "Probation period unit must be Days, Months, or Years.");

        var noticeUnit = NormalizePeriodUnit(request.NoticePeriodUnit);
        if (request.NoticePeriod is <= 0)
            return Result<EmployeeEmploymentDto>.Invalid("noticePeriod", "Notice period must be greater than zero.");

        if (request.NoticePeriod.HasValue != (noticeUnit is not null))
            return Result<EmployeeEmploymentDto>.Invalid("noticePeriodUnit", "Notice period and unit must be supplied together.");

        if (noticeUnit is not null && noticeUnit is not ("Days" or "Months"))
            return Result<EmployeeEmploymentDto>.Invalid("noticePeriodUnit", "Notice period unit must be Days or Months.");

        return null;
    }

    private static Result<EmployeeEmploymentHistoryDto>? ValidateEmploymentChangeCommand(
        EmploymentChangeRequest request)
    {
        if (request.EffectiveFrom == default)
            return Result<EmployeeEmploymentHistoryDto>.Invalid("effectiveFrom", "Effective date is required.");

        if (request.EffectiveFrom < DateOnly.FromDateTime(DateTime.UtcNow))
            return Result<EmployeeEmploymentHistoryDto>.Invalid("effectiveFrom", "Effective date must be today or in the future.");

        var employeeCode = Normalize(request.EmployeeCode);
        if (employeeCode?.Length > 100)
            return Result<EmployeeEmploymentHistoryDto>.Invalid("employeeCode", "Employee code must not exceed 100 characters.");

        if (employeeCode is not null && !Regex.IsMatch(employeeCode, CodeFormats.Pattern))
            return Result<EmployeeEmploymentHistoryDto>.Invalid("employeeCode", CodeFormats.Message);

        if (!request.DepartmentId.HasValue)
            return Result<EmployeeEmploymentHistoryDto>.Invalid("departmentId", "Department is required.");

        if (!request.DesignationId.HasValue)
            return Result<EmployeeEmploymentHistoryDto>.Invalid("designationId", "Designation is required.");

        if (!request.PositionChangeReasonId.HasValue)
            return Result<EmployeeEmploymentHistoryDto>.Invalid("positionChangeReasonId", "Change reason is required.");

        if (!Enum.IsDefined(typeof(EmploymentChangeReason), request.ChangeReason) ||
            request.ChangeReason == EmploymentChangeReason.Unspecified)
        {
            return Result<EmployeeEmploymentHistoryDto>.Invalid("changeReason", "Change reason is invalid.");
        }

        if (!Enum.IsDefined(typeof(EmploymentType), request.EmploymentType) ||
            request.EmploymentType == EmploymentType.Unspecified)
        {
            return Result<EmployeeEmploymentHistoryDto>.Invalid("employmentType", "Employment type is invalid.");
        }

        if (!Enum.IsDefined(typeof(EmployeeStatus), request.EmploymentStatus))
            return Result<EmployeeEmploymentHistoryDto>.Invalid("employmentStatus", "Employment status is invalid.");

        if (Normalize(request.BusinessRole)?.Length > 200)
            return Result<EmployeeEmploymentHistoryDto>.Invalid("businessRole", "Business role must not exceed 200 characters.");

        if (Normalize(request.GradeLevel)?.Length > 50)
            return Result<EmployeeEmploymentHistoryDto>.Invalid("gradeLevel", "Grade level must not exceed 50 characters.");

        if (Normalize(request.CareerGroup)?.Length > 100)
            return Result<EmployeeEmploymentHistoryDto>.Invalid("careerGroup", "Career group must not exceed 100 characters.");

        if (Normalize(request.ChangeReasonDescription)?.Length > 500)
            return Result<EmployeeEmploymentHistoryDto>.Invalid("changeReasonDescription", "Change reason description must not exceed 500 characters.");

        if (request.WorkLocationId.HasValue && !request.CountryLocationId.HasValue)
            return Result<EmployeeEmploymentHistoryDto>.Invalid("countryLocationId", "Country is required when a work location is selected.");

        return null;
    }

    private async Task<(Result<EmployeeEmploymentHistoryDto>? Error, EmploymentReferences References)>
        ValidateEmploymentReferencesAsync(
            Guid employeeId,
            Guid tenantId,
            EmploymentChangeRequest request,
            CancellationToken cancellationToken)
    {
        var references = new EmploymentReferences();

        references.Department = await _db.Departments.AsNoTracking().FirstOrDefaultAsync(
            d => d.Id == request.DepartmentId && d.TenantId == tenantId && d.IsActive, cancellationToken);
        if (references.Department is null)
            return (Result<EmployeeEmploymentHistoryDto>.Invalid("departmentId", "Department does not exist, is inactive, or belongs to another tenant."), references);

        references.Designation = await _db.Designations.AsNoTracking().FirstOrDefaultAsync(
            d => d.Id == request.DesignationId && d.TenantId == tenantId && d.IsActive, cancellationToken);
        if (references.Designation is null)
            return (Result<EmployeeEmploymentHistoryDto>.Invalid("designationId", "Designation does not exist, is inactive, or belongs to another tenant."), references);

        if (request.HoldingCompanyId is Guid holdingCompanyId)
        {
            references.HoldingCompany = await _db.HoldingCompanies.AsNoTracking().FirstOrDefaultAsync(
                h => h.Id == holdingCompanyId && h.TenantId == tenantId && h.IsActive, cancellationToken);
            if (references.HoldingCompany is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("holdingCompanyId", "Holding Company does not exist, is inactive, or belongs to another tenant."), references);
        }

        if (request.LobId is Guid lobId)
        {
            references.Lob = await _db.LinesOfBusiness.AsNoTracking().FirstOrDefaultAsync(
                l => l.Id == lobId && l.TenantId == tenantId && l.IsActive, cancellationToken);
            if (references.Lob is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("lobId", "Line of Business does not exist, is inactive, or belongs to another tenant."), references);

            if (references.Lob.HoldingCompanyId != request.HoldingCompanyId)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("lobId", "Line of Business does not belong to the selected Holding Company."), references);
        }

        if (request.OrganisationId is Guid organisationId)
        {
            references.Organisation = await _db.Organisations.AsNoTracking().FirstOrDefaultAsync(
                o => o.Id == organisationId && o.TenantId == tenantId && o.IsActive, cancellationToken);
            if (references.Organisation is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("organisationId", "Organisation does not exist, is inactive, or belongs to another tenant."), references);
        }

        if (request.SubDepartmentId is Guid subDepartmentId)
        {
            references.SubDepartment = await _db.SubDepartments.AsNoTracking().FirstOrDefaultAsync(
                s => s.Id == subDepartmentId && s.TenantId == tenantId && s.IsActive, cancellationToken);
            if (references.SubDepartment is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("subDepartmentId", "Sub Department does not exist, is inactive, or belongs to another tenant."), references);

            if (references.SubDepartment.DepartmentId != request.DepartmentId)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("subDepartmentId", "Sub Department does not belong to the selected Department."), references);
        }

        if (request.SectionId is Guid sectionId)
        {
            references.Section = await _db.Sections.AsNoTracking().FirstOrDefaultAsync(
                s => s.Id == sectionId && s.TenantId == tenantId && s.IsActive, cancellationToken);
            if (references.Section is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("sectionId", "Section does not exist, is inactive, or belongs to another tenant."), references);

            if (references.Section.SubDepartmentId != request.SubDepartmentId)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("sectionId", "Section does not belong to the selected Sub Department."), references);
        }

        if (request.SubSectionId is Guid subSectionId)
        {
            references.SubSection = await _db.SubSections.AsNoTracking().FirstOrDefaultAsync(
                s => s.Id == subSectionId && s.TenantId == tenantId && s.IsActive, cancellationToken);
            if (references.SubSection is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("subSectionId", "Sub Section does not exist, is inactive, or belongs to another tenant."), references);

            if (references.SubSection.SectionId != request.SectionId)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("subSectionId", "Sub Section does not belong to the selected Section."), references);
        }

        if (request.FunctionId is Guid functionId)
        {
            references.Function = await _db.Functions.AsNoTracking().FirstOrDefaultAsync(
                f => f.Id == functionId && f.TenantId == tenantId && f.IsActive, cancellationToken);
            if (references.Function is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("functionId", "Function does not exist, is inactive, or belongs to another tenant."), references);
        }

        if (request.SubFunctionId is Guid subFunctionId)
        {
            references.SubFunction = await _db.SubFunctions.AsNoTracking().FirstOrDefaultAsync(
                s => s.Id == subFunctionId && s.TenantId == tenantId && s.IsActive, cancellationToken);
            if (references.SubFunction is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("subFunctionId", "Sub Function does not exist, is inactive, or belongs to another tenant."), references);

            if (references.SubFunction.FunctionId != request.FunctionId)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("subFunctionId", "Sub Function does not belong to the selected Function."), references);
        }

        if (request.GradeId is Guid gradeId)
        {
            references.Grade = await _db.Grades.AsNoTracking().FirstOrDefaultAsync(
                g => g.Id == gradeId && g.TenantId == tenantId && g.IsActive, cancellationToken);
            if (references.Grade is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("gradeId", "Grade does not exist, is inactive, or belongs to another tenant."), references);
        }

        if (request.EmployeeTypeId is Guid employeeTypeId)
        {
            references.EmployeeType = await _db.EmployeeTypes.AsNoTracking().FirstOrDefaultAsync(
                e => e.Id == employeeTypeId && e.TenantId == tenantId && e.IsActive, cancellationToken);
            if (references.EmployeeType is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("employeeTypeId", "Employee Type does not exist, is inactive, or belongs to another tenant."), references);
        }

        if (request.CountryLocationId is Guid countryId)
        {
            references.Country = await _db.Countries.AsNoTracking().FirstOrDefaultAsync(
                c => c.Id == countryId && c.IsActive, cancellationToken);
            if (references.Country is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("countryLocationId", "Country does not exist or is inactive."), references);
        }

        if (request.WorkLocationId is Guid workLocationId)
        {
            references.WorkLocation = await _db.WorkLocations.AsNoTracking().FirstOrDefaultAsync(
                w => w.Id == workLocationId && w.TenantId == tenantId && w.IsActive, cancellationToken);
            if (references.WorkLocation is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("workLocationId", "Work Location does not exist, is inactive, or belongs to another tenant."), references);

            // WorkLocation currently has no CountryId in the schema. Country existence and tenant ownership
            // are enforced here; validating the relationship itself requires a separately approved schema change.
        }

        if (request.CostCenterId is Guid costCenterId)
        {
            references.CostCenter = await _db.CostCenters.AsNoTracking().FirstOrDefaultAsync(
                c => c.Id == costCenterId && c.TenantId == tenantId && c.IsActive, cancellationToken);
            if (references.CostCenter is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("costCenterId", "Cost Center does not exist, is inactive, or belongs to another tenant."), references);
        }

        references.PositionChangeReason = await _db.PositionChangeReasons.AsNoTracking().FirstOrDefaultAsync(
            p => p.Id == request.PositionChangeReasonId && p.TenantId == tenantId && p.IsActive, cancellationToken);
        if (references.PositionChangeReason is null)
            return (Result<EmployeeEmploymentHistoryDto>.Invalid("positionChangeReasonId", "Position Change Reason does not exist, is inactive, or belongs to another tenant."), references);

        if (request.ManagerId is Guid managerId)
        {
            if (managerId == employeeId)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("managerId", "An employee cannot be their own manager."), references);

            references.Manager = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(
                m => m.Id == managerId && m.TenantId == tenantId && m.Status == EmployeeStatus.Active,
                cancellationToken);
            if (references.Manager is null)
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("managerId", "Manager employee does not exist, is inactive, or belongs to another tenant."), references);

            if (await WouldCreateManagerCycleAsync(employeeId, managerId, tenantId, cancellationToken))
                return (Result<EmployeeEmploymentHistoryDto>.Invalid("managerId", "The selected manager would create a reporting cycle."), references);
        }

        return (null, references);
    }

    private async Task<bool> WouldCreateManagerCycleAsync(
        Guid employeeId,
        Guid managerId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid> { employeeId };
        Guid? currentId = managerId;

        while (currentId.HasValue)
        {
            if (!visited.Add(currentId.Value))
                return true;

            currentId = await _db.Employees.AsNoTracking()
                .Where(e => e.Id == currentId.Value && e.TenantId == tenantId)
                .Select(e => e.ReportingManagerId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// Eager-loads every master / reporting navigation so the code+name display fields in
    /// <see cref="MapToDto"/> resolve. Doing this in the query keeps the projection translatable —
    /// EF cannot translate a custom method call inside <c>Select</c>.
    /// </summary>
    private static IQueryable<EmployeeEmploymentHistory> IncludeWithMasters(
        IQueryable<EmployeeEmploymentHistory> query) =>
        query
            .Include(e => e.HoldingCompany)
            .Include(e => e.Lob)
            .Include(e => e.Organisation)
            .Include(e => e.Department)
            .Include(e => e.SubDepartment)
            .Include(e => e.Section)
            .Include(e => e.SubSection)
            .Include(e => e.Function)
            .Include(e => e.SubFunction)
            .Include(e => e.Grade)
            .Include(e => e.Designation)
            .Include(e => e.EmployeeType)
            .Include(e => e.CountryLocation)
            .Include(e => e.WorkLocation)
            .Include(e => e.CostCenter)
            .Include(e => e.Manager)
            .Include(e => e.PositionChangeReason);

    private async Task<bool> EmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        return await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId, cancellationToken);
    }

    private static EmployeeEmploymentHistoryDto MapToDto(EmployeeEmploymentHistory e) =>
        new(e.Id, e.EmployeeId, e.EffectiveFrom, e.EffectiveTo,

            // Organizational FK references + display names from navigation properties
            e.HoldingCompanyId, e.HoldingCompany?.Code, e.HoldingCompany?.Name,
            e.LobId, e.Lob?.Code, e.Lob?.Name,
            e.OrganisationId, e.Organisation?.Code, e.Organisation?.Name,
            e.DepartmentId, e.Department?.Code, e.DepartmentName ?? e.Department?.Name,
            e.SubDepartmentId, e.SubDepartment?.Code, e.SubDepartment?.Name,
            e.SectionId, e.Section?.Code, e.Section?.Name,
            e.SubSectionId, e.SubSection?.Code, e.SubSection?.Name,
            e.FunctionId, e.Function?.Code, e.Function?.Name,
            e.SubFunctionId, e.SubFunction?.Code, e.SubFunction?.Name,

            // Job classification
            e.GradeId, e.Grade?.Code, e.Grade?.Name,
            e.DesignationId, e.Designation?.Code, e.DesignationName ?? e.Designation?.Name,
            e.EmployeeTypeId, e.EmployeeType?.Code, e.EmployeeType?.Name,

            // Location
            e.CountryLocationId, e.CountryLocation?.Code, e.CountryLocation?.Name,
            e.WorkLocationId, e.WorkLocation?.Code, e.WorkLocation?.Name,

            // Cost center
            e.CostCenterId, e.CostCenter?.Code, e.CostCenter?.Name,

            // Reporting
            e.ManagerId, e.ManagerCode ?? e.Manager?.EmployeeCode,
            e.ManagerName ?? (e.Manager is not null ? EmployeeFullName(e.Manager) : null),

            // Change metadata
            e.PositionChangeReasonId, e.PositionChangeReason?.Code, e.PositionChangeReason?.Name,
            e.ChangeReason, e.ChangeReasonDescription,

            // Snapshot fields
            e.BusinessRole, e.GradeLevel, e.CareerGroup, e.EmploymentType, e.EmploymentStatus,

            // Audit
            e.CreatedBy, e.CreatedDate, e.ModifiedDate);

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? NormalizePeriodUnit(string? value) =>
        Normalize(value)?.ToLowerInvariant() switch
        {
            "day" or "days" => "Days",
            "month" or "months" => "Months",
            "year" or "years" => "Years",
            null => null,
            _ => Normalize(value)
        };

    private static string EmployeeFullName(Employee employee) =>
        string.Join(" ", new[] { employee.FirstName, employee.MiddleName, employee.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    private static bool ConditionMatches(
        EmployeeCodeRuleCondition condition,
        IReadOnlyDictionary<EmployeeCodeConditionField, string?> values,
        IReadOnlyDictionary<EmployeeCodeConditionField, Guid?> referenceIds)
    {
        if (condition.Operator != EmployeeCodeConditionOperator.Equals)
            return false;

        if (condition.ReferenceId is Guid referenceId)
        {
            return referenceIds.TryGetValue(condition.Field, out var actualId) && actualId == referenceId;
        }

        return values.TryGetValue(condition.Field, out var actualCode) &&
               actualCode is not null &&
               string.Equals(actualCode, condition.Value, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class EmploymentReferences
    {
        public HoldingCompany? HoldingCompany { get; set; }
        public Lob? Lob { get; set; }
        public Organisation? Organisation { get; set; }
        public Department? Department { get; set; }
        public SubDepartment? SubDepartment { get; set; }
        public Section? Section { get; set; }
        public SubSection? SubSection { get; set; }
        public Function? Function { get; set; }
        public SubFunction? SubFunction { get; set; }
        public Grade? Grade { get; set; }
        public Designation? Designation { get; set; }
        public EmployeeType? EmployeeType { get; set; }
        public Country? Country { get; set; }
        public WorkLocation? WorkLocation { get; set; }
        public CostCenter? CostCenter { get; set; }
        public Employee? Manager { get; set; }
        public PositionChangeReason? PositionChangeReason { get; set; }
    }
}
