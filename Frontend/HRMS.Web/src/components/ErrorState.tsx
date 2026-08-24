import type { ApiError } from '../api/errors.ts'

interface ErrorStateProps {
  error: ApiError
  onRetry?: () => void
}

/**
 * A failed read, explained.
 *
 * The message comes from the API's envelope where there is one, so a refused export says "narrow the
 * filters" rather than "request failed". A retry is only offered when it could plausibly help — a 403
 * will not resolve itself, so the button is hidden and the cause is spelled out instead.
 */
export function ErrorState({ error, onRetry }: ErrorStateProps) {
  const retryable = !error.isForbidden && !error.isUnauthorized
  const hint = error.isForbidden
    ? 'Your roles do not include the permission this needs. Ask an administrator to grant it.'
    : error.isNetworkError
      ? 'The API did not respond. Confirm it is running, then try again.'
      : undefined

  return (
    <div className="state-block state-error" role="alert">
      <p className="state-title">Could not load this</p>
      <p className="state-message">{error.message}</p>
      {hint !== undefined && <p className="state-hint">{hint}</p>}
      {retryable && onRetry !== undefined && (
        <div className="state-action">
          <button type="button" className="button button-secondary" onClick={onRetry}>
            Try again
          </button>
        </div>
      )}
    </div>
  )
}
