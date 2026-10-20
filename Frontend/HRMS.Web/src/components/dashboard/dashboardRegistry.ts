import type { AuthenticatedUser } from '../../api/types.ts'
import { Permissions } from '../../auth/permissions.ts'
import { PlatformPermissions } from '../../auth/platformPermissions.ts'

export type DashboardWorkspace = 'platform' | 'tenant'
export type PermissionMode = 'all' | 'any'
export type IdentityRequirement = 'none' | 'linked-employee'
export type AuthorizedDataScope = 'platform' | 'tenant' | 'team' | 'self'
export type DashboardCategory = 'kpi' | 'workflow' | 'analytics'

export interface DashboardContext {
  workspace: DashboardWorkspace
  permissions: readonly string[]
  user: AuthenticatedUser | null
}

export interface DashboardWidgetDefinition {
  id: string
  title: string
  category: DashboardCategory
  requiredPermissions: readonly string[]
  permissionMode: PermissionMode
  identityRequirement: IdentityRequirement
  authorizedDataScope: AuthorizedDataScope
  apiSource: string
  priority: number
  profiles: readonly DashboardWorkspace[]
  route?: string
  supported: boolean
}

/** Declarative inventory. Presentation profiles never grant access; permissions and identity do. */
export const dashboardWidgetRegistry: readonly DashboardWidgetDefinition[] = [
  {
    id: 'tenant.employee-count', title: 'Total employees', category: 'kpi',
    requiredPermissions: [Permissions.employee.view], permissionMode: 'all', identityRequirement: 'none',
    authorizedDataScope: 'tenant', apiSource: '/api/employees', priority: 10, profiles: ['tenant'], supported: true,
  },
  {
    id: 'tenant.active-employee-count', title: 'Active employees', category: 'kpi',
    requiredPermissions: [Permissions.employee.view], permissionMode: 'all', identityRequirement: 'none',
    authorizedDataScope: 'tenant', apiSource: '/api/employees', priority: 20, profiles: ['tenant'], supported: true,
  },
  {
    id: 'tenant.department-count', title: 'Departments', category: 'kpi',
    requiredPermissions: [Permissions.department.view], permissionMode: 'all', identityRequirement: 'none',
    authorizedDataScope: 'tenant', apiSource: '/api/departments', priority: 30, profiles: ['tenant'], supported: true,
  },
  {
    id: 'tenant.designation-count', title: 'Designations', category: 'kpi',
    requiredPermissions: [Permissions.designation.view], permissionMode: 'all', identityRequirement: 'none',
    authorizedDataScope: 'tenant', apiSource: '/api/designations', priority: 40, profiles: ['tenant'], supported: true,
  },
  {
    id: 'tenant.department-headcount', title: 'Headcount by department', category: 'analytics',
    requiredPermissions: [Permissions.department.view], permissionMode: 'all', identityRequirement: 'none',
    authorizedDataScope: 'tenant', apiSource: '/api/departments', priority: 50, profiles: ['tenant'], supported: true,
  },
  {
    id: 'tenant.recent-hires', title: 'Recent hires', category: 'analytics',
    requiredPermissions: [Permissions.employee.view], permissionMode: 'all', identityRequirement: 'none',
    authorizedDataScope: 'tenant', apiSource: '/api/employees', priority: 60, profiles: ['tenant'], supported: true,
  },
  {
    id: 'tenant.employee-actions', title: 'Employee actions', category: 'workflow',
    requiredPermissions: [Permissions.employee.create, Permissions.employee.view, Permissions.employeeCodeConfiguration.view],
    permissionMode: 'any', identityRequirement: 'none', authorizedDataScope: 'tenant', apiSource: 'routes', priority: 70,
    profiles: ['tenant'], supported: true,
  },
  {
    id: 'tenant.self-service', title: 'My employee profile', category: 'workflow',
    requiredPermissions: [Permissions.leave.requestViewOwn, Permissions.leave.balanceViewOwn], permissionMode: 'any',
    identityRequirement: 'linked-employee', authorizedDataScope: 'self', apiSource: '/api/me', priority: 80,
    profiles: ['tenant'], route: '/profile', supported: false,
  },
  {
    id: 'tenant.hr-leave-summary', title: 'HR leave operations', category: 'analytics',
    requiredPermissions: [Permissions.leave.dashboardViewAll], permissionMode: 'all', identityRequirement: 'none',
    authorizedDataScope: 'tenant', apiSource: '/api/leave-dashboard/hr-summary', priority: 110,
    profiles: ['tenant'], route: '/leave-management', supported: true,
  },
  {
    id: 'tenant.attendance-operations', title: 'Attendance operations', category: 'analytics',
    requiredPermissions: [Permissions.attendance.exceptionView], permissionMode: 'all', identityRequirement: 'none',
    authorizedDataScope: 'tenant', apiSource: '/api/attendance/operations/dashboard', priority: 120,
    profiles: ['tenant'], route: '/attendance/operations', supported: true,
  },
  {
    id: 'tenant.manager-attendance', title: 'Team attendance', category: 'analytics',
    requiredPermissions: [Permissions.attendance.view], permissionMode: 'all', identityRequirement: 'linked-employee',
    authorizedDataScope: 'team', apiSource: '/api/attendance/manager/team', priority: 130,
    profiles: ['tenant'], route: '/attendance/team', supported: true,
  },
  {
    id: 'tenant.self-profile', title: 'My profile', category: 'kpi',
    requiredPermissions: [Permissions.leave.requestViewOwn, Permissions.leave.balanceViewOwn, Permissions.payroll.payslipViewOwn], permissionMode: 'any',
    identityRequirement: 'linked-employee', authorizedDataScope: 'self', apiSource: '/api/me/profile', priority: 140,
    profiles: ['tenant'], route: '/my-profile', supported: true,
  },
  {
    id: 'tenant.self-leave-balances', title: 'My leave balances', category: 'kpi',
    requiredPermissions: [Permissions.leave.balanceViewOwn], permissionMode: 'all', identityRequirement: 'linked-employee',
    authorizedDataScope: 'self', apiSource: '/api/leave-balances/mine', priority: 150,
    profiles: ['tenant'], route: '/leave-management/my-requests', supported: true,
  },
  {
    id: 'tenant.self-leave-requests', title: 'My leave requests', category: 'workflow',
    requiredPermissions: [Permissions.leave.requestViewOwn], permissionMode: 'all', identityRequirement: 'linked-employee',
    authorizedDataScope: 'self', apiSource: '/api/leave-requests', priority: 160,
    profiles: ['tenant'], route: '/leave-management/my-requests', supported: true,
  },
  {
    id: 'tenant.admin-actions', title: 'Administration', category: 'workflow',
    requiredPermissions: [Permissions.user.view, Permissions.roleManagement.assignmentView, Permissions.pageAccess.view, Permissions.user.edit],
    permissionMode: 'any', identityRequirement: 'none', authorizedDataScope: 'tenant', apiSource: 'routes', priority: 170,
    profiles: ['tenant'], supported: true,
  },
  {
    id: 'tenant.hr-actions', title: 'HR operations', category: 'workflow',
    requiredPermissions: [Permissions.employee.view, Permissions.leave.dashboardViewAll, Permissions.confirmation.reviewCaseView, Permissions.separation.hrReview],
    permissionMode: 'any', identityRequirement: 'none', authorizedDataScope: 'tenant', apiSource: 'routes', priority: 180,
    profiles: ['tenant'], supported: true,
  },
  {
    id: 'platform.tenant-administration', title: 'Tenant administration', category: 'workflow',
    requiredPermissions: [PlatformPermissions.view], permissionMode: 'all', identityRequirement: 'none',
    authorizedDataScope: 'platform', apiSource: '/api/platform/tenants', priority: 10, profiles: ['platform'], supported: true,
  },
]
