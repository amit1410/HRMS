using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public class EmployeePreviousEmploymentService : IEmployeePreviousEmploymentService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Employee not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EmployeePreviousEmploymentService> _logger;

    public EmployeePreviousEmploymentService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<EmployeePreviousEmploymentService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<EmployeePreviousEmploymentDto>>> GetAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<IReadOnlyList<EmployeePreviousEmploymentDto>>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<IReadOnlyList<EmployeePreviousEmploymentDto>>.NotFound(NotFoundMessage);
        }

        var records = await _db.EmployeePreviousEmployments.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId)
            .OrderByDescending(p => p.TenureFrom)
            .Select(p => new EmployeePreviousEmploymentDto(
                p.Id,
                p.EmployeeId,
                p.Company,
                p.Designation,
                p.Location,
                p.EmploymentType,
                p.TenureFrom,
                p.TenureTill,
                p.DocumentOfProof,
                p.CreatedDate,
                p.ModifiedDate))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<EmployeePreviousEmploymentDto>>.Success(records);
    }

    public async Task<Result<EmployeePreviousEmploymentDto>> CreateAsync(
        Guid employeeId, EmployeePreviousEmploymentRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeePreviousEmploymentDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeePreviousEmploymentDto>.NotFound(NotFoundMessage);
        }

        var record = new EmployeePreviousEmployment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            Company = request.Company.Trim(),
            Designation = Normalize(request.Designation),
            Location = Normalize(request.Location),
            EmploymentType = request.EmploymentType,
            TenureFrom = request.TenureFrom,
            TenureTill = request.TenureTill,
            DocumentOfProof = Normalize(request.DocumentOfProof)
        };

        _db.EmployeePreviousEmployments.Add(record);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created previous employment {PreviousEmploymentId} for employee {EmployeeId} in tenant {TenantId}.",
            record.Id, employeeId, tenantId);

        return Result<EmployeePreviousEmploymentDto>.Success(MapToDto(record), "Previous employment created.");
    }

    public async Task<Result<EmployeePreviousEmploymentDto>> UpdateAsync(
        Guid employeeId, Guid id, EmployeePreviousEmploymentRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeePreviousEmploymentDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeePreviousEmploymentDto>.NotFound(NotFoundMessage);
        }

        var record = await _db.EmployeePreviousEmployments
            .FirstOrDefaultAsync(p => p.Id == id && p.EmployeeId == employeeId, cancellationToken);

        if (record is null)
        {
            return Result<EmployeePreviousEmploymentDto>.NotFound("Previous employment record not found.");
        }

        record.Company = request.Company.Trim();
        record.Designation = Normalize(request.Designation);
        record.Location = Normalize(request.Location);
        record.EmploymentType = request.EmploymentType;
        record.TenureFrom = request.TenureFrom;
        record.TenureTill = request.TenureTill;
        record.DocumentOfProof = Normalize(request.DocumentOfProof);
        record.ModifiedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated previous employment {PreviousEmploymentId} for employee {EmployeeId} in tenant {TenantId}.",
            id, employeeId, tenantId);

        return Result<EmployeePreviousEmploymentDto>.Success(MapToDto(record), "Previous employment updated.");
    }

    public async Task<Result<bool>> DeleteAsync(
        Guid employeeId, Guid id, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<bool>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<bool>.NotFound(NotFoundMessage);
        }

        var record = await _db.EmployeePreviousEmployments
            .FirstOrDefaultAsync(p => p.Id == id && p.EmployeeId == employeeId, cancellationToken);

        if (record is null)
        {
            return Result<bool>.NotFound("Previous employment record not found.");
        }

        _db.EmployeePreviousEmployments.Remove(record);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("Previous employment {PreviousEmploymentId} for employee {EmployeeId} could not be deleted.", id, employeeId);
            return Result<bool>.Conflict("This previous employment record is still referenced and cannot be deleted.");
        }

        _logger.LogInformation("Deleted previous employment {PreviousEmploymentId} for employee {EmployeeId} in tenant {TenantId}.",
            id, employeeId, tenantId);

        return Result<bool>.Success(true, "Previous employment deleted.");
    }

    private async Task<bool> EmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        return await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId, cancellationToken);
    }

    private static EmployeePreviousEmploymentDto MapToDto(EmployeePreviousEmployment p) =>
        new(p.Id, p.EmployeeId, p.Company, p.Designation, p.Location, p.EmploymentType,
            p.TenureFrom, p.TenureTill, p.DocumentOfProof, p.CreatedDate, p.ModifiedDate);

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
