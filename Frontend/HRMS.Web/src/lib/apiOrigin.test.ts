import { describe, expect, it } from 'vitest'
import { apiOriginConfigFromEnv, resolveApiBaseUrl } from './apiOrigin.ts'

/**
 * The routing half of the tenancy boundary: the label in the address bar decides which API origin a
 * request leaves for. Every rule is pinned here as pure string handling — workspace-labeled hosts,
 * the apex, foreign hosts, and what happens with no template or a broken one.
 */

const DEV = apiOriginConfigFromEnv({
  VITE_API_BASE_URL: 'http://localhost:5080',
  VITE_API_ORIGIN_TEMPLATE: 'http://{workspace}.localhost:5080',
})

describe('resolveApiBaseUrl', () => {
  it('routes a workspace-labeled page to the API origin carrying the same label', () => {
    expect(resolveApiBaseUrl(DEV, 'demo01.localhost')).toBe('http://demo01.localhost:5080')
    expect(resolveApiBaseUrl(DEV, 'demo02.localhost')).toBe('http://demo02.localhost:5080')
  })

  it('is case-insensitive and tolerates a trailing root dot', () => {
    expect(resolveApiBaseUrl(DEV, 'DEMO01.LOCALHOST.')).toBe('http://demo01.localhost:5080')
  })

  it('keeps plain localhost on the configured base URL', () => {
    // The apex has no workspace to name; its answer is the same origin requests always went to.
    expect(resolveApiBaseUrl(DEV, 'localhost')).toBe('http://localhost:5080')
    expect(resolveApiBaseUrl(DEV, 'localhost.')).toBe('http://localhost:5080')
  })

  it('falls back to the configured base URL for a host outside the template domain', () => {
    expect(resolveApiBaseUrl(DEV, '127.0.0.1')).toBe('http://localhost:5080')
    expect(resolveApiBaseUrl(DEV, 'demo01.other.example')).toBe('http://localhost:5080')
  })

  it('refuses a nested label rather than guessing two workspaces deep', () => {
    expect(resolveApiBaseUrl(DEV, 'a.b.localhost')).toBe('http://localhost:5080')
  })

  // The template carries the API's port (`localhost:5080`) and the resolver receives a bare
  // hostname — the dev server's own port never leaks into the API origin.
  it('derives the port from the template alone', () => {
    expect(resolveApiBaseUrl(DEV, 'demo02.localhost')).toBe('http://demo02.localhost:5080')
  })

  describe('without a template', () => {
    const config = apiOriginConfigFromEnv({ VITE_API_BASE_URL: 'http://localhost:5080' })

    it('sends every host to the configured base URL — the pre-workspace behaviour', () => {
      expect(resolveApiBaseUrl(config, 'localhost')).toBe('http://localhost:5080')
      expect(resolveApiBaseUrl(config, 'demo01.localhost')).toBe('http://localhost:5080')
      expect(resolveApiBaseUrl(config, 'hrms.example')).toBe('http://localhost:5080')
    })
  })

  describe('with a malformed template', () => {
    const config = apiOriginConfigFromEnv({
      VITE_API_BASE_URL: 'http://localhost:5080',
      VITE_API_ORIGIN_TEMPLATE: '{workspace}.localhost',
    })

    it('ignores the template instead of producing an unroutable origin', () => {
      expect(resolveApiBaseUrl(config, 'demo01.localhost')).toBe('http://localhost:5080')
    })
  })

  it('defaults to the dev base URL when no configuration is present at all', () => {
    expect(resolveApiBaseUrl({}, 'demo01.localhost')).toBe('http://localhost:5080')
  })
})

describe('apiOriginConfigFromEnv', () => {
  it('reads both variables and tolerates absence', () => {
    expect(apiOriginConfigFromEnv({})).toEqual({})
    expect(apiOriginConfigFromEnv({ VITE_API_BASE_URL: 'https://api.example' })).toEqual({
      baseUrl: 'https://api.example',
    })
  })
})
