import { useMemo, useState, type FormEvent } from 'react'
import { ApiError } from '../../api/errors.ts'
import { listLeaveTypes, type LeaveType } from '../../api/leaveConfiguration.ts'
import { previewLeaveRequest, submitLeaveRequest, type LeaveRequestPreview, type LeaveRequestSubmission } from '../../api/leaveRequests.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

const emptyDraft = { leaveTypeId: '', startDate: '', endDate: '' }

function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  return `draft-${Date.now()}-${Math.random().toString(36).slice(2)}`
}

export function LeaveRequestPreviewPage() {
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
  const types = useApiQuery(signal => listLeaveTypes({ page: 1, pageSize: 100, isActive: true }, signal), [])
  const selectedType = useMemo(() => types.data?.items.find(item => item.id === draft.leaveTypeId), [draft.leaveTypeId, types.data])
  const previewIsCurrent = preview !== null && previewDraft !== null && previewDraft.leaveTypeId === draft.leaveTypeId && previewDraft.startDate === draft.startDate && previewDraft.endDate === draft.endDate

  function changeDraft(field: keyof typeof draft, value: string) {
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
    const message = validateDraft()
    if (message) { setLocalError(message); setPreview(null); return }
    setPreviewing(true)
    setError(null)
    setSubmitError(null)
    setLocalError(null)
    try {
      const result = await previewLeaveRequest({ ...draft, idempotencyKey })
      setPreview(result)
      setPreviewDraft({ ...draft })
    } catch (caught) {
      setPreview(null)
      setPreviewDraft(null)
      setError(caught instanceof ApiError ? caught : new ApiError('Unable to preview this Leave request.'))
    } finally { setPreviewing(false) }
  }

  async function submitRequest() {
    if (!preview || !previewIsCurrent || submission) return
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
    } finally { setSubmitting(false) }
  }

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
    <PageHeader title="Apply Leave" subtitle="Review the server-authoritative interpretation before submission is enabled." />
    <Notice tone="info">This is preview only. No Leave request, balance, or approval record is created.</Notice>
    {localError ? <Notice tone="error">{localError}</Notice> : null}
    {error ? <Notice tone="error"><span>{unsupported ? 'This Leave Policy uses a configuration that is not supported in preview yet. ' : ''}{error.message}</span></Notice> : null}
    {submitError ? <Notice tone="error">{submitErrorMessage}</Notice> : null}
    <Card title="Leave details" subtitle="Full-day preview is currently supported.">
      <form className="form-stack" onSubmit={previewRequest} aria-busy={previewing}>
        {types.isLoading ? <Spinner label="Loading Leave Types" /> : types.error ? <Notice tone="error">{types.error.message}</Notice> : <label className="field"><span>Leave Type <em>(required)</em></span><select className="input" value={draft.leaveTypeId} onChange={event => changeDraft('leaveTypeId', event.target.value)} disabled={previewing || submitting || submission !== null} required><option value="">Select Leave Type</option>{(types.data?.items ?? []).filter(item => item.isActive).map(item => <option key={item.id} value={item.id}>{item.code} — {item.name}</option>)}</select></label>}
        <div className="form-grid"><label className="field"><span>Start Date <em>(required)</em></span><input className="input" type="date" value={draft.startDate} onChange={event => changeDraft('startDate', event.target.value)} disabled={previewing || submitting || submission !== null} required /></label><label className="field"><span>End Date <em>(required)</em></span><input className="input" type="date" value={draft.endDate} onChange={event => changeDraft('endDate', event.target.value)} disabled={previewing || submitting || submission !== null} required /></label></div>
        <div className="form-actions"><button className="button button-secondary" type="button" onClick={reset} disabled={previewing || submitting}>Reset</button><button className="button button-primary" type="submit" disabled={previewing || submitting || types.isLoading || submission !== null}>{previewing ? <Spinner size={14} label="Previewing…" /> : 'Preview Leave'}</button></div>
      </form>
    </Card>
    {preview ? <PreviewResult preview={preview} leaveType={selectedType} /> : null}
    {preview && !submission ? <Card title="Submit Leave Request" subtitle="Submission uses the same idempotency key as this preview.">
      {preview.balanceReservationRequired ? <Notice tone="info">This request will reserve {preview.chargeableQuantity} day(s) from your leave balance when submitted.</Notice> : null}
      <div className="form-actions"><button className="button button-primary" type="button" onClick={() => void submitRequest()} disabled={!previewIsCurrent || submitting}>{submitting ? <Spinner size={14} label="Submitting…" /> : 'Submit Leave Request'}</button></div>
    </Card> : null}
    {submission ? <SubmissionResult submission={submission} leaveType={selectedType} reservationRequired={preview?.balanceReservationRequired === true} onNewRequest={reset} /> : null}
  </div>
}

