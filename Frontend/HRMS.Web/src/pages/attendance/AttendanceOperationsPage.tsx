import { useState } from 'react'
import { applyAttendanceBulkAction, downloadAttendanceOperationsExceptions, getAttendanceOperationsDashboard, getAttendanceOperationsHistory, listAttendanceOperationalExceptions, listManagerRegularizations, resolveAttendanceException, submitBulkAttendanceCorrections, submitManualAttendance, type AttendanceBulkCorrectionResult, type AttendanceOperationalException } from '../../api/attendance.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'

export function AttendanceOperationsPage() {
  const { can, user } = useAuth()
  useDocumentTitle('Attendance Operations')
  const today = new Date().toISOString().slice(0, 10)
  const [page, setPage] = useState(1)
  const [filters, setFilters] = useState({ fromDate: today, toDate: today, search: '', employeeId: '', exceptionType: '', attendanceStatus: '' })
  const [applied, setApplied] = useState(filters)
  const [bulkInput, setBulkInput] = useState('')
  const [bulkReason, setBulkReason] = useState('')
  const [bulkResults, setBulkResults] = useState<AttendanceBulkCorrectionResult[]>([])
  const [bulkError, setBulkError] = useState('')
  const [selectedRequests, setSelectedRequests] = useState<string[]>([])
  const [reviewResults, setReviewResults] = useState<{ requestId: string; success: boolean; failureCode?: string | null; message: string }[]>([])
  const [reviewError, setReviewError] = useState('')
  const [historyEmployeeId, setHistoryEmployeeId] = useState('')
  const [manual, setManual] = useState({ employeeId: '', businessDate: today, correctionType: 'CorrectInOutTime', inAt: '', outAt: '', reason: '', version: '1', comments: '' })
  const [manualMessage, setManualMessage] = useState('')
  const [exportError, setExportError] = useState('')
  const key = JSON.stringify({ page, applied })
  const exceptions = useApiQuery(() => listAttendanceOperationalExceptions({ ...applied, page, pageSize: 25 }), [key])
  const dashboard = useApiQuery(() => getAttendanceOperationsDashboard(applied.fromDate, applied.toDate), [JSON.stringify(applied)])
  const inbox = useApiQuery(() => listManagerRegularizations({ page: 1, pageSize: 25, fromDate: applied.fromDate, toDate: applied.toDate }), [applied.fromDate, applied.toDate])
  const history = useApiQuery(() => historyEmployeeId ? getAttendanceOperationsHistory(historyEmployeeId, { fromDate: applied.fromDate, toDate: applied.toDate, page: 1, pageSize: 50 }) : Promise.resolve(null), [historyEmployeeId, applied.fromDate, applied.toDate])
  const rows = exceptions.data?.items ?? []
  const pending = (inbox.data?.items ?? []).filter(item => item.status === 'Pending')
  const selectedItems = pending.filter(item => selectedRequests.includes(item.id))

  function update(name: keyof typeof filters, value: string) { setFilters(current => ({ ...current, [name]: value })) }
  function apply() { setPage(1); setApplied(filters) }
  function drillDown(exceptionType: string) { const next = { ...filters, exceptionType }; setFilters(next); setPage(1); setApplied(next); document.getElementById('attendance-exception-workbench')?.scrollIntoView?.({ behavior: 'smooth' }) }
  async function submitBulk() {
    if (!bulkReason.trim()) return
    const lines = bulkInput.split('\n').map(line => line.trim()).filter(Boolean)
    if (lines.length > 100) { setBulkError('A bulk correction request is limited to 100 items.'); return }
    const items = lines.flatMap(line => {
      const [employeeId, businessDate, version] = line.split('|').map(value => value.trim())
      const expectedAttendanceVersion = Number(version)
      if (!employeeId || !businessDate || !Number.isInteger(expectedAttendanceVersion) || expectedAttendanceVersion < 1) return []
      return [{ employeeId, businessDate, correctionType: 'CorrectInOutTime', proposedInAtUtc: `${businessDate}T09:00:00Z`, proposedOutAtUtc: `${businessDate}T18:00:00Z`, reason: bulkReason.trim(), expectedAttendanceVersion }]
    })
    if (items.length !== lines.length) { setBulkError('Each item must contain employeeId | YYYY-MM-DD | positive expected version.'); return }
    setBulkError('')
    try { setBulkResults((await submitBulkAttendanceCorrections(items)).items) } catch (error) { setBulkError(error instanceof Error ? error.message : 'Bulk correction was rejected.') }
  }
  async function reviewSelected(approve: boolean) {
    if (selectedItems.length === 0 || selectedItems.length > 100) return
    const comments = approve ? null : window.prompt('Rejection reason')
    if (!approve && !comments?.trim()) return
    setReviewError('')
    try {
      const result = await applyAttendanceBulkAction(selectedItems.map(item => ({ requestId: item.id, isOnDuty: false, approve, expectedVersion: 1, comments: comments?.trim() ?? null })))
      setReviewResults(result.items)
      setSelectedRequests([])
      inbox.refetch()
    } catch (error) { setReviewError(error instanceof Error ? error.message : 'Bulk review failed.') }
  }
  async function submitManual() {
    setManualMessage('')
    try {
      await submitManualAttendance({ employeeId: manual.employeeId, businessDate: manual.businessDate, correctionType: manual.correctionType, proposedInAtUtc: manual.inAt ? `${manual.businessDate}T${manual.inAt}:00Z` : null, proposedOutAtUtc: manual.outAt ? `${manual.businessDate}T${manual.outAt}:00Z` : null, reason: manual.reason.trim(), comments: manual.comments.trim() || null, expectedAttendanceVersion: Number(manual.version) })
      setManualMessage('Manual Attendance request submitted for maker-checker review.')
    } catch (error) { setManualMessage(error instanceof Error ? error.message : 'Manual Attendance request failed. Check period state and Attendance version.') }
  }
  async function exportExceptions() {
    setExportError('')
    try {
      const blob = await downloadAttendanceOperationsExceptions({ fromDate: applied.fromDate, toDate: applied.toDate, employeeId: applied.employeeId || undefined, exceptionType: applied.exceptionType || undefined, search: applied.search || undefined })
      const url = URL.createObjectURL(blob); const link = document.createElement('a'); link.href = url; link.download = 'attendance-exceptions.csv'; link.click(); URL.revokeObjectURL(url)
    } catch (error) { setExportError(error instanceof Error ? error.message : 'Export failed.') }
  }
  async function resolve(row: AttendanceOperationalException, action: 'Acknowledge' | 'Waive') {
    const reason = window.prompt(`${action} reason`)
    if (!reason?.trim()) return
    try { await resolveAttendanceException({ attendanceDayId: row.id, exceptionType: row.exceptionType as 'LateArrival' | 'EarlyDeparture', action, reason: reason.trim(), expectedAttendanceVersion: row.attendanceVersion }); exceptions.refetch() }
    catch (error) { setBulkError(error instanceof Error ? error.message : 'Exception resolution failed.') }
  }

  return <div className="attendance-operations-page">
    <PageHeader title="Attendance Operations" subtitle="Scoped, server-paged exceptions over authoritative daily Attendance." />
    <div className="form-actions"><a className="button button-secondary" href="/attendance/my-exceptions">Employee exception inbox</a><a className="button button-secondary" href="/attendance/monthly-finalization">Period finalization / reopen</a><button className="button button-secondary" type="button" onClick={() => void exportExceptions()}>Export filtered exceptions</button></div>
    {exportError ? <Notice tone="error">{exportError}</Notice> : null}
    <Card title="Operational filters" subtitle="Scope, filtering and paging are enforced by the backend.">
      <div className="form-grid">
        <label className="field"><span>From date</span><input className="input" type="date" value={filters.fromDate} onChange={event => update('fromDate', event.target.value)} /></label>
        <label className="field"><span>To date</span><input className="input" type="date" value={filters.toDate} onChange={event => update('toDate', event.target.value)} /></label>
        <label className="field"><span>Employee search</span><input className="input" value={filters.search} onChange={event => update('search', event.target.value)} /></label>
        <label className="field"><span>Employee ID filter</span><input className="input" value={filters.employeeId} onChange={event => update('employeeId', event.target.value)} /></label>
        <label className="field"><span>Attendance status</span><select className="input" value={filters.attendanceStatus} onChange={event => update('attendanceStatus', event.target.value)}><option value="">All statuses</option><option value="Present">Present</option><option value="Absent">Absent</option><option value="Incomplete">Incomplete</option><option value="OnLeave">Leave</option><option value="OnDuty">On Duty</option></select></label>
        <label className="field"><span>Exception type</span><select className="input" value={filters.exceptionType} onChange={event => update('exceptionType', event.target.value)}><option value="">All exceptions</option><option value="MissingInPunch">Missing IN</option><option value="MissingOutPunch">Missing OUT</option><option value="LateArrival">Late arrival</option><option value="EarlyDeparture">Early departure</option><option value="Absent">Absent</option><option value="Incomplete">Incomplete</option></select></label>
      </div>
      <div className="form-actions"><button className="button button-primary" type="button" onClick={apply}>Apply filters</button></div>
    </Card>
    {dashboard.data ? <div className="summary-grid"><Card title="Employees" subtitle={String(dashboard.data.employees)}><span /></Card><Card title="Processed" subtitle={String(dashboard.data.processedDays)}><span /></Card><Card title="Present" subtitle={String(dashboard.data.presentDays)}><span /></Card><Card title="Absent" subtitle={String(dashboard.data.absentDays)}><span /></Card><Card title="Leave" subtitle={String(dashboard.data.leaveDays)}><span /></Card><Card title="On Duty" subtitle={String(dashboard.data.onDutyDays)}><span /></Card><Card title="Week Off" subtitle={String(dashboard.data.weeklyOffDays)}><span /></Card><Card title="Holiday" subtitle={String(dashboard.data.holidayDays)}><span /></Card><Card title="Exceptions" subtitle={String(dashboard.data.exceptionDays)}><span /></Card><Card title="Pending Regularization" subtitle={String(dashboard.data.pendingRegularizations)}><span /></Card><Card title="Pending On Duty" subtitle={String(dashboard.data.pendingOnDuty)}><span /></Card><Card title="Period state / version" subtitle={`${dashboard.data.openPeriods} open · ${dashboard.data.finalizedPeriods} finalized`}><span /></Card></div> : null}
    {dashboard.data ? <div className="form-actions" aria-label="Dashboard drill-down"><button className="button button-secondary" type="button" onClick={() => drillDown('LateArrival')}>Late ({dashboard.data.lateDays})</button><button className="button button-secondary" type="button" onClick={() => drillDown('EarlyDeparture')}>Early ({dashboard.data.earlyDepartureDays})</button><button className="button button-secondary" type="button" onClick={() => drillDown('Absent')}>Absent ({dashboard.data.activeAbsentExceptions})</button><button className="button button-secondary" type="button" onClick={() => drillDown('MissingInPunch')}>Missing IN ({dashboard.data.missingInPunchExceptions})</button><button className="button button-secondary" type="button" onClick={() => drillDown('MissingOutPunch')}>Missing OUT ({dashboard.data.missingOutPunchExceptions})</button><button className="button button-secondary" type="button" onClick={() => document.getElementById('attendance-correction-inbox')?.scrollIntoView?.({ behavior: 'smooth' })}>Pending corrections ({dashboard.data.pendingCorrections})</button></div> : null}
    <Card title="Exception workbench" subtitle={exceptions.data ? `${exceptions.data.totalCount} matching day(s)` : undefined} isRefreshing={exceptions.isRefreshing}>
      <span id="attendance-exception-workbench" />
      <div className="form-grid" aria-label="Bulk Attendance correction"><label className="field"><span>Correction items (employeeId | YYYY-MM-DD | expected version, one per line; maximum 100)</span><textarea className="input" rows={3} value={bulkInput} onChange={event => setBulkInput(event.target.value)} /></label><label className="field"><span>Correction reason</span><input className="input" value={bulkReason} onChange={event => setBulkReason(event.target.value)} /></label></div>
      <button className="button button-primary" type="button" disabled={!bulkInput.trim() || !bulkReason.trim()} onClick={() => void submitBulk()}>Submit bulk Attendance corrections</button>
      {bulkError ? <Notice tone="error">{bulkError}</Notice> : null}
      {bulkResults.length ? <ul aria-label="Bulk correction results">{bulkResults.map((item, index) => <li key={`${item.employeeId}-${item.businessDate}-${index}`}>{item.businessDate}: {item.success ? 'Success' : `${item.failureCode ?? 'Failed'} — ${item.message}`}{item.currentVersion != null ? ` (current version ${item.currentVersion})` : ''}</li>)}</ul> : null}
      {exceptions.isLoading ? <Spinner label="Loading attendance exceptions" /> : exceptions.error ? <Notice tone="error">{exceptions.error.message}</Notice> : rows.length === 0 ? <EmptyState title="No exceptions match the selected filters." message="Try a wider date range or clear the exception filter." /> : <ExceptionTable rows={rows} onResolve={resolve} />}
      {exceptions.data && exceptions.data.totalPages > 1 ? <div className="pagination" aria-label="Attendance exception pagination"><button className="button button-secondary" type="button" disabled={!exceptions.data.hasPreviousPage} onClick={() => setPage(value => value - 1)}>Previous</button><span>Page {exceptions.data.page} of {exceptions.data.totalPages}</span><button className="button button-secondary" type="button" disabled={!exceptions.data.hasNextPage} onClick={() => setPage(value => value + 1)}>Next</button></div> : null}
    </Card>
    {can(Permissions.attendance.adminCorrectionManage) ? <Card title="Manual Attendance maker request" subtitle="Expected version is checked by the backend; approval reprocesses authoritative Attendance."><div className="form-grid"><label className="field"><span>Employee ID</span><input className="input" value={manual.employeeId} onChange={e => setManual({ ...manual, employeeId: e.target.value })} /></label><label className="field"><span>Work date</span><input className="input" type="date" value={manual.businessDate} onChange={e => setManual({ ...manual, businessDate: e.target.value })} /></label><label className="field"><span>Correction type</span><select className="input" value={manual.correctionType} onChange={e => setManual({ ...manual, correctionType: e.target.value })}><option value="MissingInPunch">Missing IN</option><option value="MissingOutPunch">Missing OUT</option><option value="MissingBothPunches">Missing IN and OUT</option><option value="CorrectInTime">Correct IN</option><option value="CorrectOutTime">Correct OUT</option><option value="CorrectInOutTime">Correct IN/OUT</option></select></label><label className="field"><span>IN (UTC)</span><input className="input" type="time" value={manual.inAt} onChange={e => setManual({ ...manual, inAt: e.target.value })} /></label><label className="field"><span>OUT (UTC)</span><input className="input" type="time" value={manual.outAt} onChange={e => setManual({ ...manual, outAt: e.target.value })} /></label><label className="field"><span>Expected Attendance version</span><input className="input" type="number" min="1" value={manual.version} onChange={e => setManual({ ...manual, version: e.target.value })} /></label><label className="field"><span>Reason</span><input className="input" value={manual.reason} onChange={e => setManual({ ...manual, reason: e.target.value })} /></label><label className="field"><span>Comments</span><input className="input" value={manual.comments} onChange={e => setManual({ ...manual, comments: e.target.value })} /></label></div><button className="button button-primary" type="button" disabled={!manual.employeeId || !manual.reason.trim() || !Number.isInteger(Number(manual.version))} onClick={() => void submitManual()}>Submit manual request</button>{manualMessage ? <Notice tone={manualMessage.includes('failed') || manualMessage.includes('finalized') || manualMessage.includes('conflict') ? 'error' : 'success'}>{manualMessage}</Notice> : null}</Card> : null}
    <Card title="Attendance audit and version history" subtitle="Read-only scoped workflow and audit events."><div className="form-actions"><label className="field"><span>Employee ID</span><input className="input" value={historyEmployeeId} onChange={event => setHistoryEmployeeId(event.target.value)} /></label></div>{history.data ? <ul aria-label="Attendance history">{history.data.items.map(item => <li key={item.id}>{item.businessDate ?? '—'} · {item.action} · version {item.oldAttendanceVersion != null || item.newAttendanceVersion != null ? `${item.oldAttendanceVersion ?? '—'} to ${item.newAttendanceVersion ?? '—'}` : item.attendanceVersion ?? 'not recorded'} · {item.oldValue ?? '—'} to {item.newValue ?? '—'} · actor {item.actorUserId} · {item.reason ?? ''}</li>)}</ul> : null}</Card>
    <Card title="Manager correction inbox" subtitle="Backend-scoped pending Regularization requests."><span id="attendance-correction-inbox" />
      {inbox.error ? <Notice tone="error">{inbox.error.message}</Notice> : <>
        {can(Permissions.attendance.regularizationApprove) ? <div className="form-actions"><button className="button button-primary" type="button" disabled={!selectedItems.length} onClick={() => void reviewSelected(true)}>Bulk approve selected</button><button className="button button-secondary" type="button" disabled={!selectedItems.length} onClick={() => void reviewSelected(false)}>Bulk reject selected</button></div> : null}
        {reviewError ? <Notice tone="error">{reviewError}</Notice> : null}
        {reviewResults.length ? <ul aria-label="Bulk review results">{reviewResults.map((item, index) => <li key={`${item.requestId}-${index}`}>{item.requestId}: {item.success ? 'Success' : `${item.failureCode ?? 'Failed'} — ${item.message}`}</li>)}</ul> : null}
        <ul aria-label="Maker-checker queue">{pending.map(item => { const isMaker = item.events.find(event => event.eventType === 'Submitted')?.actorUserId === user?.id; return <li key={item.id}>{item.businessDate} · {item.requestType} · {item.reason} · {item.status}{can(Permissions.attendance.regularizationApprove) && !isMaker ? <label><input type="checkbox" aria-label={`Select ${item.id}`} checked={selectedRequests.includes(item.id)} onChange={event => setSelectedRequests(current => event.target.checked ? [...current, item.id] : current.filter(id => id !== item.id))} /> Select for review</label> : null}{isMaker ? <span> · Maker cannot approve own request</span> : null}</li> })}</ul>
      </>}
    </Card>
  </div>
}

