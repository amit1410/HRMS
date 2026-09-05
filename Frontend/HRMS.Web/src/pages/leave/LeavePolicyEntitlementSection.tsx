import { useEffect, useState } from 'react'
import { ApiError } from '../../api/errors.ts'
import { getLeaveTypeEntitlement, saveLeaveTypeEntitlement, type AccrualFrequency, type AccrualTiming, type EntitlementMode, type EntitlementSource, type LeavePolicyEntitlementRule, type LeavePolicyVersion, type LeaveTypeSelection } from '../../api/leaveConfiguration.ts'
import { Badge } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { Spinner } from '../../components/Spinner.tsx'

interface Props { policyId: string; version: LeavePolicyVersion; leaveTypes: LeaveTypeSelection[]; canManage: boolean; onNotice: (message: string) => void; onChanged?: () => void }
interface FormState { entitlementMode: EntitlementMode; entitlementSource: EntitlementSource; entitlementQuantity: string; accrualFrequency: AccrualFrequency; accrualTiming: AccrualTiming }
const defaultForm: FormState = { entitlementMode: 'Allocated', entitlementSource: 'PolicyAccrual', entitlementQuantity: '', accrualFrequency: 'None', accrualTiming: 'StartOfPeriod' }
function formFromRule(rule: LeavePolicyEntitlementRule | null): FormState { return rule ? { entitlementMode: rule.entitlementMode, entitlementSource: rule.entitlementSource, entitlementQuantity: rule.entitlementQuantity?.toString() ?? '', accrualFrequency: rule.accrualFrequency, accrualTiming: rule.accrualTiming ?? 'StartOfPeriod' } : { ...defaultForm } }

