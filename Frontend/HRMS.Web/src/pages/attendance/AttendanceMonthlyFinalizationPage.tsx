import { useState } from 'react'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Pagination } from '../../components/Pagination.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Permissions } from '../../auth/permissions.ts'
import { closeAttendancePeriod, getAttendanceClosePreview, listAttendancePeriodEvents, listAttendancePeriods, listAttendanceSummaries, processAttendancePeriod, reopenAttendancePeriod } from '../../api/attendance.ts'

const money = (value: number) => value.toFixed(2)

export function AttendanceMonthlyFinalizationPage() {
  const { can } = useAuth()
  const [selectedId, setSelectedId] = useState<string>()
  const [page, setPage] = useState(1)
  const [lopOnly, setLopOnly] = useState(false)
  const [message, setMessage] = useState<string>()
  const [error, setError] = useState<string>()
  const [reason, setReason] = useState('')
  const periods = useApiQuery(() => listAttendancePeriods({ page: 1, pageSize: 50 }), [])
  const selected = periods.data?.items.find(x => x.id === selectedId) ?? periods.data?.items[0]
  const preview = useApiQuery(() => selected ? getAttendanceClosePreview(selected.id) : Promise.resolve(undefined), [selected?.id, selected?.status])
  const summaries = useApiQuery(() => selected ? listAttendanceSummaries(selected.id, { page, pageSize: 100, hasLop: lopOnly || undefined }) : Promise.resolve(undefined), [selected?.id, page, lopOnly, selected?.status])
  const events = useApiQuery(() => selected ? listAttendancePeriodEvents(selected.id) : Promise.resolve([]), [selected?.id, selected?.status])
  const run = async (action: () => Promise<unknown>, success: string) => { try { setError(undefined); await action(); setMessage(success); void periods.refetch(); void preview.refetch(); void summaries.refetch(); void events.refetch() } catch (e) { setError(e instanceof Error ? e.message : 'Attendance action failed.') } }

  return <section className="page-shell"><PageHeader title="Monthly Attendance Finalization" subtitle="Server-calculated attendance summaries become the immutable Payroll input only after finalization." />
    {message ? <Notice tone="success" onDismiss={() => setMessage(undefined)}>{message}</Notice> : null}{error ? <Notice tone="error">{error}</Notice> : null}
    <Card title="Attendance periods" isRefreshing={periods.isLoading}><div className="table-wrap"><table className="data-table"><thead><tr><th>Period</th><th>Status</th><th>Version</th><th>Action</th></tr></thead><tbody>{periods.data?.items.map(period => <tr key={period.id}><td>{period.year}-{String(period.month).padStart(2, '0')}</td><td>{period.status}</td><td>{period.dataVersion}</td><td><button className="row-action" type="button" onClick={() => { setSelectedId(period.id); setPage(1) }}>Open</button></td></tr>)}</tbody></table></div>{periods.data ? <Pagination info={periods.data} onPageChange={() => undefined} /> : null}</Card>
    {selected ? <><Card title={`${selected.year}-${String(selected.month).padStart(2, '0')} · ${selected.status}`}><div className="summary-grid"><span>Current version <strong>{selected.dataVersion}</strong></span><span>Processed <strong>{preview.data?.employeesProcessed ?? '—'}</strong></span><span>Blocked <strong>{preview.data?.blockingExceptionCount ?? '—'}</strong></span><span>Ready <strong>{preview.data?.canClose ? 'Yes' : 'No'}</strong></span></div>{preview.data?.blockers.length ? <div role="alert"><strong>Blockers</strong><ul>{preview.data.blockers.map(blocker => <li key={blocker}>{blocker}</li>)}</ul></div> : null}<div className="form-actions">{can(Permissions.attendance.monthlyProcess) && selected.status !== 'Closed' ? <button className="button button-secondary" type="button" onClick={() => void run(() => processAttendancePeriod(selected.id), 'Attendance processed.')}>Process / Preview</button> : null}{can(Permissions.attendance.monthlyClose) && selected.status !== 'Closed' ? <button className="button button-primary" type="button" disabled={!preview.data?.canClose} onClick={() => void run(() => closeAttendancePeriod(selected.id), 'Attendance period finalized.')}>Finalize</button> : null}{can(Permissions.attendance.monthlyReopen) && selected.status === 'Closed' ? <><input className="input" aria-label="Reopen reason" placeholder="Reason for reopen" value={reason} onChange={e => setReason(e.target.value)} /><button className="button button-secondary" type="button" disabled={!reason.trim()} onClick={() => void run(() => reopenAttendancePeriod(selected.id, reason), 'Attendance period reopened.')}>Reopen</button></> : null}</div></Card>
      <Card title="Finalization preview" subtitle="All values below come from the server; the browser performs no attendance or payroll calculation."><label className="checkbox-label"><input type="checkbox" checked={lopOnly} onChange={e => { setLopOnly(e.target.checked); setPage(1) }} /> LOP greater than zero</label><div className="table-wrap"><table className="data-table"><thead><tr><th>Employee</th><th>Eligible</th><th>Present</th><th>Absent</th><th>Paid leave</th><th>Unpaid leave</th><th>Holiday</th><th>Week off</th><th>On duty</th><th>Payable</th><th>LOP</th></tr></thead><tbody>{summaries.data?.items.map(row => <tr key={row.id}><td>{row.employeeCode ?? '—'} — {row.employeeName}</td><td>{row.employmentDays}</td><td>{money(row.presentDayQuantity)}</td><td>{row.absentDays}</td><td>{money(row.paidLeaveDays)}</td><td>{money(row.unpaidLeaveDays)}</td><td>{row.holidayDays}</td><td>{row.weeklyOffDays}</td><td>{row.onDutyDays}</td><td>{money(row.payableDays)}</td><td>{money(row.lopDays)}</td></tr>)}</tbody></table></div>{summaries.data ? <Pagination info={summaries.data} onPageChange={setPage} disabled={summaries.isLoading} /> : null}</Card>
      <Card title="Version history"><p>Current finalized version: {selected.status === 'Closed' ? selected.dataVersion : 'not finalized'}. {selected.dataVersion > 1 ? `Previous finalized version: ${selected.dataVersion - 1}.` : 'No previous finalized version is recorded.'} Payroll historical snapshots are not automatically recalculated by re-finalization.</p><ul>{events.data?.map(event => <li key={event.id}>{event.eventType} · version {event.dataVersion} · {event.occurredAtUtc}{event.details ? ` · ${event.details}` : ''}</li>)}</ul></Card></> : <Card title="Finalization preview"><p>Select an Attendance period to inspect its server-calculated preview.</p></Card>}
  </section>
}