function ExceptionTable({ rows, onResolve }: { rows: AttendanceOperationalException[]; onResolve: (row: AttendanceOperationalException, action: 'Acknowledge' | 'Waive') => void }) {
  return <div className="table-wrap"><table className="data-table"><caption className="sr-only">Attendance exception workbench</caption><thead><tr><th>Employee</th><th>Date</th><th>Shift</th><th>Exception</th><th>Status</th><th>Scheduled Start</th><th>Scheduled End</th><th>Actual IN</th><th>Actual OUT</th><th>Worked</th><th>Late</th><th>Early</th><th>Version</th><th>Action</th></tr></thead><tbody>{rows.map(row => <tr key={row.id}><td>{row.employeeName}<br />{row.employeeCode ?? '—'}</td><td>{row.businessDate}</td><td>{row.shiftCode ?? '—'}</td><td>{row.exceptionType}</td><td>{row.attendanceStatus}</td><td>{format(row.scheduledStartUtc)}</td><td>{format(row.scheduledEndUtc)}</td><td>{format(row.firstPunchAtUtc)}</td><td>{format(row.lastPunchAtUtc)}</td><td>{row.workedMinutes ?? '—'} min</td><td>{row.lateMinutes} min</td><td>{row.earlyDepartureMinutes} min</td><td>{row.attendanceVersion}</td><td>{row.exceptionType === 'LateArrival' || row.exceptionType === 'EarlyDeparture' ? <><button className="button button-secondary" type="button" onClick={() => onResolve(row, 'Acknowledge')}>Acknowledge</button><button className="button button-secondary" type="button" onClick={() => onResolve(row, 'Waive')}>Waive</button></> : row.exceptionType === 'MissingInPunch' || row.exceptionType === 'MissingOutPunch' || row.exceptionType === 'Incomplete' ? <a className="button button-secondary" href="/attendance/requests">Open correction workflow</a> : row.exceptionType === 'Absent' ? <a className="button button-secondary" href="/attendance/requests">Open Leave / On Duty / correction workflows</a> : '—'}</td></tr>)}</tbody></table></div>
}

function format(value?: string | null) { return value ? value.slice(11, 19) : '—' }
