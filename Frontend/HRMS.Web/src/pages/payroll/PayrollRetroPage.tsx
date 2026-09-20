import { useState } from 'react'
import { createPayrollRetroCase, evaluatePayrollRetroCase, type PayrollRetroCase } from '../../api/payroll.ts'

export function PayrollRetroPage() {
  const [caseId, setCaseId] = useState('')
  const [item, setItem] = useState<PayrollRetroCase | null>(null)
  const [error, setError] = useState<string | null>(null)
  const create = async () => { try { const created = await createPayrollRetroCase({ employeeId: caseId, triggerType: 'ManualCorrection', effectiveFrom: new Date().toISOString().slice(0, 10) }); setItem(created); setCaseId(created.id) } catch (e) { setError(e instanceof Error ? e.message : 'Unable to create retro case.') } }
  const evaluate = async () => { try { setItem(await evaluatePayrollRetroCase(caseId)) } catch (e) { setError(e instanceof Error ? e.message : 'Unable to evaluate retro case.') } }
  return <section className="page-shell"><div className="page-heading"><div><p className="eyebrow">Payroll</p><h1>Retro / Arrears</h1><p>Evaluate historical differences without mutating finalized payroll.</p></div></div>{error && <p role="alert">{error}</p>}<div className="card"><label>Employee ID<input value={caseId} onChange={event => setCaseId(event.target.value)} /></label><div className="button-row"><button type="button" onClick={() => void create()} disabled={!caseId}>Create case</button>{item && <button type="button" onClick={() => void evaluate()}>Evaluate</button>}</div>{item && <p role="status">{item.status} · {item.results.length} impacted result(s)</p>}</div></section>
}
