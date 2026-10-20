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
  flyoutSection?: string
}

export type NavGroupId = 'employee' | 'leave' | 'attendance' | 'reports' | 'administration' | 'profile'

export type NavModuleId = 'home' | 'people' | 'attendance' | 'leave' | 'payroll' | 'separation' | 'reports' | 'communication' | 'administration' | 'workspace'

export interface NavModule {
  id: NavModuleId
  label: string
  items: NavItem[]
}

export type ConfigurationCategory =
  | 'Employee & Onboarding'
  | 'Employee Masters'
  | 'Leave Policies'
  | 'Attendance & Work Schedules'
  | 'Payroll Configuration'
  | 'Confirmation & Separation Policies'
  | 'Security & Access Management'

export interface ConfigurationDestination {
  label: string
  to: string
  category: ConfigurationCategory
  description: string
  permission?: string
  anyPermission?: readonly string[]
  icon: 'employee' | 'master' | 'leave' | 'attendance' | 'payroll' | 'policy' | 'security'
}

/**
 * The single source of truth for the Configuration Center. Every entry maps to an existing route and
 * reuses the same effective permission that protects that route in App.tsx.
 */
export const CONFIGURATION_DESTINATIONS: readonly ConfigurationDestination[] = [
  { label: 'Employee Code Configuration', to: '/configuration/employee-code', category: 'Employee & Onboarding', description: 'Define how employee identifiers are generated and maintained.', permission: Permissions.employeeCodeConfiguration.view, icon: 'employee' },
  { label: 'Masters', to: '/masters/holding-companies', category: 'Employee Masters', description: 'Maintain organization, employee, workplace, and other master data.', anyPermission: [Permissions.geography.view, Permissions.department.view, Permissions.designation.view, Permissions.bankMaster.view], icon: 'master' },
  { label: 'Account–Employee Links', to: '/administration/account-employee-links', category: 'Employee Masters', description: 'Configure links between user accounts and employee identities.', permission: Permissions.accountEmployeeLink.view, icon: 'employee' },
  { label: 'Leave Types', to: '/leave-management/types', category: 'Leave Policies', description: 'Define the leave types available to the organization.', permission: Permissions.leave.typeManage, icon: 'leave' },
  { label: 'Leave Periods', to: '/leave-management/periods', category: 'Leave Policies', description: 'Set the periods used to administer leave balances.', permission: Permissions.leave.periodManage, icon: 'leave' },
  { label: 'Leave Policies', to: '/leave-management/policies', category: 'Leave Policies', description: 'Configure accrual, eligibility, and leave application rules.', permission: Permissions.leave.policyView, icon: 'leave' },
  { label: 'Working Day Calendar', to: '/leave-management/working-day-calendar', category: 'Leave Policies', description: 'Manage working days and holidays used by leave calculations.', permission: Permissions.leave.policyView, icon: 'leave' },
  { label: 'Shifts', to: '/attendance/shifts', category: 'Attendance & Work Schedules', description: 'Define the work shifts used for attendance tracking.', permission: Permissions.attendance.view, icon: 'attendance' },
  { label: 'Shift Patterns', to: '/attendance/shift-patterns', category: 'Attendance & Work Schedules', description: 'Build recurring patterns for scheduled work.', permission: Permissions.attendance.view, icon: 'attendance' },
  { label: 'Shift Applicability', to: '/attendance/applicability', category: 'Attendance & Work Schedules', description: 'Set how shifts apply across the workforce.', permission: Permissions.attendance.view, icon: 'attendance' },
  { label: 'Device Integration', to: '/attendance/devices', category: 'Attendance & Work Schedules', description: 'Operate biometric devices, employee mappings, sync runs, and ingestion issues.', anyPermission: [Permissions.attendance.deviceView, Permissions.attendance.deviceManage, Permissions.attendance.deviceMapping, Permissions.attendance.deviceSync, Permissions.attendance.deviceViewHistory], icon: 'attendance' },
  { label: 'Salary Components', to: '/payroll/salary-components', category: 'Payroll Configuration', description: 'Configure the earnings and deduction components used in payroll.', permission: Permissions.payroll.salaryComponentView, icon: 'payroll' },
  { label: 'Salary Structures', to: '/payroll/salary-structures', category: 'Payroll Configuration', description: 'Define salary structures and their component composition.', permission: Permissions.payroll.salaryStructureView, icon: 'payroll' },
  { label: 'Payroll Periods', to: '/payroll/periods', category: 'Payroll Configuration', description: 'Set up and manage the periods used to process payroll.', permission: Permissions.payroll.periodView, icon: 'payroll' },
  { label: 'Statutory Configurations', to: '/payroll/statutory', category: 'Payroll Configuration', description: 'Maintain statutory payroll rules and settings.', permission: Permissions.payroll.statutoryView, icon: 'payroll' },
  { label: 'Accounting Configuration', to: '/payroll/accounting/configuration', category: 'Payroll Configuration', description: 'Configure payroll accounting integration and posting rules.', permission: Permissions.payroll.accountingManageConfiguration, icon: 'payroll' },
  { label: 'Confirmation Policies', to: '/confirmation/policies', category: 'Confirmation & Separation Policies', description: 'Set the policy rules used for employee confirmation.', permission: Permissions.confirmation.policyView, icon: 'policy' },
  { label: 'Confirmation Scheduler', to: '/confirmation/scheduler', category: 'Confirmation & Separation Policies', description: 'Configure the scheduling of confirmation reviews.', permission: Permissions.confirmation.schedulerView, icon: 'policy' },
  { label: 'Review Questions', to: '/confirmation/review-questions', category: 'Confirmation & Separation Policies', description: 'Manage the questions used in confirmation reviews.', permission: Permissions.confirmation.reviewQuestionView, icon: 'policy' },
  { label: 'Rating Scales', to: '/confirmation/rating-scales', category: 'Confirmation & Separation Policies', description: 'Define rating scales used to evaluate confirmation reviews.', permission: Permissions.confirmation.ratingScaleView, icon: 'policy' },
  { label: 'Exit Interview Configuration', to: '/separation/exit-interview-configuration', category: 'Confirmation & Separation Policies', description: 'Configure the exit interview experience and questions.', permission: Permissions.separation.clearanceConfigure, icon: 'policy' },
  { label: 'Separation Document Templates', to: '/separation/document-templates', category: 'Confirmation & Separation Policies', description: 'Manage templates used for separation documents.', permission: Permissions.separation.viewAll, icon: 'policy' },
  { label: 'Login Settings', to: '/configuration/login-settings', category: 'Security & Access Management', description: 'Configure tenant sign-in behavior and authentication settings.', permission: Permissions.user.edit, icon: 'security' },
  { label: 'Password Recovery', to: '/configuration/password-recovery', category: 'Security & Access Management', description: 'Configure tenant password recovery behavior.', permission: Permissions.user.edit, icon: 'security' },
  { label: 'Role Management', to: '/role-management', category: 'Security & Access Management', description: 'Manage roles and their employee assignments.', permission: Permissions.roleManagement.assignmentView, icon: 'security' },
  { label: 'Page Access Management', to: '/page-access-management', category: 'Security & Access Management', description: 'Control page-level access for tenant roles and users.', permission: Permissions.pageAccess.view, icon: 'security' },
]

