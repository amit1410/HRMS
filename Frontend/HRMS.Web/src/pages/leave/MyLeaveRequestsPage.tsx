import { useRef, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { cancelLeaveRequest, getMyLeaveRequest, listMyLeaveRequests, withdrawLeaveRequest, type LeaveRequestDetail, type LeaveRequestListItem, type LeaveRequestStatus } from '../../api/leaveRequests.ts'
import { ApiError } from '../../api/errors.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Badge, type BadgeTone } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { formatDate, formatDateTime as formatListDateTime } from '../../lib/format.ts'

const statusLabels: Record<LeaveRequestStatus, string> = {
  PendingApproval: 'Pending Approval',
  Approved: 'Approved',
  Rejected: 'Rejected',
  Withdrawn: 'Withdrawn',
  Cancelled: 'Cancelled',
}

export function MyLeaveRequestsPage() {
  const query = useApiQuery(() => listMyLeaveRequests(), [])
  const [filter, setFilter] = useState<'all' | 'pending' | 'approved' | 'rejected' | 'closed'>('all')
  const { can } = useAuth()

  if (query.isLoading) return <MyRequestsShell><Card className="my-requests-results-card"><RequestSkeleton /></Card></MyRequestsShell>
  if (query.error) return <MyRequestsShell><Card className="my-requests-error-card"><div className="my-requests-error-icon"><RequestIcon name="alert" /></div><h2>Unable to load leave requests.</h2><p>{query.error.message === 'Unable to load leave requests.' ? 'The request history could not be loaded.' : query.error.message}</p><button type="button" className="button button-secondary" onClick={query.refetch}>Retry</button></Card></MyRequestsShell>

  const items = (query.data?.items ?? []).filter(item => filter === 'all' || filter === 'pending' && item.status === 'PendingApproval' || filter === 'approved' && item.status === 'Approved' || filter === 'rejected' && item.status === 'Rejected' || filter === 'closed' && (item.status === 'Withdrawn' || item.status === 'Cancelled'))
  return <MyRequestsShell>
    <Card className="my-requests-filter-card"><div className="my-requests-filter-heading"><div><span className="eyebrow">Request history</span><h2>Filter requests</h2><p>Use the status filter to narrow the submitted requests currently loaded.</p></div>{filter !== 'all' ? <button type="button" className="button button-link my-requests-reset" onClick={() => setFilter('all')}><RequestIcon name="refresh" />Reset filters</button> : null}</div><div className="my-requests-filter-row"><label className="field"><span>Request status</span><select aria-label="Filter requests" className="input" value={filter} onChange={event => setFilter(event.target.value as typeof filter)}><option value="all">All requests</option><option value="pending">Pending approval</option><option value="approved">Approved</option><option value="rejected">Rejected</option><option value="closed">Withdrawn or cancelled</option></select></label><div className="my-requests-filter-note"><RequestIcon name="info" /><span>Date range and Leave Type filters are not exposed by the current requests endpoint.</span></div></div></Card>
    {items.length === 0 ? <Card className="my-requests-empty-card"><div className="my-requests-empty-icon"><RequestIcon name="clipboard" /></div><EmptyState title={filter === 'all' ? 'You have no leave requests yet.' : 'No leave requests match this filter.'} message={filter === 'all' ? 'Your submitted leave requests will appear here once you apply for leave.' : 'Try another status filter to view your request history.'} action={filter === 'all' ? <Link aria-label="Apply for Leave" className="button button-primary" to="/leave-management/apply">+ Apply for Leave</Link> : undefined} /></Card> : <Card className="my-requests-results-card" title="Submitted requests" subtitle={`${query.data?.totalCount ?? items.length} request${(query.data?.totalCount ?? items.length) === 1 ? '' : 's'} in your history`}><LeaveRequestTable items={items} /></Card>}
    <Card className="my-requests-help-card"><div className="my-requests-help-copy"><div className="my-requests-help-icon"><RequestIcon name="info" /></div><div><h2>Need help?</h2><p>For any leave related queries, please contact your HR team or refer to the Leave Policy.</p></div></div>{can(Permissions.leave.policyView) ? <Link className="button button-secondary" to="/leave-management/policies">View Leave Policy <span aria-hidden="true">→</span></Link> : null}</Card>
  </MyRequestsShell>
}

function MyRequestsShell({ children }: { children: ReactNode }) {
  return <div className="leave-admin-page my-requests-page"><div className="my-requests-breadcrumb"><Link to="/">Home</Link><span aria-hidden="true">/</span><Link to="/leave-management">Leave</Link><span aria-hidden="true">/</span><strong>My Leave Requests</strong></div><PageHeader title="My Leave Requests" subtitle="View and track your submitted leave requests" actions={<Link className="button button-primary" to="/leave-management/apply">+ Apply for Leave</Link>} />{children}</div>
}

function LeaveRequestTable({ items }: { items: LeaveRequestListItem[] }) {
  return <div className="table-wrap my-requests-table-wrap"><table className="data-table my-requests-table"><caption className="sr-only">My Leave Requests</caption><thead><tr><th>Request</th><th>Leave Type</th><th>Date range</th><th>Duration</th><th>Chargeable</th><th>Status</th><th>Submitted on</th><th>Actions</th></tr></thead><tbody>{items.map(item => <tr key={item.requestId}><td data-label="Request"><span className="my-requests-id">{item.requestId.slice(0, 8).toUpperCase()}</span></td><td data-label="Leave Type"><strong>{item.leaveTypeName}</strong><span className="my-requests-code">{item.leaveTypeCode}</span></td><td data-label="Date range"><strong>{formatDate(item.startDate)} - {formatDate(item.endDate)}</strong><span className="my-requests-iso">{item.startDate} → {item.endDate}</span></td><td data-label="Duration"><Quantity value={item.requestedQuantity} /></td><td data-label="Chargeable"><Quantity value={item.chargeableQuantity} /></td><td data-label="Status"><StatusBadge status={item.status} /></td><td data-label="Submitted on">{formatListDateTime(item.submittedAtUtc)}</td><td data-label="Actions"><Link className="row-action" to={`/leave-management/my-requests/${item.requestId}`}>View Details</Link></td></tr>)}</tbody></table></div>
}

function Quantity({ value }: { value: number }) { return <span className="request-quantity"><strong>{value}</strong><small>day{value === 1 ? '' : 's'}</small></span> }
function StatusBadge({ status }: { status: LeaveRequestStatus }) { return <Badge tone={statusTone(status)}>{statusLabels[status]}</Badge> }
function statusTone(status: LeaveRequestStatus): BadgeTone { return status === 'Approved' ? 'success' : status === 'PendingApproval' ? 'warning' : status === 'Rejected' ? 'danger' : 'neutral' }
function RequestSkeleton() { return <div className="my-requests-skeleton" aria-label="Loading My Leave Requests"><span /><span /><span /><span /></div> }
function MyRequestsIcon({ name }: { name: 'alert' | 'clipboard' | 'info' | 'refresh' }) {
  const paths = { alert: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5M12 16h.01" /></>, clipboard: <><rect x="5" y="4" width="14" height="17" rx="2" /><path d="M9 4V3h6v1M8 9h8M8 13h6" /></>, info: <><circle cx="12" cy="12" r="9" /><path d="M12 11v5M12 8h.01" /></>, refresh: <><path d="M20 11a8 8 0 0 0-14.8-3L3 11M4 5v6h6M4 13a8 8 0 0 0 14.8 3L21 13M20 19v-6h-6" /></> }[name]
  return <svg className="my-requests-icon" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8">{paths}</svg>
}
const RequestIcon = MyRequestsIcon

export function MyLeaveRequestDetailPage() {
  const { requestId = '' } = useParams()
  const query = useApiQuery(() => getMyLeaveRequest(requestId), [requestId])
  const [action, setAction] = useState<'withdraw' | 'cancel' | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const actionBusyRef = useRef(false)

  if (query.isLoading) return <div className="leave-admin-page"><p className="state-block"><Spinner label="Loading Leave Request" /></p></div>
  if (query.error || !query.data) return <div className="leave-admin-page"><PageHeader title="Leave Request Details" /><Notice tone="error">{detailErrorMessage(query.error)}</Notice><Link className="button button-secondary" to="/leave-management/my-requests">Back to My Leave Requests</Link></div>

  const detail = query.data
  const canWithdraw = detail.status === 'PendingApproval'
  const canCancel = detail.status === 'Approved'

  async function withdraw() {
    if (action !== null || actionBusyRef.current || !window.confirm('Are you sure you want to withdraw this leave request?')) return
    actionBusyRef.current = true
    setAction('withdraw')
    setActionError(null)
    setSuccess(null)
    try {
      await withdrawLeaveRequest(requestId)
      setSuccess('Leave request withdrawn successfully.')
      query.refetch()
    } catch (error) {
      setActionError(withdrawErrorMessage(error))
      if (error instanceof ApiError && error.status === 409) query.refetch()
    } finally {
      actionBusyRef.current = false
      setAction(null)
    }
  }

  async function cancel() {
    if (action !== null || actionBusyRef.current || !window.confirm('Are you sure you want to cancel this approved leave request?')) return
    actionBusyRef.current = true
    setAction('cancel')
    setActionError(null)
    setSuccess(null)
    try {
      await cancelLeaveRequest(requestId)
      setSuccess('Leave request cancelled successfully.')
      query.refetch()
    } catch (error) {
      setActionError(cancelErrorMessage(error))
      if (error instanceof ApiError && error.status === 409) query.refetch()
    } finally {
      actionBusyRef.current = false
      setAction(null)
    }
  }

  return <div className="leave-admin-page">
    <PageHeader title="Leave Request Details" actions={<Link className="button button-secondary" to="/leave-management/my-requests">Back to My Leave Requests</Link>} />
    {success && <Notice tone="success">{success}</Notice>}
    {actionError && <Notice tone="error">{actionError}</Notice>}
    {canCancel && <div className="page-actions"><button type="button" className="button button-primary" disabled={action !== null} onClick={() => void cancel()}>{action === 'cancel' ? 'Cancelling…' : 'Cancel'}</button></div>}
    <RequestSummary detail={detail} />
    <Card title="Request Days"><div className="table-wrap"><table className="data-table"><caption className="sr-only">Request Days</caption><thead><tr><th>Date</th><th>Requested</th><th>Chargeable</th><th>Classification</th><th>Reason</th></tr></thead><tbody>{detail.requestDays.map(day => <tr key={day.date}><td>{day.date}</td><td>{day.requestedQuantity}</td><td>{day.chargeableQuantity}</td><td>{day.dayClassification ?? '—'}</td><td>{day.calculationReason ?? '—'}</td></tr>)}</tbody></table></div></Card>
    <Card title="History"><ul>{detail.events.map((event, index) => <li key={`${event.occurredAtUtc}-${index}`}>{event.eventType} — {formatDateTime(event.occurredAtUtc)}</li>)}</ul></Card>
    {canWithdraw && <div className="page-actions"><button type="button" className="button button-primary" disabled={action !== null} onClick={() => void withdraw()}>{action === 'withdraw' ? 'Withdrawing…' : 'Withdraw'}</button></div>}
  </div>
}

function RequestSummary({ detail }: { detail: LeaveRequestDetail }) {
  return <Card title={detail.leaveTypeName}><dl className="detail-list"><div><dt>Status</dt><dd>{statusLabels[detail.status]}</dd></div><div><dt>From</dt><dd>{detail.startDate}</dd></div><div><dt>To</dt><dd>{detail.endDate}</dd></div><div><dt>Requested Quantity</dt><dd>{detail.requestedQuantity}</dd></div><div><dt>Chargeable Quantity</dt><dd>{detail.chargeableQuantity}</dd></div><div><dt>Submitted</dt><dd>{formatDateTime(detail.submittedAtUtc)}</dd></div><div><dt>Leave Period</dt><dd>{detail.leavePeriodName} ({detail.leavePeriodCode})</dd></div></dl></Card>
}

function detailErrorMessage(error: ApiError | null): string {
  if (error?.status === 404) return 'This leave request is no longer available.'
  return error?.message ?? 'This leave request is no longer available.'
}

function withdrawErrorMessage(error: unknown): string {
  if (!(error instanceof ApiError)) return 'The leave request could not be withdrawn. Please try again.'
  if (error.status === 404) return 'This leave request is no longer available.'
  if (error.status === 409 && /InvalidStatusTransition/i.test(error.message)) return 'This leave request has already been processed and can no longer be withdrawn.'
  if (error.status === 409 && /ConcurrencyConflict/i.test(error.message)) return 'The request changed while you were reviewing it. Refresh and try again.'
  if (error.status === 409 && /AllocatedReservationNotFound/i.test(error.message)) return 'This leave request does not have an authoritative reserved balance and cannot be withdrawn. Please contact HR.'
  return error.message
}

function cancelErrorMessage(error: unknown): string {
  if (!(error instanceof ApiError)) return 'The leave request could not be cancelled. Please try again.'
  if (error.status === 404) return 'This leave request is no longer available.'
  if (error.status === 409 && /CancellationNotAllowed/i.test(error.message)) return 'This leave request cannot be cancelled under its leave policy.'
  if (error.status === 409 && /AllocatedConsumptionNotFound/i.test(error.message)) return 'This approved leave request does not have authoritative consumed-balance history and cannot be cancelled. Please contact HR.'
  if (error.status === 409 && /InvalidStatusTransition/i.test(error.message)) return 'This leave request has already been processed and can no longer be cancelled.'
  if (error.status === 409 && /ConcurrencyConflict/i.test(error.message)) return 'The request changed while you were reviewing it. Refresh and try again.'
  return error.message
}

function formatDateTime(value?: string | null): string { return value ? new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '—' }
