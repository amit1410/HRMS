import { AxiosError } from 'axios'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { login } from './auth.ts'
import { api, subscribeToSessionEvents, type SessionEvent } from './client.ts'
import { listEmployees } from './employees.ts'
import { ApiError } from './errors.ts'
import { session } from './session.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../test/stubAdapter.ts'
import { makeLoginResponse, makeUser, paged } from '../test/fixtures.ts'

/**
 * The transport layer's behaviour on 401.
 *
 * These go through the real axios instance and the real interceptors, with only the adapter replaced —
 * the refresh-once-and-replay logic is the thing under test, so mocking the endpoint functions would
 * test nothing.
 */
describe('api client', () => {
  let stub: StubAdapter
  let events: SessionEvent[]
  let unsubscribe: () => void

  beforeEach(() => {
    session.clear()
    window.localStorage.clear()
    stub = installStubAdapter()
    events = []
    unsubscribe = subscribeToSessionEvents((event) => events.push(event))
  })

  afterEach(() => {
    unsubscribe()
    stub.restore()
    session.clear()
  })

  describe('request headers', () => {
    it('attaches the access token as a bearer credential', async () => {
      session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
      stub.on('get', '/api/employees', () => ({ data: ok(paged([])) }))

      await listEmployees()

      expect(stub.calls[0]?.authorization).toBe('Bearer access-1')
    })

    it('sends no authorization header when there is no session', async () => {
      stub.on('get', '/api/employees', () => ({ data: ok(paged([])) }))

      await listEmployees()

      expect(stub.calls[0]?.authorization).toBeUndefined()
    })

    it('drops blank query values so the API sees an absent filter', async () => {
      stub.on('get', '/api/employees', () => ({ data: ok(paged([])) }))

      await listEmployees({ search: '', page: 2, status: 'Active' })

      expect(stub.calls[0]?.params).toEqual({ page: 2, status: 'Active' })
    })
  })

  describe('refresh on 401', () => {
    it('refreshes once and replays the original request', async () => {
      session.save({ accessToken: 'expired', refreshToken: 'refresh-1' })
      stub.on('get', '/api/employees', (_call, attempt) =>
        attempt === 0
          ? { status: 401, data: fail('Unauthorized') }
          : { data: ok(paged([{ id: 'x' }])) },
      )
      stub.on('post', '/api/auth/refresh', () => ({
        data: ok(makeLoginResponse({ accessToken: 'access-2', refreshToken: 'refresh-2' })),
      }))

      const result = await listEmployees()

      expect(result.items).toHaveLength(1)
      expect(stub.callsTo('post', '/api/auth/refresh')).toHaveLength(1)
      // The replay must carry the *new* token, not the one that just failed.
      expect(stub.callsTo('get', '/api/employees')[1]?.authorization).toBe('Bearer access-2')
    })

    it('sends the stored refresh token and saves the rotated pair', async () => {
      session.save({ accessToken: 'expired', refreshToken: 'refresh-1' })
      stub.on('get', '/api/employees', (_call, attempt) =>
        attempt === 0 ? { status: 401, data: fail('Unauthorized') } : { data: ok(paged([])) },
      )
      stub.on('post', '/api/auth/refresh', () => ({
        data: ok(makeLoginResponse({ accessToken: 'access-2', refreshToken: 'refresh-2' })),
      }))

      await listEmployees()

      expect(stub.callsTo('post', '/api/auth/refresh')[0]?.body).toEqual({
        refreshToken: 'refresh-1',
      })
      expect(session.getAccessToken()).toBe('access-2')
      expect(session.getRefreshToken()).toBe('refresh-2')
    })

    it('collapses concurrent 401s onto a single refresh', async () => {
      // The reason this is a correctness test and not a performance one: refresh tokens are single-use.
      // A second exchange of the same token is indistinguishable from a replayed stolen token, and the
      // API answers that by revoking every session the user has.
      session.save({ accessToken: 'expired', refreshToken: 'refresh-1' })
      stub.on('get', '/api/employees', (_call, attempt) =>
        attempt < 2
          ? { status: 401, data: fail('Unauthorized'), delay: true }
          : { data: ok(paged([])) },
      )
      stub.on('post', '/api/auth/refresh', () => ({
        data: ok(makeLoginResponse({ accessToken: 'access-2', refreshToken: 'refresh-2' })),
        delay: true,
      }))

      await Promise.all([listEmployees(), listEmployees()])

      expect(stub.callsTo('post', '/api/auth/refresh')).toHaveLength(1)
      expect(stub.callsTo('get', '/api/employees')).toHaveLength(4)
    })

    it('starts a fresh refresh for a later 401 once the first has settled', async () => {
      session.save({ accessToken: 'expired', refreshToken: 'refresh-1' })
      stub.on('get', '/api/employees', (call) =>
        call.authorization === 'Bearer expired'
          ? { status: 401, data: fail('Unauthorized') }
          : { data: ok(paged([])) },
      )
      stub.on('post', '/api/auth/refresh', (_call, attempt) => ({
        data: ok(
          makeLoginResponse({
            accessToken: `access-${attempt + 2}`,
            refreshToken: `refresh-${attempt + 2}`,
          }),
        ),
      }))

      await listEmployees()
      // Simulate the new access token expiring too: the single-flight promise must not be sticky.
      session.setAccessToken('expired')
      await listEmployees()

      expect(stub.callsTo('post', '/api/auth/refresh')).toHaveLength(2)
    })

    it('replays only once, so a second 401 surfaces to the caller', async () => {
      session.save({ accessToken: 'expired', refreshToken: 'refresh-1' })
      stub.on('get', '/api/employees', () => ({ status: 401, data: fail('Unauthorized') }))
      stub.on('post', '/api/auth/refresh', () => ({
        data: ok(makeLoginResponse({ accessToken: 'access-2', refreshToken: 'refresh-2' })),
      }))

      await expect(listEmployees()).rejects.toMatchObject({ status: 401 })

      expect(stub.callsTo('post', '/api/auth/refresh')).toHaveLength(1)
      expect(stub.callsTo('get', '/api/employees')).toHaveLength(2)
    })

    it('ends the session when the refresh is refused', async () => {
      session.save({ accessToken: 'expired', refreshToken: 'revoked' })
      stub.on('get', '/api/employees', () => ({ status: 401, data: fail('Unauthorized') }))
      stub.on('post', '/api/auth/refresh', () => ({
        status: 401,
        data: fail('The refresh token is no longer valid.'),
      }))

      await expect(listEmployees()).rejects.toBeInstanceOf(ApiError)

      expect(session.getAccessToken()).toBeNull()
      expect(session.getRefreshToken()).toBeNull()
      expect(events).toEqual([{ type: 'expired' }])
    })

    it('does not attempt a refresh when nothing is stored to refresh with', async () => {
      session.setAccessToken('expired')
      stub.on('get', '/api/employees', () => ({ status: 401, data: fail('Unauthorized') }))

      await expect(listEmployees()).rejects.toMatchObject({ status: 401 })

      expect(stub.callsTo('post', '/api/auth/refresh')).toHaveLength(0)
      expect(events).toEqual([{ type: 'expired' }])
    })

    it('announces the refreshed user, so permission changes reach an open tab', async () => {
      session.save({ accessToken: 'expired', refreshToken: 'refresh-1' })
      const user = makeUser({ roles: ['TenantAdmin'] })
      stub.on('get', '/api/employees', (_call, attempt) =>
        attempt === 0 ? { status: 401, data: fail('Unauthorized') } : { data: ok(paged([])) },
      )
      stub.on('post', '/api/auth/refresh', () => ({
        data: ok(makeLoginResponse({ accessToken: 'access-2', refreshToken: 'refresh-2', user })),
      }))

      await listEmployees()

      expect(events).toEqual([{ type: 'refreshed', user }])
    })
  })

  describe('routes exempt from refresh', () => {
    it('treats a 401 from login as bad credentials, not an expired session', async () => {
      // A stored token from a previous session must not turn a rejected sign-in into a refresh attempt.
      session.save({ accessToken: 'stale', refreshToken: 'refresh-1' })
      stub.on('post', '/api/auth/login', () => ({
        status: 401,
        data: fail('Invalid credentials.'),
      }))

      await expect(
        login({ email: 'nobody@demo01.test', password: 'wrong' }),
      ).rejects.toMatchObject({ status: 401, message: 'Invalid credentials.' })

      expect(stub.callsTo('post', '/api/auth/refresh')).toHaveLength(0)
    })

    it('does not refresh in order to sign out', async () => {
      session.save({ accessToken: 'expired', refreshToken: 'refresh-1' })
      stub.on('post', '/api/auth/logout', () => ({ status: 401, data: fail('Unauthorized') }))

      await expect(api.post('/api/auth/logout', { refreshToken: 'refresh-1' })).rejects.toBeDefined()

      expect(stub.callsTo('post', '/api/auth/refresh')).toHaveLength(0)
    })
  })

  describe('error normalization', () => {
    it('maps field errors from the validation envelope', async () => {
      stub.on('post', '/api/auth/login', () => ({
        status: 400,
        data: fail('Validation failed.', [
          { field: 'email', message: 'Email is required.' },
          { field: 'password', message: 'Password is required.' },
        ]),
      }))

      const error = await login({ email: '', password: '' }).catch(
        (caught: unknown) => caught,
      )

      expect(error).toBeInstanceOf(ApiError)
      expect((error as ApiError).fieldErrors).toEqual({
        email: 'Email is required.',
        password: 'Password is required.',
      })
    })

    it('reports an unreachable server rather than an axios code', async () => {
      stub.on('get', '/api/employees', () => {
        throw new AxiosError('Network Error', AxiosError.ERR_NETWORK)
      })

      const error = (await listEmployees().catch((caught: unknown) => caught)) as ApiError

      expect(error.isNetworkError).toBe(true)
      expect(error.message).toContain('could not be reached')
    })

    it('swallows nothing but reports a cancellation as canceled', async () => {
      const controller = new AbortController()
      controller.abort()
      stub.on('get', '/api/employees', () => ({ data: ok(paged([])) }))

      const error = (await listEmployees({}, controller.signal).catch(
        (caught: unknown) => caught,
      )) as ApiError

      expect(error.isCanceled).toBe(true)
    })

    it('rejects a 2xx envelope that claims failure', async () => {
      // Should never happen; if it does, `undefined` must not flow into a component as if it were data.
      stub.on('get', '/api/employees', () => ({
        status: 200,
        data: { success: false, message: 'Something odd happened.' },
      }))

      await expect(listEmployees()).rejects.toMatchObject({
        message: 'Something odd happened.',
      })
    })

    it('falls back to a readable message when the body carries none', async () => {
      stub.on('get', '/api/employees', () => ({ status: 500, data: '<html>error</html>' }))

      await expect(listEmployees()).rejects.toMatchObject({
        message: 'The server ran into a problem. Please try again.',
      })
    })
  })
})
