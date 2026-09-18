import { useEffect, useId, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api/errors.ts'
import { beginEditLeavePolicy, createLeavePolicy, getLeavePolicy, listLeavePolicies, updateLeavePolicy, type LeavePolicy, type LeavePolicyRequest } from '../../api/leaveConfiguration.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { ActiveBadge, Badge } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Pagination } from '../../components/Pagination.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

const emptyForm: LeavePolicyRequest = { code: '', name: '', description: '', isActive: true }

function PolicyIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-policy-drawer-icon"><path d="M12 3 19 6v5c0 4.5-2.9 7.8-7 10-4.1-2.2-7-5.5-7-10V6zM9 12l2 2 4-4" /></svg> }
function InfoIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-policy-drawer-icon"><circle cx="12" cy="12" r="9" /><path d="M12 10.5v5M12 7.5h.01" /></svg> }
function TagIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-policy-field-icon"><path d="M4 5v6l8 8 7-7-8-8zM8 8h.01" /></svg> }
function DocumentIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-policy-field-icon"><path d="M6 3.5h8l4 4v13H6zM14 3.5v4h4M9 12h6M9 15.5h6" /></svg> }
function SearchIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-policy-icon"><circle cx="10.5" cy="10.5" r="6.5" /><path d="m16 16 5 5" /></svg> }
function ResetIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-policy-icon"><path d="M4 7v5h5M5 12a7 7 0 1 0 2-5" /></svg> }
function PlusIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-policy-icon"><path d="M12 5v14M5 12h14" /></svg> }
function PencilIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-policy-action-icon"><path d="m4 16-.8 4.8L8 20l11-11-4-4zM13.5 6.5l4 4" /></svg> }
function OpenIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-policy-action-icon"><path d="M14 5h5v5M19 5l-9 9M18 13v5H5V5h5" /></svg> }
function SaveIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="leave-policy-action-icon"><path d="M5 4h12l2 2v14H5zM8 4v6h8V4M8 20v-6h8v6" /></svg> }

