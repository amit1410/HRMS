using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public class EmployeeEducationService : IEmployeeEducationService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Employee not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EmployeeEducationService> _logger;

    public EmployeeEducationService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<EmployeeEducationService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<EmployeeEducationDto>>> GetAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<IReadOnlyList<EmployeeEducationDto>>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<IReadOnlyList<EmployeeEducationDto>>.NotFound(NotFoundMessage);
        }

        var records = await _db.EmployeeEducationRecords.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .OrderByDescending(e => e.YearOfPassing)
            .Select(e => new EmployeeEducationDto(
                e.Id,
                e.EmployeeId,
                e.EducationLevel,
                e.Qualification,
                e.University,
                e.Institute,
                e.EducationType,
                e.AreaOfSpecialization,
                e.YearOfPassing,
                e.Score,
                e.DocumentOfProof,
                e.CreatedDate,
                e.ModifiedDate))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<EmployeeEducationDto>>.Success(records);
    }

    public async Task<Result<EmployeeEducationDto>> CreateAsync(
        Guid employeeId, EmployeeEducationRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeEducationDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeeEducationDto>.NotFound(NotFoundMessage);
        }

        var record = new EmployeeEducation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            EducationLevel = request.EducationLevel.Trim(),
            Qualification = request.Qualification.Trim(),
            University = Normalize(request.University),
            Institute = Normalize(request.Institute),
            EducationType = request.EducationType,
            AreaOfSpecialization = Normalize(request.AreaOfSpecialization),
            YearOfPassing = request.YearOfPassing,
            Score = Normalize(request.Score),
            DocumentOfProof = Normalize(request.DocumentOfProof)
        };

        _db.EmployeeEducationRecords.Add(record);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created education record {EducationId} for employee {EmployeeId} in tenant {TenantId}.",
            record.Id, employeeId, tenantId);

        return Result<EmployeeEducationDto>.Success(MapToDto(record), "Education record created.");
    }

    public async Task<Result<EmployeeEducationDto>> UpdateAsync(
        Guid employeeId, Guid id, EmployeeEducationRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeEducationDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeeEducationDto>.NotFound(NotFoundMessage);
        }

        var record = await _db.EmployeeEducationRecords
            .FirstOrDefaultAsync(e => e.Id == id && e.EmployeeId == employeeId, cancellationToken);

        if (record is null)
        {
            return Result<EmployeeEducationDto>.NotFound("Education record not found.");
        }

        record.EducationLevel = request.EducationLevel.Trim();
        record.Qualification = request.Qualification.Trim();
        record.University = Normalize(request.University);
        record.Institute = Normalize(request.Institute);
        record.EducationType = request.EducationType;
        record.AreaOfSpecialization = Normalize(request.AreaOfSpecialization);
        record.YearOfPassing = request.YearOfPassing;
        record.Score = Normalize(request.Score);
        record.DocumentOfProof = Normalize(request.DocumentOfProof);
        record.ModifiedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated education record {EducationId} for employee {EmployeeId} in tenant {TenantId}.",
            id, employeeId, tenantId);

        return Result<EmployeeEducationDto>.Success(MapToDto(record), "Education record updated.");
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

        var record = await _db.EmployeeEducationRecords
            .FirstOrDefaultAsync(e => e.Id == id && e.EmployeeId == employeeId, cancellationToken);

        if (record is null)
        {
            return Result<bool>.NotFound("Education record not found.");
        }

        _db.EmployeeEducationRecords.Remove(record);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("Education record {EducationId} for employee {EmployeeId} could not be deleted.", id, employeeId);
            return Result<bool>.Conflict("This education record is still referenced and cannot be deleted.");
        }

        _logger.LogInformation("Deleted education record {EducationId} for employee {EmployeeId} in tenant {TenantId}.",
            id, employeeId, tenantId);

        return Result<bool>.Success(true, "Education record deleted.");
    }

    private async Task<bool> EmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        return await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId, cancellationToken);
    }

    private static EmployeeEducationDto MapToDto(EmployeeEducation e) =>
        new(e.Id, e.EmployeeId, e.EducationLevel, e.Qualification, e.University, e.Institute,
            e.EducationType, e.AreaOfSpecialization, e.YearOfPassing, e.Score,
            e.DocumentOfProof, e.CreatedDate, e.ModifiedDate);

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
