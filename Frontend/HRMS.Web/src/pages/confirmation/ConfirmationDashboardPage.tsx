import type { ReactElement } from 'react'
import { Link } from 'react-router-dom'
import { getHrConfirmationDashboardSummary, type HrConfirmationDashboardSummary } from '../../api/confirmationDashboard.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Badge, type BadgeTone } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'

export function ConfirmationDashboardPage() {
  useDocumentTitle('Confirmation Dashboard')
  const { user, can } = useAuth()
  const linked = user?.employeeIdentity?.status === 'Linked' && Boolean(user.employeeIdentity.employee)
  const canViewHrDashboard = can(Permissions.confirmation.dashboardViewAll)
  const hrDashboard = useApiQuery(
  (signal) => canViewHrDashboard ? getHrConfirmationDashboardSummary(undefined, signal) : Promise.resolve(null),
  [canViewHrDashboard],
)

  const displayName = user?.employeeIdentity?.employee?.displayName ?? user?.fullName ?? 'you'
  if (!linked) return <div className="leave-admin-page confirmation-dashboard-page"><DashboardHero displayName={displayName} period={null} /><div className="confirmation-dashboard-grid"><Card className="confirmation-dashboard-identity-card"><DashboardEmpty icon="user" title="Confirmation access needs an employee link" message="Your account is not linked to an active employee. Contact HR before accessing confirmation data." /></Card></div></div>

  const failures = [hrDashboard.error].filter(Boolean)

  return <div className="leave-admin-page confirmation-dashboard-page">
    <DashboardHero displayName={displayName} period={hrDashboard.data ? { from: hrDashboard.data.from, to: hrDashboard.data.to, name: hrDashboard.data.currentConfirmationPeriodName } : null} />
    <QuickActions canConfigure={can(Permissions.confirmation.policyView)} />
    {failures.length > 0 && <Notice tone="error">Some Confirmation dashboard sections could not be loaded. The available sections are still shown.</Notice>}
    <div className="confirmation-dashboard-stat-grid">
      <SummaryStat icon="users" tone="teal" label="Probation Employees" value={hrDashboard.isLoading ? '…' : hrDashboard.data?.kpis.probationEmployees ?? 0} hint="Active probation cases" href="/confirmation/review-cases" />
      <SummaryStat icon="calendar" tone="blue" label="Upcoming Confirmations" value={hrDashboard.isLoading ? '…' : hrDashboard.data?.kpis.upcomingConfirmations ?? 0} hint="Due in future" href="/confirmation/review-cases?status=Open&status=InReview&status=Completed" />
      <SummaryStat icon="calendar" tone="red" label="Overdue Confirmations" value={hrDashboard.isLoading ? '…' : hrDashboard.data?.kpis.overdueConfirmations ?? 0} hint="Past due" href="/confirmation/review-cases?status=Open&status=InReview&status=Completed" />
      <SummaryStat icon="clock" tone="amber" label="Pending Manager Actions" value={hrDashboard.isLoading ? '…' : hrDashboard.data?.kpis.pendingManagerActions ?? 0} hint="Reviews awaited" href="/confirmation/review-cases?status=Open&status=InReview" />
      <SummaryStat icon="clock" tone="amber" label="Pending HRBP Actions" value={hrDashboard.isLoading ? '…' : hrDashboard.data?.kpis.pendingHrbpActions ?? 0} hint="Reviews awaited" href="/confirmation/review-cases?status=Open&status=InReview" />
      <SummaryStat icon="check" tone="green" label="Confirmed Employees" value={hrDashboard.isLoading ? '…' : hrDashboard.data?.kpis.confirmedEmployees ?? 0} hint="Successfully confirmed" href="/confirmation/review-cases?outcome=Confirmed" />
      <SummaryStat icon="check" tone="warning" label="Extended Probation" value={hrDashboard.isLoading ? '…' : hrDashboard.data?.kpis.extendedProbation ?? 0} hint="Probation extended" href="/confirmation/review-cases?outcome=ProbationExtended" />
      <SummaryStat icon="check" tone="danger" label="Rejected/Not Confirmed" value={hrDashboard.isLoading ? '…' : hrDashboard.data?.kpis.rejectedNotConfirmed ?? 0} hint="Not confirmed" href="/confirmation/review-cases?outcome=NotConfirmed" />
    </div>
    <div className="confirmation-dashboard-grid">
      <StatusBreakdownCard query={hrDashboard} />
      <OutcomeBreakdownCard query={hrDashboard} />
      <DepartmentBreakdownCard query={hrDashboard} />
      <DesignationBreakdownCard query={hrDashboard} />
      <TrendCard query={hrDashboard} />
      {canViewHrDashboard && <PendingActionsCard query={hrDashboard} />}
    </div>
  </div>
}