export const CONFIGURATION_CENTER_PERMISSIONS = [...new Set(CONFIGURATION_DESTINATIONS.flatMap(item => item.permission ? [item.permission] : item.anyPermission ?? []))]

export function visibleConfigurationDestinations(can: (permission: string) => boolean): ConfigurationDestination[] {
  return CONFIGURATION_DESTINATIONS.filter(item =>
    (item.permission === undefined || can(item.permission)) &&
    (item.anyPermission === undefined || item.anyPermission.some(can)),
  )
}

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
  { label: 'Configuration Center', to: '/configuration', anyPermission: CONFIGURATION_CENTER_PERMISSIONS, available: true, group: 'administration' },
  { label: 'Welcome Invitations', to: '/communication/welcome-invitations', permission: Permissions.welcomeInvitation.view, available: true, group: 'administration' },
  { label: 'My Profile', to: '/my-profile', available: true, requiresEmployeeIdentity: true, group: 'profile' },
  { label: 'Change Password', to: '/change-password', available: true, group: 'profile' },
  {
    label: 'Employees',
    to: '/employees',
    permission: Permissions.employee.view,
    available: true,
    group: 'employee',
  },
  { label: 'Employee Salary', to: '/payroll/employee-salary', permission: Permissions.payroll.employeeSalaryView, available: true, group: 'administration' },
  { label: 'Payroll Runs', to: '/payroll/runs', permission: Permissions.payroll.runView, available: true, group: 'administration' },
  { label: 'My Payslips', to: '/payroll/my-payslips', permission: Permissions.payroll.payslipViewOwn, available: true, requiresEmployeeIdentity: true, group: 'profile' },
  { label: 'Bank Advice', to: '/payroll/bank-advice', permission: Permissions.payroll.bankAdviceView, available: true, group: 'administration' },
  { label: 'Payroll Accounting', to: '/payroll/accounting', permission: Permissions.payroll.accountingView, available: true, group: 'administration' },
  { label: 'Retro / Arrears', to: '/payroll/retro', permission: Permissions.payroll.retroView, available: true, group: 'administration' },
  { label: 'Final Settlement', to: '/payroll/final-settlements', permission: Permissions.payroll.finalSettlementView, available: true, group: 'administration' },
  { label: 'My Separation / Resignation', to: '/separation/my-request', permission: Permissions.separation.viewSelf, available: true, requiresEmployeeIdentity: true, group: 'profile' },
  { label: 'Separation Manager Inbox', to: '/separation/manager-inbox', permission: Permissions.separation.managerReview, available: true, group: 'administration' },
  { label: 'Separation HR Inbox', to: '/separation/hr-inbox', permission: Permissions.separation.hrReview, available: true, group: 'administration' },
  { label: 'Separation Exit Closure', to: '/separation/exit-closure', permission: Permissions.separation.manage, available: true, group: 'administration' },
  { label: 'Statutory Compliance', to: '/payroll/statutory-compliance', permission: Permissions.payroll.statutoryComplianceView, available: true, group: 'administration' },
  { label: 'Payroll Operations', to: '/payroll/operations', permission: Permissions.payroll.runView, available: true, group: 'administration' },
  { label: 'Configuration Health', to: '/payroll/configuration-health', permission: Permissions.payroll.controlsView, available: true, group: 'administration' },
  { label: 'Attendance Overview', to: '/attendance', permission: Permissions.attendance.view, available: true, group: 'attendance', flyoutSection: 'Overview' },
  {
    label: 'Leave Dashboard',
    to: '/leave-management',
    anyPermission: [Permissions.leave.requestViewOwn, Permissions.leave.dashboardViewAll, Permissions.leave.approve],
    available: true,
    group: 'leave',
  },
  { label: 'Apply Leave', to: '/leave-management/apply', permission: Permissions.leave.requestCreate, available: true, requiresEmployeeIdentity: true, group: 'leave', flyoutSection: 'Requests & balances' },
  { label: 'My Leave Requests', to: '/leave-management/my-requests', permission: Permissions.leave.requestViewOwn, available: true, requiresEmployeeIdentity: true, group: 'leave', flyoutSection: 'Requests & balances' },
  {
    label: 'Team Leave Calendar',
    to: '/leave-management/team-calendar',
    anyPermission: [Permissions.leave.requestViewOwn, Permissions.leave.dashboardViewAll, Permissions.leave.approve],
    available: true,
    group: 'leave',
    flyoutSection: 'Overview',
  },
  { label: 'Leave Balance Import', to: '/leave-management/balances/import', permission: Permissions.leave.balanceImport, available: true, group: 'leave', flyoutSection: 'Requests & balances' },
  { label: 'Leave Approvals', to: '/leave-management/approvals', permission: Permissions.leave.approve, available: true, group: 'leave', flyoutSection: 'Approvals' },
  { label: 'Leave Reports', to: '/leave-management/reports', permission: Permissions.leave.reportsView, available: true, group: 'reports' },
  { label: 'Confirmation Dashboard', to: '/confirmation/dashboard', permission: Permissions.confirmation.dashboardViewAll, available: true, group: 'employee' },
  { label: 'Confirmation Reviews', to: '/confirmation/review-cases', permission: Permissions.confirmation.reviewCaseView, available: true, group: 'employee' },
  { label: 'Roster', to: '/attendance/roster', permission: Permissions.attendance.view, available: true, group: 'attendance', flyoutSection: 'Team operations' },
  { label: 'Roster Upload', to: '/attendance/roster-upload', permission: Permissions.attendance.view, available: true, group: 'attendance', flyoutSection: 'Team operations' },
  { label: 'My Attendance', to: '/attendance/my-attendance', permission: Permissions.attendance.view, available: true, requiresEmployeeIdentity: true, group: 'attendance', flyoutSection: 'My attendance' },
  { label: 'My Exceptions', to: '/attendance/my-exceptions', permission: Permissions.attendance.view, available: true, requiresEmployeeIdentity: true, group: 'attendance', flyoutSection: 'My attendance' },
  { label: 'My Monthly Attendance', to: '/attendance/my-monthly', permission: Permissions.attendance.monthlyViewSelf, available: true, requiresEmployeeIdentity: true, group: 'attendance', flyoutSection: 'My attendance' },
  { label: 'Monthly Finalization', to: '/attendance/monthly-finalization', permission: Permissions.attendance.monthlyViewAll, available: true, group: 'attendance', flyoutSection: 'Team operations' },
  { label: 'Attendance Requests', to: '/attendance/requests', permission: Permissions.attendance.view, available: true, requiresEmployeeIdentity: true, group: 'attendance', flyoutSection: 'My attendance' },
  { label: 'Team Attendance', to: '/attendance/team', permission: Permissions.attendance.view, available: true, group: 'attendance', flyoutSection: 'Team operations' },
  { label: 'Attendance Operations', to: '/attendance/operations', permission: Permissions.attendance.exceptionView, available: true, group: 'attendance', flyoutSection: 'Team operations' },
  { label: 'Attendance Reports', to: '/attendance/reports', permission: Permissions.attendance.reportView, available: true, group: 'reports' },
  { label: 'My Comp-Off', to: '/attendance/comp-off', permission: Permissions.attendance.compOffViewSelf, available: true, requiresEmployeeIdentity: true, group: 'attendance', flyoutSection: 'My attendance' },
  { label: 'Overtime', to: '/attendance/overtime', anyPermission: [Permissions.attendance.overtimeRequest, Permissions.attendance.overtimeViewTeam, Permissions.attendance.overtimeViewAll, Permissions.attendance.overtimeApprove, Permissions.attendance.overtimeFinalize, Permissions.attendance.overtimeReopen], available: true, group: 'attendance', flyoutSection: 'Team operations' },
  { label: 'Comp-Off Operations', to: '/attendance/comp-off/operations', anyPermission: [Permissions.attendance.compOffViewTeam, Permissions.attendance.compOffViewAll, Permissions.attendance.compOffManage, Permissions.attendance.compOffApprove], available: true, group: 'attendance', flyoutSection: 'Team operations' },
]

