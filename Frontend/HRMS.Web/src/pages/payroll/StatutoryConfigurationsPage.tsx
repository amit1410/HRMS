import { useState, type FormEvent } from 'react'
import { createStatutoryConfiguration, listStatutoryConfigurations, type StatutoryConfigurationRequest, type StatutoryType } from '../../api/payroll.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Pagination } from '../../components/Pagination.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'
import { useListQuery } from '../../hooks/useListQuery.ts'

const types: StatutoryType[] = ['ProvidentFund', 'Esi', 'ProfessionalTax', 'IncomeTax']
const empty: StatutoryConfigurationRequest = { jurisdictionCode: 'IN', stateCode: null, statutoryType: 'ProvidentFund', code: '', name: '', isActive: true }

export function StatutoryConfigurationsPage() {
  useDocumentTitle('Statutory Configuration')
  const { can } = useAuth(); const list = useListQuery({ sortFields: ['code'], defaultSortBy: 'code', filterKeys: ['statutoryType', 'isActive'] }); const { data, error, isLoading, refetch } = useApiQuery(signal => listStatutoryConfigurations({ ...list.pagedQuery, ...list.filters }, signal), [list.key]); const [form, setForm] = useState(empty); const [message, setMessage] = useState<string | null>(null); const [formError, setFormError] = useState<string | null>(null)
  async function save(event: FormEvent) { event.preventDefault(); setFormError(null); try { await createStatutoryConfiguration(form); setMessage('Statutory configuration created.'); setForm({ ...empty }); refetch() } catch (e) { setFormError(e instanceof Error ? e.message : 'Unable to save statutory configuration.') } }
  return <><PageHeader title="Statutory Configuration" subtitle="Configure effective-dated, jurisdiction-aware statutory rules. Rates and ceilings are data, not code." />{message && <Notice tone="success" onDismiss={() => setMessage(null)}>{message}</Notice>}{error && <Notice tone="error">Unable to load statutory configurations.</Notice>}{can(Permissions.payroll.statutoryManage) && <Card><form className="form-grid" onSubmit={save}><h2 className="section-title">Add configuration</h2><label>Jurisdiction<input className="input" required maxLength={8} value={form.jurisdictionCode} onChange={e => setForm({ ...form, jurisdictionCode: e.target.value.toUpperCase() })} /></label><label>State code<input className="input" value={form.stateCode ?? ''} onChange={e => setForm({ ...form, stateCode: e.target.value.toUpperCase() || null })} /></label><label>Statutory type<select className="input select" value={form.statutoryType} onChange={e => setForm({ ...form, statutoryType: e.target.value as StatutoryType })}>{types.map(x => <option key={x}>{x}</option>)}</select></label><label>Code<input className="input" required value={form.code} onChange={e => setForm({ ...form, code: e.target.value })} /></label><label>Name<input className="input" required value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} /></label>{formError && <Notice tone="error">{formError}</Notice>}<button className="button button-primary" type="submit">Create configuration</button></form></Card>}<Card isRefreshing={isLoading}><div className="toolbar"><input className="input" placeholder="Search code or name" value={list.search} onChange={e => list.setSearch(e.target.value)} /></div><div className="table-wrap"><table className="data-table"><thead><tr><th>Code</th><th>Name</th><th>Jurisdiction</th><th>Type</th><th>Versions</th><th>Status</th></tr></thead><tbody>{data?.items.map(x => <tr key={x.id}><td>{x.code}</td><td>{x.name}</td><td>{x.jurisdictionCode}{x.stateCode ? `/${x.stateCode}` : ''}</td><td>{x.statutoryType}</td><td>{x.versions.length}</td><td>{x.isActive ? 'Active' : 'Inactive'}</td></tr>)}</tbody></table></div>{data && <Pagination info={data} onPageChange={list.setPage} disabled={isLoading} />}</Card></>
}
