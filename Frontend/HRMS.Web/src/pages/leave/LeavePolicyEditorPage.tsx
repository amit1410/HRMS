import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api/errors.ts'
import { createLeavePolicyVersion, getLeavePolicyEditor, listLeavePolicyVersions, publishLeavePolicyVersion, retireLeavePolicyVersion, testLeavePolicy, updateLeavePolicyVersion, validateLeavePolicyVersion, type LeavePolicyEditor, type LeavePolicyTest, type LeavePolicyValidation, type LeavePolicyVersion } from '../../api/leaveConfiguration.ts'
import { listEmployees } from '../../api/employees.ts'
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

const wizardSteps = [
  ['Overview', 'Basic policy information'],
  ['Effective Period', 'When this policy applies'],
  ['Leave Types & Applicability', 'Who receives which leave'],
  ['Eligibility', 'When employees qualify'],
  ['Entitlement', 'How much leave is provided'],
  ['Request Rules', 'How leave can be requested'],
  ['Calendar & Documents', 'Working days and attachments'],
  ['Advanced Rules', 'Combinations and cancellation'],
  ['Review & Publish', 'Check before publishing'],
] as const

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
  const [validationBusy, setValidationBusy] = useState(false)
  const validationBusyRef = useRef(false)
  const [creating, setCreating] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [validation, setValidation] = useState<LeavePolicyValidation | null>(null)
  const [validationStale, setValidationStale] = useState(false)
  const [lifecycleAction, setLifecycleAction] = useState<'publish' | 'retire' | null>(null)
  const [lifecycleBusy, setLifecycleBusy] = useState(false)
  const [reloadKey, setReloadKey] = useState(0)
  const [policyTest, setPolicyTest] = useState<LeavePolicyTest | null>(null)
  const [overlapAcknowledged, setOverlapAcknowledged] = useState(false)
  const [draft, setDraft] = useState({ effectiveFrom: '', effectiveTo: '', priority: 0 })
  const [activeStep, setActiveStep] = useState(versionId ? 2 : 1)
  const versionsQuery = useApiQuery(signal => listLeavePolicyVersions(policyId, signal), [policyId])

  useEffect(() => {
    let cancelled = false
    setLoading(true); setError(null)
    void getLeavePolicyEditor(policyId, versionId).then(value => { if (!cancelled) { setEditor(value); if (value.currentVersion) setDraft({ effectiveFrom: value.currentVersion.effectiveFrom, effectiveTo: value.currentVersion.effectiveTo ?? '', priority: value.currentVersion.priority }) } }).catch(caught => { if (!cancelled) setError(caught instanceof ApiError ? caught : new ApiError('Unable to load Leave Policy.')) }).finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [policyId, versionId, reloadKey])

  useEffect(() => { if (versionsQuery.data) setVersions(versionsQuery.data.items) }, [versionsQuery.data])
  function chooseVersion(value: string) { setValidation(null); setValidationStale(false); if (value) { setActiveStep(2); setSearchParams({ versionId: value }) } else setSearchParams({}) }
  function markConfigurationChanged() { if (validation) setValidationStale(true) }
  async function refreshCurrentVersion(versionToRefresh = editor?.currentVersion?.id) {
    if (!versionToRefresh) return
    const refreshed = await getLeavePolicyEditor(policyId, versionToRefresh)
    setEditor(refreshed)
    if (refreshed.currentVersion) setDraft({ effectiveFrom: refreshed.currentVersion.effectiveFrom, effectiveTo: refreshed.currentVersion.effectiveTo ?? '', priority: refreshed.currentVersion.priority })
    versionsQuery.refetch()
  }
  function editDraft<K extends keyof typeof draft>(key: K, value: (typeof draft)[K]) { markConfigurationChanged(); setDraft(previous => ({ ...previous, [key]: value })) }
  async function saveDraft(event: FormEvent) { event.preventDefault(); if (!editor?.currentVersion) return; if (draft.effectiveTo && draft.effectiveFrom > draft.effectiveTo) { setError(new ApiError('Effective From must be on or before Effective To.')); return } markConfigurationChanged(); setSaving(true); setError(null); setNotice(null); try { await updateLeavePolicyVersion(policyId, editor.currentVersion.id, { effectiveFrom: draft.effectiveFrom, effectiveTo: draft.effectiveTo || null, priority: Number(draft.priority), concurrencyToken: editor.currentVersion.concurrencyToken }); setNotice('Draft version saved.'); setSearchParams({ versionId: editor.currentVersion.id }) } catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to save Draft version.')) } finally { setSaving(false) } }
  async function validateDraft() { if (!editor?.currentVersion || validationBusyRef.current || lifecycleBusy) return; validationBusyRef.current = true; setValidationBusy(true); setError(null); setNotice(null); try { const result = await validateLeavePolicyVersion(policyId, editor.currentVersion.id); setValidation(result); setValidationStale(false) } catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to validate Draft version.')) } finally { validationBusyRef.current = false; setValidationBusy(false) } }
  async function performLifecycleAction() { if (!editor?.currentVersion || !lifecycleAction) return; setLifecycleBusy(true); setError(null); try { if (lifecycleAction === 'publish') { await publishLeavePolicyVersion(policyId, editor.currentVersion.id, overlapAcknowledged); setNotice(versions.some(version => version.status === 'Published' && version.id !== editor.currentVersion?.id) ? `Version ${editor.currentVersion.versionNumber} is now Published. The previous version has been superseded.` : 'Policy version published.') } else { await retireLeavePolicyVersion(policyId, editor.currentVersion.id); setNotice('Policy version retired.') } setValidation(null); setValidationStale(false); setOverlapAcknowledged(false); setLifecycleAction(null); versionsQuery.refetch(); setReloadKey(value => value + 1) } catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError(`Unable to ${lifecycleAction} Policy version.`)); } finally { setLifecycleBusy(false) } }
  async function createDraft(copyFromVersionId?: string) { setCreating(true); setError(null); try { const source = copyFromVersionId ?? (current?.status === 'Published' ? current.id : undefined); const created = await createLeavePolicyVersion(policyId, { effectiveFrom: source && current ? current.effectiveFrom : new Date().toISOString().slice(0, 10), effectiveTo: source && current ? current.effectiveTo : null, priority: source && current ? current.priority : 0, copyFromVersionId: source || null }); setActiveStep(2); setSearchParams({ versionId: created.id }); setNotice(`Draft version ${created.versionNumber} created${source ? ' from the published version' : ''}.`); versionsQuery.refetch() } catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to create Draft version.')) } finally { setCreating(false) } }
  const current = editor?.currentVersion
  const fieldErrors = error?.fieldErrors ?? {}

  if (loading) return <div className="leave-admin-page"><p className="state-block"><Spinner label="Loading Leave Policy" /></p></div>
  if (error && !editor) return <div className="leave-admin-page"><Notice tone="error">{error.message}</Notice><Link className="button button-secondary" to="/leave-management/policies">Back to Leave Policies</Link></div>
  if (!editor) return null
  const readOnly = !canManage || current?.status !== 'Draft'
  const canValidate = Boolean(current && current.status === 'Draft' && canManage && current.allowedActions.canValidate)
  const canPublish = Boolean(current && current.status === 'Draft' && can(Permissions.leave.policyPublish) && current.allowedActions.canPublish)
  const canRetire = Boolean(current && current.status === 'Published' && can(Permissions.leave.policyPublish) && current.allowedActions.canRetire)
  const stepVisible = (step: number) => activeStep === step
  function goBack() { setActiveStep(step => Math.max(1, step - 1)) }
  function goNext() { setActiveStep(step => Math.min(wizardSteps.length, step + 1)) }
  const selectedLeaveTypeNames = editor.leaveTypes.map(item => item.name).join(', ')

  return <div className="leave-admin-page" aria-busy={validationBusy || lifecycleBusy}>
    <PageHeader title={`${editor.policy.code} — ${editor.policy.name}`} subtitle="Configure this policy step by step" actions={<Link className="button button-secondary" to="/leave-management/policies">Back to Policies</Link>} />
    {notice ? <Notice tone="success" onDismiss={() => setNotice(null)}>{notice}</Notice> : null}
    {error ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest version before saving.' : ''}</Notice> : null}
    {current?.status === 'Draft' && versions.some(version => version.status === 'Published') ? <div className="lifecycle-notice"><Badge tone="info">Editing Draft</Badge><span>This Draft is based on the latest Published version. Publishing it will supersede the replaced range automatically; no manual retirement is needed.</span></div> : null}
    <div className="policy-editor-header"><div><ActiveBadge isActive={editor.policy.isActive} /><span className="policy-editor-code"><code>{editor.policy.code}</code></span></div><label className="field policy-version-selector"><span>Version</span><select className="input" aria-label="Policy version" value={current?.id ?? ''} onChange={event => chooseVersion(event.target.value)}><option value="">No version</option>{versions.map(version => <option key={version.id} value={version.id}>Version {version.versionNumber} — {version.status} ({formatDateOnly(version.effectiveFrom)})</option>)}</select></label><div className="form-actions policy-lifecycle-actions">{canManage && editor.policy.isActive ? <button className="button button-primary" type="button" onClick={() => void createDraft()} disabled={creating}>{creating ? <Spinner size={14} label="Creating…" /> : '+ Create Draft Version'}</button> : null}{canValidate ? <button className="button button-secondary" type="button" onClick={() => void validateDraft()} disabled={lifecycleBusy}>Validate Draft</button> : null}{canPublish ? <button className="button button-primary" type="button" onClick={() => setLifecycleAction('publish')} disabled={lifecycleBusy}>Publish Version</button> : null}{canRetire ? <button className="button button-danger" type="button" onClick={() => setLifecycleAction('retire')} disabled={lifecycleBusy}>Retire Version</button> : null}</div></div>
    {validation || validationStale ? <Card title="Publish Readiness" className="policy-validation"><div className="validation-summary"><Badge tone={validationStale ? 'warning' : validation?.isValid ? 'success' : 'danger'}>{validationStale ? 'Potentially stale' : validation?.isValid ? 'Ready' : 'Not ready'}</Badge><span>{validationStale ? 'Configuration has changed since the last validation. Validate again before publishing.' : validation?.isValid ? 'No blocking configuration errors.' : `${validation?.errors.length ?? 0} issue${validation?.errors.length === 1 ? '' : 's'} must be corrected before publishing.`}</span></div>{validation?.errors.length ? <div className="validation-list validation-errors"><h4>Errors</h4><ul>{validation.errors.map((item, index) => <li key={`${item.field}-${index}`}>{item.message}</li>)}</ul></div> : null}{validation?.warnings.length ? <div className="validation-list validation-warnings"><h4>Warnings</h4><ul>{validation.warnings.map((warning, index) => <li key={index}>{warning}</li>)}</ul></div> : null}{validation?.errors.some(item => item.field === 'overlapAcknowledgement') ? <label className="checkbox-field"><input type="checkbox" checked={overlapAcknowledged} onChange={event => setOverlapAcknowledged(event.target.checked)} /> I understand this policy will override another matching policy.</label> : null}{canValidate ? <button className="button button-secondary" type="button" onClick={() => void validateDraft()}>Validate Again</button> : null}</Card> : null}
    {current && editor.leaveTypes.length ? <PolicyTestPanel leaveTypes={editor.leaveTypes} onResult={setPolicyTest} /> : null}
    {policyTest ? <Card title="Policy resolution result" subtitle="Resolved by the same policy engine used by Apply Leave."><dl className="detail-list"><div><dt>Policy selected</dt><dd>{policyTest.policyName} (<code>{policyTest.policyCode}</code>)</dd></div><div><dt>Effective</dt><dd>{formatDateOnly(policyTest.effectiveFrom)} — {policyTest.effectiveTo ? formatDateOnly(policyTest.effectiveTo) : 'Open ended'}</dd></div><div><dt>Priority</dt><dd>{policyTest.priority}</dd></div><div><dt>Why selected</dt><dd>{policyTest.reason}</dd></div><div><dt>Request type</dt><dd>{policyTest.partialDayMode === 'FullDayOnly' ? 'Full Day Only' : policyTest.partialDayMode}</dd></div><div><dt>Eligibility</dt><dd>{policyTest.eligibilityMode}</dd></div><div><dt>Notice Period</dt><dd>{policyTest.noticePeriodMode}</dd></div><div><dt>Calendar</dt><dd>{policyTest.sandwichMode === 'Disabled' ? 'Sandwich Leave disabled' : 'Needs administrator attention'}; holidays {policyTest.holidayTreatment === 'Exclude' ? 'excluded' : 'counted'}, weekly offs {policyTest.weekOffTreatment === 'Exclude' ? 'excluded' : 'counted'}</dd></div></dl>{policyTest.competingPolicies.length ? <div className="validation-list validation-warnings"><h4>Competing matching policies</h4><ul>{policyTest.competingPolicies.map(item => <li key={item.policyVersionId}>{item.policyName} — priority {item.priority}, {formatDateOnly(item.effectiveFrom)} to {item.effectiveTo ? formatDateOnly(item.effectiveTo) : 'open ended'}</li>)}</ul></div> : null}</Card> : null}
    <Card title="Version History" subtitle="Historical policy versions remain available for inspection."><div className="table-wrap"><table className="data-table"><caption className="sr-only">Leave Policy version history</caption><thead><tr><th>Version</th><th>Status</th><th>Effective range</th><th>Priority</th><th>Leave Types</th><th>Applicability</th><th>Created</th><th>Action</th></tr></thead><tbody>{[...versions].sort((left, right) => right.versionNumber - left.versionNumber).map(item => <tr key={item.id} className={item.id === current?.id ? 'is-selected' : undefined}><td>Version {item.versionNumber}</td><td><Badge tone={item.status === 'Published' ? 'success' : item.status === 'Draft' ? 'info' : 'neutral'}>{item.status}</Badge></td><td>{formatDateOnly(item.effectiveFrom)} — {item.effectiveTo ? formatDateOnly(item.effectiveTo) : 'Open ended'}</td><td>{item.priority}</td><td>{item.leaveTypeCount}</td><td>{item.applicabilityGroupCount}</td><td>{formatDateTime(item.modifiedDate ?? item.createdDate)}</td><td>{item.id === current?.id ? <span className="muted">Selected</span> : <button className="button button-link" type="button" onClick={() => chooseVersion(item.id)}>Open version</button>}</td></tr>)}</tbody></table></div>{versions.length === 0 ? <p className="muted">No versions have been created.</p> : null}</Card>
    <nav className="leave-policy-wizard" aria-label="Leave Policy setup steps"><ol>{wizardSteps.map(([label, hint], index) => <li key={label} className={activeStep === index + 1 ? 'is-current' : activeStep > index + 1 ? 'is-complete' : undefined}><button type="button" onClick={() => setActiveStep(index + 1)} disabled={index > 0 && !current} aria-current={activeStep === index + 1 ? 'step' : undefined}><span>{index + 1}</span><strong>{label}</strong><small>{hint}</small></button></li>)}</ol></nav>
    <section className="policy-editor-sections" aria-label="Policy editor sections">
      <Card title="1. Policy Details" subtitle="Stable policy identity"><dl className="detail-list"><div><dt>Code</dt><dd><code>{editor.policy.code}</code></dd></div><div><dt>Name</dt><dd>{editor.policy.name}</dd></div><div><dt>Description</dt><dd>{editor.policy.description || 'No description provided.'}</dd></div><div><dt>Status</dt><dd><ActiveBadge isActive={editor.policy.isActive} /></dd></div></dl>{canManage ? <Link className="row-action" to={`/leave-management/policies?edit=${editor.policy.id}`}>Edit policy identity</Link> : null}</Card>
      <Card title="2. Version Settings" subtitle={current ? `Version ${current.versionNumber}` : 'Create a Draft to configure version settings'}>{current ? <><div className="lifecycle-notice"><Badge tone={current.status === 'Draft' ? 'info' : current.status === 'Published' ? 'success' : 'neutral'}>{current.status}</Badge><span>{current.status === 'Draft' ? (canManage ? 'This Draft can be edited.' : 'You can view this Draft but do not have edit permission.') : current.status === 'Published' ? 'This version is Published and cannot be edited.' : 'This version is Retired and retained for history.'}</span></div><form className="form-stack" aria-label="Policy version settings" onSubmit={saveDraft}>{readOnly ? <dl className="detail-list"><div><dt>Effective From</dt><dd>{formatDateOnly(current.effectiveFrom)}</dd></div><div><dt>Effective To</dt><dd>{current.effectiveTo ? formatDateOnly(current.effectiveTo) : 'Open ended'}</dd></div><div><dt>Priority</dt><dd>{current.priority} <small className="field-help">Higher numbers take precedence.</small></dd></div></dl> : <><div className="form-grid"><label className="field"><span>Effective From <em>(required)</em></span><input className={fieldErrors.effectiveFrom ? 'input has-error' : 'input'} type="date" required value={draft.effectiveFrom} onChange={event => editDraft('effectiveFrom', event.target.value)} /></label><label className="field"><span>Effective To</span><input className="input" type="date" value={draft.effectiveTo} onChange={event => editDraft('effectiveTo', event.target.value)} /></label></div><label className="field"><span>Priority</span><input className="input" type="number" min="0" step="1" value={draft.priority} onChange={event => editDraft('priority', Number(event.target.value))} /><small className="field-help">Higher numeric Priority takes precedence when more than one Policy matches.</small></label><div className="form-actions"><button className="button button-primary" type="submit" disabled={saving}>{saving ? <Spinner size={14} label="Saving…" /> : 'Save Draft'}</button></div></>}</form></> : <p className="muted">No version selected. Create a Draft Version to begin.</p>}</Card>
      {stepVisible(3) && current ? <LeavePolicyConfigurationSections policyId={policyId} version={current} selectedLeaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} onSaved={() => refreshCurrentVersion(current.id)} /> : null}
      {stepVisible(4) && current ? <LeavePolicyEligibilitySection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} onSaved={() => refreshCurrentVersion(current.id)} /> : null}
      {stepVisible(5) && current ? <LeavePolicyEntitlementSection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} onSaved={() => refreshCurrentVersion(current.id)} /> : null}
      {stepVisible(6) && current ? <LeavePolicyRequestRulesSection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} onSaved={() => refreshCurrentVersion(current.id)} /> : null}
      {stepVisible(7) && current ? <><LeavePolicyCalendarSection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} onSaved={() => refreshCurrentVersion(current.id)} /><LeavePolicyAttachmentSection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} onSaved={() => refreshCurrentVersion(current.id)} /></> : null}
      {stepVisible(8) && current ? <><details className="advanced-policy-rules"><summary>Advanced Leave Combination Rules</summary><LeavePolicyClubbingSection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} onSaved={() => refreshCurrentVersion(current.id)} /></details><LeavePolicyCancellationSection policyId={policyId} version={current} leaveTypes={editor.leaveTypes} canManage={canManage} onNotice={setNotice} onChanged={markConfigurationChanged} onSaved={() => refreshCurrentVersion(current.id)} /></> : null}
      {stepVisible(9) ? <Card title="9. Review & Publish" subtitle="Review the policy before publishing."><dl className="detail-list"><div><dt>Policy</dt><dd>{editor.policy.name} (<code>{editor.policy.code}</code>)</dd></div><div><dt>Effective</dt><dd>{current ? `${formatDateOnly(current.effectiveFrom)}${current.effectiveTo ? ` to ${formatDateOnly(current.effectiveTo)}` : ' onwards'}` : 'No Draft selected'}</dd></div><div><dt>Leave Types</dt><dd>{selectedLeaveTypeNames || 'None selected yet'}</dd></div><div><dt>Applies To</dt><dd>{current?.applicabilityGroupCount ? `${current.applicabilityGroupCount} configured group${current.applicabilityGroupCount === 1 ? '' : 's'}` : 'All Employees'}</dd></div><div><dt>Status</dt><dd>{current?.status ?? 'No version'}</dd></div></dl>{canValidate ? <button className="button button-primary" type="button" onClick={() => void validateDraft()} disabled={validationBusy}>{validationBusy ? <Spinner size={14} label="Validating…" /> : 'Validate Policy'}</button> : null}</Card> : null}
      <Card title="13. Additional Rules" subtitle="Future configuration section"><p className="muted">Other rules are not part of this phase.</p></Card>
    </section>
    <div className="leave-policy-wizard-footer"><button className="button button-secondary" type="button" onClick={goBack} disabled={activeStep === 1}>Back</button>{activeStep < wizardSteps.length ? <button className="button button-primary" type="button" onClick={goNext} disabled={!current}>Continue</button> : null}</div>
    {lifecycleAction ? <ConfirmDialog title={lifecycleAction === 'publish' ? 'Publish Policy Version?' : 'Retire Policy Version?'} message={lifecycleAction === 'publish' ? `Version ${current?.versionNumber} of ${editor.policy.code} will become Published. Any previous version covering the replaced dates will be superseded automatically.` : `Version ${current?.versionNumber} of ${editor.policy.code} will no longer participate in current or future Policy resolution.`} hint={lifecycleAction === 'publish' ? 'The previous version and its audit references will remain available in Version History.' : 'Historical configuration and effective dates will remain preserved.'} confirmLabel={lifecycleAction === 'publish' ? 'Publish Version' : 'Retire Version'} onConfirm={performLifecycleAction} onClose={() => { if (!lifecycleBusy) setLifecycleAction(null) }} /> : null}
  </div>
}

