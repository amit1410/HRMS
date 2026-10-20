import { fireEvent, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { renderAsUser } from '../test/renderWith.tsx'
import { makeUser } from '../test/fixtures.ts'
import { Sidebar } from './Sidebar.tsx'
import { Permissions } from '../auth/permissions.ts'

describe('Sidebar flyout navigation', () => {
  it('opens independent Attendance and Leave flyouts beside the sidebar', () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />, { user: makeUser({ permissions: [Permissions.attendance.view, Permissions.leave.requestViewOwn] }) })

    fireEvent.click(screen.getByRole('button', { name: 'Attendance' }))
    expect(screen.getByRole('heading', { name: 'Attendance' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Attendance Overview' })).toHaveAttribute('href', '/attendance')

    fireEvent.click(screen.getByRole('button', { name: 'Leave' }))
    expect(screen.queryByRole('heading', { name: 'Attendance' })).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Leave' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Leave Dashboard' })).toHaveAttribute('href', '/leave-management')
  })

  it('groups Attendance destinations under non-clickable headings with SVG icons', () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />, { user: makeUser({ permissions: [Permissions.attendance.view] }) })

    fireEvent.click(screen.getByRole('button', { name: 'Attendance' }))

    expect(screen.getByRole('heading', { name: 'Overview' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Team operations' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Roster' }).querySelector('svg')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Team operations' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Device Integration' })).not.toBeInTheDocument()
  })

  it('does not render Attendance & Leave as a combined module', () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />)
    expect(screen.queryByRole('button', { name: 'Attendance & Leave' })).not.toBeInTheDocument()
  })

  it('opens People beside the sidebar without rendering a downward submenu', () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />)

    fireEvent.click(screen.getByRole('button', { name: 'People' }))

    expect(screen.getByRole('button', { name: 'People' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('heading', { name: 'People' })).toBeInTheDocument()
    expect(document.querySelector('.nav-sublist')).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Employees' })).toHaveAttribute('href', '/employees')
  })

  it('switches groups, closes on the active group, and closes on outside click', () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />)

    fireEvent.click(screen.getByRole('button', { name: 'People' }))
    fireEvent.click(screen.getByRole('button', { name: 'My Workspace' }))
    expect(screen.getByRole('heading', { name: 'My Workspace' })).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'People' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'My Workspace' }))
    expect(screen.queryByRole('heading', { name: 'My Workspace' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'People' }))
    fireEvent.pointerDown(document.body)
    expect(screen.queryByRole('heading', { name: 'People' })).not.toBeInTheDocument()
  })

  it('closes with Escape and restores focus to the trigger', async () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />)
    const trigger = screen.getByRole('button', { name: 'People' })
    fireEvent.click(trigger)
    fireEvent.keyDown(document, { key: 'Escape' })

    await waitFor(() => expect(trigger).toHaveFocus())
    expect(screen.queryByRole('heading', { name: 'People' })).not.toBeInTheDocument()
  })

  it('filters flyout destinations by effective permissions', () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />, { user: makeUser({ permissions: [Permissions.employee.view] }) })
    fireEvent.click(screen.getByRole('button', { name: 'People' }))

    expect(screen.getByRole('link', { name: 'Employees' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Bank Master' })).not.toBeInTheDocument()
  })

  it('keeps Change Password and active-route highlighting inside My Workspace', () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />, { route: '/change-password' })
    fireEvent.click(screen.getByRole('button', { name: 'My Workspace' }))

    expect(screen.getByRole('link', { name: 'Change Password' })).toHaveClass('is-active')
    expect(screen.getByRole('link', { name: 'Change Password' })).toHaveAttribute('aria-current', 'page')
  })

  it('renders Reports destinations only after opening the Reports flyout', () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />, { user: makeUser({ permissions: [Permissions.leave.reportsView, Permissions.attendance.reportView] }) })
    fireEvent.click(screen.getByRole('button', { name: 'Reports' }))

    expect(screen.getByRole('heading', { name: 'Reports' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Leave Reports' })).toHaveAttribute('href', '/leave-management/reports')
    expect(screen.getByRole('link', { name: 'Attendance Reports' })).toHaveAttribute('href', '/attendance/reports')
  })

  it('does not render Reports when the user has no report permission', () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />, { user: makeUser({ permissions: [] }) })
    expect(screen.queryByRole('button', { name: 'Reports' })).not.toBeInTheDocument()
  })

  it('closes the flyout and calls the close callback when a destination is selected', () => {
    const onClose = vi.fn()
    renderAsUser(<Sidebar branding={null} open={false} onClose={onClose} />)
    fireEvent.click(screen.getByRole('button', { name: 'People' }))
    fireEvent.click(screen.getByRole('link', { name: 'Employees' }))

    expect(onClose).toHaveBeenCalled()
    expect(screen.queryByRole('heading', { name: 'People' })).not.toBeInTheDocument()
  })
})
