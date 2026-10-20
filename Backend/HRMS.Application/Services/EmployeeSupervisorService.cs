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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmployeeSupervisorService> _logger;

    public EmployeeSupervisorService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<EmployeeSupervisorService> logger,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        if (supervisor is null)
            return Result<EmployeeSupervisorDto>.NotFound("Supervisor record not found for this employee.");

        return Result<EmployeeSupervisorDto>.Success(supervisor);
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

        await SynchronizeL1Async(employeeId, tenantId, request, cancellationToken);
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
            : await GetAsync(employeeId, cancellationToken);
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
        var references = new (string Field, Guid? Id, Action<Employee> Populate)[]
        {
            ("l1ManagerId", request.L1ManagerId, e => { request.L1ManagerCode = e.EmployeeCode; request.L1ManagerName = (e.FirstName + " " + (e.MiddleName != null ? e.MiddleName + " " : "") + e.LastName).Trim(); }),
            ("l2ManagerId", request.L2ManagerId, e => { request.L2ManagerCode = e.EmployeeCode; request.L2ManagerName = (e.FirstName + " " + (e.MiddleName != null ? e.MiddleName + " " : "") + e.LastName).Trim(); }),
            ("l3ManagerId", request.L3ManagerId, e => { request.L3ManagerCode = e.EmployeeCode; request.L3ManagerName = (e.FirstName + " " + (e.MiddleName != null ? e.MiddleName + " " : "") + e.LastName).Trim(); }),
            ("l4ManagerId", request.L4ManagerId, e => { request.L4ManagerCode = e.EmployeeCode; request.L4ManagerName = (e.FirstName + " " + (e.MiddleName != null ? e.MiddleName + " " : "") + e.LastName).Trim(); }),
            ("l5ManagerId", request.L5ManagerId, e => { request.L5ManagerCode = e.EmployeeCode; request.L5ManagerName = (e.FirstName + " " + (e.MiddleName != null ? e.MiddleName + " " : "") + e.LastName).Trim(); }),
            ("timeManagerId", request.TimeManagerId, e => { request.TimeManagerCode = e.EmployeeCode; request.TimeManagerName = (e.FirstName + " " + (e.MiddleName != null ? e.MiddleName + " " : "") + e.LastName).Trim(); }),
            ("eroId", request.EroId, e => { request.EroCode = e.EmployeeCode; request.EroName = (e.FirstName + " " + (e.MiddleName != null ? e.MiddleName + " " : "") + e.LastName).Trim(); }),
            ("chroManagerId", request.ChroManagerId, e => { request.ChroManagerCode = e.EmployeeCode; request.ChroManagerName = (e.FirstName + " " + (e.MiddleName != null ? e.MiddleName + " " : "") + e.LastName).Trim(); })
        };

        foreach (var (field, id, populate) in references)
        {
            if (id == employeeId)
            {
                return Result<EmployeeSupervisorDto>.Invalid(
                    field, "An employee cannot be assigned as their own supervisor.");
            }

            if (id is Guid supervisorId)
            {
                var manager = await EligibleSupervisorEmployees(tenantId, employeeId, BusinessDateToday())
                    .FirstOrDefaultAsync(e => e.Id == supervisorId, cancellationToken);
                if (manager is null)
                {
                    return Result<EmployeeSupervisorDto>.Invalid(
                        field, "Supervisor employee does not exist, is inactive, or belongs to another tenant.");
                }

                populate(manager);
            }
        }

        return null;
    }

    private async Task SynchronizeL1Async(
        Guid employeeId, Guid tenantId, EmployeeSupervisorRequest request, CancellationToken cancellationToken)
    {
        var employee = await _db.Employees.FirstAsync(e => e.Id == employeeId && e.TenantId == tenantId, cancellationToken);
        employee.ReportingManagerId = request.L1ManagerId;

        var businessDate = BusinessDateToday();
        var current = await _db.EmployeeEmploymentHistory
            .Where(h => h.EmployeeId == employeeId && h.TenantId == tenantId && !h.IsSuperseded &&
                        h.EffectiveFrom <= businessDate && (h.EffectiveTo == null || h.EffectiveTo >= businessDate))
            .OrderByDescending(h => h.EffectiveFrom)
            .ThenByDescending(h => h.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is not null)
        {
            current.ManagerId = request.L1ManagerId;
            current.ManagerCode = request.L1ManagerCode;
            current.ManagerName = request.L1ManagerName;
            current.ModifiedDate = DateTime.UtcNow;
        }
    }

    private DateOnly BusinessDateToday() =>
        DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);

    public async Task<Result<PagedResult<SupervisorOptionDto>>> GetSupervisorOptionsAsync(
        Guid employeeId, string supervisorType, PagedQuery query, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<PagedResult<SupervisorOptionDto>>.Unauthorized(NoTenantMessage);
        }

        if (!Enum.TryParse<SupervisorType>(supervisorType, ignoreCase: true, out _))
        {
            return Result<PagedResult<SupervisorOptionDto>>.Invalid($"Invalid supervisor type: {supervisorType}");
        }

        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId, cancellationToken);

        if (employee is null)
        {
            return Result<PagedResult<SupervisorOptionDto>>.NotFound(NotFoundMessage);
        }

        var businessDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var search = query.Search?.Trim().ToLowerInvariant();
        // A scheduled retirement or joining must not change today's supervisor choices before its
        // effective date. All supervisor roles share this same eligible employee population.
        var options = EligibleSupervisorEmployees(tenantId, employeeId, businessDate)
            .Where(e => search == null ||
                (e.EmployeeCode ?? string.Empty).ToLower().Contains(search) ||
                (e.FirstName + " " + (e.MiddleName != null ? e.MiddleName + " " : "") + e.LastName).ToLower().Contains(search))
            .OrderBy(e => e.EmployeeCode)
            .ThenBy(e => e.Id)
            .Select(e => new SupervisorOptionDto(
                e.Id,
                e.EmployeeCode ?? string.Empty,
                (e.FirstName + " " + (e.MiddleName != null ? e.MiddleName + " " : "") + e.LastName).Trim(),
                e.Department != null ? e.Department.Name : null,
                e.Designation != null ? e.Designation.Name : null));

        var page = await options.ToPagedResultAsync(query, cancellationToken);
        return Result<PagedResult<SupervisorOptionDto>>.Success(page);
    }

    private IQueryable<Employee> EligibleSupervisorEmployees(Guid tenantId, Guid employeeId, DateOnly businessDate)
    {
        var currentHistory = _db.EmployeeEmploymentHistory
            .Where(h => h.TenantId == tenantId && !h.IsSuperseded && h.EffectiveFrom <= businessDate &&
                        (h.EffectiveTo == null || h.EffectiveTo >= businessDate));

        return _db.Employees.AsNoTracking()
            .Where(e =>
                e.TenantId == tenantId &&
                e.Id != employeeId &&
                e.Status == EmployeeStatus.Active &&
                (currentHistory.Any(h => h.EmployeeId == e.Id && h.EmploymentStatus == EmployeeStatus.Active) ||
                 !currentHistory.Any(h => h.EmployeeId == e.Id) && e.DateOfJoining <= businessDate));
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