export const NAV_GROUPS: readonly NavGroup[] = [
  { id: 'employee', label: 'Employee Management', collapsible: true },
  { id: 'leave', label: 'Leave Management', collapsible: true },
  { id: 'attendance', label: 'Attendance Management', collapsible: true },
  { id: 'reports', label: 'Reports', collapsible: true },
  { id: 'administration', label: 'Administration', collapsible: true },
  { id: 'profile', label: 'User/Profile', collapsible: true },
]

const NAV_MODULE_ORDER: readonly NavModuleId[] = [
  'home',
  'people',
  'attendance',
  'leave',
  'payroll',
  'separation',
  'reports',
  'communication',
  'administration',
  'workspace',
]

const NAV_MODULE_LABELS: Record<NavModuleId, string> = {
  home: 'Home',
  people: 'People',
  attendance: 'Attendance',
  leave: 'Leave',
  payroll: 'Payroll',
  separation: 'Separation',
  reports: 'Reports',
  communication: 'Communication Center',
  administration: 'Administration',
  workspace: 'My Workspace',
}

function moduleForItem(item: NavItem): NavModuleId {
  if (item.to === '/dashboard') return 'home'
  if (item.group === 'profile') return 'workspace'
  if (item.group === 'reports') return 'reports'
  if (item.group === 'leave') return 'leave'
  if (item.group === 'attendance') return 'attendance'
  if (item.to.startsWith('/payroll/')) return 'payroll'
  if (item.to.startsWith('/separation/')) return 'separation'
  if (item.to.startsWith('/communication/')) return 'communication'
  if (item.group === 'administration') return 'administration'
  return 'people'
}

