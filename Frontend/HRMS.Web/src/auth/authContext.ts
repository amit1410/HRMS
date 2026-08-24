import { createContext } from 'react'
import type { AuthenticatedUser, LoginRequest } from '../api/types.ts'

/**
 * Session state as the UI sees it.
 *
 * `restoring` is its own state rather than a boolean flag because it is the one case where the app
 * knows nothing yet: a stored refresh token exists and is being exchanged. Rendering the sign-in page
 * during that window would flash the login form at a user who is, in fact, signed in.
 */
export type AuthStatus = 'restoring' | 'authenticated' | 'anonymous'

export interface AuthContextValue {
  status: AuthStatus
  user: AuthenticatedUser | null

  /** Signs in, or throws an `ApiError` whose message is safe to show (the API never says which half was wrong). */
  login: (credentials: LoginRequest) => Promise<void>

  /** Revokes the refresh token server-side and clears local state. Never throws. */
  logout: () => Promise<void>

  /** Whether the signed-in user holds a permission. Cosmetic — the API remains the authority. */
  can: (permission: string) => boolean

  /** Whether the signed-in user holds at least one of these permissions. */
  canAny: (permissions: readonly string[]) => boolean
}

/**
 * No default value: a missing provider is a wiring bug, and `useAuth` throwing at the point of use is
 * far easier to diagnose than a silently anonymous app.
 */
export const AuthContext = createContext<AuthContextValue | null>(null)
