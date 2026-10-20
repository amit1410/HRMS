import { listDepartments } from '../api/departments.ts'
import { listDesignations } from '../api/designations.ts'
import { listEmployees } from '../api/employees.ts'
import { Permissions } from '../auth/permissions.ts'
import { useAuth } from '../auth/useAuth.ts'
import { Card } from '../components/Card.tsx'
import { DashboardEmptyState } from '../components/dashboard/DashboardEmptyState.tsx'
import { DashboardHeader } from '../components/dashboard/DashboardHeader.tsx'
import { DashboardQuickActions } from '../components/dashboard/DashboardQuickActions.tsx'
import { DashboardSection } from '../components/dashboard/DashboardSection.tsx'
import { DashboardShell } from '../components/dashboard/DashboardShell.tsx'
import { composeDashboard } from '../components/dashboard/composeDashboard.ts'
import { useDocumentTitle } from '../hooks/useDocumentTitle.ts'
import { ExportEmployeesButton } from './dashboard/ExportEmployeesButton.tsx'
import { HeadcountByDepartmentCard } from './dashboard/HeadcountByDepartmentCard.tsx'
import { EmployeeStatusCard } from './dashboard/EmployeeStatusCard.tsx'
import { RecentHiresCard } from './dashboard/RecentHiresCard.tsx'
import { CountTile } from './dashboard/CountTile.tsx'
import { AttendanceOperationsWidget, HrLeaveSummaryWidget, IdentityFallback, ManagerAttendanceWidget, SelfServiceWidgets } from '../components/dashboard/RoleDashboardWidgets.tsx'

