import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getExitInterviewInbox, type ExitInterviewInboxItem } from '../../api/separation.ts'

export function ExitInterviewHrInboxPage() {
  const [rows, setRows] = useState<ExitInterviewInboxItem[]>([])
  const [status, setStatus] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [total, setTotal] = useState(0)
  const [error, setError] = useState('')
  useEffect(() => { void getExitInterviewInbox({ page, pageSize: 25, status: status || undefined, employeeSearch: search || undefined }).then(value => { setRows(value.items); setTotal(value.totalCount) }).catch(value => setError(value instanceof Error ? value.message : 'Unable to load inbox.')) }, [page, search, status])
  return <section><h1>Exit interview inbox</h1><div><label>Status <select value={status} onChange={event => { setPage(1); setStatus(event.target.value) }}><option value="">All</option><option>NotStarted</option><option>EmployeeSubmitted</option><option>HrInProgress</option><option>Completed</option><option>Reopened</option></select></label> <label>Employee <input value={search} onChange={event => { setPage(1); setSearch(event.target.value) }} /></label></div>{error && <p role="alert">{error}</p>}<table><thead><tr><th>Employee</th><th>Code</th><th>Reason</th><th>Approved LWD</th><th>Status</th><th>Submitted</th><th>Assigned HR</th><th /></tr></thead><tbody>{rows.map(row => <tr key={row.id}><td>{row.employeeName}</td><td>{row.employeeCode}</td><td>{row.separationReason}</td><td>{row.approvedLastWorkingDate}</td><td>{row.status}</td><td>{row.employeeSubmittedAtUtc ? new Date(row.employeeSubmittedAtUtc).toLocaleDateString() : '—'}</td><td>{row.assignedHrUserId ?? 'Unassigned'}</td><td><Link to={`/separation/hr-exit-interviews/${row.employeeSeparationId}`}>Open interview</Link></td></tr>)}</tbody></table><p>Showing {rows.length} of {total}. <button type="button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button> <button type="button" disabled={page * 25 >= total} onClick={() => setPage(value => value + 1)}>Next</button></p></section>
}
