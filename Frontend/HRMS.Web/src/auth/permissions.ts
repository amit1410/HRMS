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
    typeManage: 'Leave.TypeManage',
    periodManage: 'Leave.PeriodManage',
    policyView: 'Leave.PolicyView',
    policyManage: 'Leave.PolicyManage',
    policyPublish: 'Leave.PolicyPublish',
    approve: 'Leave.Approve',
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