export function DashboardPage() {
  useDocumentTitle('Dashboard')
  const { user, can } = useAuth()
  const canViewEmployees = can(Permissions.employee.view)
  const canViewDepartments = can(Permissions.department.view)
  const canViewDesignations = can(Permissions.designation.view)
  const canExport = can(Permissions.employee.export)
  const canCreateEmployee = can(Permissions.employee.create)
  const canConfigureCodes = can(Permissions.employeeCodeConfiguration.view)
  const composition = composeDashboard({ workspace: 'tenant', permissions: user?.permissions ?? [], user })
  const hasAnything = canViewEmployees || canViewDepartments || canViewDesignations || !composition.isFallback
  const hasWidget = (id: string) => composition.widgets.some((widget) => widget.id === id)
  const linked = user?.employeeIdentity?.status === 'Linked' && Boolean(user.employeeIdentity.employee?.id)
  const hasSelfAccess = can(Permissions.leave.requestViewOwn) || can(Permissions.leave.balanceViewOwn) || can(Permissions.payroll.payslipViewOwn)
  const quickActions = [
    { to: '/employees', label: 'Employee directory', icon: 'people', show: canViewEmployees },
    { to: '/employees/new', label: 'Add employee', icon: '+', show: canCreateEmployee },
    { to: '/leave-management', label: 'Leave administration', icon: 'calendar', show: can(Permissions.leave.dashboardViewAll) },
    { to: '/confirmation/review-cases', label: 'Confirmation reviews', icon: 'check', show: can(Permissions.confirmation.reviewCaseView) },
    { to: '/separation/hr-inbox', label: 'Separation workflows', icon: 'exit', show: can(Permissions.separation.hrReview) },
    { to: '/configuration/employee-code', label: 'Configure employee codes', icon: 'settings', show: canConfigureCodes },
    { to: '/role-management', label: 'User and role management', icon: 'users', show: can(Permissions.roleManagement.assignmentView) || can(Permissions.user.view) },
    { to: '/page-access-management', label: 'Page access management', icon: 'shield', show: can(Permissions.pageAccess.view) },
    { to: '/configuration/login-settings', label: 'Tenant configuration', icon: 'settings', show: can(Permissions.user.edit) },
  ] as const
  const showQuickActions = quickActions.some((action) => action.show)
  const topQuickActions = quickActions.filter((action) => ['/employees', '/employees/new', '/configuration/employee-code'].includes(action.to))
  const hrQuickActions = quickActions.filter((action) => ['/employees', '/employees/new', '/leave-management', '/confirmation/review-cases', '/separation/hr-inbox'].includes(action.to))
  const administrationQuickActions = quickActions.filter((action) => ['/role-management', '/page-access-management', '/configuration/login-settings'].includes(action.to))
  const today = new Date()
  const todayLabel = today.toLocaleDateString(undefined, { weekday: 'long' })
  const monthLabel = today.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })
  const displayName = user?.fullName || [user?.firstName, user?.lastName].filter(Boolean).join(' ') || 'there'
  const coreActionRoutes = ['/employees', '/employees/new', '/configuration/employee-code']
  const additionalHrActions = hrQuickActions.filter((action) => !coreActionRoutes.includes(action.to))

  return <DashboardShell>
    <DashboardHeader title="Dashboard" subtitle={user?.tenantName ? `${user.tenantName} · Your people, at a glance.` : 'Your people, at a glance.'} actions={canExport ? <ExportEmployeesButton /> : undefined} />
    {!hasAnything || composition.isFallback ? (hasSelfAccess && !linked ? <IdentityFallback /> : <Card><DashboardEmptyState /></Card>) : <>
      <section className="dashboard-welcome" aria-label="Welcome message">
        <div className="dashboard-welcome-copy"><p className="dashboard-welcome-eyebrow">People operations</p><h2>Welcome back, {displayName}</h2><p>A clear view of your organization.</p></div>
        <div className="dashboard-welcome-art" aria-label={`${todayLabel}, ${today.getDate()} ${monthLabel}`}>
          <span className="welcome-art-back welcome-art-back-one" aria-hidden="true" />
          <span className="welcome-art-back welcome-art-back-two" aria-hidden="true" />
          <span className="welcome-calendar" aria-hidden="true"><span className="welcome-calendar-header">{todayLabel}</span><span className="welcome-calendar-body"><strong>{today.getDate()}</strong><span>{monthLabel}</span></span></span>
        </div>
      </section>
      <DashboardSection title="At a glance" className="dashboard-section-kpis"><div className="stat-grid">
        {canViewEmployees && <><CountTile label="Total employees" hint="Scoped employee records" icon="•" load={(signal) => listEmployees({ pageSize: 1 }, signal)} /><CountTile label="Active employees" hint="Currently employed" icon="✓" load={(signal) => listEmployees({ pageSize: 1, status: 'Active' }, signal)} /></>}
        {canViewDepartments && <CountTile label="Departments" hint="Active only" icon="▦" load={(signal) => listDepartments({ pageSize: 1, isActive: true }, signal)} />}
        {canViewDesignations && <CountTile label="Designations" hint="Active only" icon="✦" load={(signal) => listDesignations({ pageSize: 1, isActive: true }, signal)} />}
      </div></DashboardSection>
      <DashboardSection title="Analytics and actions"><div className="dashboard-analytics-grid">
        {canViewDepartments && <HeadcountByDepartmentCard />}
        {canViewEmployees && <EmployeeStatusCard />}
        {topQuickActions.some((action) => action.show) && <div className="dashboard-top-actions"><Card title="Quick actions" subtitle="Common tasks"><DashboardQuickActions actions={topQuickActions} bare /></Card></div>}
      </div></DashboardSection>
      {canViewEmployees && <DashboardSection title="Recent employees"><RecentHiresCard /></DashboardSection>}
      {(hasWidget('tenant.hr-leave-summary') || hasWidget('tenant.attendance-operations') || hasWidget('tenant.manager-attendance')) && <DashboardSection title="Authorized operations" className="dashboard-role-section"><div className="dashboard-role-grid">{hasWidget('tenant.hr-leave-summary') && <HrLeaveSummaryWidget />}{hasWidget('tenant.attendance-operations') && <AttendanceOperationsWidget />}{hasWidget('tenant.manager-attendance') && <ManagerAttendanceWidget />}</div></DashboardSection>}
      {(hasWidget('tenant.self-profile') || hasWidget('tenant.self-leave-balances') || hasWidget('tenant.self-leave-requests')) && <DashboardSection title="My workspace" className="dashboard-role-section"><SelfServiceWidgets showProfile={hasWidget('tenant.self-profile') && linked} showBalances={hasWidget('tenant.self-leave-balances') && linked} showRequests={hasWidget('tenant.self-leave-requests') && linked} /></DashboardSection>}
      {showQuickActions && (additionalHrActions.some((action) => action.show) || administrationQuickActions.some((action) => action.show)) && <section className="dashboard-action-groups" aria-label="Additional dashboard actions">
        {additionalHrActions.some((action) => action.show) && <Card title="HR quick actions" subtitle="Common tasks"><DashboardQuickActions actions={additionalHrActions} bare /></Card>}
        {administrationQuickActions.some((action) => action.show) && <Card title="Administration quick actions" subtitle="Common tasks"><DashboardQuickActions actions={administrationQuickActions} bare /></Card>}
      </section>}

    </>}
  </DashboardShell>
}
