import { Permissions } from '../auth/permissions.ts'
import type { AuthenticatedUser } from '../api/types.ts'

export interface NavItem {
  label: string
  to: string
  /** Hidden entirely when the signed-in user lacks this. Undefined means everyone signed in sees it. */
  permission?: string
  /** Used for catalogue entries whose child screens have independent permissions. */
  anyPermission?: readonly string[]
  /**
   * Whether the screen exists yet. The nav is written once, in delivery order: an item that is not
   * built is shown greyed with a "soon" marker rather than linking to a 404.
   */
  available: boolean
  requiresEmployeeIdentity?: boolean
  group?: NavGroupId
}

export type NavGroupId = 'employee' | 'leave' | 'attendance' | 'reports' | 'administration' | 'profile'

export interface NavGroup {
  id: NavGroupId
  label: string
  collapsible: boolean
}

/**
 * The navigation, filtered by permission at render time.
 *
 * Filtering here is presentational — a user who forces the URL still meets `RequirePermission` and,
 * behind that, an API that answers 403. What it buys is an honest menu: an Employee-role user is not
 * shown a "Departments" link that would only ever fail.
 */
export const NAV_ITEMS: readonly NavItem[] = [
  { label: 'Dashboard', to: '/dashboard', available: true },
  { label: 'My Profile', to: '/my-profile', available: true, requiresEmployeeIdentity: true, group: 'profile' },
  { label: 'Change Password', to: '/change-password', available: true, group: 'profile' },
  {
    label: 'Employees',
    to: '/employees',
    permission: Permissions.employee.view,
    available: true,
    group: 'employee',
  },
  {
    label: 'Employee Code Configuration',
    to: '/configuration/employee-code',
    permission: Permissions.employeeCodeConfiguration.view,
    available: true,
    group: 'employee',
  },
  { label: 'Login Settings', to: '/configuration/login-settings', permission: Permissions.user.edit, available: true, group: 'administration' },
  { label: 'Password Recovery', to: '/configuration/password-recovery', permission: Permissions.user.edit, available: true, group: 'administration' },
  {
    label: 'Masters',
    to: '/masters/holding-companies',
    anyPermission: [Permissions.geography.view, Permissions.department.view, Permissions.designation.view],
    available: true,
    group: 'employee',
  },
  {
    label: 'Account–Employee Links',
    to: '/administration/account-employee-links',
    permission: Permissions.accountEmployeeLink.view,
    available: true,
    group: 'employee',
  },
  { label: 'Role Management', to: '/role-management', permission: Permissions.roleManagement.assignmentView, available: true, group: 'administration' },
  { label: 'Page Access Management', to: '/page-access-management', permission: Permissions.pageAccess.view, available: true, group: 'administration' },
  { label: 'Salary Components', to: '/payroll/salary-components', permission: Permissions.payroll.salaryComponentView, available: true, group: 'administration' },
  { label: 'Salary Structures', to: '/payroll/salary-structures', permission: Permissions.payroll.salaryStructureView, available: true, group: 'administration' },
  { label: 'Employee Salary', to: '/payroll/employee-salary', permission: Permissions.payroll.employeeSalaryView, available: true, group: 'administration' },
  { label: 'Payroll Periods', to: '/payroll/periods', permission: Permissions.payroll.periodView, available: true, group: 'administration' },
  { label: 'Payroll Runs', to: '/payroll/runs', permission: Permissions.payroll.runView, available: true, group: 'administration' },
  { label: 'My Payslips', to: '/payroll/my-payslips', permission: Permissions.payroll.payslipViewOwn, available: true, requiresEmployeeIdentity: true, group: 'profile' },
  { label: 'Bank Advice', to: '/payroll/bank-advice', permission: Permissions.payroll.bankAdviceView, available: true, group: 'administration' },
  { label: 'Payroll Accounting', to: '/payroll/accounting', permission: Permissions.payroll.accountingView, available: true, group: 'administration' },
  { label: 'Accounting Configuration', to: '/payroll/accounting/configuration', permission: Permissions.payroll.accountingManageConfiguration, available: true, group: 'administration' },
  { label: 'Retro / Arrears', to: '/payroll/retro', permission: Permissions.payroll.retroView, available: true, group: 'administration' },
  { label: 'Final Settlement', to: '/payroll/final-settlements', permission: Permissions.payroll.finalSettlementView, available: true, group: 'administration' },
  { label: 'Statutory Compliance', to: '/payroll/statutory-compliance', permission: Permissions.payroll.statutoryComplianceView, available: true, group: 'administration' },
  { label: 'Payroll Operations', to: '/payroll/operations', permission: Permissions.payroll.runView, available: true, group: 'administration' },
  { label: 'Configuration Health', to: '/payroll/configuration-health', permission: Permissions.payroll.controlsView, available: true, group: 'administration' },
  { label: 'Leave Types', to: '/leave-management/types', permission: Permissions.leave.typeManage, available: true, group: 'leave' },
  { label: 'Leave Periods', to: '/leave-management/periods', permission: Permissions.leave.periodManage, available: true, group: 'leave' },
  { label: 'Leave Policies', to: '/leave-management/policies', permission: Permissions.leave.policyView, available: true, group: 'leave' },
  { label: 'Working Day Calendar', to: '/leave-management/working-day-calendar', permission: Permissions.leave.policyView, available: true, group: 'leave' },
  {
    label: 'Leave Dashboard',
    to: '/leave-management',
    anyPermission: [Permissions.leave.requestViewOwn, Permissions.leave.dashboardViewAll, Permissions.leave.approve],
    available: true,
    group: 'leave',
  },
  { label: 'Apply Leave', to: '/leave-management/apply', permission: Permissions.leave.requestCreate, available: true, requiresEmployeeIdentity: true, group: 'leave' },
  { label: 'My Leave Requests', to: '/leave-management/my-requests', permission: Permissions.leave.requestViewOwn, available: true, requiresEmployeeIdentity: true, group: 'leave' },
  {
    label: 'Team Leave Calendar',
    to: '/leave-management/team-calendar',
    anyPermission: [Permissions.leave.requestViewOwn, Permissions.leave.dashboardViewAll, Permissions.leave.approve],
    available: true,
    group: 'leave',
  },
  { label: 'Leave Balance Import', to: '/leave-management/balances/import', permission: Permissions.leave.balanceImport, available: true, group: 'leave' },
  { label: 'Leave Approvals', to: '/leave-management/approvals', permission: Permissions.leave.approve, available: true, group: 'leave' },
  { label: 'Leave Reports', to: '/leave-management/reports', permission: Permissions.leave.reportsView, available: true, group: 'reports' },
  { label: 'Shifts', to: '/attendance/shifts', permission: Permissions.attendance.view, available: true, group: 'attendance' },
  { label: 'Shift Patterns', to: '/attendance/shift-patterns', permission: Permissions.attendance.view, available: true, group: 'attendance' },
  { label: 'Shift Applicability', to: '/attendance/applicability', permission: Permissions.attendance.view, available: true, group: 'attendance' },
  { label: 'Roster', to: '/attendance/roster', permission: Permissions.attendance.view, available: true, group: 'attendance' },
  { label: 'Roster Upload', to: '/attendance/roster-upload', permission: Permissions.attendance.view, available: true, group: 'attendance' },
  { label: 'My Attendance', to: '/attendance/my-attendance', permission: Permissions.attendance.view, available: true, requiresEmployeeIdentity: true, group: 'attendance' },
  { label: 'Attendance Requests', to: '/attendance/requests', permission: Permissions.attendance.view, available: true, requiresEmployeeIdentity: true, group: 'attendance' },
  { label: 'Team Attendance', to: '/attendance/team', permission: Permissions.attendance.view, available: true, group: 'attendance' },
  { label: 'Attendance Reports', to: '/attendance/reports', permission: Permissions.attendance.reportView, available: true, group: 'reports' },
]

export const NAV_GROUPS: readonly NavGroup[] = [
  { id: 'employee', label: 'Employee Management', collapsible: true },
  { id: 'leave', label: 'Leave Management', collapsible: true },
  { id: 'attendance', label: 'Attendance Management', collapsible: true },
  { id: 'reports', label: 'Reports', collapsible: true },
  { id: 'administration', label: 'Administration', collapsible: true },
  { id: 'profile', label: 'User/Profile', collapsible: true },
]

export function visibleNavItems(can: (permission: string) => boolean, user?: AuthenticatedUser | null): NavItem[] {
  return NAV_ITEMS.filter((item) =>
    (item.permission === undefined || can(item.permission)) &&
    (item.anyPermission === undefined || item.anyPermission.some(can)) &&
    (!item.requiresEmployeeIdentity || user?.employeeIdentity?.status === 'Linked'),
  )
}

export function visibleNavGroups(can: (permission: string) => boolean, user?: AuthenticatedUser | null): Array<NavGroup & { items: NavItem[] }> {
  const items = visibleNavItems(can, user)
  return NAV_GROUPS.map(group => ({ ...group, items: items.filter(item => item.group === group.id) })).filter(group => group.items.length > 0)
}
