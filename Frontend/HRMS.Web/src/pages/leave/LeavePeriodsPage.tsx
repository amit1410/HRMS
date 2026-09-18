import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react'
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
import { Pagination } from '../../components/Pagination.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

const emptyForm: LeavePeriodRequest = { code: '', name: '', startDate: '', endDate: '', isActive: true }

function CalendarIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-periods-icon"><rect x="4" y="5" width="16" height="15" rx="2" /><path d="M8 3v4M16 3v4M4 10h16" /></svg> }
function SearchIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-periods-icon"><circle cx="10.8" cy="10.8" r="6.3" /><path d="m16 16 4.2 4.2" /></svg> }
function ResetIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-periods-icon"><path d="M4 7v5h5M5 12a7 7 0 1 0 2-5" /></svg> }
function PlusIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-periods-icon"><path d="M12 5v14M5 12h14" /></svg> }
function PencilIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-periods-action-icon"><path d="m4 16-.8 4.8L8 20l11-11-4-4zM13.5 6.5l4 4" /></svg> }
function PowerIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-periods-action-icon"><path d="M12 3v9M6.3 5.8a8 8 0 1 0 11.4 0" /></svg> }
function CalendarPlusIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-period-drawer-icon"><rect x="4" y="5" width="16" height="15" rx="2" /><path d="M8 3v4M16 3v4M4 10h16M12 13v4M10 15h4" /></svg> }
function InfoIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-period-drawer-icon"><circle cx="12" cy="12" r="9" /><path d="M12 10.5v5M12 7.5h.01" /></svg> }
function TagIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-period-field-icon"><path d="M4 5v6l8 8 7-7-8-8zM8 8h.01" /></svg> }
function DocumentIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-period-field-icon"><path d="M6 3.5h8l4 4v13H6zM14 3.5v4h4M9 12h6M9 15.5h6" /></svg> }
function SaveIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-period-action-icon"><path d="M5 4h12l2 2v14H5zM8 4v6h8V4M8 20v-6h8v6" /></svg> }

