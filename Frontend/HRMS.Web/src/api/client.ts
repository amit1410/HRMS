import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { apiOriginConfigFromEnv, resolveApiBaseUrl } from '../lib/apiOrigin.ts'
import { ApiError, toApiError } from './errors.ts'
import { session } from './session.ts'
import type { ApiResponse, AuthenticatedUser, LoginResponse } from './types.ts'

/**
 * The single axios instance every request goes through, plus the token-refresh machinery attached to
 * it. Nothing else in the app touches axios directly.
 */

const DEFAULT_BASE_URL = 'http://localhost:5080'

/** Trailing slashes are trimmed so `baseURL + '/api/…'` cannot produce a double slash. */
const baseURL = (import.meta.env.VITE_API_BASE_URL ?? DEFAULT_BASE_URL).replace(/\/+$/, '')

export const api = axios.create({
  baseURL,
  headers: { 'Content-Type': 'application/json' },
  // A request that has hung for half a minute is not going to succeed; failing lets the UI say so
  // instead of showing a spinner indefinitely.
  timeout: 30_000,
})

/**
 * Refresh runs on its own instance on purpose. Sharing `api` would put the refresh call through the
 * same 401 interceptor that triggered it — a 401 from `/api/auth/refresh` would try to refresh, and
 * so on. A separate instance makes that recursion impossible by construction rather than by flag.
 */
export const refreshApi = axios.create({
  baseURL,
  headers: { 'Content-Type': 'application/json' },
  timeout: 30_000,
})

// ---------------------------------------------------------------------------------------------
// Workspace-aware base URL
// ---------------------------------------------------------------------------------------------

/**
 * The origin this request should leave for, resolved per request: the host in the address bar
 * decides which tenant's API answers, so `demo01.localhost` must call `demo01.localhost:5080`
 * and never another workspace's origin. Without a configured template this is simply the static
 * `VITE_API_BASE_URL`, unchanged.
 *
 * The two instances share the interceptor because a refresh must reach the *same* workspace's API
 * as the request that triggered it — tokens live in one tenant's database, and `localStorage`,
 * where the refresh token sits, is already partitioned by page origin.
 */
function currentBaseUrl(): string {
  return resolveApiBaseUrl(apiOriginConfigFromEnv(import.meta.env), window.location.hostname)
}

for (const instance of [api, refreshApi]) {
  instance.interceptors.request.use((config) => {
    // Axios combines `baseURL` with `url` only after every request interceptor has run, so writing
    // it here is what the eventual request actually uses.
    config.baseURL = currentBaseUrl()
    return config
  })
}

// ---------------------------------------------------------------------------------------------
// Session events
// ---------------------------------------------------------------------------------------------

export type SessionEvent =
  /** The session is over: no stored token, or the server refused the one we had. */
  | { type: 'expired' }
  /** A refresh succeeded. Carries the user, whose roles/permissions the server just recalculated. */
  | { type: 'refreshed'; user: AuthenticatedUser }

type SessionListener = (event: SessionEvent) => void

const listeners = new Set<SessionListener>()

/**
 * Lets the auth layer react to something the transport layer discovered. The interceptor is the only
 * place that learns a refresh has failed, and it has no business knowing about React or routing.
 */
export function subscribeToSessionEvents(listener: SessionListener): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

function emit(event: SessionEvent): void {
  for (const listener of [...listeners]) {
    listener(event)
  }
}

/** Wipes the local session and tells the app. Called when the server will not renew us. */
function endSession(): void {
  session.clear()
  emit({ type: 'expired' })
}

// ---------------------------------------------------------------------------------------------
// Refresh, single-flight
// ---------------------------------------------------------------------------------------------

let refreshInFlight: Promise<LoginResponse | null> | null = null

/**
 * Refreshes the token pair, collapsing concurrent callers onto one request.
 *
 * The de-duplication is a correctness requirement, not an optimisation. Refresh tokens are single-use
 * server-side: the first exchange consumes the token and returns a replacement. If a page fires three
 * requests that all 401 and each refreshed independently, the second and third would present an
 * already-consumed token — which the API cannot distinguish from a stolen one being replayed, so it
 * revokes *every* session for that user. One in-flight refresh, shared by all waiters, is what keeps
 * a burst of expired requests from logging the user out of everything.
 */
export function refreshSession(): Promise<LoginResponse | null> {
  refreshInFlight ??= performRefresh().finally(() => {
    refreshInFlight = null
  })
  return refreshInFlight
}

