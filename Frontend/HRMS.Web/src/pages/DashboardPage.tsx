import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { listDepartments } from '../api/departments.ts'
import { listDesignations } from '../api/designations.ts'
import { listEmployees } from '../api/employees.ts'
import { Permissions } from '../auth/permissions.ts'
import { useAuth } from '../auth/useAuth.ts'
import { Card } from '../components/Card.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { PageHeader } from '../components/PageHeader.tsx'
import { useDocumentTitle } from '../hooks/useDocumentTitle.ts'
import { CountTile } from './dashboard/CountTile.tsx'
import { ExportEmployeesButton } from './dashboard/ExportEmployeesButton.tsx'
import { HeadcountByDepartmentCard } from './dashboard/HeadcountByDepartmentCard.tsx'
import { RecentHiresCard } from './dashboard/RecentHiresCard.tsx'

export function DashboardPage() {
  useDocumentTitle('Dashboard')
  const { user, can } = useAuth()
  const canViewEmployees = can(Permissions.employee.view)
  const canViewDepartments = can(Permissions.department.view)
  const canViewDesignations = can(Permissions.designation.view)
  const canExport = can(Permissions.employee.export)
  const hasAnything = canViewEmployees || canViewDepartments || canViewDesignations
  const canCreateEmployee = can(Permissions.employee.create)
  const canConfigureCodes = can(Permissions.employeeCodeConfiguration.view)
  const calendarDate = useLocalDate()
  const month = calendarDate.toLocaleString('en-US', { month: 'long' }).toUpperCase()
  const day = String(calendarDate.getDate()).padStart(2, '0')
  const weekday = calendarDate.toLocaleString('en-US', { weekday: 'long' })

  return <>
    <PageHeader title="Dashboard" subtitle="Your people, at a glance." actions={canExport ? <ExportEmployeesButton /> : undefined} />
    {!hasAnything ? <Card><EmptyState title="Nothing to show yet" message="Your roles do not yet include permission to view employees, departments or designations. An administrator can grant them." /></Card> : <>
      <section className="dashboard-welcome" aria-label="Welcome message">
        <div className="dashboard-welcome-copy"><p className="dashboard-welcome-eyebrow">People operations</p><h2>Welcome back, {user?.firstName || 'there'}</h2><p>A clear view of your organization.</p></div>
        <div className="dashboard-welcome-art" aria-hidden="true"><span className="welcome-art-back welcome-art-back-one" /><span className="welcome-art-back welcome-art-back-two" /><div className="welcome-calendar"><div className="welcome-calendar-header">{month}</div><div className="welcome-calendar-body"><strong>{day}</strong><span>{weekday}</span></div><span className="welcome-calendar-ring welcome-calendar-ring-one" /><span className="welcome-calendar-ring welcome-calendar-ring-two" /></div></div>
      </section>
      <div className="stat-grid">
        {canViewEmployees && <><CountTile label="Total employees" hint="All records, any status" icon="♙" load={(signal) => listEmployees({ pageSize: 1 }, signal)} /><CountTile label="Active employees" hint="Currently employed" icon="✓" load={(signal) => listEmployees({ pageSize: 1, status: 'Active' }, signal)} /></>}
        {canViewDepartments && <CountTile label="Departments" hint="Active only" icon="▦" load={(signal) => listDepartments({ pageSize: 1, isActive: true }, signal)} />}
        {canViewDesignations && <CountTile label="Designations" hint="Active only" icon="✦" load={(signal) => listDesignations({ pageSize: 1, isActive: true }, signal)} />}
      </div>
      <div className="dashboard-grid">{canViewDepartments && <HeadcountByDepartmentCard />}<QuickActions canCreateEmployee={canCreateEmployee} canViewEmployees={canViewEmployees} canConfigureCodes={canConfigureCodes} /></div>
      {canViewEmployees && <RecentHiresCard />}
    </>}
  </>
}

function QuickActions({ canCreateEmployee, canViewEmployees, canConfigureCodes }: { canCreateEmployee: boolean; canViewEmployees: boolean; canConfigureCodes: boolean }) {
  const actions = [{ to: '/employees/new', label: 'Add employee', icon: '+', show: canCreateEmployee }, { to: '/employees', label: 'View directory', icon: '↗', show: canViewEmployees }, { to: '/configuration/employee-code', label: 'Configure employee codes', icon: '⚙', show: canConfigureCodes }].filter((action) => action.show)
  if (actions.length === 0) return null
  return <Card title="Quick actions" subtitle="Common tasks"><div className="quick-actions">{actions.map((action) => <Link className="quick-action" to={action.to} key={action.to}><span className="quick-action-icon" aria-hidden="true">{action.icon}</span><span>{action.label}</span><span className="quick-action-arrow" aria-hidden="true">→</span></Link>)}</div></Card>
}

function useLocalDate(): Date {
  const [date, setDate] = useState(() => new Date())
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout>
    const schedule = () => { const now = new Date(); const nextMidnight = new Date(now); nextMidnight.setHours(24, 0, 0, 0); timer = setTimeout(() => { setDate(new Date()); schedule() }, Math.max(1000, nextMidnight.getTime() - now.getTime())) }
    const refresh = () => { if (document.visibilityState === 'visible') setDate(new Date()) }
    schedule(); document.addEventListener('visibilitychange', refresh)
    return () => { clearTimeout(timer); document.removeEventListener('visibilitychange', refresh) }
  }, [])
  return date
}
