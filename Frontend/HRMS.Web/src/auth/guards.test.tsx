import { screen } from '@testing-library/react'
import { Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { Permissions } from './permissions.ts'
import { RequireAuth } from './RequireAuth.tsx'
import { RequirePermission } from './RequirePermission.tsx'
import { makeUser, MANAGER_PERMISSIONS } from '../test/fixtures.ts'
import { renderAsUser } from '../test/renderWith.tsx'

const routes = (
  <Routes>
    <Route path="/login" element={<p>sign-in screen</p>} />
    <Route path="/forbidden" element={<p>no access</p>} />
    <Route element={<RequireAuth />}>
      <Route path="/dashboard" element={<p>dashboard</p>} />
      <Route element={<RequirePermission permission={Permissions.employee.delete} />}>
        <Route path="/employees/archive" element={<p>archive</p>} />
      </Route>
      <Route
        element={
          <RequirePermission anyOf={[Permissions.department.view, Permissions.designation.view]} />
        }
      >
        <Route path="/organization" element={<p>organization</p>} />
      </Route>
    </Route>
  </Routes>
)

describe('RequireAuth', () => {
  it('sends an anonymous visitor to the sign-in screen', () => {
    renderAsUser(routes, { user: null, status: 'anonymous', route: '/dashboard' })

    expect(screen.getByText('sign-in screen')).toBeInTheDocument()
  })

  it('waits rather than deciding while the session is being restored', () => {
    // Redirecting here would bounce a signed-in user to the login form on every hard refresh.
    renderAsUser(routes, { user: null, status: 'restoring', route: '/dashboard' })

    expect(screen.getByRole('status')).toHaveTextContent('Restoring your session')
    expect(screen.queryByText('sign-in screen')).not.toBeInTheDocument()
  })

  it('lets a signed-in user through', () => {
    renderAsUser(routes, { route: '/dashboard' })

    expect(screen.getByText('dashboard')).toBeInTheDocument()
  })
})

describe('RequirePermission', () => {
  it('explains rather than showing a screen full of 403s', () => {
    renderAsUser(routes, {
      user: makeUser({ permissions: MANAGER_PERMISSIONS }),
      route: '/employees/archive',
    })

    expect(screen.getByText('no access')).toBeInTheDocument()
  })

  it('admits a user who holds the permission', () => {
    renderAsUser(routes, {
      user: makeUser({ permissions: [Permissions.employee.delete] }),
      route: '/employees/archive',
    })

    expect(screen.getByText('archive')).toBeInTheDocument()
  })

  it('admits a user who holds any one of several', () => {
    renderAsUser(routes, {
      user: makeUser({ permissions: [Permissions.designation.view] }),
      route: '/organization',
    })

    expect(screen.getByText('organization')).toBeInTheDocument()
  })

  it('refuses a user who holds none of them', () => {
    renderAsUser(routes, {
      user: makeUser({ permissions: [Permissions.employee.view] }),
      route: '/organization',
    })

    expect(screen.getByText('no access')).toBeInTheDocument()
  })
})
