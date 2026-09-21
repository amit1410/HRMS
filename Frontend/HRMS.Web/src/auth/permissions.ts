import type { AuthenticatedUser } from '../api/types.ts'

/**
 * Mirror of `Backend/HRMS.Domain/Authorization/Permissions.cs`.
 *
 * **These checks are cosmetic.** They decide what to render — a hidden "Delete" button, a nav item
 * that is not shown, a screen that says "no access" instead of an empty table. They are not a security
 * boundary: every endpoint is guarded server-side by `[HasPermission(...)]`, and a user who edits their
 * own JavaScript gains nothing but a button that returns 403.
 *
 * A test (`permissions.mirror.test.ts`) reads the C# file and asserts these two lists still agree, so
 * a permission added on the server cannot quietly go missing here.
 */
export const Permissions = {
  employee: {
    view: 'Employee.View',
    create: 'Employee.Create',
    edit: 'Employee.Edit',
    delete: 'Employee.Delete',
    export: 'Employee.Export',
    import: 'Employee.Import',
  },
  employeeSensitive: {
    view: 'EmployeeSensitive.View',
    edit: 'EmployeeSensitive.Edit',
  },
  geography: {
    view: 'Geography.View',
    manage: 'Geography.Manage',
  },
  employmentHistory: {
    view: 'EmploymentHistory.View',
    change: 'EmploymentHistory.Change',
  },
  employeeCodeConfiguration: {
    view: 'EmployeeCodeConfiguration.View',
    manage: 'EmployeeCodeConfiguration.Manage',
  },
  department: {
    view: 'Department.View',
    create: 'Department.Create',
    edit: 'Department.Edit',
    delete: 'Department.Delete',
  },
  designation: {
    view: 'Designation.View',
    create: 'Designation.Create',
    edit: 'Designation.Edit',
    delete: 'Designation.Delete',
  },
  user: {
    view: 'User.View',
    create: 'User.Create',
    edit: 'User.Edit',
    delete: 'User.Delete',
  },
  accountEmployeeLink: {
    view: 'AccountEmployeeLink.View',
    viewHistory: 'AccountEmployeeLink.ViewHistory',
    manage: 'AccountEmployeeLink.Manage',
  },
  leave: {
    dashboardViewAll: 'LeaveDashboard.ViewAll',
    reportsView: 'LeaveReports.View',
    reportsExport: 'LeaveReports.Export',
    typeManage: 'Leave.TypeManage',
    periodManage: 'Leave.PeriodManage',
    policyView: 'Leave.PolicyView',
    policyManage: 'Leave.PolicyManage',
    policyPublish: 'Leave.PolicyPublish',
    approve: 'Leave.Approve',
    balanceView: 'LeaveBalance.View',
    balanceAdjust: 'LeaveBalance.Adjust',
    balanceImport: 'LeaveBalance.Import',
    balanceViewImportHistory: 'LeaveBalance.ViewImportHistory',
    requestCreate: 'Leave.RequestCreate',
    requestViewOwn: 'Leave.RequestViewOwn',
    requestWithdrawOwn: 'Leave.RequestWithdrawOwn',
    requestCancelOwn: 'Leave.RequestCancelOwn',
    balanceViewOwn: 'Leave.BalanceViewOwn',
    typeViewAvailable: 'Leave.TypeViewAvailable',
  },
  roleManagement: {
    view: 'Role.View',
    manage: 'Role.Manage',
    assignmentView: 'RoleAssignment.View',
    assignmentManage: 'RoleAssignment.Manage',
    assignmentViewHistory: 'RoleAssignment.ViewHistory',
  },
  pageAccess: {
    view: 'PageAccess.View',
    manage: 'PageAccess.Manage',
  },
  payroll: {
    salaryComponentView: 'Payroll.SalaryComponent.View',
    salaryComponentManage: 'Payroll.SalaryComponent.Manage',
    salaryComponentViewHistory: 'Payroll.SalaryComponent.ViewHistory',
    salaryStructureView: 'Payroll.SalaryStructure.View',
    salaryStructureManage: 'Payroll.SalaryStructure.Manage',
    salaryStructureViewHistory: 'Payroll.SalaryStructure.ViewHistory',
    employeeSalaryView: 'Payroll.EmployeeSalary.View',
    employeeSalaryManage: 'Payroll.EmployeeSalary.Manage',
    employeeSalaryViewHistory: 'Payroll.EmployeeSalary.ViewHistory',
    periodView: 'Payroll.Period.View',
    periodManage: 'Payroll.Period.Manage',
    periodUnlock: 'Payroll.Period.Unlock',
    controlsView: 'Payroll.Controls.View',
    controlsManage: 'Payroll.Controls.Manage',
    runView: 'Payroll.Run.View',
    runManage: 'Payroll.Run.Manage',
    runPrepare: 'Payroll.Run.Prepare',
    runApprove: 'Payroll.Run.Approve',
    runFinalize: 'Payroll.Run.Finalize',
    runViewHistory: 'Payroll.Run.ViewHistory',
    runCalculate: 'Payroll.Run.Calculate',
    runRecalculate: 'Payroll.Run.Recalculate',
    runViewResults: 'Payroll.Run.ViewResults',
    statutoryView: 'Payroll.Statutory.View',
    statutoryManage: 'Payroll.Statutory.Manage',
    statutoryViewHistory: 'Payroll.Statutory.ViewHistory',
    employeeStatutoryView: 'Payroll.EmployeeStatutory.View',
    employeeStatutoryManage: 'Payroll.EmployeeStatutory.Manage',
    payslipViewAll: 'Payroll.Payslip.ViewAll',
    payslipGenerate: 'Payroll.Payslip.Generate',
    payslipPublish: 'Payroll.Payslip.Publish',
    payslipViewHistory: 'Payroll.Payslip.ViewHistory',
    registerView: 'Payroll.Register.View',
    registerExport: 'Payroll.Register.Export',
    payslipViewOwn: 'Payroll.Payslip.ViewOwn',
    bankAdviceView: 'Payroll.BankAdvice.View',
    bankAdviceGenerate: 'Payroll.BankAdvice.Generate',
    bankAdviceValidate: 'Payroll.BankAdvice.Validate',
    bankAdviceApprove: 'Payroll.BankAdvice.Approve',
    bankAdviceExport: 'Payroll.BankAdvice.Export',
    bankAdviceCancel: 'Payroll.BankAdvice.Cancel',
    bankAdviceViewHistory: 'Payroll.BankAdvice.ViewHistory',
    retroView: 'Payroll.Retro.View',
    retroEvaluate: 'Payroll.Retro.Evaluate',
    retroApprove: 'Payroll.Retro.Approve',
    retroApply: 'Payroll.Retro.Apply',
    retroCancel: 'Payroll.Retro.Cancel',
    finalSettlementView: 'Payroll.FinalSettlement.View',
    finalSettlementManage: 'Payroll.FinalSettlement.Manage',
    finalSettlementCalculate: 'Payroll.FinalSettlement.Calculate',
    finalSettlementApprove: 'Payroll.FinalSettlement.Approve',
    finalSettlementFinalize: 'Payroll.FinalSettlement.Finalize',
    finalSettlementCancel: 'Payroll.FinalSettlement.Cancel',
    accountingView: 'Payroll.Accounting.View',
    accountingGenerate: 'Payroll.Accounting.Generate',
    accountingValidate: 'Payroll.Accounting.Validate',
    accountingApprove: 'Payroll.Accounting.Approve',
    accountingPost: 'Payroll.Accounting.Post',
    accountingExport: 'Payroll.Accounting.Export',
    accountingViewHistory: 'Payroll.Accounting.ViewHistory',
    accountingManageConfiguration: 'Payroll.Accounting.ManageConfiguration',
    statutoryComplianceView: 'Payroll.StatutoryCompliance.View',
    statutoryComplianceGenerate: 'Payroll.StatutoryCompliance.Generate',
    statutoryComplianceValidate: 'Payroll.StatutoryCompliance.Validate',
    statutoryComplianceApprove: 'Payroll.StatutoryCompliance.Approve',
    statutoryComplianceExport: 'Payroll.StatutoryCompliance.Export',
    statutoryComplianceMarkFiled: 'Payroll.StatutoryCompliance.MarkFiled',
    statutoryComplianceCancel: 'Payroll.StatutoryCompliance.Cancel',
    statutoryComplianceViewHistory: 'Payroll.StatutoryCompliance.ViewHistory',
    statutoryComplianceManagePeriods: 'Payroll.StatutoryCompliance.ManagePeriods',
    loansView: 'Payroll.Loans.View',
    loansRequest: 'Payroll.Loans.Request',
    loansManage: 'Payroll.Loans.Manage',
    loansApprove: 'Payroll.Loans.Approve',
    loansDisburse: 'Payroll.Loans.Disburse',
    loansRecover: 'Payroll.Loans.Recover',
    loansClose: 'Payroll.Loans.Close',
    loansCancel: 'Payroll.Loans.Cancel',
    loansViewHistory: 'Payroll.Loans.ViewHistory',
    loansManageProducts: 'Payroll.Loans.ManageProducts',
    reimbursementsView: 'Payroll.Reimbursements.View',
    reimbursementsRequest: 'Payroll.Reimbursements.Request',
    reimbursementsManage: 'Payroll.Reimbursements.Manage',
    reimbursementsApprove: 'Payroll.Reimbursements.Approve',
    reimbursementsSettle: 'Payroll.Reimbursements.Settle',
    reimbursementsCancel: 'Payroll.Reimbursements.Cancel',
    reimbursementsViewHistory: 'Payroll.Reimbursements.ViewHistory',
    reimbursementsManageCategories: 'Payroll.Reimbursements.ManageCategories',
    separationBenefitsView: 'Payroll.SeparationBenefits.View',
    separationBenefitsCalculate: 'Payroll.SeparationBenefits.Calculate',
    separationBenefitsManagePolicies: 'Payroll.SeparationBenefits.ManagePolicies',
    separationBenefitsOverride: 'Payroll.SeparationBenefits.Override',
    separationBenefitsApprove: 'Payroll.SeparationBenefits.Approve',
    separationBenefitsViewHistory: 'Payroll.SeparationBenefits.ViewHistory',
  },
  attendance: {
    view: 'Attendance.View',
    shiftManage: 'Attendance.ShiftManage',
    patternManage: 'Attendance.PatternManage',
    rosterManage: 'Attendance.RosterManage',
    rosterUpload: 'Attendance.RosterUpload',
    regularizationRequest: 'Attendance.Regularization.Request',
    regularizationApprove: 'Attendance.Regularization.Approve',
    onDutyRequest: 'Attendance.OnDuty.Request',
    onDutyApprove: 'Attendance.OnDuty.Approve',
    monthlyViewSelf: 'Attendance.Monthly.ViewSelf',
    monthlyViewTeam: 'Attendance.Monthly.ViewTeam',
    monthlyViewAll: 'Attendance.Monthly.ViewAll',
    monthlyProcess: 'Attendance.Monthly.Process',
    monthlyClose: 'Attendance.Monthly.Close',
    monthlyReopen: 'Attendance.Monthly.Reopen',
    exceptionView: 'Attendance.Exception.View',
    adminCorrectionManage: 'Attendance.AdminCorrection.Manage',
    reportView: 'Attendance.Report.View',
    reportExport: 'Attendance.Report.Export',
  },
} as const

/** Every permission the system knows about — the counterpart of `Permissions.All` in C#. */
export const ALL_PERMISSIONS: readonly string[] = Object.values(Permissions).flatMap((group) =>
  Object.values(group),
)

/**
 * Whether the user holds a permission. Comparison is case-insensitive because the API's authorization
 * policies are, and a mismatch of case would otherwise hide a button the user can actually use.
 */
export function hasPermission(
  user: Pick<AuthenticatedUser, 'permissions'> | null | undefined,
  permission: string,
): boolean {
  if (!user) return false
  const wanted = permission.toLowerCase()
  return user.permissions.some((granted) => granted.toLowerCase() === wanted)
}

/** Whether the user holds at least one of the given permissions. */
export function hasAnyPermission(
  user: Pick<AuthenticatedUser, 'permissions'> | null | undefined,
  permissions: readonly string[],
): boolean {
  return permissions.some((permission) => hasPermission(user, permission))
}
