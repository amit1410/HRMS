import { useEffect, useMemo, useState } from 'react'
import { toApiError, type ApiError } from '../../api/errors.ts'
import { getPageAccessHistory, getPageAccessMatrix, listPageAccessRoles, updatePageAccess, type PageAccessHistoryPage, type PageAccessMatrix, type PageAccessPage, type PageAccessRole } from '../../api/pageAccess.ts'
import { getUserAccessPreview, searchAccessPreviewUsers, type UserAccessPreview } from '../../api/pageAccessPreview.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { PageHeader } from '../../components/PageHeader.tsx'

const moduleDescriptions: Record<string, string> = {
  Dashboard: 'System overview and insights',
  Employee: 'Manage employee information',
  Leave: 'Manage leave requests and approvals',
  Attendance: 'Track employee attendance',
  Reports: 'View and export reports',
}

const moduleThemes = ['blue', 'green', 'purple', 'amber', 'rose']

function ModuleIcon({ module }: { module: string }) {
  const symbol = module.trim().charAt(0).toUpperCase() || '•'
  return <span className="page-access-module-icon" aria-hidden="true">{symbol}</span>
}

function PageAccessHeaderIcon() {
  return <svg aria-hidden="true" viewBox="0 0 24 24" className="page-access-header-icon" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8"><path d="M12 3 4.5 6v5.5c0 4.4 3 7.6 7.5 9.5 4.5-1.9 7.5-5.1 7.5-9.5V6L12 3Z" /><path d="m8.5 12 2.2 2.2 4.8-5" /></svg>
}

function AccessTabIcon({ history }: { history?: boolean }) {
  return history ? <HistoryIcon /> : <svg aria-hidden="true" viewBox="0 0 24 24" className="page-access-tab-icon" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8"><rect x="4" y="4" width="6" height="6" rx="1" /><rect x="14" y="4" width="6" height="6" rx="1" /><rect x="4" y="14" width="6" height="6" rx="1" /><rect x="14" y="14" width="6" height="6" rx="1" /></svg>
}

function SaveIcon() {
  return <svg aria-hidden="true" viewBox="0 0 24 24" className="page-access-icon"><path d="M5 4h11l3 3v13H5z" /><path d="M8 4v6h8V4M8 20v-6h8v6" /></svg>
}

function HistoryIcon() {
  return <svg aria-hidden="true" viewBox="0 0 24 24" className="page-access-tab-icon"><path d="M4 12a8 8 0 1 0 2.3-5.7" /><path d="M4 5v4h4M12 7v5l3 2" /></svg>
}

function ResetIcon() {
  return <svg aria-hidden="true" viewBox="0 0 24 24" className="page-access-history-icon"><path d="M4 7v5h5M5 12a7 7 0 1 0 2-5" /><path d="M12 8v4l2 2" /></svg>
}

function CalendarIcon() {
  return <svg aria-hidden="true" viewBox="0 0 24 24" className="page-access-history-icon"><rect x="4" y="5" width="16" height="15" rx="2" /><path d="M8 3v4M16 3v4M4 10h16" /></svg>
}

function eventLabel(eventType: string) {
  const labels: Record<string, string> = {
    RolePermissionGranted: 'Role Permission Granted',
    RolePermissionRevoked: 'Role Permission Revoked',
    RoleScopeAdded: 'Role Scope Added',
    RoleScopeRemoved: 'Role Scope Removed',
  }
  return labels[eventType] ?? eventType.replace(/([a-z])([A-Z])/g, '$1 $2')
}

function eventTone(eventType: string) {
  if (eventType.includes('Revoked') || eventType.includes('Removed')) return 'danger'
  if (eventType.includes('Granted') || eventType.includes('Added')) return 'success'
  return 'info'
}

function AuditActionBadge({ eventType }: { eventType: string }) {
  const tone = eventTone(eventType)
  return <span className={`audit-action-badge audit-action-${tone}`}><span aria-hidden="true">{tone === 'success' ? '+' : tone === 'danger' ? '−' : '•'}</span>{eventLabel(eventType)}<span className="sr-only">{eventType}</span></span>
}

