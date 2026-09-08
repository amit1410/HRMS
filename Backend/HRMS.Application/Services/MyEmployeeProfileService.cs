using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class MyEmployeeProfileService : IMyEmployeeProfileService
{
    private readonly IHrmsDbContext _db;
    private readonly IEmployeeIdentityResolver _identity;
    private readonly IEffectiveEmploymentResolver _employmentResolver;
    private readonly TimeProvider _clock;

    public MyEmployeeProfileService(IHrmsDbContext db, IEmployeeIdentityResolver identity,
        IEffectiveEmploymentResolver employmentResolver, TimeProvider clock)
    {
        _db = db;
        _identity = identity;
        _employmentResolver = employmentResolver;
        _clock = clock;
    }

    public async Task<Result<MyEmployeeProfileDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        var identity = await _identity.ResolveCurrentAsync(cancellationToken);
        if (!identity.Succeeded) return Result<MyEmployeeProfileDto>.Failure(identity.Status, identity.Message);
        var subject = identity.Value!;

        var employee = await _db.Employees.AsNoTracking()
            .Include(e => e.Contact).Include(e => e.Addresses)
            .SingleOrDefaultAsync(e => e.TenantId == subject.TenantId && e.Id == subject.EmployeeId, cancellationToken);
        if (employee is null) return Result<MyEmployeeProfileDto>.NotFound("No employee profile is linked to this account.");

        var today = DateOnly.FromDateTime(_clock.GetUtcNow().DateTime);
        var resolved = await _employmentResolver.ResolveAsync(subject.TenantId, employee.Id, today, cancellationToken);
        if (resolved.Status == EffectiveEmploymentResolutionStatus.ConfigurationAmbiguity)
            return Result<MyEmployeeProfileDto>.Conflict("Employment history contains multiple current records.");

        MyCurrentEmploymentDto? current = null;
        if (resolved.Employment is not null)
        {
            var h = await _db.EmployeeEmploymentHistory.AsNoTracking()
                .Include(x => x.HoldingCompany).Include(x => x.Lob).Include(x => x.Organisation)
                .Include(x => x.Department).Include(x => x.SubDepartment).Include(x => x.Section)
                .Include(x => x.SubSection).Include(x => x.Function).Include(x => x.SubFunction)
                .Include(x => x.Grade).Include(x => x.Designation).Include(x => x.EmployeeType)
                .Include(x => x.CountryLocation).Include(x => x.WorkLocation).Include(x => x.CostCenter)
                .Include(x => x.Manager)
                .SingleAsync(x => x.TenantId == subject.TenantId && x.Id == resolved.Employment.HistoryId, cancellationToken);
            current = new(h.HoldingCompany?.Name, h.Lob?.Name, h.Organisation?.Name, h.Department?.Name,
                h.SubDepartment?.Name, h.Section?.Name, h.SubSection?.Name, h.Function?.Name,
                h.SubFunction?.Name, h.Grade?.Name, h.Designation?.Name, h.EmployeeType?.Name,
                h.CountryLocation?.Name, h.WorkLocation?.Name,
                h.CostCenter?.Code, h.EffectiveFrom, h.ManagerName ?? (h.Manager == null ? null : h.Manager.FirstName + " " + h.Manager.LastName),
                h.EmploymentType, h.EmploymentStatus);
        }

        var bankRows = await (from b in _db.EmployeeBankDetails.AsNoTracking()
                           join bank in _db.Banks.AsNoTracking() on b.BankId equals bank.Id into bankJoin
                           from bank in bankJoin.DefaultIfEmpty()
                           where b.TenantId == subject.TenantId && b.EmployeeId == employee.Id && b.IsActive && b.Status == BankAccountStatus.Active
                           orderby b.EffectiveFrom descending, b.CreatedDate descending
                           select new { b, BankName = bank == null ? string.Empty : bank.Name }).ToListAsync(cancellationToken);
        var banks = bankRows.Select(x => new MyEmployeeBankDto(x.BankName,
            SensitiveDataMasker.AccountNumber(x.b.AccountNumber), SensitiveDataMasker.Ifsc(x.b.IfscCode), x.b.BranchName,
            x.b.AccountType, x.b.EffectiveFrom)).ToList();

        var address = employee.Addresses.ToDictionary(x => x.AddressType);
        return Result<MyEmployeeProfileDto>.Success(new(
            employee.EmployeeCode, employee.Salutation, employee.FirstName, employee.MiddleName, employee.LastName,
            string.Join(" ", new[] { employee.FirstName, employee.MiddleName, employee.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
            employee.Gender, employee.DateOfBirth, employee.BloodGroup, employee.MaritalStatus, employee.Citizenship,
            employee.BirthCountry, employee.BirthState, employee.BirthCity, employee.Religion, employee.Caste,
            SensitiveDataMasker.Aadhaar(employee.AadhaarNumber), SensitiveDataMasker.Pan(employee.PanNumber),
            SensitiveDataMasker.Identifier(employee.UanNumber), SensitiveDataMasker.Identifier(employee.PfNumber),
            SensitiveDataMasker.Identifier(employee.EsicNumber), SensitiveDataMasker.Identifier(employee.MediclaimNumber),
            employee.EsicApplicable, employee.Gratuity, employee.Pension, employee.DateOfJoining, employee.GroupDateOfJoining,
            employee.EmployeeType, employee.JobStatus, current?.EmploymentStatus ?? employee.Status, employee.GroupId,
            employee.PayrollLocation, employee.CostCenterCode, employee.ProfilePictureUrl,
            new(employee.Contact?.OfficialEmail ?? employee.Email, employee.Contact?.OfficialPhone ?? employee.Phone),
            MapAddress(address.GetValueOrDefault(AddressType.Current)), MapAddress(address.GetValueOrDefault(AddressType.Permanent)), current, banks));
    }

    private static MyEmployeeAddressDto? MapAddress(HRMS.Domain.Entities.EmployeeAddress? a) => a is null ? null :
        new(a.Country, a.State, a.District, a.City, a.ZipCode, a.AddressLine1, a.AddressLine2, a.HouseNumber);
}
