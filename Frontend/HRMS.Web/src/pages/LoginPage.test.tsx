import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/errors.ts'
import type { AuthContextValue } from '../auth/authContext.ts'
import { LoginPage } from './LoginPage.tsx'
import { renderAsUser } from '../test/renderWith.tsx'

const routes = (
  <Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route path="/dashboard" element={<p>dashboard</p>} />
  </Routes>
)

function renderLogin(login: AuthContextValue['login']) {
  return renderAsUser(routes, { user: null, status: 'anonymous', route: '/login', login })
}

describe('LoginPage', () => {
  beforeEach(() => {
    window.localStorage.clear()
  })

  it('asks for the tenant code as well as the credentials', () => {
    renderLogin(vi.fn())

    // Two tenants may each have an anna@example.com; they are different people.
    expect(screen.getByLabelText('Tenant code')).toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toBeInTheDocument()
    expect(screen.getByLabelText('Password')).toBeInTheDocument()
  })

  it('sends trimmed values, and does not touch the password', async () => {
    const login = vi.fn().mockResolvedValue(undefined)
    renderLogin(login)

    await userEvent.type(screen.getByLabelText('Tenant code'), '  DEMO01 ')
    await userEvent.type(screen.getByLabelText('Email'), ' hr@demo01.test ')
    await userEvent.type(screen.getByLabelText('Password'), ' pw with spaces ')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(login).toHaveBeenCalledWith({
      tenantCode: 'DEMO01',
      email: 'hr@demo01.test',
      // Trimming a password would silently change the credential.
      password: ' pw with spaces ',
    })
  })

  it('goes to the dashboard once signed in', async () => {
    const login = vi.fn().mockResolvedValue(undefined)
    renderLogin(login)

    await userEvent.type(screen.getByLabelText('Tenant code'), 'DEMO01')
    await userEvent.type(screen.getByLabelText('Email'), 'hr@demo01.test')
    await userEvent.type(screen.getByLabelText('Password'), 'pw')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('dashboard')).toBeInTheDocument()
  })

  it('remembers the tenant code for next time', async () => {
    const login = vi.fn().mockResolvedValue(undefined)
    renderLogin(login)

    await userEvent.type(screen.getByLabelText('Tenant code'), 'DEMO02')
    await userEvent.type(screen.getByLabelText('Email'), 'hr@demo02.test')
    await userEvent.type(screen.getByLabelText('Password'), 'pw')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    await waitFor(() =>
      expect(window.localStorage.getItem('hrms.lastTenantCode.v1')).toBe('DEMO02'),
    )
    // Only the tenant code — nothing about who signed in or with what.
    expect(JSON.stringify(window.localStorage)).not.toContain('hr@demo02.test')
  })

  it('prefills the remembered tenant code', () => {
    window.localStorage.setItem('hrms.lastTenantCode.v1', 'DEMO02')

    renderLogin(vi.fn())

    expect(screen.getByLabelText('Tenant code')).toHaveValue('DEMO02')
  })

  it('shows what the server said when the credentials are refused', async () => {
    const login = vi.fn().mockRejectedValue(new ApiError('Invalid credentials.', { status: 401 }))
    renderLogin(login)

    await userEvent.type(screen.getByLabelText('Email'), 'hr@demo01.test')
    await userEvent.type(screen.getByLabelText('Password'), 'wrong')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Invalid credentials.')
    // The API never says which half was wrong, and neither does this page.
    expect(alert).not.toHaveTextContent(/email|password/i)
  })

  it('puts validation failures under the fields they belong to', async () => {
    const login = vi.fn().mockRejectedValue(
      new ApiError('Validation failed.', {
        status: 400,
        fieldErrors: {
          tenantCode: 'Tenant code is required.',
          password: 'Password is required.',
        },
      }),
    )
    renderLogin(login)

    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Tenant code is required.')).toBeInTheDocument()
    expect(screen.getByText('Password is required.')).toBeInTheDocument()
    expect(screen.getByLabelText('Tenant code')).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByLabelText('Email')).not.toHaveAttribute('aria-invalid')
    // The banner would only repeat what is already under the inputs.
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('cannot be submitted twice while a sign-in is in flight', async () => {
    const login = vi.fn().mockReturnValue(new Promise<void>(() => undefined))
    renderLogin(login)

    await userEvent.type(screen.getByLabelText('Tenant code'), 'DEMO01')
    const submit = screen.getByRole('button', { name: 'Sign in' })
    await userEvent.click(submit)

    await waitFor(() => expect(screen.getByRole('button')).toBeDisabled())
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
})
