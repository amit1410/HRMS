import { useState } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { approveLeaveRequest, getLeaveApproval, listLeaveApprovals, rejectLeaveRequest, type LeaveApprovalListItem, type LeaveRequestStatus } from '../../api/leaveRequests.ts'
import { ApiError } from '../../api/errors.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

const statusLabels: Record<LeaveRequestStatus, string> = {
  PendingApproval: 'Pending Approval',
  Approved: 'Approved',
  Rejected: 'Rejected',
  Withdrawn: 'Withdrawn',
  Cancelled: 'Cancelled',
}

export function LeaveApprovalsPage() {
  const [page, setPage] = useState(1)
  const location = useLocation()
  const query = useApiQuery(() => listLeaveApprovals(page, 25), [page])
  const message = (location.state as { message?: string } | null)?.message

  if (query.isLoading) return <div className="leave-admin-page"><p className="state-block"><Spinner label="Loading Leave Approvals" /></p></div>
  if (query.error) return <div className="leave-admin-page"><PageHeader title="Leave Approvals" /><Notice tone="error">{approvalErrorMessage(query.error)}</Notice><button type="button" className="button button-secondary" onClick={query.refetch}>Retry</button></div>

  const data = query.data
  const items = data?.items ?? []
  return <div className="leave-admin-page">
    <PageHeader title="Leave Approvals" subtitle="Pending leave requests assigned to you" />
    {message && <Notice tone="success">{message}</Notice>}
    {items.length === 0 ? <Card><p className="muted">No pending leave requests require your approval.</p></Card> : <ApprovalTable items={items} />}
    {data && data.totalPages > 1 && <Paging page={data.page} totalPages={data.totalPages} hasPreviousPage={data.hasPreviousPage} hasNextPage={data.hasNextPage} onPageChange={setPage} />}
  </div>
}

function ApprovalTable({ items }: { items: LeaveApprovalListItem[] }) {
  return <div className="table-wrap"><table className="data-table"><caption className="sr-only">Leave Approvals</caption><thead><tr><th>Employee</th><th>Leave Type</th><th>From</th><th>To</th><th>Requested</th><th>Chargeable</th><th>Status</th><th>Submitted</th><th>Action</th></tr></thead><tbody>{items.map(item => <tr key={item.requestId}><td><strong>{item.employeeName}</strong><br /><span className="muted">{item.employeeCode}</span></td><td>{item.leaveTypeName}</td><td>{item.startDate}</td><td>{item.endDate}</td><td>{item.requestedQuantity}</td><td>{item.chargeableQuantity}</td><td>{statusLabels[item.status]}</td><td>{formatDateTime(item.submittedAtUtc)}</td><td><Link className="row-action" to={`/leave-management/approvals/${item.requestId}`}>View</Link></td></tr>)}</tbody></table></div>
}

function Paging({ page, totalPages, hasPreviousPage, hasNextPage, onPageChange }: { page: number; totalPages: number; hasPreviousPage: boolean; hasNextPage: boolean; onPageChange: (page: number) => void }) {
  return <nav aria-label="Approval pages" className="page-actions"><button type="button" className="button button-secondary" disabled={!hasPreviousPage} onClick={() => onPageChange(page - 1)}>Previous</button><span>Page {page} of {totalPages}</span><button type="button" className="button button-secondary" disabled={!hasNextPage} onClick={() => onPageChange(page + 1)}>Next</button></nav>
}

