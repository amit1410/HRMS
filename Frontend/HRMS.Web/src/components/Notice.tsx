import type { ReactNode } from 'react'

export type NoticeTone = 'success' | 'error' | 'info'

interface NoticeProps {
  tone?: NoticeTone
  children: ReactNode
  /** Renders a dismiss button when provided. Omit for messages that should stay until navigation. */
  onDismiss?: () => void
}

/**
 * A banner above a page's content: "Department created", "Could not save".
 *
 * The role is chosen by tone, and the distinction is not cosmetic. A success is `status` — announced
 * politely, after whatever the screen reader was already saying. A failure is `alert`, which interrupts,
 * because the user is about to act on the assumption that it worked.
 */
export function Notice({ tone = 'info', children, onDismiss }: NoticeProps) {
  return (
    <div className={`notice notice-${tone}`} role={tone === 'error' ? 'alert' : 'status'}>
      <span className="notice-text">{children}</span>
      {onDismiss !== undefined && (
        <button type="button" className="notice-close" onClick={onDismiss} aria-label="Dismiss">
          ×
        </button>
      )}
    </div>
  )
}
