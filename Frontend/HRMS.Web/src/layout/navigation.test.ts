import { describe, expect, it } from 'vitest'
import { Permissions } from '../auth/permissions.ts'
import { CONFIGURATION_DESTINATIONS, NAV_GROUPS, NAV_ITEMS, visibleConfigurationDestinations, visibleNavGroups, visibleNavItems, visibleNavModules, visibleNavSearchModules } from './navigation.ts'
import { navigationSearchResults } from './navigationSearch.ts'
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
  it('places Welcome Invitations in the Communication Center for authorized users', () => {
    const authorized = visibleNavModules(canWith(Permissions.welcomeInvitation.view), linkedUser)
    expect(authorized.find(module => module.label === 'Communication Center')?.items).toEqual([
      expect.objectContaining({ label: 'Welcome Invitations', to: '/communication/welcome-invitations' }),
    ])
    expect(visibleNavModules(canNever, linkedUser).map(module => module.label)).not.toContain('Communication Center')
  })

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

  it('requires Attendance.View and a linked employee for personal attendance navigation', () => {
    const linkedAttendance = visibleNavItems(canWith(Permissions.attendance.view), linkedUser).map(item => item.label)
    expect(linkedAttendance).toContain('My Attendance')
    expect(linkedAttendance).toContain('Attendance Requests')

    const withoutAttendance = visibleNavItems(canNever, linkedUser).map(item => item.label)
    expect(withoutAttendance).not.toContain('My Attendance')
    expect(withoutAttendance).not.toContain('Attendance Requests')
  })

  it('filters independently per permission', () => {
    const items = visibleNavItems(
      canWith(Permissions.department.view, Permissions.designation.view),
    )

    const labels = items.map((i) => i.label)
    expect(labels).toContain('Dashboard')
    expect(labels).not.toContain('Employees')
    expect(labels).not.toContain('Masters')
  })

  it('keeps configuration-only confirmation setup out of the People flyout', () => {
    const labels = NAV_ITEMS.map(item => item.label)
    expect(labels).toContain('Confirmation Reviews')
    expect(labels).not.toContain('Confirmation Policies')
    expect(labels).not.toContain('Confirmation Scheduler')
    expect(labels).not.toContain('Review Questions')
    expect(labels).not.toContain('Rating Scales')
  })

  it('keeps configuration destinations out of People while preserving authorized global search', () => {
    const labels = NAV_ITEMS.map(item => item.label)
    expect(labels).not.toContain('Employee Code Configuration')
    expect(labels).not.toContain('Masters')

    const searchModules = visibleNavSearchModules(
      permission => permission === Permissions.employeeCodeConfiguration.view || permission === Permissions.bankMaster.view,
    )
    const results = navigationSearchResults(searchModules, '')
    expect(results.map(result => result.item.label)).toEqual(expect.arrayContaining(['Employee Code Configuration', 'Masters']))
    expect(results.filter(result => result.item.label === 'Employee Code Configuration')).toHaveLength(1)
    expect(results.filter(result => result.item.label === 'Masters')).toHaveLength(1)
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

describe('attendance and leave modules', () => {
  it('exposes separate permission-aware top-level modules and no combined module', () => {
    const modules = visibleNavModules(canAlways, linkedUser)
    expect(modules.map(module => module.label)).toContain('Attendance')
    expect(modules.map(module => module.label)).toContain('Leave')
    expect(modules.map(module => module.label)).not.toContain('Attendance & Leave')
    expect(modules.find(module => module.label === 'Attendance')?.items.map(item => item.label)).toEqual(expect.arrayContaining(['Attendance Overview', 'Attendance Operations', 'Overtime']))
    expect(modules.find(module => module.label === 'Leave')?.items.map(item => item.label)).toEqual(expect.arrayContaining(['Leave Dashboard', 'Leave Approvals']))
  })

  it('shows only the operational module authorized for the user', () => {
    const attendanceOnly = visibleNavModules(canWith(Permissions.attendance.view), linkedUser).map(module => module.label)
    expect(attendanceOnly).toContain('Attendance')
    expect(attendanceOnly).not.toContain('Leave')

    const leaveOnly = visibleNavModules(canWith(Permissions.leave.requestViewOwn), linkedUser).map(module => module.label)
    expect(leaveOnly).toContain('Leave')
    expect(leaveOnly).not.toContain('Attendance')
  })

  it('keeps setup destinations in Configuration Center and operational destinations in their module', () => {
    const navLabels = NAV_ITEMS.map(item => item.label)
    expect(navLabels).not.toEqual(expect.arrayContaining(['Leave Types', 'Leave Policies', 'Shifts', 'Shift Patterns', 'Shift Applicability', 'Employee Code Configuration']))
    expect(navLabels).toEqual(expect.arrayContaining(['Leave Balance Import', 'Attendance Operations', 'Roster Upload']))
  })

  it('moves Device Integration out of the Attendance flyout without duplicating its route', () => {
    expect(NAV_ITEMS.filter(item => item.label === 'Device Integration')).toHaveLength(0)
    expect(NAV_ITEMS.some(item => item.to === '/attendance/devices')).toBe(false)
    expect(CONFIGURATION_DESTINATIONS.filter(item => item.label === 'Device Integration')).toHaveLength(1)
    expect(CONFIGURATION_DESTINATIONS.filter(item => item.to === '/attendance/devices')).toHaveLength(1)
  })

  it('preserves the device-specific effective permissions for the Configuration Center card and search', () => {
    const devicePermissions = [
      Permissions.attendance.deviceView,
      Permissions.attendance.deviceManage,
      Permissions.attendance.deviceMapping,
      Permissions.attendance.deviceSync,
      Permissions.attendance.deviceViewHistory,
    ]
    expect(CONFIGURATION_DESTINATIONS.find(item => item.to === '/attendance/devices')).toMatchObject({
      category: 'Attendance & Work Schedules',
      anyPermission: devicePermissions,
    })
    expect(visibleConfigurationDestinations(permission => permission === Permissions.attendance.deviceSync).map(item => item.label)).toContain('Device Integration')
    expect(visibleConfigurationDestinations(() => false).map(item => item.label)).not.toContain('Device Integration')

    const searchModules = visibleNavSearchModules(permission => permission === Permissions.attendance.deviceSync)
    const results = navigationSearchResults(searchModules, 'device integration')
    expect(results).toHaveLength(1)
    expect(results[0]).toMatchObject({ item: { label: 'Device Integration', to: '/attendance/devices' } })
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
