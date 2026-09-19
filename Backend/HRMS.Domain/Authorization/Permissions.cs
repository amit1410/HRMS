namespace HRMS.Domain.Authorization;

/// <summary>
/// Canonical permission names in "Resource.Action" form. These are seeded into the Permission table,
/// granted to roles via RolePermission, and (from Phase 2) emitted as JWT claims so the API can
/// authorize by permission instead of hard-coded role checks.
/// </summary>
public static class Permissions
{
    public static class Employee
    {
        public const string View = "Employee.View";
        public const string Create = "Employee.Create";
        public const string Edit = "Employee.Edit";
        public const string Delete = "Employee.Delete";
        public const string Export = "Employee.Export";
        public const string Import = "Employee.Import";
    }

    public static class EmployeeSensitive
    {
        public const string View = "EmployeeSensitive.View";
        public const string Edit = "EmployeeSensitive.Edit";
    }

    public static class Geography
    {
        public const string View = "Geography.View";
        public const string Manage = "Geography.Manage";
    }


    public static class EmploymentHistory
    {
        public const string View = "EmploymentHistory.View";
        public const string Change = "EmploymentHistory.Change";
    }

    public static class EmployeeCodeConfiguration
    {
        public const string View = "EmployeeCodeConfiguration.View";
        public const string Manage = "EmployeeCodeConfiguration.Manage";
    }

    public static class Department
    {
        public const string View = "Department.View";
        public const string Create = "Department.Create";
        public const string Edit = "Department.Edit";
        public const string Delete = "Department.Delete";
    }

    public static class Designation
    {
        public const string View = "Designation.View";
        public const string Create = "Designation.Create";
        public const string Edit = "Designation.Edit";
        public const string Delete = "Designation.Delete";
    }

    public static class User
    {
        public const string View = "User.View";
        public const string Create = "User.Create";
        public const string Edit = "User.Edit";
        public const string Delete = "User.Delete";
    }

    public static class AccountEmployeeLink
    {
        public const string View = "AccountEmployeeLink.View";
        public const string ViewHistory = "AccountEmployeeLink.ViewHistory";
        public const string Manage = "AccountEmployeeLink.Manage";
    }

    public static class Leave
    {
        public const string DashboardViewAll = "LeaveDashboard.ViewAll";
        public const string ReportsView = "LeaveReports.View";
        public const string ReportsExport = "LeaveReports.Export";
        public const string TypeManage = "Leave.TypeManage";
        public const string PeriodManage = "Leave.PeriodManage";
        public const string PolicyView = "Leave.PolicyView";
        public const string PolicyManage = "Leave.PolicyManage";
        public const string PolicyPublish = "Leave.PolicyPublish";
        public const string Approve = "Leave.Approve";
        public const string BalanceView = "LeaveBalance.View";
        public const string BalanceAdjust = "LeaveBalance.Adjust";
        public const string BalanceImport = "LeaveBalance.Import";
        public const string BalanceViewImportHistory = "LeaveBalance.ViewImportHistory";
        public const string RequestCreate = "Leave.RequestCreate";
        public const string RequestViewOwn = "Leave.RequestViewOwn";
        public const string RequestWithdrawOwn = "Leave.RequestWithdrawOwn";
        public const string RequestCancelOwn = "Leave.RequestCancelOwn";
        public const string BalanceViewOwn = "Leave.BalanceViewOwn";
        public const string TypeViewAvailable = "Leave.TypeViewAvailable";
    }

    public static class RoleManagement
    {
        public const string View = "Role.View";
        public const string Manage = "Role.Manage";
        public const string AssignmentView = "RoleAssignment.View";
        public const string AssignmentManage = "RoleAssignment.Manage";
        public const string AssignmentViewHistory = "RoleAssignment.ViewHistory";
    }

