import { describe, expect, it } from 'vitest'
import { Permissions } from '../auth/permissions.ts'
import { NAV_ITEMS, visibleNavItems } from './navigation.ts'
import { makeUser } from '../test/fixtures.ts'

const linkedUser = makeUser({ employeeIdentity: { status: 'Linked', revision: null, linkId: 'link-1', employee: { id: 'employee-1', displayName: 'Priya Raman', employeeCode: 'EMP-1' }, employmentEligibility: 'ActiveEmployment', businessDate: '2026-09-08' } })

function canAlways(): boolean {
  return true
}

function canNever(): boolean {
  return false
}

function canWith(...granted: string[]): (permission: string) => boolean {
  const set = new Set(granted.map((p) => p.toLowerCase()))
  return (permission) => set.has(permission.toLowerCase())
}

describe('visibleNavItems', () => {
  it('returns all items when the user has every permission', () => {
    const items = visibleNavItems(canAlways, linkedUser)
    expect(items).toHaveLength(NAV_ITEMS.length)
  })

  it('returns public signed-in entries when the user has no module permissions', () => {
    const items = visibleNavItems(canNever)
    expect(items).toHaveLength(4)
    expect(items.map(item => item.label)).toEqual(['Dashboard', 'Change Password', 'Apply Leave (Preview)', 'My Leave Requests'])
  })

  it('hides items whose required permission the user lacks', () => {
    const items = visibleNavItems(canWith(Permissions.employee.view))

    const labels = items.map((i) => i.label)
    expect(labels).toContain('Dashboard')
    expect(labels).toContain('Employees')
    expect(labels).not.toContain('Departments')
    expect(labels).not.toContain('Designations')
  })

  it('always shows Dashboard (no permission required)', () => {
    const items = visibleNavItems(canNever)
    expect(items.map((i) => i.label)).toContain('Dashboard')
  })

  it('shows My Profile only when the account has a linked employee identity', () => {
    expect(visibleNavItems(canNever).map(item => item.label)).not.toContain('My Profile')
    expect(visibleNavItems(canNever, linkedUser).map(item => item.label)).toContain('My Profile')
  })

  it('filters independently per permission', () => {
    const items = visibleNavItems(
      canWith(Permissions.department.view, Permissions.designation.view),
    )

    const labels = items.map((i) => i.label)
    expect(labels).toContain('Dashboard')
    expect(labels).not.toContain('Employees')
    expect(labels).toContain('Masters')
  })

  it('returns the same items as NAV_ITEMS (no extra or missing entries)', () => {
    const items = visibleNavItems(canAlways, linkedUser)
    expect(items.map((i) => i.to)).toEqual(NAV_ITEMS.map((i) => i.to))
  })

  it('is case-insensitive in the permission check', () => {
    const items = visibleNavItems(canWith('employee.view'))
    expect(items.map((i) => i.label)).toContain('Employees')
  })
})
