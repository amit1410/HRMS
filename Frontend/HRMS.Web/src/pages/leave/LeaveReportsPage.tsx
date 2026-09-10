import { useMemo, useState } from 'react'
import { exportLeaveReport, getLeaveReport, saveFile, type LeaveReportKind, type LeaveReportQuery, type ReportPage } from '../../api/leaveReports.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'

const reports: { value: LeaveReportKind; label: string; paged: boolean }[] = [
  { value: 'requests', label: 'Leave Requests', paged: true },
  { value: 'balances', label: 'Leave Balances', paged: true },
  { value: 'usage', label: 'Leave Utilization', paged: false },
  { value: 'accounting', label: 'Accrual / Carry Forward / Expiry', paged: true },
  { value: 'pending', label: 'Pending Approval Aging', paged: true },
  { value: 'organization', label: 'Department / WorkLocation Summary', paged: false },
  { value: 'calendar', label: 'Holiday / Working Calendar', paged: false },
]

function today() { return new Date().toISOString().slice(0, 10) }

export function LeaveReportsPage() {
  useDocumentTitle('Leave Reports')
  const { can } = useAuth()
  const [kind, setKind] = useState<LeaveReportKind>('requests')
  const [fromDate, setFromDate] = useState('')
  const [toDate, setToDate] = useState('')
  const [organizationDimension, setOrganizationDimension] = useState<'department' | 'workLocation'>('department')
  const [page, setPage] = useState(1)
  const [run, setRun] = useState<LeaveReportQuery>({})
  const reportMeta = reports.find(x => x.value === kind)!
  const query = useApiQuery(() => getLeaveReport(kind, { ...run, page, pageSize: 25 }), [kind, run, page])
  const pageData = query.data && !Array.isArray(query.data) ? query.data as ReportPage : null
  const rows = Array.isArray(query.data) ? query.data : pageData?.items ?? []
  const columns = useMemo(() => rows.length && rows[0] ? Object.keys(rows[0]) : [], [rows])

  function runReport() { setPage(1); setRun({ fromDate: fromDate || undefined, toDate: toDate || undefined, organizationDimension: kind === 'organization' ? organizationDimension : undefined }) }
  function reset() { setFromDate(''); setToDate(''); setOrganizationDimension('department'); setPage(1); setRun({}) }
  async function exportReport() { saveFile(await exportLeaveReport(kind, run)) }

  if (!can(Permissions.leave.reportsView)) return <Card><EmptyState title="Reports are not available" message="You do not have permission to view tenant-wide Leave reports." /></Card>
  return <div className="leave-admin-page leave-reports-page">
    <PageHeader title="Leave Reports" subtitle="Read-only, tenant-wide Leave reporting" actions={can(Permissions.leave.reportsExport) ? <button className="button button-secondary" type="button" onClick={exportReport}>Export CSV</button> : undefined} />
    <Card title="Report filters" subtitle="Interactive reports use stored Leave quantities and current Leave Period defaults.">
      <div className="form-grid"><label>Report<select value={kind} onChange={event => { setKind(event.target.value as LeaveReportKind); setPage(1) }}>{reports.map(item => <option value={item.value} key={item.value}>{item.label}</option>)}</select></label>{kind === 'organization' && <label>Group by<select value={organizationDimension} onChange={event => setOrganizationDimension(event.target.value as 'department' | 'workLocation')}><option value="department">Department</option><option value="workLocation">WorkLocation</option></select></label>}<label>From date<input type="date" value={fromDate} max={toDate || undefined} onChange={event => setFromDate(event.target.value)} /></label><label>To date<input type="date" value={toDate} min={fromDate || undefined} onChange={event => setToDate(event.target.value)} /></label></div>
      <div className="form-actions"><button className="button" type="button" onClick={runReport}>Run Report</button><button className="button button-secondary" type="button" onClick={reset}>Reset Filters</button><span className="muted">Today: {today()}</span></div>
    </Card>
    <Card title={reportMeta.label} subtitle={pageData ? `${pageData.totalCount} row(s)` : `${rows.length} result(s)`}>
      {query.isLoading ? <Spinner label="Loading Leave report" /> : query.error ? <Notice tone="error">{query.error.message}</Notice> : rows.length === 0 ? <EmptyState title="No report rows" message="Try a wider date range or reset the filters." /> : <><div className="table-scroll"><table className="data-table"><thead><tr>{columns.map(column => <th key={column}>{column.replace(/[A-Z]/g, letter => ` ${letter}`).replace(/^./, letter => letter.toUpperCase())}</th>)}</tr></thead><tbody>{rows.map((row, index) => <tr key={String(row.requestId ?? row.employeeId ?? row.id ?? index)}>{columns.map(column => <td key={column}>{formatValue(row[column])}</td>)}</tr>)}</tbody></table></div>{pageData && <div className="pagination"><button className="button button-secondary" type="button" disabled={!pageData.hasPreviousPage} onClick={() => setPage(value => value - 1)}>Previous</button><span>Page {pageData.page} of {pageData.totalPages}</span><button className="button button-secondary" type="button" disabled={!pageData.hasNextPage} onClick={() => setPage(value => value + 1)}>Next</button></div>}</>}
    </Card>
  </div>
}

function formatValue(value: unknown): string { if (value === null || value === undefined) return '—'; if (typeof value === 'object') return JSON.stringify(value); return String(value) }
