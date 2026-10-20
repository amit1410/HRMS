import type { ReactNode } from 'react'

export function DashboardShell({ children, className }: { children: ReactNode; className?: string }) {
  return <main className={className ? `dashboard-shell ${className}` : 'dashboard-shell'}>{children}</main>
}