    public static class PageAccess
    {
        public const string View = "PageAccess.View";
        public const string Manage = "PageAccess.Manage";
    }
    public static class Payroll
    {
        public const string SalaryComponentView = "Payroll.SalaryComponent.View";
        public const string SalaryComponentManage = "Payroll.SalaryComponent.Manage";
        public const string SalaryComponentViewHistory = "Payroll.SalaryComponent.ViewHistory";
        public const string SalaryStructureView = "Payroll.SalaryStructure.View";
        public const string SalaryStructureManage = "Payroll.SalaryStructure.Manage";
        public const string SalaryStructureViewHistory = "Payroll.SalaryStructure.ViewHistory";
    }
    public static class Attendance
    {
        public const string View = "Attendance.View";
        public const string ShiftManage = "Attendance.ShiftManage";
        public const string PatternManage = "Attendance.PatternManage";
        public const string RosterManage = "Attendance.RosterManage";
        public const string RosterUpload = "Attendance.RosterUpload";
        public const string RegularizationRequest = "Attendance.Regularization.Request";
        public const string RegularizationApprove = "Attendance.Regularization.Approve";
        public const string OnDutyRequest = "Attendance.OnDuty.Request";
        public const string OnDutyApprove = "Attendance.OnDuty.Approve";
        public const string MonthlyViewSelf = "Attendance.Monthly.ViewSelf";
        public const string MonthlyViewTeam = "Attendance.Monthly.ViewTeam";
        public const string MonthlyViewAll = "Attendance.Monthly.ViewAll";
        public const string MonthlyProcess = "Attendance.Monthly.Process";
        public const string MonthlyClose = "Attendance.Monthly.Close";
        public const string MonthlyReopen = "Attendance.Monthly.Reopen";
        public const string ExceptionView = "Attendance.Exception.View";
        public const string AdminCorrectionManage = "Attendance.AdminCorrection.Manage";
        public const string ReportView = "Attendance.Report.View";
        public const string ReportExport = "Attendance.Report.Export";
    }

    /// <summary>Every permission the system knows about. Used by the seeder and SuperAdmin/TenantAdmin grants.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Employee.View, Employee.Create, Employee.Edit, Employee.Delete, Employee.Export, Employee.Import,
        EmployeeSensitive.View, EmployeeSensitive.Edit,
        Geography.View, Geography.Manage,
        EmploymentHistory.View, EmploymentHistory.Change,
        EmployeeCodeConfiguration.View, EmployeeCodeConfiguration.Manage,
        Department.View, Department.Create, Department.Edit, Department.Delete,
        Designation.View, Designation.Create, Designation.Edit, Designation.Delete,
        User.View, User.Create, User.Edit, User.Delete,
        AccountEmployeeLink.View, AccountEmployeeLink.ViewHistory, AccountEmployeeLink.Manage,
        RoleManagement.View, RoleManagement.Manage, RoleManagement.AssignmentView,
        RoleManagement.AssignmentManage, RoleManagement.AssignmentViewHistory,
        PageAccess.View, PageAccess.Manage,
        Payroll.SalaryComponentView, Payroll.SalaryComponentManage, Payroll.SalaryComponentViewHistory,
        Payroll.SalaryStructureView, Payroll.SalaryStructureManage, Payroll.SalaryStructureViewHistory,
        Leave.DashboardViewAll, Leave.ReportsView, Leave.ReportsExport, Leave.TypeManage, Leave.PeriodManage, Leave.PolicyView, Leave.PolicyManage, Leave.PolicyPublish, Leave.Approve,
        Leave.BalanceView, Leave.BalanceAdjust, Leave.BalanceImport, Leave.BalanceViewImportHistory,
        Leave.RequestCreate, Leave.RequestViewOwn, Leave.RequestWithdrawOwn, Leave.RequestCancelOwn,
        Leave.BalanceViewOwn, Leave.TypeViewAvailable
        , Attendance.View, Attendance.ShiftManage, Attendance.PatternManage, Attendance.RosterManage, Attendance.RosterUpload,
        Attendance.RegularizationRequest, Attendance.RegularizationApprove, Attendance.OnDutyRequest, Attendance.OnDutyApprove,
        Attendance.MonthlyViewSelf, Attendance.MonthlyViewTeam, Attendance.MonthlyViewAll, Attendance.MonthlyProcess, Attendance.MonthlyClose, Attendance.MonthlyReopen, Attendance.ExceptionView, Attendance.AdminCorrectionManage, Attendance.ReportView, Attendance.ReportExport
    };
}
