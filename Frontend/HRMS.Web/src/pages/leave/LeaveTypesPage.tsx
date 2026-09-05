import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../../api/errors.ts'
import { createLeaveType, getLeaveType, listLeaveTypes, updateLeaveType, type LeaveType, type LeaveTypeRequest, type LeaveUnit } from '../../api/leaveConfiguration.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { ActiveBadge } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

const emptyForm: LeaveTypeRequest = { code: '', name: '', description: '', defaultUnit: 'Day', isPaid: true, isActive: true }

export function LeaveTypesPage() {
  const { can } = useAuth()
  const canManage = can(Permissions.leave.typeManage)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'all' | 'active' | 'inactive'>('all')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<LeaveTypeRequest>(emptyForm)
  const [formLoading, setFormLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [confirming, setConfirming] = useState<LeaveType | null>(null)
  const [dirty, setDirty] = useState(false)
  const query = useApiQuery(
    signal => listLeaveTypes({ search: search || undefined, isActive: status === 'all' ? undefined : status === 'active', page: 1, pageSize: 100 }, signal),
    [search, status],
  )

  useEffect(() => {
    if (!editingId) return
    let cancelled = false
    setFormLoading(true)
    void getLeaveType(editingId).then(item => {
      if (!cancelled) setForm({ code: item.code, name: item.name, description: item.description ?? '', defaultUnit: item.defaultUnit, isPaid: item.isPaid, isActive: item.isActive, concurrencyToken: item.concurrencyToken })
    }).catch(caught => { if (!cancelled) setError(caught instanceof ApiError ? caught : new ApiError('Unable to load Leave Type.')) }).finally(() => { if (!cancelled) setFormLoading(false) })
    return () => { cancelled = true }
  }, [editingId])

  function openCreate() { setEditingId(''); setForm({ ...emptyForm }); setError(null); setNotice(null); setDirty(false) }
  function openEdit(item: LeaveType) { setEditingId(item.id); setForm({ code: item.code, name: item.name, description: item.description ?? '', defaultUnit: item.defaultUnit, isPaid: item.isPaid, isActive: item.isActive, concurrencyToken: item.concurrencyToken }); setError(null); setNotice(null); setDirty(false) }
  function closeEditor() { if (dirty && !window.confirm('Discard unsaved changes?')) return; setEditingId(null); setDirty(false) }
  function update<K extends keyof LeaveTypeRequest>(key: K, value: LeaveTypeRequest[K]) { setDirty(true); setForm(previous => ({ ...previous, [key]: value })) }
  async function submit(event: FormEvent) {
    event.preventDefault(); setSaving(true); setError(null); setNotice(null)
    try { if (editingId) await updateLeaveType(editingId, form); else await createLeaveType(form); setEditingId(null); setDirty(false); setNotice(editingId ? 'Leave Type updated.' : 'Leave Type created.'); query.refetch() }
    catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to save Leave Type.')) }
    finally { setSaving(false) }
  }
  async function deactivate() {
    if (!confirming) return
    await updateLeaveType(confirming.id, { code: confirming.code, name: confirming.name, description: confirming.description, defaultUnit: confirming.defaultUnit, isPaid: confirming.isPaid, isActive: false, concurrencyToken: confirming.concurrencyToken })
    setNotice('Leave Type deactivated. Historical references remain preserved.'); setConfirming(null); query.refetch()
  }
  const fieldErrors = error?.fieldErrors ?? {}
  const rows = query.data?.items ?? []

  return <div className="leave-admin-page">
    <PageHeader title="Leave Types" subtitle="Configure the leave categories available for tenant Leave Policies." actions={canManage ? <button className="button button-primary" type="button" onClick={openCreate}>+ Add Leave Type</button> : undefined} />
    {notice ? <Notice tone="success" onDismiss={() => setNotice(null)}>{notice}</Notice> : null}
    {error && editingId === null ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest record before saving.' : ''}</Notice> : null}
    <Card title="Leave Types" subtitle={query.data ? `${query.data.totalCount} ${query.data.totalCount === 1 ? 'record' : 'records'}` : undefined}>
      <div className="toolbar leave-toolbar"><label className="sr-only" htmlFor="leave-type-search">Search Leave Types</label><input id="leave-type-search" className="input toolbar-search" type="search" placeholder="Search code or name" value={search} onChange={event => setSearch(event.target.value)} /><label className="sr-only" htmlFor="leave-type-status">Leave Type status</label><select id="leave-type-status" className="input toolbar-filter" value={status} onChange={event => setStatus(event.target.value as typeof status)}><option value="all">All statuses</option><option value="active">Active</option><option value="inactive">Inactive</option></select></div>
      {query.isLoading ? <div className="state-block"><Spinner label="Loading Leave Types" /></div> : query.error ? <Notice tone="error">{query.error.message}</Notice> : rows.length === 0 ? <EmptyState title={search ? 'No Leave Types match your search.' : 'No Leave Types have been configured yet.'} message={!search && canManage ? 'Add a Leave Type to make it available for policy configuration.' : undefined} action={!search && canManage ? <button className="button button-primary" type="button" onClick={openCreate}>Add Leave Type</button> : undefined} /> : <div className="table-wrap"><table className="data-table"><caption className="sr-only">Configured Leave Types</caption><thead><tr><th>Code</th><th>Name</th><th>Paid / Unpaid</th><th>Default Unit</th><th>Status</th><th>Last Updated</th><th>Actions</th></tr></thead><tbody>{rows.map(item => <tr key={item.id}><td><code>{item.code}</code></td><td>{item.name}</td><td>{item.isPaid ? 'Paid' : 'Unpaid'}</td><td>{item.defaultUnit}</td><td><ActiveBadge isActive={item.isActive} /></td><td>{formatDateTime(item.modifiedDate ?? item.createdDate)}</td><td className="row-actions">{canManage ? <><button className="row-action" type="button" onClick={() => openEdit(item)}>Edit</button>{item.isActive ? <button className="row-action row-action-danger" type="button" onClick={() => setConfirming(item)}>Deactivate</button> : null}</> : <span className="muted">—</span>}</td></tr>)}</tbody></table></div>}
    </Card>
    {editingId !== null && canManage ? <Card className="leave-editor" title={editingId ? 'Edit Leave Type' : 'Add Leave Type'}><form className="form-stack" onSubmit={submit} aria-busy={formLoading || saving}>{error ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest version before saving.' : ''}</Notice> : null}{formLoading ? <p className="muted">Loading latest Leave Type…</p> : <><label className="field"><span>Code <em>(required)</em></span><input className={fieldErrors.code ? 'input has-error' : 'input'} required maxLength={40} value={form.code} readOnly={Boolean(editingId)} onChange={event => update('code', event.target.value)} aria-invalid={Boolean(fieldErrors.code)} />{fieldErrors.code ? <small className="field-error">{fieldErrors.code}</small> : null}</label><label className="field"><span>Name <em>(required)</em></span><input className={fieldErrors.name ? 'input has-error' : 'input'} required maxLength={150} value={form.name} onChange={event => update('name', event.target.value)} aria-invalid={Boolean(fieldErrors.name)} />{fieldErrors.name ? <small className="field-error">{fieldErrors.name}</small> : null}</label><label className="field"><span>Description</span><textarea className="input" maxLength={1000} value={form.description ?? ''} onChange={event => update('description', event.target.value)} /></label><label className="field"><span>Default Unit</span><select className="input" value={form.defaultUnit} onChange={event => update('defaultUnit', event.target.value as LeaveUnit)}><option value="Day">Day</option><option value="Hour">Hour (future processing)</option></select></label><fieldset className="field-group"><legend>Leave Category</legend><label><input type="radio" checked={form.isPaid} onChange={() => update('isPaid', true)} /> Paid</label><label><input type="radio" checked={!form.isPaid} onChange={() => update('isPaid', false)} /> Unpaid</label></fieldset><label className="checkbox-field"><input type="checkbox" checked={form.isActive} onChange={event => update('isActive', event.target.checked)} /> Active</label><div className="form-actions"><button className="button button-secondary" type="button" onClick={closeEditor} disabled={saving}>Cancel</button><button className="button button-primary" type="submit" disabled={formLoading || saving}>{saving ? <Spinner size={14} label="Saving…" /> : 'Save Leave Type'}</button></div></>}</form></Card> : null}
    {confirming ? <ConfirmDialog title="Deactivate Leave Type?" message={`${confirming.name} will no longer be available for new policy configuration.`} hint="Historical references remain preserved." confirmLabel="Deactivate" onConfirm={deactivate} onClose={() => setConfirming(null)} /> : null}
  </div>
}

function formatDateTime(value: string): string { const date = new Date(value); return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date) }
