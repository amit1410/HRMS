import { useEffect, useMemo, useState } from 'react'
import { toApiError, type ApiError } from '../../api/errors.ts'
import { getPageAccessHistory, getPageAccessMatrix, listPageAccessRoles, updatePageAccess, type PageAccessHistoryPage, type PageAccessMatrix, type PageAccessPage, type PageAccessRole } from '../../api/pageAccess.ts'
import { getUserAccessPreview, searchAccessPreviewUsers, type UserAccessPreview } from '../../api/pageAccessPreview.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { PageHeader } from '../../components/PageHeader.tsx'

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
  async function save() { if (!roleId) return; try { setMatrix(await updatePageAccess(roleId, [...selected])); setNotice('Page access saved.'); setError(undefined) } catch (caught) { setError(toApiError(caught)) } }
  async function loadPreviewUsers(value: string) { try { setPreviewUsers(await searchAccessPreviewUsers(value)) } catch (caught) { setError(toApiError(caught)) } }
  useEffect(() => { if (tab !== 'history') return; void getPageAccessHistory({ roleId, userId: historyUserId || undefined, eventType: historyEventType || undefined, fromDate: historyFromDate || undefined, toDate: historyToDate || undefined, page: historyPage, pageSize: 25 }).then(setHistory).catch((caught) => setError(toApiError(caught))) }, [tab, roleId, historyUserId, historyEventType, historyFromDate, historyToDate, historyPage])
  useEffect(() => { if (!previewUserId) return; void getPageAccessHistory({ userId: previewUserId, page: 1, pageSize: 5 }).then(setPreviewHistory).catch((caught) => setError(toApiError(caught))) }, [previewUserId])

  return <section className="page-access-management-page">
    <PageHeader title="Page Access Management" subtitle="Configure page permissions by role. Data scopes remain attached to effective role assignments." actions={canManage ? <button className="button button-primary" type="button" onClick={() => void save()}>Save Configuration</button> : undefined} />
    {error && <p className="form-error" role="alert">{error.message}</p>}
    {notice && <p className="form-success" role="status">{notice}</p>}
    <nav aria-label="Page Access sections"><button className="button" type="button" onClick={() => setTab('access')}>Page Access</button><button className="button" type="button" onClick={() => setTab('history')}>History</button></nav>
    <label className="field"><span>Role</span><select className="select" value={roleId ?? ''} onChange={(event) => setRoleId(Number(event.target.value))}><option value="" disabled>Select a role</option>{roles.map((role) => <option key={role.id} value={role.id}>{role.name}{role.name === 'Employee' || role.name === 'Manager' ? ' (System Role)' : ''}</option>)}</select></label>
    {tab === 'access' && <><section className="page-access-preview"><h2>Preview User Access</h2><div className="role-scope-row"><input className="input" placeholder="Search user" onChange={(event) => void loadPreviewUsers(event.target.value)} /><select className="select" value={previewUserId} onChange={async (event) => { setPreviewUserId(event.target.value); setPreview(event.target.value ? await getUserAccessPreview(event.target.value) : undefined) }}><option value="">Select a user</option>{previewUsers.map((user) => <option key={user.userId} value={user.userId}>{user.displayName} — {user.email}</option>)}</select></div>{preview && <div><p><strong>Effective roles:</strong> {preview.roles.map((role) => role.roleName).join(', ') || 'None'}</p><p><strong>Permissions:</strong> {preview.permissions.join(', ') || 'None'}</p><p><strong>Tenant-wide grants:</strong> {preview.tenantWideRoleCount}</p><p><strong>Manager access:</strong> {preview.hasManagerAccess ? 'Yes' : 'No'}</p><p><strong>Accessible pages:</strong> {preview.pages.map((page) => page.name).join(', ') || 'None'}</p><h3>Recent Access Changes</h3>{previewHistory?.items.map((item) => <p key={item.id}>{item.eventType}: {item.permissionCode ?? item.scopeDimension ?? 'Scope'} ({item.occurredAtUtc})</p>)}</div>}</section>{Object.entries(pagesByModule).map(([module, pages]) => <section className="page-access-module" key={module}><h2>{module}</h2>{pages.map((page) => <article className="page-access-row" key={page.code}><div><strong>{page.name}</strong><small>{page.route}</small></div><label><input type="checkbox" checked={page.requiredPermissions.every((permission) => selected.has(permission))} onChange={() => togglePage(page)} disabled={!canManage} /> View</label>{page.actions.map((action) => <label key={action.code}><input type="checkbox" checked={selected.has(action.permission)} onChange={() => toggle(action.permission)} disabled={!canManage} /> {action.name}</label>)}</article>)}</section>)}</>}
    {tab === 'history' && <section aria-label="Authorization history"><div className="role-scope-row"><label className="field"><span>User</span><select className="select" value={historyUserId} onChange={(event) => { setHistoryUserId(event.target.value); setHistoryPage(1) }}><option value="">All users</option>{previewUsers.map((user) => <option key={user.userId} value={user.userId}>{user.displayName}</option>)}</select></label><label className="field"><span>Event type</span><select className="select" value={historyEventType} onChange={(event) => { setHistoryEventType(event.target.value); setHistoryPage(1) }}><option value="">All changes</option><option value="RolePermissionGranted">Permission granted</option><option value="RolePermissionRevoked">Permission revoked</option><option value="RoleScopeAdded">Scope added</option><option value="RoleScopeRemoved">Scope removed</option></select></label><label className="field"><span>From</span><input className="input" type="date" value={historyFromDate} onChange={(event) => { setHistoryFromDate(event.target.value); setHistoryPage(1) }} /></label><label className="field"><span>To</span><input className="input" type="date" value={historyToDate} onChange={(event) => { setHistoryToDate(event.target.value); setHistoryPage(1) }} /></label></div><table><thead><tr><th>Date/Time</th><th>Action</th><th>Role</th><th>Permission/Scope</th><th>Old</th><th>New</th><th>Changed by</th><th>Reason</th></tr></thead><tbody>{history?.items.map((item) => <tr key={item.id}><td>{new Date(item.occurredAtUtc).toLocaleString()}</td><td>{item.eventType}</td><td>{item.roleName ?? item.roleId ?? '—'}</td><td>{item.permissionCode ?? `${item.scopeDimension ?? 'Scope'} ${item.scopeValueDisplay ?? item.scopeValueId ?? ''}`}</td><td>{item.oldValue ?? '—'}</td><td>{item.newValue ?? '—'}</td><td>{item.actorDisplayName ?? item.actorUserId}</td><td>{item.reason ?? '—'}</td></tr>)}</tbody></table><p>{history?.totalCount ?? 0} changes</p><button className="button" type="button" disabled={!history?.hasPreviousPage} onClick={() => setHistoryPage((page) => page - 1)}>Previous</button> <button className="button" type="button" disabled={!history?.hasNextPage} onClick={() => setHistoryPage((page) => page + 1)}>Next</button></section>}
  </section>
}
