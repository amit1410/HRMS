import { useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { ApiError } from '../../api/errors.ts'
import { listLeaveTypes, type LeaveType } from '../../api/leaveConfiguration.ts'
import { previewLeaveRequest, submitLeaveRequest, type LeaveRequestPreview, type LeaveRequestSubmission } from '../../api/leaveRequests.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Permissions } from '../../auth/permissions.ts'
import { Link } from 'react-router-dom'

const emptyDraft = { leaveTypeId: '', startDate: '', endDate: '' }

function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  return `draft-${Date.now()}-${Math.random().toString(36).slice(2)}`
}

export function LeaveRequestPreviewPage() {
  const { status, user, can } = useAuth()
  const [draft, setDraft] = useState(emptyDraft)
  const [idempotencyKey, setIdempotencyKey] = useState(newIdempotencyKey)
  const [preview, setPreview] = useState<LeaveRequestPreview | null>(null)
  const [previewDraft, setPreviewDraft] = useState<typeof draft | null>(null)
  const [submission, setSubmission] = useState<LeaveRequestSubmission | null>(null)
  const [error, setError] = useState<ApiError | null>(null)
  const [submitError, setSubmitError] = useState<ApiError | null>(null)
  const [localError, setLocalError] = useState<string | null>(null)
  const [previewing, setPreviewing] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const previewBusyRef = useRef(false)
  const submitBusyRef = useRef(false)
  const requestSequenceRef = useRef(0)
  const types = useApiQuery(signal => listLeaveTypes({ page: 1, pageSize: 100, isActive: true }, signal), [])
  const selectedType = useMemo(() => types.data?.items.find(item => item.id === draft.leaveTypeId), [draft.leaveTypeId, types.data])
  const previewIsCurrent = preview !== null && previewDraft !== null && previewDraft.leaveTypeId === draft.leaveTypeId && previewDraft.startDate === draft.startDate && previewDraft.endDate === draft.endDate

  function changeDraft(field: keyof typeof draft, value: string) {
    requestSequenceRef.current += 1
    setDraft(current => ({ ...current, [field]: value }))
    setPreview(null)
    setPreviewDraft(null)
    setSubmission(null)
    setError(null)
    setSubmitError(null)
    setLocalError(null)
  }

  function validateDraft(): string | null {
    if (!draft.leaveTypeId) return 'Select a Leave Type.'
    if (!draft.startDate) return 'Enter a Start Date.'
    if (!draft.endDate) return 'Enter an End Date.'
    if (draft.startDate > draft.endDate) return 'Start Date must be on or before End Date.'
    return null
  }

  async function previewRequest(event: FormEvent) {
    event.preventDefault()
    if (previewBusyRef.current || submitting || submission) return
    const message = validateDraft()
    if (message) { setLocalError(message); setPreview(null); return }
    previewBusyRef.current = true
    const requestSequence = requestSequenceRef.current
    setPreviewing(true)
    setError(null)
    setSubmitError(null)
    setLocalError(null)
    try {
      const result = await previewLeaveRequest({ ...draft, idempotencyKey })
      if (requestSequence === requestSequenceRef.current) {
        setPreview(result)
        setPreviewDraft({ ...draft })
      }
    } catch (caught) {
      setPreview(null)
      setPreviewDraft(null)
      setError(caught instanceof ApiError ? caught : new ApiError('Unable to preview this Leave request.'))
    } finally {
      previewBusyRef.current = false
      if (requestSequence === requestSequenceRef.current) setPreviewing(false)
    }
  }

  async function submitRequest() {
    if (!preview || !previewIsCurrent || submission || submitBusyRef.current) return
    submitBusyRef.current = true
    setSubmitting(true)
    setSubmitError(null)
    try {
      setSubmission(await submitLeaveRequest({ ...draft, idempotencyKey }))
    } catch (caught) {
      const apiError = caught instanceof ApiError ? caught : new ApiError('Unable to submit this Leave request.')
      setSubmitError(apiError)
      if (/unsupportedconfiguration|unsupported configuration/i.test(apiError.message)) {
        setPreview(null)
        setPreviewDraft(null)
      }
    } finally { submitBusyRef.current = false; setSubmitting(false) }
  }

  if (status === 'restoring') return <div className="leave-admin-page"><p className="state-block"><Spinner label="Loading employee context" /></p></div>
  if (status !== 'authenticated') return <div className="leave-admin-page"><PageHeader title="Apply Leave" /><Notice tone="error">Sign in to apply for Leave.</Notice></div>
  if (!user?.employeeIdentity || user.employeeIdentity.status !== 'Linked' || !user.employeeIdentity.employee) return <div className="leave-admin-page"><PageHeader title="Apply Leave" /><Notice tone="error">Your account is not linked to an active employee. Contact HR before applying for Leave.</Notice></div>

  function reset() {
    setDraft(emptyDraft)
    setIdempotencyKey(newIdempotencyKey())
    setPreview(null)
    setPreviewDraft(null)
    setSubmission(null)
    setError(null)
    setSubmitError(null)
    setLocalError(null)
  }

  const unsupported = error?.message.toLowerCase().includes('unsupportedconfiguration') || error?.message.toLowerCase().includes('unsupported configuration')
  const submitErrorMessage = submitError?.message.toLowerCase().includes('balancenotinitialized')
    ? 'Your leave balance has not been initialized for this leave period. Please contact HR.'
    : submitError?.message.toLowerCase().includes('insufficientleavebalance')
      ? 'Insufficient leave balance for this request.'
      : submitError?.message.toLowerCase().includes('idempotencyconflict')
      ? 'This draft key was already used for different request data. Start a new request to continue.'
      : submitError?.message.toLowerCase().includes('overlap')
        ? 'The requested dates overlap another active leave request.'
        : submitError?.message.toLowerCase().includes('concurrencyconflict')
          ? 'The request changed while it was being submitted. Please try again with this draft.'
          : submitError?.message

  return <div className="leave-admin-page leave-preview-page">
    <div className="leave-preview-breadcrumb"><Link to="/">Home</Link><span aria-hidden="true">/</span><Link to="/leave-management">Leave</Link><span aria-hidden="true">/</span><strong>Apply Leave</strong></div>
    <PageHeader title="Apply Leave" subtitle="Submit a leave request for your planned absence" actions={<Link className="button button-secondary leave-preview-back" to="/leave-management"><span aria-hidden="true">←</span> Back to Leave Dashboard</Link>} />
    <div className="leave-preview-info-banner"><span className="leave-preview-info-icon"><ApplyIcon name="info" /></span><div><strong>This is a preview only</strong><span>No leave request, balances, or approval record is created. Review the server-authoritative interpretation before submission is enabled.</span></div></div>
    <div className="leave-preview-layout">
      <div className="leave-preview-main">
        <Card className="leave-preview-form-card" title="Leave details" subtitle="Provide the leave period and type for your request.">
          <div className="leave-preview-stepper" aria-label="Leave request progress"><div className="is-current"><span>1</span><div><strong>Leave Details</strong><small>Select dates and type</small></div></div><div><span>2</span><div><strong>Review</strong><small>Check details</small></div></div><div><span>3</span><div><strong>Submit</strong><small>Preview only</small></div></div></div>
          {(localError || error || submitError) ? <div className="leave-preview-form-errors">{localError ? <InlineError message={localError} /> : null}{error ? <InlineError message={`${unsupported ? 'This Leave Policy uses a configuration that is not supported in preview yet. ' : ''}${error.message}`} /> : null}{submitError ? <InlineError message={submitErrorMessage ?? 'Unable to submit this Leave request.'} /> : null}</div> : null}
          <form className="form-stack leave-preview-form" onSubmit={previewRequest} aria-busy={previewing}>
            {types.isLoading ? <div className="leave-preview-loading"><Spinner label="Loading Leave Types" /></div> : types.error ? <InlineError message={types.error.message} /> : <label className="field"><span>Leave Type <em>(required)</em></span><div className="leave-preview-input-wrap"><ApplyIcon name="layers" /><select className="input" value={draft.leaveTypeId} onChange={event => changeDraft('leaveTypeId', event.target.value)} disabled={previewing || submitting || submission !== null} required><option value="">Select leave type</option>{(types.data?.items ?? []).filter(item => item.isActive).map(item => <option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}</select></div></label>}
            <div className="form-grid"><label className="field"><span>Start Date <em>(required)</em></span><div className="leave-preview-input-wrap"><ApplyIcon name="calendar" /><input className="input" type="date" value={draft.startDate} onChange={event => changeDraft('startDate', event.target.value)} disabled={previewing || submitting || submission !== null} required /></div></label><label className="field"><span>End Date <em>(required)</em></span><div className="leave-preview-input-wrap"><ApplyIcon name="calendar" /><input className="input" type="date" value={draft.endDate} onChange={event => changeDraft('endDate', event.target.value)} disabled={previewing || submitting || submission !== null} required /></div></label></div>
            <div className="leave-preview-form-note"><ApplyIcon name="info" /><span>Full-day preview is currently supported. The server will calculate working days, holidays, and balance impact.</span></div>
            <div className="form-actions leave-preview-actions"><button className="button button-secondary" type="button" onClick={reset} disabled={previewing || submitting}>Reset</button><button className="button button-primary" type="submit" disabled={previewing || submitting || types.isLoading || submission !== null}>{previewing ? <Spinner size={14} label="Previewing…" /> : <>Preview Leave <span aria-hidden="true">→</span></>}</button></div>
          </form>
        </Card>
        {preview ? <PreviewResult preview={preview} leaveType={selectedType} /> : null}
        {preview && !submission ? <Card title="Submit Leave Request" subtitle="Submission uses the same idempotency key as this preview.">
      {preview.balanceReservationRequired ? <Notice tone="info">This request will reserve {preview.chargeableQuantity} day(s) from your leave balance when submitted.</Notice> : null}
      <div className="form-actions"><button className="button button-primary" type="button" onClick={() => void submitRequest()} disabled={!previewIsCurrent || submitting}>{submitting ? <Spinner size={14} label="Submitting…" /> : 'Submit Leave Request'}</button></div>
        </Card> : null}
        {submission ? <SubmissionResult submission={submission} leaveType={selectedType} reservationRequired={preview?.balanceReservationRequired === true} onNewRequest={reset} /> : null}
      </div>
      <aside className="leave-preview-sidebar" aria-label="Leave information">
        <Card title="Quick Information" subtitle="Things to keep in mind"><ul className="leave-preview-tips"><li><ApplyIcon name="check" />Check your available leave balance</li><li><ApplyIcon name="check" />Select the correct leave type</li><li><ApplyIcon name="check" />Review dates before previewing</li><li><ApplyIcon name="check" />The server calculates chargeable days</li></ul></Card>
        <Card title="Need Help?"><div className="leave-preview-help"><p>For leave-related queries, please contact your HR team or refer to the Leave Policy.</p>{can(Permissions.leave.policyView) ? <Link className="row-action" to="/leave-management/policies">View Leave Policy <span aria-hidden="true">→</span></Link> : null}</div></Card>
        <Card title="Other Actions" subtitle="Quick access to leave related pages"><nav className="leave-preview-links" aria-label="Other leave actions"><Link to="/leave-management/my-requests"><ApplyIcon name="clipboard" /><span>My Leave Requests</span><span aria-hidden="true">→</span></Link><Link to="/leave-management/team-calendar"><ApplyIcon name="users" /><span>Team Leave Calendar</span><span aria-hidden="true">→</span></Link></nav></Card>
      </aside>
    </div>
  </div>
}

function InlineError({ message }: { message: string }) { return <div className="leave-preview-inline-error" role="alert"><ApplyIcon name="alert" /><span>{message}</span></div> }

type ApplyIconName = 'alert' | 'calendar' | 'check' | 'clipboard' | 'info' | 'layers' | 'users'
function ApplyIcon({ name }: { name: ApplyIconName }) {
  const paths: Record<ApplyIconName, ReactNode> = {
    alert: <><circle cx="12" cy="12" r="9" /><path d="M12 7v5M12 16h.01" /></>,
    calendar: <><rect x="3" y="4" width="18" height="17" rx="2" /><path d="M16 2v4M8 2v4M3 10h18" /></>,
    check: <><circle cx="12" cy="12" r="9" /><path d="m8 12 2.5 2.5L16 9" /></>,
    clipboard: <><rect x="5" y="4" width="14" height="17" rx="2" /><path d="M9 4V3h6v1M8 9h8M8 13h6" /></>,
    info: <><circle cx="12" cy="12" r="9" /><path d="M12 11v5M12 8h.01" /></>,
    layers: <><path d="m12 3 8 4-8 4-8-4 8-4Z" /><path d="m4 12 8 4 8-4" /></>,
    users: <><circle cx="9" cy="8" r="3" /><path d="M3 20c.5-3 2.5-5 6-5s5.5 2 6 5M16 5.5a3 3 0 0 1 0 5.5M17 15c2.3.4 3.6 2 4 4" /></>,
  }
  return <svg className="leave-preview-icon" viewBox="0 0 24 24" aria-hidden="true" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8">{paths[name]}</svg>
}

function PreviewResult({ preview, leaveType }: { preview: LeaveRequestPreview; leaveType?: LeaveType }) {
  const entitlement = preview.entitlementMode === 'Allocated' ? 'Allocated entitlement' : preview.entitlementMode === 'Unlimited' ? 'Unlimited entitlement' : 'No balance required'
  return <Card title="Preview result" subtitle="These quantities and days came from the server.">
    <dl className="detail-list"><div><dt>Leave Type</dt><dd>{leaveType ? `${leaveType.code} — ${leaveType.name}` : preview.leaveTypeId}</dd></div><div><dt>Dates</dt><dd>{preview.startDate} — {preview.endDate}</dd></div><div><dt>Calendar days</dt><dd>{preview.calendarDays ?? preview.requestDays.length}</dd></div><div><dt>Weekly offs excluded</dt><dd>{preview.excludedWeeklyOffDays ?? 0}</dd></div><div><dt>Holidays excluded</dt><dd>{preview.excludedHolidayDays ?? 0}</dd></div><div><dt>Requested Quantity</dt><dd>{preview.requestedQuantity.toFixed(3)}</dd></div><div><dt>Chargeable Quantity</dt><dd>{preview.chargeableQuantity.toFixed(3)}</dd></div><div><dt>Leave days charged</dt><dd>{(preview.workingLeaveDays ?? preview.chargeableQuantity).toFixed(3)}</dd></div><div><dt>Entitlement</dt><dd>{entitlement}</dd></div></dl>
    {preview.balanceReservationRequired ? <Notice tone="info">Balance reservation will be required when this request is submitted.</Notice> : null}
    <div className="table-wrap"><table className="data-table"><caption className="sr-only">Preview request days</caption><thead><tr><th>Date</th><th>Requested</th><th>Chargeable</th><th>Employee requested</th><th>Classification</th><th>Calculation reason</th></tr></thead><tbody>{preview.requestDays.map(day => <tr key={day.date}><td>{day.date}</td><td>{day.requestedQuantity.toFixed(3)}</td><td>{day.chargeableQuantity.toFixed(3)}</td><td>{day.isEmployeeRequested ? 'Yes' : 'No'}</td><td>{day.dayClassification ?? '—'}</td><td>{day.calculationReason ?? '—'}</td></tr>)}</tbody></table></div>
  </Card>
}

function SubmissionResult({ submission, leaveType, reservationRequired, onNewRequest }: { submission: LeaveRequestSubmission; leaveType?: LeaveType; reservationRequired: boolean; onNewRequest: () => void }) {
  return <Card title={submission.isReplay ? 'Leave Request Already Submitted' : 'Leave Request Submitted'} subtitle={submission.isReplay ? 'The existing request has been loaded.' : 'Your leave request is now pending approval.'}>
    {submission.isReplay ? <Notice tone="info">This request was already submitted. The existing request has been loaded.</Notice> : <Notice tone="success">{reservationRequired ? 'Leave request submitted. The required leave balance has been reserved.' : 'Leave request submitted successfully.'}</Notice>}
    <dl className="detail-list"><div><dt>Request ID</dt><dd>{submission.requestId}</dd></div><div><dt>Status</dt><dd>{submission.status === 'PendingApproval' ? 'Pending Approval' : submission.status}</dd></div><div><dt>Leave Type</dt><dd>{leaveType ? `${leaveType.code} — ${leaveType.name}` : submission.leaveTypeId}</dd></div><div><dt>Dates</dt><dd>{submission.startDate} — {submission.endDate}</dd></div><div><dt>Calendar days</dt><dd>{submission.calendarDays ?? submission.requestDays.length}</dd></div><div><dt>Requested Quantity</dt><dd>{submission.requestedQuantity.toFixed(3)}</dd></div><div><dt>Chargeable Quantity</dt><dd>{submission.chargeableQuantity.toFixed(3)}</dd></div><div><dt>Leave days charged</dt><dd>{(submission.workingLeaveDays ?? submission.chargeableQuantity).toFixed(3)}</dd></div><div><dt>Submitted</dt><dd>{submission.submittedAtUtc}</dd></div></dl>
    <div className="form-actions"><button className="button button-secondary" type="button" onClick={onNewRequest}>New Request</button></div>
  </Card>
}
