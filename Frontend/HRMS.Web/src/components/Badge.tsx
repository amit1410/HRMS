import type { ReactNode } from 'react'
import type { EmployeeStatus } from '../api/types.ts'

export type BadgeTone = 'neutral' | 'success' | 'warning' | 'danger' | 'info'

export function Badge({ tone = 'neutral', children }: { tone?: BadgeTone; children: ReactNode }) {
  return <span className={`badge badge-${tone}`}>{children}</span>
}

/** Employment status, coloured the same way everywhere it appears. */
export function StatusBadge({ status }: { status: EmployeeStatus }) {
  const tone: BadgeTone =
    status === 'Active' ? 'success' : status === 'Resigned' ? 'warning' : 'danger'
  return <Badge tone={tone}>{status}</Badge>
}

/** Active/inactive for departments and designations. */
export function ActiveBadge({ isActive }: { isActive: boolean }) {
  return <Badge tone={isActive ? 'success' : 'neutral'}>{isActive ? 'Active' : 'Inactive'}</Badge>
}
