import { useEffect, useState, type FormEvent } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api/errors.ts'
import { createLeavePolicyVersion, getLeavePolicyEditor, listLeavePolicyVersions, publishLeavePolicyVersion, retireLeavePolicyVersion, updateLeavePolicyVersion, validateLeavePolicyVersion, type LeavePolicyEditor, type LeavePolicyValidation, type LeavePolicyVersion } from '../../api/leaveConfiguration.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { ActiveBadge, Badge } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { LeavePolicyConfigurationSections } from './LeavePolicyConfigurationSections.tsx'
import { LeavePolicyEligibilitySection } from './LeavePolicyEligibilitySection.tsx'
import { LeavePolicyEntitlementSection } from './LeavePolicyEntitlementSection.tsx'
import { LeavePolicyRequestRulesSection } from './LeavePolicyRequestRulesSection.tsx'
import { LeavePolicyCalendarSection } from './LeavePolicyCalendarSection.tsx'
import { LeavePolicyAttachmentSection } from './LeavePolicyAttachmentSection.tsx'
import { LeavePolicyClubbingSection } from './LeavePolicyClubbingSection.tsx'
import { LeavePolicyCancellationSection } from './LeavePolicyCancellationSection.tsx'

export function LeavePolicyEditorPage() {
  const { policyId = '' } = useParams()
  const [searchParams, setSearchParams] = useSearchParams()
  const { can } = useAuth()
  const canManage = can(Permissions.leave.policyManage)
  const versionId = searchParams.get('versionId') ?? undefined
  const [editor, setEditor] = useState<LeavePolicyEditor | null>(null)
  const [versions, setVersions] = useState<LeavePolicyVersion[]>([])
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [creating, setCreating] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [validation, setValidation] = useState<LeavePolicyValidation | null>(null)
  const [validationStale, setValidationStale] = useState(false)
  const [lifecycleAction, setLifecycleAction] = useState<'publish' | 'retire' | null>(null)
  const [lifecycleBusy, setLifecycleBusy] = useState(false)
  const [reloadKey, setReloadKey] = useState(0)
  const [draft, setDraft] = useState({ effectiveFrom: '', effectiveTo: '', priority: 0 })
  const versionsQuery = useApiQuery(signal => listLeavePolicyVersions(policyId, signal), [policyId])

  useEffect(() => {
    let cancelled = false
    setLoading(true); setError(null)
    void getLeavePolicyEditor(policyId, versionId).then(value => { if (!cancelled) { setEditor(value); if (value.currentVersion) setDraft({ effectiveFrom: value.currentVersion.effectiveFrom, effectiveTo: value.currentVersion.effectiveTo ?? '', priority: value.currentVersion.priority }) } }).catch(caught => { if (!cancelled) setError(caught instanceof ApiError ? caught : new ApiError('Unable to load Leave Policy.')) }).finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [policyId, versionId, reloadKey])

  useEffect(() => { if (versionsQuery.data) setVersions(versionsQuery.data.items) }, [versionsQuery.data])
  function chooseVersion(value: string) { setValidation(null); setValidationStale(false); if (value) setSearchParams({ versionId: value }); else setSearchParams({}) }
  function markConfigurationChanged() { if (validation) setValidationStale(true) }
  function editDraft<K extends keyof typeof draft>(key: K, value: (typeof draft)[K]) { markConfigurationChanged(); setDraft(previous => ({ ...previous, [key]: value })) }
  async function saveDraft(event: FormEvent) { event.preventDefault(); if (!editor?.currentVersion) return; if (draft.effectiveTo && draft.effectiveFrom > draft.effectiveTo) { setError(new ApiError('Effective From must be on or before Effective To.')); return } markConfigurationChanged(); setSaving(true); setError(null); setNotice(null); try { await updateLeavePolicyVersion(policyId, editor.currentVersion.id, { effectiveFrom: draft.effectiveFrom, effectiveTo: draft.effectiveTo || null, priority: Number(draft.priority), concurrencyToken: editor.currentVersion.concurrencyToken }); setNotice('Draft version saved.'); setSearchParams({ versionId: editor.currentVersion.id }) } catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to save Draft version.')) } finally { setSaving(false) } }
  async function validateDraft() { if (!editor?.currentVersion) return; setError(null); setNotice(null); try { const result = await validateLeavePolicyVersion(policyId, editor.currentVersion.id); setValidation(result); setValidationStale(false) } catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to validate Draft version.')) } }
  async function performLifecycleAction() { if (!editor?.currentVersion || !lifecycleAction) return; setLifecycleBusy(true); setError(null); try { if (lifecycleAction === 'publish') await publishLeavePolicyVersion(policyId, editor.currentVersion.id); else await retireLeavePolicyVersion(policyId, editor.currentVersion.id); setNotice(lifecycleAction === 'publish' ? 'Policy version published.' : 'Policy version retired.'); setValidation(null); setValidationStale(false); versionsQuery.refetch(); setReloadKey(value => value + 1) } catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError(`Unable to ${lifecycleAction} Policy version.`)); } finally { setLifecycleBusy(false) } }
  async function createDraft(copyFromVersionId?: string) { setCreating(true); setError(null); try { const created = await createLeavePolicyVersion(policyId, { effectiveFrom: new Date().toISOString().slice(0, 10), effectiveTo: null, priority: 0, copyFromVersionId: copyFromVersionId || null }); setSearchParams({ versionId: created.id }); setNotice(`Draft version ${created.versionNumber} created.`); versionsQuery.refetch() } catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to create Draft version.')) } finally { setCreating(false) } }
  const current = editor?.currentVersion
  const fieldErrors = error?.fieldErrors ?? {}

  if (loading) return <div className="leave-admin-page"><p className="state-block"><Spinner label="Loading Leave Policy" /></p></div>
  if (error && !editor) return <div className="leave-admin-page"><Notice tone="error">{error.message}</Notice><Link className="button button-secondary" to="/leave-management/policies">Back to Leave Policies</Link></div>
  if (!editor) return null
  const readOnly = !canManage || current?.status !== 'Draft'
  const canValidate = Boolean(current && current.status === 'Draft' && canManage && current.allowedActions.canValidate)
  const canPublish = Boolean(current && current.status === 'Draft' && can(Permissions.leave.policyPublish) && current.allowedActions.canPublish)
  const canRetire = Boolean(current && current.status === 'Published' && can(Permissions.leave.policyPublish) && current.allowedActions.canRetire)

  return <div className="leave-admin-page">
    <PageHeader title={`${editor.policy.code} — ${editor.policy.name}`} subtitle="Leave Policy configuration shell" actions={<Link className="button button-secondary" to="/leave-management/policies">Back to Policies</Link>} />
    {notice ? <Notice tone="success" onDismiss={() => setNotice(null)}>{notice}</Notice> : null}
    {error ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest version before saving.' : ''}</Notice> : null}
    <div className="policy-editor-header"><div><ActiveBadge isActive={editor.policy.isActive} /><span className="policy-editor-code"><code>{editor.policy.code}</code></span></div><label className="field policy-version-selector"><span>Version</span><select className="input" aria-label="Policy version" value={current?.id ?? ''} onChange={event => chooseVersion(event.target.value)}><option value="">No version</option>{versions.map(version => <option key={version.id} value={version.id}>Version {version.versionNumber} — {version.status} ({formatDateOnly(version.effectiveFrom)})</option>)}</select></label><div className="form-actions policy-lifecycle-actions">{canManage && editor.policy.isActive ? <button className="button button-primary" type="button" onClick={() => void createDraft()} disabled={creating}>{creating ? <Spinner size={14} label="Creating…" /> : '+ Create Draft Version'}</button> : null}{canValidate ? <button className="button button-secondary" type="button" onClick={() => void validateDraft()} disabled={lifecycleBusy}>Validate Draft</button> : null}{canPublish ? <button className="button button-primary" type="button" onClick={() => setLifecycleAction('publish')} disabled={lifecycleBusy}>Publish Version</button> : null}{canRetire ? <button className="button button-danger" type="button" onClick={() => setLifecycleAction('retire')} disabled={lifecycleBusy}>Retire Version</button> : null}</div></div>
    {validation || validationStale ? <Card title="Policy Validation" className="policy-validation"><div className="validation-summary"><Badge tone={validationStale ? 'warning' : validation?.isValid ? 'success' : 'danger'}>{validationStale ? 'Potentially stale' : validation?.isValid ? 'Valid' : 'Not ready'}</Badge><span>{validationStale ? 'Configuration has changed since the last validation. Validate again before publishing.' : validation?.isValid ? 'No blocking configuration errors.' : `${validation?.errors.length ?? 0} issue${validation?.errors.length === 1 ? '' : 's'} must be corrected before publishing.`}</span></div>{validation?.errors.length ? <div className="validation-list validation-errors"><h4>Errors</h4><ul>{validation.errors.map((item, index) => <li key={`${item.field}-${index}`}>{item.message}</li>)}</ul></div> : null}{validation?.warnings.length ? <div className="validation-list validation-warnings"><h4>Warnings</h4><ul>{validation.warnings.map((warning, index) => <li key={index}>{warning}</li>)}</ul></div> : null}{canValidate ? <button className="button button-secondary" type="button" onClick={() => void validateDraft()}>Validate Again</button> : null}</Card> : null}
    <Card title="Version History" subtitle="Historical policy versions remain available for inspection."><div className="table-wrap"><table className="data-table"><caption className="sr-only">Leave Policy version history</caption><thead><tr><th>Version</th><th>Status</th><th>Effective range</th><th>Priority</th><th>Leave Types</th><th>Applicability</th><th>Created</th><th>Action</th></tr></thead><tbody>{[...versions].sort((left, right) => right.versionNumber - left.versionNumber).map(item => <tr key={item.id} className={item.id === current?.id ? 'is-selected' : undefined}><td>Version {item.versionNumber}</td><td><Badge tone={item.status === 'Published' ? 'success' : item.status === 'Draft' ? 'info' : 'neutral'}>{item.status}</Badge></td><td>{formatDateOnly(item.effectiveFrom)} — {item.effectiveTo ? formatDateOnly(item.effectiveTo) : 'Open ended'}</td><td>{item.priority}</td><td>{item.leaveTypeCount}</td><td>{item.applicabilityGroupCount}</td><td>{formatDateTime(item.modifiedDate ?? item.createdDate)}</td><td>{item.id === current?.id ? <span className="muted">Selected</span> : <button className="button button-link" type="button" onClick={() => chooseVersion(item.id)}>Open version</button>}</td></tr>)}</tbody></table></div>{versions.length === 0 ? <p className="muted">No versions have been created.</p> : null}</Card>
    <section className="policy-editor-sections" aria-label="Policy editor sections">
      <Card title="1. Policy Details" subtitle="Stable policy identity"><dl className="detail-list"><div><dt>Code</dt><dd><code>{editor.policy.code}</code></dd></div><div><dt>Name</dt><dd>{editor.policy.name}</dd></div><div><dt>Description</dt><dd>{editor.policy.description || 'No description provided.'}</dd></div><div><dt>Status</dt><dd><ActiveBadge isActive={editor.policy.isActive} /></dd></div></dl>{canManage ? <Link className="row-action" to={`/leave-management/policies?edit=${editor.policy.id}`}>Edit policy identity</Link> : null}</Card>
      <Card title="2. Version Settings" subtitle={current ? `Version ${current.versionNumber}` : 'Create a Draft to configure version settings'}>{current ? <><div className="lifecycle-notice"><Badge tone={current.status === 'Draft' ? 'info' : current.status === 'Published' ? 'success' : 'neutral'}>{current.status}</Badge><span>{current.status === 'Draft' ? (canManage ? 'This Draft can be edited.' : 'You can view this Draft but do not have edit permission.') : current.status === 'Published' ? 'This version is Published and cannot be edited.' : 'This version is Retired and retained for history.'}</span></div><form className="form-stack" aria-label="Policy version settings" onSubmit={saveDraft}>{readOnly ? <dl className="detail-list"><div><dt>Effective From</dt><dd>{formatDateOnly(current.effectiveFrom)}</dd></div><div><dt>Effective To</dt><dd>{current.effectiveTo ? formatDateOnly(current.effectiveTo) : 'Open ended'}</dd></div><div><dt>Priority</dt><dd>{current.priority} <small className="field-help">Higher numbers take precedence.</small></dd></div></dl> : <><div className="form-grid"><label className="field"><span>Effective From <em>(required)</em></span><input className={fieldErrors.effectiveFrom ? 'input has-error' : 'input'} type="date" required value={draft.effectiveFrom} onChange={event => editDraft('effectiveFrom', event.target.value)} /></label><label className="field"><span>Effective To</span><input className="input" type="date" value={draft.effectiveTo} onChange={event => editDraft('effectiveTo', event.target.value)} /></label></div><label className="field"><span>Priority</span><input className="input" type="number" min="0" step="1" value={draft.priority} onChange={event => editDraft('priority', Number(event.target.value))} /><small className="field-help">Higher numeric Priority takes precedence when more than one Policy matches.</small></label><div className="form-actions"><button className="button button-primary" type="submit" disabled={saving}>{saving ? <Spinner size={14} label="Saving…" /> : 'Save Draft'}</button></div></>}</form></> : <p className="muted">No version selected. Create a Draft Version to begin.</p>}</Card>
      {current ? <LeavePolicyConfigurationSections policyId={policyId} version={current} selectedLeaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} /> : null}
      {current ? <LeavePolicyEligibilitySection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} /> : null}
      {current ? <LeavePolicyEntitlementSection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} /> : null}
      {current ? <LeavePolicyRequestRulesSection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} /> : null}
      {current ? <LeavePolicyCalendarSection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} /> : null}
      {current ? <LeavePolicyAttachmentSection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} /> : null}
      {current ? <LeavePolicyClubbingSection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} /> : null}
      {current ? <LeavePolicyCancellationSection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} /> : null}
      <Card title="13. Additional Rules" subtitle="Future configuration section"><p className="muted">Other rules are not part of this phase.</p></Card>
    </section>
    {lifecycleAction ? <ConfirmDialog title={lifecycleAction === 'publish' ? 'Publish Policy Version?' : 'Retire Policy Version?'} message={lifecycleAction === 'publish' ? `Version ${current?.versionNumber} of ${editor.policy.code} will become Published and can no longer be edited.` : `Version ${current?.versionNumber} of ${editor.policy.code} will no longer participate in current or future Policy resolution.`} hint={lifecycleAction === 'publish' ? 'The version configuration will be preserved as history.' : 'Historical configuration and effective dates will remain preserved.'} confirmLabel={lifecycleAction === 'publish' ? 'Publish Version' : 'Retire Version'} onConfirm={performLifecycleAction} onClose={() => { if (!lifecycleBusy) setLifecycleAction(null) }} /> : null}
  </div>
}

function formatDateOnly(value: string): string { const [year, month, day] = value.slice(0, 10).split('-').map(Number); if (!year || !month || !day) return '—'; return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(year, month - 1, day, 12)) }
function formatDateTime(value: string): string { const date = new Date(value); return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date) }
