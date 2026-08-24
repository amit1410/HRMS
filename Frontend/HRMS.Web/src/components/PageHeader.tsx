import type { ReactNode } from 'react'

interface PageHeaderProps {
  title: string
  subtitle?: ReactNode
  /** Right-hand slot: "New employee", "Export CSV", or both. */
  actions?: ReactNode
}

/**
 * The heading block every screen opens with.
 *
 * `title` is the page's only `<h1>` — one per document, so the heading outline a screen reader reads out
 * matches the navigation the user just used to get here.
 */
export function PageHeader({ title, subtitle, actions }: PageHeaderProps) {
  return (
    <div className="page-head">
      <div>
        <h1 className="page-title">{title}</h1>
        {subtitle !== undefined && <p className="page-subtitle">{subtitle}</p>}
      </div>
      {actions !== undefined && <div className="page-actions">{actions}</div>}
    </div>
  )
}
