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
  { label: 'My Profile', to: '/my-profile', available: true, requiresEmployeeIdentity: true },
  { label: 'Change Password', to: '/change-password', available: true },
  {
    label: 'Employees',
    to: '/employees',
    permission: Permissions.employee.view,
    available: true,
  },
  {
    label: 'Employee Code Configuration',
    to: '/configuration/employee-code',
    permission: Permissions.employeeCodeConfiguration.view,
    available: true,
  },
  { label: 'Login Settings', to: '/configuration/login-settings', permission: Permissions.user.edit, available: true },
  { label: 'Password Recovery', to: '/configuration/password-recovery', permission: Permissions.user.edit, available: true },
  {
    label: 'Masters',
    to: '/masters/holding-companies',
    anyPermission: [Permissions.geography.view, Permissions.department.view, Permissions.designation.view],
    available: true,
  },
  {
    label: 'Account–Employee Links',
    to: '/administration/account-employee-links',
    permission: Permissions.accountEmployeeLink.view,
    available: true,
  },
  { label: 'Leave Types', to: '/leave-management/types', permission: Permissions.leave.typeManage, available: true },
  { label: 'Leave Periods', to: '/leave-management/periods', permission: Permissions.leave.periodManage, available: true },
  { label: 'Leave Policies', to: '/leave-management/policies', permission: Permissions.leave.policyView, available: true },
  { label: 'Leave Dashboard', to: '/leave-management', available: true },
  { label: 'Apply Leave (Preview)', to: '/leave-management/apply', available: true },
  { label: 'My Leave Requests', to: '/leave-management/my-requests', available: true },
  { label: 'Team Leave Calendar', to: '/leave-management/team-calendar', available: true },
  { label: 'Leave Balance Import', to: '/leave-management/balances/import', permission: Permissions.leave.balanceImport, available: true },
  { label: 'Leave Approvals', to: '/leave-management/approvals', permission: Permissions.leave.approve, available: true },
]

export function visibleNavItems(can: (permission: string) => boolean, user?: AuthenticatedUser | null): NavItem[] {
  return NAV_ITEMS.filter((item) =>
    (item.permission === undefined || can(item.permission)) &&
    (item.anyPermission === undefined || item.anyPermission.some(can)) &&
    (!item.requiresEmployeeIdentity || user?.employeeIdentity?.status === 'Linked'),
  )
}
