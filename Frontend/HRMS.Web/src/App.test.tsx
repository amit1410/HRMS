import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { StrictMode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { App } from './App.tsx'
import { session } from './api/session.ts'
import { Permissions } from './auth/permissions.ts'
import {
  makeDepartment,
  makeEmployee,
  makeLoginResponse,
  makeUser,
  paged,
} from './test/fixtures.ts'
import { installStubAdapter, ok, type StubAdapter } from './test/stubAdapter.ts'

/**
 * The route table, end to end.
 *
 * `App` brings its own `AuthProvider`, so this renders the real thing — guard, provider, layout and
 * screens together — against the stub transport. What is being tested is the wiring no single unit
 * covers: an interrupted navigation surviving a sign-in, a reload restoring a session without flashing
 * the login form, and sign-out putting the user back outside.
 */
function renderApp(route: string) {
  return render(
    <StrictMode>
      <MemoryRouter initialEntries={[route]}>
        <App />
      </MemoryRouter>
    </StrictMode>,
  )
}

function stubDashboard(stub: StubAdapter): void {
  stub.on('get', '/api/employees', (call) =>
    call.params.pageSize === 1
      ? { data: ok(paged([], { totalCount: call.params.status === 'Active' ? 39 : 42 })) }
      : { data: ok(paged([makeEmployee()], { totalCount: 42 })) },
  )
  stub.on('get', '/api/departments', (call) =>
    call.params.pageSize === 1
      ? { data: ok(paged([], { totalCount: 3 })) }
      : { data: ok(paged([makeDepartment()])) },
  )
  stub.on('get', '/api/designations', () => ({ data: ok(paged([], { totalCount: 7 })) }))
}

describe('App', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.clear()
    window.localStorage.clear()
    stub = installStubAdapter()
    stubDashboard(stub)
    stub.on('get', '/api/tenants/current/branding', () => ({
      data: ok({
        displayName: 'Northwind Demo',
        logoUrl: null,
        primaryColor: '#1D4ED8',
        welcomeMessage: null,
        supportEmail: null,
        ssoEnabled: false,
        ssoProviderName: null,
      }),
    }))
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  it('sends an anonymous visitor to sign in, then back to where they were going', async () => {
    stub.on('post', '/api/auth/login', () => ({ data: ok(makeLoginResponse()) }))

    // A path no module claims, so the round-trip cannot be confused with the default landing.
    renderApp('/reports/headcount')

    expect(await screen.findByLabelText('Password')).toBeInTheDocument()
    // Only the anonymous branding endpoint was fetched — no authenticated calls on behalf of a visitor.
    expect(stub.callsTo('get', '/api/tenants/current/branding')).toHaveLength(1)

    await userEvent.type(screen.getByLabelText('Email'), 'hr@demo01.test')
    await userEvent.type(screen.getByLabelText('Password'), 'pw')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    // Back at the interrupted path — and the 404 lives inside the shell, so there is a way out of it.
    expect(await screen.findByText('That page does not exist')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Dashboard' })).toBeInTheDocument()
  })

  it('lands on the dashboard when nothing interrupted the visit', async () => {
    stub.on('post', '/api/auth/login', () => ({ data: ok(makeLoginResponse()) }))

    renderApp('/login')

    await userEvent.type(await screen.findByLabelText('Email'), 'hr@demo01.test')
    await userEvent.type(screen.getByLabelText('Password'), 'pw')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('heading', { name: 'Dashboard', level: 1 })).toBeInTheDocument()
    expect(await screen.findByText('42')).toBeInTheDocument()
  })

  it('restores a stored session on reload without showing the form', async () => {
    window.localStorage.setItem(session.refreshTokenKey, 'refresh-1')
    stub.on('post', '/api/auth/refresh', () => ({ data: ok(makeLoginResponse()) }))

    renderApp('/dashboard')

    // The guard waits instead of redirecting, so the login form is never mounted.
    expect(screen.getByRole('status')).toHaveTextContent('Restoring your session')
    expect(await screen.findByRole('heading', { name: 'Dashboard', level: 1 })).toBeInTheDocument()
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument()
    // StrictMode double-invokes the boot effect; a second exchange would spend a single-use token.
    expect(stub.callsTo('post', '/api/auth/refresh')).toHaveLength(1)
  })

  it('redirects the root to the dashboard', async () => {
    window.localStorage.setItem(session.refreshTokenKey, 'refresh-1')
    stub.on('post', '/api/auth/refresh', () => ({ data: ok(makeLoginResponse()) }))

    renderApp('/')

    expect(await screen.findByRole('heading', { name: 'Dashboard', level: 1 })).toBeInTheDocument()
  })

  it('shows the tenant in the header, so two sessions cannot be mixed up', async () => {
    window.localStorage.setItem(session.refreshTokenKey, 'refresh-1')
    stub.on('post', '/api/auth/refresh', () => ({ data: ok(makeLoginResponse()) }))

    renderApp('/dashboard')

    const header = await screen.findByRole('banner', { name: 'Workspace header' })
    expect(within(header).getByText('Northwind Demo')).toBeInTheDocument()
    expect(within(header).getByText('DEMO01')).toBeInTheDocument()
    expect(within(header).getByText('Priya Raman')).toBeInTheDocument()
  })

  it('puts the user back outside on sign-out', async () => {
    window.localStorage.setItem(session.refreshTokenKey, 'refresh-1')
    stub.on('post', '/api/auth/refresh', () => ({ data: ok(makeLoginResponse()) }))
    stub.on('post', '/api/auth/logout', () => ({ data: ok(true) }))

    renderApp('/dashboard')
    await screen.findByRole('heading', { name: 'Dashboard', level: 1 })

    await userEvent.click(screen.getByRole('button', { name: 'Sign out' }))

    expect(await screen.findByLabelText('Password')).toBeInTheDocument()
    await waitFor(() => expect(session.getRefreshToken()).toBeNull())
  })

  it('links each module in the navigation to a route that exists', async () => {
    window.localStorage.setItem(session.refreshTokenKey, 'refresh-1')
    stub.on('post', '/api/auth/refresh', () => ({ data: ok(makeLoginResponse()) }))

    renderApp('/dashboard')
    await screen.findByRole('heading', { name: 'Dashboard', level: 1 })

    // The href and the route table have to agree; a typo in either is a link that lands on the 404.
    expect(screen.getByRole('link', { name: 'Employees' })).toHaveAttribute('href', '/employees')
    expect(screen.getByRole('link', { name: 'Masters' })).toHaveAttribute('href', '/masters/holding-companies')

    await userEvent.click(screen.getByRole('link', { name: 'Employees' }))
    expect(await screen.findByRole('heading', { name: 'Employees', level: 1 })).toBeInTheDocument()
  })

  it('leaves a module the user cannot open out of the navigation entirely', async () => {
    window.localStorage.setItem(session.refreshTokenKey, 'refresh-1')
    stub.on('post', '/api/auth/refresh', () => ({
      data: ok(
        makeLoginResponse({
          user: makeUser({ roles: ['Employee'], permissions: [Permissions.employee.view] }),
        }),
      ),
    }))

    renderApp('/dashboard')
    await screen.findByRole('heading', { name: 'Dashboard', level: 1 })

    // Absent, not disabled: a greyed link still says the screen is somewhere they might get to.
    expect(screen.getByRole('link', { name: 'Employees' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Masters' })).not.toBeInTheDocument()
  })
})
