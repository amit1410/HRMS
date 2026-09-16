import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { renderAsUser } from '../test/renderWith.tsx'
import { makeUser } from '../test/fixtures.ts'
import { Sidebar } from './Sidebar.tsx'
import { Permissions } from '../auth/permissions.ts'

describe('Sidebar account area', () => {
  it('renders the authenticated user summary and one Change Password link', () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />, { user: makeUser({ fullName: 'Ragu Agarwal', roles: ['Employee'] }) })

    expect(screen.getByText('Ragu Agarwal')).toBeInTheDocument()
    expect(screen.getByText('Employee')).toBeInTheDocument()
    expect(screen.getByText('RA')).toBeInTheDocument()
    expect(screen.getByText('Online')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: /Change Password/ })).toHaveLength(1)
  })

  it('marks Change Password active for its route without changing navigation visibility', () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />, { route: '/change-password' })

    expect(screen.getByRole('link', { name: /Change Password/ })).toHaveClass('is-active')
    expect(screen.getByRole('link', { name: /Change Password/ })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('link', { name: 'Dashboard' })).toBeInTheDocument()
  })

  it('renders Leave and Attendance reports only inside the Reports group', () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />, { user: makeUser({ permissions: [Permissions.leave.reportsView, Permissions.attendance.reportView] }) })

    const reports = screen.getByText('Reports')
    expect(reports.closest('details')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Leave Reports' })).toHaveAttribute('href', '/leave-management/reports')
    expect(screen.getByRole('link', { name: 'Attendance Reports' })).toHaveAttribute('href', '/attendance/reports')
    expect(screen.queryByRole('link', { name: 'Leave Reports' })?.closest('details')?.querySelector('.nav-group-summary')).toHaveTextContent('Reports')
  })

  it('hides the Reports group when the user has no report permission', () => {
    renderAsUser(<Sidebar branding={null} open={false} onClose={() => undefined} />, { user: makeUser({ permissions: [] }) })
    expect(screen.queryByText('Reports')).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Leave Reports' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Attendance Reports' })).not.toBeInTheDocument()
  })
})
