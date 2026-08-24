import { api, refreshSession, request } from './client.ts'
import { session } from './session.ts'
import type { ApiResponse, AuthenticatedUser, LoginRequest, LoginResponse } from './types.ts'

/**
 * Sign-in, sign-out and "who am I".
 *
 * Signing in replaces whatever session was there before, so any previous tokens are dropped *first* —
 * a stale bearer header on the login request would be pointless at best and confusing in the logs.
 */
export async function login(credentials: LoginRequest): Promise<LoginResponse> {
  session.clear()

  const result = await request<LoginResponse>(() =>
    api.post<ApiResponse<LoginResponse>>('/api/auth/login', credentials),
  )

  session.save({ accessToken: result.accessToken, refreshToken: result.refreshToken })
  return result
}

/**
 * Restores a session after a page reload by exchanging the stored refresh token. Returns null when
 * there is nothing to restore, or when the server refuses — both mean "show the sign-in page".
 */
export function restoreSession(): Promise<LoginResponse | null> {
  if (!session.hasStoredSession()) {
    return Promise.resolve(null)
  }
  return refreshSession()
}

/**
 * The signed-in user as the server sees them right now. Roles and permissions are recalculated from
 * the database on each call, so this is how a permission change reaches an open tab.
 */
export function fetchCurrentUser(signal?: AbortSignal): Promise<AuthenticatedUser> {
  return request<AuthenticatedUser>(() =>
    api.get<ApiResponse<AuthenticatedUser>>('/api/auth/me', { signal }),
  )
}

/**
 * Revokes the refresh token server-side, then clears local state.
 *
 * The local clear happens whatever the server says. A user who asks to sign out must end up signed
 * out even if the network is down or the token was already dead — leaving them in a half-session
 * because a revoke call failed would be the wrong way round.
 */
export async function logout(): Promise<void> {
  const refreshToken = session.getRefreshToken()
  try {
    if (refreshToken) {
      await api.post<ApiResponse<boolean>>('/api/auth/logout', { refreshToken })
    }
  } catch {
    // Deliberately swallowed; see above.
  } finally {
    session.clear()
  }
}
