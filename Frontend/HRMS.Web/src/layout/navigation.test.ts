import { describe, expect, it } from 'vitest'
import { Permissions } from '../auth/permissions.ts'
import { NAV_GROUPS, NAV_ITEMS, visibleNavGroups, visibleNavItems } from './navigation.ts'
import { makeUser } from '../test/fixtures.ts'

const linkedUser = makeUser({ employeeIdentity: { status: 'Linked', revision: null, linkId: 'link-1', employee: { id: 'employee-1', displayName: 'Priya Raman', employeeCode: 'EMP-1' }, employmentEligibility: 'ActiveEmployment', businessDate: '2026-09-08' } })
const unlinkedUser = makeUser({ permissions: [Permissions.leave.requestCreate, Permissions.leave.requestViewOwn] })

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
    expect(items).toHaveLength(2)
    expect(items.map(item => item.label)).toEqual(['Dashboard', 'Change Password'])
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

  it('hides employee self-service leave actions when the account is not linked', () => {
    const labels = visibleNavItems((permission) => unlinkedUser.permissions.includes(permission), unlinkedUser).map(item => item.label)
    expect(labels).not.toContain('Apply Leave')
    expect(labels).not.toContain('My Leave Requests')
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

describe('grouped report navigation', () => {
  it('keeps Reports as a separate top-level group', () => {
    expect(NAV_GROUPS.map(group => group.label)).toContain('Reports')
    const reports = NAV_GROUPS.find(group => group.id === 'reports')
    expect(reports?.collapsible).toBe(true)
    expect(NAV_ITEMS.filter(item => item.group === 'reports').map(item => item.label)).toEqual(['Leave Reports', 'Attendance Reports'])
  })

  it('places report links only under Reports with their original permissions and routes', () => {
    const reportItems = NAV_ITEMS.filter(item => item.group === 'reports')
    expect(reportItems).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: 'Leave Reports', to: '/leave-management/reports', permission: Permissions.leave.reportsView }),
      expect.objectContaining({ label: 'Attendance Reports', to: '/attendance/reports', permission: Permissions.attendance.reportView }),
    ]))
    expect(NAV_ITEMS.filter(item => item.group === 'leave').map(item => item.label)).not.toContain('Leave Reports')
    expect(NAV_ITEMS.filter(item => item.group === 'attendance').map(item => item.label)).not.toContain('Attendance Reports')
  })

  it('filters Reports children independently and hides the empty Reports group', () => {
    const leaveOnly = visibleNavGroups(canWith(Permissions.leave.reportsView)).find(group => group.id === 'reports')
    expect(leaveOnly?.items.map(item => item.label)).toEqual(['Leave Reports'])
    const attendanceOnly = visibleNavGroups(canWith(Permissions.attendance.reportView)).find(group => group.id === 'reports')
    expect(attendanceOnly?.items.map(item => item.label)).toEqual(['Attendance Reports'])
    expect(visibleNavGroups(canNever).some(group => group.id === 'reports')).toBe(false)
  })
})