export function LeaveApprovalDetailPage() {
  const { requestId = '' } = useParams()
  const navigate = useNavigate()
  const { can } = useAuth()
  const query = useApiQuery(() => getLeaveApproval(requestId), [requestId])
  const [action, setAction] = useState<'approve' | 'reject' | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  if (query.isLoading) return <div className="leave-admin-page"><p className="state-block"><Spinner label="Loading Leave Approval" /></p></div>
  if (query.error || !query.data) return <div className="leave-admin-page"><PageHeader title="Leave Approval" /><Notice tone="error">{approvalErrorMessage(query.error)}</Notice><Link className="button button-secondary" to="/leave-management/approvals">Back to Approvals</Link></div>

  const detail = query.data
  const actionable = detail.status === 'PendingApproval' && can('Leave.Approve')
  async function transition(kind: 'approve' | 'reject') {
    if (!window.confirm(kind === 'approve' ? 'Approve this leave request?' : 'Reject this leave request?')) return
    setAction(kind)
    setActionError(null)
    try {
      await (kind === 'approve' ? approveLeaveRequest(requestId) : rejectLeaveRequest(requestId))
      navigate('/leave-management/approvals', { replace: true, state: { message: kind === 'approve' ? 'Leave request approved successfully.' : 'Leave request rejected successfully.' } })
    } catch (error) {
      setActionError(transitionErrorMessage(error))
      setAction(null)
    }
  }

  return <div className="leave-admin-page">
    <PageHeader title="Leave Approval Details" actions={<Link className="button button-secondary" to="/leave-management/approvals">Back to Approvals</Link>} />
    {actionError && <Notice tone="error">{actionError}</Notice>}
    <Card title="Employee"><dl className="detail-list"><div><dt>Name</dt><dd>{detail.employeeName}</dd></div><div><dt>Employee Code</dt><dd>{detail.employeeCode}</dd></div></dl></Card>
    <Card title={detail.leaveTypeName}><dl className="detail-list"><div><dt>Status</dt><dd>{statusLabels[detail.status]}</dd></div><div><dt>From</dt><dd>{detail.startDate}</dd></div><div><dt>To</dt><dd>{detail.endDate}</dd></div><div><dt>Requested Quantity</dt><dd>{detail.requestedQuantity}</dd></div><div><dt>Chargeable Quantity</dt><dd>{detail.chargeableQuantity}</dd></div><div><dt>Submitted</dt><dd>{formatDateTime(detail.submittedAtUtc)}</dd></div><div><dt>Leave Period</dt><dd>{detail.leavePeriodName} ({detail.leavePeriodCode})</dd></div></dl></Card>
    <Card title="Request Days"><div className="table-wrap"><table className="data-table"><caption className="sr-only">Request Days</caption><thead><tr><th>Date</th><th>Requested</th><th>Chargeable</th><th>Classification</th><th>Reason</th></tr></thead><tbody>{detail.requestDays.map(day => <tr key={day.date}><td>{day.date}</td><td>{day.requestedQuantity}</td><td>{day.chargeableQuantity}</td><td>{day.dayClassification ?? '—'}</td><td>{day.calculationReason ?? '—'}</td></tr>)}</tbody></table></div></Card>
    <Card title="History"><ul>{detail.events.map((event, index) => <li key={`${event.occurredAtUtc}-${index}`}>{event.eventType} — {formatDateTime(event.occurredAtUtc)}</li>)}</ul></Card>
    {actionable && <div className="page-actions"><button type="button" className="button button-primary" disabled={action !== null} onClick={() => void transition('approve')}>{action === 'approve' ? 'Approving…' : 'Approve'}</button><button type="button" className="button button-secondary" disabled={action !== null} onClick={() => void transition('reject')}>{action === 'reject' ? 'Rejecting…' : 'Reject'}</button></div>}
  </div>
}

function approvalErrorMessage(error: ApiError | null): string {
  if (!error) return 'This leave request is no longer available for approval.'
  if (error.status === 403) return 'You are not authorized to approve this request.'
  if (error.status === 404) return 'This leave request is no longer available for approval.'
  return error.message
}

function transitionErrorMessage(error: unknown): string {
  if (!(error instanceof ApiError)) return 'The request could not be processed. Please try again.'
  if (error.status === 403) return 'You are not authorized to approve this request.'
  if (error.status === 404) return 'This leave request is no longer available for approval.'
  if (error.status === 409 && /InvalidStatusTransition/i.test(error.message)) return 'This leave request has already been processed or is no longer pending.'
  if (error.status === 409 && /ConcurrencyConflict/i.test(error.message)) return 'The request changed while you were reviewing it. Refresh and try again.'
  if (error.status === 409 && /AllocatedReservationNotFound/i.test(error.message)) return 'This leave request does not have an authoritative reserved balance and cannot be processed. Please contact HR.'
  return error.message
}

function formatDateTime(value?: string | null): string { return value ? new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '—' }