async function performRefresh(): Promise<LoginResponse | null> {
  const refreshToken = session.getRefreshToken()
  if (!refreshToken) {
    endSession()
    return null
  }

  try {
    const response = await refreshApi.post<ApiResponse<LoginResponse>>('/api/auth/refresh', {
      refreshToken,
    })
    const payload = response.data?.data
    if (!payload?.accessToken) {
      endSession()
      return null
    }

    session.save({ accessToken: payload.accessToken, refreshToken: payload.refreshToken })
    emit({ type: 'refreshed', user: payload.user })
    return payload
  } catch {
    // Any failure here — expired, already used, revoked, or the server being unreachable — means we
    // cannot prove who we are any more. Guessing again would only burn tokens.
    endSession()
    return null
  }
}

// ---------------------------------------------------------------------------------------------
// Interceptors
// ---------------------------------------------------------------------------------------------

api.interceptors.request.use((config) => {
  const token = session.getAccessToken()
  if (token && !config.headers.Authorization) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

/** Requests whose own 401 must never trigger a refresh. */
function isRefreshExempt(url: string | undefined): boolean {
  if (!url) return false
  // Login: a 401 means the credentials were wrong. Refresh: it means the token is dead. Logout: the
  // session is being ended anyway, so renewing it first would be backwards.
  return /\/api\/auth\/(login|refresh|logout)$/.test(url)
}

interface RetriedConfig extends InternalAxiosRequestConfig {
  /** Set once a request has already been replayed with a renewed token, so it cannot loop. */
  hrmsRetried?: boolean
}

api.interceptors.response.use(
  (response) => response,
  async (error: unknown) => {
    if (!(error instanceof AxiosError)) {
      throw toApiError(error)
    }

    const config = error.config as RetriedConfig | undefined

    const shouldTryRefresh =
      error.response?.status === 401 &&
      config !== undefined &&
      !config.hrmsRetried &&
      !isRefreshExempt(config.url) &&
      session.hasStoredSession()

    if (!shouldTryRefresh) {
      // A 401 with no refresh token to fall back on is simply the end of the session.
      if (error.response?.status === 401 && !isRefreshExempt(config?.url)) {
        endSession()
      }
      throw toApiError(error)
    }

    const renewed = await refreshSession()
    if (!renewed) {
      throw toApiError(error)
    }

    // Replay the original request once, with the new credential. `hrmsRetried` guarantees that a
    // second 401 on the replay surfaces to the caller instead of starting another refresh. The
    // config is mutated rather than copied: it carries an `AxiosHeaders` instance and other internal
    // state that a shallow clone would flatten.
    config.hrmsRetried = true
    config.headers.Authorization = `Bearer ${renewed.accessToken}`

    return api.request(config)
  },
)

// ---------------------------------------------------------------------------------------------
// Envelope handling
// ---------------------------------------------------------------------------------------------

/**
 * Unwraps the API's `{ success, message, data }` envelope.
 *
 * A 2xx with `success: false`, or with no `data`, should not happen — but treating it as a failure
 * here means one impossible response cannot become `undefined` flowing into a component that expects
 * a record.
 */
export function unwrap<T>(envelope: ApiResponse<T> | undefined, status?: number): T {
  if (!envelope?.success || envelope.data === undefined || envelope.data === null) {
    throw new ApiError(envelope?.message?.trim() || 'The server returned an unexpected response.', {
      status,
      fieldErrors: Object.fromEntries(
        (envelope?.errors ?? []).map((entry) => [entry.field, entry.message]),
      ),
    })
  }
  return envelope.data
}

/** Query-string values, with anything empty dropped so the API sees an absent filter, not a blank one. */
export type QueryParams = Record<string, string | number | boolean | undefined>

export function cleanParams(params: QueryParams): QueryParams {
  const cleaned: QueryParams = {}
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === '') continue
    cleaned[key] = value
  }
  return cleaned
}

/**
 * Runs a request and normalizes both halves of the result: the envelope on success, an
 * {@link ApiError} on failure. Every call in `src/api` goes through this.
 */
export async function request<T>(
  run: () => Promise<{ data: ApiResponse<T>; status: number }>,
): Promise<T> {
  try {
    const response = await run()
    return unwrap(response.data, response.status)
  } catch (error) {
    throw toApiError(error)
  }
}
