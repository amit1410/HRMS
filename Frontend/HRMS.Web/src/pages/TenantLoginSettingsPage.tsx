import { useEffect, useState } from 'react'
import type { TenantLoginIdentifierMode } from '../api/types.ts'
import { getTenantLoginSettings, saveTenantLoginSettings } from '../api/tenantLoginSettings.ts'
import { Card } from '../components/Card.tsx'
import { Notice } from '../components/Notice.tsx'
import { PageHeader } from '../components/PageHeader.tsx'
import { useApiQuery } from '../hooks/useApiQuery.ts'

const options: Array<{ value: TenantLoginIdentifierMode; label: string; description: string }> = [
  { value: 'EmailOnly', label: 'Email only', description: 'Employees sign in using their email address.' },
  { value: 'EmployeeCodeOnly', label: 'Employee Code only', description: 'Employees sign in using their Employee Code.' },
  { value: 'EmailOrEmployeeCode', label: 'Email or Employee Code', description: 'Employees can use either identifier.' },
]

export function TenantLoginSettingsPage() {
  const query = useApiQuery((signal) => getTenantLoginSettings(signal), [])
  const [mode, setMode] = useState<TenantLoginIdentifierMode>('EmailOrEmployeeCode')
  const [saving, setSaving] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  useEffect(() => { if (query.data) setMode(query.data.loginIdentifierMode) }, [query.data])
  async function save() { setSaving(true); setNotice(null); try { await saveTenantLoginSettings(mode); setNotice('Login identifier setting saved.') } catch (error) { setNotice(error instanceof Error ? error.message : 'Unable to save login identifier setting.') } finally { setSaving(false) } }
  return <div className="page-stack"><PageHeader title="Login Settings" subtitle="Choose what employees can use to sign in to this tenant." />
    <Card title="Authentication / Login Settings">
      {query.isLoading ? <p className="muted">Loading settings…</p> : query.error ? <Notice tone="error">{query.error.message}</Notice> : <>
        <fieldset className="login-settings-options"><legend>Login Identifier</legend>{options.map((option) => <label key={option.value} className="login-settings-option"><input type="radio" name="loginIdentifierMode" value={option.value} checked={mode === option.value} onChange={() => setMode(option.value)} /><span><strong>{option.label}</strong><small>{option.description}</small></span></label>)}</fieldset>
        {mode === 'EmployeeCodeOnly' ? <p className="field-help">Employees will sign in using Employee Code. Tenant administrators without an employee link can continue to sign in using email.</p> : null}
        {notice ? <p className="field-help" role="status">{notice}</p> : null}<button className="button button-primary" type="button" onClick={() => void save()} disabled={saving}>{saving ? 'Saving…' : 'Save Login Settings'}</button>
      </>}
    </Card>
  </div>
}
