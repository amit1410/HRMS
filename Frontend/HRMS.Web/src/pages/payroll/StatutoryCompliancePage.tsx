import { useState } from 'react'
import { createPayrollCompliancePeriod, generatePayrollStatutoryReturn, type PayrollStatutoryReturn } from '../../api/payroll.ts'

export function StatutoryCompliancePage() {
  const [periodId, setPeriodId] = useState('')
  const [result, setResult] = useState<PayrollStatutoryReturn | null>(null)
  const [error, setError] = useState<string | null>(null)
  const create = async () => { try { const period = await createPayrollCompliancePeriod({ jurisdictionCode: 'IN', complianceType: 'ProvidentFund', periodStart: new Date().toISOString().slice(0, 8) + '01', periodEnd: new Date().toISOString().slice(0, 10) }); setPeriodId(period.id) } catch (e) { setError(e instanceof Error ? e.message : 'Unable to create compliance period.') } }
  const generate = async () => { try { setResult(await generatePayrollStatutoryReturn(periodId)) } catch (e) { setError(e instanceof Error ? e.message : 'Unable to generate statutory return.') } }
  return <section className="page-shell"><div className="page-heading"><div><p className="eyebrow">Payroll</p><h1>Statutory Compliance</h1><p>Prepare filing-ready registers from persisted statutory payroll results.</p></div></div>{error && <p role="alert">{error}</p>}<div className="card"><div className="button-row"><button type="button" onClick={() => void create()}>Create open period</button><button type="button" onClick={() => void generate()} disabled={!periodId}>Generate return</button></div>{result && <p role="status">{result.batchNumber} · {result.status} · {result.employeeCount} employee(s) · {result.totalPayable.toFixed(2)}</p>}</div></section>
}
