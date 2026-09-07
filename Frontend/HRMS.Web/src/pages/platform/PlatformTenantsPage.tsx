import { useEffect, useState, type FormEvent } from 'react'
import {
  createPlatformTenant, listPlatformTenants, retryPlatformTenant, updateInactivePlatformTenant,
  type CreatePlatformTenantRequest, type PlatformTenant, type RetryPlatformTenantRequest,
} from '../../api/platformTenants.ts'
import { toApiError } from '../../api/errors.ts'
import { usePlatformAuth } from '../../auth/PlatformAuthProvider.tsx'
import { PlatformPermissions } from '../../auth/platformPermissions.ts'
import { PageHeader } from '../../components/PageHeader.tsx'

const initialForm: CreatePlatformTenantRequest = {
  tenantName: '', tenantCode: '', host: '', databaseProvider: 'SqlServer', shardKey: '',
  email: '', phone: '', address: '', firstName: '', lastName: '', initialAdminEmail: '',
}
const initialRetry: RetryPlatformTenantRequest = { firstName: '', lastName: '', initialAdminEmail: '' }

function localTenantUrl(host: string): string {
  const url = new URL(window.location.href)
  url.hostname = host
  return url.toString().replace(/\/$/, '')
}

export function PlatformTenantsPage() {
  const { can } = usePlatformAuth()
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
  const [retrying, setRetrying] = useState<PlatformTenant | null>(null)
  const [retryForm, setRetryForm] = useState(initialRetry)

  useEffect(() => {
    if (!can(PlatformPermissions.view)) return
    void listPlatformTenants().then(setTenants).catch((reason) => setError(toApiError(reason).message)).finally(() => setLoading(false))
  }, [can])

  const update = (field: keyof CreatePlatformTenantRequest, value: string) => setForm((current) => ({ ...current, [field]: value }))
  const updateRetry = (field: keyof RetryPlatformTenantRequest, value: string) => setRetryForm((current) => ({ ...current, [field]: value }))
  const mysqlPreview = `HRMS_${form.shardKey.trim().toLowerCase() || '<shard-key>'}`

  function replaceTenant(updated: PlatformTenant) {
    setTenants((current) => current.map((tenant) => tenant.id === updated.id ? updated : tenant))
  }

  async function submit(event: FormEvent) {
    event.preventDefault(); setError(''); setMessage(''); setCreated(null); setSaving(true)
    try {
      const tenant = await createPlatformTenant({ ...form, tenantCode: form.tenantCode.toUpperCase(), host: form.host.toLowerCase(), shardKey: form.shardKey.toLowerCase() })
      setTenants((current) => [...current, tenant].sort((a, b) => a.tenantCode.localeCompare(b.tenantCode)))
      setCreated(tenant); setMessage('Tenant created successfully'); setForm(initialForm)
    } catch (reason) {
      const apiError = toApiError(reason)
      setError(apiError.message.includes('TenantCode is already in use')
        ? 'A tenant with this code already exists in Inactive state. Use Retry Provisioning instead of creating another tenant.'
        : apiError.message)
    } finally { setSaving(false) }
  }

  async function saveEdit(event: FormEvent) {
    event.preventDefault(); if (!editing) return
    setError(''); setMessage(''); setSaving(true)
    try {
      const tenant = await updateInactivePlatformTenant(editing.id, { tenantName: editName, host: editHost.toLowerCase() })
      replaceTenant(tenant); setEditing(null); setMessage('Inactive tenant details updated.')
    } catch (reason) { setError(toApiError(reason).message) } finally { setSaving(false) }
  }

  async function retry(event: FormEvent) {
    event.preventDefault(); if (!retrying) return
    const preview = retrying.databaseProvider === 'MySql' ? `HRMS_${retrying.shardKey}` : 'Shared SQL Server HRMS'
    if (!window.confirm(`Retry provisioning for ${retrying.tenantCode} using ${retrying.databaseProvider} and shard ${retrying.shardKey}?\nDatabase preview: ${preview}`)) return
    setError(''); setMessage(''); setSaving(true)
    try {
      const tenant = await retryPlatformTenant(retrying.id, retryForm)
      replaceTenant(tenant); setRetrying(null); setRetryForm(initialRetry); setMessage('Tenant provisioning retry completed successfully.')
    } catch (reason) { setError(toApiError(reason).message) } finally { setSaving(false) }
  }

  if (!can(PlatformPermissions.view)) return <p role="alert">You do not have permission to view platform tenants.</p>

  return <section>
    <PageHeader title="Platform tenants" subtitle="Create and inspect tenant workspaces. Provider credentials remain server-side." />
    {error && <p role="alert">{error}</p>}{message && <p role="status">{message}</p>}
    {created && <div className="card" role="status"><strong>Tenant created successfully</strong><p>{created.tenantCode} · {created.databaseProvider} · {created.status}</p><p>Web URL: {localTenantUrl(created.host)}</p>{created.developmentTemporaryPassword && <p>Development temporary administrator password: <code>{created.developmentTemporaryPassword}</code></p>}<a href={localTenantUrl(created.host)}>Open Tenant</a></div>}
    <div className="card"><h2>Tenant list</h2>{loading ? <p>Loading…</p> : <table><thead><tr><th>Tenant Code</th><th>Tenant Name</th><th>Host</th><th>Provider</th><th>Shard Key</th><th>Status</th><th>URL</th><th>Actions</th></tr></thead><tbody>{tenants.map((tenant) => <tr key={tenant.id}><td>{tenant.tenantCode}</td><td>{tenant.tenantName}</td><td>{tenant.host}</td><td>{tenant.databaseProvider}</td><td>{tenant.shardKey}</td><td>{tenant.status}</td><td><a href={localTenantUrl(tenant.host)}>{localTenantUrl(tenant.host)}</a></td><td>{can(PlatformPermissions.create) && tenant.status === 'Inactive' && <><button type="button" onClick={() => { setEditing(tenant); setEditHost(tenant.host); setEditName(tenant.tenantName) }}>Edit</button>{' '}<button type="button" onClick={() => { setRetrying(tenant); setRetryForm(initialRetry) }}>Retry provisioning</button></>}</td></tr>)}</tbody></table>}</div>
    {editing && <form className="card" onSubmit={(event) => void saveEdit(event)}><h2>Edit inactive tenant</h2><p>{editing.tenantCode} · {editing.databaseProvider} · {editing.shardKey}</p><label>Tenant Name *<input required value={editName} onChange={(e) => setEditName(e.target.value)} /></label><label>Host *<input required value={editHost} onChange={(e) => setEditHost(e.target.value)} /></label><button type="submit" disabled={saving}>Save</button>{' '}<button type="button" onClick={() => setEditing(null)}>Cancel</button></form>}
    {retrying && <form className="card" onSubmit={(event) => void retry(event)}><h2>Retry provisioning</h2><p>Tenant: <strong>{retrying.tenantCode}</strong> · Provider: <strong>{retrying.databaseProvider}</strong> · Host: <strong>{retrying.host}</strong></p><p>Database preview: <code>{retrying.databaseProvider === 'MySql' ? `HRMS_${retrying.shardKey}` : 'Shared SQL Server HRMS'}</code></p><label>Initial Administrator First Name *<input required value={retryForm.firstName} onChange={(e) => updateRetry('firstName', e.target.value)} /></label><label>Initial Administrator Last Name *<input required value={retryForm.lastName} onChange={(e) => updateRetry('lastName', e.target.value)} /></label><label>Initial Administrator Email *<input required type="email" value={retryForm.initialAdminEmail} onChange={(e) => updateRetry('initialAdminEmail', e.target.value)} /></label><button type="submit" disabled={saving}>Confirm retry</button>{' '}<button type="button" onClick={() => setRetrying(null)}>Cancel</button></form>}
    {can(PlatformPermissions.create) && <form className="card" onSubmit={(event) => void submit(event)}><h2>Create tenant</h2><label>Tenant Name *<input required value={form.tenantName} onChange={(e) => update('tenantName', e.target.value)} /></label><label>Tenant Code *<input required pattern="[A-Za-z0-9_-]{2,20}" value={form.tenantCode} onChange={(e) => update('tenantCode', e.target.value)} /></label><label>Host *<input required value={form.host} onChange={(e) => update('host', e.target.value)} /></label><label>Database Provider *<select value={form.databaseProvider} onChange={(e) => update('databaseProvider', e.target.value)}><option value="SqlServer">SQL Server</option><option value="MySql">MySQL</option></select></label><label>Shard Key *<input required value={form.shardKey} onChange={(e) => update('shardKey', e.target.value)} /></label>{form.databaseProvider === 'MySql' ? <p>Database preview: <code>{mysqlPreview}</code> (preview only)</p> : <p>Database routing: Shared SQL Server database</p>}<label>Contact Email<input type="email" value={form.email} onChange={(e) => update('email', e.target.value)} /></label><label>Phone<input value={form.phone} onChange={(e) => update('phone', e.target.value)} /></label><label>Address<textarea value={form.address} onChange={(e) => update('address', e.target.value)} /></label><h3>Initial Administrator</h3><label>First Name *<input required value={form.firstName} onChange={(e) => update('firstName', e.target.value)} /></label><label>Last Name *<input required value={form.lastName} onChange={(e) => update('lastName', e.target.value)} /></label><label>Email *<input required type="email" value={form.initialAdminEmail} onChange={(e) => update('initialAdminEmail', e.target.value)} /></label><button type="submit" disabled={saving}>{saving ? 'Provisioning…' : 'Create Tenant'}</button></form>}
  </section>
}