export function LeavePolicyEntitlementSection({ policyId, version, leaveTypes, canManage, onNotice, onChanged }: Props) {
  const [selectedTypeId, setSelectedTypeId] = useState(leaveTypes[0]?.id ?? '')
  const [, setRule] = useState<LeavePolicyEntitlementRule | null>(null)
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
    void getLeaveTypeEntitlement(policyId, version.id, selectedTypeId).then(value => { if (!cancelled) { setRule(value); setForm(formFromRule(value)) } }).catch(caught => { if (!cancelled) setError(caught instanceof ApiError ? caught : new ApiError('Unable to load Entitlement.')) }).finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [policyId, version.id, selectedTypeId])

  function update<K extends keyof FormState>(key: K, value: FormState[K]) { onChanged?.(); setForm(current => ({ ...current, [key]: value })) }
  function updateMode(value: EntitlementMode) {
    onChanged?.()
    setForm(current => ({ ...current, entitlementMode: value, entitlementSource: value === 'NoBalanceRequired' ? 'NoBalanceRequired' : current.entitlementSource, entitlementQuantity: value === 'Allocated' ? current.entitlementQuantity : '', accrualFrequency: value === 'NoBalanceRequired' ? 'None' : current.accrualFrequency, accrualTiming: value === 'NoBalanceRequired' ? 'StartOfPeriod' : current.accrualTiming }))
  }
  async function save() {
    if (!selectedTypeId) return
    setSaving(true); setError(null)
    try {
      const saved = await saveLeaveTypeEntitlement(policyId, version.id, selectedTypeId, { entitlementMode: form.entitlementMode, entitlementSource: form.entitlementSource, entitlementQuantity: form.entitlementMode === 'Allocated' && form.entitlementQuantity !== '' ? Number(form.entitlementQuantity) : null, accrualFrequency: form.accrualFrequency, accrualTiming: form.accrualFrequency === 'None' ? null : form.accrualTiming, concurrencyToken: version.concurrencyToken })
      setRule(saved); setForm(formFromRule(saved)); onChanged?.(); onNotice('Entitlement saved.')
    } catch (caught) { setError(caught instanceof ApiError ? caught : new ApiError('Unable to save Entitlement.')) } finally { setSaving(false) }
  }
  const selectedType = leaveTypes.find(item => item.id === selectedTypeId)
  return <Card title="6. Entitlement" subtitle="Configure the quantity and credit source for each selected Leave Type. Runtime balances and credits are deferred.">
    {leaveTypes.length === 0 ? <p className="muted">Assign at least one Leave Type before configuring Entitlement.</p> : <>
      <div className="eligibility-type-tabs" role="tablist" aria-label="Leave Types for Entitlement"><span className="field-help">Entitlement is configured separately for each Leave Type.</span>{leaveTypes.map(item => <button key={item.id} type="button" role="tab" aria-selected={item.id === selectedTypeId} className={item.id === selectedTypeId ? 'button button-secondary is-selected' : 'button button-link'} onClick={() => setSelectedTypeId(item.id)}>{item.code} — {item.name}{!item.isActive ? ' (Inactive)' : ''}</button>)}</div>
      {selectedType ? <div className="eligibility-type-heading"><strong>{selectedType.code} — {selectedType.name}</strong>{!selectedType.isActive ? <Badge>Inactive historical reference</Badge> : null}</div> : null}
      {error ? <Notice tone="error">{error.message}{error.isConflict ? ' Reload the latest version before saving.' : ''}</Notice> : null}
      {loading ? <Spinner label="Loading Entitlement" /> : <>{!editable && <div className="lifecycle-notice"><Badge>{version.status}</Badge><span>{version.status === 'Published' ? 'Published Entitlement is immutable.' : version.status === 'Retired' ? 'Retired Entitlement is retained for history.' : 'You can view Entitlement but do not have edit permission.'}</span></div>}<form className="form-stack" aria-label={`${selectedType?.name ?? 'Leave Type'} Entitlement`} onSubmit={event => { event.preventDefault(); void save() }}><fieldset disabled={!editable} className="eligibility-fields"><legend>Entitlement configuration</legend><label className="field"><span>Entitlement Mode</span><select className="input" value={form.entitlementMode} onChange={event => updateMode(event.target.value as EntitlementMode)}><option value="Allocated">Allocated</option><option value="Unlimited">Unlimited</option><option value="NoBalanceRequired">No balance required</option></select></label><label className="field"><span>Entitlement Source</span><select className="input" value={form.entitlementSource} onChange={event => update('entitlementSource', event.target.value as EntitlementSource)}><option value="PolicyAccrual">Policy accrual</option><option value="ExternalGrant">External grant</option><option value="NoBalanceRequired">No balance required</option></select></label>{form.entitlementMode === 'Allocated' ? <label className="field"><span>Entitlement Quantity <em>(required)</em></span><input className="input" type="number" min="0.001" step="0.001" required value={form.entitlementQuantity} onChange={event => update('entitlementQuantity', event.target.value)} /></label> : <p className="field-help">{form.entitlementMode === 'Unlimited' ? 'No finite allocation quantity is stored.' : 'This Leave Type does not consume a normal balance.'}</p>}<label className="field"><span>Accrual Frequency</span><select className="input" value={form.accrualFrequency} onChange={event => update('accrualFrequency', event.target.value as AccrualFrequency)}><option value="None">None</option><option value="Upfront">Upfront</option><option value="Monthly">Monthly</option><option value="SemiAnnual">Semi-annual</option><option value="Annual">Annual</option></select></label>{form.accrualFrequency !== 'None' ? <label className="field"><span>Accrual Timing</span><select className="input" value={form.accrualTiming} onChange={event => update('accrualTiming', event.target.value as AccrualTiming)}><option value="StartOfPeriod">Start of period</option><option value="EndOfPeriod">End of period</option></select></label> : null}</fieldset>{editable ? <div className="form-actions"><button className="button button-primary" type="submit" disabled={saving}>{saving ? <Spinner size={14} label="Saving…" /> : 'Save Entitlement'}</button></div> : null}</form></>}
    </>}
  </Card>
}
