using HRMS.Application.Abstractions;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

/// <summary>Provisions the canonical Manager role from effective employment history.</summary>
public sealed class ManagerRoleProvisioningService : IManagerRoleProvisioningService
{
    private readonly IHrmsDbContext _db;
    private readonly ILogger<ManagerRoleProvisioningService> _logger;
    private readonly TimeProvider _timeProvider;

    public ManagerRoleProvisioningService(IHrmsDbContext db, ILogger<ManagerRoleProvisioningService> logger, TimeProvider? timeProvider = null)
    {
        _db = db;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task EnsureForManagerEmployeeAsync(Guid tenantId, Guid managerEmployeeId, CancellationToken cancellationToken = default)
        => await ReconcileManagerAccessAsync(tenantId, managerEmployeeId, cancellationToken);

    public async Task<ManagerAccessReconciliationResult> ReconcileManagerAccessAsync(Guid tenantId, Guid managerEmployeeId, CancellationToken cancellationToken = default)
    {
        var link = await _db.AccountEmployeeCurrentLinks.SingleOrDefaultAsync(
            x => x.TenantId == tenantId && x.EmployeeId == managerEmployeeId, cancellationToken);
        if (link is null)
            return new(false, false, false, false);

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var managerIsActive = await _db.EmployeeEmploymentHistory.AnyAsync(x =>
            x.TenantId == tenantId && x.EmployeeId == managerEmployeeId && !x.IsSuperseded &&
            x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today) &&
            x.EmploymentStatus == EmployeeStatus.Active, cancellationToken);
        var hasEffectiveSubordinate = await _db.EmployeeEmploymentHistory.AnyAsync(x =>
            x.TenantId == tenantId && !x.IsSuperseded && x.ManagerId == managerEmployeeId &&
            x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today) &&
            x.EmploymentStatus == EmployeeStatus.Active, cancellationToken);
        if (!managerIsActive || !hasEffectiveSubordinate)
            return new(false, true, false, false);

        var managerRole = await _db.Roles.SingleOrDefaultAsync(x => x.Name == RoleNames.Manager, cancellationToken);
        if (managerRole is null)
        {
            _logger.LogWarning("Manager role is missing while reconciling tenant {TenantId}.", tenantId);
            return new(true, true, false, false);
        }

        var alreadyAssigned = await _db.UserRoles.AnyAsync(x =>
            x.TenantId == tenantId && x.UserId == link.UserId && x.RoleId == managerRole.Id &&
            x.AssignmentSource == RoleAssignmentSource.System &&
            x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today), cancellationToken);
        if (alreadyAssigned)
            return new(true, true, true, false);

        var assignment = new UserRole
        {
            TenantId = tenantId, UserId = link.UserId, RoleId = managerRole.Id,
            EffectiveFrom = today, AssignmentSource = RoleAssignmentSource.System,
            AssignmentReason = "Reporting manager assignment reconciliation",
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };
        _db.UserRoles.Add(assignment);
        _db.UserRoleAssignmentEvents.Add(new UserRoleAssignmentEvent
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AssignmentId = assignment.Id,
            UserId = link.UserId, RoleId = managerRole.Id, EventType = UserRoleAssignmentEventType.Assigned,
            EffectiveFrom = assignment.EffectiveFrom, AssignmentSource = assignment.AssignmentSource,
            Reason = assignment.AssignmentReason, OccurredAtUtc = assignment.CreatedAtUtc
        });
        return new(true, true, true, true);
    }

    public async Task EnsureForLinkedEmployeeAsync(Guid tenantId, Guid userId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var managerIsActive = await _db.EmployeeEmploymentHistory.AnyAsync(x =>
            x.TenantId == tenantId && x.EmployeeId == employeeId && !x.IsSuperseded &&
            x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today) &&
            x.EmploymentStatus == EmployeeStatus.Active, cancellationToken);
        var isManager = await _db.EmployeeEmploymentHistory.AnyAsync(x =>
            x.TenantId == tenantId && !x.IsSuperseded && x.ManagerId == employeeId &&
            x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today) &&
            x.EmploymentStatus == EmployeeStatus.Active, cancellationToken);
        if (!managerIsActive || !isManager)
            return;

        var managerRole = await _db.Roles.SingleOrDefaultAsync(x => x.Name == RoleNames.Manager, cancellationToken);
        if (managerRole is null)
        {
            _logger.LogWarning("Manager role is missing while provisioning tenant {TenantId}.", tenantId);
            return;
        }
        if (await _db.UserRoles.AnyAsync(x => x.TenantId == tenantId && x.UserId == userId && x.RoleId == managerRole.Id, cancellationToken))
            return;

        var assignment = new UserRole
        {
            TenantId = tenantId, UserId = userId, RoleId = managerRole.Id,
            EffectiveFrom = today, AssignmentSource = RoleAssignmentSource.System,
            AssignmentReason = "Reporting manager assignment", CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };
        _db.UserRoles.Add(assignment);
        _db.UserRoleAssignmentEvents.Add(new UserRoleAssignmentEvent
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AssignmentId = assignment.Id,
            UserId = userId, RoleId = managerRole.Id, EventType = UserRoleAssignmentEventType.Assigned,
            EffectiveFrom = assignment.EffectiveFrom, AssignmentSource = assignment.AssignmentSource,
            Reason = assignment.AssignmentReason, OccurredAtUtc = assignment.CreatedAtUtc
        });
    }
}