export function LeavePeriodsPage() {
  const { can } = useAuth()
  const canManage = can(Permissions.leave.periodManage)
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'all' | 'active' | 'inactive'>('all')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [form, setForm] = useState<LeavePeriodRequest>(emptyForm)
  const [formLoading, setFormLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [confirming, setConfirming] = useState<LeavePeriod | null>(null)
  const [dirty, setDirty] = useState(false)
  const query = useApiQuery(
    signal => listLeavePeriods({ search: search || undefined, isActive: status === 'all' ? undefined : status === 'active', page, pageSize }, signal),
    [search, status, page, pageSize],
  )

  useEffect(() => {
    const timer = window.setTimeout(() => { setSearch(searchInput); setPage(1) }, 300)
    return () => window.clearTimeout(timer)
  }, [searchInput])

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

  return <div className="leave-admin-page leave-periods-page">
    <div className="leave-periods-header-card"><PageHeader title="Leave Periods" subtitle="Define the tenant leave-year and accounting periods used by future entitlement and balance processing." actions={canManage ? <button className="button button-primary leave-periods-add" type="button" onClick={openCreate}><PlusIcon />Add Leave Period</button> : undefined} /></div>
    {notice ? <Notice tone="success" onDismiss={() => setNotice(null)}>{notice}</Notice> : null}
    {error && editingId === null ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest record before saving.' : ''}</Notice> : null}
    <Card className="leave-periods-card" title={<span className="leave-periods-card-title"><span className="leave-periods-card-icon" aria-hidden="true">▣</span>Leave Periods</span>} subtitle={query.data ? `${query.data.totalCount} leave period${query.data.totalCount === 1 ? '' : 's'} configured` : undefined} actions={<div className="leave-periods-card-info"><span aria-hidden="true">◷</span><span><strong>Manage Leave Accounting Periods</strong><small>Set up financial and leave years for accurate processing.</small></span></div>}>
      <div className="toolbar leave-periods-toolbar"><label className="leave-periods-search"><span className="sr-only">Search Leave Periods</span><SearchIcon /><input id="leave-period-search" aria-label="Search Leave Periods" className="input toolbar-search" type="search" placeholder="Search code or name..." value={searchInput} onChange={event => setSearchInput(event.target.value)} /></label><label className="leave-periods-status"><span>Status</span><select id="leave-period-status" aria-label="Leave Period status" className="input toolbar-filter" value={status} onChange={event => { setStatus(event.target.value as typeof status); setPage(1) }}><option value="all">All statuses</option><option value="active">Active</option><option value="inactive">Inactive</option></select></label><button className="button button-secondary leave-periods-clear" type="button" onClick={() => { setSearchInput(''); setSearch(''); setStatus('all'); setPage(1) }}><ResetIcon />Clear filters</button></div>
      {query.isLoading ? <div className="state-block"><Spinner label="Loading Leave Periods" /></div> : query.error ? <Notice tone="error">{query.error.message}</Notice> : rows.length === 0 ? <EmptyState title={search ? 'No Leave Periods match your search.' : 'No Leave Periods have been configured yet.'} message={!search && canManage ? 'Add a Leave Period for future period-aware processing.' : undefined} action={!search && canManage ? <button className="button button-primary" type="button" onClick={openCreate}>Add Leave Period</button> : undefined} /> : <div className="table-wrap leave-periods-table-wrap"><table className="data-table leave-periods-table"><caption className="sr-only">Configured Leave Periods</caption><thead><tr><th scope="col">Code</th><th scope="col">Name</th><th scope="col">Start Date</th><th scope="col">End Date</th><th scope="col">Status</th><th scope="col">Last Updated</th><th scope="col">Actions</th></tr></thead><tbody>{rows.map(item => <tr key={item.id}><td><code className="leave-period-code">{item.code}</code></td><td><strong className="leave-period-name">{item.name}</strong></td><td><span className="leave-period-date"><CalendarIcon />{formatDateOnly(item.startDate)}</span></td><td><span className="leave-period-date"><CalendarIcon />{formatDateOnly(item.endDate)}</span></td><td><ActiveBadge isActive={item.isActive} /></td><td><span className="leave-period-date"><CalendarIcon />{formatDateTime(item.modifiedDate ?? item.createdDate)}</span></td><td className="row-actions"><span className="leave-period-actions">{canManage ? <><button className="row-action leave-period-edit-action" type="button" onClick={() => openEdit(item)}><PencilIcon />Edit</button>{item.isActive ? <button className="row-action row-action-danger leave-period-deactivate-action" type="button" onClick={() => setConfirming(item)}><PowerIcon />Deactivate</button> : null}</> : <span className="muted">—</span>}</span></td></tr>)}</tbody></table></div>}
      {query.data && rows.length > 0 ? <Pagination info={query.data} onPageChange={setPage} onPageSizeChange={size => { setPageSize(size); setPage(1) }} disabled={query.isLoading} /> : null}
    </Card>
    {editingId !== null && canManage ? <LeavePeriodDrawer title={editingId ? 'Edit Leave Period' : 'Add Leave Period'} subtitle={editingId ? 'Update leave-year and accounting period details.' : 'Define a new leave-year and accounting period.'} onClose={closeEditor}><form className="form-stack leave-period-drawer-form" aria-label="Leave Period editor" onSubmit={submit} aria-busy={formLoading || saving}>{error ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest record before saving.' : ''}</Notice> : null}<div className="leave-period-drawer-callout"><InfoIcon /><span><strong>Important</strong><small>Leave periods support entitlement calculation and balance processing. Avoid overlapping dates with existing periods.</small></span></div>{formLoading ? <p className="muted">Loading latest Leave Period...</p> : <><label className="field leave-period-drawer-field"><span className="leave-period-drawer-label"><span>Code <em>*</em></span><CharacterCounter value={form.code} max={40} /></span><span className="leave-period-drawer-control"><TagIcon /><input id="leave-period-code" className={fieldErrors.code ? 'input has-error' : 'input'} required maxLength={40} placeholder="Enter leave period code (e.g. LP-2026)" value={form.code} onChange={event => update('code', event.target.value)} aria-invalid={Boolean(fieldErrors.code)} aria-describedby="leave-period-code-help" /></span><small id="leave-period-code-help" className="field-help">Unique code to identify the leave period. Must be unique within the tenant.</small>{fieldErrors.code ? <small className="field-error">{fieldErrors.code}</small> : null}</label><label className="field leave-period-drawer-field"><span className="leave-period-drawer-label"><span>Name <em>*</em></span><CharacterCounter value={form.name} max={150} /></span><span className="leave-period-drawer-control"><DocumentIcon /><input id="leave-period-name" className={fieldErrors.name ? 'input has-error' : 'input'} required maxLength={150} placeholder="Enter leave period name (e.g. Leave Period 2026)" value={form.name} onChange={event => update('name', event.target.value)} aria-invalid={Boolean(fieldErrors.name)} aria-describedby="leave-period-name-help" /></span><small id="leave-period-name-help" className="field-help">Descriptive name for the leave period.</small>{fieldErrors.name ? <small className="field-error">{fieldErrors.name}</small> : null}</label><label className="field leave-period-drawer-field"><span className="leave-period-drawer-label"><span>Start Date <em>*</em></span></span><span className="leave-period-drawer-control"><CalendarIcon /><input id="leave-period-start-date" className={fieldErrors.dates ? 'input has-error' : 'input'} type="date" required value={form.startDate} onChange={event => update('startDate', event.target.value)} aria-describedby="leave-period-start-help" /></span><small id="leave-period-start-help" className="field-help">Start date of the leave period.</small></label><label className="field leave-period-drawer-field"><span className="leave-period-drawer-label"><span>End Date <em>*</em></span></span><span className="leave-period-drawer-control"><CalendarIcon /><input id="leave-period-end-date" className={fieldErrors.dates ? 'input has-error' : 'input'} type="date" required value={form.endDate} onChange={event => update('endDate', event.target.value)} aria-describedby="leave-period-end-help" /></span><small id="leave-period-end-help" className="field-help">End date must be on or after the start date.</small>{fieldErrors.dates ? <small className="field-error">{fieldErrors.dates}</small> : null}</label><label className="leave-period-active-control"><span><input type="checkbox" checked={form.isActive} onChange={event => update('isActive', event.target.checked)} /><strong>Active</strong></span><small>Active leave periods can be used for entitlement calculation and balance processing.</small></label><div className="form-actions leave-period-drawer-footer"><button className="button button-secondary" type="button" onClick={closeEditor} disabled={saving}>Cancel</button><button className="button button-primary" type="submit" aria-label={editingId ? 'Save Leave Period' : undefined} disabled={formLoading || saving}>{saving ? <Spinner size={14} label="Saving..." /> : <><SaveIcon />{editingId ? 'Save Changes' : 'Save Leave Period'}</>}</button></div></>}</form></LeavePeriodDrawer> : null}
    {confirming ? <ConfirmDialog title="Deactivate Leave Period?" message={`${confirming.name} will no longer be available for current configuration.`} hint="Historical data remains preserved." confirmLabel="Deactivate" onConfirm={deactivate} onClose={() => setConfirming(null)} /> : null}
  </div>
}

function CharacterCounter({ value, max }: { value: string; max: number }) { return <span className="leave-period-drawer-counter">{value.length} / {max}</span> }

function LeavePeriodDrawer({ title, subtitle, onClose, children }: { title: string; subtitle: string; onClose: () => void; children: ReactNode }) {
  const titleId = useId()
  const drawerRef = useRef<HTMLElement>(null)
  const onCloseRef = useRef(onClose)
  useEffect(() => { onCloseRef.current = onClose }, [onClose])
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    const overflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    drawerRef.current?.focus()
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.stopPropagation(); onCloseRef.current(); return }
      if (event.key !== 'Tab' || !drawerRef.current) return
      const focusable = [...drawerRef.current.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')]
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (!first || !last) { event.preventDefault(); return }
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
    }
    document.addEventListener('keydown', onKeyDown, true)
    return () => { document.removeEventListener('keydown', onKeyDown, true); document.body.style.overflow = overflow; previous?.focus() }
  }, [])
  return <div className="leave-period-drawer-backdrop" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}><aside className="leave-period-drawer" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1} ref={drawerRef}><header className="leave-period-drawer-header"><div className="leave-period-drawer-heading"><span className="leave-period-drawer-title-icon"><CalendarPlusIcon /></span><span><h2 id={titleId}>{title}</h2><p>{subtitle}</p></span></div><button type="button" className="modal-close" onClick={onClose} aria-label="Close Leave Period drawer">×</button></header><div className="leave-period-drawer-body">{children}</div></aside></div>
}

function formatDateOnly(value: string): string { const [year, month, day] = value.slice(0, 10).split('-').map(Number); if (!year || !month || !day) return '—'; return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(year, month - 1, day, 12)) }
function formatDateTime(value: string): string { const date = new Date(value); return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date) }
