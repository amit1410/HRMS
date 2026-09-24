import { useEffect, useState } from 'react'
import { getSettlementDashboard, initiateSettlement, retrySettlement, type SeparationSettlementDashboardItem } from '../../api/separation.ts'

export function SettlementDashboardPage() {
  const [rows, setRows] = useState<SeparationSettlementDashboardItem[]>([])
  const [status, setStatus] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [total, setTotal] = useState(0)
  const [message, setMessage] = useState('')
  const load = () => void getSettlementDashboard({ page, pageSize: 25, status: status || undefined, employeeSearch: search || undefined }).then(value => { setRows(value.items); setTotal(value.totalCount) }).catch(error => setMessage(error instanceof Error ? error.message : 'Unable to load settlement dashboard.'))
  useEffect(load, [page, search, status])
  const act = (operation: Promise<unknown>) => void operation.then(() => { setMessage('Settlement state updated.'); load() }).catch(error => setMessage(error instanceof Error ? error.message : 'Settlement operation failed.'))
  return <section><h1>Separation settlement readiness</h1><div><label>Status <select value={status} onChange={event => { setPage(1); setStatus(event.target.value) }}><option value="">All</option><option>NotReady</option><option>Initiated</option><option>Failed</option><option>Completed</option></select></label> <label>Employee <input value={search} onChange={event => { setPage(1); setSearch(event.target.value) }} /></label></div>{message && <p role="status">{message}</p>}<table><thead><tr><th>Employee</th><th>Approved LWD</th><th>Readiness</th><th>Payroll settlement</th><th>Blockers</th><th>Closure</th><th>Actions</th></tr></thead><tbody>{rows.map(row => <tr key={row.separationId}><td>{row.employeeName} ({row.employeeCode || '—'})</td><td>{row.approvedLastWorkingDate || '—'}</td><td>{row.isReady ? 'Ready' : row.orchestrationStatus}</td><td>{row.payrollSettlementStatus || 'Not started'}</td><td>{row.blockerCount}</td><td>{row.readyForFinalExitClosure ? 'Ready' : '—'}</td><td>{row.isReady && row.orchestrationStatus === 'NotReady' && <button type="button" onClick={() => act(initiateSettlement(row.separationId, crypto.randomUUID()))}>Initiate</button>}{row.orchestrationStatus === 'Failed' && <button type="button" onClick={() => act(retrySettlement(row.separationId, 'Operator retry'))}>Retry</button>}</td></tr>)}</tbody></table><p>Showing {rows.length} of {total}. <button type="button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button> <button type="button" disabled={page * 25 >= total} onClick={() => setPage(value => value + 1)}>Next</button></p></section>
}
