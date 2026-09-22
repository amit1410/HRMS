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
        public const string EmployeeSalaryView = "Payroll.EmployeeSalary.View";
        public const string EmployeeSalaryManage = "Payroll.EmployeeSalary.Manage";
        public const string EmployeeSalaryViewHistory = "Payroll.EmployeeSalary.ViewHistory";
        public const string PeriodView = "Payroll.Period.View";
        public const string PeriodManage = "Payroll.Period.Manage";
        public const string PeriodUnlock = "Payroll.Period.Unlock";
        public const string ControlsView = "Payroll.Controls.View";
        public const string ControlsManage = "Payroll.Controls.Manage";
        public const string RunView = "Payroll.Run.View";
        public const string RunManage = "Payroll.Run.Manage";
        public const string RunPrepare = "Payroll.Run.Prepare";
        public const string RunApprove = "Payroll.Run.Approve";
        public const string RunFinalize = "Payroll.Run.Finalize";
        public const string RunViewHistory = "Payroll.Run.ViewHistory";
        public const string RunCalculate = "Payroll.Run.Calculate";
        public const string RunRecalculate = "Payroll.Run.Recalculate";
        public const string RunViewResults = "Payroll.Run.ViewResults";
        public const string StatutoryView = "Payroll.Statutory.View";
        public const string StatutoryManage = "Payroll.Statutory.Manage";
        public const string StatutoryViewHistory = "Payroll.Statutory.ViewHistory";
        public const string EmployeeStatutoryView = "Payroll.EmployeeStatutory.View";
        public const string EmployeeStatutoryManage = "Payroll.EmployeeStatutory.Manage";
        public const string PayslipViewAll = "Payroll.Payslip.ViewAll";
        public const string PayslipGenerate = "Payroll.Payslip.Generate";
        public const string PayslipPublish = "Payroll.Payslip.Publish";
        public const string PayslipViewHistory = "Payroll.Payslip.ViewHistory";
        public const string RegisterView = "Payroll.Register.View";
        public const string RegisterExport = "Payroll.Register.Export";
        public const string PayslipViewOwn = "Payroll.Payslip.ViewOwn";
        public const string BankAdviceView = "Payroll.BankAdvice.View";
        public const string BankAdviceGenerate = "Payroll.BankAdvice.Generate";
        public const string BankAdviceValidate = "Payroll.BankAdvice.Validate";
        public const string BankAdviceApprove = "Payroll.BankAdvice.Approve";
        public const string BankAdviceExport = "Payroll.BankAdvice.Export";
        public const string BankAdviceCancel = "Payroll.BankAdvice.Cancel";
        public const string BankAdviceViewHistory = "Payroll.BankAdvice.ViewHistory";
        public const string RetroView = "Payroll.Retro.View";
        public const string RetroEvaluate = "Payroll.Retro.Evaluate";
        public const string RetroApprove = "Payroll.Retro.Approve";
        public const string RetroApply = "Payroll.Retro.Apply";
        public const string RetroCancel = "Payroll.Retro.Cancel";
        public const string FinalSettlementView = "Payroll.FinalSettlement.View";
        public const string FinalSettlementManage = "Payroll.FinalSettlement.Manage";
        public const string FinalSettlementCalculate = "Payroll.FinalSettlement.Calculate";
        public const string FinalSettlementApprove = "Payroll.FinalSettlement.Approve";
        public const string FinalSettlementFinalize = "Payroll.FinalSettlement.Finalize";
        public const string FinalSettlementCancel = "Payroll.FinalSettlement.Cancel";
        public const string AccountingView = "Payroll.Accounting.View";
        public const string AccountingGenerate = "Payroll.Accounting.Generate";
        public const string AccountingValidate = "Payroll.Accounting.Validate";
        public const string AccountingApprove = "Payroll.Accounting.Approve";
        public const string AccountingPost = "Payroll.Accounting.Post";
        public const string AccountingExport = "Payroll.Accounting.Export";
        public const string AccountingViewHistory = "Payroll.Accounting.ViewHistory";
        public const string AccountingManageConfiguration = "Payroll.Accounting.ManageConfiguration";
        public const string StatutoryComplianceView = "Payroll.StatutoryCompliance.View";
        public const string StatutoryComplianceGenerate = "Payroll.StatutoryCompliance.Generate";
        public const string StatutoryComplianceValidate = "Payroll.StatutoryCompliance.Validate";
        public const string StatutoryComplianceApprove = "Payroll.StatutoryCompliance.Approve";
        public const string StatutoryComplianceExport = "Payroll.StatutoryCompliance.Export";
        public const string StatutoryComplianceMarkFiled = "Payroll.StatutoryCompliance.MarkFiled";
        public const string StatutoryComplianceCancel = "Payroll.StatutoryCompliance.Cancel";
        public const string StatutoryComplianceViewHistory = "Payroll.StatutoryCompliance.ViewHistory";
        public const string StatutoryComplianceManagePeriods = "Payroll.StatutoryCompliance.ManagePeriods";
        public const string LoansView = "Payroll.Loans.View";
        public const string LoansRequest = "Payroll.Loans.Request";
        public const string LoansManage = "Payroll.Loans.Manage";
        public const string LoansApprove = "Payroll.Loans.Approve";
        public const string LoansDisburse = "Payroll.Loans.Disburse";
        public const string LoansRecover = "Payroll.Loans.Recover";
        public const string LoansClose = "Payroll.Loans.Close";
        public const string LoansCancel = "Payroll.Loans.Cancel";
        public const string LoansViewHistory = "Payroll.Loans.ViewHistory";
        public const string LoansManageProducts = "Payroll.Loans.ManageProducts";
        public const string ReimbursementsView = "Payroll.Reimbursements.View";
        public const string ReimbursementsRequest = "Payroll.Reimbursements.Request";
        public const string ReimbursementsManage = "Payroll.Reimbursements.Manage";
        public const string ReimbursementsApprove = "Payroll.Reimbursements.Approve";
        public const string ReimbursementsSettle = "Payroll.Reimbursements.Settle";
        public const string ReimbursementsCancel = "Payroll.Reimbursements.Cancel";
        public const string ReimbursementsViewHistory = "Payroll.Reimbursements.ViewHistory";
        public const string ReimbursementsManageCategories = "Payroll.Reimbursements.ManageCategories";
        public const string SeparationBenefitsView = "Payroll.SeparationBenefits.View";
        public const string SeparationBenefitsCalculate = "Payroll.SeparationBenefits.Calculate";
        public const string SeparationBenefitsManagePolicies = "Payroll.SeparationBenefits.ManagePolicies";
        public const string SeparationBenefitsOverride = "Payroll.SeparationBenefits.Override";
        public const string SeparationBenefitsApprove = "Payroll.SeparationBenefits.Approve";
        public const string SeparationBenefitsViewHistory = "Payroll.SeparationBenefits.ViewHistory";
        public const string VariablePayView = "Payroll.VariablePay.View";
        public const string VariablePayManagePlans = "Payroll.VariablePay.ManagePlans";
        public const string VariablePayCalculate = "Payroll.VariablePay.Calculate";
        public const string VariablePayCreateAward = "Payroll.VariablePay.CreateAward";
        public const string VariablePaySubmit = "Payroll.VariablePay.Submit";
        public const string VariablePayApprove = "Payroll.VariablePay.Approve";
        public const string VariablePayOverride = "Payroll.VariablePay.Override";
        public const string VariablePayCancel = "Payroll.VariablePay.Cancel";
        public const string VariablePayViewHistory = "Payroll.VariablePay.ViewHistory";
        public const string AdjustmentsView = "Payroll.Adjustments.View";
        public const string AdjustmentsCreate = "Payroll.Adjustments.Create";
        public const string AdjustmentsSubmit = "Payroll.Adjustments.Submit";
        public const string AdjustmentsApprove = "Payroll.Adjustments.Approve";
        public const string AdjustmentsCancel = "Payroll.Adjustments.Cancel";
        public const string AdjustmentsReverse = "Payroll.Adjustments.Reverse";
        public const string AdjustmentsViewHistory = "Payroll.Adjustments.ViewHistory";
        public const string OffCycleView = "Payroll.OffCycle.View";
        public const string OffCycleCreate = "Payroll.OffCycle.Create";
        public const string OffCycleApprove = "Payroll.OffCycle.Approve";
        public const string OffCycleProcess = "Payroll.OffCycle.Process";
        public const string OffCycleCancel = "Payroll.OffCycle.Cancel";
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
        Payroll.EmployeeSalaryView, Payroll.EmployeeSalaryManage, Payroll.EmployeeSalaryViewHistory,
        Payroll.PeriodView, Payroll.PeriodManage, Payroll.PeriodUnlock, Payroll.ControlsView, Payroll.ControlsManage, Payroll.RunView, Payroll.RunManage, Payroll.RunPrepare, Payroll.RunApprove, Payroll.RunFinalize, Payroll.RunViewHistory, Payroll.RunCalculate, Payroll.RunRecalculate, Payroll.RunViewResults,
        Payroll.StatutoryView, Payroll.StatutoryManage, Payroll.StatutoryViewHistory, Payroll.EmployeeStatutoryView, Payroll.EmployeeStatutoryManage,
        Payroll.PayslipViewAll, Payroll.PayslipGenerate, Payroll.PayslipPublish, Payroll.PayslipViewHistory, Payroll.RegisterView, Payroll.RegisterExport, Payroll.PayslipViewOwn,
        Payroll.BankAdviceView, Payroll.BankAdviceGenerate, Payroll.BankAdviceValidate, Payroll.BankAdviceApprove, Payroll.BankAdviceExport, Payroll.BankAdviceCancel, Payroll.BankAdviceViewHistory,
        Payroll.AccountingView, Payroll.AccountingGenerate, Payroll.AccountingValidate, Payroll.AccountingApprove, Payroll.AccountingPost, Payroll.AccountingExport, Payroll.AccountingViewHistory, Payroll.AccountingManageConfiguration,
        Payroll.RetroView, Payroll.RetroEvaluate, Payroll.RetroApprove, Payroll.RetroApply, Payroll.RetroCancel,
        Payroll.FinalSettlementView, Payroll.FinalSettlementManage, Payroll.FinalSettlementCalculate, Payroll.FinalSettlementApprove, Payroll.FinalSettlementFinalize, Payroll.FinalSettlementCancel,
        Payroll.StatutoryComplianceView, Payroll.StatutoryComplianceGenerate, Payroll.StatutoryComplianceValidate, Payroll.StatutoryComplianceApprove, Payroll.StatutoryComplianceExport, Payroll.StatutoryComplianceMarkFiled, Payroll.StatutoryComplianceCancel, Payroll.StatutoryComplianceViewHistory, Payroll.StatutoryComplianceManagePeriods,
        Payroll.LoansView, Payroll.LoansRequest, Payroll.LoansManage, Payroll.LoansApprove, Payroll.LoansDisburse, Payroll.LoansRecover, Payroll.LoansClose, Payroll.LoansCancel, Payroll.LoansViewHistory, Payroll.LoansManageProducts,
        Payroll.ReimbursementsView, Payroll.ReimbursementsRequest, Payroll.ReimbursementsManage, Payroll.ReimbursementsApprove, Payroll.ReimbursementsSettle, Payroll.ReimbursementsCancel, Payroll.ReimbursementsViewHistory, Payroll.ReimbursementsManageCategories,
        Payroll.SeparationBenefitsView, Payroll.SeparationBenefitsCalculate, Payroll.SeparationBenefitsManagePolicies, Payroll.SeparationBenefitsOverride, Payroll.SeparationBenefitsApprove, Payroll.SeparationBenefitsViewHistory,
        Payroll.VariablePayView, Payroll.VariablePayManagePlans, Payroll.VariablePayCalculate, Payroll.VariablePayCreateAward, Payroll.VariablePaySubmit, Payroll.VariablePayApprove, Payroll.VariablePayOverride, Payroll.VariablePayCancel, Payroll.VariablePayViewHistory,
        Payroll.AdjustmentsView, Payroll.AdjustmentsCreate, Payroll.AdjustmentsSubmit, Payroll.AdjustmentsApprove, Payroll.AdjustmentsCancel, Payroll.AdjustmentsReverse, Payroll.AdjustmentsViewHistory, Payroll.OffCycleView, Payroll.OffCycleCreate, Payroll.OffCycleApprove, Payroll.OffCycleProcess, Payroll.OffCycleCancel,
        Leave.DashboardViewAll, Leave.ReportsView, Leave.ReportsExport, Leave.TypeManage, Leave.PeriodManage, Leave.PolicyView, Leave.PolicyManage, Leave.PolicyPublish, Leave.Approve,
        Leave.BalanceView, Leave.BalanceAdjust, Leave.BalanceImport, Leave.BalanceViewImportHistory,
        Leave.RequestCreate, Leave.RequestViewOwn, Leave.RequestWithdrawOwn, Leave.RequestCancelOwn,
        Leave.BalanceViewOwn, Leave.TypeViewAvailable
        , Attendance.View, Attendance.ShiftManage, Attendance.PatternManage, Attendance.RosterManage, Attendance.RosterUpload,
        Attendance.RegularizationRequest, Attendance.RegularizationApprove, Attendance.OnDutyRequest, Attendance.OnDutyApprove,
        Attendance.MonthlyViewSelf, Attendance.MonthlyViewTeam, Attendance.MonthlyViewAll, Attendance.MonthlyProcess, Attendance.MonthlyClose, Attendance.MonthlyReopen, Attendance.ExceptionView, Attendance.AdminCorrectionManage, Attendance.ReportView, Attendance.ReportExport
    };
}
