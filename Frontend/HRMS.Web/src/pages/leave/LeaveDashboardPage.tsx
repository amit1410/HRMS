import type { ReactElement } from 'react'
import { Link } from 'react-router-dom'
import { listMyLeaveBalances, type LeaveBalanceSummary } from '../../api/leaveBalances.ts'
import { listLeaveApprovals, listMyLeaveRequests, type LeaveApprovalListItem, type LeaveRequestListItem, type LeaveRequestStatus } from '../../api/leaveRequests.ts'
import { listLeaveCalendar, type LeaveCalendarEvent } from '../../api/leaveCalendar.ts'
import { getHrLeaveDashboardSummary, type HrLeaveDashboardSummary } from '../../api/leaveDashboard.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Badge, type BadgeTone } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'

const emptyPage = { items: [], page: 1, pageSize: 5, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }
const statusLabels: Record<LeaveRequestStatus, string> = { PendingApproval: 'Pending approval', Approved: 'Approved', Rejected: 'Rejected', Withdrawn: 'Withdrawn', Cancelled: 'Cancelled' }

function pad(value: number) { return String(value).padStart(2, '0') }
function dateValue(date: Date) { return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}` }
function addDays(date: Date, days: number) { const result = new Date(date); result.setDate(result.getDate() + days); return result }
function formatQuantity(value: number) { return Number.isInteger(value) ? String(value) : value.toFixed(2).replace(/0+$/, '').replace(/\.$/, '') }
function statusTone(status: LeaveRequestStatus): BadgeTone { return status === 'Approved' ? 'success' : status === 'PendingApproval' ? 'warning' : status === 'Rejected' ? 'danger' : 'neutral' }

export function LeaveDashboardPage() {
  useDocumentTitle('Leave Dashboard')
  const { user, can } = useAuth()
  const linked = user?.employeeIdentity?.status === 'Linked' && Boolean(user.employeeIdentity.employee)
  const canApprove = can(Permissions.leave.approve)
  const canViewHrDashboard = can(Permissions.leave.dashboardViewAll)
  const today = dateValue(new Date())
  const calendarTo = dateValue(addDays(new Date(), 30))
  const balances = useApiQuery(signal => linked ? listMyLeaveBalances(signal) : Promise.resolve([]), [linked])
  const requests = useApiQuery(() => linked ? listMyLeaveRequests(1, 25) : Promise.resolve(emptyPage), [linked])
  const approvals = useApiQuery(() => canApprove ? listLeaveApprovals(1, 5) : Promise.resolve(emptyPage), [canApprove])
  const calendar = useApiQuery(signal => linked ? listLeaveCalendar(today, calendarTo, signal) : Promise.resolve([]), [linked, today, calendarTo])
  const hrDashboard = useApiQuery(signal => canViewHrDashboard ? getHrLeaveDashboardSummary(undefined, signal) : Promise.resolve(null), [canViewHrDashboard])

  if (!linked) return <div className="leave-admin-page leave-dashboard-page"><PageHeader title="Leave Dashboard" subtitle="Your Leave overview" />{canViewHrDashboard && <HrDashboardCard query={hrDashboard} />}<Card><DashboardEmpty icon="user" title="Leave access needs an employee link" message="Your account is not linked to an active employee. Contact HR before applying for or viewing Leave." /></Card></div>

  const requestItems = requests.data?.items ?? []
  const upcoming = requestItems.filter(item => item.status === 'Approved' && item.endDate >= today).sort((a, b) => a.startDate.localeCompare(b.startDate)).slice(0, 5)
  const pending = requestItems.filter(item => item.status === 'PendingApproval')
  const approved = requestItems.filter(item => item.status === 'Approved')
  const recent = requestItems.filter(item => item.status !== 'PendingApproval' && item.status !== 'Approved').sort((a, b) => (b.submittedAtUtc ?? '').localeCompare(a.submittedAtUtc ?? '')).slice(0, 5)
  const teamEvents = (calendar.data ?? []).filter(event => event.startDate >= today).slice(0, 5)
  const onLeaveToday = (calendar.data ?? []).filter(event => event.status === 'Approved' && event.startDate <= today && event.endDate >= today)
  const failures = [balances.error, requests.error, canApprove ? approvals.error : null, calendar.error].filter(Boolean)

  return <div className="leave-admin-page leave-dashboard-page">
    <PageHeader title="Leave Dashboard" subtitle={`A quick overview for ${user?.employeeIdentity?.employee?.displayName ?? user?.fullName ?? 'you'}`} actions={<QuickActions canApprove={canApprove} canConfigure={can(Permissions.leave.policyView)} />} />
    {failures.length > 0 && <Notice tone="error">Some Leave summary sections could not be loaded. The available sections are still shown.</Notice>}
    <div className="leave-dashboard-stat-grid">
      <SummaryStat icon="clock" tone="amber" label={canApprove ? 'Approvals waiting' : 'Pending requests'} value={canApprove && !approvals.isLoading ? approvals.data?.totalCount ?? 0 : requests.isLoading ? '…' : pending.length} hint={canApprove ? 'Assigned to you' : 'Awaiting approval'} href={canApprove ? '/leave-management/approvals' : '/leave-management/my-requests'} />
      <SummaryStat icon="calendar" tone="blue" label="Upcoming Leave" value={requests.isLoading ? '…' : upcoming.length} hint="Approved requests" href="/leave-management/my-requests" />
      <SummaryStat icon="check" tone="green" label="Approved Requests" value={requests.isLoading ? '…' : approved.length} hint="Approved requests" href="/leave-management/my-requests" />
      <SummaryStat icon="users" tone="teal" label="On leave today" value={calendar.isLoading ? '…' : calendar.error ? '—' : onLeaveToday.length} hint="Authorized team view" href="/leave-management/team-calendar" />
    </div>
    <div className="leave-dashboard-grid">
      <BalanceCard query={balances} />
      <RequestListCard title="Upcoming Leave" subtitle="Your next approved absences" items={upcoming} loading={requests.isLoading} error={requests.error?.message} empty="No upcoming approved leave" emptyMessage="You have no upcoming approved leave requests." />
      <RequestListCard title="Recent Status" subtitle="Latest completed requests" items={recent} loading={requests.isLoading} error={requests.error?.message} empty="No completed leave requests yet" emptyMessage="Your completed leave requests will appear here once available." />
      <TeamCard events={teamEvents} onLeaveToday={onLeaveToday.length} loading={calendar.isLoading} error={calendar.error?.message} />
      {canApprove && <ApprovalCard items={approvals.data?.items ?? []} loading={approvals.isLoading} error={approvals.error?.message} />}
      {canViewHrDashboard && <HrDashboardCard query={hrDashboard} />}
    </div>
  </div>
}

function HrDashboardCard({ query }: { query: { data: HrLeaveDashboardSummary | null; error: { message: string } | null; isLoading: boolean } }) {
  const data = query.data
  return <Card className="leave-dashboard-admin-card" title="HR / Admin overview" subtitle={data ? `${data.from} → ${data.to}${data.currentLeavePeriodName ? ` · ${data.currentLeavePeriodName}` : ''}` : 'Tenant-wide Leave operations'} actions={<Link className="row-action" to="/leave-management/team-calendar">Open calendar</Link>}>
    {query.isLoading ? <DashboardSkeleton label="Loading HR Leave dashboard" /> : query.error ? <WidgetError message={query.error.message} /> : !data ? <DashboardEmpty icon="chart" title="No HR dashboard data" /> : <>
      <div className="leave-dashboard-stat-grid"><SummaryStat icon="users" tone="teal" label="On Leave today" value={`${data.kpis.employeesOnLeaveToday} / ${data.kpis.activeEmployeeCount}`} hint="Active employees" href="/leave-management/team-calendar" /><SummaryStat icon="clock" tone="amber" label="Pending approvals" value={data.kpis.pendingApprovalRequests} hint="Tenant-wide" href="/leave-management/approvals" /><SummaryStat icon="calendar" tone="blue" label="Upcoming Leave" value={data.kpis.upcomingApprovedRequests} hint="Next 30 days" href="/leave-management/team-calendar" /><SummaryStat icon="check" tone="green" label="Approved in scope" value={data.kpis.approvedRequests} hint="Approved requests" href="/leave-management/team-calendar" /></div>
      <div className="leave-dashboard-admin-lists"><DashboardList title="Leave type usage" rows={data.leaveTypeUsage.map(item => `${item.name}: ${formatQuantity(item.quantity)} day(s) · ${item.requestCount} request(s)`)} empty="No approved Leave usage in this period." /><DashboardList title="Request status" rows={data.statusBreakdown.map(item => `${item.status}: ${item.requestCount} · ${formatQuantity(item.quantity)} day(s)`)} empty="No requests in this period." /><DashboardList title="Approval aging" rows={data.approvalAging.map(item => `${item.bucket}: ${item.requestCount}`)} empty="No pending approvals." /><DashboardList title="Upcoming absences" rows={data.upcomingAbsences.slice(0, 5).map(item => `${item.employeeName || item.employeeCode} · ${item.leaveTypeName} · ${item.startDate} → ${item.endDate}`)} empty="No upcoming approved Leave." /></div>
    </>}
  </Card>
}

function DashboardList({ title, rows, empty }: { title: string; rows: string[]; empty: string }) {
  return <section className="leave-dashboard-admin-list"><h3>{title}</h3>{rows.length ? <ul className="leave-dashboard-list">{rows.map(row => <li key={row}><span>{row}</span></li>)}</ul> : <p className="muted">{empty}</p>}</section>
}

function QuickActions({ canApprove, canConfigure }: { canApprove: boolean; canConfigure: boolean }) {
  const actions = [{ to: '/leave-management/apply', label: 'Apply Leave', icon: 'plus', primary: true }, { to: '/leave-management/my-requests', label: 'My Leave Requests', icon: 'clipboard' }, { to: '/leave-management/team-calendar', label: 'Team Leave Calendar', icon: 'calendar' }, { to: '/leave-management/approvals', label: 'Manager Approvals', show: canApprove, icon: 'check' }, { to: '/leave-management/policies', label: 'Leave Policy', show: canConfigure, icon: 'layers' }].filter(action => action.show !== false)
  return <nav className="leave-dashboard-actions" aria-label="Leave quick actions">{actions.map(action => <Link className={action.primary ? 'button button-primary' : 'button button-secondary'} to={action.to} key={action.to}><Icon name={action.icon as IconName} />{action.label}{!action.primary && <span className="action-arrow" aria-hidden="true">→</span>}</Link>)}</nav>
}

function SummaryStat({ icon, tone, label, value, hint, href }: { icon: IconName; tone: string; label: string; value: string | number; hint: string; href: string }) {
  return <Link className="leave-dashboard-stat" to={href}><span className={`leave-dashboard-stat-icon is-${tone}`}><Icon name={icon} /></span><span className="leave-dashboard-stat-label">{label}</span><strong>{value}</strong><span className="muted">{hint}</span></Link>
}

function BalanceCard({ query }: { query: { data: LeaveBalanceSummary[] | null; error: { message: string } | null; isLoading: boolean } }) {
  return <Card className="leave-dashboard-balance-card" title={<span className="card-title-with-icon"><span className="card-icon is-blue"><Icon name="layers" /></span>Leave Balances</span>} subtitle="Your current entitlement summary">{query.isLoading ? <DashboardSkeleton label="Loading Leave balances" /> : query.error ? <WidgetError message={query.error.message} /> : query.data?.length ? <div className="leave-balance-list">{query.data.map((balance, index) => <BalanceRow balance={balance} key={`${balance.leaveTypeCode}-${balance.leavePeriodName ?? 'unlimited'}-${index}`} />)}</div> : <DashboardEmpty icon="wallet" title="No balance allocations yet" message="No current finite or Unlimited Leave entitlement is available for your employee record." />}</Card>
}

function BalanceRow({ balance }: { balance: LeaveBalanceSummary }) {
  if (balance.entitlementMode === 'Unlimited') return <div className="leave-balance-item"><div className="balance-heading"><span className="balance-dot is-teal" /><div><strong>{balance.leaveTypeName}</strong><span className="muted">{balance.leaveTypeCode}{balance.leavePeriodName ? ` · ${balance.leavePeriodName}` : ''}</span></div><Badge tone="info">Unlimited</Badge></div></div>
  const granted = balance.grantedQuantity ?? 0
  const available = balance.availableQuantity ?? 0
  const percent = granted > 0 ? Math.min(100, Math.max(0, available / granted * 100)) : 0
  return <div className="leave-balance-item"><div className="balance-heading"><span className="balance-dot is-blue" /><div><strong>{balance.leaveTypeName}</strong><span className="muted">{balance.leaveTypeCode}{balance.leavePeriodName ? ` · ${balance.leavePeriodName}` : ''}</span></div><strong className="balance-available">{formatQuantity(available)} available</strong></div><div className="balance-progress" aria-label={`${formatQuantity(available)} of ${formatQuantity(granted)} days available`}><span style={{ width: `${percent}%` }} /></div><div className="balance-meta"><span>{formatQuantity(available)} / {formatQuantity(granted)} days available</span><span>Used {formatQuantity(balance.consumedQuantity ?? 0)} · Reserved {formatQuantity(balance.reservedQuantity ?? 0)}</span></div></div>
}

function RequestListCard({ title, subtitle, items, loading, error, empty, emptyMessage }: { title: string; subtitle: string; items: LeaveRequestListItem[]; loading: boolean; error?: string; empty: string; emptyMessage: string }) {
  return <Card title={<span className="card-title-with-icon"><span className="card-icon is-blue"><Icon name={title === 'Recent Status' ? 'clipboard' : 'calendar'} /></span>{title}</span>} subtitle={subtitle} actions={<Link className="row-action" to="/leave-management/my-requests">View all</Link>}>{loading ? <DashboardSkeleton label={`Loading ${title}`} /> : error ? <WidgetError message={error} /> : items.length === 0 ? <DashboardEmpty icon={title === 'Recent Status' ? 'clipboard' : 'calendar'} title={empty} message={emptyMessage} /> : <ul className="leave-dashboard-list">{items.map(item => <li key={item.requestId}><div><strong>{item.leaveTypeName}</strong><span className="muted">{item.startDate} → {item.endDate} · {formatQuantity(item.chargeableQuantity)} day(s)</span></div><Badge tone={statusTone(item.status)}>{statusLabels[item.status]}</Badge></li>)}</ul>}</Card>
}

function ApprovalCard({ items, loading, error }: { items: LeaveApprovalListItem[]; loading: boolean; error?: string }) {
  return <Card title={<span className="card-title-with-icon"><span className="card-icon is-amber"><Icon name="clock" /></span>Manager approvals</span>} subtitle="Requests assigned to you" actions={<Link className="row-action" to="/leave-management/approvals">Open approvals</Link>}>{loading ? <DashboardSkeleton label="Loading manager approvals" /> : error ? <WidgetError message={error} /> : items.length === 0 ? <DashboardEmpty icon="check" title="No approvals waiting" message="Your approval inbox is clear." /> : <ul className="leave-dashboard-list">{items.map(item => <li key={item.requestId}><div><strong>{item.employeeName}</strong><span className="muted">{item.leaveTypeName} · {item.startDate} → {item.endDate}</span></div><Badge tone="warning">Pending</Badge></li>)}</ul>}</Card>
}

function TeamCard({ events, onLeaveToday, loading, error }: { events: LeaveCalendarEvent[]; onLeaveToday: number; loading: boolean; error?: string }) {
  return <Card title={<span className="card-title-with-icon"><span className="card-icon is-teal"><Icon name="users" /></span>Team Availability</span>} subtitle="Authorized team leave in the next 30 days" actions={<Link className="row-action" to="/leave-management/team-calendar">Open calendar</Link>}>{loading ? <DashboardSkeleton label="Loading team availability" /> : error ? <WidgetError message={error} /> : <div className="team-availability"><div className="team-availability-summary"><div><span className="eyebrow">On leave today</span><strong>{onLeaveToday}</strong><span className="muted">authorized team members</span></div><div className="team-calendar-mini"><span className="mini-calendar-month">{new Date().toLocaleDateString(undefined, { month: 'short' })}</span><span className="mini-calendar-day">{new Date().getDate()}</span><span className="mini-calendar-label">Today</span></div></div>{events.length === 0 ? <DashboardEmpty icon="calendar" title="No team members are on leave today" message="Your authorized team calendar is clear for the next 30 days." /> : <ul className="leave-dashboard-list">{events.slice(0, 3).map(event => <li key={event.requestId}><div><strong>{event.employeeName || event.employeeCode}</strong><span className="muted">{event.leaveTypeName} · {event.startDate} → {event.endDate}</span></div><Badge tone={event.status === 'Approved' ? 'success' : 'warning'}>{event.status === 'Approved' ? 'Approved' : 'Pending'}</Badge></li>)}</ul>}</div>}</Card>
}

function DashboardSkeleton({ label }: { label: string }) { return <div className="dashboard-skeleton" aria-label={label}><span /><span /><span /></div> }
function WidgetError({ message }: { message: string }) { return <div className="dashboard-widget-error"><span>{message || 'Unable to load this section.'}</span><button type="button" className="button button-link" onClick={() => window.location.reload()}>Retry</button></div> }
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
    wallet: <><path d="M4 7h16v12H4zM4 7l2-3h12l2 3" /><path d="M16 13h4" /></>,
  }
  return <svg className="dashboard-icon" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8">{paths[name]}</svg>
}
