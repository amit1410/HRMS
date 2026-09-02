using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public class EmployeeSupervisorService : IEmployeeSupervisorService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Employee not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EmployeeSupervisorService> _logger;

    public EmployeeSupervisorService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<EmployeeSupervisorService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<EmployeeSupervisorDto>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<EmployeeSupervisorDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeeSupervisorDto>.NotFound(NotFoundMessage);
        }

        var supervisor = await _db.EmployeeSupervisors.AsNoTracking()
            .Where(s => s.EmployeeId == employeeId)
            .Select(s => new EmployeeSupervisorDto(
                s.Id,
                s.EmployeeId,
                s.L1ManagerCode,
                s.L1ManagerName,
                s.L1ManagerId,
                s.L2ManagerCode,
                s.L2ManagerName,
                s.L2ManagerId,
                s.L3ManagerCode,
                s.L3ManagerName,
                s.L3ManagerId,
                s.L4ManagerCode,
                s.L4ManagerName,
                s.L4ManagerId,
                s.L5ManagerCode,
                s.L5ManagerName,
                s.L5ManagerId,
                s.TimeManagerCode,
                s.TimeManagerName,
                s.TimeManagerId,
                s.EroCode,
                s.EroName,
                s.EroId,
                s.ChroManagerCode,
                s.ChroManagerName,
                s.ChroManagerId,
                s.CreatedDate,
                s.ModifiedDate))
            .FirstOrDefaultAsync(cancellationToken);

        return supervisor is null
            ? Result<EmployeeSupervisorDto>.NotFound("Supervisor record not found for this employee.")
            : Result<EmployeeSupervisorDto>.Success(supervisor);
    }

    public async Task<Result<EmployeeSupervisorDto>> UpsertAsync(
        Guid employeeId, EmployeeSupervisorRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeSupervisorDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeeSupervisorDto>.NotFound(NotFoundMessage);
        }

        var referenceValidation = await ValidateSupervisorReferencesAsync(
            employeeId, tenantId, request, cancellationToken);
        if (referenceValidation is not null)
        {
            return referenceValidation;
        }

        var existing = await _db.EmployeeSupervisors
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId, cancellationToken);

        if (existing is null)
        {
            var supervisor = new EmployeeSupervisor
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                L1ManagerCode = Normalize(request.L1ManagerCode),
                L1ManagerName = Normalize(request.L1ManagerName),
                L1ManagerId = request.L1ManagerId,
                L2ManagerCode = Normalize(request.L2ManagerCode),
                L2ManagerName = Normalize(request.L2ManagerName),
                L2ManagerId = request.L2ManagerId,
                L3ManagerCode = Normalize(request.L3ManagerCode),
                L3ManagerName = Normalize(request.L3ManagerName),
                L3ManagerId = request.L3ManagerId,
                L4ManagerCode = Normalize(request.L4ManagerCode),
                L4ManagerName = Normalize(request.L4ManagerName),
                L4ManagerId = request.L4ManagerId,
                L5ManagerCode = Normalize(request.L5ManagerCode),
                L5ManagerName = Normalize(request.L5ManagerName),
                L5ManagerId = request.L5ManagerId,
                TimeManagerCode = Normalize(request.TimeManagerCode),
                TimeManagerName = Normalize(request.TimeManagerName),
                TimeManagerId = request.TimeManagerId,
                EroCode = Normalize(request.EroCode),
                EroName = Normalize(request.EroName),
                EroId = request.EroId,
                ChroManagerCode = Normalize(request.ChroManagerCode),
                ChroManagerName = Normalize(request.ChroManagerName),
                ChroManagerId = request.ChroManagerId
            };

            _db.EmployeeSupervisors.Add(supervisor);
        }
        else
        {
            existing.L1ManagerCode = Normalize(request.L1ManagerCode);
            existing.L1ManagerName = Normalize(request.L1ManagerName);
            existing.L1ManagerId = request.L1ManagerId;
            existing.L2ManagerCode = Normalize(request.L2ManagerCode);
            existing.L2ManagerName = Normalize(request.L2ManagerName);
            existing.L2ManagerId = request.L2ManagerId;
            existing.L3ManagerCode = Normalize(request.L3ManagerCode);
            existing.L3ManagerName = Normalize(request.L3ManagerName);
            existing.L3ManagerId = request.L3ManagerId;
            existing.L4ManagerCode = Normalize(request.L4ManagerCode);
            existing.L4ManagerName = Normalize(request.L4ManagerName);
            existing.L4ManagerId = request.L4ManagerId;
            existing.L5ManagerCode = Normalize(request.L5ManagerCode);
            existing.L5ManagerName = Normalize(request.L5ManagerName);
            existing.L5ManagerId = request.L5ManagerId;
            existing.TimeManagerCode = Normalize(request.TimeManagerCode);
            existing.TimeManagerName = Normalize(request.TimeManagerName);
            existing.TimeManagerId = request.TimeManagerId;
            existing.EroCode = Normalize(request.EroCode);
            existing.EroName = Normalize(request.EroName);
            existing.EroId = request.EroId;
            existing.ChroManagerCode = Normalize(request.ChroManagerCode);
            existing.ChroManagerName = Normalize(request.ChroManagerName);
            existing.ChroManagerId = request.ChroManagerId;
            existing.ModifiedDate = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Upserted supervisor for employee {EmployeeId} in tenant {TenantId}.", employeeId, tenantId);

        var saved = await _db.EmployeeSupervisors.AsNoTracking()
            .Where(s => s.EmployeeId == employeeId)
            .Select(s => new EmployeeSupervisorDto(
                s.Id,
                s.EmployeeId,
                s.L1ManagerCode,
                s.L1ManagerName,
                s.L1ManagerId,
                s.L2ManagerCode,
                s.L2ManagerName,
                s.L2ManagerId,
                s.L3ManagerCode,
                s.L3ManagerName,
                s.L3ManagerId,
                s.L4ManagerCode,
                s.L4ManagerName,
                s.L4ManagerId,
                s.L5ManagerCode,
                s.L5ManagerName,
                s.L5ManagerId,
                s.TimeManagerCode,
                s.TimeManagerName,
                s.TimeManagerId,
                s.EroCode,
                s.EroName,
                s.EroId,
                s.ChroManagerCode,
                s.ChroManagerName,
                s.ChroManagerId,
                s.CreatedDate,
                s.ModifiedDate))
            .FirstOrDefaultAsync(cancellationToken);

        return saved is null
            ? Result<EmployeeSupervisorDto>.NotFound("Supervisor record not found after save.")
            : Result<EmployeeSupervisorDto>.Success(saved, "Supervisor updated.");
    }

    private async Task<bool> EmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        return await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId, cancellationToken);
    }

    private async Task<Result<EmployeeSupervisorDto>?> ValidateSupervisorReferencesAsync(
        Guid employeeId,
        Guid tenantId,
        EmployeeSupervisorRequest request,
        CancellationToken cancellationToken)
    {
        var references = new (string Field, Guid? Id)[]
        {
            ("l1ManagerId", request.L1ManagerId),
            ("l2ManagerId", request.L2ManagerId),
            ("l3ManagerId", request.L3ManagerId),
            ("l4ManagerId", request.L4ManagerId),
            ("l5ManagerId", request.L5ManagerId),
            ("timeManagerId", request.TimeManagerId),
            ("eroId", request.EroId),
            ("chroManagerId", request.ChroManagerId)
        };

        foreach (var (field, id) in references)
        {
            if (id == employeeId)
            {
                return Result<EmployeeSupervisorDto>.Invalid(
                    field, "An employee cannot be assigned as their own supervisor.");
            }

            if (id is Guid supervisorId)
            {
                var valid = await _db.Employees.AsNoTracking().AnyAsync(
                    e => e.Id == supervisorId && e.TenantId == tenantId && e.Status == EmployeeStatus.Active,
                    cancellationToken);
                if (!valid)
                {
                    return Result<EmployeeSupervisorDto>.Invalid(
                        field, "Supervisor employee does not exist, is inactive, or belongs to another tenant.");
                }
            }
        }

        // L1 is the direct reporting relationship. Reuse the authoritative Employee.ReportingManagerId
        // graph so supervisor writes cannot introduce a direct or indirect reporting cycle either.
        if (request.L1ManagerId is Guid l1ManagerId &&
            await WouldCreateReportingCycleAsync(employeeId, l1ManagerId, tenantId, cancellationToken))
        {
            return Result<EmployeeSupervisorDto>.Invalid(
                "l1ManagerId", "The selected supervisor would create a reporting cycle.");
        }

        return null;
    }

    private async Task<bool> WouldCreateReportingCycleAsync(
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

    public async Task<Result<IReadOnlyList<SupervisorOptionDto>>> GetSupervisorOptionsAsync(
        Guid employeeId, string supervisorType, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<IReadOnlyList<SupervisorOptionDto>>.Unauthorized(NoTenantMessage);
        }

        if (!Enum.TryParse<SupervisorType>(supervisorType, ignoreCase: true, out var supervisorTypeValue))
        {
            return Result<IReadOnlyList<SupervisorOptionDto>>.Invalid($"Invalid supervisor type: {supervisorType}");
        }

        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId, cancellationToken);

        if (employee is null)
        {
            return Result<IReadOnlyList<SupervisorOptionDto>>.NotFound(NotFoundMessage);
        }

        // Query employees eligible for the specified supervisor type
        var options = await _db.Employees.AsNoTracking()
            .Where(e =>
                e.TenantId == tenantId &&
                e.Id != employeeId &&
                e.Status == EmployeeStatus.Active &&
                (e.ManagerCategories & supervisorTypeValue) != 0)
            .OrderBy(e => e.EmployeeCode)
            .Select(e => new SupervisorOptionDto(
                e.Id,
                e.EmployeeCode ?? string.Empty,
                (e.FirstName + " " + (e.MiddleName != null ? e.MiddleName + " " : "") + e.LastName).Trim(),
                e.Department != null ? e.Department.Name : null,
                e.Designation != null ? e.Designation.Name : null))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SupervisorOptionDto>>.Success(options);
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
