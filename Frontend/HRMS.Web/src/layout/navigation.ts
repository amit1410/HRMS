import { Permissions } from '../auth/permissions.ts'

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
  {
    label: 'Masters',
    to: '/masters/holding-companies',
    anyPermission: [Permissions.geography.view, Permissions.department.view, Permissions.designation.view],
    available: true,
  },
]

export function visibleNavItems(can: (permission: string) => boolean): NavItem[] {
  return NAV_ITEMS.filter((item) =>
    (item.permission === undefined || can(item.permission)) &&
    (item.anyPermission === undefined || item.anyPermission.some(can)),
  )
}
