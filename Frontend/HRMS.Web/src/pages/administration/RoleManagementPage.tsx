import { useEffect, useState } from 'react'
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
    <PageHeader title="Role Management" subtitle="Manage user roles, effective dates, organizational scope, and assignment history." actions={canManage ? <button className="button button-primary" type="button" onClick={() => setAssigning(true)}>Assign Role</button> : undefined} />
    {notice && <p className="form-success" role="status">{notice}</p>}
    <div className="role-tabs" role="tablist" aria-label="Role management views">
      {(['assignments', 'roles', 'history'] as const).map((value) => <button key={value} className={tab === value ? 'role-tab is-active' : 'role-tab'} role="tab" aria-selected={tab === value} type="button" onClick={() => selectTab(value)}>{titleCase(value)}</button>)}
    </div>
    {tab === 'assignments' && <AssignmentsView assignments={assignments} all={assignments} roles={roles} loading={loading} error={error} canManage={canManage} search={search} setSearch={(value) => { setPage(1); setSearch(value) }} roleFilter={roleFilter} setRoleFilter={(value) => { setPage(1); setRoleFilter(value) }} statusFilter={statusFilter} setStatusFilter={(value) => { setPage(1); setStatusFilter(value) }} sourceFilter={sourceFilter} setSourceFilter={(value) => { setPage(1); setSourceFilter(value) }} page={page} pageSize={pageSize} totalCount={totalCount} setPage={setPage} onRetry={() => void loadAssignments()} onSelect={setSelected} onAssign={() => setAssigning(true)} revoked={revoked} />}
    {tab === 'roles' && <RolesView roles={roles} loading={loading} />}
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
      <label className="field role-search"><span>Search employee/user</span><input className="input" value={props.search} onChange={(e) => props.setSearch(e.target.value)} placeholder="Name, code, or user ID" /></label>
      <label className="field"><span>Role</span><select className="select" value={props.roleFilter} onChange={(e) => props.setRoleFilter(e.target.value)}><option value="">All roles</option>{props.roles.map((role) => <option key={role.id} value={role.id}>{role.name}</option>)}</select></label>
      <label className="field"><span>Status</span><select className="select" value={props.statusFilter} onChange={(e) => props.setStatusFilter(e.target.value)}><option value="">All statuses</option>{(['Active', 'Scheduled', 'Expired', 'Revoked'] as Status[]).map((value) => <option key={value}>{value}</option>)}</select></label>
      <label className="field"><span>Source</span><select className="select" value={props.sourceFilter} onChange={(e) => props.setSourceFilter(e.target.value)}><option value="">All sources</option><option value="System">System</option><option value="Manual">Manual</option></select></label>
      <button className="button button-secondary role-clear" type="button" onClick={() => { clear(); props.setPage(1) }}>Clear filters</button>
    </div>
    {props.error ? <div className="role-error" role="alert">{props.error.message}<button className="button button-secondary" type="button" onClick={props.onRetry}>Retry</button></div> : props.loading ? <p className="role-loading" role="status">Loading assignments…</p> : props.assignments.length === 0 ? <div className="role-empty"><h2>No role assignments found.</h2><p>Try clearing the filters or assign the first manual role.</p>{props.canManage && <button className="button button-primary" type="button" onClick={props.onAssign}>Assign Role</button>}</div> : <><div className="table-scroll"><table className="data-table role-table"><caption className="sr-only">Role assignments</caption><thead><tr>{['Employee / User', 'Employee Code', 'Role', 'Assignment Type', 'Scope', 'Effective Period', 'Status', 'Source', 'Actions'].map((heading) => <th key={heading}>{heading}</th>)}</tr></thead><tbody>{props.assignments.map((assignment) => { const status = statusOf(assignment, props.revoked); return <tr key={assignment.assignmentId} onClick={() => props.onSelect(assignment)}><td><strong>{assignment.employeeName ?? 'Unlinked user'}</strong><small>{assignment.userId}</small></td><td>{assignment.employeeCode ?? '—'}</td><td>{assignment.roleName} {assignment.isSystemManaged && <Badge>System managed</Badge>}</td><td>{assignment.isSystemManaged ? 'Automatic' : 'Manual'}</td><td>{assignment.scopeSummary ?? scopeText(assignment.scopes)}</td><td>{formatDate(assignment.effectiveFrom)} – {formatDate(assignment.effectiveTo)}</td><td><Badge tone={badgeTone(status)}>{status}</Badge></td><td>{assignment.assignmentSource}</td><td><button type="button" className="button-link" onClick={(e) => { e.stopPropagation(); props.onSelect(assignment) }}>View</button></td></tr> })}</tbody></table></div><div className="role-pagination" aria-label="Role assignment pagination"><span>{props.totalCount} assignment{props.totalCount === 1 ? '' : 's'}</span><button className="button button-secondary" type="button" disabled={props.page <= 1} onClick={() => props.setPage(props.page - 1)}>Previous</button><span>Page {props.page} of {Math.max(1, Math.ceil(props.totalCount / props.pageSize))}</span><button className="button button-secondary" type="button" disabled={props.page >= Math.ceil(props.totalCount / props.pageSize)} onClick={() => props.setPage(props.page + 1)}>Next</button></div></>}
  </div>
}

