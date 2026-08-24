import { beforeEach, describe, expect, it } from 'vitest'
import { session } from './session.ts'

describe('session storage', () => {
  beforeEach(() => {
    session.clear()
    window.localStorage.clear()
  })

  it('keeps the access token out of storage', () => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })

    expect(session.getAccessToken()).toBe('access-1')
    // The whole point of holding it in memory: a storage dump must not contain a bearer credential.
    expect(window.localStorage.getItem(session.refreshTokenKey)).toBe('refresh-1')
    expect(JSON.stringify(window.localStorage)).not.toContain('access-1')
  })

  it('reads the refresh token from storage every time, not from a cached copy', () => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })

    // Stands in for another tab rotating the token: this tab must pick up the new value, because
    // presenting the consumed one would look like a replay and revoke every session.
    window.localStorage.setItem(session.refreshTokenKey, 'refresh-2')

    expect(session.getRefreshToken()).toBe('refresh-2')
  })

  it('reports a stored session only while a refresh token is present', () => {
    expect(session.hasStoredSession()).toBe(false)

    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    expect(session.hasStoredSession()).toBe(true)

    session.clear()
    expect(session.hasStoredSession()).toBe(false)
  })

  it('clears both halves', () => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })

    session.clear()

    expect(session.getAccessToken()).toBeNull()
    expect(window.localStorage.getItem(session.refreshTokenKey)).toBeNull()
  })

  it('allows the access token to be replaced without touching the stored one', () => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })

    session.setAccessToken('access-2')

    expect(session.getAccessToken()).toBe('access-2')
    expect(session.getRefreshToken()).toBe('refresh-1')
  })
})
