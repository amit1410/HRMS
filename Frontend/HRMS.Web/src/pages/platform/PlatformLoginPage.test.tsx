import { screen, render, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/errors.ts'
import { PlatformLoginPage } from './PlatformLoginPage.tsx'

const { login } = vi.hoisted(() => ({ login: vi.fn() }))

vi.mock('../../auth/PlatformAuthProvider.tsx', () => ({
  usePlatformAuth: () => ({ login }),
}))

function renderLogin() {
  return render(<MemoryRouter initialEntries={['/platform/login']}><Routes><Route path="/platform/login" element={<PlatformLoginPage />} /><Route path="/platform/tenants" element={<p>tenant administration</p>} /></Routes></MemoryRouter>)
}

describe('PlatformLoginPage', () => {
  beforeEach(() => {
    login.mockReset()
    window.localStorage.clear()
  })

  it('renders the split platform administration login', () => {
    renderLogin()

    expect(screen.getByRole('heading', { name: 'Manage Multiple Tenants Effortlessly' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Platform administration' })).toBeInTheDocument()
    expect(screen.getByText('Centralized Tenant Management')).toBeInTheDocument()
    expect(screen.getByText('Tenant overview')).toBeInTheDocument()
    expect(document.querySelector('.platform-login-page')).toBeInTheDocument()
    expect(document.querySelector('.platform-login-brand-panel')).toBeInTheDocument()
    expect(document.querySelector('.platform-login-form-panel')).toBeInTheDocument()
  })

  it('renders accessible email and password fields', () => {
    renderLogin()

    expect(screen.getByLabelText('Email')).toHaveAttribute('type', 'email')
    expect(screen.getByLabelText('Password')).toHaveAttribute('type', 'password')
  })

  it('toggles password visibility', async () => {
    renderLogin()

    const password = screen.getByLabelText('Password')
    await userEvent.click(screen.getByRole('button', { name: 'Show password' }))
    expect(password).toHaveAttribute('type', 'text')
    expect(screen.getByRole('button', { name: 'Hide password' })).toBeInTheDocument()
  })

  it('submits the existing platform authentication request and navigates', async () => {
    login.mockResolvedValue(undefined)
    renderLogin()

    await userEvent.type(screen.getByLabelText('Email'), 'admin@anevra.test')
    await userEvent.type(screen.getByLabelText('Password'), 'platform-password')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(login).toHaveBeenCalledWith('admin@anevra.test', 'platform-password')
    expect(await screen.findByText('tenant administration')).toBeInTheDocument()
  })

  it('keeps native required-field validation before submitting', async () => {
    renderLogin()

    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(login).not.toHaveBeenCalled()
    expect(screen.getByLabelText('Email')).toBeInvalid()
    expect(screen.getByLabelText('Password')).toBeInvalid()
  })

  it('renders API errors in a safe accessible alert', async () => {
    login.mockRejectedValue(new ApiError('The platform credentials were not accepted.', { status: 401 }))
    renderLogin()

    await userEvent.type(screen.getByLabelText('Email'), 'admin@anevra.test')
    await userEvent.type(screen.getByLabelText('Password'), 'wrong')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The platform credentials were not accepted.')
    expect(screen.getByRole('alert')).toHaveAttribute('aria-live', 'polite')
  })

  it('does not persist platform credentials in browser storage', async () => {
    login.mockResolvedValue(undefined)
    renderLogin()

    await userEvent.type(screen.getByLabelText('Email'), 'admin@anevra.test')
    await userEvent.type(screen.getByLabelText('Password'), 'platform-password')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
    await waitFor(() => expect(screen.getByText('tenant administration')).toBeInTheDocument())

    expect(window.localStorage.length).toBe(0)
  })
})
