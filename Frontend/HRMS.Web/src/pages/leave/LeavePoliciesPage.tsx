import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ApiError } from '../../api/errors.ts'
import { createLeavePolicy, getLeavePolicy, listLeavePolicies, updateLeavePolicy, type LeavePolicy, type LeavePolicyRequest } from '../../api/leaveConfiguration.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { ActiveBadge } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

const emptyForm: LeavePolicyRequest = { code: '', name: '', description: '', isActive: true }

export function LeavePoliciesPage() {
  const { can } = useAuth()
  const navigate = useNavigate()
  const canManage = can(Permissions.leave.policyManage)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'all' | 'active' | 'inactive'>('all')
  const [editing, setEditing] = useState<LeavePolicy | null>(null)
  const [editorOpen, setEditorOpen] = useState(false)
  const [form, setForm] = useState<LeavePolicyRequest>(emptyForm)
  const [loadingEditor, setLoadingEditor] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const query = useApiQuery(signal => listLeavePolicies({ search: search || undefined, isActive: status === 'all' ? undefined : status === 'active', page: 1, pageSize: 100 }, signal), [search, status])

  function openCreate() { setEditing(null); setEditorOpen(true); setForm({ ...emptyForm }); setError(null); setNotice(null) }
  function openEdit(item: LeavePolicy) { setEditorOpen(true); setLoadingEditor(true); setError(null); void getLeavePolicy(item.id).then(latest => { setEditing(latest); setForm({ code: latest.code, name: latest.name, description: latest.description ?? '', isActive: latest.isActive, concurrencyToken: latest.concurrencyToken }) }).catch(caught => setError(caught instanceof ApiError ? caught : new ApiError('Unable to load Leave Policy.'))).finally(() => setLoadingEditor(false)) }
  function update<K extends keyof LeavePolicyRequest>(key: K, value: LeavePolicyRequest[K]) { setForm(previous => ({ ...previous, [key]: value })) }
  async function submit(event: FormEvent) { event.preventDefault(); setSaving(true); setError(null); setNotice(null); try { if (editing) { await updateLeavePolicy(editing.id, form); setEditing(null); setEditorOpen(false); setNotice('Leave Policy updated.'); query.refetch() } else { const created = await createLeavePolicy(form); setEditing(null); setEditorOpen(false); navigate(`/leave-management/policies/${created.id}`) } } catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to save Leave Policy.')) } finally { setSaving(false) } }
  const rows = query.data?.items ?? []
  const fieldErrors = error?.fieldErrors ?? {}

  return <div className="leave-admin-page">
    <PageHeader title="Leave Policies" subtitle="Configure effective-dated Leave Policies and manage their Draft and Published versions." actions={canManage ? <button className="button button-primary" type="button" onClick={openCreate}>+ Add Leave Policy</button> : undefined} />
    {notice ? <Notice tone="success" onDismiss={() => setNotice(null)}>{notice}</Notice> : null}
    {error && !editorOpen ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest record before saving.' : ''}</Notice> : null}
    <Card title="Leave Policies" subtitle={query.data ? `${query.data.totalCount} ${query.data.totalCount === 1 ? 'record' : 'records'}` : undefined}>
      <div className="toolbar leave-toolbar"><label className="sr-only" htmlFor="leave-policy-search">Search Leave Policies</label><input id="leave-policy-search" className="input toolbar-search" type="search" placeholder="Search code or name" value={search} onChange={event => setSearch(event.target.value)} /><label className="sr-only" htmlFor="leave-policy-status">Leave Policy status</label><select id="leave-policy-status" className="input toolbar-filter" value={status} onChange={event => setStatus(event.target.value as typeof status)}><option value="all">All statuses</option><option value="active">Active</option><option value="inactive">Inactive</option></select></div>
      {query.isLoading ? <div className="state-block"><Spinner label="Loading Leave Policies" /></div> : query.error ? <Notice tone="error">{query.error.message}</Notice> : rows.length === 0 ? <EmptyState title={search ? 'No Leave Policies match your search.' : 'No Leave Policies have been configured yet.'} message={!search && canManage ? 'Add a Leave Policy to begin configuring versions.' : undefined} action={!search && canManage ? <button className="button button-primary" type="button" onClick={openCreate}>Add Leave Policy</button> : undefined} /> : <div className="table-wrap"><table className="data-table"><caption className="sr-only">Configured Leave Policies</caption><thead><tr><th>Code</th><th>Name</th><th>Status</th><th>Latest Version</th><th>Last Updated</th><th>Actions</th></tr></thead><tbody>{rows.map(item => <tr key={item.id}><td><code>{item.code}</code></td><td><Link to={`/leave-management/policies/${item.id}`}>{item.name}</Link></td><td><ActiveBadge isActive={item.isActive} /></td><td>{item.currentVersionNumber ? `v${item.currentVersionNumber} — Published` : item.versionCount ? `${item.versionCount} version${item.versionCount === 1 ? '' : 's'} — no Published version` : 'No versions'}</td><td>{formatDate(item.modifiedDate ?? item.createdDate)}</td><td className="row-actions">{canManage ? <button className="row-action" type="button" onClick={() => openEdit(item)}>Edit</button> : <span className="muted">View</span>} <Link className="row-action" to={`/leave-management/policies/${item.id}`}>Open</Link></td></tr>)}</tbody></table></div>}
    </Card>
    {editorOpen && loadingEditor ? <p className="muted">Loading latest Leave Policy…</p> : null}
    {canManage && editorOpen && !loadingEditor ? <Card className="leave-editor" title={editing ? 'Edit Leave Policy' : 'Add Leave Policy'}><form className="form-stack" aria-label="Leave Policy identity editor" onSubmit={submit} aria-busy={saving}><label className="field"><span>Code <em>(required)</em></span><input className={fieldErrors.code ? 'input has-error' : 'input'} required maxLength={40} value={form.code} onChange={event => update('code', event.target.value)} readOnly={Boolean(editing?.currentVersionNumber)} aria-invalid={Boolean(fieldErrors.code)} />{editing?.currentVersionNumber ? <small className="muted">Code is immutable after a Published version exists.</small> : null}{fieldErrors.code ? <small className="field-error">{fieldErrors.code}</small> : null}</label><label className="field"><span>Name <em>(required)</em></span><input className={fieldErrors.name ? 'input has-error' : 'input'} required maxLength={150} value={form.name} onChange={event => update('name', event.target.value)} aria-invalid={Boolean(fieldErrors.name)} />{fieldErrors.name ? <small className="field-error">{fieldErrors.name}</small> : null}</label><label className="field"><span>Description</span><textarea className="input" maxLength={1000} value={form.description ?? ''} onChange={event => update('description', event.target.value)} /></label><label className="checkbox-field"><input type="checkbox" checked={form.isActive} onChange={event => update('isActive', event.target.checked)} /> Active</label>{error ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest version before saving.' : ''}</Notice> : null}<div className="form-actions"><button className="button button-secondary" type="button" onClick={() => { setEditing(null); setEditorOpen(false) }} disabled={saving}>Cancel</button><button className="button button-primary" type="submit" disabled={saving}>{saving ? <Spinner size={14} label="Saving…" /> : 'Save Leave Policy'}</button></div></form></Card> : null}
  </div>
}

function formatDate(value: string): string { const date = new Date(value); return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date) }
