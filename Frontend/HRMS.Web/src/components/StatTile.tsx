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

export function StatTile({ label, value, hint, isLoading, error }: StatTileProps) {
  return (
    <div className="stat-tile">
      <span className="stat-icon" aria-hidden="true"><StatIcon name={label} /></span>
      <span className="stat-label">{label}</span>
      <span className="stat-value">
        {isLoading ? <Spinner size={18} /> : error ? <span className="stat-unavailable" title={error.message}>—</span> : formatNumber(value ?? 0)}
      </span>
      <span className="stat-hint">{error ? error.message : (hint ?? ' ')}</span>
    </div>
  )
}

function StatIcon({ name }: { name: string }) {
  if (name === 'Active employees') return <svg viewBox="0 0 24 24" focusable="false"><circle cx="12" cy="8" r="3" /><path d="M5 20c.5-3.7 2.8-5.5 7-5.5s6.5 1.8 7 5.5" /></svg>
  if (name === 'Departments') return <svg viewBox="0 0 24 24" focusable="false"><path d="M5 20V5h10v15M15 9h4v11M8 8h4M8 12h4M8 16h4" /></svg>
  if (name === 'Designations') return <svg viewBox="0 0 24 24" focusable="false"><path d="M5 8.5h14v10H5zM8 8.5V6h8v2.5M9 13h6M12 11v4" /></svg>
  return <svg viewBox="0 0 24 24" focusable="false"><circle cx="9" cy="8" r="3" /><path d="M3.5 19c.4-3 2.2-4.5 5.5-4.5s5.1 1.5 5.5 4.5M16 5.5a3 3 0 1 1 0 5.7M17 14.7c2 .5 3.2 1.9 3.5 4.3" /></svg>
}
