import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { DashboardQuickActions } from './DashboardQuickActions.tsx'

describe('DashboardQuickActions', () => {
  it('renders each authorized destination once while preserving the first priority label', () => {
    render(
      <MemoryRouter>
        <DashboardQuickActions actions={[
          { to: '/employees', label: 'Employee directory', icon: 'people', show: true },
          { to: '/employees', label: 'View directory', icon: 'people', show: true },
          { to: '/employees/new', label: 'Add employee', icon: '+', show: false },
        ]} />
      </MemoryRouter>,
    )

    expect(screen.getAllByRole('link')).toHaveLength(1)
    expect(screen.getByRole('link', { name: /Employee directory/ })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Employee directory' }).querySelector('svg')).toBeInTheDocument()
    expect(screen.queryByText('people')).not.toBeInTheDocument()
    expect(screen.queryByText('View directory')).not.toBeInTheDocument()
  })

  it('keeps full labels accessible while rendering named icons as SVGs', () => {
    render(
      <MemoryRouter>
        <DashboardQuickActions actions={[
          { to: '/employees/new', label: 'Add employee', icon: '+', show: true },
          { to: '/configuration/employee-code', label: 'Configure employee codes', icon: 'settings', show: true },
        ]} />
      </MemoryRouter>,
    )

    expect(screen.getByRole('link', { name: 'Add employee' }).querySelector('svg')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Configure employee codes' }).querySelector('svg')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Configure employee codes' })).toHaveAttribute('title', 'Configure employee codes')
    expect(screen.getByText('Configure employee codes')).toBeVisible()
  })
})
