import { fireEvent, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Permissions } from '../auth/permissions.ts'
import { CONFIGURATION_DESTINATIONS, visibleConfigurationDestinations } from '../layout/navigation.ts'
import { renderAsUser } from '../test/renderWith.tsx'
import { makeUser } from '../test/fixtures.ts'
import { ConfigurationCenterPage } from './ConfigurationCenterPage.tsx'

describe('ConfigurationCenterPage', () => {
  it('classifies every verified destination once and keeps routes unique', () => {
    expect(CONFIGURATION_DESTINATIONS.length).toBeGreaterThan(0)
    expect(new Set(CONFIGURATION_DESTINATIONS.map(item => item.to)).size).toBe(CONFIGURATION_DESTINATIONS.length)
    expect(CONFIGURATION_DESTINATIONS.every(item => item.to.startsWith('/'))).toBe(true)
    expect(new Set(CONFIGURATION_DESTINATIONS.map(item => item.category))).toEqual(new Set([
      'Employee & Onboarding', 'Employee Masters', 'Leave Policies', 'Attendance & Work Schedules',
      'Payroll Configuration', 'Confirmation & Separation Policies', 'Security & Access Management',
    ]))
  })

  it('exposes exactly one Masters card and keeps Bank Master out of Configuration Center', () => {
    const masters = CONFIGURATION_DESTINATIONS.filter(item => item.label === 'Masters')
    expect(masters).toHaveLength(1)
    expect(masters[0]).toMatchObject({ to: '/masters/holding-companies', category: 'Employee Masters' })
    expect(CONFIGURATION_DESTINATIONS.some(item => item.label === 'Bank Master')).toBe(false)
    expect(visibleConfigurationDestinations(permission => permission === Permissions.bankMaster.view)).toEqual(masters)
  })

  it('exposes Employee Code Configuration exactly once', () => {
    expect(CONFIGURATION_DESTINATIONS.filter(item => item.label === 'Employee Code Configuration')).toHaveLength(1)
    expect(visibleConfigurationDestinations(permission => permission === Permissions.employeeCodeConfiguration.view)).toEqual([
      expect.objectContaining({ label: 'Employee Code Configuration', to: '/configuration/employee-code' }),
    ])
  })

  it('places Device Integration exactly once under Attendance & Work Schedules for authorized users', () => {
    const deviceCard = CONFIGURATION_DESTINATIONS.filter(item => item.label === 'Device Integration')
    expect(deviceCard).toHaveLength(1)
    expect(deviceCard[0]).toMatchObject({ to: '/attendance/devices', category: 'Attendance & Work Schedules' })

    renderAsUser(<ConfigurationCenterPage />, { user: makeUser({ permissions: [Permissions.attendance.deviceView] }) })
    expect(screen.getAllByRole('link', { name: /Device Integration/i })).toHaveLength(1)
    expect(screen.getByRole('link', { name: /Device Integration/i })).toHaveAttribute('href', '/attendance/devices')
    expect(screen.getByRole('heading', { name: 'Attendance & Work Schedules' })).toBeInTheDocument()
  })

  it('hides Device Integration from Configuration Center when the user lacks every device permission', () => {
    renderAsUser(<ConfigurationCenterPage />, { user: makeUser({ permissions: [Permissions.attendance.view] }) })
    expect(screen.queryByRole('link', { name: /Device Integration/i })).not.toBeInTheDocument()
  })

  it('keeps operational destinations out of Configuration Center', () => {
    expect(CONFIGURATION_DESTINATIONS.map(item => item.to)).not.toEqual(expect.arrayContaining([
      '/leave-management',
      '/leave-management/apply',
      '/leave-management/approvals',
      '/leave-management/balances/import',
      '/attendance',
      '/attendance/operations',
      '/attendance/roster-upload',
      '/attendance/overtime',
      '/confirmation/review-cases',
    ]))
  })

  it('shows only destinations granted by effective permissions', () => {
    const destinations = visibleConfigurationDestinations(permission => permission === Permissions.leave.policyView)
    expect(destinations.map(item => item.to)).toEqual([
      '/leave-management/policies',
      '/leave-management/working-day-calendar',
    ])

    renderAsUser(<ConfigurationCenterPage />, { user: makeUser({ permissions: [Permissions.leave.policyView] }) })
    expect(screen.getByRole('link', { name: /Leave Policies/i })).toHaveAttribute('href', '/leave-management/policies')
    expect(screen.getByRole('link', { name: /Working Day Calendar/i })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Role Management/i })).not.toBeInTheDocument()
  })

  it('supports custom multi-permission users without role-name checks', () => {
    renderAsUser(<ConfigurationCenterPage />, {
      user: makeUser({ roles: ['Custom operator'], permissions: [Permissions.employeeCodeConfiguration.view, Permissions.attendance.view] }),
    })
    expect(screen.getByRole('link', { name: /Employee Code Configuration/i })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Shift Patterns/i })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Leave Policies/i })).not.toBeInTheDocument()
  })

  it('filters by destination title and category and exposes a no-results state', () => {
    renderAsUser(<ConfigurationCenterPage />, { user: makeUser({ permissions: [Permissions.attendance.view, Permissions.leave.policyView] }) })
    const search = screen.getByRole('searchbox', { name: /search configuration/i })
    fireEvent.change(search, { target: { value: 'attendance' } })
    expect(screen.getByRole('link', { name: /Shift Patterns/i })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Leave Policies/i })).not.toBeInTheDocument()
    fireEvent.change(search, { target: { value: 'does not exist' } })
    expect(screen.getByRole('status')).toHaveTextContent(/No configuration destinations found/i)
  })

  it('redirects users with no authorized configuration destination and makes cards navigable', () => {
    renderAsUser(<ConfigurationCenterPage />, { user: makeUser({ permissions: [] }) })
    expect(screen.queryByRole('heading', { name: 'Configuration Center' })).not.toBeInTheDocument()

    renderAsUser(<ConfigurationCenterPage />, { user: makeUser({ permissions: [Permissions.pageAccess.view] }) })
    expect(screen.getByRole('link', { name: /Page Access Management/i })).toHaveAttribute('href', '/page-access-management')
  })
})
