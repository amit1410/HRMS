using HRMS.Application.Abstractions;
using HRMS.Domain.Authorization;

namespace HRMS.Application.Services;

/// <summary>
/// Narrow worker identity for the onboarding path. It does not authorize HTTP requests and is activated
/// only after the worker has atomically claimed a durably accepted tenant operation.
/// </summary>
public sealed class EmployeeImportWorkerExecutionContext : IEmployeeImportWorkerExecutionContext
{
    private static readonly HashSet<string> WorkerPermissions =
    [
        Permissions.Employee.Create,
        Permissions.EmployeeSensitive.Edit,
        Permissions.EmploymentHistory.Change
    ];

    public Guid? UserId { get; private set; }
    public bool IsActive => UserId is Guid id && id != Guid.Empty;

    public void Activate(Guid initiatingUserId)
    {
        if (initiatingUserId == Guid.Empty) throw new ArgumentException("Initiating user identity is required.", nameof(initiatingUserId));
        if (IsActive) throw new InvalidOperationException("Worker identity is already active in this scope.");
        UserId = initiatingUserId;
    }

    public void Deactivate() => UserId = null;

    public bool HasAnyPermission(params string[] permissions) => IsActive && permissions.Any(WorkerPermissions.Contains);
}
