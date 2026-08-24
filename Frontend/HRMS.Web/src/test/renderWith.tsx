import { render, type RenderResult } from '@testing-library/react'
import { StrictMode, type ReactNode } from 'react'
import { MemoryRouter, parsePath, type InitialEntry } from 'react-router-dom'
import type { AuthenticatedUser } from '../api/types.ts'
import { AuthContext, type AuthContextValue } from '../auth/authContext.ts'
import { AuthProvider } from '../auth/AuthProvider.tsx'
import { hasAnyPermission, hasPermission } from '../auth/permissions.ts'
import { makeUser } from './fixtures.ts'

/**
 * Render helpers.
 *
 * Two of them, because there are two different things to test:
 *
 * - {@link renderWithAuth} keeps the real {@link AuthProvider}, so the session lifecycle — restore on
 *   load, sign-in, sign-out — is exercised end to end against the stub adapter.
 * - {@link renderAsUser} supplies the context directly. A screen's job is to render the right things for
 *   a given set of permissions; making it sign in first would test the provider again and make the
 *   permission under test three steps away from the assertion.
 */

export function renderWithAuth(ui: ReactNode, { route = '/' } = {}): RenderResult {
  // StrictMode matches `main.tsx`, and is what makes the double-invoked boot effect part of the test:
  // a session restore that fired twice would spend a single-use refresh token twice.
  return render(
    <StrictMode>
      <MemoryRouter initialEntries={[route]}>
        <AuthProvider>{ui}</AuthProvider>
      </MemoryRouter>
    </StrictMode>,
  )
}

interface RenderAsUserOptions {
  user?: AuthenticatedUser | null
  route?: string
  /**
   * History state the screen is entered with — a flash message from a save, or the `from` a list hands
   * its form. Both travel in `location.state` in the app, so a test that supplied it another way would
   * be exercising a path the app does not take.
   */
  state?: unknown
  login?: AuthContextValue['login']
  logout?: AuthContextValue['logout']
  status?: AuthContextValue['status']
}

export function renderAsUser(
  ui: ReactNode,
  {
    user = makeUser(),
    route = '/',
    state,
    login,
    logout,
    status = 'authenticated',
  }: RenderAsUserOptions = {},
): RenderResult {
  const value: AuthContextValue = {
    status,
    user,
    login: login ?? (async () => undefined),
    logout: logout ?? (async () => undefined),
    can: (permission) => hasPermission(user, permission),
    canAny: (permissions) => hasAnyPermission(user, permissions),
  }

  // `parsePath` splits the query string out of `route`, which a location object needs held separately.
  const entry: InitialEntry = state === undefined ? route : { ...parsePath(route), state }

  return render(
    <MemoryRouter initialEntries={[entry]}>
      <AuthContext.Provider value={value}>{ui}</AuthContext.Provider>
    </MemoryRouter>,
  )
}
