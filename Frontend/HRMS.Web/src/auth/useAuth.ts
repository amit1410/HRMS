import { useContext } from 'react'
import { AuthContext, type AuthContextValue } from './authContext.ts'

/**
 * Access to the session for the whole app. Throws if called outside an {@link AuthProvider}, which
 * is a wiring bug — it means something rendered before the provider did, or a test forgot to wrap.
 */
export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (context === null) {
    throw new Error('useAuth must be used inside an AuthProvider.')
  }
  return context
}
