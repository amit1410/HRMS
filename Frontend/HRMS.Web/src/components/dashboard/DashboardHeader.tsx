import type { ReactNode } from 'react'

export function DashboardHeader({ title, subtitle, actions }: { title: string; subtitle: ReactNode; actions?: ReactNode }) {
  return <header className="dashboard-header">
    <div><h1>{title}</h1><p>{subtitle}</p></div>
    {actions !== undefined && <div className="dashboard-header-actions">{actions}</div>}
  </header>
}
