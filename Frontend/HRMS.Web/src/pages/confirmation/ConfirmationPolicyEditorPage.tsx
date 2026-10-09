import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { useParams } from 'react-router-dom'
import { ApiError } from '../../api/errors.ts'
import {
  createConfirmationPolicyVersion,
  getConfirmationPolicy,
  getConfirmationPolicyEditor,
  publishConfirmationPolicyVersion,
  updateConfirmationPolicyVersion,
  type ApprovalSequence,
  type ConfirmationApprovalRequirement,
  type ConfirmationDecisionRule,
  type ConfirmationOutcome,
  type ConfirmationPolicy,
  type ConfirmationPolicyApplicability,
  type ConfirmationPolicyEditor,
  type ConfirmationProbationConfig,
  type OverallRatingMethod,
  type ProbationDurationUnit,
} from '../../api/confirmation.ts'
import {
  listDepartments,
  listDesignations,
  listEmployeeTypes,
  listGrades,
  listWorkLocations,
} from '../../api/masterData.ts'
import type { MasterLookup } from '../../api/types.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'

const defaultProbation: ConfirmationProbationConfig = {
  probationDuration: 6,
  probationDurationUnit: 'Months',
  triggerDaysBeforeEnd: 30,
  maxExtensions: 0,
  extensionDuration: 3,
  extensionDurationUnit: 'Months',
  requiredApproval: 'ManagerAndHrbp',
  confirmRatingThreshold: null,
  autoConfirmOnApproval: true,
  overallRatingMethod: 'AverageOfRatings',
  approvalSequence: 'Parallel',
  requireReviewerComments: false,
  confirmedJobStatus: null,
  notConfirmedJobStatus: null,
}

const OUTCOME_OPTIONS: { value: ConfirmationOutcome; label: string }[] = [
  { value: 'Confirmed', label: 'Confirm' },
  { value: 'ProbationExtended', label: 'Extend probation' },
  { value: 'NotConfirmed', label: 'Not confirmed' },
  { value: 'AdditionalReview', label: 'Additional review' },
]

const today = () => new Date().toISOString().slice(0, 10)

