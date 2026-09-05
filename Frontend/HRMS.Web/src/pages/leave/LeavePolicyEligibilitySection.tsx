import { useEffect, useState } from 'react'
import { ApiError } from '../../api/errors.ts'
import { getLeaveTypeEligibility, saveLeaveTypeEligibility, type EligibilityMode, type EligibilityServiceUnit, type LeavePolicyEligibilityRule, type LeavePolicyVersion, type NoticePeriodMode, type ProbationMode, type LeaveTypeSelection } from '../../api/leaveConfiguration.ts'
import { Badge } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { Spinner } from '../../components/Spinner.tsx'

interface Props { policyId: string; version: LeavePolicyVersion; leaveTypes: LeaveTypeSelection[]; canManage: boolean; onNotice: (message: string) => void; onChanged?: () => void }
interface FormState { eligibilityMode: EligibilityMode; minimumServiceValue: string; minimumServiceUnit: EligibilityServiceUnit; probationMode: ProbationMode; noticePeriodMode: NoticePeriodMode }

const defaultForm: FormState = { eligibilityMode: 'Immediate', minimumServiceValue: '', minimumServiceUnit: 'Days', probationMode: 'Allowed', noticePeriodMode: 'Allowed' }
function formFromRule(rule: LeavePolicyEligibilityRule | null): FormState { return rule ? { eligibilityMode: rule.eligibilityMode, minimumServiceValue: rule.minimumServiceValue?.toString() ?? '', minimumServiceUnit: rule.minimumServiceUnit ?? 'Days', probationMode: rule.probationMode, noticePeriodMode: rule.noticePeriodMode } : { ...defaultForm } }

export function LeavePolicyEligibilitySection({ policyId, version, leaveTypes, canManage, onNotice, onChanged }: Props) {
  const [selectedTypeId, setSelectedTypeId] = useState(leaveTypes[0]?.id ?? '')
  const [, setRule] = useState<LeavePolicyEligibilityRule | null>(null)
  const [form, setForm] = useState<FormState>({ ...defaultForm })
  const [loading, setLoading] = useState(Boolean(leaveTypes[0]))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const editable = canManage && version.status === 'Draft'

  useEffect(() => { if (!leaveTypes.some(item => item.id === selectedTypeId)) setSelectedTypeId(leaveTypes[0]?.id ?? '') }, [leaveTypes, selectedTypeId])
  useEffect(() => {
    if (!selectedTypeId) { setRule(null); setForm({ ...defaultForm }); setLoading(false); return }
    let cancelled = false
    setLoading(true); setError(null)
    void getLeaveTypeEligibility(policyId, version.id, selectedTypeId).then(value => { if (!cancelled) { setRule(value); setForm(formFromRule(value)) } }).catch(caught => { if (!cancelled) setError(caught instanceof ApiError ? caught : new ApiError('Unable to load Eligibility.')) }).finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [policyId, version.id, selectedTypeId])

  function update<K extends keyof FormState>(key: K, value: FormState[K]) { onChanged?.(); setForm(current => ({ ...current, [key]: value })) }
  async function save() {
    if (!selectedTypeId) return
    setSaving(true); setError(null)
    const minimumServiceValue = form.minimumServiceValue === '' ? null : Number(form.minimumServiceValue)
    try {
      const saved = await saveLeaveTypeEligibility(policyId, version.id, selectedTypeId, { eligibilityMode: form.eligibilityMode, minimumServiceValue: form.eligibilityMode === 'MinimumService' ? minimumServiceValue : null, minimumServiceUnit: form.eligibilityMode === 'MinimumService' ? form.minimumServiceUnit : null, probationMode: form.probationMode, noticePeriodMode: form.noticePeriodMode, concurrencyToken: version.concurrencyToken })
      setRule(saved); setForm(formFromRule(saved)); onChanged?.(); onNotice('Eligibility saved.')
    } catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to save Eligibility.')) } finally { setSaving(false) }
  }
  const selectedType = leaveTypes.find(item => item.id === selectedTypeId)

  return <Card title="5. Eligibility" subtitle="Configure when each selected Leave Type becomes eligible after applicability is matched.">
    {leaveTypes.length === 0 ? <p className="muted">Assign at least one Leave Type before configuring Eligibility.</p> : <>
      <div className="eligibility-type-tabs" role="tablist" aria-label="Leave Types for Eligibility"><span className="field-help">Eligibility is configured separately for each Leave Type.</span>{leaveTypes.map(item => <button key={item.id} type="button" role="tab" aria-selected={item.id === selectedTypeId} className={item.id === selectedTypeId ? 'button button-secondary is-selected' : 'button button-link'} onClick={() => setSelectedTypeId(item.id)}>{item.code} — {item.name}{!item.isActive ? ' (Inactive)' : ''}</button>)}</div>
      {selectedType ? <div className="eligibility-type-heading"><strong>{selectedType.code} — {selectedType.name}</strong>{!selectedType.isActive ? <Badge>Inactive historical reference</Badge> : null}</div> : null}
      {error ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest version before saving.' : ''}</Notice> : null}
      {loading ? <Spinner label="Loading Eligibility" /> : <>{!editable && <div className="lifecycle-notice"><Badge>{version.status}</Badge><span>{version.status === 'Published' ? 'Published Eligibility is immutable.' : version.status === 'Retired' ? 'Retired Eligibility is retained for history.' : 'You can view Eligibility but do not have edit permission.'}</span></div>}<form className="form-stack" aria-label={`${selectedType?.name ?? 'Leave Type'} Eligibility`} onSubmit={event => { event.preventDefault(); void save() }}><fieldset disabled={!editable} className="eligibility-fields"><legend>Eligibility conditions</legend><label className="field"><span>Eligibility Mode</span><select className="input" value={form.eligibilityMode} onChange={event => update('eligibilityMode', event.target.value as EligibilityMode)}><option value="Immediate">Immediate</option><option value="MinimumService">Minimum service</option></select></label>{form.eligibilityMode === 'MinimumService' ? <div className="form-grid"><label className="field"><span>Minimum Service Value <em>(required)</em></span><input className="input" type="number" min="1" step="1" required value={form.minimumServiceValue} onChange={event => update('minimumServiceValue', event.target.value)} /></label><label className="field"><span>Minimum Service Unit</span><select className="input" value={form.minimumServiceUnit} onChange={event => update('minimumServiceUnit', event.target.value as EligibilityServiceUnit)}><option value="Days">Days</option><option value="Months">Months</option></select></label></div> : null}<label className="field"><span>Probation Mode</span><select className="input" value={form.probationMode} onChange={event => update('probationMode', event.target.value as ProbationMode)}><option value="Allowed">Allowed</option><option value="NotAllowed">Not allowed</option>{form.probationMode === 'AfterConfirmation' ? <option value="AfterConfirmation">After confirmation (unavailable)</option> : null}</select>{form.probationMode === 'AfterConfirmation' ? <small className="field-error">After confirmation is unavailable until an authoritative confirmation source is approved.</small> : null}</label><label className="field"><span>Notice Period Mode</span><select className="input" value={form.noticePeriodMode} onChange={event => update('noticePeriodMode', event.target.value as NoticePeriodMode)}><option value="Allowed">Allowed</option><option value="NotAllowed">Not allowed</option><option value="AllowedWithApproval">Allowed with approval</option></select></label></fieldset>{editable ? <div className="form-actions"><button className="button button-primary" type="submit" disabled={saving}>{saving ? <Spinner size={14} label="Saving…" /> : 'Save Eligibility'}</button></div> : null}</form></>}
    </>}
  </Card>
}
