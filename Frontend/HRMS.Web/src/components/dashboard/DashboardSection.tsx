import type { ReactNode } from 'react'

export function DashboardSection({ title, children, className }: { title: string; children: ReactNode; className?: string }) {
  return <section className={className ? `dashboard-section ${className}` : 'dashboard-section'} aria-labelledby={`dashboard-section-${title.replace(/\s+/g, '-').toLowerCase()}`}>
    <h2 id={`dashboard-section-${title.replace(/\s+/g, '-').toLowerCase()}`}>{title}</h2>{children}
  </section>
}
