import { useEffect, useRef, useState, type DependencyList } from 'react'
import { toApiError, type ApiError } from '../api/errors.ts'

/**
 * Runs a read against the API and tracks its lifecycle.
 *
 * Two things it deliberately does:
 *
 * - **Aborts.** Every fetcher receives an `AbortSignal` that fires when the dependencies change or the
 *   component unmounts. It is the client-side counterpart of the `CancellationToken` each backend
 *   query already takes: a search the user has typed past should stop occupying a connection.
 * - **Swallows cancellations.** An aborted request is not a failure the user should read about, so a
 *   canceled `ApiError` never reaches state. Without that, typing in a search box would flash an error
 *   on every keystroke.
 *
 * There is no cache and no shared store. A dashboard and three list screens do not need one, and a
 * cache that nobody invalidates correctly is worse than a refetch.
 */
export interface ApiQueryResult<T> {
  /** The last successful payload; `null` until the first one arrives. */
  data: T | null
  error: ApiError | null
  /** First load, or a reload after a failure — there is nothing on screen yet. */
  isLoading: boolean
  /** A reload while previous data is still displayed, so the table can stay put and dim instead. */
  isRefreshing: boolean
  refetch: () => void
}

interface QueryState<T> {
  data: T | null
  error: ApiError | null
  isLoading: boolean
  isRefreshing: boolean
}

export function useApiQuery<T>(
  fetcher: (signal: AbortSignal) => Promise<T>,
  deps: DependencyList,
): ApiQueryResult<T> {
  const fetcherRef = useRef(fetcher)

  // Declared *before* the query effect on purpose: effects run in declaration order within a commit,
  // so the ref is already current by the time the query below reads it. That lets callers pass an
  // inline arrow — closing over whatever props they like — without every render triggering a request.
  useEffect(() => {
    fetcherRef.current = fetcher
  })

  const [attempt, setAttempt] = useState(0)
  const [state, setState] = useState<QueryState<T>>({
    data: null,
    error: null,
    isLoading: true,
    isRefreshing: false,
  })

  useEffect(() => {
    const controller = new AbortController()
    let active = true

    // The external system this effect synchronizes with is the HTTP request itself, and "a request is
    // now in flight" is not derivable during render. The update is a reset, not a cascade: it runs once
    // per dependency change, and the `previous.data` check is what keeps a refetch showing the old rows
    // instead of blanking the screen.
    // oxlint-disable-next-line react/set-state-in-effect
    setState((previous) => ({
      data: previous.data,
      error: null,
      isLoading: previous.data === null,
      isRefreshing: previous.data !== null,
    }))

    fetcherRef
      .current(controller.signal)
      .then((data) => {
        if (!active) return
        setState({ data, error: null, isLoading: false, isRefreshing: false })
      })
      .catch((error: unknown) => {
        if (!active) return
        const apiError = toApiError(error)
        // Our own abort, not a problem worth reporting.
        if (apiError.isCanceled) return
        setState({ data: null, error: apiError, isLoading: false, isRefreshing: false })
      })

    return () => {
      active = false
      controller.abort()
    }
    // `attempt` drives refetch(); the caller's deps drive re-reads when the query changes. A spread is
    // the only way to accept caller-supplied deps, and the linter cannot check what it cannot see — the
    // contract is the same as React's own: a call site keeps the length of `deps` constant.
    // oxlint-disable-next-line react-hooks/exhaustive-deps
  }, [attempt, ...deps])

  return {
    data: state.data,
    error: state.error,
    isLoading: state.isLoading,
    isRefreshing: state.isRefreshing,
    refetch: () => setAttempt((value) => value + 1),
  }
}
