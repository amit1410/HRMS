import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { session } from '../api/session.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../test/stubAdapter.ts'
import { makeLoginResponse, makeUser } from '../test/fixtures.ts'
import { renderWithAuth } from '../test/renderWith.tsx'
import { useAuth } from './useAuth.ts'

/** Shows everything the context exposes, and offers the two actions. */
function Probe() {
  const { status, user, login, logout } = useAuth()
  const [error, setError] = useState('')

  return (
    <div>
      <span data-testid="status">{status}</span>
      <span data-testid="user">{user?.fullName ?? 'nobody'}</span>
      <span data-testid="error">{error}</span>
      <button
        type="button"
        onClick={async () => {
          setError('')
          try {
            await login({ email: 'hr@demo01.test', password: 'pw' })
          } catch (caught) {
            setError(caught instanceof Error ? caught.message : 'failed')
          }
        }}
      >
        sign in
      </button>
      <button type="button" onClick={() => void logout()}>
        sign out
      </button>
    </div>
  )
}

describe('AuthProvider', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.clear()
    window.localStorage.clear()
    stub = installStubAdapter()
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  describe('on load', () => {
    it('starts anonymous, and asks the API nothing, when no session is stored', () => {
      renderWithAuth(<Probe />)

      expect(screen.getByTestId('status')).toHaveTextContent('anonymous')
      // A request here would mean an unauthenticated visitor causes traffic on every page load.
      expect(stub.calls).toHaveLength(0)
    })

    it('restores a stored session with exactly one refresh', async () => {
      window.localStorage.setItem(session.refreshTokenKey, 'refresh-1')
      stub.on('post', '/api/auth/refresh', () => ({
        data: ok(makeLoginResponse({ user: makeUser({ fullName: 'Priya Raman' }) })),
      }))

      renderWithAuth(<Probe />)

      // 'restoring' first: rendering the login form during this window would flash it at a signed-in user.
      expect(screen.getByTestId('status')).toHaveTextContent('restoring')

      await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))
      expect(screen.getByTestId('user')).toHaveTextContent('Priya Raman')
      // StrictMode double-invokes the boot effect; a second exchange would burn a single-use token.
      expect(stub.callsTo('post', '/api/auth/refresh')).toHaveLength(1)
      expect(session.getAccessToken()).toBe('access-1')
    })

    it('falls back to anonymous and clears storage when the stored token is dead', async () => {
      window.localStorage.setItem(session.refreshTokenKey, 'revoked')
      stub.on('post', '/api/auth/refresh', () => ({
        status: 401,
        data: fail('The refresh token is no longer valid.'),
      }))

      renderWithAuth(<Probe />)

      await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous'))
      expect(session.getRefreshToken()).toBeNull()
    })
  })

  describe('signing in', () => {
    it('authenticates and stores the pair', async () => {
      stub.on('post', '/api/auth/login', () => ({
        data: ok(makeLoginResponse({ accessToken: 'access-9', refreshToken: 'refresh-9' })),
      }))

      renderWithAuth(<Probe />)
      await userEvent.click(screen.getByRole('button', { name: 'sign in' }))

      await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))
      expect(session.getAccessToken()).toBe('access-9')
      expect(window.localStorage.getItem(session.refreshTokenKey)).toBe('refresh-9')
    })

    it('surfaces the message the server gave and stays anonymous when credentials are refused', async () => {
      stub.on('post', '/api/auth/login', () => ({
        status: 401,
        data: fail('Invalid credentials.'),
      }))

      renderWithAuth(<Probe />)
      await userEvent.click(screen.getByRole('button', { name: 'sign in' }))

      await waitFor(() => expect(screen.getByTestId('error')).toHaveTextContent('Invalid credentials.'))
      expect(screen.getByTestId('status')).toHaveTextContent('anonymous')
      expect(session.getRefreshToken()).toBeNull()
    })
  })

  describe('signing out', () => {
    it('revokes the token server-side and clears local state', async () => {
      stub.on('post', '/api/auth/login', () => ({ data: ok(makeLoginResponse()) }))
      stub.on('post', '/api/auth/logout', () => ({ data: ok(true) }))

      renderWithAuth(<Probe />)
      await userEvent.click(screen.getByRole('button', { name: 'sign in' }))
      await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))

      await userEvent.click(screen.getByRole('button', { name: 'sign out' }))

      await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous'))
      expect(stub.callsTo('post', '/api/auth/logout')[0]?.body).toEqual({
        refreshToken: 'refresh-1',
      })
      expect(session.getRefreshToken()).toBeNull()
    })

    it('signs out locally even when the revoke call fails', async () => {
      stub.on('post', '/api/auth/login', () => ({ data: ok(makeLoginResponse()) }))
      stub.on('post', '/api/auth/logout', () => ({ status: 500, data: fail('Server error') }))

      renderWithAuth(<Probe />)
      await userEvent.click(screen.getByRole('button', { name: 'sign in' }))
      await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))

      await userEvent.click(screen.getByRole('button', { name: 'sign out' }))

      // Leaving someone half signed-in because the network was down would be the wrong way round.
      await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous'))
      expect(session.getRefreshToken()).toBeNull()
    })
  })

  describe('sessions ending elsewhere', () => {
    it('goes anonymous when another tab signs out', async () => {
      stub.on('post', '/api/auth/login', () => ({ data: ok(makeLoginResponse()) }))

      renderWithAuth(<Probe />)
      await userEvent.click(screen.getByRole('button', { name: 'sign in' }))
      await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))

      // `storage` fires only in *other* tabs, which is why this cannot fight with our own writes.
      window.localStorage.removeItem(session.refreshTokenKey)
      window.dispatchEvent(
        new StorageEvent('storage', { key: session.refreshTokenKey, newValue: null }),
      )

      await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous'))
      expect(session.getAccessToken()).toBeNull()
    })

    it('ignores unrelated storage keys', async () => {
      stub.on('post', '/api/auth/login', () => ({ data: ok(makeLoginResponse()) }))

      renderWithAuth(<Probe />)
      await userEvent.click(screen.getByRole('button', { name: 'sign in' }))
      await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))

      window.dispatchEvent(
        new StorageEvent('storage', { key: 'hrms.lastWorkspaceLabel.v1', newValue: null }),
      )

      expect(screen.getByTestId('status')).toHaveTextContent('authenticated')
    })
  })

  it('throws when used without a provider, rather than looking quietly anonymous', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    try {
      expect(() => render(<Probe />)).toThrow(/AuthProvider/)
    } finally {
      consoleError.mockRestore()
    }
  })
})
