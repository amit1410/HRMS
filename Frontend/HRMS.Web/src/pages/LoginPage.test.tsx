import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/errors.ts'
import type { AuthContextValue } from '../auth/authContext.ts'
import { session } from '../api/session.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../test/stubAdapter.ts'
import { renderAsUser } from '../test/renderWith.tsx'
import { LoginPage } from './LoginPage.tsx'

const routes = (
  <Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route path="/dashboard" element={<p>dashboard</p>} />
  </Routes>
)

const publishedBranding = {
  displayName: 'Northwind Demo',
  logoUrl: 'https://cdn.demo01.test/logo.png',
  primaryColor: '#1D4ED8',
  welcomeMessage: 'Welcome to Northwind.',
  ssoEnabled: false,
}

function renderLogin(login: AuthContextValue['login']) {
  return renderAsUser(routes, { user: null, status: 'anonymous', route: '/login', login })
}

describe('LoginPage', () => {
  let stub: StubAdapter

  beforeEach(() => {
    window.localStorage.clear()
    session.clear()
    stub = installStubAdapter()
    stub.on('get', '/api/tenants/current/branding', () => ({
      data: ok(publishedBranding),
    }))
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  it('asks for the credentials only - never for an organization', async () => {
    renderLogin(vi.fn())
    expect(screen.queryByLabelText(/tenant code|organization/i)).not.toBeInTheDocument()
    await waitFor(() => expect(screen.getByLabelText('Email or Employee Code')).toBeInTheDocument())
    expect(screen.getByLabelText('Password')).toBeInTheDocument()
  })

  it.each([
    ['EmailOnly', 'Email', 'name@company.com'],
    ['EmployeeCodeOnly', 'Employee Code', 'ANV_1001'],
    ['EmailOrEmployeeCode', 'Email or Employee Code', 'name@company.com or ANV_1001'],
  ] as const)('uses the %s login identifier presentation', async (mode, label, placeholder) => {
    stub.on('get', '/api/tenants/current/branding', () => ({ data: ok({ ...publishedBranding, loginIdentifierMode: mode }) }))
    renderLogin(vi.fn())
    const input = await screen.findByLabelText(label)
    expect(input).toHaveAttribute('placeholder', placeholder)
  })

  it.each([
    ['EmailOrEmployeeCode', 'Email or Employee Code', 'nehaa0210@gmail.com'],
    ['EmployeeCodeOnly', 'Employee Code', 'ANV_1001'],
    ['EmailOnly', 'Email', 'nehaa0210@gmail.com'],
  ] as const)('submits the canonical identifier in %s mode', async (mode, label, value) => {
    stub.on('get', '/api/tenants/current/branding', () => ({ data: ok({ ...publishedBranding, loginIdentifierMode: mode }) }))
    const login = vi.fn().mockResolvedValue(undefined)
    renderLogin(login)

    await userEvent.type(await screen.findByLabelText(label), value)
    await userEvent.type(screen.getByLabelText('Password'), 'pw')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(login).toHaveBeenCalledWith({ identifier: value, password: 'pw' })
    expect(screen.queryByText(/email is required/i)).not.toBeInTheDocument()
  })

  it.each([
    ['EmailOnly', 'Email', 'Email is required.'],
    ['EmployeeCodeOnly', 'Employee Code', 'Employee Code is required.'],
    ['EmailOrEmployeeCode', 'Email or Employee Code', 'Email or Employee Code is required.'],
  ] as const)('uses the mode-specific required message in %s mode', async (mode, label, message) => {
    stub.on('get', '/api/tenants/current/branding', () => ({ data: ok({ ...publishedBranding, loginIdentifierMode: mode }) }))
    renderLogin(vi.fn())

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText(message)).toBeInTheDocument()
    expect(screen.getByLabelText(label)).toHaveAttribute('aria-invalid', 'true')
  })

  it('sends trimmed values, and does not touch the password', async () => {
    const login = vi.fn().mockResolvedValue(undefined)
    renderLogin(login)

    await userEvent.type(await screen.findByLabelText('Email or Employee Code'), ' hr@demo01.test ')
    await userEvent.type(screen.getByLabelText('Password'), ' pw with spaces ')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(login).toHaveBeenCalledWith({
      identifier: 'hr@demo01.test',
      password: ' pw with spaces ',
    })
  })

  it('goes to the dashboard once signed in', async () => {
    const login = vi.fn().mockResolvedValue(undefined)
    renderLogin(login)

    await userEvent.type(await screen.findByLabelText('Email or Employee Code'), 'hr@demo01.test')
    await userEvent.type(screen.getByLabelText('Password'), 'pw')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('dashboard')).toBeInTheDocument()
  })

  it('shows what the server said when the credentials are refused', async () => {
    const login = vi.fn().mockRejectedValue(new ApiError('Invalid credentials.', { status: 401 }))
    renderLogin(login)

    await userEvent.type(await screen.findByLabelText('Email or Employee Code'), 'hr@demo01.test')
    await userEvent.type(screen.getByLabelText('Password'), 'wrong')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Invalid credentials.')
    expect(alert).not.toHaveTextContent(/email|password/i)
  })

  it('puts validation failures under the fields they belong to', async () => {
    const login = vi.fn().mockRejectedValue(
      new ApiError('Validation failed.', {
        status: 400,
        fieldErrors: { identifier: 'Email or Employee Code is required.', password: 'Password is required.' },
      }),
    )
    renderLogin(login)

    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Email or Employee Code is required.')).toBeInTheDocument()
    expect(screen.getByText('Password is required.')).toBeInTheDocument()
    expect(screen.getByLabelText('Email or Employee Code')).toHaveAttribute('aria-invalid', 'true')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('cannot be submitted twice while a sign-in is in flight', async () => {
    const login = vi.fn().mockReturnValue(new Promise<void>(() => undefined))
    renderLogin(login)

    await waitFor(() => expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument())
    await userEvent.type(screen.getByLabelText('Email or Employee Code'), 'hr@demo01.test')
    await userEvent.type(screen.getByLabelText('Password'), 'pw')
    const submit = screen.getByRole('button', { name: /^Sign in$/ })
    await userEvent.click(submit)

    await waitFor(() => expect(submit).toBeDisabled())
    expect(login).toHaveBeenCalledTimes(1)
  })

  it('redirects away from the form when the session is already good', () => {
    renderAsUser(routes, { route: '/login' })
    expect(screen.getByText('dashboard')).toBeInTheDocument()
  })

  it('shows a wait indicator instead of the form while a session is being restored', () => {
    renderAsUser(routes, { user: null, status: 'restoring', route: '/login' })
    expect(screen.getByRole('status')).toHaveTextContent('Restoring your session')
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument()
  })

  it('shows a spinner while branding is loading', () => {
    stub.on('get', '/api/tenants/current/branding', () => ({
      data: ok(publishedBranding),
      delay: true,
    }))
    renderLogin(vi.fn())
    expect(screen.getByRole('status')).toHaveTextContent('Loading workspace')
  })

  it('renders the tenant display name from branding', async () => {
    renderLogin(vi.fn())
    expect(await screen.findByText('Northwind Demo')).toBeInTheDocument()
    expect(screen.getByText('Welcome to Northwind.')).toBeInTheDocument()
  })

  it('shows the tenant logo when branding includes a logo URL', async () => {
    renderLogin(vi.fn())
    const img = await screen.findByRole('img', { name: 'Northwind Demo' })
    expect(img).toHaveAttribute('src', 'https://cdn.demo01.test/logo.png')
  })

  it('falls back to the HR mark when no logo is published', async () => {
    stub.on('get', '/api/tenants/current/branding', () => ({
      data: ok({ displayName: 'Acme Corp', ssoEnabled: false }),
    }))
    renderLogin(vi.fn())
    expect(await screen.findByText('Acme Corp')).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(screen.getByText('HR', { selector: '.login-mark' })).toBeInTheDocument()
  })

  it('applies the tenant primary color as a CSS custom property', async () => {
    renderLogin(vi.fn())
    const page = await screen.findByTestId('login-page')
    expect(page.style.getPropertyValue('--ws-accent')).toBe('#1D4ED8')
  })

  it('uses the default accent color when no primary color is published', async () => {
    stub.on('get', '/api/tenants/current/branding', () => ({
      data: ok({ displayName: 'Acme Corp', ssoEnabled: false }),
    }))
    renderLogin(vi.fn())
    const page = await screen.findByTestId('login-page')
    expect(page.style.getPropertyValue('--ws-accent')).toBe('')
  })

  it('shows the workspace-unavailable state when branding is neutral', async () => {
    stub.on('get', '/api/tenants/current/branding', () => ({
      status: 404,
      data: ok({
        displayName: null,
        logoUrl: null,
        primaryColor: null,
        welcomeMessage: null,
        ssoEnabled: false,
      }),
    }))
    renderLogin(vi.fn())
    expect(await screen.findByText('Workspace not found')).toBeInTheDocument()
    expect(screen.getByText('There is no organization at this address.')).toBeInTheDocument()
    expect(screen.queryByLabelText('Email or Employee Code')).not.toBeInTheDocument()
  })

  it('renders the login page when the server supplies fallback branding', async () => {
    stub.on('get', '/api/tenants/current/branding', () => ({
      status: 200,
      data: ok({ displayName: 'Anevra Technologies', primaryColor: '#1D4ED8', ssoEnabled: false }),
    }))
    renderLogin(vi.fn())

    expect(await screen.findByText('Anevra Technologies')).toBeInTheDocument()
    expect(screen.getByLabelText('Email or Employee Code')).toBeInTheDocument()
    expect(screen.queryByText('Workspace not found')).not.toBeInTheDocument()
  })

  it('shows an error when branding fetch fails', async () => {
    stub.on('get', '/api/tenants/current/branding', () => ({
      status: 500,
      data: fail('Internal Server Error'),
    }))
    renderLogin(vi.fn())
    expect(await screen.findByText('Workspace unavailable')).toBeInTheDocument()
    expect(screen.queryByLabelText('Email or Employee Code')).not.toBeInTheDocument()
  })
})
