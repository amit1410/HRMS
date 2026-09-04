import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { fetchCurrentUser, login as apiLogin, logout as apiLogout, restoreSession } from '../api/auth.ts'
import { subscribeToSessionEvents } from '../api/client.ts'
import { session } from '../api/session.ts'
import type { AuthenticatedUser, LoginRequest } from '../api/types.ts'
import { AuthContext, type AuthContextValue, type AuthStatus } from './authContext.ts'
import { hasAnyPermission, hasPermission } from './permissions.ts'

/**
 * Owns the session for the whole app: restores it on load, exposes sign-in/sign-out, and reacts to
 * the transport layer discovering that the session is over.
 *
 * The user object is never read from storage. It is whatever the last login or refresh returned, both
 * of which recalculate roles and permissions server-side — so an administrator revoking a permission
 * takes effect on this tab at its next refresh, without a cached copy overriding it.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>(() =>
    session.hasStoredSession() ? 'restoring' : 'anonymous',
  )
  const [user, setUser] = useState<AuthenticatedUser | null>(null)
  const sessionGeneration = useRef(0)

  const loadFreshUser = useCallback(async () => fetchCurrentUser(), [])

  // Exchange the stored refresh token once on load. `restoreSession` shares one in-flight request, so
  // StrictMode's double-invoked effect cannot spend two single-use refresh tokens.
  useEffect(() => {
    if (!session.hasStoredSession()) {
      // Nothing to restore; the initial state is already 'anonymous'.
      return
    }

    let active = true
    restoreSession()
      .then(async (restored) => {
        if (!active) return
        if (restored) {
          const freshUser = await loadFreshUser()
          if (!active) return
          setUser(freshUser)
          setStatus('authenticated')
        } else {
          setUser(null)
          setStatus('anonymous')
        }
      })
      .catch(() => {
        if (!active) return
        setUser(null)
        setStatus('anonymous')
      })

    return () => {
      active = false
    }
  }, [loadFreshUser])

  // The interceptor is the only code that learns a refresh has failed, and a background refresh is
  // the only place fresh permissions arrive. Both are surfaced as session events.
  useEffect(
    () =>
      subscribeToSessionEvents((event) => {
        if (event.type === 'expired') {
          sessionGeneration.current += 1
          setUser(null)
          setStatus('anonymous')
        } else {
          const generation = ++sessionGeneration.current
          void loadFreshUser()
            .then((freshUser) => {
              if (generation === sessionGeneration.current) {
                setUser(freshUser)
                setStatus('authenticated')
              }
            })
            .catch(() => {
              if (generation === sessionGeneration.current) {
                setUser(null)
                setStatus('anonymous')
              }
            })
        }
      }),
    [loadFreshUser],
  )

  // Signing out in one tab should not leave another tab looking signed in. The `storage` event fires
  // only in *other* tabs, so this cannot fight with our own writes.
  useEffect(() => {
    function onStorage(event: StorageEvent) {
      if (event.key === session.refreshTokenKey && event.newValue === null) {
        sessionGeneration.current += 1
        session.setAccessToken(null)
        setUser(null)
        setStatus('anonymous')
      }
    }
    window.addEventListener('storage', onStorage)
    return () => window.removeEventListener('storage', onStorage)
  }, [])

  const login = useCallback(async (credentials: LoginRequest) => {
    const generation = ++sessionGeneration.current
    await apiLogin(credentials)
    const freshUser = await loadFreshUser()
    if (generation !== sessionGeneration.current) return
    setUser(freshUser)
    setStatus('authenticated')
  }, [loadFreshUser])

  const logout = useCallback(async () => {
    sessionGeneration.current += 1
    await apiLogout()
    setUser(null)
    setStatus('anonymous')
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      login,
      logout,
      can: (permission) => hasPermission(user, permission),
      canAny: (permissions) => hasAnyPermission(user, permissions),
    }),
    [status, user, login, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