function DashboardHero({ displayName, period }: { displayName: string; period: { from: string; to: string; name?: string | null } | null }) {
  return <header className="confirmation-dashboard-hero"><div className="confirmation-dashboard-hero-copy"><span className="confirmation-dashboard-eyebrow">Welcome back</span><h1>Confirmation Dashboard</h1><p>A quick overview for {displayName}</p><span className="confirmation-dashboard-hero-support">Manage probation reviews, confirmations, and employee transitions.</span></div>{period && <div className="confirmation-dashboard-period"><span>Period</span><strong>{period.from} – {period.to}</strong>{period.name && <small>{period.name}</small>}</div>}</header>
}

function StatusBreakdownCard({ query }: { query: { data: HrConfirmationDashboardSummary | null; error: { message: string } | null; isLoading: boolean } }) {
  const data = query.data
  return <Card className="confirmation-dashboard-status-card" title={<span className="card-title-with-icon"><span className="card-icon is-teal"><Icon name="clipboard" /></span>Status Breakdown</span>} subtitle={data ? `${data.from} → ${data.to}${data.currentConfirmationPeriodName ? ` · ${data.currentConfirmationPeriodName}` : ''}` : 'Confirmation case statuses'}>
    {query.isLoading ? <DashboardSkeleton label="Loading status breakdown" /> : query.error ? <WidgetError message={query.error.message} /> : !data ? <DashboardEmpty icon="clipboard" title="No status data" /> : <>
      <div className="confirmation-dashboard-status-list">
        {data.statusBreakdown.map(item => (
          <div key={item.status} className="confirmation-dashboard-status-item">
            <span className="status-label">{item.status}</span>
            <span className="status-count">{item.count}</span>
          </div>
        ))}
      </div>
    </>}
  </Card>
}

function OutcomeBreakdownCard({ query }: { query: { data: HrConfirmationDashboardSummary | null; error: { message: string } | null; isLoading: boolean } }) {
  const data = query.data
  return <Card className="confirmation-dashboard-outcome-card" title={<span className="card-title-with-icon"><span className="card-icon is-green"><Icon name="check" /></span>Outcome Breakdown</span>} subtitle={data ? `${data.from} → ${data.to}${data.currentConfirmationPeriodName ? ` · ${data.currentConfirmationPeriodName}` : ''}` : 'Confirmation outcomes'}>
    {query.isLoading ? <DashboardSkeleton label="Loading outcome breakdown" /> : query.error ? <WidgetError message={query.error.message} /> : !data ? <DashboardEmpty icon="check" title="No outcome data" /> : <>
      <div className="confirmation-dashboard-outcome-list">
        {data.outcomeBreakdown.map(item => {
          let tone: BadgeTone = 'neutral'
          switch (item.outcome) {
            case 'Confirmed': tone = 'success'; break
            case 'ProbationExtended': tone = 'warning'; break
            case 'NotConfirmed': tone = 'danger'; break
            case 'AdditionalReview': tone = 'info'; break
            default: tone = 'neutral'
          }
          return (
            <div key={item.outcome} className="confirmation-dashboard-outcome-item">
              <span className="outcome-label">{item.outcome}</span>
              <Badge tone={tone}>{item.count}</Badge>
            </div>
          )
        })}
      </div>
    </>}
  </Card>
}

