import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PlatformAuthProvider, usePlatformAuth } from './PlatformAuthProvider.tsx'

const { platformLogin, platformLogout, platformMe, platformRefresh, hasStoredSession, clear } = vi.hoisted(() => ({
  platformLogin: vi.fn(),
  platformLogout: vi.fn(),
  platformMe: vi.fn(),
  platformRefresh: vi.fn(),
  hasStoredSession: vi.fn(),
  clear: vi.fn(),
}))

vi.mock('../api/platformAuth.ts', () => ({ platformLogin, platformLogout, platformMe, platformRefresh }))
vi.mock('./platformSession.ts', () => ({
  platformSession: { hasStoredSession, clear },
  platformSessionExpiredEvent: 'hrms-platform-session-expired',
}))

function Consumer() {
  const { status, user, can } = usePlatformAuth()
  return <div><span data-testid="status">{status}</span><span data-testid="user">{user?.fullName ?? 'none'}</span><span data-testid="permission">{can('PlatformTenant.View') ? 'granted' : 'denied'}</span></div>
}

const user = { id: 'platform-user', email: 'admin@anevra.test', firstName: 'Platform', lastName: 'Admin', fullName: 'Platform Admin', roles: ['PlatformSuperAdmin'], permissions: ['PlatformTenant.View'] }

describe('PlatformAuthProvider session restoration', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    hasStoredSession.mockReturnValue(true)
    platformRefresh.mockResolvedValue({ accessToken: 'access', refreshToken: 'refresh', accessTokenExpiresAtUtc: '2026-09-09T12:00:00Z', expiresInSeconds: 900, user })
    platformMe.mockResolvedValue(user)
  })

  it('waits while restoring and then restores the identity and permissions', async () => {
    render(<PlatformAuthProvider><Consumer /></PlatformAuthProvider>)

    expect(screen.getByTestId('status')).toHaveTextContent('restoring')
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))
    expect(screen.getByTestId('user')).toHaveTextContent('Platform Admin')
    expect(screen.getByTestId('permission')).toHaveTextContent('granted')
    expect(platformRefresh).toHaveBeenCalledTimes(1)
    expect(platformMe).toHaveBeenCalledTimes(1)
  })

  it('becomes anonymous when the stored platform session cannot be refreshed', async () => {
    platformRefresh.mockResolvedValue(null)
    render(<PlatformAuthProvider><Consumer /></PlatformAuthProvider>)

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous'))
    expect(screen.getByTestId('user')).toHaveTextContent('none')
    expect(platformMe).not.toHaveBeenCalled()
  })
})