export function ConfirmationPolicyEditorPage() {
  const { policyId = '' } = useParams()
  const { can } = useAuth()
  const canManage = can(Permissions.confirmation.policyManage)
  const canPublish = can(Permissions.confirmation.policyPublish)

  const [policy, setPolicy] = useState<ConfirmationPolicy | null>(null)
  const [versionId, setVersionId] = useState<string | null>(null)
  const [isDraft, setIsDraft] = useState(true)
  const [effectiveFrom, setEffectiveFrom] = useState(today())
  const [effectiveTo, setEffectiveTo] = useState<string>('')
  const [priority, setPriority] = useState(1)
  const [probation, setProbation] = useState<ConfirmationProbationConfig>(defaultProbation)
  const [applicability, setApplicability] = useState<ConfirmationPolicyApplicability>({})
  const [decisionRules, setDecisionRules] = useState<ConfirmationDecisionRule[]>([])

  const [departments, setDepartments] = useState<MasterLookup[]>([])
  const [designations, setDesignations] = useState<MasterLookup[]>([])
  const [grades, setGrades] = useState<MasterLookup[]>([])
  const [employeeTypes, setEmployeeTypes] = useState<MasterLookup[]>([])
  const [workLocations, setWorkLocations] = useState<MasterLookup[]>([])

  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const applyEditor = useCallback((editor: ConfirmationPolicyEditor) => {
    setPolicy(editor.policy)
    setVersionId(editor.version.id)
    setIsDraft(editor.version.status === 'Draft')
    setEffectiveFrom(editor.version.effectiveFrom.slice(0, 10))
    setEffectiveTo(editor.version.effectiveTo ? editor.version.effectiveTo.slice(0, 10) : '')
    setPriority(editor.version.priority)
    setProbation(editor.probation)
    setApplicability(editor.applicability[0] ?? {})
    setDecisionRules(editor.decisionRules ?? [])
  }, [])

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const [editorResult] = await Promise.allSettled([getConfirmationPolicyEditor(policyId)])
      if (editorResult.status === 'fulfilled') applyEditor(editorResult.value)
      else {
        // No version yet — start a fresh draft against the loaded policy.
        setPolicy(await getConfirmationPolicy(policyId))
        setVersionId(null)
        setIsDraft(true)
      }
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError('Unable to load policy.'))
    } finally {
      setLoading(false)
    }
  }, [policyId, applyEditor])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    const signal = new AbortController()
    void Promise.all([
      listDepartments(undefined, signal.signal),
      listDesignations(undefined, signal.signal),
      listGrades(undefined, signal.signal),
      listEmployeeTypes(undefined, signal.signal),
      listWorkLocations(undefined, signal.signal),
    ])
      .then(([d, des, g, et, wl]) => {
        setDepartments(d)
        setDesignations(des)
        setGrades(g)
        setEmployeeTypes(et)
        setWorkLocations(wl)
      })
      .catch(() => undefined)
    return () => signal.abort()
  }, [])

  function updateProbation<K extends keyof ConfirmationProbationConfig>(key: K, value: ConfirmationProbationConfig[K]) {
    setProbation((previous) => ({ ...previous, [key]: value }))
  }

  function updateApplicability<K extends keyof ConfirmationPolicyApplicability>(key: K, value: string) {
    setApplicability((previous) => ({ ...previous, [key]: value || null }))
  }

  function addDecisionRule() {
    setDecisionRules((previous) => [
      ...previous,
      { priority: (previous.at(-1)?.priority ?? 0) + 1, name: '', outcome: 'Confirmed', minOverallRating: null, maxOverallRating: null, isActive: true },
    ])
  }

  function updateDecisionRule(index: number, patch: Partial<ConfirmationDecisionRule>) {
    setDecisionRules((previous) => previous.map((rule, i) => (i === index ? { ...rule, ...patch } : rule)))
  }

  function removeDecisionRule(index: number) {
    setDecisionRules((previous) => previous.filter((_, i) => i !== index))
  }

  function numberOrNull(value: string): number | null {
    return value === '' ? null : Number(value)
  }

  const hasCriteria = Object.values(applicability).some((value) => value)

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!canManage) return
    setSaving(true)
    setError(null)
    setNotice(null)
    const body = {
      effectiveFrom,
      effectiveTo: effectiveTo || null,
      priority,
      probation,
      applicability: hasCriteria ? [applicability] : [],
      decisionRules,
    }
    try {
      const editor =
        versionId && isDraft
          ? await updateConfirmationPolicyVersion(policyId, versionId, body)
          : await createConfirmationPolicyVersion(policyId, body)
      applyEditor(editor)
      setNotice('Draft saved.')
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError('Unable to save version.'))
    } finally {
      setSaving(false)
    }
  }

  async function publish() {
    if (!versionId) return
    setSaving(true)
    setError(null)
    setNotice(null)
    try {
      await publishConfirmationPolicyVersion(policyId, versionId)
      setNotice('Version published.')
      await load()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError('Unable to publish version.'))
    } finally {
      setSaving(false)
    }
  }

  const fieldErrors = error?.fieldErrors ?? {}

  if (loading) {
    return (
      <div className="leave-admin-page">
        <div className="state-block">
          <Spinner label="Loading policy" />
        </div>
      </div>
    )
  }

  return (
    <div className="leave-admin-page">
      <PageHeader
        title={policy ? `${policy.code} — ${policy.name}` : 'Confirmation Policy'}
        subtitle="Probation configuration & applicability. Draft versions are editable; publish to make a version effective."
      />
      {notice ? (
        <Notice tone="success" onDismiss={() => setNotice(null)}>
          {notice}
        </Notice>
      ) : null}
      {error ? <Notice tone="error">{error.message}</Notice> : null}

      <form className="form-stack" onSubmit={save} aria-busy={saving}>
        <Card title="Version" subtitle={versionId ? `Status: ${isDraft ? 'Draft' : 'Published'}` : 'New draft'}>
          <div className="form-grid">
            <label className="field">
              <span>Effective From *</span>
              <input className="input" type="date" required value={effectiveFrom} disabled={!isDraft} onChange={(e) => setEffectiveFrom(e.target.value)} />
            </label>
            <label className="field">
              <span>Effective To</span>
              <input className={fieldErrors.effectiveTo ? 'input has-error' : 'input'} type="date" value={effectiveTo} disabled={!isDraft} onChange={(e) => setEffectiveTo(e.target.value)} />
            </label>
            <label className="field">
              <span>Priority</span>
              <input className="input" type="number" min={0} max={1000} value={priority} disabled={!isDraft} onChange={(e) => setPriority(Number(e.target.value))} />
            </label>
          </div>
          <p className="field-help">Higher priority wins when several policies apply; ties break on the most specific applicability.</p>
        </Card>

        <Card title="Probation Configuration">
          <div className="form-grid">
            <label className="field">
              <span>Probation Duration *</span>
              <input className={fieldErrors['probation.probationDuration'] ? 'input has-error' : 'input'} type="number" min={1} value={probation.probationDuration} disabled={!isDraft} onChange={(e) => updateProbation('probationDuration', Number(e.target.value))} />
              {fieldErrors['probation.probationDuration'] ? <small className="field-error">{fieldErrors['probation.probationDuration']}</small> : null}
            </label>
            <label className="field">
              <span>Duration Unit</span>
              <select className="input" value={probation.probationDurationUnit} disabled={!isDraft} onChange={(e) => updateProbation('probationDurationUnit', e.target.value as ProbationDurationUnit)}>
                <option value="Months">Months</option>
                <option value="Days">Days</option>
              </select>
            </label>
            <label className="field">
              <span>Review Trigger (days before end)</span>
              <input className="input" type="number" min={0} max={365} value={probation.triggerDaysBeforeEnd} disabled={!isDraft} onChange={(e) => updateProbation('triggerDaysBeforeEnd', Number(e.target.value))} />
            </label>
            <label className="field">
              <span>Max Extensions</span>
              <input className="input" type="number" min={0} max={10} value={probation.maxExtensions} disabled={!isDraft} onChange={(e) => updateProbation('maxExtensions', Number(e.target.value))} />
            </label>
            <label className="field">
              <span>Extension Duration</span>
              <input className="input" type="number" min={0} value={probation.extensionDuration} disabled={!isDraft || probation.maxExtensions === 0} onChange={(e) => updateProbation('extensionDuration', Number(e.target.value))} />
            </label>
            <label className="field">
              <span>Extension Unit</span>
              <select className="input" value={probation.extensionDurationUnit} disabled={!isDraft || probation.maxExtensions === 0} onChange={(e) => updateProbation('extensionDurationUnit', e.target.value as ProbationDurationUnit)}>
                <option value="Months">Months</option>
                <option value="Days">Days</option>
              </select>
            </label>
          </div>
        </Card>

        <Card title="Approval & Decision" subtitle="Which reviews are required before a decision, how the overall rating is computed, and the outcome job statuses.">
          <div className="form-grid">
            <label className="field">
              <span>Required approval</span>
              <select className="input" value={probation.requiredApproval} disabled={!isDraft} onChange={(e) => updateProbation('requiredApproval', e.target.value as ConfirmationApprovalRequirement)}>
                <option value="Manager">Manager only</option>
                <option value="Hrbp">HR / HRBP only</option>
                <option value="ManagerAndHrbp">Manager and HR / HRBP</option>
                <option value="ManagerOrHrbp">Manager or HR / HRBP</option>
              </select>
            </label>
            <label className="field">
              <span>Approval sequence</span>
              <select className="input" value={probation.approvalSequence} disabled={!isDraft} onChange={(e) => updateProbation('approvalSequence', e.target.value as ApprovalSequence)}>
                <option value="Parallel">Parallel (any order)</option>
                <option value="ManagerThenHrbp">Manager then HR / HRBP</option>
              </select>
            </label>
            <label className="field">
              <span>Overall rating method</span>
              <select className="input" value={probation.overallRatingMethod} disabled={!isDraft} onChange={(e) => updateProbation('overallRatingMethod', e.target.value as OverallRatingMethod)}>
                <option value="AverageOfRatings">Average of ratings</option>
                <option value="MinimumRating">Minimum rating</option>
              </select>
            </label>
            <label className="field">
              <span>Require reviewer comments</span>
              <select className="input" value={probation.requireReviewerComments ? 'yes' : 'no'} disabled={!isDraft} onChange={(e) => updateProbation('requireReviewerComments', e.target.value === 'yes')}>
                <option value="no">No</option>
                <option value="yes">Yes</option>
              </select>
            </label>
            <label className="field">
              <span>Confirmed job status</span>
              <input className="input" type="text" maxLength={100} placeholder="Confirmed" value={probation.confirmedJobStatus ?? ''} disabled={!isDraft} onChange={(e) => updateProbation('confirmedJobStatus', e.target.value || null)} />
            </label>
            <label className="field">
              <span>Not-confirmed job status</span>
              <input className="input" type="text" maxLength={100} placeholder="Not Confirmed" value={probation.notConfirmedJobStatus ?? ''} disabled={!isDraft} onChange={(e) => updateProbation('notConfirmedJobStatus', e.target.value || null)} />
            </label>
          </div>
        </Card>

        <Card title="Decision Rules" subtitle="Map an overall-rating range to an outcome. Rules are evaluated by ascending priority; active ranges must not overlap.">
          {decisionRules.length === 0 ? <p className="muted">No decision rules. Without rules, the decider chooses any permitted outcome manually.</p> : null}
          <div className="form-stack">
            {decisionRules.map((rule, index) => (
              <div key={index} className="form-grid" data-testid="decision-rule-row">
                <label className="field">
                  <span>Priority</span>
                  <input className="input" type="number" min={1} value={rule.priority} disabled={!isDraft} onChange={(e) => updateDecisionRule(index, { priority: Number(e.target.value) })} />
                </label>
                <label className="field">
                  <span>Name</span>
                  <input className="input" type="text" value={rule.name} disabled={!isDraft} onChange={(e) => updateDecisionRule(index, { name: e.target.value })} />
                </label>
                <label className="field">
                  <span>Min rating</span>
                  <input className="input" type="number" step="0.001" value={rule.minOverallRating ?? ''} disabled={!isDraft} onChange={(e) => updateDecisionRule(index, { minOverallRating: numberOrNull(e.target.value) })} />
                </label>
                <label className="field">
                  <span>Max rating</span>
                  <input className="input" type="number" step="0.001" value={rule.maxOverallRating ?? ''} disabled={!isDraft} onChange={(e) => updateDecisionRule(index, { maxOverallRating: numberOrNull(e.target.value) })} />
                </label>
                <label className="field">
                  <span>Outcome</span>
                  <select className="input" value={rule.outcome} disabled={!isDraft} onChange={(e) => updateDecisionRule(index, { outcome: e.target.value as ConfirmationOutcome })}>
                    {OUTCOME_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
                  </select>
                </label>
                {isDraft ? (
                  <div className="form-actions">
                    <button className="button button-secondary" type="button" onClick={() => removeDecisionRule(index)}>Remove</button>
                  </div>
                ) : null}
              </div>
            ))}
          </div>
          {isDraft ? (
            <div className="form-actions">
              <button className="button" type="button" onClick={addDecisionRule}>Add decision rule</button>
            </div>
          ) : null}
        </Card>

        <Card title="Applicability / Criteria" subtitle="Leave all blank for a tenant-wide default. Populated dimensions narrow the policy to matching employees.">
          <div className="form-grid">
            <ApplicabilitySelect label="Department" options={departments} value={applicability.departmentId ?? ''} disabled={!isDraft} onChange={(v) => updateApplicability('departmentId', v)} />
            <ApplicabilitySelect label="Designation" options={designations} value={applicability.designationId ?? ''} disabled={!isDraft} onChange={(v) => updateApplicability('designationId', v)} />
            <ApplicabilitySelect label="Grade" options={grades} value={applicability.gradeId ?? ''} disabled={!isDraft} onChange={(v) => updateApplicability('gradeId', v)} />
            <ApplicabilitySelect label="Employee Type" options={employeeTypes} value={applicability.employeeTypeId ?? ''} disabled={!isDraft} onChange={(v) => updateApplicability('employeeTypeId', v)} />
            <ApplicabilitySelect label="Work Location" options={workLocations} value={applicability.workLocationId ?? ''} disabled={!isDraft} onChange={(v) => updateApplicability('workLocationId', v)} />
            <label className="field">
              <span>Gender</span>
              <select className="input" value={applicability.gender ?? ''} disabled={!isDraft} onChange={(e) => updateApplicability('gender', e.target.value)}>
                <option value="">Any</option>
                <option value="Male">Male</option>
                <option value="Female">Female</option>
                <option value="Other">Other</option>
              </select>
            </label>
          </div>
        </Card>

        {canManage ? (
          <div className="form-actions">
            <button className="button button-primary" type="submit" disabled={saving || !isDraft}>
              {saving ? <Spinner size={14} label="Saving…" /> : versionId && isDraft ? 'Save Draft' : 'Create Draft Version'}
            </button>
            {canPublish && versionId && isDraft ? (
              <button className="button button-secondary" type="button" onClick={() => void publish()} disabled={saving}>
                Publish Version
              </button>
            ) : null}
          </div>
        ) : null}
      </form>
    </div>
  )
}

function ApplicabilitySelect({
  label,
  options,
  value,
  disabled,
  onChange,
}: {
  label: string
  options: MasterLookup[]
  value: string
  disabled: boolean
  onChange: (value: string) => void
}) {
  return (
    <label className="field">
      <span>{label}</span>
      <select className="input" value={value} disabled={disabled} onChange={(event) => onChange(event.target.value)}>
        <option value="">Any</option>
        {options.map((option) => (
          <option key={option.id} value={option.id}>
            {option.code} - {option.name}
          </option>
        ))}
      </select>
    </label>
  )
}