function PolicyTestPanel({ leaveTypes, onResult }: { leaveTypes: LeavePolicyEditor['leaveTypes']; onResult: (result: LeavePolicyTest | null) => void }) {
  const employees = useApiQuery(signal => listEmployees({ page: 1, pageSize: 100, status: 'Active' }, signal), [])
  const [employeeId, setEmployeeId] = useState('')
  const [leaveTypeId, setLeaveTypeId] = useState(leaveTypes[0]?.id ?? '')
  const [date, setDate] = useState(new Date().toISOString().slice(0, 10))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  async function run() {
    if (!employeeId || !leaveTypeId || !date) { setError('Select an employee, Leave Type, and date.'); return }
    setBusy(true); setError(null)
    try { onResult(await testLeavePolicy({ employeeId, leaveTypeId, date })) } catch (caught) { setError(caught instanceof ApiError ? caught.message : 'Unable to test policy resolution.') } finally { setBusy(false) }
  }
  return <Card title="Test Policy" subtitle="See which Published policy will apply to an employee and date."><div className="form-grid"><label className="field"><span>Employee</span><select className="input" value={employeeId} onChange={event => setEmployeeId(event.target.value)}><option value="">Select employee</option>{(employees.data?.items ?? []).map(employee => <option key={employee.id} value={employee.id}>{employee.fullName} ({employee.employeeCode})</option>)}</select></label><label className="field"><span>Leave Type</span><select className="input" value={leaveTypeId} onChange={event => setLeaveTypeId(event.target.value)}>{leaveTypes.map(type => <option key={type.id} value={type.id}>{type.code} — {type.name}</option>)}</select></label><label className="field"><span>Date</span><input className="input" type="date" value={date} onChange={event => setDate(event.target.value)} /></label></div>{error ? <Notice tone="error">{error}</Notice> : null}<button className="button button-secondary" type="button" onClick={() => void run()} disabled={busy || employees.isLoading}>{busy ? <Spinner size={14} label="Testing…" /> : 'Test Policy'}</button></Card>
}

function formatDateOnly(value: string): string { const [year, month, day] = value.slice(0, 10).split('-').map(Number); if (!year || !month || !day) return '—'; return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(new Date(year, month - 1, day, 12)) }
function formatDateTime(value: string): string { const date = new Date(value); return Number.isNaN(date.getTime()) ? '—' : new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date) }
