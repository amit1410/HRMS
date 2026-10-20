import { MemoryRouter, useLocation } from 'react-router-dom'
import userEvent from '@testing-library/user-event'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AuthContext, type AuthContextValue } from '../auth/authContext.ts'
import { hasAnyPermission, hasPermission, Permissions } from '../auth/permissions.ts'
import { makeUser } from '../test/fixtures.ts'
import { CommandPalette } from './CommandPalette.tsx'

function LocationProbe() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname}</output>
}

function renderPalette(overrides: Partial<AuthContextValue> = {}) {
  const user = overrides.user ?? makeUser({
    permissions: [Permissions.employee.view, Permissions.attendance.view, Permissions.leave.requestViewOwn],
    employeeIdentity: { status: 'Linked', revision: null, linkId: 'link-1', employee: { id: 'employee-1', displayName: 'Priya Raman', employeeCode: 'EMP-1' }, employmentEligibility: 'ActiveEmployment', businessDate: '2026-09-08' },
  })
  const value: AuthContextValue = {
    status: 'authenticated',
    user,
    login: async () => undefined,
    logout: async () => undefined,
    can: permission => hasPermission(user, permission),
    canAny: permissions => hasAnyPermission(user, permissions),
    ...overrides,
  }
  const triggerRef = { current: null } as React.RefObject<HTMLButtonElement | null>
  return render(
    <MemoryRouter initialEntries={['/dashboard']}>
      <AuthContext.Provider value={value}>
        <button ref={triggerRef}>Search pages and actions</button>
        <CommandPalette open onClose={overrides.logout ? vi.fn() : vi.fn()} triggerRef={triggerRef} />
        <LocationProbe />
      </AuthContext.Provider>
    </MemoryRouter>,
  )
}

describe('CommandPalette', () => {
  it('focuses the search field, groups only authorized results, and supports aliases', async () => {
    renderPalette()
    const input = await screen.findByRole('combobox', { name: 'Search pages and actions' })
    expect(input).toHaveFocus()
    expect(screen.getByRole('heading', { name: 'People' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Attendance' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Leave' })).toBeInTheDocument()
    expect(screen.queryByText('Role Management')).not.toBeInTheDocument()

    await userEvent.type(input, 'staff')
    expect(screen.getByRole('option', { name: /Employees/ })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: /Leave Dashboard/ })).not.toBeInTheDocument()
  })

  it('supports arrow selection, Enter navigation, and empty states', async () => {
    const first = renderPalette()
    const input = await screen.findByRole('combobox', { name: 'Search pages and actions' })
    await userEvent.clear(input)
    await userEvent.type(input, 'leave')
    await userEvent.keyboard('{ArrowDown}{Enter}')

    expect(screen.getByTestId('location')).toHaveTextContent('/leave-management')

    first.unmount()
    renderPalette()
    const secondInput = await screen.findByRole('combobox', { name: 'Search pages and actions' })
    await userEvent.type(secondInput, 'does-not-exist')
    expect(screen.getByText('No matching pages')).toBeInTheDocument()
  })

  it('searches configuration-only destinations without adding them to a sidebar flyout', async () => {
    renderPalette({ user: makeUser({ permissions: [Permissions.employeeCodeConfiguration.view, Permissions.bankMaster.view] }) })
    const input = await screen.findByRole('combobox', { name: 'Search pages and actions' })
    await userEvent.type(input, 'employee code')
    expect(screen.getByRole('option', { name: /Employee Code Configuration/ })).toBeInTheDocument()
  })

  it('finds Device Integration exactly once for users with a device permission', async () => {
    renderPalette({ user: makeUser({ permissions: [Permissions.attendance.deviceSync] }) })
    const input = await screen.findByRole('combobox', { name: 'Search pages and actions' })
    await userEvent.type(input, 'device integration')
    expect(screen.getAllByRole('option', { name: /Device Integration/ })).toHaveLength(1)
    expect(screen.queryByRole('option', { name: /Attendance & Leave/ })).not.toBeInTheDocument()
  })

  it('does not search Device Integration for users without a device permission', async () => {
    renderPalette({ user: makeUser({ permissions: [Permissions.attendance.view] }) })
    const input = await screen.findByRole('combobox', { name: 'Search pages and actions' })
    await userEvent.type(input, 'device integration')
    expect(screen.queryByRole('option', { name: /Device Integration/ })).not.toBeInTheDocument()
  })

  it('closes on Escape and restores focus to the trigger', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    const trigger = document.createElement('button')
    document.body.append(trigger)
    const triggerRef = { current: trigger } as React.RefObject<HTMLButtonElement | null>
    render(
      <MemoryRouter>
        <AuthContext.Provider value={{ status: 'authenticated', user: makeUser(), login: async () => undefined, logout: async () => undefined, can: () => true, canAny: () => true }}>
          <CommandPalette open onClose={onClose} triggerRef={triggerRef} />
        </AuthContext.Provider>
      </MemoryRouter>,
    )

    await screen.findByRole('combobox')
    await user.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalledOnce()
    await new Promise(requestAnimationFrame)
    expect(document.activeElement).toBe(trigger)
  })

  it('refreshes results when effective permissions change while open', async () => {
    const initialUser = makeUser({ permissions: [] })
    const grantedUser = makeUser({ permissions: [Permissions.roleManagement.assignmentView] })
    const authValue = (user: typeof initialUser): AuthContextValue => ({
      status: 'authenticated',
      user,
      login: async () => undefined,
      logout: async () => undefined,
      can: permission => hasPermission(user, permission),
      canAny: permissions => hasAnyPermission(user, permissions),
    })
    const view = render(
      <MemoryRouter>
        <AuthContext.Provider value={authValue(initialUser)}>
          <CommandPalette open onClose={vi.fn()} triggerRef={{ current: null }} />
        </AuthContext.Provider>
      </MemoryRouter>,
    )
    expect(screen.queryByRole('option', { name: /Role Management/ })).not.toBeInTheDocument()

    view.rerender(
      <MemoryRouter>
        <AuthContext.Provider value={authValue(grantedUser)}>
          <CommandPalette open onClose={vi.fn()} triggerRef={{ current: null }} />
        </AuthContext.Provider>
      </MemoryRouter>,
    )
    expect(screen.getByRole('option', { name: /Role Management/ })).toBeInTheDocument()
  })
})
