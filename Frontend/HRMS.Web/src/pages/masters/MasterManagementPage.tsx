import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api/errors.ts'
import { createManagedMaster, deleteManagedMaster, listManagedMasters, updateManagedMaster, type MasterRecord, type MasterRequest } from '../../api/masterManagement.ts'
import { listDepartments, listFunctions, listHoldingCompanies, listSections, listSubDepartments } from '../../api/masterData.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { ActiveBadge } from '../../components/Badge.tsx'
import { useAuth } from '../../auth/useAuth.ts'
import { Permissions } from '../../auth/permissions.ts'
import { MasterBulkImport } from '../../components/MasterBulkImport.tsx'

type ParentDefinition = { label: string; required: boolean; load: () => Promise<{ id: string; code: string; name: string }[]> }
type MasterDefinition = { title: string; singular: string; subtitle: string; nameLabel: string; parent?: ParentDefinition }

const definitions: Record<string, MasterDefinition> = {
  'holding-companies': { title: 'Holding Companies', singular: 'holding company', subtitle: 'Manage the companies that own or group your organization.', nameLabel: 'Company name' },
  'lines-of-business': { title: 'LOB', singular: 'LOB', subtitle: 'Manage lines of business used across the workspace.', nameLabel: 'LOB name', parent: { label: 'Holding Company', required: false, load: async () => listHoldingCompanies({ isActive: true }) } },
  organisations: { title: 'Organizations', singular: 'organization', subtitle: 'Manage the organizations available to your people.', nameLabel: 'Organization name' },
  departments: { title: 'Departments', singular: 'department', subtitle: 'Manage the units employees are assigned to.', nameLabel: 'Department name' },
  'sub-departments': { title: 'Sub Departments', singular: 'sub department', subtitle: 'Manage sub departments under a department.', nameLabel: 'Sub department name', parent: { label: 'Department', required: true, load: async () => listDepartments({ isActive: true }) } },
  sections: { title: 'Sections', singular: 'section', subtitle: 'Manage sections under sub departments.', nameLabel: 'Section name', parent: { label: 'Sub Department', required: false, load: async () => listSubDepartments({ isActive: true }) } },
  'sub-sections': { title: 'Sub Sections', singular: 'sub section', subtitle: 'Manage sub sections under sections.', nameLabel: 'Sub section name', parent: { label: 'Section', required: false, load: async () => listSections({ isActive: true }) } },
  functions: { title: 'Functions', singular: 'function', subtitle: 'Manage organizational functions.', nameLabel: 'Function name' },
  'sub-functions': { title: 'Sub Functions', singular: 'sub function', subtitle: 'Manage sub functions under functions.', nameLabel: 'Sub function name', parent: { label: 'Function', required: false, load: async () => listFunctions({ isActive: true }) } },
  grades: { title: 'Grades', singular: 'grade', subtitle: 'Manage employee grades and levels.', nameLabel: 'Grade name' },
  designations: { title: 'Designations', singular: 'designation', subtitle: 'Manage the job titles employees hold.', nameLabel: 'Designation name' },
  'employee-types': { title: 'Employee Types', singular: 'employee type', subtitle: 'Manage employee type classifications.', nameLabel: 'Employee type name' },
  countries: { title: 'Countries', singular: 'country', subtitle: 'View the shared country reference data.', nameLabel: 'Country name' },
  'work-locations': { title: 'Work Locations', singular: 'work location', subtitle: 'Manage work locations available to employees.', nameLabel: 'Work location name' },
  'cost-centers': { title: 'Cost Centers', singular: 'cost center', subtitle: 'Manage cost centers used for employment records.', nameLabel: 'Cost center name' },
  'position-change-reasons': { title: 'Change Reasons', singular: 'change reason', subtitle: 'Manage reasons for employment changes.', nameLabel: 'Reason name' },
}

