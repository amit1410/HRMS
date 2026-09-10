import { Link } from 'react-router-dom'
import { listMyLeaveBalances, type LeaveBalanceSummary } from '../../api/leaveBalances.ts'
import { listLeaveApprovals, listMyLeaveRequests, type LeaveApprovalListItem, type LeaveRequestListItem, type LeaveRequestStatus } from '../../api/leaveRequests.ts'
import { listLeaveCalendar, type LeaveCalendarEvent } from '../../api/leaveCalendar.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Badge, type BadgeTone } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
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
  const today = dateValue(new Date())
  const calendarTo = dateValue(addDays(new Date(), 30))
  const balances = useApiQuery(signal => linked ? listMyLeaveBalances(signal) : Promise.resolve([]), [linked])
  const requests = useApiQuery(() => linked ? listMyLeaveRequests(1, 25) : Promise.resolve(emptyPage), [linked])
  const approvals = useApiQuery(() => canApprove ? listLeaveApprovals(1, 5) : Promise.resolve(emptyPage), [canApprove])
  const calendar = useApiQuery(signal => linked ? listLeaveCalendar(today, calendarTo, signal) : Promise.resolve([]), [linked, today, calendarTo])

  if (!linked) return <div className="leave-admin-page"><PageHeader title="Leave Dashboard" subtitle="Your Leave overview" /><Card><EmptyState title="Leave access needs an employee link" message="Your account is not linked to an active employee. Contact HR before applying for or viewing Leave." /></Card></div>

  const requestItems = requests.data?.items ?? []
  const upcoming = requestItems.filter(item => item.status === 'Approved' && item.endDate >= today).sort((a, b) => a.startDate.localeCompare(b.startDate)).slice(0, 5)
  const pending = requestItems.filter(item => item.status === 'PendingApproval')
  const recent = requestItems.filter(item => item.status !== 'PendingApproval' && item.status !== 'Approved').sort((a, b) => (b.submittedAtUtc ?? '').localeCompare(a.submittedAtUtc ?? '')).slice(0, 5)
  const teamEvents = (calendar.data ?? []).filter(event => event.startDate >= today).slice(0, 5)
  const failures = [balances.error, requests.error, canApprove ? approvals.error : null, calendar.error].filter(Boolean)

  return <div className="leave-admin-page leave-dashboard-page">
    <PageHeader title="Leave Dashboard" subtitle={`A quick overview for ${user?.employeeIdentity?.employee?.displayName ?? user?.fullName ?? 'you'}`} actions={<QuickActions canApprove={canApprove} canConfigure={can(Permissions.leave.policyView)} />} />
    {failures.length > 0 && <Notice tone="error">Some Leave summary sections could not be loaded. The available sections are still shown.</Notice>}
    <div className="leave-dashboard-stat-grid">
      <SummaryStat label="Pending requests" value={requests.isLoading ? '…' : pending.length} hint="Awaiting approval" href="/leave-management/my-requests" />
      {canApprove && <SummaryStat label="Approvals waiting" value={approvals.isLoading ? '…' : approvals.data?.totalCount ?? 0} hint="Assigned to you" href="/leave-management/approvals" />}
      <SummaryStat label="Upcoming Leave" value={requests.isLoading ? '…' : upcoming.length} hint="Approved requests" href="/leave-management/my-requests" />
    </div>
    <div className="leave-dashboard-grid">
      <BalanceCard query={balances} />
      <RequestListCard title="Upcoming Leave" subtitle="Your next approved absences" items={upcoming} loading={requests.isLoading} error={requests.error?.message} empty="No upcoming approved Leave." />
      <RequestListCard title="Recent status" subtitle="Latest completed requests" items={recent} loading={requests.isLoading} error={requests.error?.message} empty="No completed Leave requests yet." />
      {canApprove && <ApprovalCard items={approvals.data?.items ?? []} loading={approvals.isLoading} error={approvals.error?.message} />}
      <TeamCard events={teamEvents} loading={calendar.isLoading} error={calendar.error?.message} />
    </div>
  </div>
}

function QuickActions({ canApprove, canConfigure }: { canApprove: boolean; canConfigure: boolean }) {
  const actions = [{ to: '/leave-management/apply', label: 'Apply Leave' }, { to: '/leave-management/my-requests', label: 'My Leave Requests' }, { to: '/leave-management/team-calendar', label: 'Team Leave Calendar' }, { to: '/leave-management/approvals', label: 'Manager Approvals', show: canApprove }, { to: '/leave-management/policies', label: 'Leave Policy', show: canConfigure }].filter(action => action.show !== false)
  return <nav className="leave-dashboard-actions" aria-label="Leave quick actions">{actions.map(action => <Link className="button button-secondary" to={action.to} key={action.to}>{action.label} <span aria-hidden="true">→</span></Link>)}</nav>
}

