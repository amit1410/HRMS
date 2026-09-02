using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public class EmployeeAdditionalInfoService : IEmployeeAdditionalInfoService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Employee not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EmployeeAdditionalInfoService> _logger;

    public EmployeeAdditionalInfoService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<EmployeeAdditionalInfoService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<EmployeeAdditionalInfoDto>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<EmployeeAdditionalInfoDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeeAdditionalInfoDto>.NotFound(NotFoundMessage);
        }

        var info = await _db.EmployeeAdditionalInfo.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .Select(a => new EmployeeAdditionalInfoDto(
                a.Id,
                a.EmployeeId,
                a.Division,
                a.PaPsa,
                a.AdditionalEmployeeCode,
                a.ContractId,
                a.CreatedDate,
                a.ModifiedDate))
            .FirstOrDefaultAsync(cancellationToken);

        return info is null
            ? Result<EmployeeAdditionalInfoDto>.NotFound("Additional info record not found for this employee.")
            : Result<EmployeeAdditionalInfoDto>.Success(info);
    }

    public async Task<Result<EmployeeAdditionalInfoDto>> UpsertAsync(
        Guid employeeId, EmployeeAdditionalInfoRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeAdditionalInfoDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeeAdditionalInfoDto>.NotFound(NotFoundMessage);
        }

        var existing = await _db.EmployeeAdditionalInfo
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId, cancellationToken);

        if (existing is null)
        {
            var info = new EmployeeAdditionalInfo
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                Division = Normalize(request.Division),
                PaPsa = Normalize(request.PaPsa),
                AdditionalEmployeeCode = Normalize(request.AdditionalEmployeeCode),
                ContractId = Normalize(request.ContractId)
            };

            _db.EmployeeAdditionalInfo.Add(info);
        }
        else
        {
            existing.Division = Normalize(request.Division);
            existing.PaPsa = Normalize(request.PaPsa);
            existing.AdditionalEmployeeCode = Normalize(request.AdditionalEmployeeCode);
            existing.ContractId = Normalize(request.ContractId);
            existing.ModifiedDate = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Upserted additional info for employee {EmployeeId} in tenant {TenantId}.", employeeId, tenantId);

        var saved = await _db.EmployeeAdditionalInfo.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .Select(a => new EmployeeAdditionalInfoDto(
                a.Id,
                a.EmployeeId,
                a.Division,
                a.PaPsa,
                a.AdditionalEmployeeCode,
                a.ContractId,
                a.CreatedDate,
                a.ModifiedDate))
            .FirstOrDefaultAsync(cancellationToken);

        return saved is null
            ? Result<EmployeeAdditionalInfoDto>.NotFound("Additional info record not found after save.")
            : Result<EmployeeAdditionalInfoDto>.Success(saved, "Additional info updated.");
    }

    private async Task<bool> EmployeeExistsAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        return await _db.Employees.AsNoTracking().AnyAsync(e => e.Id == employeeId, cancellationToken);
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