function PreviewResult({ preview, leaveType }: { preview: LeaveRequestPreview; leaveType?: LeaveType }) {
  const entitlement = preview.entitlementMode === 'Allocated' ? 'Allocated entitlement' : preview.entitlementMode === 'Unlimited' ? 'Unlimited entitlement' : 'No balance required'
  return <Card title="Preview result" subtitle="These quantities and days came from the server.">
    <dl className="detail-list"><div><dt>Leave Type</dt><dd>{leaveType ? `${leaveType.code} — ${leaveType.name}` : preview.leaveTypeId}</dd></div><div><dt>Dates</dt><dd>{preview.startDate} — {preview.endDate}</dd></div><div><dt>Requested Quantity</dt><dd>{preview.requestedQuantity.toFixed(3)}</dd></div><div><dt>Chargeable Quantity</dt><dd>{preview.chargeableQuantity.toFixed(3)}</dd></div><div><dt>Entitlement</dt><dd>{entitlement}</dd></div></dl>
    {preview.balanceReservationRequired ? <Notice tone="info">Balance reservation will be required when this request is submitted.</Notice> : null}
    <div className="table-wrap"><table className="data-table"><caption className="sr-only">Preview request days</caption><thead><tr><th>Date</th><th>Requested</th><th>Chargeable</th><th>Employee requested</th><th>Classification</th><th>Calculation reason</th></tr></thead><tbody>{preview.requestDays.map(day => <tr key={day.date}><td>{day.date}</td><td>{day.requestedQuantity.toFixed(3)}</td><td>{day.chargeableQuantity.toFixed(3)}</td><td>{day.isEmployeeRequested ? 'Yes' : 'No'}</td><td>{day.dayClassification ?? '—'}</td><td>{day.calculationReason ?? '—'}</td></tr>)}</tbody></table></div>
  </Card>
}

function SubmissionResult({ submission, leaveType, reservationRequired, onNewRequest }: { submission: LeaveRequestSubmission; leaveType?: LeaveType; reservationRequired: boolean; onNewRequest: () => void }) {
  return <Card title={submission.isReplay ? 'Leave Request Already Submitted' : 'Leave Request Submitted'} subtitle={submission.isReplay ? 'The existing request has been loaded.' : 'Your leave request is now pending approval.'}>
    {submission.isReplay ? <Notice tone="info">This request was already submitted. The existing request has been loaded.</Notice> : <Notice tone="success">{reservationRequired ? 'Leave request submitted. The required leave balance has been reserved.' : 'Leave request submitted successfully.'}</Notice>}
    <dl className="detail-list"><div><dt>Request ID</dt><dd>{submission.requestId}</dd></div><div><dt>Status</dt><dd>{submission.status === 'PendingApproval' ? 'Pending Approval' : submission.status}</dd></div><div><dt>Leave Type</dt><dd>{leaveType ? `${leaveType.code} — ${leaveType.name}` : submission.leaveTypeId}</dd></div><div><dt>Dates</dt><dd>{submission.startDate} — {submission.endDate}</dd></div><div><dt>Requested Quantity</dt><dd>{submission.requestedQuantity.toFixed(3)}</dd></div><div><dt>Chargeable Quantity</dt><dd>{submission.chargeableQuantity.toFixed(3)}</dd></div><div><dt>Submitted</dt><dd>{submission.submittedAtUtc}</dd></div></dl>
    <div className="form-actions"><button className="button button-secondary" type="button" onClick={onNewRequest}>New Request</button></div>
  </Card>
}