function RolesView({ roles, loading }: { roles: RoleSummary[]; loading: boolean }) {
  return <div className="card role-card"><div className="role-section-heading"><div><h2>Role catalogue</h2><p>Canonical roles are managed by the platform.</p></div></div>{loading ? <p role="status">Loading roles…</p> : <div className="table-scroll"><table className="data-table"><thead><tr><th>Role Name</th><th>Management Type</th><th>Scope Support</th><th>Status</th><th>Description</th></tr></thead><tbody>{roles.map((role) => { const system = SYSTEM_ROLES.has(role.name); return <tr key={role.id}><td><strong>{role.name}</strong></td><td><Badge tone={system ? 'neutral' : 'info'}>{system ? 'System managed' : 'Manual'}</Badge></td><td>{system || ['IT', 'Accounts', 'Super HR'].includes(role.name) ? 'Tenant-wide' : 'Scoped / tenant-wide'}</td><td><Badge tone="success">Active</Badge></td><td>{role.description ?? '—'}</td></tr> })}</tbody></table></div>}</div>
}

function HistoryView({ history, loading, canHistory }: { history: RoleAssignmentHistory[]; loading: boolean; canHistory: boolean }) {
  if (!canHistory) return <div className="role-empty"><h2>History is restricted</h2><p>Your account does not have RoleAssignment.ViewHistory.</p></div>
  return <div className="card role-card"><div className="role-section-heading"><div><h2>Immutable assignment history</h2><p>Events are read-only and remain available after an assignment expires.</p></div></div>{loading ? <p role="status">Loading history…</p> : history.length === 0 ? <div className="role-empty"><h2>No history found.</h2></div> : <div className="table-scroll"><table className="data-table"><thead><tr><th>Date / Time</th><th>Employee / User</th><th>Role</th><th>Event</th><th>Effective Period</th><th>Source</th><th>Performed By</th><th>Reason</th></tr></thead><tbody>{history.sort((a, b) => b.occurredAtUtc.localeCompare(a.occurredAtUtc)).map((event) => <tr key={event.eventId}><td>{new Date(event.occurredAtUtc).toLocaleString()}</td><td>{event.userId ?? 'User'}</td><td>{event.roleName}</td><td><Badge tone={event.eventType === 'Revoked' ? 'danger' : 'info'}>{event.eventType}</Badge></td><td>{formatDate(event.effectiveFrom)} – {formatDate(event.effectiveTo)}</td><td>{event.assignmentSource}</td><td>{event.performedByUserId ?? '—'}</td><td>{event.reason ?? '—'}</td></tr>)}</tbody></table></div>}</div>
}

function AssignDialog({ roles, onClose, onSaved }: { roles: RoleSummary[]; onClose: () => void; onSaved: (assignment: RoleAssignment) => void }) {
  const [users, setUsers] = useState<RoleManagementCandidate[]>([]); const [userId, setUserId] = useState(''); const [roleId, setRoleId] = useState(''); const [from, setFrom] = useState(today()); const [to, setTo] = useState(''); const [reason, setReason] = useState(''); const [scopeType, setScopeType] = useState<RoleScopeType | ''>(''); const [scopeValue, setScopeValue] = useState(''); const [options, setOptions] = useState<MasterOption[]>([]); const [scopes, setScopes] = useState<RoleAssignmentScope[]>([]); const [busy, setBusy] = useState(false); const [error, setError] = useState('')
  useEffect(() => { void listRoleCandidates({ page: 1, pageSize: 100 }).then((result) => setUsers(result.items)).catch((caught) => setError(toApiError(caught).message)) }, [])
  const role = roles.find((item) => String(item.id) === roleId); const scopeTypes = role ? (SCOPE_TYPES[role.name] ?? []) : []
  useEffect(() => { setScopeType(''); setScopeValue(''); setScopes([]) }, [roleId])
  useEffect(() => { let active = true; if (!scopeType) { setOptions([]); return } const loaders: Partial<Record<RoleScopeType, () => Promise<MasterOption[]>>> = { HoldingCompany: getHoldingCompanies, Lob: getLinesOfBusiness, Organisation: getOrganisations, Department: getDepartments, Location: getWorkLocations, WorkLocation: getWorkLocations, Grade: getGrades, Designation: getDesignations, EmployeeType: getEmployeeTypes, CostCenter: getCostCenters }; void loaders[scopeType]?.().then((items) => { if (active) setOptions(items) }).catch(() => { if (active) setOptions([]) }); return () => { active = false } }, [scopeType])
  const addScope = () => { if (!scopeType || !scopeValue || scopes.some((scope) => scope.scopeType === scopeType && scope.scopeEntityId === scopeValue)) return; setScopes([...scopes, { scopeType, scopeEntityId: scopeValue }]); setScopeValue('') }
  async function submit() { setError(''); if (!userId || !roleId || !from || (to && to < from)) { setError('Select a user and role, and ensure Effective To is not before Effective From.'); return } setBusy(true); try { onSaved(await assignRole(userId, { roleId: Number(roleId), effectiveFrom: from, effectiveTo: to || null, reason: reason.trim() || null, scopes: scopes.length ? scopes : null })) } catch (caught) { const apiError = toApiError(caught); setError(apiError.isConflict ? 'This role already has an overlapping effective assignment.' : apiError.message) } finally { setBusy(false) } }
  return <Modal title="Assign Role" onClose={busy ? () => undefined : onClose} footer={<><button className="button button-secondary" type="button" onClick={onClose} disabled={busy}>Cancel</button><button className="button button-primary" type="button" onClick={() => void submit()} disabled={busy || users.length === 0}>{busy ? 'Saving…' : 'Assign Role'}</button></>}>
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
