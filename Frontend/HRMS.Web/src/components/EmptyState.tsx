import type { ReactNode } from 'react'

interface EmptyStateProps {
  title: string
  message?: string
  /** A way forward, when there is one: "Add the first department", "Clear filters". */
  action?: ReactNode
}

/** Shown when a request succeeded and there is genuinely nothing to display. Never for failures. */
export function EmptyState({ title, message, action }: EmptyStateProps) {
  return (
    <div className="state-block">
      <p className="state-title">{title}</p>
      {message !== undefined && <p className="state-message">{message}</p>}
      {action !== undefined && <div className="state-action">{action}</div>}
    </div>
  )
}
