using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public class EmployeeContactService : IEmployeeContactService
{
    private const string NoTenantMessage = "No authenticated tenant.";
    private const string NotFoundMessage = "Employee not found.";
    private const string PlaceholderEmailSuffix = "@placeholder.local";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EmployeeContactService> _logger;

    public EmployeeContactService(
        IHrmsDbContext db,
        ITenantContext tenantContext,
        ILogger<EmployeeContactService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Result<EmployeeContactDto>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is null)
        {
            return Result<EmployeeContactDto>.Unauthorized(NoTenantMessage);
        }

        var employee = await _db.Employees.AsNoTracking()
            .Include(e => e.Contact)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        return employee is null
            ? Result<EmployeeContactDto>.NotFound(NotFoundMessage)
            : Result<EmployeeContactDto>.Success(ToDto(employee));
    }

    public async Task<Result<EmployeeContactDto>> UpsertAsync(
        Guid employeeId, EmployeeContactRequest request, CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
        {
            return Result<EmployeeContactDto>.Unauthorized(NoTenantMessage);
        }

        var employee = await _db.Employees
            .Include(e => e.Contact)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            return Result<EmployeeContactDto>.NotFound(NotFoundMessage);
        }

        var existing = employee.Contact;
        var officialEmail = Normalize(request.OfficialEmail)
            ?? existing?.OfficialEmail
            ?? GetRealEmployeeEmail(employee.Email);

        if (officialEmail is not null && await OfficialEmailIsInUseAsync(employeeId, officialEmail, cancellationToken))
        {
            return OfficialEmailConflict();
        }

        // A legacy employee can pre-date the Contact row. Preserve its current phone when another section
        // (notably Address) creates the row without supplying contact fields. Once the row exists, an
        // explicit null remains a valid way for the Contact section to clear an optional official phone.
        var officialPhone = Normalize(request.OfficialPhone);
        if (existing is null)
        {
            officialPhone ??= Normalize(employee.Phone);
        }

        if (existing is null)
        {
            var contact = new EmployeeContact
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EmployeeId = employeeId,
                OfficialEmail = officialEmail,
                PersonalEmail = Normalize(request.PersonalEmail),
                AlternateEmail = Normalize(request.AlternateEmail),
                OfficialPhone = officialPhone,
                PersonalPhone = Normalize(request.PersonalPhone),
                EmergencyNumber = Normalize(request.EmergencyNumber),
                SameAsCurrentAddress = request.SameAsCurrentAddress
            };

            _db.EmployeeContacts.Add(contact);
        }
        else
        {
            existing.OfficialEmail = officialEmail;
            existing.PersonalEmail = Normalize(request.PersonalEmail);
            existing.AlternateEmail = Normalize(request.AlternateEmail);
            existing.OfficialPhone = officialPhone;
            existing.PersonalPhone = Normalize(request.PersonalPhone);
            existing.EmergencyNumber = Normalize(request.EmergencyNumber);
            existing.SameAsCurrentAddress = request.SameAsCurrentAddress;
            existing.ModifiedDate = DateTime.UtcNow;
        }

        // Employee.Email/Phone are the established compatibility projection used by list, search, export,
        // and legacy Add/Edit flows. Contact remains the editable source and updates both values atomically.
        if (officialEmail is not null)
        {
            employee.Email = officialEmail;
        }

        employee.Phone = officialPhone;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (officialEmail is null ||
                !await OfficialEmailIsInUseAsync(employeeId, officialEmail, cancellationToken))
            {
                throw;
            }

            _logger.LogWarning(
                "Contact update for employee {EmployeeId} lost an official-email uniqueness race.", employeeId);
            return OfficialEmailConflict();
        }

        _logger.LogInformation("Upserted contact for employee {EmployeeId} in tenant {TenantId}.", employeeId, tenantId);

        var saved = await _db.Employees.AsNoTracking()
            .Include(e => e.Contact)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        return saved is null
            ? Result<EmployeeContactDto>.NotFound("Contact record not found after save.")
            : Result<EmployeeContactDto>.Success(ToDto(saved), "Contact updated.");
    }

    private async Task<bool> OfficialEmailIsInUseAsync(
        Guid employeeId, string officialEmail, CancellationToken cancellationToken)
    {
        var normalizedEmail = officialEmail.ToLower();
        return await _db.Employees.AsNoTracking()
            .AnyAsync(e => e.Id != employeeId && e.Email.ToLower() == normalizedEmail, cancellationToken);
    }

    private static Result<EmployeeContactDto> OfficialEmailConflict() =>
        Result<EmployeeContactDto>.Conflict(
            "An employee with that official email already exists.",
            [new ValidationError("officialEmail", "Official email must be unique within the tenant.")]);

    private static EmployeeContactDto ToDto(Employee employee)
    {
        var contact = employee.Contact;
        return new EmployeeContactDto(
            contact?.Id ?? Guid.Empty,
            employee.Id,
            contact?.OfficialEmail ?? GetRealEmployeeEmail(employee.Email),
            contact?.PersonalEmail,
            contact?.AlternateEmail,
            contact?.OfficialPhone ?? employee.Phone,
            contact?.PersonalPhone,
            contact?.EmergencyNumber,
            contact?.SameAsCurrentAddress ?? false,
            contact?.CreatedDate ?? employee.CreatedDate,
            contact?.ModifiedDate);
    }

    private static string? GetRealEmployeeEmail(string email) =>
        email.EndsWith(PlaceholderEmailSuffix, StringComparison.OrdinalIgnoreCase) ? null : email;

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
