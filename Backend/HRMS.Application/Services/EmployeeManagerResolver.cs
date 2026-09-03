using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System;

namespace HRMS.Application.Services;

public sealed class EmployeeManagerResolver : IEmployeeManagerResolver
{
    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public EmployeeManagerResolver(IHrmsDbContext db, ITenantContext tenantContext, TimeProvider? timeProvider = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<Result<EmployeeManagerResolution>> ResolveAsync(
        Guid employeeId,
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
            return Result<EmployeeManagerResolution>.Unauthorized("No authenticated tenant.");

        var employee = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId && e.TenantId == tenantId)
            .Select(e => new { e.ReportingManagerId })
            .SingleOrDefaultAsync(cancellationToken);
        if (employee is null)
            return Result<EmployeeManagerResolution>.NotFound("Employee not found.");

        var records = await _db.EmployeeEmploymentHistory.AsNoTracking()
            .Where(h => h.TenantId == tenantId && h.EmployeeId == employeeId &&
                        h.EffectiveFrom <= asOfDate &&
                        (h.EffectiveTo == null || h.EffectiveTo >= asOfDate))
            .OrderBy(h => h.EffectiveFrom)
            .ToListAsync(cancellationToken);

        if (records.Count > 1)
            return Result<EmployeeManagerResolution>.Conflict(
                "Employment history contains overlapping records for the requested date.");

        var record = records.SingleOrDefault();
        if (record is null)
            return Result<EmployeeManagerResolution>.Success(new(
                EmployeeManagerResolutionStatus.NoApplicableEmployment, employeeId, null, null, null,
                $"No employment record applies on {asOfDate:yyyy-MM-dd}."));

        var businessDate = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var legacySupervisor = await _db.EmployeeSupervisors.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.EmployeeId == employeeId)
            .FirstOrDefaultAsync(cancellationToken);
        var supervisorManagerId = legacySupervisor?.L1ManagerId;
        if (record.ManagerId is not Guid managerId)
        {
            if (asOfDate == businessDate &&
                (employee.ReportingManagerId is not null || legacySupervisor is not null && supervisorManagerId is not null))
                return Result<EmployeeManagerResolution>.Success(new(
                    EmployeeManagerResolutionStatus.LegacyConflict, employeeId, null, null, null,
                    "The effective employment is explicitly unassigned but a legacy manager assignment remains; reconciliation is required."));

            return Result<EmployeeManagerResolution>.Success(new(
                EmployeeManagerResolutionStatus.NoAssignedManager, employeeId, null, null, null,
                "The effective employment record has no assigned direct manager."));
        }

        if (asOfDate == businessDate &&
            (employee.ReportingManagerId != record.ManagerId || legacySupervisor is not null && supervisorManagerId != record.ManagerId))
            return Result<EmployeeManagerResolution>.Success(new(
                EmployeeManagerResolutionStatus.LegacyConflict, employeeId, record.ManagerId, null, null,
                "The effective employment manager differs from a legacy manager assignment; reconciliation is required."));

        if (managerId == employeeId || !await _db.Employees.AsNoTracking().AnyAsync(
                e => e.Id == managerId && e.TenantId == tenantId, cancellationToken))
            return Result<EmployeeManagerResolution>.Success(new(
                EmployeeManagerResolutionStatus.InvalidManagerReference, employeeId, managerId, null, null,
                "The effective manager reference is missing or belongs to another tenant."));

        var managerHistory = await _db.EmployeeEmploymentHistory.AsNoTracking()
            .Where(h => h.TenantId == tenantId && h.EmployeeId == managerId &&
                        h.EffectiveFrom <= asOfDate &&
                        (h.EffectiveTo == null || h.EffectiveTo >= asOfDate))
            .ToListAsync(cancellationToken);

        if (managerHistory.Count > 1)
            return Result<EmployeeManagerResolution>.Success(new(
                EmployeeManagerResolutionStatus.OverlappingEmployment, employeeId, managerId, null, null,
                "The assigned manager has overlapping employment records for the requested date."));

        if (managerHistory.Count == 0 || managerHistory[0].EmploymentStatus != EmployeeStatus.Active)
            return Result<EmployeeManagerResolution>.Success(new(
                EmployeeManagerResolutionStatus.ManagerNotEligible, employeeId, managerId, null, null,
                "The assigned manager is not actively employed on the requested date."));

        var manager = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == managerId && e.TenantId == tenantId)
            .Select(e => new { e.EmployeeCode, e.FirstName, e.MiddleName, e.LastName })
            .SingleAsync(cancellationToken);

        if (await WouldCreateCycleAsync(employeeId, managerId, asOfDate, cancellationToken))
            return Result<EmployeeManagerResolution>.Success(new(
                EmployeeManagerResolutionStatus.ReportingCycle, employeeId, managerId,
                manager.EmployeeCode, FullName(manager.FirstName, manager.MiddleName, manager.LastName),
                "The effective manager relationship creates a reporting cycle."));

        return Result<EmployeeManagerResolution>.Success(new(
            EmployeeManagerResolutionStatus.Resolved, employeeId, managerId, manager.EmployeeCode,
            FullName(manager.FirstName, manager.MiddleName, manager.LastName), "Direct manager resolved."));
    }

    public async Task<bool> WouldCreateCycleAsync(
        Guid employeeId,
        Guid proposedManagerId,
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        if (_tenantContext.TenantId is not Guid tenantId)
            return true;

        var visited = new HashSet<Guid> { employeeId };
        var cursor = (Guid?)proposedManagerId;

        while (cursor is Guid currentId)
        {
            if (!visited.Add(currentId))
                return true;

            var managerIds = await _db.EmployeeEmploymentHistory.AsNoTracking()
                .Where(h => h.TenantId == tenantId && h.EmployeeId == currentId &&
                            h.EffectiveFrom <= asOfDate &&
                            (h.EffectiveTo == null || h.EffectiveTo >= asOfDate))
                .Select(h => h.ManagerId)
                .ToListAsync(cancellationToken);

            if (managerIds.Count > 1)
                return true;

            cursor = managerIds.SingleOrDefault();
        }

        return false;
    }

    private static string FullName(string first, string? middle, string last) =>
        string.Join(" ", new[] { first, middle, last }.Where(x => !string.IsNullOrWhiteSpace(x)));
}
