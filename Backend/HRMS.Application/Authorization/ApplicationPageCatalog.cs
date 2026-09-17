using HRMS.Domain.Authorization;

namespace HRMS.Application.Authorization;

public sealed record ApplicationPageAction(string Code, string Name, string Permission);

public sealed record ApplicationPageDefinition(
    string Code,
    string Name,
    string ModuleCode,
    string Route,
    IReadOnlyList<string> RequiredPermissions,
    IReadOnlyList<ApplicationPageAction> Actions);

/// <summary>
/// Stable page identifiers used by administration and navigation. Permission grants remain the
/// authorization source of truth; this catalog only gives administrators a page-oriented view.
/// </summary>
public static class ApplicationPageCatalog
{
    public static IReadOnlyList<ApplicationPageDefinition> All { get; } =
    [
        Page("DASHBOARD", "Dashboard", "DASHBOARD", "/dashboard", [Permissions.Employee.View]),
        Page("EMPLOYEE.LIST", "Employees", "EMPLOYEE", "/employees", [Permissions.Employee.View],
            Action("VIEW", "View", Permissions.Employee.View), Action("CREATE", "Create", Permissions.Employee.Create),
            Action("EDIT", "Edit", Permissions.Employee.Edit), Action("EXPORT", "Export", Permissions.Employee.Export)),
        Page("EMPLOYEE.DETAIL", "Employee Details", "EMPLOYEE", "/employees", [Permissions.Employee.View],
            Action("VIEW", "View", Permissions.Employee.View), Action("EDIT", "Edit", Permissions.Employee.Edit),
            Action("EMPLOYMENT", "Edit employment", Permissions.EmploymentHistory.Change)),
        Page("LEAVE.DASHBOARD", "Leave Dashboard", "LEAVE", "/leave-management", [Permissions.Leave.RequestViewOwn, Permissions.Leave.DashboardViewAll, Permissions.Leave.Approve]),
        Page("LEAVE.INBOX", "Leave Approvals", "LEAVE", "/leave-management/approvals", [Permissions.Leave.Approve],
            Action("VIEW", "View", Permissions.Leave.Approve), Action("APPROVE", "Approve", Permissions.Leave.Approve)),
        Page("ATTENDANCE.DASHBOARD", "Attendance", "ATTENDANCE", "/attendance", [Permissions.Attendance.View]),
        Page("REPORTS.LEAVE", "Leave Reports", "REPORTS", "/leave-management/reports", [Permissions.Leave.ReportsView],
            Action("VIEW", "View", Permissions.Leave.ReportsView), Action("EXPORT", "Export", Permissions.Leave.ReportsExport)),
        Page("ADMIN.ROLE_MANAGEMENT", "Role Management", "ADMINISTRATION", "/role-management", [Permissions.RoleManagement.AssignmentView]),
        Page("ADMIN.PAGE_ACCESS", "Page Access Management", "ADMINISTRATION", "/page-access-management", [Permissions.PageAccess.View],
            Action("VIEW", "View", Permissions.PageAccess.View), Action("MANAGE", "Manage", Permissions.PageAccess.Manage)),
        Page("ADMIN.ACCOUNT_EMPLOYEE_LINKS", "Account–Employee Links", "ADMINISTRATION", "/administration/account-employee-links", [Permissions.AccountEmployeeLink.View])
    ];

    private static ApplicationPageDefinition Page(string code, string name, string module, string route,
        IReadOnlyList<string> permissions, params ApplicationPageAction[] actions) =>
        new(code, name, module, route, permissions, actions);

    private static ApplicationPageAction Action(string code, string name, string permission) => new(code, name, permission);
}
