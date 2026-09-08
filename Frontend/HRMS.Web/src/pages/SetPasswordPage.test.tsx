import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { ok } from '../test/stubAdapter.ts'
import { installStubAdapter, type StubAdapter } from '../test/stubAdapter.ts'
import { renderAsUser } from '../test/renderWith.tsx'
import { SetPasswordPage } from './SetPasswordPage.tsx'

describe('SetPasswordPage', () => {
  let stub: StubAdapter

  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => { stub.restore() })

  it('submits the one-time token without persisting it and shows success', async () => {
    stub.on('post', '/api/auth/set-password', (call) => {
      expect(call.body).toEqual({ token: 'one-time-token', password: 'Employee-password-1', confirmPassword: 'Employee-password-1' })
      return { data: ok(true) }
    })

    renderAsUser(<SetPasswordPage />, { user: null, status: 'anonymous', route: '/set-password?token=one-time-token' })
    await userEvent.type(screen.getByLabelText('Password'), 'Employee-password-1')
    await userEvent.type(screen.getByLabelText('Confirm password'), 'Employee-password-1')
    await userEvent.click(screen.getByRole('button', { name: 'Set password' }))

    expect(await screen.findByText('Your password has been set.')).toBeInTheDocument()
    expect(window.localStorage.length).toBe(0)
    expect(window.sessionStorage.length).toBe(0)
  })
})
