import { useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import { activatePlatformTenant, createPlatformTenant, deactivatePlatformTenant, listPlatformTenants, resetTenantAdminPassword, retryPlatformTenant, updatePlatformTenant, type CreatePlatformTenantRequest, type PlatformTenant, type ResetTenantAdminPasswordResponse, type RetryPlatformTenantRequest } from '../../api/platformTenants.ts'
import { toApiError } from '../../api/errors.ts'
import { usePlatformAuth } from '../../auth/PlatformAuthProvider.tsx'
import { PlatformPermissions } from '../../auth/platformPermissions.ts'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { initials } from '../../lib/format.ts'

const initialForm: CreatePlatformTenantRequest = { tenantName: '', tenantCode: '', host: '', databaseProvider: 'MySql', shardKey: '', email: '', phone: '', address: '', firstName: '', lastName: '', initialAdminEmail: '' }
const initialRetry: RetryPlatformTenantRequest = { firstName: '', lastName: '', initialAdminEmail: '' }

function localTenantUrl(host: string): string { const url = new URL(window.location.href); url.hostname = host; return url.toString().replace(/\/$/, '') }

export function PlatformTenantsPage() {
  const { can, user, logout } = usePlatformAuth()
  const [tenants, setTenants] = useState<PlatformTenant[]>([])
  const [form, setForm] = useState(initialForm)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [created, setCreated] = useState<PlatformTenant | null>(null)
  const [editing, setEditing] = useState<PlatformTenant | null>(null)
  const [editHost, setEditHost] = useState('')
  const [editName, setEditName] = useState('')
  const [editEmail, setEditEmail] = useState('')
  const [editPhone, setEditPhone] = useState('')
  const [editAddress, setEditAddress] = useState('')
  const [inspecting, setInspecting] = useState<PlatformTenant | null>(null)
  const [retrying, setRetrying] = useState<PlatformTenant | null>(null)
  const [retryForm, setRetryForm] = useState(initialRetry)
  const [resetting, setResetting] = useState<PlatformTenant | null>(null)
  const [resetResult, setResetResult] = useState<ResetTenantAdminPasswordResponse | null>(null)
  const [resetAdminEmail, setResetAdminEmail] = useState('')
  const [statusChange, setStatusChange] = useState<{ tenant: PlatformTenant; target: 'Active' | 'Inactive' } | null>(null)
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<'All' | PlatformTenant['status']>('All')

  useEffect(() => {
    if (!can(PlatformPermissions.view)) return
    void listPlatformTenants().then(setTenants).catch((reason) => setError(toApiError(reason).message)).finally(() => setLoading(false))
  }, [can])

  useEffect(() => {
    const overlayOpen = inspecting !== null || editing !== null
    if (!overlayOpen) return
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      if (editing) return
      setInspecting(null)
    }
    document.addEventListener('keydown', closeOnEscape)
    return () => {
      document.body.style.overflow = previousOverflow
      document.removeEventListener('keydown', closeOnEscape)
    }
  }, [editing, inspecting])

  const update = (field: keyof CreatePlatformTenantRequest, value: string) => setForm((current) => ({ ...current, [field]: value }))
  const updateRetry = (field: keyof RetryPlatformTenantRequest, value: string) => setRetryForm((current) => ({ ...current, [field]: value }))
  const mysqlPreview = `HRMS_${form.shardKey.trim().toLowerCase() || '<shard-key>'}`
  const activeTenants = tenants.filter((tenant) => tenant.status === 'Active')
  const filteredTenants = useMemo(() => tenants.filter((tenant) => {
    const haystack = `${tenant.tenantCode} ${tenant.tenantName} ${tenant.host} ${tenant.shardKey}`.toLowerCase()
    return (statusFilter === 'All' || tenant.status === statusFilter) && haystack.includes(search.trim().toLowerCase())
  }), [search, statusFilter, tenants])

  function replaceTenant(updated: PlatformTenant) { setTenants((current) => current.map((tenant) => tenant.id === updated.id ? updated : tenant)) }
  async function submit(event: FormEvent) {
    event.preventDefault(); setError(''); setMessage(''); setCreated(null); setSaving(true)
    try { const tenant = await createPlatformTenant({ ...form, tenantCode: form.tenantCode.toUpperCase(), host: form.host.toLowerCase(), shardKey: form.shardKey.toLowerCase() }); setTenants((current) => [...current, tenant].sort((a, b) => a.tenantCode.localeCompare(b.tenantCode))); setCreated(tenant); setMessage('Tenant created successfully'); setForm(initialForm) } catch (reason) { const apiError = toApiError(reason); setError(apiError.message.includes('TenantCode is already in use') ? 'A tenant with this code already exists in Inactive state. Use Retry Provisioning instead of creating another tenant.' : apiError.message) } finally { setSaving(false) }
  }
  async function saveEdit(event: FormEvent) {
    event.preventDefault(); if (!editing) return; setError(''); setMessage(''); setSaving(true)
    try { const tenant = await updatePlatformTenant(editing.id, { tenantName: editName, host: editHost.toLowerCase(), email: editEmail || undefined, phone: editPhone || undefined, address: editAddress || undefined }); replaceTenant(tenant); setEditing(null); setMessage('Tenant details updated.') } catch (reason) { setError(toApiError(reason).message) } finally { setSaving(false) }
  }
  async function confirmStatusChange() {
    if (!statusChange) return
    setError(''); setMessage(''); setSaving(true)
    try { const requestedStatus = statusChange.target; const tenantId = statusChange.tenant.id; const tenant = requestedStatus === 'Active' ? await activatePlatformTenant(tenantId) : await deactivatePlatformTenant(tenantId); setTenants((current) => current.map((item) => item.id === tenantId ? { ...item, ...tenant, status: requestedStatus } : item)); setStatusChange(null); setMessage(`Tenant ${requestedStatus.toLowerCase()} successfully.`) } catch (reason) { setError(toApiError(reason).message) } finally { setSaving(false) }
  }
  async function retry(event: FormEvent) {
    event.preventDefault(); if (!retrying) return
    const preview = retrying.databaseProvider === 'MySql' ? `HRMS_${retrying.shardKey}` : 'Shared SQL Server HRMS'
    if (!window.confirm(`Retry provisioning for ${retrying.tenantCode} using ${retrying.databaseProvider} and shard ${retrying.shardKey}?\nDatabase preview: ${preview}`)) return
    setError(''); setMessage(''); setSaving(true)
    try { const tenant = await retryPlatformTenant(retrying.id, retryForm); replaceTenant(tenant); setRetrying(null); setRetryForm(initialRetry); setMessage('Tenant provisioning retry completed successfully.') } catch (reason) { setError(toApiError(reason).message) } finally { setSaving(false) }
  }
  async function resetAdminPassword() {
    if (!resetting) return; setError(''); setMessage(''); setSaving(true)
    try { const result = await resetTenantAdminPassword(resetting.id, resetAdminEmail.trim() || undefined); setResetResult(result); setResetting(null); setResetAdminEmail('') } catch (reason) { setError(toApiError(reason).message) } finally { setSaving(false) }
  }
  function closeResetResult() { setResetResult(null) }

  if (!can(PlatformPermissions.view)) return <p role="alert">You do not have permission to view platform tenants.</p>
  const total = tenants.length
  const mysql = tenants.filter((tenant) => tenant.databaseProvider === 'MySql').length
  const pending = tenants.filter((tenant) => tenant.status !== 'Active').length
  const adminName = user?.fullName || 'Platform Administrator'

  return <div className="platform-admin-shell">
    <aside className="platform-admin-sidebar" aria-label="Platform administration">
      <div className="platform-admin-brand"><span className="platform-admin-brand-mark">HR</span><span><strong>Anevra</strong><small>Platform</small></span></div>
      <nav className="platform-admin-nav" aria-label="Platform navigation">
        <span className="platform-admin-nav-label">Workspace</span>
        <span className="platform-admin-nav-item"><DashboardIcon /> Dashboard</span>
        <span className="platform-admin-nav-item is-active"><BuildingsIcon /> Tenants</span>
        <span className="platform-admin-nav-label">Administration</span>
        <span className="platform-admin-nav-item is-disabled"><UsersIcon /> Users <small>Soon</small></span>
        <span className="platform-admin-nav-item is-disabled"><ShieldIcon /> Roles &amp; Permissions <small>Soon</small></span>
        <span className="platform-admin-nav-item is-disabled"><SettingsIcon /> System Settings <small>Soon</small></span>
        <span className="platform-admin-nav-item is-disabled"><ActivityIcon /> Audit Logs <small>Soon</small></span>
      </nav>
      <div className="platform-admin-sidebar-footer"><span><HelpIcon /> Help &amp; support</span><small>Platform administration</small></div>
    </aside>
    <div className="platform-admin-main">
      <header className="platform-admin-header"><div className="platform-admin-header-context"><span className="platform-admin-header-dot" /> Platform control center</div><div className="platform-admin-user"><span className="platform-admin-avatar">{initials(adminName)}</span><span><strong>{adminName}</strong><small>{user?.roles?.[0] || 'Platform Administrator'}</small></span><button type="button" className="button button-secondary" onClick={() => void logout()}>Sign out</button></div></header>
      <main className="platform-admin-content">
        <PageHeader title="Development tenant administration" subtitle="Manage platform tenants and their workspaces" />
        {error && <div className="platform-admin-alert" role="alert"><AlertIcon /> <span>{error}</span></div>}{message && <div className="platform-admin-success" role="status"><CheckIcon /> <span>{message}</span></div>}
        {created && <div className="platform-admin-created card" role="status"><div><strong>Tenant created successfully</strong><p>{created.tenantCode} · {created.databaseProvider} · {created.status}</p></div><a href={localTenantUrl(created.host)}>Open Tenant <ArrowIcon /></a>{created.developmentTemporaryPassword && <p className="platform-admin-temp-password">Development temporary administrator password: <code>{created.developmentTemporaryPassword}</code></p>}</div>}

        <section className="platform-summary-grid" aria-label="Tenant summary">
          <SummaryCard icon={<BuildingsIcon />} label="Total Tenants" value={total} hint="All tenants in platform" />
          <SummaryCard icon={<CheckIcon />} label="Active Tenants" value={activeTenants.length} hint={`${total ? Math.round(activeTenants.length / total * 100) : 0}% active`} tone="green" />
          <SummaryCard icon={<DatabaseIcon />} label="MySQL Tenants" value={mysql} hint="Using MySQL provider" tone="blue" />
          <SummaryCard icon={<ClockIcon />} label="Pending / Inactive" value={pending} hint="Awaiting activation" tone="amber" />
        </section>

        <section className="platform-admin-card platform-tenant-list-card"><div className="platform-section-head"><div><h2>Platform tenants</h2><p>Create and inspect tenant workspaces. Provider credentials remain server-side.</p></div><div className="platform-tenant-filters"><label className="platform-search"><SearchIcon /><span className="sr-only">Search tenants</span><input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search tenants" /></label><label className="platform-filter"><span className="sr-only">Filter tenant status</span><select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)}><option value="All">All statuses</option><option value="Active">Active</option><option value="Inactive">Inactive</option><option value="Suspended">Suspended</option></select></label></div></div>{loading ? <div className="platform-loading"><Spinner size={18} label="Loading tenants…" /></div> : <div className="platform-table-wrap"><table className="platform-tenant-table"><thead><tr><th>Tenant Code</th><th>Tenant Name</th><th>Host</th><th>Provider</th><th>Shard Key</th><th>Status</th><th>URL</th><th>Actions</th></tr></thead><tbody>{filteredTenants.map((tenant) => <TenantRow key={tenant.id} tenant={tenant} canEdit={can(PlatformPermissions.create)} canChangeStatus={can(PlatformPermissions.updateStatus)} onInspect={() => { setEditing(null); setInspecting(tenant) }} onEdit={() => { setInspecting(null); setEditing(tenant); setEditHost(tenant.host); setEditName(tenant.tenantName); setEditEmail(tenant.email ?? ''); setEditPhone(tenant.phone ?? ''); setEditAddress(tenant.address ?? '') }} onRetry={() => { setRetrying(tenant); setRetryForm(initialRetry) }} onReset={() => { setResetting(tenant); setResetAdminEmail('') }} onStatusChange={(target) => setStatusChange({ tenant, target })} />)}</tbody></table>{filteredTenants.length === 0 && <p className="platform-empty-state">No tenants match the current filters.</p>}</div>}</section>

        {can(PlatformPermissions.create) && <form className="platform-admin-card platform-create-card" onSubmit={(event) => void submit(event)}><div className="platform-section-head"><div><h2>Create tenant</h2><p>Provision a new tenant workspace with its database and initial administrator.</p></div></div><div className="platform-create-grid"><FormSection title="Tenant Details" helper="Basic information about the tenant workspace."><Field label="Tenant Name" required value={form.tenantName} onChange={(value) => update('tenantName', value)} /><Field label="Tenant Code" required pattern="[A-Za-z0-9_-]{2,20}" value={form.tenantCode} onChange={(value) => update('tenantCode', value)} /><Field label="Host" required value={form.host} onChange={(value) => update('host', value)} /><SelectField label="Database Provider" required value={form.databaseProvider} onChange={(value) => update('databaseProvider', value)} options={[['SqlServer', 'SQL Server'], ['MySql', 'MySQL']]} /><Field label="Shard Key" required value={form.shardKey} onChange={(value) => update('shardKey', value)} /><div className="platform-database-preview"><DatabaseIcon /><span><strong>Database preview <small>(preview only)</small></strong><code>{form.databaseProvider === 'MySql' ? mysqlPreview : 'Shared SQL Server HRMS'}</code></span></div></FormSection><FormSection title="Contact Details" helper="Primary contact information for this tenant."><Field label="Contact Email" type="email" value={form.email} onChange={(value) => update('email', value)} /><Field label="Phone" value={form.phone} onChange={(value) => update('phone', value)} /><label className="platform-field"><span>Address</span><textarea value={form.address} onChange={(event) => update('address', event.target.value)} rows={4} /></label></FormSection><FormSection title="Initial Administrator" helper="Set up the initial administrator for this tenant."><Field label="First Name" required value={form.firstName} onChange={(value) => update('firstName', value)} /><Field label="Last Name" required value={form.lastName} onChange={(value) => update('lastName', value)} /><Field label="Email" required type="email" value={form.initialAdminEmail} onChange={(value) => update('initialAdminEmail', value)} /><p className="platform-form-note"><ShieldIcon /> Credentials are generated securely by the platform.</p></FormSection></div><div className="platform-form-actions"><button type="button" className="button button-secondary" onClick={() => setForm(initialForm)}>Cancel</button><button type="submit" className="button button-primary" disabled={saving}>{saving ? <><Spinner size={15} label="Provisioning…" /> Provisioning…</> : <><span>Create Tenant</span><ArrowIcon /></>}</button></div></form>}

        {(inspecting || editing) && <div className="platform-overlay-backdrop" aria-hidden="true" onClick={() => { if (inspecting) setInspecting(null) }} />}
        {inspecting && <div className="platform-overlay platform-inspect-overlay" role="dialog" aria-modal="true" aria-labelledby="inspect-tenant-title"><header className="platform-overlay-header"><div><span className="platform-overlay-kicker"><BuildingsIcon /> Tenant workspace</span><h2 id="inspect-tenant-title">Inspect Tenant</h2></div><button type="button" className="platform-overlay-close" aria-label="Close Inspect Tenant" onClick={() => setInspecting(null)}>×</button></header><div className="platform-overlay-body"><p className="platform-inspect-code">{inspecting.tenantCode}</p><dl className="platform-inspect-grid"><dt>Tenant Name</dt><dd>{inspecting.tenantName}</dd><dt>Host</dt><dd>{inspecting.host}</dd><dt>Provider</dt><dd>{inspecting.databaseProvider === 'MySql' ? 'MySQL' : 'SQL Server'}</dd><dt>Shard Key</dt><dd><code>{inspecting.shardKey}</code></dd><dt>Status</dt><dd><span className={`platform-status-pill is-${inspecting.status.toLowerCase()}`}><span />{inspecting.status}</span></dd><dt>Contact Email</dt><dd>{inspecting.email || '—'}</dd><dt>Phone</dt><dd>{inspecting.phone || '—'}</dd><dt>Address</dt><dd>{inspecting.address || '—'}</dd></dl></div><footer className="platform-overlay-footer"><button type="button" className="button button-secondary" onClick={() => setInspecting(null)}>Close</button></footer></div>}
        {editing && <form className="platform-overlay platform-edit-drawer" onSubmit={(event) => void saveEdit(event)} role="dialog" aria-modal="true" aria-labelledby="edit-tenant-title"><header className="platform-overlay-header"><div><span className="platform-overlay-kicker"><SettingsIcon /> Tenant workspace</span><h2 id="edit-tenant-title">Edit Tenant</h2><p>Update safe tenant metadata. Provisioning identity remains protected.</p></div><button type="button" className="platform-overlay-close" aria-label="Close Edit Tenant" onClick={() => setEditing(null)}>×</button></header><div className="platform-overlay-body"><div className="platform-edit-grid"><Field label="Tenant Name" required value={editName} onChange={setEditName} /><Field label="Host" required value={editHost} onChange={setEditHost} /><Field label="Tenant Code" value={editing.tenantCode} onChange={() => undefined} readOnly /><Field label="Database Provider" value={editing.databaseProvider === 'MySql' ? 'MySQL' : 'SQL Server'} onChange={() => undefined} readOnly /><Field label="Shard Key" value={editing.shardKey} onChange={() => undefined} readOnly /><Field label="Contact Email" type="email" value={editEmail} onChange={setEditEmail} /><Field label="Phone" value={editPhone} onChange={setEditPhone} /><label className="platform-field platform-edit-address"><span>Address</span><textarea value={editAddress} onChange={(event) => setEditAddress(event.target.value)} rows={4} /></label></div><p className="platform-form-note"><ShieldIcon /> Database provider and shard key cannot be changed after provisioning.</p></div><footer className="platform-overlay-footer"><button type="button" className="button button-secondary" onClick={() => setEditing(null)}>Cancel</button><button type="submit" className="button button-primary" disabled={saving}>{saving ? 'Saving…' : 'Save Changes'}</button></footer></form>}
        {retrying && <form className="platform-admin-card platform-modal-card" onSubmit={(event) => void retry(event)}><h2>Retry provisioning</h2><p>Tenant: <strong>{retrying.tenantCode}</strong> · Provider: <strong>{retrying.databaseProvider}</strong> · Host: <strong>{retrying.host}</strong></p><p>Database preview: <code>{retrying.databaseProvider === 'MySql' ? `HRMS_${retrying.shardKey}` : 'Shared SQL Server HRMS'}</code></p><Field label="Initial Administrator First Name" required value={retryForm.firstName} onChange={(value) => updateRetry('firstName', value)} /><Field label="Initial Administrator Last Name" required value={retryForm.lastName} onChange={(value) => updateRetry('lastName', value)} /><Field label="Initial Administrator Email" required type="email" value={retryForm.initialAdminEmail} onChange={(value) => updateRetry('initialAdminEmail', value)} /><div className="platform-form-actions"><button type="button" className="button button-secondary" onClick={() => setRetrying(null)}>Cancel</button><button type="submit" className="button button-primary" disabled={saving}>Confirm retry</button></div></form>}
        {resetting && <form className="platform-admin-card platform-modal-card" onSubmit={(event) => { event.preventDefault(); if (window.confirm(`Reset the TenantAdmin password for ${resetting.tenantCode}?`)) void resetAdminPassword() }}><h2>Reset Tenant Admin Password</h2><p>Generate a new Development-only temporary password for <strong>{resetting.tenantCode}</strong>.</p><Field label="Admin email (optional when exactly one active TenantAdmin exists)" type="email" value={resetAdminEmail} onChange={setResetAdminEmail} /><div className="platform-form-actions"><button type="button" className="button button-secondary" onClick={() => { setResetting(null); setResetAdminEmail('') }}>Cancel</button><button type="submit" className="button button-primary" disabled={saving}>Confirm reset</button></div></form>}
        {statusChange && <div className="platform-admin-card platform-modal-card" role="dialog" aria-modal="true" aria-labelledby="status-change-title"><h2 id="status-change-title">{statusChange.target === 'Inactive' ? `Deactivate ${statusChange.tenant.tenantName}?` : `Activate ${statusChange.tenant.tenantName}?`}</h2><p>{statusChange.target === 'Inactive' ? 'This will prevent tenant users from signing in or accessing the workspace. Tenant data will not be deleted and the tenant can be reactivated later.' : 'Tenant users will be able to sign in and use the workspace again.'}</p><div className="platform-form-actions"><button type="button" className="button button-secondary" onClick={() => setStatusChange(null)}>Cancel</button><button type="button" className="button button-primary" onClick={() => void confirmStatusChange()} disabled={saving}>{statusChange.target === 'Inactive' ? 'Deactivate Tenant' : 'Activate Tenant'}</button></div></div>}
        {resetResult && <div className="platform-admin-card platform-reset-result" role="dialog" aria-modal="true"><h2>Temporary password</h2><p>Tenant: <strong>{resetResult.tenantName}</strong></p><p>Admin email: <strong>{resetResult.adminEmail}</strong></p><p><strong>Copy this password now. It will not be shown again.</strong></p><code>{resetResult.temporaryPassword}</code> <button type="button" className="button button-secondary" onClick={() => void navigator.clipboard?.writeText(resetResult.temporaryPassword)}>Copy</button><p>{resetResult.message}</p><button type="button" className="button button-secondary" onClick={closeResetResult}>Close</button></div>}
      </main>
    </div>
  </div>
}

