using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public class EmployeeAddressService : IEmployeeAddressService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Employee not found.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EmployeeAddressService> _logger;

    public EmployeeAddressService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<EmployeeAddressService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<EmployeeAddressDto>>> GetAsync(
        Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<IReadOnlyList<EmployeeAddressDto>>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<IReadOnlyList<EmployeeAddressDto>>.NotFound(NotFoundMessage);
        }

        var addresses = await _db.EmployeeAddresses.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .OrderBy(a => a.AddressType)
            .Select(a => new EmployeeAddressDto(
                a.Id,
                a.EmployeeId,
                a.AddressType,
                a.Country,
                a.State,
                a.District,
                a.City,
                a.ZipCode,
                a.AddressLine1,
                a.AddressLine2,
                a.HouseNumber,
                a.CreatedDate,
                a.ModifiedDate))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<EmployeeAddressDto>>.Success(addresses);
    }

    public async Task<Result<EmployeeAddressDto>> UpsertAsync(
        Guid employeeId, EmployeeAddressRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeAddressDto>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<EmployeeAddressDto>.NotFound(NotFoundMessage);
        }

        var existing = await _db.EmployeeAddresses
            .FirstOrDefaultAsync(a => a.EmployeeId == employeeId && a.AddressType == request.AddressType, cancellationToken);

        if (existing is null)
        {
            var address = new EmployeeAddress
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                AddressType = request.AddressType,
                Country = Normalize(request.Country),
                State = Normalize(request.State),
                District = Normalize(request.District),
                City = Normalize(request.City),
                ZipCode = Normalize(request.ZipCode),
                AddressLine1 = Normalize(request.AddressLine1),
                AddressLine2 = Normalize(request.AddressLine2),
                HouseNumber = Normalize(request.HouseNumber)
            };

            _db.EmployeeAddresses.Add(address);
        }
        else
        {
            existing.Country = Normalize(request.Country);
            existing.State = Normalize(request.State);
            existing.District = Normalize(request.District);
            existing.City = Normalize(request.City);
            existing.ZipCode = Normalize(request.ZipCode);
            existing.AddressLine1 = Normalize(request.AddressLine1);
            existing.AddressLine2 = Normalize(request.AddressLine2);
            existing.HouseNumber = Normalize(request.HouseNumber);
            existing.ModifiedDate = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Upserted {AddressType} address for employee {EmployeeId} in tenant {TenantId}.",
            request.AddressType, employeeId, tenantId);

        var saved = await _db.EmployeeAddresses.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.AddressType == request.AddressType)
            .Select(a => new EmployeeAddressDto(
                a.Id,
                a.EmployeeId,
                a.AddressType,
                a.Country,
                a.State,
                a.District,
                a.City,
                a.ZipCode,
                a.AddressLine1,
                a.AddressLine2,
                a.HouseNumber,
                a.CreatedDate,
                a.ModifiedDate))
            .FirstOrDefaultAsync(cancellationToken);

        return saved is null
            ? Result<EmployeeAddressDto>.NotFound("Address not found after save.")
            : Result<EmployeeAddressDto>.Success(saved, "Address updated.");
    }

    public async Task<Result<bool>> DeleteAsync(
        Guid employeeId, Guid addressId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<bool>.Unauthorized(NoTenantMessage);
        }

        if (!await EmployeeExistsAsync(employeeId, cancellationToken))
        {
            return Result<bool>.NotFound(NotFoundMessage);
        }

        var address = await _db.EmployeeAddresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.EmployeeId == employeeId, cancellationToken);

        if (address is null)
        {
            return Result<bool>.NotFound("Address not found.");
        }

        _db.EmployeeAddresses.Remove(address);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _logger.LogWarning("Address {AddressId} for employee {EmployeeId} could not be deleted.", addressId, employeeId);
            return Result<bool>.Conflict("This address record is still referenced and cannot be deleted.");
        }

        _logger.LogInformation("Deleted address {AddressId} for employee {EmployeeId} in tenant {TenantId}.",
            addressId, employeeId, tenantId);

        return Result<bool>.Success(true, "Address deleted.");
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