function DepartmentBreakdownCard({ query }: { query: { data: HrConfirmationDashboardSummary | null; error: { message: string } | null; isLoading: boolean } }) {
  const data = query.data
  return <Card className="confirmation-dashboard-department-card" title={<span className="card-title-with-icon"><span className="card-icon is-blue"><Icon name="layers" /></span>Department Breakdown</span>} subtitle={data ? `${data.from} → ${data.to}${data.currentConfirmationPeriodName ? ` · ${data.currentConfirmationPeriodName}` : ''}` : 'Employees by department'}>
    {query.isLoading ? <DashboardSkeleton label="Loading department breakdown" /> : query.error ? <WidgetError message={query.error.message} /> : !data ? <DashboardEmpty icon="layers" title="No department data" /> : <>
      <div className="confirmation-dashboard-department-list">
        {data.departmentBreakdown.slice(0, 10).map(item => (
          <div key={item.name} className="confirmation-dashboard-department-item">
            <span className="dept-name">{item.name}</span>
            <span className="dept-count">{item.employeeCount}</span>
          </div>
        ))}
        {data.departmentBreakdown.length > 10 && (
          <div className="confirmation-dashboard-department-item">
            <span className="dept-name">and {data.departmentBreakdown.length - 10} more</span>
            <span className="dept-count"></span>
          </div>
        )}
      </div>
    </>}
  </Card>
}

function DesignationBreakdownCard({ query }: { query: { data: HrConfirmationDashboardSummary | null; error: { message: string } | null; isLoading: boolean } }) {
  const data = query.data
  return <Card className="confirmation-dashboard-designation-card" title={<span className="card-title-with-icon"><span className="card-icon is-blue"><Icon name="layers" /></span>Designation Breakdown</span>} subtitle={data ? `${data.from} → ${data.to}${data.currentConfirmationPeriodName ? ` · ${data.currentConfirmationPeriodName}` : ''}` : 'Employees by designation'}>
    {query.isLoading ? <DashboardSkeleton label="Loading designation breakdown" /> : query.error ? <WidgetError message={query.error.message} /> : !data ? <DashboardEmpty icon="layers" title="No designation data" /> : <>
      <div className="confirmation-dashboard-designation-list">
        {data.designationBreakdown.slice(0, 10).map(item => (
          <div key={item.name} className="confirmation-dashboard-designation-item">
            <span className="desig-name">{item.name}</span>
            <span className="desig-count">{item.employeeCount}</span>
          </div>
        ))}
        {data.designationBreakdown.length > 10 && (
          <div className="confirmation-dashboard-designation-item">
            <span className="desig-name">and {data.designationBreakdown.length - 10} more</span>
            <span className="desig-count"></span>
          </div>
        )}
      </div>
    </>}
  </Card>
}

function TrendCard({ query }: { query: { data: HrConfirmationDashboardSummary | null; error: { message: string } | null; isLoading: boolean } }) {
  const data = query.data
  return <Card className="confirmation-dashboard-trend-card" title={<span className="card-title-with-icon"><span className="card-icon is-purple"><Icon name="chart" /></span>Confirmation Trends</span>} subtitle={data ? `${data.from} → ${data.to}${data.currentConfirmationPeriodName ? ` · ${data.currentConfirmationPeriodName}` : ''}` : 'Monthly confirmation trends'}>
    {query.isLoading ? <DashboardSkeleton label="Loading trend data" /> : query.error ? <WidgetError message={query.error.message} /> : !data ? <DashboardEmpty icon="chart" title="No trend data" /> : <>
      <div className="confirmation-dashboard-trend-list">
        {data.trend.map(item => (
          <div key={item.period} className="confirmation-dashboard-trend-item">
            <div className="trend-period">{item.period}</div>
            <div className="trend-bars">
              <span className="trend-bar confirmed" style={{ width: Math.min(item.confirmedCount * 10, 100) }}>{item.confirmedCount}</span>
              <span className="trend-bar extended" style={{ width: Math.min(item.extendedCount * 10, 100) }}>{item.extendedCount}</span>
              <span className="trend-bar rejected" style={{ width: Math.min(item.rejectedCount * 10, 100) }}>{item.rejectedCount}</span>
            </div>
          </div>
        ))}
      </div>
    </>}
  </Card>
}

function PendingActionsCard({ query }: { query: { data: HrConfirmationDashboardSummary | null; error: { message: string } | null; isLoading: boolean } }) {
  const data = query.data
  return <Card className="confirmation-dashboard-actions-card" title={<span className="card-title-with-icon"><span className="card-icon is-amber"><Icon name="clock" /></span>Pending Actions</span>} subtitle="Reviews awaiting submission">
    {query.isLoading ? <DashboardSkeleton label="Loading pending actions" /> : query.error ? <WidgetError message={query.error.message} /> : !data ? <DashboardEmpty icon="clock" title="No pending actions" /> : <>
      <div className="confirmation-dashboard-actions-list">
        {data.pendingActions.slice(0, 5).map(item => (
          <div key={`${item.employeeCode ?? item.employeeName}-${item.actionType}`} className="confirmation-dashboard-action-item">
            <div className="action-info">
              <strong>{item.employeeName}</strong>
              {item.employeeCode && <span className="muted">({item.employeeCode})</span>}
            </div>
            <div className="action-details">
              <span className="action-type">{item.actionType}</span>
              <span className="action-due">Due: {item.dueDate}</span>
            </div>
          </div>
        ))}
        {data.pendingActions.length > 5 && (
          <div className="confirmation-dashboard-action-item">
            <span className="action-info">and {data.pendingActions.length - 5} more pending actions</span>
            <span className="action-details"></span>
          </div>
        )}
      </div>
    </>}
  </Card>
}

