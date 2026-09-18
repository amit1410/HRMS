import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react'
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
import { Pagination } from '../../components/Pagination.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

const emptyForm: LeaveTypeRequest = { code: '', name: '', description: '', defaultUnit: 'Day', isPaid: true, isActive: true }

function CalendarIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-types-icon"><rect x="4" y="5" width="16" height="15" rx="2" /><path d="M8 3v4M16 3v4M4 10h16" /></svg> }
function SearchIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-types-icon"><circle cx="10.5" cy="10.5" r="6.5" /><path d="m16 16 5 5" /></svg> }
function ResetIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-types-icon"><path d="M4 7v5h5M5 12a7 7 0 1 0 2-5" /></svg> }
function PlusIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-types-icon"><path d="M12 5v14M5 12h14" /></svg> }
function PencilIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-types-action-icon"><path d="m4 16-.8 4.8L8 20l11-11-4-4zM13.5 6.5l4 4" /></svg> }
function PowerIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-types-action-icon"><path d="M12 3v9M6.3 5.8a8 8 0 1 0 11.4 0" /></svg> }
function LeaveDocumentIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-drawer-icon"><path d="M6 3.5h8l4 4v13H6zM14 3.5v4h4M9 12h6M9 15.5h6" /></svg> }
function InfoIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-drawer-icon"><circle cx="12" cy="12" r="9" /><path d="M12 10.5v5M12 7.5h.01" /></svg> }
function TagIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-drawer-field-icon"><path d="M4 5v6l8 8 7-7-8-8zM8 8h.01" /></svg> }
function LayersIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-drawer-field-icon"><path d="m12 4 8 4-8 4-8-4zM4 12l8 4 8-4M4 16l8 4 8-4" /></svg> }
function SaveIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-drawer-action-icon"><path d="M5 4h12l2 2v14H5zM8 4v6h8V4M8 20v-6h8v6" /></svg> }

