import { useEffect, useMemo, useState } from 'react'
import { toApiError, type ApiError } from '../../api/errors.ts'
import { getCostCenters, getDepartments, getDesignations, getEmployeeTypes, getGrades, getHoldingCompanies, getLinesOfBusiness, getOrganisations, getWorkLocations } from '../../api/roleManagementMasters.ts'
import {
  assignRole,
  getUserRoleHistory,
  listAssignments,
  listRoleCandidates,
  listRoles,
  revokeRole,
  type RoleAssignment,
  type RoleAssignmentHistory,
  type RoleAssignmentScope,
  type RoleManagementCandidate,
  type RoleScopeType,
  type RoleSummary,
} from '../../api/roleAssignments.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Badge, type BadgeTone } from '../../components/Badge.tsx'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { Modal } from '../../components/Modal.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'

type Tab = 'assignments' | 'roles' | 'history'
type Status = 'Active' | 'Scheduled' | 'Expired' | 'Revoked'
type MasterOption = { id: string; name: string; code: string }

const SYSTEM_ROLES = new Set(['Employee', 'Manager'])
const SCOPE_TYPES: Record<string, RoleScopeType[]> = {
  HRBP: ['HoldingCompany', 'Lob', 'Organisation', 'Department', 'Location', 'Grade', 'Designation', 'EmployeeType', 'CostCenter'],
  'Employee Relationship Officer': ['Organisation', 'Department', 'Location', 'Grade', 'Designation', 'EmployeeType', 'CostCenter'],
  'Time Manager': ['Organisation', 'Department', 'Location', 'WorkLocation', 'Grade', 'Designation', 'EmployeeType', 'CostCenter'],
}
const SCOPE_LABELS: Record<RoleScopeType, string> = {
  HoldingCompany: 'Head Company', Lob: 'LOB', Organisation: 'Organization', Department: 'Department',
  SubDepartment: 'Sub-department', Section: 'Section', SubSection: 'Sub-section', Function: 'Function',
  SubFunction: 'Sub-function', Country: 'Country', Location: 'Location', WorkLocation: 'Work Location', CostCenter: 'Cost Center', Grade: 'Grade', Designation: 'Designation', EmployeeType: 'Employee type',
}

function today(): string {
  const date = new Date()
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 10)
}

function statusOf(assignment: RoleAssignment, revoked: Set<string>): Status {
  if (revoked.has(assignment.assignmentId)) return 'Revoked'
  return assignment.status
}

function badgeTone(status: Status): BadgeTone {
  return status === 'Active' ? 'success' : status === 'Scheduled' ? 'info' : status === 'Revoked' ? 'danger' : 'neutral'
}

function formatDate(value?: string | null): string {
  if (!value) return 'Ongoing'
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(`${value}T00:00:00`))
}

function titleCase(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1)
}

function scopeText(scopes: RoleAssignmentScope[]): string {
  return scopes.length === 0 ? 'Tenant-wide' : scopes.map((scope) => `${SCOPE_LABELS[scope.scopeType]}: ${scope.scopeEntityId.slice(0, 8)}`).join(', ')
}

function UserPlusIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="role-management-icon"><path d="M15 20v-1a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v1M8.5 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8ZM19 8v6M16 11h6" /></svg> }
function SearchIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="role-management-control-icon"><circle cx="10.5" cy="10.5" r="6.5" /><path d="m16 16 5 5" /></svg> }
function ResetIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="role-management-control-icon"><path d="M4 7v5h5M5 12a7 7 0 1 0 2-5" /></svg> }
function CalendarIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="role-management-control-icon"><rect x="4" y="5" width="16" height="15" rx="2" /><path d="M8 3v4M16 3v4M4 10h16" /></svg> }
function AssignRoleModalIcon() { return <span className="role-assign-modal-icon"><UserPlusIcon /></span> }
function initials(name?: string | null) { return (name ?? 'Unlinked user').split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]).join('').toUpperCase() || 'UU' }
function historyEventTone(eventType: string) { return eventType === 'Revoked' ? 'danger' : eventType === 'Expired' ? 'neutral' : eventType === 'Assigned' ? 'info' : 'purple' }
function RoleHistoryEventBadge({ eventType }: { eventType: string }) { const tone = historyEventTone(eventType); return <span className={`role-history-event role-history-event-${tone}`}><span aria-hidden="true">{eventType === 'Revoked' ? '−' : eventType === 'Assigned' ? '✓' : '•'}</span>{eventType}</span> }

