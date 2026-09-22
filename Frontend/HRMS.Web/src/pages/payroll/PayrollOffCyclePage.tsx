import { useState } from 'react'
import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { approveOffCycleRun, listOffCycleRuns, prepareOffCycleRun, processOffCycleRun } from '../../api/payrollAdjustments.ts'

export function PayrollOffCyclePage() {
  const [refresh, setRefresh] = useState(0)
  const rows = useApiQuery(signal => listOffCycleRuns({ page: 1, pageSize: 50 }, signal), [refresh])
  const action = async (id: string, fn: (value: string) => Promise<unknown>) => { await fn(id); setRefresh(value => value + 1) }
  return <section className="page-shell"><PageHeader title="Off-Cycle Payroll" subtitle="Review, approve, and process explicitly selected adjustment runs." /><Card title="Off-cycle run register"><div className="table-wrap"><table className="data-table"><thead><tr><th>Run</th><th>Type</th><th>Status</th><th>Employees</th><th>Actions</th></tr></thead><tbody>{rows.data?.items.map(row => <tr key={row.id}><td>{row.runNumber}</td><td>{row.runType}</td><td>{row.status}</td><td>{row.employeeCount}</td><td><button type="button" onClick={() => action(row.id, prepareOffCycleRun)}>Prepare</button> <button type="button" onClick={() => action(row.id, approveOffCycleRun)}>Approve</button> <button type="button" onClick={() => action(row.id, processOffCycleRun)}>Process</button></td></tr>)}</tbody></table></div></Card></section>
}