export function LeaveTypesPage() {
  const { can } = useAuth()
  const canManage = can(Permissions.leave.typeManage)
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'all' | 'active' | 'inactive'>('all')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [form, setForm] = useState<LeaveTypeRequest>(emptyForm)
  const [formLoading, setFormLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [confirming, setConfirming] = useState<LeaveType | null>(null)
  const [dirty, setDirty] = useState(false)
  const query = useApiQuery(
    signal => listLeaveTypes({ search: search || undefined, isActive: status === 'all' ? undefined : status === 'active', page, pageSize }, signal),
    [search, status, page, pageSize],
  )

  useEffect(() => {
    const timer = window.setTimeout(() => { setSearch(searchInput); setPage(1) }, 300)
    return () => window.clearTimeout(timer)
  }, [searchInput])

  useEffect(() => {
    if (editingId === null) return
    let cancelled = false
    setFormLoading(true)
    void getLeaveType(editingId).then(item => {
      if (!cancelled) setForm({ code: item.code, name: item.name, description: item.description ?? '', defaultUnit: item.defaultUnit, isPaid: item.isPaid, isActive: item.isActive, concurrencyToken: item.concurrencyToken })
    }).catch(caught => { if (!cancelled) setError(caught instanceof ApiError ? caught : new ApiError('Unable to load Leave Type.')) }).finally(() => { if (!cancelled) setFormLoading(false) })
    return () => { cancelled = true }
  }, [editingId])

  function openCreate() { setEditingId(null); setDrawerOpen(true); setForm({ ...emptyForm }); setError(null); setNotice(null); setDirty(false) }
  function openEdit(item: LeaveType) { setEditingId(item.id); setDrawerOpen(true); setForm({ code: item.code, name: item.name, description: item.description ?? '', defaultUnit: item.defaultUnit, isPaid: item.isPaid, isActive: item.isActive, concurrencyToken: item.concurrencyToken }); setError(null); setNotice(null); setDirty(false) }
  function closeEditor() { if (dirty && !window.confirm('Discard unsaved changes?')) return; setEditingId(null); setDrawerOpen(false); setDirty(false) }
  function update<K extends keyof LeaveTypeRequest>(key: K, value: LeaveTypeRequest[K]) { setDirty(true); setForm(previous => ({ ...previous, [key]: value })) }
  async function submit(event: FormEvent) {
    event.preventDefault(); setSaving(true); setError(null); setNotice(null)
    const isEditing = editingId !== null
    try { if (isEditing) await updateLeaveType(editingId, form); else await createLeaveType(form); setEditingId(null); setDrawerOpen(false); setDirty(false); setNotice(isEditing ? 'Leave Type updated.' : 'Leave Type created.'); query.refetch() }
    catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to save Leave Type.')) }
    finally { setSaving(false) }
  }
  async function deactivate() {
    if (!confirming) return
    setError(null)
    try { await updateLeaveType(confirming.id, { code: confirming.code, name: confirming.name, description: confirming.description, defaultUnit: confirming.defaultUnit, isPaid: confirming.isPaid, isActive: false, concurrencyToken: confirming.concurrencyToken }); setNotice('Leave Type deactivated. Historical references remain preserved.'); setConfirming(null); query.refetch() }
    catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to deactivate Leave Type.')) }
  }
  const fieldErrors = error?.fieldErrors ?? {}
  const rows = query.data?.items ?? []

  return <div className="leave-admin-page leave-types-page">
    <div className="leave-types-header-card"><PageHeader title="Leave Types" subtitle="Configure the leave categories available for tenant Leave Policies." actions={canManage ? <button className="button button-primary leave-types-add" type="button" onClick={openCreate}><PlusIcon />Add Leave Type</button> : undefined} /></div>
    {notice ? <Notice tone="success" onDismiss={() => setNotice(null)}>{notice}</Notice> : null}
    {error && !drawerOpen ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest record before saving.' : ''}</Notice> : null}
    <Card className="leave-types-card" title={<span className="leave-types-card-title"><span className="leave-types-card-icon" aria-hidden="true">▣</span>Leave Types</span>} subtitle={query.data ? `${query.data.totalCount} leave type${query.data.totalCount === 1 ? '' : 's'} configured` : undefined} actions={<div className="leave-types-card-info"><span aria-hidden="true">▪</span><span><strong>Manage Leave Categories</strong><small>Keep your leave policies organised and consistent.</small></span></div>}>
      <div className="toolbar leave-toolbar"><label className="leave-types-search"><span className="sr-only">Search Leave Types</span><SearchIcon /><input id="leave-type-search" aria-label="Search Leave Types" className="input toolbar-search" type="search" placeholder="Search code or name..." value={searchInput} onChange={event => setSearchInput(event.target.value)} /></label><label className="leave-types-status"><span>Status</span><select id="leave-type-status" aria-label="Leave Type status" className="input toolbar-filter" value={status} onChange={event => { setStatus(event.target.value as typeof status); setPage(1) }}><option value="all">All statuses</option><option value="active">Active</option><option value="inactive">Inactive</option></select></label><button className="button button-secondary leave-types-clear" type="button" onClick={() => { setSearchInput(''); setSearch(''); setStatus('all'); setPage(1) }}><ResetIcon />Clear filters</button></div>
      {query.isLoading ? <div className="state-block"><Spinner label="Loading Leave Types" /></div> : query.error ? <Notice tone="error">{query.error.message}</Notice> : rows.length === 0 ? <EmptyState title={search ? 'No Leave Types match your search.' : 'No Leave Types have been configured yet.'} message={!search && canManage ? 'Add a Leave Type to make it available for policy configuration.' : undefined} /> : <div className="table-wrap leave-types-table-wrap"><table className="data-table leave-types-table"><caption className="sr-only">Configured Leave Types</caption><thead><tr><th scope="col">Code</th><th scope="col">Name</th><th scope="col">Paid / Unpaid</th><th scope="col">Default Unit</th><th scope="col">Status</th><th scope="col">Last Updated</th><th scope="col">Actions</th></tr></thead><tbody>{rows.map(item => <tr key={item.id}><td><code className="leave-type-code">{item.code}</code></td><td><strong className="leave-type-name">{item.name}</strong></td><td><span className={`leave-paid-badge ${item.isPaid ? 'is-paid' : 'is-unpaid'}`}>{item.isPaid ? 'Paid' : 'Unpaid'}</span></td><td><span className="leave-type-unit">{item.defaultUnit}</span></td><td><ActiveBadge isActive={item.isActive} /></td><td><span className="leave-type-updated"><CalendarIcon />{formatDateTime(item.modifiedDate ?? item.createdDate)}</span></td><td className="row-actions">{canManage ? <span className="leave-type-actions"><button className="row-action leave-edit-action" type="button" onClick={() => openEdit(item)}><PencilIcon />Edit</button>{item.isActive ? <button className="row-action row-action-danger leave-deactivate-action" type="button" onClick={() => setConfirming(item)}><PowerIcon />Deactivate</button> : null}</span> : <span className="muted">—</span>}</td></tr>)}</tbody></table></div>}
      {query.data && rows.length > 0 ? <Pagination info={query.data} onPageChange={setPage} onPageSizeChange={size => { setPageSize(size); setPage(1) }} disabled={query.isLoading} /> : null}
    </Card>
    {drawerOpen && canManage ? <LeaveTypeDrawer title={editingId !== null ? 'Edit Leave Type' : 'Add Leave Type'} subtitle={editingId !== null ? 'Update leave category details.' : 'Create a new leave category for your organization.'} onClose={closeEditor}><form className="form-stack leave-type-drawer-form" aria-label="Leave Type editor" onSubmit={submit} aria-busy={formLoading || saving}>{error ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest version before saving.' : ''}</Notice> : null}<div className="leave-type-drawer-callout"><InfoIcon /><span><strong>Important</strong><small>Leave Types remain linked to policies, requests, balances, and historical records. Deactivation preserves those references.</small></span></div>{formLoading ? <p className="muted">Loading latest Leave Type…</p> : <><label className="field leave-drawer-field"><span className="leave-drawer-label"><span>Code <em>*</em></span><CharacterCounter value={form.code} max={40} /></span><span className="leave-drawer-control"><TagIcon /><input id="leave-type-code" className={fieldErrors.code ? 'input has-error' : 'input'} required maxLength={40} placeholder="Enter leave type code (e.g. CL, EL)" value={form.code} readOnly={editingId !== null} onChange={event => update('code', event.target.value)} aria-invalid={Boolean(fieldErrors.code)} aria-describedby="leave-type-code-help" /></span><small id="leave-type-code-help" className="field-help">Short code to identify the leave type. Must be unique within the tenant.</small>{fieldErrors.code ? <small className="field-error">{fieldErrors.code}</small> : null}</label><label className="field leave-drawer-field"><span className="leave-drawer-label"><span>Name <em>*</em></span><CharacterCounter value={form.name} max={150} /></span><span className="leave-drawer-control"><LeaveDocumentIcon /><input id="leave-type-name" className={fieldErrors.name ? 'input has-error' : 'input'} required maxLength={150} placeholder="Enter leave type name (e.g. Casual Leave)" value={form.name} onChange={event => update('name', event.target.value)} aria-invalid={Boolean(fieldErrors.name)} aria-describedby="leave-type-name-help" /></span><small id="leave-type-name-help" className="field-help">Full name of the leave type as it will appear in the system.</small>{fieldErrors.name ? <small className="field-error">{fieldErrors.name}</small> : null}</label><label className="field leave-drawer-field"><span className="leave-drawer-label"><span>Description</span><CharacterCounter value={form.description ?? ''} max={1000} /></span><span className="leave-drawer-control leave-drawer-textarea"><LeaveDocumentIcon /><textarea id="leave-type-description" className="input" maxLength={1000} placeholder="Enter description (optional)" value={form.description ?? ''} onChange={event => update('description', event.target.value)} aria-describedby="leave-type-description-help" /></span><small id="leave-type-description-help" className="field-help">Brief description of this leave type and its intended use.</small></label><label className="field leave-drawer-field"><span className="leave-drawer-label"><span>Default Unit <em>*</em></span></span><span className="leave-drawer-control"><LayersIcon /><select id="leave-type-unit" className="input" value={form.defaultUnit} onChange={event => update('defaultUnit', event.target.value as LeaveUnit)} aria-describedby="leave-type-unit-help"><option value="Day">Day</option><option value="Hour">Hour (future processing)</option></select></span><small id="leave-type-unit-help" className="field-help">The default unit used when applying this leave type.</small></label><fieldset className="leave-category-group"><legend>Leave Category <em>*</em></legend><div className="leave-category-options"><label className={form.isPaid ? 'leave-category-option is-selected' : 'leave-category-option'}><input type="radio" name="leave-category" checked={form.isPaid} onChange={() => update('isPaid', true)} /><span><strong>Paid</strong><small>Counts towards leave balance and entitlement.</small></span></label><label className={!form.isPaid ? 'leave-category-option is-selected' : 'leave-category-option'}><input type="radio" name="leave-category" checked={!form.isPaid} onChange={() => update('isPaid', false)} /><span><strong>Unpaid</strong><small>Does not count towards leave balance or entitlement.</small></span></label></div></fieldset><label className="leave-active-control"><span><input type="checkbox" checked={form.isActive} onChange={event => update('isActive', event.target.checked)} /><strong>Active</strong></span><small>Inactive leave types cannot be selected in new leave requests, but remain for historical reference.</small></label><div className="form-actions leave-type-drawer-footer"><button className="button button-secondary" type="button" onClick={closeEditor} disabled={saving}>Cancel</button><button className="button button-primary" type="submit" aria-label={editingId !== null ? 'Save Leave Type' : undefined} disabled={formLoading || saving}>{saving ? <Spinner size={14} label="Saving…" /> : <><SaveIcon />{editingId !== null ? 'Save Changes' : 'Save Leave Type'}</>}</button></div></>}</form></LeaveTypeDrawer> : null}
    {confirming ? <ConfirmDialog title="Deactivate Leave Type?" message={`${confirming.name} will no longer be available for new policy configuration.`} hint="Historical references remain preserved." confirmLabel="Deactivate" onConfirm={deactivate} onClose={() => setConfirming(null)} /> : null}
  </div>
}

function CharacterCounter({ value, max }: { value: string; max: number }) { return <span className="leave-drawer-counter">{value.length} / {max}</span> }

function LeaveTypeDrawer({ title, subtitle, onClose, children }: { title: string; subtitle: string; onClose: () => void; children: ReactNode }) {
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
  return <div className="leave-type-drawer-backdrop" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}><aside className="leave-type-drawer" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1} ref={drawerRef}><header className="leave-type-drawer-header"><div className="leave-type-drawer-heading"><span className="leave-type-drawer-title-icon"><LeaveDocumentIcon /></span><span><h2 id={titleId}>{title}</h2><p>{subtitle}</p></span></div><button type="button" className="modal-close" onClick={onClose} aria-label="Close Leave Type drawer">×</button></header><div className="leave-type-drawer-body">{children}</div></aside></div>
}

function formatDateTime(value: string): string { const date = new Date(value); return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date) }
