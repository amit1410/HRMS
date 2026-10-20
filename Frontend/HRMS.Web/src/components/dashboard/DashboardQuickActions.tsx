import { Link } from 'react-router-dom'
import { Card } from '../Card.tsx'

export type DashboardActionIcon = 'people' | '+' | 'calendar' | 'check' | 'exit' | 'settings' | 'users' | 'shield'
export interface DashboardAction { to: string; label: string; icon: DashboardActionIcon; show: boolean }

export function DashboardQuickActions({ actions, bare = false }: { actions: readonly DashboardAction[]; bare?: boolean }) {
  const authorizedActions = actions.filter((action) => action.show)
  const visibleActions = authorizedActions.filter((action, index, authorized) => authorized.findIndex((candidate) => candidate.to === action.to) === index)
  if (visibleActions.length === 0) return null
  const content = <div className="dashboard-quick-actions">
    {visibleActions.map((action) => <Link className="dashboard-quick-action" to={action.to} key={action.to} aria-label={action.label} title={action.label}>
      <span className="dashboard-quick-action-icon" aria-hidden="true"><QuickActionIcon name={action.icon} /></span><span>{action.label}</span><span className="dashboard-quick-action-arrow" aria-hidden="true"><ArrowIcon /></span>
    </Link>)}
  </div>
  return bare ? content : <Card title="Quick actions" subtitle="Common tasks">{content}</Card>
}

function QuickActionIcon({ name }: { name: DashboardActionIcon }) {
  if (name === '+') return <svg viewBox="0 0 24 24" focusable="false"><path d="M12 5v14M5 12h14" /></svg>
  if (name === 'people' || name === 'users') return <svg viewBox="0 0 24 24" focusable="false"><circle cx="9" cy="8" r="3" /><path d="M3.5 19c.4-3 2.2-4.5 5.5-4.5s5.1 1.5 5.5 4.5M16 5.5a3 3 0 1 1 0 5.7M17 14.7c2 .5 3.2 1.9 3.5 4.3" /></svg>
  if (name === 'calendar') return <svg viewBox="0 0 24 24" focusable="false"><rect x="4" y="5" width="16" height="15" rx="2" /><path d="M8 3v4M16 3v4M4 10h16" /></svg>
  if (name === 'check') return <svg viewBox="0 0 24 24" focusable="false"><path d="m5 12 4.2 4.2L19 6.5" /></svg>
  if (name === 'exit') return <svg viewBox="0 0 24 24" focusable="false"><path d="M13 5H6a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h7M15 8l4 4-4 4M9 12h10" /></svg>
  if (name === 'settings') return <svg viewBox="0 0 24 24" focusable="false"><circle cx="12" cy="12" r="3.5" /><path d="m19.4 15 .1.1-1.6 2.7-.2-.1a2 2 0 0 0-2.8 1.2v.2h-3.2v-.2a2 2 0 0 0-2.8-1.2l-.2.1-1.6-2.7.1-.1a2 2 0 0 0 0-3l-.1-.1 1.6-2.7.2.1a2 2 0 0 0 2.8-1.2V8h3.2v.2a2 2 0 0 0 2.8 1.2l.2-.1 1.6 2.7-.1.1a2 2 0 0 0 0 3Z" /></svg>
  return <svg viewBox="0 0 24 24" focusable="false"><path d="M12 3 20 6v5.5c0 4.8-3.3 7.8-8 9.5-4.7-1.7-8-4.7-8-9.5V6l8-3Z" /><path d="m8.5 12 2.2 2.2 4.8-5" /></svg>
}

function ArrowIcon() {
  return <svg viewBox="0 0 24 24" focusable="false"><path d="M5 12h13M13 6l6 6-6 6" /></svg>
}
