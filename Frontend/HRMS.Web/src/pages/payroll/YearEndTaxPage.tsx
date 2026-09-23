import { useState } from 'react'
import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Permissions } from '../../auth/permissions.ts'
import { approveYearEndTaxRun, calculateYearEndTaxRun, closeYearEndTaxRun, createYearEndTaxRun, getYearEndTaxEmployees, listYearEndTaxRuns, submitYearEndTaxRun } from '../../api/yearEndTax.ts'

type Tab = 'runs' | 'employees' | 'previous' | 'exceptions'

export function YearEndTaxPage() {
  const { can } = useAuth()
  const [tab, setTab] = useState<Tab>('runs')
  const [selectedRun, setSelectedRun] = useState('')
  const [taxYear, setTaxYear] = useState(new Date().getFullYear())
  const [refresh, setRefresh] = useState(0)
  const runs = useApiQuery(signal => listYearEndTaxRuns({ page: 1, pageSize: 50 }, signal), [refresh])
  const employees = useApiQuery(signal => selectedRun ? getYearEndTaxEmployees(selectedRun, { page: 1, pageSize: 50 }, signal) : Promise.resolve(undefined), [selectedRun, refresh])
  const reload = () => setRefresh(value => value + 1)
  const create = async () => { if (!can(Permissions.payroll.yearEndTaxManage)) return; const result = await createYearEndTaxRun({ taxYear, startDate: `${taxYear}-04-01`, endDate: `${taxYear + 1}-03-31`, taxYearCode: `${taxYear}-${String(taxYear + 1).slice(-2)}` }); if (result) setSelectedRun(result.id); reload() }
  const action = async (operation: (id: string) => Promise<unknown>) => { if (selectedRun) { await operation(selectedRun); reload() } }
  return <section className="page-shell"><PageHeader title="Year-End Tax" subtitle="Reconcile finalized payroll, approved declarations and controlled previous-employer inputs without rewriting historical payroll." />
    <nav aria-label="Year-end tax sections" className="tabs"><button type="button" aria-current={tab === 'runs'} onClick={() => setTab('runs')}>Tax Year Runs</button><button type="button" aria-current={tab === 'employees'} onClick={() => setTab('employees')}>Employee Reconciliation</button><button type="button" aria-current={tab === 'previous'} onClick={() => setTab('previous')}>Previous Employer Inputs</button><button type="button" aria-current={tab === 'exceptions'} onClick={() => setTab('exceptions')}>Exceptions</button></nav>
    {tab === 'runs' && <Card title="Create year-end run"><div className="form-grid"><label>Tax year <input type="number" value={taxYear} onChange={event => setTaxYear(Number(event.target.value))} /></label><button type="button" onClick={() => void create()} disabled={!can(Permissions.payroll.yearEndTaxManage)}>Create draft</button></div></Card>}
    {(tab === 'runs' || tab === 'exceptions') && <Card title="Tax year runs"><div className="table-wrap"><table className="data-table"><thead><tr><th>Tax year</th><th>Status</th><th>Employees</th><th>Blocking issues</th><th>Tax due</th><th>Excess</th><th>Actions</th></tr></thead><tbody>{runs.data?.items.map(run => <tr key={run.id}><td>{run.taxYearCode}</td><td>{run.status}</td><td>{run.employeeCount}</td><td>{run.blockingIssueCount}</td><td>{run.totalTaxDue.toFixed(2)}</td><td>{run.totalExcessTax.toFixed(2)}</td><td><button type="button" onClick={() => { setSelectedRun(run.id); setTab('employees') }}>Inspect</button>{run.status === 'Draft' && can(Permissions.payroll.yearEndTaxCalculate) && <button type="button" onClick={() => void action(calculateYearEndTaxRun)}>Calculate</button>}{run.status === 'Calculated' && can(Permissions.payroll.yearEndTaxSubmit) && <button type="button" onClick={() => void action(submitYearEndTaxRun)}>Submit</button>}{run.status === 'Submitted' && can(Permissions.payroll.yearEndTaxApprove) && <button type="button" onClick={() => void action(approveYearEndTaxRun)}>Approve</button>}{run.status === 'Approved' && can(Permissions.payroll.yearEndTaxClose) && <button type="button" onClick={() => void action(closeYearEndTaxRun)}>Close</button>}</td></tr>)}</tbody></table></div></Card>}
    {tab === 'employees' && <Card title="Employee reconciliation"><p>Finalized payroll is the authoritative YTD source. Estimated tax due is a reconciliation result, not final payroll net pay.</p><div className="table-wrap"><table className="data-table"><thead><tr><th>Employee</th><th>YTD taxable</th><th>Tax deducted</th><th>Projected tax</th><th>Due</th><th>Excess</th><th>Status</th></tr></thead><tbody>{employees.data?.items.map(row => <tr key={row.id}><td>{row.employeeId}</td><td>{row.ytdTaxableIncome.toFixed(2)}</td><td>{row.ytdTaxDeducted.toFixed(2)}</td><td>{row.projectedAnnualTax.toFixed(2)}</td><td>{row.estimatedTaxDue.toFixed(2)}</td><td>{row.estimatedExcessTax.toFixed(2)}</td><td>{row.status}{row.blockingIssueMessage ? ` — ${row.blockingIssueMessage}` : ''}</td></tr>)}</tbody></table></div></Card>}
    {tab === 'previous' && <Card title="Previous-employer inputs"><p>Controlled, tenant-scoped inputs are reviewed through the same year-end run and approval boundary.</p><p>Only approved records affect reconciliation; closed runs are immutable.</p></Card>}
  </section>
}
