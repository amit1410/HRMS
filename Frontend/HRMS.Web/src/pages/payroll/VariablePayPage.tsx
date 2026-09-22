import { useState } from 'react'
import type { FormEvent } from 'react'
import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { createVariablePayPlan, listMyVariablePay, listVariablePayAwards, listVariablePayPlans } from '../../api/variablePay.ts'

const money = (value: number) => value.toFixed(2)

export function VariablePayPage() {
  const [refresh, setRefresh] = useState(0)
  const [form, setForm] = useState({ code: '', name: '', planType: 'FixedBonus' })
  const plans = useApiQuery(signal => listVariablePayPlans(signal), [refresh])
  const awards = useApiQuery(signal => listVariablePayAwards({ page: 1, pageSize: 50 }, signal), [])
  const save = async (event: FormEvent) => { event.preventDefault(); await createVariablePayPlan(form); setForm({ code: '', name: '', planType: 'FixedBonus' }); setRefresh(value => value + 1) }
  return <section className="page-shell"><PageHeader title="Variable Pay" subtitle="Configure policy-driven bonuses and track auditable award settlement." /><Card title="Plans"><form className="form-grid" onSubmit={save}><input aria-label="Plan code" required value={form.code} onChange={e => setForm({ ...form, code: e.target.value })} placeholder="Plan code" /><input aria-label="Plan name" required value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} placeholder="Plan name" /><select aria-label="Plan type" value={form.planType} onChange={e => setForm({ ...form, planType: e.target.value })}><option>FixedBonus</option><option>PercentageOfSalary</option><option>TargetVariablePay</option><option>PerformanceLinked</option><option>OneTimeAward</option><option>DiscretionaryAward</option></select><button type="submit">Create plan</button></form><ul>{plans.data?.map(plan => <li key={plan.id}>{plan.code} — {plan.name} — {plan.planType} — {plan.isActive ? 'Active' : 'Inactive'}</li>)}</ul></Card><Card title="Award register"><div className="table-wrap"><table className="data-table"><thead><tr><th>Award</th><th>Period</th><th>Calculated</th><th>Approved</th><th>Paid</th><th>Outstanding</th><th>Status</th></tr></thead><tbody>{awards.data?.items.map(award => <tr key={award.id}><td>{award.awardNumber}</td><td>{award.awardPeriodFrom} – {award.awardPeriodTo}</td><td>{money(award.calculatedAmount)}</td><td>{money(award.approvedAmount ?? 0)}</td><td>{money(award.settledAmount)}</td><td>{money(award.outstandingAmount)}</td><td>{award.status}</td></tr>)}</tbody></table></div></Card></section>
}

export function MyVariablePayPage() {
  const awards = useApiQuery(signal => listMyVariablePay(signal), [])
  return <section className="page-shell"><PageHeader title="My Variable Pay" subtitle="View variable-pay awards linked to your employee identity." /><Card title="Awards"><ul>{awards.data?.map(award => <li key={award.id}>{award.awardNumber} — {money(award.approvedAmount ?? award.calculatedAmount)} — {award.status}</li>)}</ul>{awards.data?.length === 0 && <p>No variable-pay awards are available.</p>}</Card></section>
}