const groups: { title: string; items: [string, string][] }[] = [
  { title: 'Organization', items: [['holding-companies', 'Holding Companies'], ['lines-of-business', 'LOB'], ['organisations', 'Organizations'], ['departments', 'Departments'], ['sub-departments', 'Sub Departments'], ['sections', 'Sections'], ['sub-sections', 'Sub Sections']] },
  { title: 'Classification', items: [['functions', 'Functions'], ['sub-functions', 'Sub Functions'], ['grades', 'Grades'], ['designations', 'Designations'], ['employee-types', 'Employee Types']] },
  { title: 'Workplace', items: [['countries', 'Countries'], ['work-locations', 'Work Locations'], ['cost-centers', 'Cost Centers']] },
  { title: 'Employment', items: [['position-change-reasons', 'Change Reasons']] },
]
const bulkKinds = new Set(['holding-companies', 'lines-of-business', 'organisations', 'departments', 'sub-departments', 'sections', 'sub-sections', 'functions', 'sub-functions', 'grades', 'designations', 'employee-types', 'work-locations', 'cost-centers', 'position-change-reasons'])

export function MasterManagementPage() {
  const { kind = 'holding-companies' } = useParams()
  const [searchParams] = useSearchParams()
  const definition = definitions[kind]
  const { can } = useAuth()
  const canView = kind === 'departments' ? can(Permissions.department.view) : kind === 'designations' ? can(Permissions.designation.view) : can(Permissions.geography.view)
  const canCreate = kind === 'departments' ? can(Permissions.department.create) : kind === 'designations' ? can(Permissions.designation.create) : can(Permissions.geography.manage) && kind !== 'countries'
  const canEdit = kind === 'departments' ? can(Permissions.department.edit) : kind === 'designations' ? can(Permissions.designation.edit) : can(Permissions.geography.manage) && kind !== 'countries'
  const canDelete = kind === 'departments' ? can(Permissions.department.delete) : kind === 'designations' ? can(Permissions.designation.delete) : can(Permissions.geography.manage) && kind !== 'countries'
  const canBulkImport = bulkKinds.has(kind) && can(Permissions.geography.manage)
  const [rows, setRows] = useState<MasterRecord[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [catalogueSearch, setCatalogueSearch] = useState('')
  const [status, setStatus] = useState('')
  const [editing, setEditing] = useState<MasterRecord | null>(null)
  const [editorOpen, setEditorOpen] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [form, setForm] = useState<MasterRequest>({ code: '', name: '', description: '', isActive: true, parentId: null })
  const [parents, setParents] = useState<{ id: string; code: string; name: string }[]>([])
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [loading, setLoading] = useState(true)
  const closeRef = useRef<HTMLButtonElement>(null)
  const triggerRef = useRef<HTMLElement | null>(null)
  const intentHandledRef = useRef(false)
  const visibleGroups = useMemo(() => groups.map(group => ({ ...group, items: group.items.filter(([key, label]) => label.toLowerCase().includes(catalogueSearch.toLowerCase().trim()) && (key !== 'departments' || can(Permissions.department.view)) && (key !== 'designations' || can(Permissions.designation.view))) })).filter(group => group.items.length > 0), [catalogueSearch, can])
  const totalPages = Math.max(1, Math.ceil(totalCount / 25))

  useEffect(() => { intentHandledRef.current = false; setPage(1); setEditing(null); setEditorOpen(false); setDirty(false); setError(null) }, [kind])
  useEffect(() => {
    if (!definition?.parent) { setParents([]); return }
    let cancelled = false
    void definition.parent.load().then(value => { if (!cancelled) setParents(value) }).catch(() => { if (!cancelled) setError('Unable to load parent options.') })
    return () => { cancelled = true }
  }, [definition])
  useEffect(() => {
    if (!editorOpen) return
    closeRef.current?.focus()
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === 'Escape') closeEditor() }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [editorOpen])

  async function refresh(nextPage = page) {
    if (!definition) return
    setLoading(true)
    try { const result = await listManagedMasters(kind, { page: nextPage, pageSize: 25, search: search || undefined, isActive: status === '' ? undefined : status === 'active' }); setRows(result.items); setTotalCount(result.totalCount); setError(null) }
    catch (caught) { setRows([]); setTotalCount(0); setError(caught instanceof ApiError ? caught.message : 'Unable to load master records.') }
    finally { setLoading(false) }
  }
  useEffect(() => { void refresh() }, [kind, page, search, status])
  useEffect(() => {
    const requestedId = searchParams.get('edit')
    if (!intentHandledRef.current && requestedId && !editorOpen && canEdit) {
      const record = rows.find(row => row.id === requestedId)
      if (record) { intentHandledRef.current = true; startEdit(record) }
    } else if (!intentHandledRef.current && searchParams.get('add') === '1' && !editorOpen && canCreate) { intentHandledRef.current = true; startNew() }
  }, [rows, searchParams, editorOpen, canEdit, canCreate])

  if (!definition) return <Notice tone="error">This master is not available. Location is not supported by the current data model.</Notice>
  if (!canView) return <Notice tone="error">You do not have permission to view this master.</Notice>
  function startNew(element?: HTMLElement) { triggerRef.current = element ?? null; setEditing(null); setEditorOpen(true); setDirty(false); setForm({ code: '', name: '', description: '', isActive: true, parentId: null }); setError(null); setInfo(null) }
  function startEdit(row: MasterRecord, element?: HTMLElement) { triggerRef.current = element ?? null; setEditing(row); setEditorOpen(true); setDirty(false); setForm({ code: row.code, name: row.name, description: row.description ?? '', isActive: row.isActive, parentId: row.parentId ?? null }); setError(null); setInfo(null) }
  function closeEditor() { if (dirty && !window.confirm('Discard unsaved changes?')) return; setEditorOpen(false); setEditing(null); setDirty(false); window.setTimeout(() => triggerRef.current?.focus(), 0) }
  async function submit(event: FormEvent) { event.preventDefault(); setSaving(true); setError(null); setInfo(null); try { if (editing) await updateManagedMaster(kind, editing.id, form); else await createManagedMaster(kind, form); setEditorOpen(false); setEditing(null); setDirty(false); setInfo('Saved successfully. If the record does not match the current search or status filter, it will remain hidden.'); await refresh() } catch (caught) { setError(caught instanceof ApiError ? caught.message : 'Unable to save master record.') } finally { setSaving(false) } }
  async function remove(row: MasterRecord) { if (!window.confirm(`Deactivate ${row.code} — ${row.name}? Existing references will be preserved.`)) return; setError(null); try { await deleteManagedMaster(kind, row.id); if (editing?.id === row.id) closeEditor(); const nextPage = rows.length === 1 && page > 1 ? page - 1 : page; setPage(nextPage); await refresh(nextPage) } catch (caught) { setError(caught instanceof ApiError ? caught.message : 'Unable to deactivate master record.') } }

  return <div className="master-management-page">
    <PageHeader title="Master management" subtitle="Maintain the reference data behind your people operations." actions={<div className="form-actions">{canBulkImport ? <MasterBulkImport kind={kind} onCompleted={() => refresh()} /> : null}{canCreate ? <button className="button button-primary" type="button" onClick={event => startNew(event.currentTarget)}>+ Add {definition.singular}</button> : null}</div>} />
    <p className="master-breadcrumb"><Link to="/dashboard">Workspace</Link> / <Link to="/masters/holding-companies">Masters</Link> / {definition.title}</p>
    {error ? <Notice tone="error">{error}</Notice> : null}{info ? <Notice tone="success">{info}</Notice> : null}
    <div className={`master-workspace${editorOpen ? ' has-editor' : ''}`}>
      <Card className="master-catalogue" title="Masters"><label className="sr-only" htmlFor="master-search">Search masters</label><input id="master-search" className="input" type="search" placeholder="Search masters" value={catalogueSearch} onChange={event => setCatalogueSearch(event.target.value)} /><nav className="master-links" aria-label="Master catalogue">{visibleGroups.map(group => <div className="master-link-group" key={group.title}><h3>{group.title}</h3>{group.items.map(([key, label]) => key === 'departments' || key === 'designations' ? <Link key={key} to={`/${key}`} className={key === kind ? 'is-active' : ''}>{label}</Link> : <Link key={key} to={`/masters/${key}`} className={key === kind ? 'is-active' : ''}>{label}</Link>)}</div>)}<p className="master-unsupported">Location management is unavailable because no Location master is defined.</p>{kind === 'countries' ? <p className="master-unsupported">Countries are shared reference data and are read-only here.</p> : null}</nav></Card>
      <Card className="master-records" title={`${definition.title} records`} subtitle={`${totalCount} ${totalCount === 1 ? 'record' : 'records'}`}><div className="toolbar master-toolbar"><label className="sr-only" htmlFor="record-search">Search code or name</label><input id="record-search" className="input" type="search" placeholder="Search code or name" value={search} onChange={event => { setPage(1); setSearch(event.target.value) }} /><select className="input select" aria-label="Status filter" value={status} onChange={event => { setPage(1); setStatus(event.target.value) }}><option value="">All statuses</option><option value="active">Active</option><option value="inactive">Inactive</option></select></div>{loading ? <p className="muted">Loading records…</p> : error ? <p className="state-error">Records could not be loaded.</p> : rows.length === 0 ? <p className="muted">{search ? 'No records match this search.' : 'No records have been added yet.'}</p> : <><div className="table-wrap"><table className="data-table"><thead><tr><th>Code</th><th>{definition.nameLabel}</th>{definition.parent ? <th>Parent</th> : null}<th>Status</th><th>Actions</th></tr></thead><tbody>{rows.map(row => <tr className={editing?.id === row.id ? 'is-editing' : ''} key={row.id}><td><code className="master-code">{row.code}</code></td><td><span aria-hidden="true">◇ </span>{row.name}</td>{definition.parent ? <td>{row.parentCode ? `${row.parentCode} — ${row.parentName}` : '—'}</td> : null}<td><ActiveBadge isActive={row.isActive} /></td><td className="master-row-actions">{canEdit || canDelete ? <>{canEdit ? <button className="row-action" type="button" onClick={event => startEdit(row, event.currentTarget)} aria-label={`Edit ${row.code} ${row.name}`}>✎ Edit</button> : null}{canDelete ? <button className="row-action row-action-danger" type="button" onClick={() => void remove(row)} aria-label={`Delete ${row.code} ${row.name}`}>♲ Delete</button> : null}</> : <span className="muted">—</span>}</td></tr>)}</tbody></table></div><div className="master-list-footer"><span>Showing {(page - 1) * 25 + 1}–{Math.min(page * 25, totalCount)} of {totalCount}</span><div><button className="button button-secondary" type="button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button><button className="button button-secondary" type="button" disabled={page >= totalPages} onClick={() => setPage(value => value + 1)}>Next</button></div></div></>}</Card>
      {canEdit || canCreate ? editorOpen ? <Card className="master-editor" title={editing ? `Edit ${definition.singular}` : `Add ${definition.singular}`} subtitle={editing?.name}><button ref={closeRef} className="master-editor-close" type="button" onClick={closeEditor} aria-label="Close editor">×</button><form className="form-stack" onSubmit={submit}><label className="field"><span>Code <em>(required)</em></span><input required maxLength={20} value={form.code} onChange={event => { setDirty(true); setForm({ ...form, code: event.target.value }) }} /></label><label className="field"><span>{definition.nameLabel} <em>(required)</em></span><input required maxLength={200} value={form.name} onChange={event => { setDirty(true); setForm({ ...form, name: event.target.value }) }} /></label>{definition.parent ? <label className="field"><span>{definition.parent.label}{definition.parent.required ? ' (required)' : ''}</span><select required={definition.parent.required} value={form.parentId ?? ''} onChange={event => { setDirty(true); setForm({ ...form, parentId: event.target.value || null }) }}><option value="">{definition.parent.required ? 'Select parent' : 'No parent'}</option>{parents.map(parent => <option key={parent.id} value={parent.id}>{parent.code} — {parent.name}</option>)}</select></label> : null}<label className="field"><span>Description</span><textarea value={form.description ?? ''} onChange={event => { setDirty(true); setForm({ ...form, description: event.target.value }) }} /></label><label className="checkbox-field"><input type="checkbox" checked={form.isActive} onChange={event => { setDirty(true); setForm({ ...form, isActive: event.target.checked }) }} /> Active</label><p className="field-help">Available for new selections.</p><div className="form-actions"><button className="button button-primary" disabled={saving}>{saving ? 'Saving…' : editing ? '✓ Save changes' : '✓ Create'}</button><button className="button button-secondary" type="button" onClick={closeEditor}>Cancel</button></div></form></Card> : null : null}
    </div>
  </div>
}
