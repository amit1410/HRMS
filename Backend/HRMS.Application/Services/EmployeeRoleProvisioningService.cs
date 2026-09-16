using HRMS.Application.Abstractions;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public sealed class EmployeeRoleProvisioningService : IEmployeeRoleProvisioningService
{
    private readonly IHrmsDbContext _db;
    private readonly ILogger<EmployeeRoleProvisioningService> _logger;

    public EmployeeRoleProvisioningService(IHrmsDbContext db, ILogger<EmployeeRoleProvisioningService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task EnsureForLinkedEmployeeAsync(
        Guid tenantId,
        Guid userId,
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        var roleId = await _db.Roles
            .Where(x => x.Name == RoleNames.Employee)
            .Select(x => (int?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (roleId is null)
        {
            _logger.LogWarning("Employee role is missing while provisioning tenant {TenantId}.", tenantId);
            return;
        }

        if (await _db.UserRoles.AnyAsync(
                x => x.TenantId == tenantId && x.UserId == userId && x.RoleId == roleId.Value,
                cancellationToken))
            return;

        var assignment = new UserRole
        {
            TenantId = tenantId, UserId = userId, RoleId = roleId.Value,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
            AssignmentSource = RoleAssignmentSource.System,
            AssignmentReason = "Employee account link",
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.UserRoles.Add(assignment);
        _db.UserRoleAssignmentEvents.Add(new UserRoleAssignmentEvent
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AssignmentId = assignment.Id,
            UserId = userId, RoleId = roleId.Value, EventType = UserRoleAssignmentEventType.Assigned,
            EffectiveFrom = assignment.EffectiveFrom, AssignmentSource = assignment.AssignmentSource,
            Reason = assignment.AssignmentReason, OccurredAtUtc = assignment.CreatedAtUtc
        });
    }
}
