import { describe, expect, it } from 'vitest'
import { Permissions } from '../auth/permissions.ts'
import { NAV_ITEMS, visibleNavItems } from './navigation.ts'

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
    const items = visibleNavItems(canAlways)
    expect(items).toHaveLength(NAV_ITEMS.length)
  })

  it('returns only Dashboard when the user has no module permissions', () => {
    const items = visibleNavItems(canNever)
    expect(items).toHaveLength(1)
    expect(items.at(0)?.label).toBe('Dashboard')
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
    const items = visibleNavItems(canAlways)
    expect(items.map((i) => i.to)).toEqual(NAV_ITEMS.map((i) => i.to))
  })

  it('is case-insensitive in the permission check', () => {
    const items = visibleNavItems(canWith('employee.view'))
    expect(items.map((i) => i.label)).toContain('Employees')
  })
})