/** Canonical hierarchical navigation. Children remain the existing, permission-aware menu entries. */
export const NAV_MODULES: readonly NavModule[] = NAV_MODULE_ORDER.map(id => ({
  id,
  label: NAV_MODULE_LABELS[id],
  items: NAV_ITEMS.filter(item => moduleForItem(item) === id),
})).filter(module => module.items.length > 0)

/**
 * Search also includes authorized configuration destinations that intentionally do not belong in a
 * sidebar flyout. Routes remain represented once, so an existing operational link is not duplicated.
 */
const NAV_SEARCH_ITEMS: readonly NavItem[] = [
  ...NAV_ITEMS,
  ...CONFIGURATION_DESTINATIONS
    .filter(destination => !NAV_ITEMS.some(item => item.to === destination.to))
    .map(destination => ({
      label: destination.label,
      to: destination.to,
      permission: destination.permission,
      anyPermission: destination.anyPermission,
      available: true,
      group: 'administration' as const,
    })),
]

const NAV_SEARCH_MODULES: readonly NavModule[] = NAV_MODULE_ORDER.map(id => ({
  id,
  label: NAV_MODULE_LABELS[id],
  items: NAV_SEARCH_ITEMS.filter(item => moduleForItem(item) === id),
})).filter(module => module.items.length > 0)

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

export function visibleNavModules(can: (permission: string) => boolean, user?: AuthenticatedUser | null): NavModule[] {
  const visible = new Set(visibleNavItems(can, user))
  return NAV_MODULES
    .map(module => ({ ...module, items: module.items.filter(item => visible.has(item)) }))
    .filter(module => module.items.length > 0)
}

export function visibleNavSearchModules(can: (permission: string) => boolean, user?: AuthenticatedUser | null): NavModule[] {
  const visible = new Set(visibleNavItemsFrom(NAV_SEARCH_ITEMS, can, user))
  return NAV_SEARCH_MODULES
    .map(module => ({ ...module, items: module.items.filter(item => visible.has(item)) }))
    .filter(module => module.items.length > 0)
}

function visibleNavItemsFrom(items: readonly NavItem[], can: (permission: string) => boolean, user?: AuthenticatedUser | null): NavItem[] {
  return items.filter((item) =>
    (item.permission === undefined || can(item.permission)) &&
    (item.anyPermission === undefined || item.anyPermission.some(can)) &&
    (!item.requiresEmployeeIdentity || user?.employeeIdentity?.status === 'Linked'),
  )
}