function SummaryCard({ icon, label, value, hint, tone = 'teal' }: { icon: ReactNode; label: string; value: number; hint: string; tone?: string }) { return <div className={`platform-summary-card is-${tone}`}><span className="platform-summary-icon">{icon}</span><span className="platform-summary-label">{label}</span><strong>{value}</strong><small>{hint}</small></div> }
function FormSection({ title, helper, children }: { title: string; helper: string; children: ReactNode }) { return <section className="platform-form-section"><h3>{title}</h3><p>{helper}</p>{children}</section> }
function Field({ label, value, onChange, required = false, type = 'text', pattern, readOnly = false }: { label: string; value: string | undefined; onChange: (value: string) => void; required?: boolean; type?: string; pattern?: string; readOnly?: boolean }) { return <label className="platform-field"><span>{label}{required && ' *'}</span><input required={required} pattern={pattern} type={type} value={value ?? ''} onChange={(event) => onChange(event.target.value)} readOnly={readOnly} /></label> }
function SelectField({ label, value, onChange, required, options }: { label: string; value: string; onChange: (value: string) => void; required?: boolean; options: [string, string][] }) { return <label className="platform-field"><span>{label}{required && ' *'}</span><select required={required} value={value} onChange={(event) => onChange(event.target.value)}>{options.map(([option, text]) => <option key={option} value={option}>{text}</option>)}</select></label> }
function TenantRow({ tenant, canEdit, canChangeStatus, onInspect, onEdit, onRetry, onReset, onStatusChange }: { tenant: PlatformTenant; canEdit: boolean; canChangeStatus: boolean; onInspect: () => void; onEdit: () => void; onRetry: () => void; onReset: () => void; onStatusChange: (target: 'Active' | 'Inactive') => void }) {
  const [menuOpen, setMenuOpen] = useState(false)
  const closeMenu = () => setMenuOpen(false)
  return <tr><td><code className="platform-tenant-code">{tenant.tenantCode}</code></td><td><strong>{tenant.tenantName}</strong></td><td>{tenant.host}</td><td><span className="platform-provider-pill"><DatabaseIcon /> {tenant.databaseProvider === 'MySql' ? 'MySQL' : 'SQL Server'}</span></td><td><code>{tenant.shardKey}</code></td><td><span className={`platform-status-pill is-${tenant.status.toLowerCase()}`}><span />{tenant.status}</span></td><td><a className="platform-tenant-url" href={localTenantUrl(tenant.host)}>{localTenantUrl(tenant.host)} <ExternalIcon /></a></td><td><details className="platform-actions-menu" open={menuOpen} onToggle={(event) => setMenuOpen(event.currentTarget.open)}><summary aria-label="Tenant actions">Actions</summary><div><button type="button" onClick={() => { closeMenu(); onInspect() }}>View / Inspect</button>{canEdit && <button type="button" onClick={() => { closeMenu(); onEdit() }}>Edit</button>}{canChangeStatus && tenant.status === 'Active' && <button type="button" onClick={() => { closeMenu(); onStatusChange('Inactive') }}>Deactivate</button>}{canChangeStatus && tenant.status === 'Inactive' && <button type="button" onClick={() => { closeMenu(); onStatusChange('Active') }}>Activate</button>}{import.meta.env.DEV && canEdit && tenant.status === 'Active' && <button type="button" onClick={() => { closeMenu(); onReset() }}>Reset Tenant Admin Password</button>}{canEdit && tenant.status === 'Inactive' && <button type="button" onClick={() => { closeMenu(); onRetry() }}>Retry provisioning</button>}</div></details></td></tr>
}
function Icon({ children }: { children: ReactNode }) { return <svg viewBox="0 0 24 24" aria-hidden="true">{children}</svg> }
function DashboardIcon() { return <Icon><rect x="4" y="4" width="6" height="6" rx="1" /><rect x="14" y="4" width="6" height="6" rx="1" /><rect x="4" y="14" width="6" height="6" rx="1" /><rect x="14" y="14" width="6" height="6" rx="1" /></Icon> }
function BuildingsIcon() { return <Icon><path d="M4 20V5.5L12 3v17M12 20V8l8-2.5V20M7.5 8h1M7.5 11h1M15 10h1M15 13h1M2.5 20h19" /></Icon> }
function UsersIcon() { return <Icon><circle cx="9" cy="8" r="3" /><path d="M3.5 19c.4-3 2.2-4.5 5.5-4.5s5.1 1.5 5.5 4.5M16 5.5a3 3 0 0 1 0 5.7M17 14.7c2 .5 3.2 1.9 3.5 4.3" /></Icon> }
function ShieldIcon() { return <Icon><path d="M12 3 20 6v5.5c0 4.8-3.3 7.8-8 9.5-4.7-1.7-8-4.7-8-9.5V6l8-3Z" /><path d="m8.5 12 2.2 2.2 4.8-5" /></Icon> }
function SettingsIcon() { return <Icon><circle cx="12" cy="12" r="3.5" /><path d="m19.4 15 .1.1-1.6 2.7-.2-.1a2 2 0 0 0-2.8 1.2v.2h-3.2v-.2a2 2 0 0 0-2.8-1.2l-.2.1-1.6-2.7.1-.1a2 2 0 0 0 0-3l-.1-.1 1.6-2.7.2.1a2 2 0 0 0 2.8-1.2V8h3.2v.2a2 2 0 0 0 2.8 1.2l.2-.1 1.6 2.7-.1.1a2 2 0 0 0 0 3Z" /></Icon> }
function ActivityIcon() { return <Icon><path d="M3 12h4l2.2-5 4.1 10 2.2-5H21" /></Icon> }
function HelpIcon() { return <Icon><circle cx="12" cy="12" r="9" /><path d="M9.7 9a2.5 2.5 0 1 1 4.2 1.8c-1.1.9-1.9 1.2-1.9 2.7M12 16.7h.01" /></Icon> }
function DatabaseIcon() { return <Icon><ellipse cx="12" cy="5.5" rx="7" ry="2.5" /><path d="M5 5.5v6c0 1.4 3.1 2.5 7 2.5s7-1.1 7-2.5v-6M5 11.5v6c0 1.4 3.1 2.5 7 2.5s7-1.1 7-2.5v-6" /></Icon> }
function CheckIcon() { return <Icon><path d="m5 12 4.2 4.2L19 6.5" /></Icon> }
function ClockIcon() { return <Icon><circle cx="12" cy="12" r="8.5" /><path d="M12 7v5l3 2" /></Icon> }
function SearchIcon() { return <Icon><circle cx="10.8" cy="10.8" r="6.3" /><path d="m16 16 4.5 4.5" /></Icon> }
function ExternalIcon() { return <Icon><path d="M14 5h5v5M19 5l-8 8M19 14v4a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1h4" /></Icon> }
function ArrowIcon() { return <Icon><path d="M5 12h13M13 6l6 6-6 6" /></Icon> }
function AlertIcon() { return <Icon><circle cx="12" cy="12" r="9" /><path d="M12 7.5v5M12 16.5h.01" /></Icon> }