function SummaryStat({ label, value, hint, href }: { label: string; value: string | number; hint: string; href: string }) {
  return <Link className="leave-dashboard-stat" to={href}><span className="leave-dashboard-stat-label">{label}</span><strong>{value}</strong><span className="muted">{hint}</span></Link>
}

function BalanceCard({ query }: { query: { data: LeaveBalanceSummary[] | null; error: { message: string } | null; isLoading: boolean } }) {
  return <Card title="Leave balances" subtitle="Current entitlement summary">{query.isLoading ? <div className="state-block"><Spinner label="Loading Leave balances" /></div> : query.error ? <Notice tone="error">{query.error.message}</Notice> : query.data?.length ? <div className="leave-balance-list">{query.data.map((balance, index) => <div className="leave-balance-item" key={`${balance.leaveTypeCode}-${balance.leavePeriodName ?? 'unlimited'}-${index}`}><div><strong>{balance.leaveTypeName}</strong><span className="muted">{balance.leaveTypeCode}{balance.leavePeriodName ? ` · ${balance.leavePeriodName}` : ''}</span></div>{balance.entitlementMode === 'Unlimited' ? <strong>Unlimited</strong> : <><strong>{formatQuantity(balance.availableQuantity ?? 0)} available</strong><dl><div><dt>Allocated</dt><dd>{formatQuantity(balance.grantedQuantity ?? 0)}</dd></div><div><dt>Reserved</dt><dd>{formatQuantity(balance.reservedQuantity ?? 0)}</dd></div><div><dt>Used</dt><dd>{formatQuantity(balance.consumedQuantity ?? 0)}</dd></div></dl></>}</div>)}</div> : <EmptyState title="No balance allocations" message="No current finite or Unlimited Leave entitlement is available for your employee record." />}</Card>
}

function RequestListCard({ title, subtitle, items, loading, error, empty }: { title: string; subtitle: string; items: LeaveRequestListItem[]; loading: boolean; error?: string; empty: string }) {
  return <Card title={title} subtitle={subtitle} actions={<Link className="row-action" to="/leave-management/my-requests">View all</Link>}>{loading ? <Spinner label={`Loading ${title}`} /> : error ? <Notice tone="error">{error}</Notice> : items.length === 0 ? <EmptyState title={empty} /> : <ul className="leave-dashboard-list">{items.map(item => <li key={item.requestId}><div><strong>{item.leaveTypeName}</strong><span className="muted">{item.startDate} → {item.endDate} · {formatQuantity(item.chargeableQuantity)} day(s)</span></div><Badge tone={statusTone(item.status)}>{statusLabels[item.status]}</Badge></li>)}</ul>}</Card>
}

function ApprovalCard({ items, loading, error }: { items: LeaveApprovalListItem[]; loading: boolean; error?: string }) {
  return <Card title="Manager approvals" subtitle="Requests assigned to you" actions={<Link className="row-action" to="/leave-management/approvals">Open approvals</Link>}>{loading ? <Spinner label="Loading manager approvals" /> : error ? <Notice tone="error">{error}</Notice> : items.length === 0 ? <EmptyState title="No approvals waiting" message="Your approval inbox is clear." /> : <ul className="leave-dashboard-list">{items.map(item => <li key={item.requestId}><div><strong>{item.employeeName}</strong><span className="muted">{item.leaveTypeName} · {item.startDate} → {item.endDate}</span></div><Badge tone="warning">Pending</Badge></li>)}</ul>}</Card>
}

function TeamCard({ events, loading, error }: { events: LeaveCalendarEvent[]; loading: boolean; error?: string }) {
  return <Card title="Team availability" subtitle="Authorized team Leave in the next 30 days" actions={<Link className="row-action" to="/leave-management/team-calendar">Open calendar</Link>}>{loading ? <Spinner label="Loading team availability" /> : error ? <Notice tone="error">{error}</Notice> : events.length === 0 ? <EmptyState title="No team Leave coming up" /> : <ul className="leave-dashboard-list">{events.map(event => <li key={event.requestId}><div><strong>{event.employeeName || event.employeeCode}</strong><span className="muted">{event.leaveTypeName} · {event.startDate} → {event.endDate}</span></div><Badge tone={event.status === 'Approved' ? 'success' : 'warning'}>{event.status === 'Approved' ? 'Approved' : 'Pending'}</Badge></li>)}</ul>}</Card>
}
