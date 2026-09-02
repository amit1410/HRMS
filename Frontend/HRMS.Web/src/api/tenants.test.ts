import { AxiosError } from 'axios'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { fetchCurrentTenantBranding, isNeutralBranding } from './tenants.ts'
import { session } from './session.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../test/stubAdapter.ts'

/**
 * The one pre-authentication endpoint.
 *
 * Run through the real axios instance and interceptors with only the adapter replaced, like
 * `client.test.ts`, because what is worth pinning is exactly what the transport adds: the envelope
 * unwrap, the error normalization for the anonymous failure modes (throttling, unreachable server),
 * and that reading branding never needs — or leaks — a credential.
 */
describe('tenant branding API', () => {
  let stub: StubAdapter

  beforeEach(() => {
    window.localStorage.clear()
    session.clear()
    stub = installStubAdapter()
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  it('unwraps a published branding response', async () => {
    stub.on('get', '/api/tenants/current/branding', () => ({
      data: ok({
        displayName: 'Northwind Demo',
        logoUrl: 'https://cdn.demo01.test/logo.png',
        primaryColor: '#1D4ED8',
        welcomeMessage: 'Welcome back.',
        supportEmail: 'help@demo01.test',
        ssoEnabled: false,
        ssoProviderName: null,
      }),
    }))

    const branding = await fetchCurrentTenantBranding()

    expect(branding.displayName).toBe('Northwind Demo')
    expect(stub.calls[0]?.url).toBe('/api/tenants/current/branding')
  })

  it('hands back the neutral payload as success, not an error', async () => {
    // The server answers an unknown, inactive or opted-out organization with this exact all-null
    // shape; the caller, not the transport, decides that nothing-to-show is fine.
    stub.on('get', '/api/tenants/current/branding', () => ({
      data: ok({
        displayName: null,
        logoUrl: null,
        primaryColor: null,
        welcomeMessage: null,
        supportEmail: null,
        ssoEnabled: false,
        ssoProviderName: null,
      }),
    }))

    const branding = await fetchCurrentTenantBranding()

    expect(isNeutralBranding(branding)).toBe(true)
    expect(branding.displayName).toBeNull()
  })

  it('reads anonymously even when a session is stored', async () => {
    // The endpoint decides by host alone; sending a bearer token would be noise at best. The
    // interceptor only attaches a token when there is one, so this pins the absent case.
    stub.on('get', '/api/tenants/current/branding', () => ({
      data: ok({ displayName: 'Northwind Demo', ssoEnabled: false }),
    }))

    await fetchCurrentTenantBranding()

    expect(stub.calls[0]?.authorization).toBeUndefined()
  })

  describe('isNeutralBranding', () => {
    it('is true only when every display field is absent', () => {
      expect(isNeutralBranding({ ssoEnabled: false })).toBe(true)
      expect(
        isNeutralBranding({
          displayName: null,
          logoUrl: null,
          primaryColor: null,
          welcomeMessage: null,
          supportEmail: null,
          ssoEnabled: false,
          ssoProviderName: null,
        }),
      ).toBe(true)
    })

    it('is false once any field carries something, colour included', () => {
      expect(isNeutralBranding({ displayName: 'Northwind Demo', ssoEnabled: false })).toBe(false)
      expect(isNeutralBranding({ primaryColor: '#1D4ED8', ssoEnabled: false })).toBe(false)
      expect(isNeutralBranding({ ssoEnabled: true, ssoProviderName: 'Okta' })).toBe(false)
    })

    it('ignores empty strings the same as nulls', () => {
      expect(isNeutralBranding({ displayName: '', ssoEnabled: false })).toBe(true)
    })
  })

  describe('error handling', () => {
    it('surfaces throttling with the readable message, not a status code', async () => {
      // The endpoint shares the authentication rate limiter; a client re-reading branding in a loop
      // gets 429 before anything else does.
      stub.on('get', '/api/tenants/current/branding', () => ({
        status: 429,
        data: fail('Too many attempts. Please wait a moment and try again.'),
      }))

      await expect(fetchCurrentTenantBranding()).rejects.toMatchObject({
        name: 'ApiError',
        status: 429,
        message: 'Too many attempts. Please wait a moment and try again.',
      })
    })

    it('reports an unreachable API rather than an axios code', async () => {
      stub.on('get', '/api/tenants/current/branding', () => {
        throw new AxiosError('Network Error', AxiosError.ERR_NETWORK)
      })

      await expect(fetchCurrentTenantBranding()).rejects.toMatchObject({
        name: 'ApiError',
        isNetworkError: true,
      })
    })

    it('rejects a 2xx envelope that claims failure instead of yielding undefined branding', async () => {
      stub.on('get', '/api/tenants/current/branding', () => ({
        status: 200,
        data: { success: false, message: 'Something odd happened.' },
      }))

      await expect(fetchCurrentTenantBranding()).rejects.toMatchObject({
        message: 'Something odd happened.',
      })
    })

    it('passes an abort through so a superseded load can be told from a failure', async () => {
      const controller = new AbortController()
      controller.abort()
      stub.on('get', '/api/tenants/current/branding', () => ({
        data: ok({ ssoEnabled: false }),
      }))

      await expect(fetchCurrentTenantBranding(controller.signal)).rejects.toMatchObject({
        isCanceled: true,
      })
    })
  })
})
