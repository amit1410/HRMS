import type { ReactNode } from 'react'

interface CardProps {
  title?: ReactNode
  subtitle?: ReactNode
  /** Right-hand slot in the header: a filter, a link, an export button. */
  actions?: ReactNode
  /** Dims the body during a background reload so the content does not jump. */
  isRefreshing?: boolean
  className?: string
  children: ReactNode
}

/** The one surface used to group content. Every panel on a page is one of these. */
export function Card({ title, subtitle, actions, isRefreshing, className, children }: CardProps) {
  return (
    <section className={className ? `card ${className}` : 'card'}>
      {(title !== undefined || actions !== undefined) && (
        <header className="card-header">
          <div>
            {title !== undefined && <h2 className="card-title">{title}</h2>}
            {subtitle !== undefined && <p className="card-subtitle">{subtitle}</p>}
          </div>
          {actions !== undefined && <div className="card-actions">{actions}</div>}
        </header>
      )}
      <div className={isRefreshing ? 'card-body is-refreshing' : 'card-body'}>{children}</div>
    </section>
  )
}
