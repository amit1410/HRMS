import { useState, type FormEvent } from 'react'
import { useAuth } from '../../auth/useAuth.ts'
import { Permissions } from '../../auth/permissions.ts'
import { createSalaryComponent, getSalaryComponentHistory, listSalaryComponents, setSalaryComponentActive, updateSalaryComponent, type SalaryComponent, type SalaryComponentHistory, type SalaryComponentRequest, type SalaryComponentType, type SalaryCalculationType, type SalaryStatutoryType } from '../../api/payroll.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Pagination } from '../../components/Pagination.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'
import { useListQuery } from '../../hooks/useListQuery.ts'

const types: SalaryComponentType[] = ['Earning', 'Deduction', 'EmployerContribution', 'Reimbursement', 'Information']
const calculations: SalaryCalculationType[] = ['FixedAmount', 'Percentage', 'Formula', 'ManualInput', 'AttendanceBased', 'LeaveBased', 'Statutory']
const empty: SalaryComponentRequest = { code: '', name: '', description: '', componentType: 'Earning', calculationType: 'FixedAmount', statutoryType: 'None', isTaxable: false, isStatutory: false, isRecurring: true, affectsGross: true, affectsNetPay: true, displayOrder: 0, effectiveFrom: new Date().toISOString().slice(0, 10), effectiveTo: null, isActive: true }

