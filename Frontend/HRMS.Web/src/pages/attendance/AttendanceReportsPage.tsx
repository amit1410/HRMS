import { useState } from 'react'
import { Link } from 'react-router-dom'
import {
  exportAttendanceReport,
  getAttendanceExceptionReport,
  getDailyAttendanceReport,
  getMonthlyAttendanceReport,
  type AttendanceDailyReportFilters,
  type AttendanceDailyReportRow,
  type AttendanceExceptionReportFilters,
  type AttendanceExceptionReportRow,
  type AttendanceMonthlyReportFilters,
  type AttendanceMonthlyReportRow,
} from '../../api/attendance.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'

export type AttendanceReportKind = 'daily' | 'monthly' | 'exceptions'
type ReportRow = AttendanceDailyReportRow | AttendanceMonthlyReportRow | AttendanceExceptionReportRow
type ReportPage = { items: ReportRow[]; page: number; pageSize: number; totalCount: number; totalPages: number; hasPreviousPage: boolean; hasNextPage: boolean }

export function AttendanceReportsLandingPage() {
  useDocumentTitle('Attendance Reports')
  const { can } = useAuth()
  return <div className="attendance-reports-page"><PageHeader title="Attendance Reports" subtitle="Read-only, effective Attendance reporting" /><div className="summary-grid report-links"><Link className="card report-link" to="/attendance/reports/daily"><strong>Daily Attendance</strong><span>Effective employee attendance by business date.</span></Link><Link className="card report-link" to="/attendance/reports/monthly"><strong>Monthly Summary</strong><span>Persisted monthly Attendance summaries.</span></Link>{can(Permissions.attendance.exceptionView) ? <Link className="card report-link" to="/attendance/reports/exceptions"><strong>Exceptions</strong><span>Phase 5B Attendance blockers and variances.</span></Link> : null}</div></div>
}

export function AttendanceReportPage({ kind }: { kind: AttendanceReportKind }) {
  useDocumentTitle(kind === 'daily' ? 'Daily Attendance Report' : kind === 'monthly' ? 'Monthly Attendance Report' : 'Attendance Exceptions')
  const { can } = useAuth()
  const [page, setPage] = useState(1)
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)
  const [filters, setFilters] = useState<Record<string, string>>({})
  const [applied, setApplied] = useState<Record<string, string>>({})
  const queryKey = JSON.stringify({ kind, page, applied })
  const query = useApiQuery<ReportPage>((signal) => loadReport(kind, applied, page, signal), [queryKey])
  const rows = query.data?.items ?? []

  const title = kind === 'daily' ? 'Daily Attendance Report' : kind === 'monthly' ? 'Monthly Attendance Report' : 'Attendance Exceptions'
  const exportAllowed = can(Permissions.attendance.reportExport)

  function update(name: string, value: string) { setFilters(current => ({ ...current, [name]: value })) }
  function apply() { setPage(1); setApplied({ ...filters }) }
  function reset() { setFilters({}); setApplied({}); setPage(1) }
  async function exportCsv() {
    setExportError(null); setExporting(true)
    try {
      const result = await exportAttendanceReport(kind, { ...applied } as AttendanceDailyReportFilters | AttendanceMonthlyReportFilters | AttendanceExceptionReportFilters)
      download(result.blob, result.filename ?? `attendance-${kind}.csv`)
    } catch (error) {
      setExportError(error instanceof Error ? error.message : 'The report could not be exported.')
    } finally { setExporting(false) }
  }

  return <div className="attendance-reports-page">
    <PageHeader title={title} subtitle="Report values come from finalized Attendance read models." actions={exportAllowed ? <button className="button button-secondary" type="button" disabled={exporting} onClick={() => { void exportCsv() }}>{exporting ? <Spinner size={14} label="Exporting" /> : 'Export CSV'}</button> : undefined} />
    {exportError ? <Notice tone="error">{exportError}</Notice> : null}
    <Card title="Report filters" subtitle="Filters are applied by the server and reset the report to page 1."><ReportFilters kind={kind} values={filters} onChange={update} onApply={apply} onReset={reset} /></Card>
    <Card title={title} subtitle={query.data ? `${query.data.totalCount} row(s)` : undefined} isRefreshing={query.isRefreshing}>
      {query.isLoading ? <Spinner label={`Loading ${title}`} /> : query.error ? <Notice tone="error">{query.error.message}</Notice> : rows.length === 0 ? <EmptyState title="No attendance records match the selected filters." message="Try a wider range or reset the filters." /> : <ReportTable kind={kind} rows={rows} />}
      {query.data && query.data.totalPages > 1 ? <div className="pagination" aria-label="Report pagination"><button className="button button-secondary" type="button" disabled={!query.data.hasPreviousPage} onClick={() => setPage(value => value - 1)}>Previous</button><span>Page {query.data.page} of {query.data.totalPages} · {query.data.totalCount} rows</span><button className="button button-secondary" type="button" disabled={!query.data.hasNextPage} onClick={() => setPage(value => value + 1)}>Next</button></div> : null}
    </Card>
  </div>
}

