import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { cancelLeaveRequest, getMyLeaveRequest, listMyLeaveRequests, withdrawLeaveRequest, type LeaveRequestDetail, type LeaveRequestListItem, type LeaveRequestStatus } from '../../api/leaveRequests.ts'
import { ApiError } from '../../api/errors.ts'
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

export function MyLeaveRequestsPage() {
  const query = useApiQuery(() => listMyLeaveRequests(), [])

  if (query.isLoading) return <div className="leave-admin-page"><p className="state-block"><Spinner label="Loading My Leave Requests" /></p></div>
  if (query.error) return <div className="leave-admin-page"><PageHeader title="My Leave Requests" /><Notice tone="error">{query.error.message}</Notice><button type="button" className="button button-secondary" onClick={query.refetch}>Retry</button></div>

  const items = query.data?.items ?? []
  return <div className="leave-admin-page">
    <PageHeader title="My Leave Requests" subtitle="Your submitted leave requests" />
    {items.length === 0 ? <Card><p className="muted">You have no leave requests yet.</p></Card> : <LeaveRequestTable items={items} />}
  </div>
}

function LeaveRequestTable({ items }: { items: LeaveRequestListItem[] }) {
  return <div className="table-wrap"><table className="data-table"><caption className="sr-only">My Leave Requests</caption><thead><tr><th>Leave Type</th><th>From</th><th>To</th><th>Requested</th><th>Chargeable</th><th>Status</th><th>Submitted</th><th>Action</th></tr></thead><tbody>{items.map(item => <tr key={item.requestId}><td>{item.leaveTypeName}</td><td>{item.startDate}</td><td>{item.endDate}</td><td>{item.requestedQuantity}</td><td>{item.chargeableQuantity}</td><td>{statusLabels[item.status]}</td><td>{formatDateTime(item.submittedAtUtc)}</td><td><Link className="row-action" to={`/leave-management/my-requests/${item.requestId}`}>View Details</Link></td></tr>)}</tbody></table></div>
}

export function MyLeaveRequestDetailPage() {
  const { requestId = '' } = useParams()
  const query = useApiQuery(() => getMyLeaveRequest(requestId), [requestId])
  const [action, setAction] = useState<'withdraw' | 'cancel' | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [success, setSuccess] = useState<string | null>(null)

  if (query.isLoading) return <div className="leave-admin-page"><p className="state-block"><Spinner label="Loading Leave Request" /></p></div>
  if (query.error || !query.data) return <div className="leave-admin-page"><PageHeader title="Leave Request Details" /><Notice tone="error">{detailErrorMessage(query.error)}</Notice><Link className="button button-secondary" to="/leave-management/my-requests">Back to My Leave Requests</Link></div>

  const detail = query.data
  const canWithdraw = detail.status === 'PendingApproval'
  const canCancel = detail.status === 'Approved'

  async function withdraw() {
    if (action !== null || !window.confirm('Are you sure you want to withdraw this leave request?')) return
    setAction('withdraw')
    setActionError(null)
    setSuccess(null)
    try {
      await withdrawLeaveRequest(requestId)
      setSuccess('Leave request withdrawn successfully.')
      query.refetch()
    } catch (error) {
      setActionError(withdrawErrorMessage(error))
    } finally {
      setAction(null)
    }
  }

  async function cancel() {
    if (action !== null || !window.confirm('Are you sure you want to cancel this approved leave request?')) return
    setAction('cancel')
    setActionError(null)
    setSuccess(null)
    try {
      await cancelLeaveRequest(requestId)
      setSuccess('Leave request cancelled successfully.')
      query.refetch()
    } catch (error) {
      setActionError(cancelErrorMessage(error))
    } finally {
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