export function RoleManagementPage() {
  const { can } = useAuth()
  const canManage = can(Permissions.roleManagement.assignmentManage)
  const canHistory = can(Permissions.roleManagement.assignmentViewHistory)
  const [tab, setTab] = useState<Tab>('assignments')
  const [roles, setRoles] = useState<RoleSummary[]>([])
  const [assignments, setAssignments] = useState<RoleAssignment[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const pageSize = 20
  const [history, setHistory] = useState<RoleAssignmentHistory[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<ApiError | null>(null)
  const [search, setSearch] = useState('')
  const [roleFilter, setRoleFilter] = useState('')
  const [statusFilter, setStatusFilter] = useState('')
  const [sourceFilter, setSourceFilter] = useState('')
  const [revoked, setRevoked] = useState<Set<string>>(new Set())
  const [selected, setSelected] = useState<RoleAssignment | null>(null)
  const [assigning, setAssigning] = useState(false)
  const [historyLoading, setHistoryLoading] = useState(false)
  const [notice, setNotice] = useState('')
  const [roleSearch, setRoleSearch] = useState('')

  async function loadAssignments() {
    setLoading(true); setError(null)
    try {
      const result = await listAssignments({ search: search || undefined, roleId: roleFilter ? Number(roleFilter) : undefined, status: (statusFilter || undefined) as Status | undefined, source: (sourceFilter || undefined) as 'System' | 'Manual' | undefined, page, pageSize })
      setAssignments(result.items); setTotalCount(result.totalCount)
    } catch (caught) { setError(toApiError(caught)) }
    finally { setLoading(false) }
  }

  useEffect(() => { void listRoles(false).then(setRoles).catch((caught) => setError(toApiError(caught))) }, [])
  useEffect(() => { void loadAssignments() }, [search, roleFilter, statusFilter, sourceFilter, page])

  async function loadHistory() {
    if (!canHistory || history.length > 0) return
    setHistoryLoading(true)
    try { setHistory((await Promise.all(assignments.map((assignment) => getUserRoleHistory(assignment.userId)))).flat()) }
    catch (caught) { setError(toApiError(caught)) }
    finally { setHistoryLoading(false) }
  }

  function selectTab(next: Tab) { setTab(next); if (next === 'history') void loadHistory() }

  return <section className="role-management-page">
    <div className="role-management-header-card"><PageHeader title="Role Management" subtitle="Manage user roles, effective dates, organizational scope, and assignment history." actions={canManage ? <button className="button button-primary role-assign-button" type="button" onClick={() => setAssigning(true)}><UserPlusIcon />Assign Role</button> : undefined} /></div>
    {notice && <p className="form-success" role="status">{notice}</p>}
    <div className="role-tabs" role="tablist" aria-label="Role management views">
      {(['assignments', 'roles', 'history'] as const).map((value) => <button key={value} className={tab === value ? 'role-tab is-active' : 'role-tab'} role="tab" aria-selected={tab === value} type="button" onClick={() => selectTab(value)}><span className="role-tab-icon" aria-hidden="true">{value === 'assignments' ? '◉' : value === 'roles' ? '◆' : '◷'}</span>{titleCase(value)}</button>)}
    </div>
    {tab === 'assignments' && <AssignmentsView assignments={assignments} all={assignments} roles={roles} loading={loading} error={error} canManage={canManage} search={search} setSearch={(value) => { setPage(1); setSearch(value) }} roleFilter={roleFilter} setRoleFilter={(value) => { setPage(1); setRoleFilter(value) }} statusFilter={statusFilter} setStatusFilter={(value) => { setPage(1); setStatusFilter(value) }} sourceFilter={sourceFilter} setSourceFilter={(value) => { setPage(1); setSourceFilter(value) }} page={page} pageSize={pageSize} totalCount={totalCount} setPage={setPage} onRetry={() => void loadAssignments()} onSelect={setSelected} onAssign={() => setAssigning(true)} revoked={revoked} />}
    {tab === 'roles' && <RolesView roles={roles} loading={loading} search={roleSearch} setSearch={setRoleSearch} />}
    {tab === 'history' && <HistoryView history={history} loading={historyLoading} canHistory={canHistory} />}
    {assigning && <AssignDialog roles={roles.filter((role) => !SYSTEM_ROLES.has(role.name))} onClose={() => setAssigning(false)} onSaved={() => { setAssigning(false); setNotice('Role assigned successfully.'); void loadAssignments() }} />}
    {selected && <AssignmentDetails assignment={selected} all={assignments} canManage={canManage} canHistory={canHistory} onClose={() => setSelected(null)} onRevoked={(id) => { setRevoked((current) => new Set(current).add(id)); setSelected(null); setNotice('Role revoked successfully.'); void loadAssignments() }} />}
  </section>
}

function AssignmentsView(props: {
  assignments: RoleAssignment[]; all: RoleAssignment[]; roles: RoleSummary[]; loading: boolean; error: ApiError | null; canManage: boolean; search: string; setSearch: (v: string) => void; roleFilter: string; setRoleFilter: (v: string) => void; statusFilter: string; setStatusFilter: (v: string) => void; sourceFilter: string; setSourceFilter: (v: string) => void; page: number; pageSize: number; totalCount: number; setPage: (v: number) => void; onRetry: () => void; onSelect: (a: RoleAssignment) => void; onAssign: () => void; revoked: Set<string>
}) {
  const clear = () => { props.setSearch(''); props.setRoleFilter(''); props.setStatusFilter(''); props.setSourceFilter('') }
  return <div className="card role-card">
    <div className="role-filter-bar">
      <label className="field role-search"><span>Search employee/user</span><span className="role-search-input"><SearchIcon /><input className="input" value={props.search} onChange={(e) => props.setSearch(e.target.value)} placeholder="Name, code, or user ID" /></span></label>
      <label className="field"><span>Role</span><select className="select" value={props.roleFilter} onChange={(e) => props.setRoleFilter(e.target.value)}><option value="">All roles</option>{props.roles.map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}</select></label>
      <label className="field"><span>Status</span><select className="select" value={props.statusFilter} onChange={(e) => props.setStatusFilter(e.target.value)}><option value="">All statuses</option>{(['Active', 'Scheduled', 'Expired', 'Revoked'] as Status[]).map((value) => <option key={value}>{value}</option>)}</select></label>
      <label className="field"><span>Source</span><select className="select" value={props.sourceFilter} onChange={(e) => props.setSourceFilter(e.target.value)}><option value="">All sources</option><option value="System">System</option><option value="Manual">Manual</option></select></label>
      <button className="button button-secondary role-clear" type="button" onClick={() => { clear(); props.setPage(1) }}><ResetIcon />Clear filters</button>
    </div>
    {props.error ? <div className="role-error" role="alert">{props.error.message}<button className="button button-secondary" type="button" onClick={props.onRetry}>Retry</button></div> : props.loading ? <p className="role-loading" role="status">Loading assignments…</p> : props.assignments.length === 0 ? <div className="role-empty"><h2>No role assignments found.</h2><p>Try changing your search or filters.</p><div className="role-empty-actions"><button className="button button-secondary" type="button" onClick={() => { clear(); props.setPage(1) }}><ResetIcon />Clear filters</button>{props.canManage && <button className="button button-primary" type="button" onClick={props.onAssign}><UserPlusIcon />Assign Role</button>}</div></div> : <><div className="table-scroll"><table className="data-table role-table"><caption className="sr-only">Role assignments</caption><thead><tr>{['Employee / User', 'Employee Code', 'Role', 'Assignment Type', 'Scope', 'Effective Period', 'Status', 'Source', 'Actions'].map((heading) => <th key={heading}>{heading}</th>)}</tr></thead><tbody>{props.assignments.map((assignment) => { const status = statusOf(assignment, props.revoked); return <tr key={assignment.assignmentId} onClick={() => props.onSelect(assignment)}><td><div className="role-person-cell"><span className="role-avatar" aria-hidden="true">{initials(assignment.employeeName)}</span><span><strong>{assignment.employeeName ?? 'Unlinked user'}</strong><small>{assignment.userId}</small></span></div></td><td className="role-code-cell">{assignment.employeeCode ?? '—'}</td><td><span className="role-name-cell">{assignment.roleName}</span>{assignment.isSystemManaged && <Badge>System managed</Badge>}</td><td><span className={`role-assignment-type role-assignment-${assignment.isSystemManaged ? 'automatic' : 'manual'}`}><span aria-hidden="true">{assignment.isSystemManaged ? '⚙' : '♙'}</span>{assignment.isSystemManaged ? 'Automatic' : 'Manual'}</span></td><td><span className="role-scope-cell"><span aria-hidden="true">◉</span>{assignment.scopeSummary ?? scopeText(assignment.scopes)}</span></td><td><span className="role-period-cell"><CalendarIcon /><span>{formatDate(assignment.effectiveFrom)}<br /><span className="role-period-separator">to</span> {formatDate(assignment.effectiveTo)}</span></span></td><td><Badge tone={badgeTone(status)}>{status}</Badge></td><td><span className="role-source-cell">{assignment.assignmentSource}</span></td><td><button type="button" className="button button-secondary role-view-button" onClick={(e) => { e.stopPropagation(); props.onSelect(assignment) }}>View</button></td></tr> })}</tbody></table></div><div className="role-pagination" aria-label="Role assignment pagination"><span>{props.totalCount} assignment{props.totalCount === 1 ? '' : 's'}</span><div className="role-pagination-controls"><button className="button button-secondary" type="button" aria-label="Previous page" disabled={props.page <= 1} onClick={() => props.setPage(props.page - 1)}>‹</button><span>Page {props.page} of {Math.max(1, Math.ceil(props.totalCount / props.pageSize))}</span><button className="button button-secondary" type="button" aria-label="Next page" disabled={props.page >= Math.ceil(props.totalCount / props.pageSize)} onClick={() => props.setPage(props.page + 1)}>›</button></div></div></>}
  </div>
}

function RolesView({ roles, loading, search, setSearch }: { roles: RoleSummary[]; loading: boolean; search: string; setSearch: (value: string) => void }) {
  const normalizedSearch = search.trim().toLowerCase()
  const filteredRoles = roles.filter((role) => !normalizedSearch || role.name.toLowerCase().includes(normalizedSearch) || (role.description ?? '').toLowerCase().includes(normalizedSearch))
  return <div className="card role-card role-catalogue-card"><header className="role-catalogue-header"><div className="role-catalogue-title"><span className="role-catalogue-icon" aria-hidden="true">◆</span><div><h2>Role catalogue</h2><p>Canonical roles are managed by the platform.</p></div></div><div className="role-catalogue-tools"><label className="role-catalogue-search"><span className="sr-only">Search roles by name or description</span><SearchIcon /><input className="input" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search roles by name or description..." /></label><span className="role-count-badge">{filteredRoles.length} {filteredRoles.length === 1 ? 'role' : 'roles'}</span></div></header>{loading ? <p className="role-loading" role="status">Loading roles…</p> : filteredRoles.length === 0 ? <div className="role-empty"><h2>No roles found.</h2><p>Try another role name or description.</p><button className="button button-secondary" type="button" onClick={() => setSearch('')}><ResetIcon />Clear search</button></div> : <div className="table-scroll"><table className="data-table role-catalogue-table"><caption className="sr-only">Role catalogue</caption><thead><tr><th scope="col">Role Name</th><th scope="col">Management Type</th><th scope="col">Scope Support</th><th scope="col">Status</th><th scope="col">Description</th></tr></thead><tbody>{filteredRoles.map((role) => { const system = SYSTEM_ROLES.has(role.name); return <tr key={role.id}><td><strong>{role.name}</strong></td><td><Badge tone={system ? 'neutral' : 'info'}>{system ? 'System managed' : 'Manual'}</Badge></td><td><span className="role-catalogue-scope"><span aria-hidden="true">◉</span>{system || ['IT', 'Accounts', 'Super HR'].includes(role.name) ? 'Tenant-wide' : 'Scoped / tenant-wide'}</span></td><td><Badge tone="success">Active</Badge></td><td className="role-catalogue-description">{role.description ?? '—'}</td></tr> })}</tbody></table></div>}</div>
}

function HistoryView({ history, loading, canHistory }: { history: RoleAssignmentHistory[]; loading: boolean; canHistory: boolean }) {
  const [from, setFrom] = useState(''); const [to, setTo] = useState(''); const [search, setSearch] = useState(''); const [role, setRole] = useState(''); const [eventType, setEventType] = useState(''); const [source, setSource] = useState('')
  const roleOptions = useMemo(() => [...new Set(history.map((event) => event.roleName))].sort(), [history])
  const eventOptions = useMemo(() => [...new Set(history.map((event) => event.eventType))].sort(), [history])
  const sourceOptions = useMemo(() => [...new Set(history.map((event) => event.assignmentSource))].sort(), [history])
  const filteredHistory = useMemo(() => history.filter((item) => { const query = search.trim().toLowerCase(); const occurredDate = item.occurredAtUtc.slice(0, 10); const userText = `${item.userId ?? ''} ${item.performedByUserId ?? ''}`.toLowerCase(); return (!from || occurredDate >= from) && (!to || occurredDate <= to) && (!query || userText.includes(query)) && (!role || item.roleName === role) && (!eventType || item.eventType === eventType) && (!source || item.assignmentSource === source) }).sort((a, b) => b.occurredAtUtc.localeCompare(a.occurredAtUtc)), [history, from, to, search, role, eventType, source])
  const clear = () => { setFrom(''); setTo(''); setSearch(''); setRole(''); setEventType(''); setSource('') }
  if (!canHistory) return <div className="role-empty"><h2>History is restricted</h2><p>Your account does not have RoleAssignment.ViewHistory.</p></div>
  return <div className="card role-card role-history-card"><header className="role-history-header"><div className="role-history-title"><span className="role-history-icon" aria-hidden="true">◷</span><div><h2>Immutable assignment history</h2><p>Events are read-only and remain available after an assignment expires.</p></div></div><span className="role-count-badge">{filteredHistory.length} {filteredHistory.length === 1 ? 'event' : 'events'}</span></header><div className="role-history-filters"><label className="role-history-filter"><span>From</span><span className="role-history-date"><CalendarIcon /><input className="input" type="date" value={from} onChange={(event) => setFrom(event.target.value)} /></span></label><label className="role-history-filter"><span>To</span><span className="role-history-date"><CalendarIcon /><input className="input" type="date" value={to} onChange={(event) => setTo(event.target.value)} /></span></label><label className="role-history-filter role-history-search"><span>Employee / User</span><span className="role-search-input"><SearchIcon /><input className="input" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Name, code, or user ID" /></span></label><label className="role-history-filter"><span>Role</span><select className="select" value={role} onChange={(event) => setRole(event.target.value)}><option value="">All roles</option>{roleOptions.map((value) => <option key={value}>{value}</option>)}</select></label><label className="role-history-filter"><span>Event</span><select className="select" value={eventType} onChange={(event) => setEventType(event.target.value)}><option value="">All events</option>{eventOptions.map((value) => <option key={value}>{value}</option>)}</select></label><label className="role-history-filter"><span>Source</span><select className="select" value={source} onChange={(event) => setSource(event.target.value)}><option value="">All sources</option>{sourceOptions.map((value) => <option key={value}>{value}</option>)}</select></label><button className="button button-secondary role-history-clear" type="button" onClick={clear}><ResetIcon />Clear filters</button></div>{loading ? <p className="role-loading" role="status">Loading history…</p> : filteredHistory.length === 0 ? <div className="role-empty"><h2>No assignment history found.</h2><p>Try changing the filters.</p><button className="button button-secondary" type="button" onClick={clear}><ResetIcon />Clear filters</button></div> : <><div className="table-scroll"><table className="data-table role-history-table"><caption className="sr-only">Immutable assignment history</caption><thead><tr><th scope="col">Date / Time</th><th scope="col">Employee / User</th><th scope="col">Role</th><th scope="col">Event</th><th scope="col">Effective Period</th><th scope="col">Source</th><th scope="col">Performed By</th><th scope="col">Reason</th></tr></thead><tbody>{filteredHistory.map((event) => <tr key={event.eventId}><td className="role-history-date-cell">{new Date(event.occurredAtUtc).toLocaleString()}</td><td><span className="role-person-cell"><span className="role-avatar" aria-hidden="true">{initials(event.userId)}</span><span><strong>{event.userId ?? 'Unlinked user'}</strong><small>{event.userId ? 'User ID' : 'Unlinked user'}</small></span></span></td><td className="role-history-role">{event.roleName}</td><td><RoleHistoryEventBadge eventType={event.eventType} /></td><td><span className="role-period-cell"><CalendarIcon /><span>{formatDate(event.effectiveFrom)}<br /><span className="role-period-separator">to</span> {formatDate(event.effectiveTo)}</span></span></td><td><span className="role-source-cell">{event.assignmentSource}</span></td><td className="role-history-actor">{event.performedByUserId ?? '—'}</td><td className="role-history-reason">{event.reason ?? '—'}</td></tr>)}</tbody></table></div><footer className="role-pagination role-history-pagination"><span>{filteredHistory.length} {filteredHistory.length === 1 ? 'event' : 'events'}</span><span>All loaded history</span></footer></>}</div>
}

function AssignDialog({ roles, onClose, onSaved }: { roles: RoleSummary[]; onClose: () => void; onSaved: (assignment: RoleAssignment) => void }) {
  const [users, setUsers] = useState<RoleManagementCandidate[]>([]); const [userId, setUserId] = useState(''); const [roleId, setRoleId] = useState(''); const [from, setFrom] = useState(today()); const [to, setTo] = useState(''); const [reason, setReason] = useState(''); const [scopeType, setScopeType] = useState<RoleScopeType | ''>(''); const [scopeValue, setScopeValue] = useState(''); const [options, setOptions] = useState<MasterOption[]>([]); const [scopes, setScopes] = useState<RoleAssignmentScope[]>([]); const [busy, setBusy] = useState(false); const [error, setError] = useState('')
  useEffect(() => { void listRoleCandidates({ page: 1, pageSize: 100 }).then((result) => setUsers(result.items)).catch((caught) => setError(toApiError(caught).message)) }, [])
  const role = roles.find((item) => String(item.id) === roleId); const scopeTypes = role ? (SCOPE_TYPES[role.name] ?? []) : []
  useEffect(() => { setScopeType(''); setScopeValue(''); setScopes([]) }, [roleId])
  useEffect(() => { let active = true; if (!scopeType) { setOptions([]); return } const loaders: Partial<Record<RoleScopeType, () => Promise<MasterOption[]>>> = { HoldingCompany: getHoldingCompanies, Lob: getLinesOfBusiness, Organisation: getOrganisations, Department: getDepartments, Location: getWorkLocations, WorkLocation: getWorkLocations, Grade: getGrades, Designation: getDesignations, EmployeeType: getEmployeeTypes, CostCenter: getCostCenters }; void loaders[scopeType]?.().then((items) => { if (active) setOptions(items) }).catch(() => { if (active) setOptions([]) }); return () => { active = false } }, [scopeType])
  const addScope = () => { if (!scopeType || !scopeValue || scopes.some((scope) => scope.scopeType === scopeType && scope.scopeEntityId === scopeValue)) return; setScopes([...scopes, { scopeType, scopeEntityId: scopeValue }]); setScopeValue('') }
  async function submit() { setError(''); if (!userId || !roleId || !from || (to && to < from)) { setError('Select a user and role, and ensure Effective To is not before Effective From.'); return } setBusy(true); try { onSaved(await assignRole(userId, { roleId: Number(roleId), effectiveFrom: from, effectiveTo: to || null, reason: reason.trim() || null, scopes: scopes.length ? scopes : null })) } catch (caught) { const apiError = toApiError(caught); setError(apiError.isConflict ? 'This role already has an overlapping effective assignment.' : apiError.message) } finally { setBusy(false) } }
  return <Modal title="Assign Role" subtitle="Grant a role with effective dates and scope." icon={<AssignRoleModalIcon />} className="role-assign-modal" onClose={busy ? () => undefined : onClose} footer={<><button className="button button-secondary" type="button" onClick={onClose} disabled={busy}>Cancel</button><button className="button button-primary" type="button" onClick={() => void submit()} disabled={busy || users.length === 0}><UserPlusIcon />{busy ? 'Saving…' : 'Assign Role'}</button></>}>
    {error && <p className="form-error" role="alert">{error}</p>}
    <div className="form-grid"><label className="field"><span>User / Employee<span className="field-required">*</span></span><select className="select" value={userId} onChange={(e) => setUserId(e.target.value)}><option value="">Select a user</option>{users.map((user) => <option key={user.userId} value={user.userId}>{user.displayName}{user.employeeCode ? ` (${user.employeeCode})` : ''}</option>)}</select></label><label className="field"><span>Role<span className="field-required">*</span></span><select className="select" value={roleId} onChange={(e) => setRoleId(e.target.value)}><option value="">Select a manually assignable role</option>{roles.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label><label className="field"><span>Effective From<span className="field-required">*</span></span><input className="input" type="date" value={from} onChange={(e) => setFrom(e.target.value)} required /></label><label className="field"><span>Effective To</span><input className="input" type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} /><small className="field-hint">Leave Effective To empty for an ongoing assignment.</small></label></div>
    {role?.name === 'Super HR' && <p className="role-warning" role="note"><strong>Super HR warning:</strong> this role has tenant-wide HR administration permissions.</p>}
    {scopeTypes.length === 0 ? <div className="role-tenant-wide"><strong>Tenant-wide access</strong><span>This role applies across the current tenant. No scope rows will be submitted.</span></div> : <fieldset className="role-scope-fieldset"><legend>Assignment Scope</legend><div className="role-scope-row"><label className="field"><span>Scope Type</span><select className="select" value={scopeType} onChange={(e) => setScopeType(e.target.value as RoleScopeType)}><option value="">Choose type</option>{scopeTypes.map((value) => <option key={value} value={value}>{SCOPE_LABELS[value]}</option>)}</select></label><label className="field"><span>Scope Value</span><select className="select" value={scopeValue} onChange={(e) => setScopeValue(e.target.value)} disabled={!scopeType}><option value="">Choose value</option>{options.map((option) => <option key={option.id} value={option.id}>{option.code} – {option.name}</option>)}</select></label><button className="button button-secondary" type="button" onClick={addScope} disabled={!scopeType || !scopeValue}>Add</button></div>{scopes.length > 0 && <ul className="role-chip-list">{scopes.map((scope) => <li key={`${scope.scopeType}-${scope.scopeEntityId}`}><span>{SCOPE_LABELS[scope.scopeType]}: {options.find((item) => item.id === scope.scopeEntityId)?.name ?? scope.scopeEntityId.slice(0, 8)}</span><button type="button" aria-label={`Remove ${SCOPE_LABELS[scope.scopeType]} scope`} onClick={() => setScopes(scopes.filter((item) => item !== scope))}>×</button></li>)}</ul>}</fieldset>}
    <label className="field"><span>Reason</span><textarea className="input textarea" maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Why is this role being assigned?" /></label>
  </Modal>
}

function AssignmentDetails({ assignment, all, canManage, canHistory, onClose, onRevoked }: { assignment: RoleAssignment; all: RoleAssignment[]; canManage: boolean; canHistory: boolean; onClose: () => void; onRevoked: (id: string) => void }) {
  const [showHistory, setShowHistory] = useState(false)
  const [revoking, setRevoking] = useState(false)
  const [history, setHistory] = useState<RoleAssignmentHistory[]>([])
  const related = all.filter((item) => item.userId === assignment.userId)
  const status = statusOf(assignment, new Set())
  const canRevoke = canManage && assignment.canRevoke && !assignment.isSystemManaged

  return <>
    <Modal title="Assignment details" onClose={onClose} footer={<><button className="button button-secondary" type="button" onClick={onClose}>Close</button>{canRevoke && <button className="button button-danger role-revoke-button" type="button" onClick={() => setRevoking(true)} data-revoke={assignment.assignmentId}>Revoke</button>}</>}>
      <dl className="role-details"><dt>User</dt><dd>{assignment.employeeName ?? assignment.userId}</dd><dt>Employee Code</dt><dd>{assignment.employeeCode ?? '—'}</dd><dt>Role</dt><dd>{assignment.roleName} {assignment.isSystemManaged && <Badge>System managed</Badge>}</dd><dt>Status</dt><dd><Badge tone={badgeTone(status)}>{status}</Badge></dd><dt>Source</dt><dd>{assignment.assignmentSource} · {assignment.isSystemManaged ? 'Automatic' : 'Manual'}</dd><dt>Effective Period</dt><dd>{formatDate(assignment.effectiveFrom)} – {formatDate(assignment.effectiveTo)}</dd><dt>Assigned By</dt><dd>{assignment.assignedByUserId ?? 'System'}</dd><dt>Reason</dt><dd>{assignment.reason ?? '—'}</dd><dt>Scope</dt><dd>{assignment.scopeSummary ?? scopeText(assignment.scopes)}</dd></dl>
      <h3>All roles for this user</h3><ul className="role-related-list">{related.map((item) => <li key={item.assignmentId}>{item.roleName} · {statusOf(item, new Set())} · {formatDate(item.effectiveFrom)}</li>)}</ul>
      {canHistory && <button className="button-link" type="button" onClick={() => { setShowHistory(true); void getUserRoleHistory(assignment.userId).then(setHistory) }}>View History</button>}
    </Modal>
    {showHistory && <Modal title="Assignment history" onClose={() => setShowHistory(false)} footer={<button className="button button-secondary" type="button" onClick={() => setShowHistory(false)}>Close</button>}>{history.length === 0 ? <p>No history recorded.</p> : history.map((event) => <p key={event.eventId}><strong>{event.eventType}</strong> · {new Date(event.occurredAtUtc).toLocaleString()} · {event.reason ?? 'No reason'}</p>)}</Modal>}
    {canRevoke && revoking && <RevokeDialog assignment={assignment} onClose={() => setRevoking(false)} onRevoked={onRevoked} />}
  </>
}

function RevokeDialog({ assignment, onClose, onRevoked }: { assignment: RoleAssignment; onClose: () => void; onRevoked: (id: string) => void }) {
  const [date, setDate] = useState(today())
  const [reason, setReason] = useState('')
  return <ConfirmDialog title="Revoke Role" message={`Revoke ${assignment.roleName} for ${assignment.employeeName ?? 'this user'}?`} hint={<><span>The assignment currently runs from {formatDate(assignment.effectiveFrom)} to {formatDate(assignment.effectiveTo)}. Revocation is inclusive.</span><label className="field"><span>Effective revocation date</span><input className="input" type="date" value={date} min={assignment.effectiveFrom} onChange={(event) => setDate(event.target.value)} /></label><label className="field"><span>Reason</span><textarea className="input textarea" value={reason} onChange={(event) => setReason(event.target.value)} /></label></>} confirmLabel="Revoke" onClose={onClose} onConfirm={async () => { const result = await revokeRole(assignment.assignmentId, { effectiveTo: date, reason: reason.trim() || null }); onRevoked(result.assignmentId) }} />
}