function ReportFilters({ kind, values, onChange, onApply, onReset }: { kind: AttendanceReportKind; values: Record<string, string>; onChange: (name: string, value: string) => void; onApply: () => void; onReset: () => void }) {
  return <div className="form-stack"><div className="form-grid">
    {(kind === 'daily' || kind === 'exceptions') && <><label className="field"><span>From Date</span><input className="input" type="date" value={values.fromDate ?? ''} onChange={event => onChange('fromDate', event.target.value)} /></label><label className="field"><span>To Date</span><input className="input" type="date" value={values.toDate ?? ''} onChange={event => onChange('toDate', event.target.value)} /></label></>}
    {kind === 'monthly' && <><label className="field"><span>Year</span><input className="input" inputMode="numeric" value={values.year ?? ''} onChange={event => onChange('year', event.target.value)} /></label><label className="field"><span>Month</span><input className="input" inputMode="numeric" min="1" max="12" value={values.month ?? ''} onChange={event => onChange('month', event.target.value)} /></label><label className="field"><span>Period ID</span><input className="input" value={values.periodId ?? ''} onChange={event => onChange('periodId', event.target.value)} /></label></>}
    {kind === 'exceptions' && <><label className="field"><span>Period ID</span><input className="input" value={values.periodId ?? ''} onChange={event => onChange('periodId', event.target.value)} /></label><label className="field"><span>Exception Type</span><input className="input" value={values.exceptionType ?? ''} onChange={event => onChange('exceptionType', event.target.value)} /></label></>}
    <label className="field"><span>Employee ID</span><input className="input" value={values.employeeId ?? ''} onChange={event => onChange('employeeId', event.target.value)} /></label>
    {kind === 'daily' && <label className="field"><span>Status</span><input className="input" value={values.status ?? ''} onChange={event => onChange('status', event.target.value)} /></label>}
  </div><div className="form-actions"><button className="button button-primary" type="button" onClick={onApply}>Apply Filters</button><button className="button button-secondary" type="button" onClick={onReset}>Reset</button></div></div>
}

function ReportTable({ kind, rows }: { kind: AttendanceReportKind; rows: Array<AttendanceDailyReportRow | AttendanceMonthlyReportRow | AttendanceExceptionReportRow> }) {
  if (kind === 'daily') { const daily = rows as AttendanceDailyReportRow[]; return <div className="table-wrap"><table className="data-table"><caption className="sr-only">Daily Attendance report</caption><thead><tr><th>Employee</th><th>Business Date</th><th>Status</th><th>In</th><th>Out</th><th>Actual Work</th><th>Expected Work</th><th>Department</th><th>Location</th></tr></thead><tbody>{daily.map(row => <tr key={`${row.employeeId}-${row.businessDate}`}><td><strong>{row.employeeName}</strong><br /><span className="muted">{row.employeeCode ?? '—'}</span></td><td>{row.businessDate}</td><td>{row.status}</td><td>{formatDateTime(row.inTimeUtc)}</td><td>{formatDateTime(row.outTimeUtc)}</td><td>{minutes(row.actualWorkMinutes)}</td><td>{minutes(row.expectedWorkMinutes)}</td><td>{row.department ?? '—'}</td><td>{row.workLocation ?? '—'}</td></tr>)}</tbody></table></div> }
  if (kind === 'monthly') { const monthly = rows as AttendanceMonthlyReportRow[]; return <div className="table-wrap"><table className="data-table"><caption className="sr-only">Monthly Attendance report</caption><thead><tr><th>Employee</th><th>Period</th><th>Status</th><th>Working</th><th>Present</th><th>Absent</th><th>Leave</th><th>On Duty</th><th>Actual Work</th><th>Expected Work</th><th>Exceptions</th></tr></thead><tbody>{monthly.map(row => <tr key={`${row.periodId}-${row.employeeCode ?? row.employeeName}`}><td><strong>{row.employeeName}</strong><br /><span className="muted">{row.employeeCode ?? '—'}</span></td><td>{row.year}-{String(row.month).padStart(2, '0')}</td><td>{row.periodStatus}</td><td>{row.workingDays}</td><td>{row.presentDays}</td><td>{row.absentDays}</td><td>{row.onLeaveDays}</td><td>{row.onDutyDays}</td><td>{minutes(row.actualWorkMinutes)}</td><td>{minutes(row.expectedWorkMinutes)}</td><td>{row.exceptionCount}</td></tr>)}</tbody></table></div> }
  const exceptions = rows as AttendanceExceptionReportRow[]; return <div className="table-wrap"><table className="data-table"><caption className="sr-only">Attendance exceptions report</caption><thead><tr><th>Business Date</th><th>Employee</th><th>Exception Type</th><th>Blocking</th><th>Details</th></tr></thead><tbody>{exceptions.map(row => <tr key={row.key}><td>{row.businessDate}</td><td>{row.employeeId}</td><td>{row.exceptionType}</td><td>{row.isBlocking ? 'Yes' : 'No'}</td><td>{row.message}</td></tr>)}</tbody></table></div>
}

function loadReport(kind: AttendanceReportKind, values: Record<string, string>, page: number, _signal: AbortSignal): Promise<ReportPage> {
  const params = { ...values, page, pageSize: 20 }
  if (kind === 'daily') return getDailyAttendanceReport(params as AttendanceDailyReportFilters) as Promise<ReportPage>
  if (kind === 'monthly') return getMonthlyAttendanceReport({ ...params, year: values.year ? Number(values.year) : undefined, month: values.month ? Number(values.month) : undefined } as AttendanceMonthlyReportFilters) as Promise<ReportPage>
  return getAttendanceExceptionReport(params as AttendanceExceptionReportFilters) as Promise<ReportPage>
}

function formatDateTime(value?: string | null): string { return value ? value.slice(11, 19) || value : '—' }
function minutes(value?: number | null): string { return value === null || value === undefined ? '—' : `${value} min` }
function download(blob: Blob, filename: string) { const url = URL.createObjectURL(blob); const anchor = document.createElement('a'); anchor.href = url; anchor.download = filename.replace(/[\\/:*?"<>|]/g, '_'); anchor.click(); URL.revokeObjectURL(url) }
