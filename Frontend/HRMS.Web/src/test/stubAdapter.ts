import {
  AxiosError,
  AxiosHeaders,
  CanceledError,
  type AxiosAdapter,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from 'axios'
import { api, refreshApi } from '../api/client.ts'

/**
 * A fake transport for both axios instances.
 *
 * Replacing `defaults.adapter` — rather than mocking the `api` module or stubbing `fetch` — keeps the
 * real interceptors in the loop. That matters here more than usual: the behaviour worth testing *is* the
 * interceptor chain (attach the bearer token, refresh once on 401, replay the original request), and a
 * mock of `api.get` would skip exactly the code under test.
 *
 * The adapter reproduces the parts of axios's contract the client depends on: `validateStatus` deciding
 * success, a rejection carrying an `AxiosError` with the response attached, `AbortSignal` producing a
 * `CanceledError`, and response headers normalised through `AxiosHeaders`.
 */

export interface StubResponse {
  status?: number
  data?: unknown
  headers?: Record<string, string>
  /** Resolve after a tick, so a test can interleave a second request or abort the first. */
  delay?: boolean
}

export interface StubCall {
  method: string
  /** The origin the request was aimed at — what workspace-aware routing decided for this call. */
  baseURL: string
  url: string
  /** Request body, parsed back from JSON when it was serialized. */
  body: unknown
  params: Record<string, unknown>
  authorization: string | undefined
}

export type StubHandler = (
  call: StubCall,
  /** 0 for the first request to this route, 1 for the second, and so on. */
  attempt: number,
) => StubResponse

interface Route {
  method: string
  url: string | RegExp
  handler: StubHandler
  attempts: number
}

export interface StubAdapter {
  /** Registers (or replaces) the handler for a route. */
  on: (method: string, url: string | RegExp, handler: StubHandler) => void
  /** Every request that reached the adapter, in order. */
  calls: StubCall[]
  /** Calls matching a route, for counting refreshes. */
  callsTo: (method: string, url: string) => StubCall[]
  restore: () => void
}

export function installStubAdapter(): StubAdapter {
  const routes: Route[] = []
  const calls: StubCall[] = []

  const previousApiAdapter = api.defaults.adapter
  const previousRefreshAdapter = refreshApi.defaults.adapter

  const adapter: AxiosAdapter = async (config) => {
    const call = toCall(config)
    calls.push(call)

    throwIfAborted(config)

    const route = routes.find(
      (candidate) =>
        candidate.method === call.method &&
        (typeof candidate.url === 'string' ? candidate.url === call.url : candidate.url.test(call.url)),
    )

    if (!route) {
      throw new Error(`No stub registered for ${call.method.toUpperCase()} ${call.url}`)
    }

    const stub = route.handler(call, route.attempts)
    route.attempts += 1

    if (stub.delay) {
      await Promise.resolve()
      await Promise.resolve()
    }

    throwIfAborted(config)

    const status = stub.status ?? 200
    const response: AxiosResponse = {
      data: stub.data,
      status,
      statusText: String(status),
      headers: AxiosHeaders.from(stub.headers ?? {}),
      config,
      request: {},
    }

    const isValid = config.validateStatus ?? ((code: number) => code >= 200 && code < 300)
    if (isValid(status)) {
      return response
    }

    throw new AxiosError(
      `Request failed with status code ${status}`,
      status >= 500 ? AxiosError.ERR_BAD_RESPONSE : AxiosError.ERR_BAD_REQUEST,
      config,
      {},
      response,
    )
  }

  api.defaults.adapter = adapter
  refreshApi.defaults.adapter = adapter

  return {
    on(method, url, handler) {
      const normalized = method.toLowerCase()
      const existing = routes.findIndex(
        (route) => route.method === normalized && String(route.url) === String(url),
      )
      const route: Route = { method: normalized, url, handler, attempts: 0 }
      if (existing >= 0) {
        routes[existing] = route
      } else {
        routes.push(route)
      }
    },
    calls,
    callsTo(method, url) {
      const normalized = method.toLowerCase()
      return calls.filter((call) => call.method === normalized && call.url === url)
    },
    restore() {
      api.defaults.adapter = previousApiAdapter
      refreshApi.defaults.adapter = previousRefreshAdapter
    },
  }
}

function toCall(config: InternalAxiosRequestConfig): StubCall {
  const authorization = config.headers?.Authorization
  return {
    method: (config.method ?? 'get').toLowerCase(),
    baseURL: config.baseURL ?? '',
    url: config.url ?? '',
    body: parseBody(config.data),
    params: (config.params ?? {}) as Record<string, unknown>,
    authorization: typeof authorization === 'string' ? authorization : undefined,
  }
}

function parseBody(data: unknown): unknown {
  if (typeof data !== 'string') return data
  try {
    return JSON.parse(data)
  } catch {
    return data
  }
}

function throwIfAborted(config: InternalAxiosRequestConfig): void {
  if (config.signal?.aborted) {
    throw new CanceledError('canceled', config)
  }
}

/** The API's success envelope, so tests read as the API would answer. */
export function ok<T>(data: T, message = 'OK') {
  return { success: true, message, data }
}

/** The API's failure envelope, with optional field errors. */
export function fail(message: string, errors?: { field: string; message: string }[]) {
  return { success: false, message, ...(errors ? { errors } : {}) }
}
