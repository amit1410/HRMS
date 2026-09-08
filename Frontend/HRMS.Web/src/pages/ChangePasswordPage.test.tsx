import { fireEvent, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ok, installStubAdapter, type StubAdapter } from '../test/stubAdapter.ts'
import { renderAsUser } from '../test/renderWith.tsx'
import { ChangePasswordPage } from './ChangePasswordPage.tsx'

describe('ChangePasswordPage', () => {
  let stub: StubAdapter

  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  it('validates the password policy before submitting', async () => {
    renderAsUser(<ChangePasswordPage />)
    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'Old-password-1' } })
    fireEvent.change(screen.getByLabelText('New Password'), { target: { value: 'weak' } })
    fireEvent.change(screen.getByLabelText('Confirm New Password'), { target: { value: 'weak' } })
    fireEvent.click(screen.getByRole('button', { name: 'Change Password' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Password must be 8-128 characters')
    expect(stub.callsTo('post', '/api/me/change-password')).toHaveLength(0)
  })

  it('submits the three password fields and signs the user out on success', async () => {
    const logout = vi.fn(async () => undefined)
    stub.on('post', '/api/me/change-password', (call) => {
      expect(call.body).toEqual({ currentPassword: 'Old-password-1', newPassword: 'New-password-1', confirmPassword: 'New-password-1' })
      return { data: ok(true) }
    })
    renderAsUser(<ChangePasswordPage />, { logout })
    fireEvent.change(screen.getByLabelText('Current Password'), { target: { value: 'Old-password-1' } })
    fireEvent.change(screen.getByLabelText('New Password'), { target: { value: 'New-password-1' } })
    fireEvent.change(screen.getByLabelText('Confirm New Password'), { target: { value: 'New-password-1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Change Password' }))

    expect(await screen.findByRole('heading', { name: 'Password changed' })).toBeInTheDocument()
    expect(logout).toHaveBeenCalledOnce()
  })

  it('renders security guidance and toggles password visibility without persisting values', () => {
    renderAsUser(<ChangePasswordPage />)

    expect(screen.getByRole('heading', { name: 'Update Your Password' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Password Requirements' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Security Note' })).toBeInTheDocument()
    expect(screen.getByText('8-128 characters')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Tips for a stronger password' })).toBeInTheDocument()

    const current = screen.getByLabelText('Current Password')
    expect(current).toHaveAttribute('type', 'password')
    fireEvent.click(screen.getByRole('button', { name: 'Show Current Password' }))
    expect(current).toHaveAttribute('type', 'text')
    expect(window.localStorage.getItem('currentPassword')).toBeNull()
    expect(window.sessionStorage.getItem('currentPassword')).toBeNull()
  })
})