function AuditValue({ value }: { value?: string | null }) {
  if (!value) return <span className="audit-empty-value">—</span>
  const normalized = value.toLowerCase()
  const tone = normalized === 'granted' || normalized === 'added' ? 'success' : normalized === 'revoked' || normalized === 'removed' ? 'danger' : ''
  return tone ? <span className={`audit-value-badge audit-value-${tone}`}>{value}</span> : <span>{value}</span>
}

export function PageAccessManagementPage() {
  const { can } = useAuth()
  const canManage = can(Permissions.pageAccess.manage)
  const [roles, setRoles] = useState<PageAccessRole[]>([])
  const [roleId, setRoleId] = useState<number>()
  const [matrix, setMatrix] = useState<PageAccessMatrix>()
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [error, setError] = useState<ApiError>()
  const [notice, setNotice] = useState('')
  const [previewUsers, setPreviewUsers] = useState<Awaited<ReturnType<typeof searchAccessPreviewUsers>>>([])
  const [previewUserId, setPreviewUserId] = useState('')
  const [preview, setPreview] = useState<UserAccessPreview>()
  const [tab, setTab] = useState<'access' | 'history'>('access')
  const [history, setHistory] = useState<PageAccessHistoryPage>()
  const [historyEventType, setHistoryEventType] = useState('')
  const [historyUserId, setHistoryUserId] = useState('')
  const [historyFromDate, setHistoryFromDate] = useState('')
  const [historyToDate, setHistoryToDate] = useState('')
  const [historyPage, setHistoryPage] = useState(1)
  const [previewHistory, setPreviewHistory] = useState<PageAccessHistoryPage>()

  useEffect(() => { void listPageAccessRoles().then((items) => { setRoles(items); if (items[0]) setRoleId(items[0].id) }).catch((caught) => setError(toApiError(caught))) }, [])
  useEffect(() => { if (roleId === undefined) return; void getPageAccessMatrix(roleId).then((value) => { setMatrix(value); setSelected(new Set(value.grantedPermissions)); setNotice('') }).catch((caught) => setError(toApiError(caught))) }, [roleId])

  const pagesByModule = useMemo(() => [...(matrix?.pages ?? [])].reduce<Record<string, PageAccessPage[]>>((groups, page) => { (groups[page.moduleCode] ??= []).push(page); return groups }, {}), [matrix])
  function toggle(permission: string) { setSelected((current) => { const next = new Set(current); if (next.has(permission)) next.delete(permission); else next.add(permission); return next }) }
  function togglePage(page: PageAccessPage) {
    const permissions = [...page.requiredPermissions, ...page.actions.map((action) => action.permission)]
    const shouldGrant = permissions.some((permission) => !selected.has(permission))
    setSelected((current) => { const next = new Set(current); permissions.forEach((permission) => shouldGrant ? next.add(permission) : next.delete(permission)); return next })
  }
  const [isSaving, setIsSaving] = useState(false)
  async function save() { if (!roleId || isSaving) return; setIsSaving(true); try { setMatrix(await updatePageAccess(roleId, [...selected])); setNotice('Page access saved.'); setError(undefined) } catch (caught) { setError(toApiError(caught)) } finally { setIsSaving(false) } }
  async function loadPreviewUsers(value: string) { try { setPreviewUsers(await searchAccessPreviewUsers(value)) } catch (caught) { setError(toApiError(caught)) } }
  useEffect(() => { if (tab !== 'history') return; void getPageAccessHistory({ roleId, userId: historyUserId || undefined, eventType: historyEventType || undefined, fromDate: historyFromDate || undefined, toDate: historyToDate || undefined, page: historyPage, pageSize: 25 }).then(setHistory).catch((caught) => setError(toApiError(caught))) }, [tab, roleId, historyUserId, historyEventType, historyFromDate, historyToDate, historyPage])
  useEffect(() => { if (!previewUserId) return; void getPageAccessHistory({ userId: previewUserId, page: 1, pageSize: 5 }).then(setPreviewHistory).catch((caught) => setError(toApiError(caught))) }, [previewUserId])

  return <section className="page-access-management-page">
    <div className="page-access-header-card">
      <div className="page-access-header-content"><span className="page-access-header-icon-box"><PageAccessHeaderIcon /></span>
      <PageHeader title="Page Access Management" subtitle="Configure page permissions by role. Data scopes remain attached to effective role assignments." actions={canManage ? <button className="button button-primary page-access-save" type="button" onClick={() => void save()} disabled={isSaving}><SaveIcon />{isSaving ? 'Saving…' : 'Save Configuration'}</button> : undefined} />
      </div>
    </div>
    {error && <p className="form-error" role="alert">{error.message}</p>}
    {notice && <p className="form-success" role="status">{notice}</p>}
    <nav className="page-access-tabs" aria-label="Page Access sections"><button className={tab === 'access' ? 'is-active' : ''} type="button" onClick={() => setTab('access')}><AccessTabIcon />Page Access</button><button className={tab === 'history' ? 'is-active' : ''} type="button" onClick={() => setTab('history')}><AccessTabIcon history />History</button></nav>
    {tab === 'access' && <><section className="page-access-toolbar" aria-label="Page access filters"><label className="field page-access-role"><span>Role</span><select className="select" aria-label="Role" value={roleId ?? ''} onChange={(event) => setRoleId(Number(event.target.value))}><option value="" disabled>Select a role</option>{roles.map((role) => <option key={role.id} value={role.id}>{role.name}{role.name === 'Employee' || role.name === 'Manager' ? ' (System Role)' : ''}</option>)}</select></label><div className="page-access-toolbar-divider" aria-hidden="true" /><div className="page-access-preview-filter"><div className="page-access-filter-heading"><span>Preview User Access</span><small>Read-only effective access</small></div><div className="page-access-preview-controls"><div className="page-access-search"><span aria-hidden="true">⌕</span><input className="input" aria-label="Search user" placeholder="Search user" onChange={(event) => void loadPreviewUsers(event.target.value)} /></div><select className="select" aria-label="Select a preview user" value={previewUserId} onChange={async (event) => { setPreviewUserId(event.target.value); setPreview(event.target.value ? await getUserAccessPreview(event.target.value) : undefined) }}><option value="">Select a user</option>{previewUsers.map((user) => <option key={user.userId} value={user.userId}>{user.displayName} — {user.email}</option>)}</select></div></div></section>{preview && <section className="page-access-preview-result" aria-label="Preview results"><div><strong>Effective roles:</strong><span>{preview.roles.map((role) => role.roleName).join(', ') || 'None'}</span></div><div><strong>Permissions:</strong><span>{preview.permissions.join(', ') || 'None'}</span></div><div><strong>Tenant-wide grants:</strong><span>{preview.tenantWideRoleCount}</span></div><div><strong>Manager access:</strong><span>{preview.hasManagerAccess ? 'Yes' : 'No'}</span></div><div><strong>Accessible pages:</strong><span>{preview.pages.map((page) => page.name).join(', ') || 'None'}</span></div><div className="page-access-preview-history"><h3>Recent Access Changes</h3>{previewHistory?.items.map((item) => <p key={item.id}>{item.eventType}: {item.permissionCode ?? item.scopeDimension ?? 'Scope'} ({item.occurredAtUtc})</p>)}</div></section>}{Object.entries(pagesByModule).map(([module, pages], moduleIndex) => <section className={`page-access-module page-access-module-${moduleThemes[moduleIndex % moduleThemes.length]}`} key={module}><header className="page-access-module-header"><ModuleIcon module={module} /><div><h2>{module}</h2><p>{moduleDescriptions[module] ?? 'Manage access for this application area'}</p></div></header><div className="page-access-module-pages">{pages.map((page) => <article className="page-access-row" key={page.code}><div className="page-access-page-heading"><strong>{page.name}</strong><small>{page.route}</small></div><div className="page-access-permissions"><label><input type="checkbox" checked={page.requiredPermissions.every((permission) => selected.has(permission))} onChange={() => togglePage(page)} disabled={!canManage} /> <span>View</span></label>{page.actions.map((action) => <label key={action.code}><input type="checkbox" checked={selected.has(action.permission)} onChange={() => toggle(action.permission)} disabled={!canManage} /> <span>{action.name}</span></label>)}</div></article>)}</div></section>)}</>}
    {tab === 'history' && <section className="page-access-history-card" aria-label="Authorization history"><header className="page-access-history-header"><div className="page-access-history-title"><span className="page-access-history-title-icon"><HistoryIcon /></span><div><h2>Audit history</h2><p>Showing role permission and scope changes</p></div></div><span className="page-access-record-count">{history?.totalCount ?? 0} {history?.totalCount === 1 ? 'record' : 'records'}</span></header><div className="page-access-history-filters"><label className="page-access-history-filter"><span>User</span><select className="select" value={historyUserId} onChange={(event) => { setHistoryUserId(event.target.value); setHistoryPage(1) }}><option value="">All users</option>{previewUsers.map((user) => <option key={user.userId} value={user.userId}>{user.displayName}</option>)}</select></label><label className="page-access-history-filter"><span>Event type</span><select className="select" value={historyEventType} onChange={(event) => { setHistoryEventType(event.target.value); setHistoryPage(1) }}><option value="">All changes</option><option value="RolePermissionGranted">Permission granted</option><option value="RolePermissionRevoked">Permission revoked</option><option value="RoleScopeAdded">Scope added</option><option value="RoleScopeRemoved">Scope removed</option></select></label><label className="page-access-history-filter"><span>From</span><span className="page-access-date-input"><CalendarIcon /><input className="input" type="date" value={historyFromDate} onChange={(event) => { setHistoryFromDate(event.target.value); setHistoryPage(1) }} /></span></label><label className="page-access-history-filter"><span>To</span><span className="page-access-date-input"><CalendarIcon /><input className="input" type="date" value={historyToDate} onChange={(event) => { setHistoryToDate(event.target.value); setHistoryPage(1) }} /></span></label><button className="button button-secondary page-access-reset" type="button" onClick={() => { setHistoryUserId(''); setHistoryEventType(''); setHistoryFromDate(''); setHistoryToDate(''); setHistoryPage(1) }}><ResetIcon />Reset filters</button></div><div className="page-access-history-table-scroll"><table className="page-access-history-table"><caption className="sr-only">Page access audit history</caption><thead><tr><th scope="col">Date / Time</th><th scope="col">Action</th><th scope="col">Role</th><th scope="col">Permission / Scope</th><th scope="col">Old</th><th scope="col">New</th><th scope="col">Changed By</th><th scope="col">Reason</th></tr></thead><tbody>{history?.items.map((item) => <tr key={item.id}><td className="audit-date">{new Date(item.occurredAtUtc).toLocaleString()}</td><td><AuditActionBadge eventType={item.eventType} /></td><td>{item.roleName ?? item.roleId ?? '—'}</td><td className="audit-permission">{item.permissionCode ?? `${item.scopeDimension ?? 'Scope'} ${item.scopeValueDisplay ?? item.scopeValueId ?? ''}`}</td><td><AuditValue value={item.oldValue} /></td><td><AuditValue value={item.newValue} /></td><td>{item.actorDisplayName ?? item.actorUserId}</td><td>{item.reason || <span className="audit-empty-value">—</span>}</td></tr>)}</tbody></table></div><footer className="page-access-history-footer"><span>{history?.totalCount ?? 0} {history?.totalCount === 1 ? 'record' : 'records'}</span><div className="page-access-history-pagination"><button className="button button-secondary" type="button" aria-label="Previous page" disabled={!history?.hasPreviousPage} onClick={() => setHistoryPage((page) => page - 1)}>‹</button><span>Page {history?.page ?? historyPage} of {history?.totalPages ?? 1}</span><button className="button button-secondary" type="button" aria-label="Next page" disabled={!history?.hasNextPage} onClick={() => setHistoryPage((page) => page + 1)}>›</button></div></footer></section>}
  </section>
}
