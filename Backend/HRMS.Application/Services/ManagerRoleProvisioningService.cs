using HRMS.Application.Abstractions;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

/// <summary>
/// Provisions the canonical Manager role from the authoritative employment relationship.
/// Role assignment is deliberately additive: an existing Manager role may have been granted
/// manually or may be required by another direct report, so it is never removed here.
/// </summary>
public sealed class ManagerRoleProvisioningService : IManagerRoleProvisioningService
{
    private readonly IHrmsDbContext _db;
    private readonly ILogger<ManagerRoleProvisioningService> _logger;
    private readonly TimeProvider _timeProvider;

    public ManagerRoleProvisioningService(
        IHrmsDbContext db,
        ILogger<ManagerRoleProvisioningService> logger,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task EnsureForManagerEmployeeAsync(
        Guid tenantId,
        Guid managerEmployeeId,
        CancellationToken cancellationToken = default)
    {
        var link = await _db.AccountEmployeeCurrentLinks
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == managerEmployeeId, cancellationToken);
        if (link is null)
            return;

        await EnsureForLinkedEmployeeAsync(tenantId, link.UserId, managerEmployeeId, cancellationToken);
    }

    public async Task EnsureForLinkedEmployeeAsync(
        Guid tenantId,
        Guid userId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime);
        var isManager = await _db.EmployeeEmploymentHistory
            .AnyAsync(x => x.TenantId == tenantId && !x.IsSuperseded && x.ManagerId == employeeId &&
                           (x.EffectiveTo == null || x.EffectiveTo >= today), cancellationToken);
        if (!isManager)
            return;

        var managerRole = await _db.Roles
            .SingleOrDefaultAsync(x => x.Name == RoleNames.Manager, cancellationToken);
        if (managerRole is null)
        {
            _logger.LogWarning("Manager role is missing while provisioning tenant {TenantId}.", tenantId);
            return;
        }

        var alreadyAssigned = await _db.UserRoles
            .AnyAsync(x => x.TenantId == tenantId && x.UserId == userId && x.RoleId == managerRole.Id, cancellationToken);
        if (alreadyAssigned)
            return;

        var assignment = new UserRole
        {
            TenantId = tenantId, UserId = userId, RoleId = managerRole.Id,
            EffectiveFrom = today,
            AssignmentSource = RoleAssignmentSource.System,
            AssignmentReason = "Reporting manager assignment",
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
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
