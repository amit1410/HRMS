using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public class EmployeeFamilyService : IEmployeeFamilyService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Employee not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EmployeeFamilyService> _logger;

    public EmployeeFamilyService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<EmployeeFamilyService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<EmployeeFamilyDto>>> GetAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<IReadOnlyList<EmployeeFamilyDto>>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<IReadOnlyList<EmployeeFamilyDto>>.NotFound(NotFoundMessage);
        }

        var members = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(f => f.EmployeeId == employeeId)
            .OrderBy(f => f.LastName).ThenBy(f => f.FirstName)
            .Select(f => new EmployeeFamilyDto(
                f.Id,
                f.EmployeeId,
                f.Salutation,
                f.FirstName,
                f.MiddleName,
                f.LastName,
                f.Relationship,
                f.Gender,
                f.DateOfBirth,
                f.BloodGroup,
                f.Nationality,
                f.Occupation,
                f.IsNominee,
                f.IsDependent,
                f.NomineePercentage,
                f.CreatedDate,
                f.ModifiedDate))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<EmployeeFamilyDto>>.Success(members);
    }

    public async Task<Result<EmployeeFamilyDto>> CreateAsync(
        Guid employeeId, EmployeeFamilyRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeFamilyDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeeFamilyDto>.NotFound(NotFoundMessage);
        }

        var member = new EmployeeFamily
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            Salutation = Normalize(request.Salutation),
            FirstName = request.FirstName.Trim(),
            MiddleName = Normalize(request.MiddleName),
            LastName = request.LastName.Trim(),
            Relationship = request.Relationship.Trim(),
            Gender = request.Gender,
            DateOfBirth = request.DateOfBirth,
            BloodGroup = request.BloodGroup,
            Nationality = Normalize(request.Nationality),
            Occupation = Normalize(request.Occupation),
            IsNominee = request.IsNominee,
            IsDependent = request.IsDependent,
            NomineePercentage = request.IsNominee ? request.NomineePercentage : null
        };

        _db.EmployeeFamilyMembers.Add(member);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created family member {FamilyId} for employee {EmployeeId} in tenant {TenantId}.",
            member.Id, employeeId, tenantId);

        return Result<EmployeeFamilyDto>.Success(MapToDto(member), "Family member created.");
    }

    public async Task<Result<EmployeeFamilyDto>> UpdateAsync(
        Guid employeeId, Guid id, EmployeeFamilyRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeFamilyDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeeFamilyDto>.NotFound(NotFoundMessage);
        }

        var member = await _db.EmployeeFamilyMembers
            .FirstOrDefaultAsync(f => f.Id == id && f.EmployeeId == employeeId, cancellationToken);

        if (member is null)
        {
            return Result<EmployeeFamilyDto>.NotFound("Family member not found.");
        }

        member.Salutation = Normalize(request.Salutation);
        member.FirstName = request.FirstName.Trim();
        member.MiddleName = Normalize(request.MiddleName);
        member.LastName = request.LastName.Trim();
        member.Relationship = request.Relationship.Trim();
        member.Gender = request.Gender;
        member.DateOfBirth = request.DateOfBirth;
        member.BloodGroup = request.BloodGroup;
        member.Nationality = Normalize(request.Nationality);
        member.Occupation = Normalize(request.Occupation);
        member.IsNominee = request.IsNominee;
        member.IsDependent = request.IsDependent;
        member.NomineePercentage = request.IsNominee ? request.NomineePercentage : null;
        member.ModifiedDate = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated family member {FamilyId} for employee {EmployeeId} in tenant {TenantId}.",
            id, employeeId, tenantId);

        return Result<EmployeeFamilyDto>.Success(MapToDto(member), "Family member updated.");
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

        var member = await _db.EmployeeFamilyMembers
            .FirstOrDefaultAsync(f => f.Id == id && f.EmployeeId == employeeId, cancellationToken);

        if (member is null)
        {
            return Result<bool>.NotFound("Family member not found.");
        }

        _db.EmployeeFamilyMembers.Remove(member);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("Family member {FamilyId} for employee {EmployeeId} could not be deleted.", id, employeeId);
            return Result<bool>.Conflict("This family member record is still referenced and cannot be deleted.");
        }

        _logger.LogInformation("Deleted family member {FamilyId} for employee {EmployeeId} in tenant {TenantId}.",
            id, employeeId, tenantId);

        return Result<bool>.Success(true, "Family member deleted.");
    }

    private async Task<bool> EmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        return await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId, cancellationToken);
    }

    private static EmployeeFamilyDto MapToDto(EmployeeFamily f) =>
        new(f.Id, f.EmployeeId, f.Salutation, f.FirstName, f.MiddleName, f.LastName,
            f.Relationship, f.Gender, f.DateOfBirth, f.BloodGroup, f.Nationality,
            f.Occupation, f.IsNominee, f.IsDependent, f.NomineePercentage, f.CreatedDate, f.ModifiedDate);

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
