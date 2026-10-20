import type { ReactNode } from 'react'
import { Card } from '../Card.tsx'

export function DashboardWidget({ title, children, className }: { title: string; children: ReactNode; className?: string }) {
  return <Card title={title} className={className ? `dashboard-widget ${className}` : 'dashboard-widget'}>{children}</Card>
}
