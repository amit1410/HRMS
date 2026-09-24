import { useEffect, useState } from 'react'
import { executeSeparationExit, getSeparationExitDashboard, retrySeparationExit, type SeparationExitDashboardItem } from '../../api/separationExit'

export function SeparationExitClosurePage() {
  const [rows, setRows] = useState<SeparationExitDashboardItem[]>([])
  const [page, setPage] = useState(1)
  const [message, setMessage] = useState('')
  const [total, setTotal] = useState(0)
  const load = () => getSeparationExitDashboard({ page, pageSize: 25 }).then(value => { setRows(value.items); setTotal(value.totalCount) }).catch(error => setMessage(error instanceof Error ? error.message : 'Unable to load exit closure dashboard'))
  useEffect(() => { void load() }, [page])
  const execute = (row: SeparationExitDashboardItem) => { if (!window.confirm(`Execute final exit for ${row.employeeName} (${row.employeeCode || 'no code'}) on ${row.approvedLastWorkingDate || 'the approved LWD'}? This will deactivate employment and tenant access.`)) return; void executeSeparationExit(row.separationId).then(load).catch(error => setMessage(error instanceof Error ? error.message : 'Exit execution failed')) }
  const retry = (row: SeparationExitDashboardItem) => { void retrySeparationExit(row.separationId).then(load).catch(error => setMessage(error instanceof Error ? error.message : 'Exit retry failed')) }
  return <section><h1>Separation exit closure</h1><p>Final LWD controls the execution date. Execute Exit deactivates employment and tenant access.</p>{message && <p role='status'>{message}</p>}<table><thead><tr><th>Employee</th><th>Final LWD</th><th>Readiness</th><th>Employment</th><th>Execution</th><th>Actions</th></tr></thead><tbody>{rows.map(row => <tr key={row.separationId}><td>{row.employeeName} ({row.employeeCode || '—'})</td><td>{row.approvedLastWorkingDate || '—'}</td><td>{row.isReady ? 'Ready' : `${row.blockerCount} blocker(s)`}</td><td>{row.employmentStatus}</td><td>{row.executionStatus}</td><td>{row.isReady && row.executionStatus === 'NotStarted' && <button type='button' onClick={() => execute(row)}>Execute Exit</button>}{row.executionStatus === 'Failed' && <button type='button' onClick={() => retry(row)}>Retry Exit</button>}</td></tr>)}</tbody></table><p>Showing {rows.length} of {total}. <button type='button' disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button> <button type='button' disabled={page * 25 >= total} onClick={() => setPage(value => value + 1)}>Next</button></p></section>
}