export function SalaryComponentsPage() {
  useDocumentTitle('Salary Components')
  const { can } = useAuth()
  const list = useListQuery({ sortFields: ['code', 'name'], defaultSortBy: 'code', filterKeys: ['componentType', 'calculationType', 'isActive', 'isStatutory'] })
  const { data, error, isLoading, refetch } = useApiQuery((signal) => listSalaryComponents({ ...list.pagedQuery, ...list.filters }, signal), [list.key])
  const [form, setForm] = useState<SalaryComponentRequest>(empty)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [history, setHistory] = useState<SalaryComponentHistory[] | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const canManage = can(Permissions.payroll.salaryComponentManage)

  async function save(event: FormEvent) {
    event.preventDefault(); setFormError(null)
    try { if (editingId) await updateSalaryComponent(editingId, form); else await createSalaryComponent(form); setMessage(editingId ? 'Salary component updated.' : 'Salary component created.'); setEditingId(null); setForm({ ...empty }); refetch() } catch (e) { setFormError(e instanceof Error ? e.message : 'Unable to save salary component.') }
  }
  function edit(row: SalaryComponent) {
    setEditingId(row.id)
    setForm({ code: row.code, name: row.name, description: row.description ?? '', componentType: row.componentType, calculationType: row.calculationType, statutoryType: row.statutoryType, isTaxable: row.isTaxable, isStatutory: row.isStatutory, isRecurring: row.isRecurring, affectsGross: row.affectsGross, affectsNetPay: row.affectsNetPay, displayOrder: row.displayOrder, effectiveFrom: row.effectiveFrom, effectiveTo: row.effectiveTo ?? null, isActive: row.isActive, expectedConcurrencyVersion: row.concurrencyVersion })
  }
  async function toggle(row: SalaryComponent) {
    try { await setSalaryComponentActive(row.id, !row.isActive, row.concurrencyVersion); setMessage(row.isActive ? 'Salary component deactivated.' : 'Salary component activated.'); refetch() } catch (e) { setFormError(e instanceof Error ? e.message : 'Unable to update salary component.') }
  }
  async function showHistory(id: string) {
    try { setHistory(await getSalaryComponentHistory(id)) } catch (e) { setFormError(e instanceof Error ? e.message : 'Unable to load history.') }
  }

  return <>
    <PageHeader title="Salary Components" subtitle="Configure reusable earnings, deductions, contributions and payroll metadata." />
    {message && <Notice tone="success" onDismiss={() => setMessage(null)}>{message}</Notice>}
    {error && <Notice tone="error">Unable to load salary components.</Notice>}
    {canManage && <Card><form className="form-grid" onSubmit={save}><h2 className="section-title">{editingId ? 'Edit Salary Component' : 'Add Salary Component'}</h2><label>Code<input className="input" required value={form.code} onChange={e => setForm({ ...form, code: e.target.value })} /></label><label>Name<input className="input" required value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} /></label><label>Component type<select className="input select" value={form.componentType} onChange={e => setForm({ ...form, componentType: e.target.value as SalaryComponentType })}>{types.map(x => <option key={x}>{x}</option>)}</select></label><label>Calculation<select className="input select" value={form.calculationType} onChange={e => setForm({ ...form, calculationType: e.target.value as SalaryCalculationType })}>{calculations.map(x => <option key={x}>{x}</option>)}</select></label><label>Effective from<input className="input" type="date" required value={form.effectiveFrom} onChange={e => setForm({ ...form, effectiveFrom: e.target.value })} /></label><label>Effective to<input className="input" type="date" value={form.effectiveTo ?? ''} onChange={e => setForm({ ...form, effectiveTo: e.target.value || null })} /></label><label>Description<input className="input" value={form.description ?? ''} onChange={e => setForm({ ...form, description: e.target.value })} /></label><label className="checkbox"><input type="checkbox" checked={form.isStatutory} onChange={e => setForm({ ...form, isStatutory: e.target.checked, statutoryType: e.target.checked ? 'ProvidentFund' as SalaryStatutoryType : 'None' })} /> Statutory</label><label className="checkbox"><input type="checkbox" checked={form.affectsGross} onChange={e => setForm({ ...form, affectsGross: e.target.checked })} /> Affects gross</label><label className="checkbox"><input type="checkbox" checked={form.affectsNetPay} onChange={e => setForm({ ...form, affectsNetPay: e.target.checked })} /> Affects net pay</label>{formError && <Notice tone="error">{formError}</Notice>}<div><button className="button button-primary" type="submit">{editingId ? 'Save changes' : 'Create component'}</button>{editingId && <button className="button button-secondary" type="button" onClick={() => { setEditingId(null); setForm({ ...empty }) }}>Cancel</button>}</div></form></Card>}
    <Card isRefreshing={isLoading}><div className="toolbar"><input className="input" placeholder="Search code or name" value={list.search} onChange={e => list.setSearch(e.target.value)} /><select className="input select" aria-label="Component Type" value={list.filters.componentType} onChange={e => list.setFilter('componentType', e.target.value)}><option value="">All types</option>{types.map(x => <option key={x}>{x}</option>)}</select><select className="input select" aria-label="Calculation Type" value={list.filters.calculationType} onChange={e => list.setFilter('calculationType', e.target.value)}><option value="">All calculations</option>{calculations.map(x => <option key={x}>{x}</option>)}</select><select className="input select" aria-label="Status" value={list.filters.isActive} onChange={e => list.setFilter('isActive', e.target.value)}><option value="">All statuses</option><option value="true">Active</option><option value="false">Inactive</option></select><select className="input select" aria-label="Statutory" value={list.filters.isStatutory} onChange={e => list.setFilter('isStatutory', e.target.value)}><option value="">All statutory states</option><option value="true">Statutory</option><option value="false">Non-statutory</option></select></div><div className="table-wrap"><table className="data-table"><thead><tr><th>Code</th><th>Name</th><th>Type</th><th>Calculation</th><th>Taxable</th><th>Statutory</th><th>Status</th><th>Actions</th></tr></thead><tbody>{data?.items.map(row => <tr key={row.id}><td>{row.code}</td><td>{row.name}</td><td>{row.componentType}</td><td>{row.calculationType}</td><td>{row.isTaxable ? 'Yes' : 'No'}</td><td>{row.isStatutory ? row.statutoryType : 'No'}</td><td>{row.isActive ? 'Active' : 'Inactive'}</td><td>{canManage && <button className="row-action" type="button" onClick={() => edit(row)}>Edit</button>} {canManage && <button className="row-action" type="button" onClick={() => toggle(row)}>{row.isActive ? 'Deactivate' : 'Activate'}</button>} {can(Permissions.payroll.salaryComponentViewHistory) && <button className="row-action" type="button" onClick={() => showHistory(row.id)}>History</button>}</td></tr>)}</tbody></table></div>{data && <Pagination info={data} onPageChange={list.setPage} disabled={isLoading} />}</Card>
    {history && <Card><h2 className="section-title">Component History</h2><button className="button button-secondary" type="button" onClick={() => setHistory(null)}>Close</button><ul>{history.map(item => <li key={item.id}>{item.changeType}: {item.code} — {item.changedAtUtc}</li>)}</ul></Card>}
  </>
}
