/**
 * Where the two tokens live, and why they live in different places.
 *
 * **Access token: memory only.** It is a bearer credential that grants everything the user can do, so
 * it is never written to `localStorage` or a cookie. A module-level variable dies with the tab, which
 * means a stolen storage dump cannot contain it and an injected script has to be running *while* the
 * user is signed in to see it.
 *
 * **Refresh token: `localStorage`.** Surviving a page reload is the entire point of a refresh token,
 * so it has to be written somewhere. `localStorage` is readable by any script on the origin, which is
 * the honest trade-off here: it buys "reload without signing in again", and it costs XSS resistance
 * for that one credential. The genuinely safe alternative is an `HttpOnly; Secure; SameSite` cookie
 * issued by the API, which no script can read — but that is a server-side change (cookie issuance
 * plus CSRF protection on every state-changing request), not something the client can adopt alone.
 * See the README's "Token handling" section.
 *
 * The refresh token is always read straight from storage rather than cached in memory. Refresh tokens
 * are single-use on the server: if one tab rotates the token, a second tab holding a stale copy would
 * present a consumed token, which the API treats as theft and answers by revoking *every* session for
 * that user. Reading storage at the moment of use keeps tabs on the same token.
 */

const REFRESH_TOKEN_KEY = 'hrms.refreshToken.v1'

let accessToken: string | null = null

/** `localStorage` throws in some privacy modes; a failure to persist must not break sign-in. */
function safeLocalStorage(): Storage | null {
  try {
    return window.localStorage
  } catch {
    return null
  }
}

export interface TokenPair {
  accessToken: string
  refreshToken: string
}

export const session = {
  getAccessToken(): string | null {
    return accessToken
  },

  /** Replaces the in-memory access token (used after a refresh, which returns a new one). */
  setAccessToken(token: string | null): void {
    accessToken = token
  },

  getRefreshToken(): string | null {
    try {
      return safeLocalStorage()?.getItem(REFRESH_TOKEN_KEY) ?? null
    } catch {
      return null
    }
  },

  /** Stores both halves of a fresh pair — from sign-in, or from a rotation. */
  save(tokens: TokenPair): void {
    accessToken = tokens.accessToken
    try {
      safeLocalStorage()?.setItem(REFRESH_TOKEN_KEY, tokens.refreshToken)
    } catch {
      // Storage unavailable: the session still works for this tab, it just will not survive a reload.
    }
  },

  clear(): void {
    accessToken = null
    try {
      safeLocalStorage()?.removeItem(REFRESH_TOKEN_KEY)
    } catch {
      // Nothing to do — the in-memory token is gone, which is what ends the session for this tab.
    }
  },

  /**
   * Whether a reload is worth attempting a refresh for. Only the presence of a stored token is
   * checked, never its contents: it is opaque to the client, and only the server can say whether it
   * is still live.
   */
  hasStoredSession(): boolean {
    return session.getRefreshToken() !== null
  },

  /** The storage key, exposed so cross-tab sign-out can recognise its own `storage` events. */
  refreshTokenKey: REFRESH_TOKEN_KEY,
}