export function LeavePoliciesPage() {
  const { can } = useAuth()
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const canManage = can(Permissions.leave.policyManage)
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'all' | 'active' | 'inactive'>('all')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [editing, setEditing] = useState<LeavePolicy | null>(null)
  const [editorOpen, setEditorOpen] = useState(false)
  const [form, setForm] = useState<LeavePolicyRequest>(emptyForm)
  const [loadingEditor, setLoadingEditor] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [requestedEditId, setRequestedEditId] = useState<string | null>(null)
  const [dirty, setDirty] = useState(false)
  const latestEditRequest = useRef<string | null>(null)
  const query = useApiQuery(signal => listLeavePolicies({ search: search || undefined, isActive: status === 'all' ? undefined : status === 'active', page, pageSize }, signal), [search, status, page, pageSize])
  const editId = searchParams.get('edit')

  useEffect(() => {
    const timer = window.setTimeout(() => { setSearch(searchInput); setPage(1) }, 300)
    return () => window.clearTimeout(timer)
  }, [searchInput])

  useEffect(() => {
    if (!canManage || !editId || requestedEditId === editId) return
    latestEditRequest.current = editId
    setRequestedEditId(editId)
    setEditorOpen(true)
    setLoadingEditor(true)
    setEditing(null)
    setError(null)
    setNotice(null)
    void getLeavePolicy(editId).then(latest => {
      if (latestEditRequest.current !== editId) return
      setEditing(latest)
      setForm({ code: latest.code, name: latest.name, description: latest.description ?? '', isActive: latest.isActive, concurrencyToken: latest.concurrencyToken })
    }).catch(caught => {
      if (latestEditRequest.current !== editId) return
      setEditing(null)
      setEditorOpen(false)
      setForm({ ...emptyForm })
      setSearchParams({}, { replace: true })
      setError(caught instanceof ApiError ? caught : new ApiError('Unable to load Leave Policy.'))
    }).finally(() => { if (latestEditRequest.current === editId) setLoadingEditor(false) })
  }, [canManage, editId, requestedEditId, setSearchParams])

  function openCreate() { setRequestedEditId(null); setSearchParams({}, { replace: true }); setEditing(null); setEditorOpen(true); setForm({ ...emptyForm }); setError(null); setNotice(null); setDirty(false) }
  async function openEdit(item: LeavePolicy) {
    setError(null)
    setNotice(null)
    try {
      const editor = await beginEditLeavePolicy(item.id)
      if (editor.currentVersion) navigate(`/leave-management/policies/${item.id}?versionId=${editor.currentVersion.id}`)
      else navigate(`/leave-management/policies/${item.id}`)
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError('Unable to open Leave Policy for editing.'))
    }
  }
  function dismissEditor() { setRequestedEditId(null); latestEditRequest.current = null; setSearchParams({}, { replace: true }); setEditing(null); setEditorOpen(false); setDirty(false) }
  function closeEditor() { if (dirty && !window.confirm('Discard unsaved changes?')) return; dismissEditor() }
  function update<K extends keyof LeavePolicyRequest>(key: K, value: LeavePolicyRequest[K]) { setDirty(true); setForm(previous => ({ ...previous, [key]: value })) }
  async function submit(event: FormEvent) {
    event.preventDefault(); setSaving(true); setError(null); setNotice(null)
    try {
      if (editing) { await updateLeavePolicy(editing.id, form); dismissEditor(); setNotice('Leave Policy updated.'); query.refetch() }
      else { const created = await createLeavePolicy(form); dismissEditor(); navigate(`/leave-management/policies/${created.id}`) }
    } catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to save Leave Policy.')) }
    finally { setSaving(false) }
  }
  const rows = query.data?.items ?? []
  const fieldErrors = error?.fieldErrors ?? {}

  return <div className="leave-admin-page leave-policies-page">
    <div className="leave-policies-header-card"><PageHeader title="Leave Policies" subtitle="Configure leave rules, eligibility, entitlement, accrual, and other policy behavior for this tenant." actions={canManage ? <button className="button button-primary leave-policies-add" type="button" onClick={openCreate}><PlusIcon />+ Add Leave Policy</button> : undefined} /></div>
    {notice ? <Notice tone="success" onDismiss={() => setNotice(null)}>{notice}</Notice> : null}
    {error && !editorOpen ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest record before saving.' : ''}</Notice> : null}
    <Card className="leave-policies-card" title={<span className="leave-policies-card-title"><span className="leave-policies-card-icon" aria-hidden="true">▣</span>Leave Policies</span>} subtitle={query.data ? `${query.data.totalCount} leave polic${query.data.totalCount === 1 ? 'y' : 'ies'} configured` : undefined} actions={<div className="leave-policies-card-info"><span aria-hidden="true">◈</span><span><strong>Manage Leave Policies</strong><small>Configure versioned leave behavior while preserving history.</small></span></div>}>
      <div className="toolbar leave-policies-toolbar"><label className="leave-policies-search"><span className="sr-only">Search Leave Policies</span><SearchIcon /><input id="leave-policy-search" aria-label="Search Leave Policies" className="input toolbar-search" type="search" placeholder="Search code or name..." value={searchInput} onChange={event => setSearchInput(event.target.value)} /></label><label className="leave-policies-status"><span>Status</span><select id="leave-policy-status" aria-label="Leave Policy status" className="input toolbar-filter" value={status} onChange={event => { setStatus(event.target.value as typeof status); setPage(1) }}><option value="all">All statuses</option><option value="active">Active</option><option value="inactive">Inactive</option></select></label><button className="button button-secondary leave-policies-clear" type="button" onClick={() => { setSearchInput(''); setSearch(''); setStatus('all'); setPage(1) }}><ResetIcon />Clear filters</button></div>
      {query.isLoading ? <div className="state-block"><Spinner label="Loading Leave Policies" /></div> : query.error ? <Notice tone="error">{query.error.message}</Notice> : rows.length === 0 ? <EmptyState title={search ? 'No Leave Policies match your search.' : 'No Leave Policies have been configured yet.'} message={!search && canManage ? 'Add a Leave Policy to begin configuring versions.' : undefined} action={!search && canManage ? <button className="button button-primary" type="button" onClick={openCreate}>Add Leave Policy</button> : undefined} /> : <div className="table-wrap leave-policies-table-wrap"><table className="data-table leave-policies-table"><caption className="sr-only">Configured Leave Policies</caption><thead><tr><th scope="col">Code</th><th scope="col">Name</th><th scope="col">Status</th><th scope="col">Latest Version</th><th scope="col">Last Updated</th><th scope="col">Actions</th></tr></thead><tbody>{rows.map(item => <tr key={item.id}><td><code className="leave-policy-code">{item.code}</code></td><td><Link className="leave-policy-name" to={`/leave-management/policies/${item.id}`}>{item.name}</Link></td><td><ActiveBadge isActive={item.isActive} />{item.overlapCount ? <div className="leave-policy-overlap"><Badge tone="warning">Overlap {item.overlapCount}</Badge></div> : null}</td><td><span className={item.currentVersionNumber ? 'leave-policy-version is-published' : 'leave-policy-version is-draft'}>{item.currentVersionNumber ? `v${item.currentVersionNumber} — Published` : item.versionCount ? `${item.versionCount} version${item.versionCount === 1 ? '' : 's'} — no Published version` : 'No versions'}</span></td><td><span className="leave-policy-updated">{formatDate(item.modifiedDate ?? item.createdDate)}</span></td><td className="row-actions">{canManage ? <button className="row-action leave-policy-edit-action" type="button" onClick={() => openEdit(item)}><PencilIcon />Edit</button> : <span className="muted">View</span>} <Link className="row-action leave-policy-open-action" to={`/leave-management/policies/${item.id}`}><OpenIcon />Open</Link></td></tr>)}</tbody></table></div>}
      {query.data && rows.length > 0 ? <Pagination info={query.data} onPageChange={setPage} onPageSizeChange={size => { setPageSize(size); setPage(1) }} disabled={query.isLoading} /> : null}
    </Card>
    {canManage && editorOpen ? <LeavePolicyDrawer title={editing ? 'Edit Leave Policy' : 'Add Leave Policy'} subtitle={editing ? 'Update leave policy details.' : 'Create a new leave policy for this tenant.'} onClose={closeEditor}><form className="form-stack leave-policy-drawer-form" aria-label="Leave Policy identity editor" onSubmit={submit} aria-busy={loadingEditor || saving}>{error ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest record before saving.' : ''}</Notice> : null}<div className="leave-policy-drawer-callout"><InfoIcon /><span><strong>Important</strong><small>Leave Policies may have effective-dated versions and historical usage. Editing metadata does not alter published historical versions.</small></span></div>{loadingEditor ? <Spinner label="Loading latest Leave Policy" /> : <><label className="field leave-policy-drawer-field"><span className="leave-policy-drawer-label"><span>Code <em>*</em></span><CharacterCounter value={form.code} max={40} /></span><span className="leave-policy-drawer-control"><TagIcon /><input id="leave-policy-code" className={fieldErrors.code ? 'input has-error' : 'input'} required maxLength={40} placeholder="Enter policy code (e.g. CL-POLICY)" value={form.code} onChange={event => update('code', event.target.value)} readOnly={Boolean(editing?.currentVersionNumber)} aria-invalid={Boolean(fieldErrors.code)} aria-describedby="leave-policy-code-help" /></span>{editing?.currentVersionNumber ? <small className="field-help">Code is immutable after a Published version exists.</small> : <small id="leave-policy-code-help" className="field-help">Unique code used to identify this policy within the tenant.</small>}{fieldErrors.code ? <small className="field-error">{fieldErrors.code}</small> : null}</label><label className="field leave-policy-drawer-field"><span className="leave-policy-drawer-label"><span>Name <em>*</em></span><CharacterCounter value={form.name} max={150} /></span><span className="leave-policy-drawer-control"><DocumentIcon /><input id="leave-policy-name" className={fieldErrors.name ? 'input has-error' : 'input'} required maxLength={150} placeholder="Enter policy name (e.g. Casual Leave Policy)" value={form.name} onChange={event => update('name', event.target.value)} aria-invalid={Boolean(fieldErrors.name)} aria-describedby="leave-policy-name-help" /></span><small id="leave-policy-name-help" className="field-help">Display name of the leave policy.</small>{fieldErrors.name ? <small className="field-error">{fieldErrors.name}</small> : null}</label><label className="field leave-policy-drawer-field"><span className="leave-policy-drawer-label"><span>Description</span><CharacterCounter value={form.description ?? ''} max={1000} /></span><textarea id="leave-policy-description" className="input" maxLength={1000} placeholder="Enter description (optional)" value={form.description ?? ''} onChange={event => update('description', event.target.value)} aria-describedby="leave-policy-description-help" /><small id="leave-policy-description-help" className="field-help">Brief description of the policy and its intended use.</small></label><label className="leave-policy-active-control"><span><input type="checkbox" checked={form.isActive} onChange={event => update('isActive', event.target.checked)} /><strong>Active</strong></span><small>Inactive policies follow the existing selection and historical-reference rules.</small></label><div className="form-actions leave-policy-drawer-footer"><button className="button button-secondary" type="button" onClick={closeEditor} disabled={saving}>Cancel</button><button className="button button-primary" type="submit" aria-label={editing ? 'Save Leave Policy' : undefined} disabled={loadingEditor || saving}>{saving ? <Spinner size={14} label="Saving..." /> : <><SaveIcon />{editing ? 'Save Changes' : 'Save Leave Policy'}</>}</button></div></>}</form></LeavePolicyDrawer> : null}
  </div>
}

function CharacterCounter({ value, max }: { value: string; max: number }) { return <span className="leave-policy-drawer-counter">{value.length} / {max}</span> }

function LeavePolicyDrawer({ title, subtitle, onClose, children }: { title: string; subtitle: string; onClose: () => void; children: ReactNode }) {
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
  return <div className="leave-policy-drawer-backdrop" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}><aside className="leave-policy-drawer" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1} ref={drawerRef}><header className="leave-policy-drawer-header"><div className="leave-policy-drawer-heading"><span className="leave-policy-drawer-title-icon"><PolicyIcon /></span><span><h2 id={titleId}>{title}</h2><p>{subtitle}</p></span></div><button type="button" className="modal-close" onClick={onClose} aria-label="Close Leave Policy drawer">×</button></header><div className="leave-policy-drawer-body">{children}</div></aside></div>
}

function formatDate(value: string): string { const date = new Date(value); return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date) }