function QuickActions({ canConfigure }: { canConfigure: boolean }) {
  const actions = [{ to: '/confirmation/policies', label: 'Confirmation Policies', icon: 'layers', show: canConfigure }].filter(action => action.show !== false)
  const subtitles: Record<string, string> = { 'Confirmation Policies': 'View and manage confirmation policies' }
  return <nav className="confirmation-dashboard-actions" aria-label="Confirmation quick actions">{actions.map(action => <Link className={`confirmation-dashboard-action`} to={action.to} key={action.to}><span className="confirmation-dashboard-action-icon"><Icon name={action.icon as IconName} /></span><span><strong>{action.label}</strong><small>{subtitles[action.label]}</small></span><span className="action-arrow" aria-hidden="true">→</span></Link>)}</nav>
}

function SummaryStat({ icon, tone, label, value, hint, href }: { icon: IconName; tone: string; label: string; value: string | number; hint: string; href: string }) {
  return <Link className="confirmation-dashboard-stat" to={href}><span className={`confirmation-dashboard-stat-icon is-${tone}`}><Icon name={icon} /></span><span className="confirmation-dashboard-stat-label">{label}</span><strong>{value}</strong><span className="muted">{hint}</span></Link>
}

function DashboardSkeleton({ label }: { label: string }) { return <div className="dashboard-skeleton" aria-label={label}><span /><span /><span /></div> }
function WidgetError({}: { message: string }) { return <div className="dashboard-widget-error"><span>Unable to load this section. Please try again.</span><button type="button" className="button button-link" onClick={() => window.location.reload()}>Retry</button></div> }
function DashboardEmpty({ icon, title, message }: { icon: IconName; title: string; message?: string }) { return <div className="dashboard-empty"><span className="dashboard-empty-icon"><Icon name={icon} /></span><EmptyState title={title} message={message} /></div> }

type IconName = 'calendar' | 'check' | 'clipboard' | 'clock' | 'chart' | 'layers' | 'plus' | 'user' | 'users' | 'wallet'
function Icon({ name }: { name: IconName }) {
  const paths: Record<IconName, ReactElement> = {
    calendar: <><rect x="3" y="4" width="18" height="17" rx="2" /><path d="M16 2v4M8 2v4M3 10h18" /></>,
    check: <><circle cx="12" cy="12" r="9" /><path d="m8 12 2.5 2.5L16 9" /></>,
    clipboard: <><rect x="5" y="4" width="14" height="17" rx="2" /><path d="M9 4V3h6v1M8 9h8M8 13h6" /></>,
    clock: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" /></>,
    chart: <><path d="M5 19V9M12 19V5M19 19v-8" /><path d="M3 19h18" /></>,
    layers: <><path d="m12 3 8 4-8 4-8-4 8-4Z" /><path d="m4 12 8 4 8-4M4 16l8 4 8-4" /></>,
    plus: <><circle cx="12" cy="12" r="9" /><path d="M12 8v8M8 12h8" /></>,
    user: <><circle cx="12" cy="8" r="3" /><path d="M5 20c.7-3.2 3-5 7-5s6.3 1.8 7 5" /></>,
    users: <><circle cx="9" cy="8" r="3" /><path d="M3 20c.5-3 2.5-5 6-5s5.5 2 6 5M16 5.5a3 3 0 0 1 0 5.5M17 15c2.3.4 3.6 2 4 4" /></>,
    wallet: <><path d="M4 7h16v12H4zM4 7l2-3h12l2 3" /><path d="M4 7l2-3h12l2 3" /><path d="M16 13h4" /></>,
  }
  return <svg className="dashboard-icon" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8">{paths[name]}</svg>
}