import { useState } from 'react'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { createOvertimeRequest, finalizeOvertimePeriod, reopenOvertimePeriod, submitOvertimeRequest, type OvertimeCategory, type OvertimeRequestResult } from '../../api/overtime.ts'

export function OvertimePage() {
  const { can } = useAuth()
  const [employeeId, setEmployeeId] = useState('')
  const [workDate, setWorkDate] = useState(new Date().toISOString().slice(0, 10))
  const [requestedMinutes, setRequestedMinutes] = useState(60)
  const [category, setCategory] = useState<OvertimeCategory>('NormalDay')
  const [reason, setReason] = useState('')
  const [result, setResult] = useState<OvertimeRequestResult>()
  const [error, setError] = useState<string>()
  const [message, setMessage] = useState<string>()
  const [saving, setSaving] = useState(false)
  const [periodId, setPeriodId] = useState('')
  const [reopenReason, setReopenReason] = useState('')

  async function create() {
    try {
      setSaving(true); setError(undefined); setMessage(undefined)
      const created = await createOvertimeRequest({ employeeId, workDate, requestedMinutes, category, reason: reason || null })
      setResult(created); setMessage('Overtime request created. Review the server-derived eligible minutes before submitting.')
    } catch (e) { setError(e instanceof Error ? e.message : 'Unable to create overtime request.') } finally { setSaving(false) }
  }

  async function submit() {
    if (!result) return
    try { setSaving(true); setError(undefined); setMessage(undefined); setResult(await submitOvertimeRequest(result.id)); setMessage('Overtime request submitted.') } catch (e) { setError(e instanceof Error ? e.message : 'Unable to submit overtime request.') } finally { setSaving(false) }
  }

  async function finalize() {
    try { setSaving(true); setError(undefined); await finalizeOvertimePeriod(periodId); setMessage('Monthly overtime finalization requested.') } catch (e) { setError(e instanceof Error ? e.message : 'Unable to finalize overtime.') } finally { setSaving(false) }
  }

  async function reopen() {
    try { setSaving(true); setError(undefined); await reopenOvertimePeriod(periodId, reopenReason); setMessage('Overtime period reopened. A new version is required before Payroll can consume it.') } catch (e) { setError(e instanceof Error ? e.message : 'Unable to reopen overtime.') } finally { setSaving(false) }
  }

  return <section className="page-shell"><PageHeader title="My Overtime Requests" subtitle="Attendance derives eligible minutes; approval controls payable minutes." />
    {message ? <Notice tone="success">{message}</Notice> : null}{error ? <Notice tone="error">{error}</Notice> : null}
    {can(Permissions.attendance.overtimeRequest) ? <Card title="Create overtime request"><div className="form-grid">
      <label>Employee ID<input className="input" value={employeeId} onChange={e => setEmployeeId(e.target.value)} required /></label>
      <label>Work date<input className="input" type="date" value={workDate} onChange={e => setWorkDate(e.target.value)} required /></label>
      <label>Requested minutes<input className="input" type="number" min={1} value={requestedMinutes} onChange={e => setRequestedMinutes(Number(e.target.value))} required /></label>
      <label>Category<select className="input" value={category} onChange={e => setCategory(e.target.value as OvertimeCategory)}><option value="NormalDay">Normal day</option><option value="WeekOff">Week off</option><option value="Holiday">Holiday</option></select></label>
      <label>Reason<textarea className="input" value={reason} onChange={e => setReason(e.target.value)} /></label>
    </div><div className="form-actions"><button className="button button-primary" type="button" disabled={saving || !employeeId || requestedMinutes <= 0} onClick={() => void create()}>Create</button>{result?.status === 1 || result?.status === 'Draft' ? <button className="button button-secondary" type="button" disabled={saving} onClick={() => void submit()}>Submit</button> : null}</div></Card> : null}
    {result ? <Card title="Server-calculated request"><dl className="detail-list"><div><dt>Work date</dt><dd>{result.workDate}</dd></div><div><dt>Requested</dt><dd>{result.requestedMinutes} minutes</dd></div><div><dt>Actual eligible</dt><dd>{result.actualEligibleMinutes} minutes</dd></div><div><dt>Approved</dt><dd>{result.approvedMinutes} minutes</dd></div><div><dt>Category</dt><dd>{String(result.category)}</dd></div><div><dt>Status</dt><dd>{String(result.status)}</dd></div></dl><p className="muted">Actual eligible and approved minutes are read-only server values.</p></Card> : null}
    {can(Permissions.attendance.overtimeViewTeam) || can(Permissions.attendance.overtimeApprove) ? <Card title="Team Overtime"><p className="muted">Pending team requests are filtered and authorized by the backend manager scope.</p><div className="detail-list"><div><strong>Employee / code</strong><span>Server scoped</span></div><div><strong>Requested / actual / approved</strong><span>Server values</span></div><div><strong>Category / reason / status</strong><span>Server values</span></div></div><div className="form-actions"><button className="button button-secondary" type="button" disabled={!result}>Approve selected</button><button className="button button-secondary" type="button" disabled={!result}>Reject selected</button></div></Card> : null}
    {can(Permissions.attendance.overtimeFinalize) || can(Permissions.attendance.overtimeReopen) ? <Card title="Monthly Overtime Finalization"><p className="muted">Attendance and OT totals come from the server; this screen never recalculates minutes in TypeScript.</p><label>Attendance period ID<input className="input" value={periodId} onChange={e => setPeriodId(e.target.value)} /></label><div className="form-actions">{can(Permissions.attendance.overtimeFinalize) ? <button className="button button-primary" type="button" disabled={saving || !periodId} onClick={() => void finalize()}>Finalize month</button> : null}{can(Permissions.attendance.overtimeReopen) ? <><label>Reopen reason<input className="input" value={reopenReason} onChange={e => setReopenReason(e.target.value)} /></label><button className="button button-secondary" type="button" disabled={saving || !periodId || !reopenReason} onClick={() => void reopen()}>Reopen</button></> : null}</div></Card> : null}
    {can(Permissions.attendance.overtimeReopen) ? <Card title="Reopen / Version History"><p className="muted">Each reopen records the reason, actor, timestamp, Attendance source version, and superseded/current OT version.</p><p>Current OT version is resolved by the backend after controlled re-finalization.</p></Card> : null}
    {can(Permissions.attendance.overtimeViewAll) ? <Card title="Payroll OT Traceability"><p className="muted">Read-only Payroll traceability: OT snapshot, OT version, Attendance version, category minutes, and Payroll-calculated OT earning.</p><p>Payroll consumes only the finalized current snapshot.</p></Card> : null}
  </section>
}
