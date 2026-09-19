import { useState, type FormEvent } from 'react'
import { useAuth } from '../../auth/useAuth.ts'
import { Permissions } from '../../auth/permissions.ts'
import { createSalaryStructure, getSalaryStructure, getSalaryStructureHistory, listSalaryComponents, listSalaryStructures, setSalaryStructureActive, updateSalaryStructure, type SalaryStructure, type SalaryStructureComponentRequest, type SalaryStructureCalculationType, type SalaryStructureHistory, type SalaryStructureRequest } from '../../api/payroll.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Pagination } from '../../components/Pagination.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'
import { useListQuery } from '../../hooks/useListQuery.ts'

const calculations: SalaryStructureCalculationType[] = ['FixedAmount', 'Percentage', 'Formula', 'Manual']
const today = () => new Date().toISOString().slice(0, 10)
const blankComponent = (sequence: number): SalaryStructureComponentRequest => ({ salaryComponentId: '', sequence, calculationType: 'FixedAmount', value: 0, percentageOfComponentId: null, formula: '', isProratable: true, isEditableAtEmployeeLevel: false, minimumAmount: null, maximumAmount: null, isActive: true, effectiveFrom: null, effectiveTo: null })
const empty = (): SalaryStructureRequest => ({ code: '', name: '', description: '', effectiveFrom: today(), effectiveTo: null, isActive: true, components: [blankComponent(1)] })

