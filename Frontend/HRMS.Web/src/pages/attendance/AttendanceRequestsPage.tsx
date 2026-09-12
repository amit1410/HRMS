import { useEffect, useState } from 'react'
import { approveOnDuty, approveRegularization, cancelOnDuty, cancelRegularization, getMyOnDuty, getMyRegularization, listManagerOnDuty, listManagerRegularizations, listMyOnDuty, listMyRegularizations, rejectOnDuty, rejectRegularization, submitOnDuty, submitRegularization, type OnDuty, type Regularization } from '../../api/attendance.ts'
import { ApiError, hasFieldErrors } from '../../api/errors.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'

function errorMessage(error: unknown) {
  if (!(error instanceof ApiError)) return 'The attendance request could not be completed.'
  const fields = hasFieldErrors(error) ? ` ${Object.values(error.fieldErrors).join(' ')}` : ''
  return `${error.message}${fields}`
}
function status(value: string) {
  const label = ['Pending', 'Approved', 'Rejected', 'Cancelled'].includes(value) ? value : 'Unknown'
  return <span className={`status-badge status-${label.toLowerCase()}`}>{label}</span>
}
const isPending = (value: string) => value === 'Pending'

export function AttendanceRequestsPage() {
  const { can } = useAuth()
  const canManageRegularization = can(Permissions.attendance.regularizationApprove)
  const canManageOnDuty = can(Permissions.attendance.onDutyApprove)
  const today = new Date().toISOString().slice(0, 10)
  const [date, setDate] = useState(today); const [out, setOut] = useState(''); const [reason, setReason] = useState('')
  const [odStart, setOdStart] = useState(today); const [odEnd, setOdEnd] = useState(today); const [odReason, setOdReason] = useState('')
  const [regularizations, setRegularizations] = useState<Regularization[]>([]); const [onDuty, setOnDuty] = useState<OnDuty[]>([])
  const [managerRegularizations, setManagerRegularizations] = useState<Regularization[]>([]); const [managerOnDuty, setManagerOnDuty] = useState<OnDuty[]>([])
  const [selected, setSelected] = useState<Regularization | OnDuty | null>(null); const [error, setError] = useState(''); const [notice, setNotice] = useState(''); const [busy, setBusy] = useState(false)

  async function refresh() {
    try {
      setError('')
      const regQueue = canManageRegularization ? listManagerRegularizations() : Promise.resolve({ items: [], page: 1, pageSize: 20, totalCount: 0 })
      const odQueue = canManageOnDuty ? listManagerOnDuty() : Promise.resolve({ items: [], page: 1, pageSize: 20, totalCount: 0 })
      const [mine, duties, regs, ods] = await Promise.all([listMyRegularizations(), listMyOnDuty(), regQueue, odQueue])
      setRegularizations(mine.items); setOnDuty(duties.items); setManagerRegularizations(regs.items); setManagerOnDuty(ods.items)
    } catch (cause) { setError(errorMessage(cause)) }
  }
  useEffect(() => { void refresh() }, [canManageRegularization, canManageOnDuty])
  async function run(action: () => Promise<unknown>, success: string) {
    try { setBusy(true); setError(''); setNotice(''); await action(); setNotice(success); await refresh() } catch (cause) { setError(errorMessage(cause)) } finally { setBusy(false) }
  }
  function confirmAction(label: string, action: () => Promise<unknown>, success: string) { if (window.confirm(`Are you sure you want to ${label}?`)) void run(action, success) }
  async function openDetails(item: Regularization | OnDuty) {
    try { setSelected('businessDate' in item ? await getMyRegularization(item.id) : await getMyOnDuty(item.id)) } catch (cause) { setError(errorMessage(cause)) }
  }
  const regularizationValid = Boolean(date && reason.trim() && (!out || !Number.isNaN(new Date(out).getTime())))
  const onDutyValid = Boolean(odStart && odEnd && odStart <= odEnd && odReason.trim())

  return <div className="leave-admin-page attendance-read-page">
    <PageHeader title="Attendance Requests" subtitle="Submit and track attendance corrections and On Duty requests." />
    {error ? <Notice tone="error">{error}</Notice> : null}{notice ? <Notice tone="success">{notice}</Notice> : null}
    <Card title="Regularization" subtitle="Request a correction for an eligible attendance day.">
      <div className="form-grid"><label className="field"><span>Date</span><input aria-label="Regularization date" className="input" type="date" value={date} onChange={event => setDate(event.target.value)} /></label><label className="field"><span>Corrected out</span><input aria-label="Corrected out" className="input" type="datetime-local" value={out} onChange={event => setOut(event.target.value)} /></label><label className="field"><span>Reason</span><input aria-label="Regularization reason" className="input" value={reason} onChange={event => setReason(event.target.value)} /></label></div>
      {!reason.trim() ? <p className="field-help">A reason is required.</p> : null}
      <button className="button button-primary" type="button" disabled={!regularizationValid || busy} onClick={() => void run(() => submitRegularization({ businessDate: date, requestType: 'MissingOutPunch', proposedOutAtUtc: out ? new Date(out).toISOString() : null, reason: reason.trim() }), 'Regularization submitted.')}>Submit Regularization</button>
      {regularizations.length === 0 ? <p className="field-help">No Regularization requests yet.</p> : <ul>{regularizations.map(item => <li key={item.id}><button className="button button-link" type="button" onClick={() => void openDetails(item)}>{item.businessDate}</button> · {status(item.status)} · {item.reason} {isPending(item.status) ? <button className="button button-link" type="button" disabled={busy} onClick={() => confirmAction('cancel this Regularization request', () => cancelRegularization(item.id), 'Regularization cancelled.')}>Cancel</button> : null}</li>)}</ul>}
    </Card>
    <Card title="On Duty" subtitle="Request a full-day On Duty period.">
      <div className="form-grid"><label className="field"><span>Start date</span><input aria-label="On Duty start date" className="input" type="date" value={odStart} onChange={event => setOdStart(event.target.value)} /></label><label className="field"><span>End date</span><input aria-label="On Duty end date" className="input" type="date" value={odEnd} onChange={event => setOdEnd(event.target.value)} /></label><label className="field"><span>Reason</span><input aria-label="On Duty reason" className="input" value={odReason} onChange={event => setOdReason(event.target.value)} /></label></div>
      {odStart > odEnd ? <p className="field-help">End date must be on or after start date.</p> : null}
      <button className="button button-primary" type="button" disabled={!onDutyValid || busy} onClick={() => void run(() => submitOnDuty({ startDate: odStart, endDate: odEnd, reason: odReason.trim() }), 'On Duty submitted.')}>Submit On Duty</button>
      {onDuty.length === 0 ? <p className="field-help">No On Duty requests yet.</p> : <ul>{onDuty.map(item => <li key={item.id}><button className="button button-link" type="button" onClick={() => void openDetails(item)}>{item.startDate} – {item.endDate}</button> · {status(item.status)} · {item.reason} {isPending(item.status) ? <button className="button button-link" type="button" disabled={busy} onClick={() => confirmAction('cancel this On Duty request', () => cancelOnDuty(item.id), 'On Duty cancelled.')}>Cancel</button> : null}</li>)}</ul>}
    </Card>
    {(canManageRegularization || canManageOnDuty) ? <Card title="Manager approvals" subtitle="Requests from your effective direct reports.">
      {canManageRegularization ? <><h3>Regularization</h3>{managerRegularizations.length === 0 ? <p className="field-help">No pending Regularization requests.</p> : managerRegularizations.map(item => <p key={item.id}>{item.employeeId} · {item.businessDate} · {item.reason} <button className="button button-link" type="button" disabled={busy} onClick={() => confirmAction('approve this Regularization request', () => approveRegularization(item.id), 'Regularization approved.')}>Approve</button><button className="button button-link" type="button" disabled={busy} onClick={() => confirmAction('reject this Regularization request', () => rejectRegularization(item.id, 'Rejected by manager'), 'Regularization rejected.')}>Reject</button></p>)}</> : null}
      {canManageOnDuty ? <><h3>On Duty</h3>{managerOnDuty.length === 0 ? <p className="field-help">No pending On Duty requests.</p> : managerOnDuty.map(item => <p key={item.id}>{item.employeeId} · {item.startDate} – {item.endDate} · {item.reason} <button className="button button-link" type="button" disabled={busy} onClick={() => confirmAction('approve this On Duty request', () => approveOnDuty(item.id), 'On Duty approved.')}>Approve</button><button className="button button-link" type="button" disabled={busy} onClick={() => confirmAction('reject this On Duty request', () => rejectOnDuty(item.id, 'Rejected by manager'), 'On Duty rejected.')}>Reject</button></p>)}</> : null}
    </Card> : null}
    {selected ? <div className="calendar-event-dialog" role="dialog" aria-modal="true" aria-labelledby="attendance-request-detail-title"><div className="calendar-event-dialog-card"><button type="button" className="dialog-close" aria-label="Close request details" onClick={() => setSelected(null)}>×</button><h2 id="attendance-request-detail-title">Attendance request detail</h2><p>{'businessDate' in selected ? 'Regularization' : 'On Duty'} · {status(selected.status)}</p><dl className="detail-list"><div><dt>Dates</dt><dd>{'businessDate' in selected ? selected.businessDate : `${selected.startDate} – ${selected.endDate}`}</dd></div><div><dt>Reason</dt><dd>{selected.reason}</dd></div><div><dt>Submitted</dt><dd>{new Date(selected.submittedAtUtc).toLocaleString()}</dd></div>{selected.reviewerComments ? <div><dt>Reviewer comments</dt><dd>{selected.reviewerComments}</dd></div> : null}</dl>{selected.events.length ? <><h3>History</h3><ul>{selected.events.map((event, index) => <li key={`${event.eventType}-${index}`}>{event.eventType} · {event.comments ?? 'No comment'}</li>)}</ul></> : null}</div></div> : null}
  </div>
}
