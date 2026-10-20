import type { AuthenticatedUser } from '../../api/types.ts'
import {
  dashboardWidgetRegistry,
  type DashboardContext,
  type DashboardWidgetDefinition,
} from './dashboardRegistry.ts'

export interface DashboardSection {
  category: DashboardWidgetDefinition['category']
  widgets: DashboardWidgetDefinition[]
}

export interface ComposedDashboard {
  widgets: DashboardWidgetDefinition[]
  sections: DashboardSection[]
  isFallback: boolean
}

function hasPermission(permissions: readonly string[], wanted: string): boolean {
  return permissions.some((permission) => permission.toLowerCase() === wanted.toLowerCase())
}

function hasRequiredPermissions(widget: DashboardWidgetDefinition, permissions: readonly string[]): boolean {
  if (widget.requiredPermissions.length === 0) return true
  return widget.permissionMode === 'all'
    ? widget.requiredPermissions.every((permission) => hasPermission(permissions, permission))
    : widget.requiredPermissions.some((permission) => hasPermission(permissions, permission))
}

function hasValidLinkedIdentity(user: AuthenticatedUser | null): boolean {
  const identity = user?.employeeIdentity
  return identity?.status === 'Linked' && Boolean(identity.employee?.id)
}

function isAuthorized(widget: DashboardWidgetDefinition, context: DashboardContext): boolean {
  if (!widget.profiles.includes(context.workspace) || !widget.supported) return false
  if (context.workspace === 'platform' && widget.authorizedDataScope !== 'platform') return false
  if (context.workspace === 'tenant' && widget.authorizedDataScope === 'platform') return false
  if (!hasRequiredPermissions(widget, context.permissions)) return false
  return widget.identityRequirement !== 'linked-employee' || hasValidLinkedIdentity(context.user)
}

/** Pure composition boundary for every dashboard workspace. It performs no I/O. */
export function composeDashboard(
  context: DashboardContext,
  registry: readonly DashboardWidgetDefinition[] = dashboardWidgetRegistry,
): ComposedDashboard {
  const widgets = [...new Map(
    registry.filter((widget) => isAuthorized(widget, context)).map((widget) => [widget.id, widget]),
  ).values()].sort((left, right) => left.priority - right.priority || left.id.localeCompare(right.id))

  const categoryOrder: DashboardWidgetDefinition['category'][] = ['kpi', 'workflow', 'analytics']
  const sections = categoryOrder
    .map((category) => ({ category, widgets: widgets.filter((widget) => widget.category === category) }))
    .filter((section) => section.widgets.length > 0)

  return { widgets, sections, isFallback: widgets.length === 0 }
}
