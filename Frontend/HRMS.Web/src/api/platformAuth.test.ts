import { beforeEach, describe, expect, it, vi } from 'vitest'

const { post } = vi.hoisted(() => ({ post: vi.fn() }))

vi.mock('./platformClient.ts', () => ({ platformApi: { post, get: vi.fn() } }))

import { platformRefresh } from './platformAuth.ts'
import { platformSession } from '../auth/platformSession.ts'

const response = {
  accessToken: 'platform-access-2',
  refreshToken: 'platform-refresh-2',
  accessTokenExpiresAtUtc: '2026-09-09T12:00:00Z',
  expiresInSeconds: 900,
  user: { id: 'platform-user', email: 'admin@anevra.test', firstName: 'Platform', lastName: 'Admin', fullName: 'Platform Admin', roles: ['PlatformSuperAdmin'], permissions: ['PlatformTenant.View'] },
}

describe('platformRefresh', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    platformSession.clear()
  })

  it('shares one refresh exchange for concurrent startup calls', async () => {
    platformSession.save({ accessToken: 'platform-access-1', refreshToken: 'platform-refresh-1' })
    post.mockResolvedValue({ status: 200, data: { success: true, data: response } })

    const [first, second] = await Promise.all([platformRefresh(), platformRefresh()])

    expect(first).toEqual(response)
    expect(second).toEqual(response)
    expect(post).toHaveBeenCalledTimes(1)
    expect(post).toHaveBeenCalledWith('/api/platform/auth/refresh', { refreshToken: 'platform-refresh-1' })
    expect(platformSession.getRefreshToken()).toBe('platform-refresh-2')
  })

  it('clears the platform session when refresh is rejected', async () => {
    platformSession.save({ accessToken: 'platform-access-1', refreshToken: 'platform-refresh-1' })
    post.mockRejectedValue(new Error('expired'))

    expect(await platformRefresh()).toBeNull()
    expect(platformSession.getRefreshToken()).toBeNull()
  })
})
