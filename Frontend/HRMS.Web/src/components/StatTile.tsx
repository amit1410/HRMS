import type { ApiError } from '../api/errors.ts'
import { formatNumber } from '../lib/format.ts'
import { Spinner } from './Spinner.tsx'

interface StatTileProps {
  label: string
  value: number | null
  hint?: string
  isLoading: boolean
  error: ApiError | null
  icon?: string
}

/**
 * A single headline number.
 *
 * A failed tile shows a short marker instead of the number and keeps the label, so one endpoint the
 * user cannot reach does not take the whole dashboard down with it.
 */
export function StatTile({ label, value, hint, isLoading, error, icon }: StatTileProps) {
  return (
    <div className="stat-tile">
      <span className="stat-icon" aria-hidden="true">{icon || '•'}</span>
      <span className="stat-label">{label}</span>
      <span className="stat-value">
        {isLoading ? (
          <Spinner size={18} />
        ) : error ? (
          <span className="stat-unavailable" title={error.message}>
            —
          </span>
        ) : (
          formatNumber(value ?? 0)
        )}
      </span>
      <span className="stat-hint">{error ? error.message : (hint ?? ' ')}</span>
    </div>
  )
}