export function SalaryStructuresPage() {
  useDocumentTitle('Salary Structures')
  const { can } = useAuth()
  const list = useListQuery({ sortFields: ['code', 'name'], defaultSortBy: 'code', filterKeys: ['isActive'] })
  const { data, error, isLoading, refetch } = useApiQuery((signal) => listSalaryStructures({ ...list.pagedQuery, ...list.filters }, signal), [list.key])
  const componentsQuery = useApiQuery((signal) => listSalaryComponents({ page: 1, pageSize: 100, isActive: true }, signal), [])
  const [form, setForm] = useState<SalaryStructureRequest>(empty)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [history, setHistory] = useState<SalaryStructureHistory[] | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const canManage = can(Permissions.payroll.salaryStructureManage)
  const components = componentsQuery.data?.items ?? []

  async function save(event: FormEvent) {
    event.preventDefault(); setFormError(null)
    if (!form.components.length || form.components.some(row => !row.salaryComponentId)) { setFormError('Add a Salary Component to every structure row.'); return }
    try {
      if (editingId) await updateSalaryStructure(editingId, form)
      else await createSalaryStructure(form)
      setMessage(editingId ? 'Salary structure updated.' : 'Salary structure created.')
      setEditingId(null); setForm(empty()); refetch()
    } catch (e) { setFormError(e instanceof Error ? e.message : 'Unable to save salary structure.') }
  }

  async function edit(row: SalaryStructure) {
    try {
      const detail = await getSalaryStructure(row.id)
      setEditingId(detail.id)
      setForm({ code: detail.code, name: detail.name, description: detail.description ?? '', effectiveFrom: detail.effectiveFrom, effectiveTo: detail.effectiveTo ?? null, isActive: detail.isActive, expectedConcurrencyVersion: detail.concurrencyVersion, components: detail.components.map(item => ({ salaryComponentId: item.salaryComponentId, sequence: item.sequence, calculationType: item.calculationType, value: item.value ?? null, percentageOfComponentId: item.percentageOfComponentId ?? null, formula: item.formula ?? '', isProratable: item.isProratable, isEditableAtEmployeeLevel: item.isEditableAtEmployeeLevel, minimumAmount: item.minimumAmount ?? null, maximumAmount: item.maximumAmount ?? null, isActive: item.isActive, effectiveFrom: item.effectiveFrom, effectiveTo: item.effectiveTo ?? null })) })
    } catch (e) { setFormError(e instanceof Error ? e.message : 'Unable to load salary structure.') }
  }

  async function toggle(row: SalaryStructure) {
    try { await setSalaryStructureActive(row.id, !row.isActive, row.concurrencyVersion); setMessage(row.isActive ? 'Salary structure deactivated.' : 'Salary structure activated.'); refetch() } catch (e) { setFormError(e instanceof Error ? e.message : 'Unable to update salary structure.') }
  }

  async function showHistory(id: string) {
    try { setHistory(await getSalaryStructureHistory(id)) } catch (e) { setFormError(e instanceof Error ? e.message : 'Unable to load history.') }
  }

  function updateComponent(index: number, change: Partial<SalaryStructureComponentRequest>) {
    setForm({ ...form, components: form.components.map((row, i) => i === index ? { ...row, ...change } : row) })
  }

  return <>
    <PageHeader title="Salary Structures" subtitle="Compose reusable, effective-dated salary structures from Salary Components." />
    {message && <Notice tone="success" onDismiss={() => setMessage(null)}>{message}</Notice>}
    {error && <Notice tone="error">Unable to load salary structures.</Notice>}
    {componentsQuery.error && <Notice tone="error">Unable to load active Salary Components.</Notice>}
    {canManage && <Card><form className="form-grid" onSubmit={save}><h2 className="section-title">{editingId ? 'Edit Salary Structure' : 'Add Salary Structure'}</h2><label>Code<input className="input" required value={form.code} onChange={e => setForm({ ...form, code: e.target.value })} /></label><label>Name<input className="input" required value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} /></label><label>Effective from<input className="input" type="date" required value={form.effectiveFrom} onChange={e => setForm({ ...form, effectiveFrom: e.target.value })} /></label><label>Effective to<input className="input" type="date" value={form.effectiveTo ?? ''} onChange={e => setForm({ ...form, effectiveTo: e.target.value || null })} /></label><label>Description<input className="input" value={form.description ?? ''} onChange={e => setForm({ ...form, description: e.target.value })} /></label><label className="checkbox"><input type="checkbox" checked={form.isActive} onChange={e => setForm({ ...form, isActive: e.target.checked })} /> Active</label>
      <div className="table-wrap"><table className="data-table"><thead><tr><th>Component</th><th>Calculation</th><th>Value / %</th><th>Base</th><th>Formula</th><th>Proratable</th><th>Editable</th><th>Sequence</th><th /></tr></thead><tbody>{form.components.map((row, index) => <tr key={`${index}-${row.salaryComponentId}`}><td><select className="input select" aria-label={`Salary Component ${index + 1}`} value={row.salaryComponentId} onChange={e => updateComponent(index, { salaryComponentId: e.target.value })}><option value="">Select component</option>{components.map(component => <option key={component.id} value={component.id}>{component.code} — {component.name}</option>)}</select></td><td><select className="input select" value={row.calculationType} onChange={e => updateComponent(index, { calculationType: e.target.value as SalaryStructureCalculationType, value: e.target.value === 'Formula' || e.target.value === 'Manual' ? null : row.value, formula: e.target.value === 'Formula' ? row.formula : '' })}>{calculations.map(value => <option key={value}>{value}</option>)}</select></td><td>{row.calculationType === 'Formula' || row.calculationType === 'Manual' ? '—' : <input className="input" type="number" min="0" step="0.000001" value={row.value ?? ''} onChange={e => updateComponent(index, { value: e.target.value === '' ? null : Number(e.target.value) })} />}</td><td>{row.calculationType === 'Percentage' ? <select className="input select" aria-label={`Percentage base ${index + 1}`} value={row.percentageOfComponentId ?? ''} onChange={e => updateComponent(index, { percentageOfComponentId: e.target.value || null })}><option value="">Select base</option>{components.map(component => <option key={component.id} value={component.id}>{component.code}</option>)}</select> : '—'}</td><td>{row.calculationType === 'Formula' ? <input className="input" value={row.formula ?? ''} onChange={e => updateComponent(index, { formula: e.target.value })} /> : '—'}</td><td><input type="checkbox" checked={row.isProratable} onChange={e => updateComponent(index, { isProratable: e.target.checked })} /></td><td><input type="checkbox" checked={row.isEditableAtEmployeeLevel} onChange={e => updateComponent(index, { isEditableAtEmployeeLevel: e.target.checked })} /></td><td><input className="input" type="number" min="1" value={row.sequence} onChange={e => updateComponent(index, { sequence: Number(e.target.value) })} /></td><td><button className="row-action" type="button" onClick={() => setForm({ ...form, components: form.components.filter((_, i) => i !== index).map((item, i) => ({ ...item, sequence: i + 1 })) })}>Remove</button></td></tr>)}</tbody></table></div><button className="button button-secondary" type="button" onClick={() => setForm({ ...form, components: [...form.components, blankComponent(form.components.length + 1)] })}>Add component</button>
      {formError && <Notice tone="error">{formError}</Notice>}<div><button className="button button-primary" type="submit">{editingId ? 'Save changes' : 'Create structure'}</button>{editingId && <button className="button button-secondary" type="button" onClick={() => { setEditingId(null); setForm(empty()) }}>Cancel</button>}</div></form></Card>}
    <Card isRefreshing={isLoading}><div className="toolbar"><input className="input" placeholder="Search code or name" value={list.search} onChange={e => list.setSearch(e.target.value)} /><select className="input select" aria-label="Status" value={list.filters.isActive} onChange={e => list.setFilter('isActive', e.target.value)}><option value="">All statuses</option><option value="true">Active</option><option value="false">Inactive</option></select></div><div className="table-wrap"><table className="data-table"><thead><tr><th>Code</th><th>Name</th><th>Effective From</th><th>Effective To</th><th>Status</th><th>Components</th><th>Actions</th></tr></thead><tbody>{data?.items.map(row => <tr key={row.id}><td>{row.code}</td><td>{row.name}</td><td>{row.effectiveFrom}</td><td>{row.effectiveTo ?? 'Open'}</td><td>{row.isActive ? 'Active' : 'Inactive'}</td><td>{row.componentCount}</td><td>{canManage && <button className="row-action" type="button" onClick={() => edit(row)}>Edit</button>} {canManage && <button className="row-action" type="button" onClick={() => toggle(row)}>{row.isActive ? 'Deactivate' : 'Activate'}</button>} {can(Permissions.payroll.salaryStructureViewHistory) && <button className="row-action" type="button" onClick={() => showHistory(row.id)}>History</button>}</td></tr>)}</tbody></table></div>{data && <Pagination info={data} onPageChange={list.setPage} disabled={isLoading} />}</Card>
    {history && <Card><h2 className="section-title">Structure History</h2><button className="button button-secondary" type="button" onClick={() => setHistory(null)}>Close</button><ul>{history.map(item => <li key={item.id}>{item.changeType}: {item.code} — {item.changedAtUtc}</li>)}</ul></Card>}
  </>
}
