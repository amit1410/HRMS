using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class EffectiveEmploymentResolver : IEffectiveEmploymentResolver
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext? _tenantContext;

    public EffectiveEmploymentResolver(IHrmsDbContext db, ITenantContext? tenantContext = null)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<EffectiveEmploymentResolutionResult> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || employeeId == Guid.Empty)
            return Failure(EffectiveEmploymentResolutionStatus.InvalidTenant, tenantId, employeeId, effectiveDate, "A valid tenant and Employee are required.");
        if (_tenantContext?.TenantId is Guid currentTenantId && currentTenantId != tenantId)
            return Failure(EffectiveEmploymentResolutionStatus.InvalidTenant, tenantId, employeeId, effectiveDate, "The requested tenant is not the authenticated tenant.");

        var employee = await _db.Employees.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == employeeId, cancellationToken);
        if (employee is null)
            return Failure(EffectiveEmploymentResolutionStatus.InvalidTenant, tenantId, employeeId, effectiveDate, "Employee was not found in the tenant.");

        var records = await _db.EmployeeEmploymentHistory.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId &&
                        x.EffectiveFrom <= effectiveDate &&
                        (x.EffectiveTo == null || effectiveDate <= x.EffectiveTo))
            .ToListAsync(cancellationToken);

        return records.Count switch
        {
            0 => Failure(EffectiveEmploymentResolutionStatus.NotFound, tenantId, employeeId, effectiveDate, "No employment history record applies on the effective date."),
            1 => new(EffectiveEmploymentResolutionStatus.Resolved, tenantId, employeeId, effectiveDate, ToSnapshot(records[0], employee), "A unique effective employment record was resolved."),
            _ => Failure(EffectiveEmploymentResolutionStatus.ConfigurationAmbiguity, tenantId, employeeId, effectiveDate, "Multiple employment history records apply on the effective date.")
        };
    }

    private static EffectiveEmploymentSnapshot ToSnapshot(EmployeeEmploymentHistory history, Employee employee) =>
        new(
            history.Id,
            history.TenantId,
            history.EmployeeId,
            history.EffectiveFrom,
            history.EffectiveTo,
            history.HoldingCompanyId,
            history.LobId,
            history.OrganisationId,
            history.DepartmentId,
            history.SubDepartmentId,
            history.SectionId,
            history.SubSectionId,
            history.FunctionId,
            history.SubFunctionId,
            history.GradeId,
            history.DesignationId,
            history.EmployeeTypeId,
            history.CountryLocationId,
            history.WorkLocationId,
            history.CostCenterId,
            history.ManagerId,
            history.EmploymentType,
            history.EmploymentStatus,
            employee.DateOfJoining,
            employee.GroupDateOfJoining,
            employee.DateOfLeaving,
            employee.Gender);

    private static EffectiveEmploymentResolutionResult Failure(
        EffectiveEmploymentResolutionStatus status,
        Guid tenantId,
        Guid employeeId,
        DateOnly effectiveDate,
        string message) => new(status, tenantId, employeeId, effectiveDate, null, message);
}
