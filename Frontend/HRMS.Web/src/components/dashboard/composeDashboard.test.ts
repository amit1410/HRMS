import { describe, expect, it } from 'vitest'
import { Permissions } from '../../auth/permissions.ts'
import { PlatformPermissions } from '../../auth/platformPermissions.ts'
import { composeDashboard } from './composeDashboard.ts'
import { dashboardWidgetRegistry, type DashboardContext, type DashboardWidgetDefinition } from './dashboardRegistry.ts'

const user = (identity: 'linked' | 'unlinked' | 'invalid' = 'linked') => ({
  id: 'u1', tenantId: 't1', tenantCode: 'T1', tenantName: 'Tenant', email: 'u@test', firstName: 'U', lastName: 'Ser', fullName: 'U Ser', roles: [], permissions: [],
  employeeIdentity: identity === 'linked' ? { status: 'Linked' as const, revision: 'r1', linkId: 'l1', employee: { id: 'e1', displayName: 'Employee' }, employmentEligibility: 'ActiveEmployment' as const, businessDate: '2026-01-01' } : identity === 'unlinked' ? null : { status: 'Invalid' as const, revision: 'r1', linkId: 'l1', employee: { id: 'e1', displayName: 'Employee' }, employmentEligibility: 'RequiresReview' as const, businessDate: '2026-01-01' },
})

function context(permissions: string[], workspace: 'tenant' | 'platform' = 'tenant', identity: 'linked' | 'unlinked' | 'invalid' = 'linked'): DashboardContext {
  return { workspace, permissions, user: workspace === 'tenant' ? { ...user(identity), permissions } : null }
}

const widget = (overrides: Partial<DashboardWidgetDefinition> = {}): DashboardWidgetDefinition => ({
  id: 'x', title: 'X', category: 'kpi', requiredPermissions: [], permissionMode: 'all', identityRequirement: 'none', authorizedDataScope: 'tenant', apiSource: '/api/x', priority: 1, profiles: ['tenant'], supported: true, ...overrides,
})

describe('composeDashboard', () => {
  it('evaluates all and any permissions without using role names', () => {
    const all = widget({ id: 'all', requiredPermissions: [Permissions.employee.view, Permissions.employee.export], permissionMode: 'all' })
    const any = widget({ id: 'any', requiredPermissions: [Permissions.department.view, Permissions.designation.view], permissionMode: 'any', priority: 2 })
    expect(composeDashboard(context([Permissions.employee.view, Permissions.department.view]), [all, any]).widgets.map((item) => item.id)).toEqual(['any'])
  })

  it('keeps platform and tenant scopes isolated', () => {
    const platform = widget({ id: 'platform', authorizedDataScope: 'platform', profiles: ['platform'], requiredPermissions: [PlatformPermissions.view] })
    const tenant = widget({ id: 'tenant' })
    expect(composeDashboard(context([PlatformPermissions.view], 'platform'), [platform, tenant]).widgets.map((item) => item.id)).toEqual(['platform'])
    expect(composeDashboard(context([PlatformPermissions.view]), [platform, tenant]).widgets.map((item) => item.id)).toEqual(['tenant'])
  })

  it('requires a valid linked employee for self-service widgets', () => {
    const self = widget({ id: 'self', identityRequirement: 'linked-employee' })
    expect(composeDashboard(context([], 'tenant', 'linked'), [self]).widgets).toHaveLength(1)
    expect(composeDashboard(context([], 'tenant', 'unlinked'), [self]).widgets).toHaveLength(0)
    expect(composeDashboard(context([], 'tenant', 'invalid'), [self]).widgets).toHaveLength(0)
  })

  it('deduplicates, excludes unsupported widgets, sorts, groups, and falls back safely', () => {
    const definitions = [
      widget({ id: 'z', priority: 20, category: 'analytics' }),
      widget({ id: 'z', priority: 1, category: 'kpi' }),
      widget({ id: 'unsupported', supported: false, priority: 0 }),
      widget({ id: 'a', priority: 10, category: 'workflow' }),
    ]
    const composed = composeDashboard(context([]), definitions)
    expect(composed.widgets.map((item) => item.id)).toEqual(['z', 'a'])
    expect(composed.sections.map((section) => section.category)).toEqual(['kpi', 'workflow'])
    expect(composeDashboard(context([]), [widget({ supported: false })]).isFallback).toBe(true)
  })

  it('reflects permission changes immediately with no cache', () => {
    const definition = widget({ requiredPermissions: [Permissions.employee.view] })
    expect(composeDashboard(context([Permissions.employee.view]), [definition]).widgets).toHaveLength(1)
    expect(composeDashboard(context([]), [definition]).widgets).toHaveLength(0)
  })

  it('composes HR, manager, administration, and self-service slices from effective permissions', () => {
    const permissions = [
      Permissions.employee.view,
      Permissions.leave.dashboardViewAll,
      Permissions.attendance.view,
      Permissions.user.view,
      Permissions.pageAccess.view,
      Permissions.leave.balanceViewOwn,
    ]
    const ids = composeDashboard(context(permissions), dashboardWidgetRegistry).widgets.map((item) => item.id)
    expect(ids).toEqual(expect.arrayContaining([
      'tenant.hr-leave-summary',
      'tenant.manager-attendance',
      'tenant.admin-actions',
      'tenant.hr-actions',
      'tenant.self-profile',
      'tenant.self-leave-balances',
    ]))
    expect(ids).not.toContain('platform.tenant-administration')
  })

  it('never composes self-service widgets for an invalid or missing linked identity', () => {
    const selfIds = dashboardWidgetRegistry.filter((item) => item.id.startsWith('tenant.self-'))
    const permissions = [Permissions.leave.requestViewOwn, Permissions.leave.balanceViewOwn]
    expect(composeDashboard(context(permissions, 'tenant', 'invalid'), selfIds).widgets).toHaveLength(0)
    expect(composeDashboard(context(permissions, 'tenant', 'unlinked'), selfIds).widgets).toHaveLength(0)
  })
})
