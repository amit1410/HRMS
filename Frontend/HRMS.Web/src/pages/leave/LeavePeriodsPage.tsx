import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../../api/errors.ts'
import { createLeavePeriod, getLeavePeriod, listLeavePeriods, updateLeavePeriod, type LeavePeriod, type LeavePeriodRequest } from '../../api/leaveConfiguration.ts'
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

const emptyForm: LeavePeriodRequest = { code: '', name: '', startDate: '', endDate: '', isActive: true }

export function LeavePeriodsPage() {
  const { can } = useAuth()
  const canManage = can(Permissions.leave.periodManage)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'all' | 'active' | 'inactive'>('all')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<LeavePeriodRequest>(emptyForm)
  const [formLoading, setFormLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [confirming, setConfirming] = useState<LeavePeriod | null>(null)
  const [dirty, setDirty] = useState(false)
  const query = useApiQuery(
    signal => listLeavePeriods({ search: search || undefined, isActive: status === 'all' ? undefined : status === 'active', page: 1, pageSize: 100 }, signal),
    [search, status],
  )

  useEffect(() => {
    if (!editingId) return
    let cancelled = false
    setFormLoading(true)
    void getLeavePeriod(editingId).then(item => {
      if (!cancelled) setForm({ code: item.code, name: item.name, startDate: item.startDate, endDate: item.endDate, isActive: item.isActive, concurrencyToken: item.concurrencyToken })
    }).catch(caught => { if (!cancelled) setError(caught instanceof ApiError ? caught : new ApiError('Unable to load Leave Period.')) }).finally(() => { if (!cancelled) setFormLoading(false) })
    return () => { cancelled = true }
  }, [editingId])

  function openCreate() { setEditingId(''); setForm({ ...emptyForm }); setError(null); setNotice(null); setDirty(false) }
  function openEdit(item: LeavePeriod) { setEditingId(item.id); setForm({ code: item.code, name: item.name, startDate: item.startDate, endDate: item.endDate, isActive: item.isActive, concurrencyToken: item.concurrencyToken }); setError(null); setNotice(null); setDirty(false) }
  function closeEditor() { if (dirty && !window.confirm('Discard unsaved changes?')) return; setEditingId(null); setDirty(false) }
  function update<K extends keyof LeavePeriodRequest>(key: K, value: LeavePeriodRequest[K]) { setDirty(true); setForm(previous => ({ ...previous, [key]: value })) }
  function localError(): string | null { if (!form.code.trim()) return 'Code is required.'; if (!form.name.trim()) return 'Name is required.'; if (!form.startDate || !form.endDate) return 'Start Date and End Date are required.'; if (form.startDate > form.endDate) return 'Start Date must be on or before End Date.'; return null }
  async function submit(event: FormEvent) {
    event.preventDefault(); const immediate = localError(); if (immediate) { setError(new ApiError(immediate)); return }
    setSaving(true); setError(null); setNotice(null)
    try { if (editingId) await updateLeavePeriod(editingId, form); else await createLeavePeriod(form); setEditingId(null); setDirty(false); setNotice(editingId ? 'Leave Period updated.' : 'Leave Period created.'); query.refetch() }
    catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to save Leave Period.')) }
    finally { setSaving(false) }
  }
  async function deactivate() {
    if (!confirming) return
    await updateLeavePeriod(confirming.id, { code: confirming.code, name: confirming.name, startDate: confirming.startDate, endDate: confirming.endDate, isActive: false, concurrencyToken: confirming.concurrencyToken })
    setNotice('Leave Period deactivated. Historical data remains preserved.'); setConfirming(null); query.refetch()
  }
  const fieldErrors = error?.fieldErrors ?? {}
  const rows = query.data?.items ?? []

  return <div className="leave-admin-page">
    <PageHeader title="Leave Periods" subtitle="Define the tenant leave-year and accounting periods used by future entitlement and balance processing." actions={canManage ? <button className="button button-primary" type="button" onClick={openCreate}>+ Add Leave Period</button> : undefined} />
    {notice ? <Notice tone="success" onDismiss={() => setNotice(null)}>{notice}</Notice> : null}
    {error && editingId === null ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest record before saving.' : ''}</Notice> : null}
    <Card title="Leave Periods" subtitle={query.data ? `${query.data.totalCount} ${query.data.totalCount === 1 ? 'record' : 'records'}` : undefined}>
      <div className="toolbar leave-toolbar"><label className="sr-only" htmlFor="leave-period-search">Search Leave Periods</label><input id="leave-period-search" className="input toolbar-search" type="search" placeholder="Search code or name" value={search} onChange={event => setSearch(event.target.value)} /><label className="sr-only" htmlFor="leave-period-status">Leave Period status</label><select id="leave-period-status" className="input toolbar-filter" value={status} onChange={event => setStatus(event.target.value as typeof status)}><option value="all">All statuses</option><option value="active">Active</option><option value="inactive">Inactive</option></select></div>
      {query.isLoading ? <div className="state-block"><Spinner label="Loading Leave Periods" /></div> : query.error ? <Notice tone="error">{query.error.message}</Notice> : rows.length === 0 ? <EmptyState title={search ? 'No Leave Periods match your search.' : 'No Leave Periods have been configured yet.'} message={!search && canManage ? 'Add a Leave Period for future period-aware processing.' : undefined} action={!search && canManage ? <button className="button button-primary" type="button" onClick={openCreate}>Add Leave Period</button> : undefined} /> : <div className="table-wrap"><table className="data-table"><caption className="sr-only">Configured Leave Periods</caption><thead><tr><th>Code</th><th>Name</th><th>Start Date</th><th>End Date</th><th>Status</th><th>Last Updated</th><th>Actions</th></tr></thead><tbody>{rows.map(item => <tr key={item.id}><td><code>{item.code}</code></td><td>{item.name}</td><td>{formatDateOnly(item.startDate)}</td><td>{formatDateOnly(item.endDate)}</td><td><ActiveBadge isActive={item.isActive} /></td><td>{formatDateTime(item.modifiedDate ?? item.createdDate)}</td><td className="row-actions">{canManage ? <><button className="row-action" type="button" onClick={() => openEdit(item)}>Edit</button>{item.isActive ? <button className="row-action row-action-danger" type="button" onClick={() => setConfirming(item)}>Deactivate</button> : null}</> : <span className="muted">—</span>}</td></tr>)}</tbody></table></div>}
    </Card>
    {editingId !== null && canManage ? <Card className="leave-editor" title={editingId ? 'Edit Leave Period' : 'Add Leave Period'}><form className="form-stack" aria-label="Leave Period editor" onSubmit={submit} aria-busy={formLoading || saving}>{error ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest version before saving.' : ''}</Notice> : null}{formLoading ? <p className="muted">Loading latest Leave Period…</p> : <><label className="field"><span>Code <em>(required)</em></span><input className={fieldErrors.code ? 'input has-error' : 'input'} required maxLength={40} value={form.code} onChange={event => update('code', event.target.value)} aria-invalid={Boolean(fieldErrors.code)} />{fieldErrors.code ? <small className="field-error">{fieldErrors.code}</small> : null}</label><label className="field"><span>Name <em>(required)</em></span><input className={fieldErrors.name ? 'input has-error' : 'input'} required maxLength={150} value={form.name} onChange={event => update('name', event.target.value)} aria-invalid={Boolean(fieldErrors.name)} />{fieldErrors.name ? <small className="field-error">{fieldErrors.name}</small> : null}</label><div className="form-grid"><label className="field"><span>Start Date <em>(required)</em></span><input className={fieldErrors.dates ? 'input has-error' : 'input'} type="date" required value={form.startDate} onChange={event => update('startDate', event.target.value)} /></label><label className="field"><span>End Date <em>(required)</em></span><input className={fieldErrors.dates ? 'input has-error' : 'input'} type="date" required value={form.endDate} onChange={event => update('endDate', event.target.value)} />{fieldErrors.dates ? <small className="field-error">{fieldErrors.dates}</small> : null}</label></div><label className="checkbox-field"><input type="checkbox" checked={form.isActive} onChange={event => update('isActive', event.target.checked)} /> Active</label><div className="form-actions"><button className="button button-secondary" type="button" onClick={closeEditor} disabled={saving}>Cancel</button><button className="button button-primary" type="submit" disabled={formLoading || saving}>{saving ? <Spinner size={14} label="Saving…" /> : 'Save Leave Period'}</button></div></>}</form></Card> : null}
    {confirming ? <ConfirmDialog title="Deactivate Leave Period?" message={`${confirming.name} will no longer be available for current configuration.`} hint="Historical data remains preserved." confirmLabel="Deactivate" onConfirm={deactivate} onClose={() => setConfirming(null)} /> : null}
  </div>
}

function formatDateOnly(value: string): string { const [year, month, day] = value.slice(0, 10).split('-').map(Number); if (!year || !month || !day) return '—'; return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(year, month - 1, day, 12)) }
function formatDateTime(value: string): string { const date = new Date(value); return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date) }
