import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Card } from '../components/Card.tsx'
import { Notice } from '../components/Notice.tsx'
import { PageHeader } from '../components/PageHeader.tsx'
import { useApiQuery } from '../hooks/useApiQuery.ts'
import { useFlash } from '../hooks/useFlash.ts'
import { deleteEmployeeCodeRule, getEmployeeCodeConfiguration, getEmployeeCodeRules, getEmployeeCodeRule, saveEmployeeCodeConfiguration, saveEmployeeCodeRule, updateEmployeeCodeRule, type EmployeeCodeRule, type EmployeeCodeRuleRequest } from '../api/employeeCodeConfiguration.ts'
import { MasterDropdown } from '../components/MasterDropdown.tsx'
import { listHoldingCompanies, listLinesOfBusiness, listOrganisations, listDepartments } from '../api/masterData.ts'
import { ApiError } from '../api/errors.ts'

type RuleConditionDraft = { id?: string; clientId: string; field: number; operator: number; valueId: string; valueCode: string; include: boolean }

const fieldValues: Record<string, number> = { HoldingCompany: 0, Lob: 1, Organisation: 2, Department: 3 }
const operatorValues: Record<string, number> = { Equals: 0 }
const segmentValues: Record<string, number> = { FixedText: 0, SequentialNumber: 1, HoldingCompanyCode: 2, LobCode: 3, OrganisationCode: 4, DepartmentCode: 5 }
function enumNumber(value: number | string, names: Record<string, number>): number {
  return typeof value === 'number' ? value : names[value] ?? Number(value)
}
function statusNumber(status: number | string): number { return enumNumber(status, { Draft: 0, Active: 1, Inactive: 2 }) }

function newCondition(field = 3): RuleConditionDraft {
  return { clientId: crypto.randomUUID(), field, operator: 0, valueId: '', valueCode: '', include: true }
}

export function EmployeeCodeConfigurationPage() {
  const flash = useFlash()
  const { data, error, isLoading, refetch } = useApiQuery((signal) => getEmployeeCodeConfiguration(signal), [])
  const rulesQuery = useApiQuery((signal) => getEmployeeCodeRules(signal), [])
  const [autoGenerate, setAutoGenerate] = useState(false)
  const [generationMethod, setGenerationMethod] = useState<'Simple' | 'RuleBased'>('Simple')
  const [prefix, setPrefix] = useState('EMP')
  const [nextNumber, setNextNumber] = useState(1)
  const [padding, setPadding] = useState(0)
  const [separator, setSeparator] = useState('-')
  const [effectiveFrom, setEffectiveFrom] = useState(new Date().toISOString().slice(0, 10))
  const [effectiveTo, setEffectiveTo] = useState('')
  const [versionActive, setVersionActive] = useState(true)
  const [saving, setSaving] = useState(false)
  const [configurationError, setConfigurationError] = useState<string | null>(null)
  const [ruleName, setRuleName] = useState('Default Employee Rule')
  const [rulePriority, setRulePriority] = useState(100)
  const [ruleDefault, setRuleDefault] = useState(true)
  const [ruleStatus, setRuleStatus] = useState(1)
  const [conditions, setConditions] = useState<RuleConditionDraft[]>([newCondition()])
  const [segments, setSegments] = useState<Array<{ id?: string; segmentType: number; fixedValue: string; paddingLength: number }>>([
    { segmentType: 0, fixedValue: 'EMP', paddingLength: 0 },
    { segmentType: 1, fixedValue: '', paddingLength: 5 },
  ])
  const [ruleSaving, setRuleSaving] = useState(false)
  const [ruleError, setRuleError] = useState<string | null>(null)
  const [selectedRuleId, setSelectedRuleId] = useState<string | null>(null)
  const [isRuleLoaded, setIsRuleLoaded] = useState(false)

  useEffect(() => {
    if (!data) return
    const configuredAuto = data.assignmentMode === 'Auto' || data.assignmentMode === 1 || (data.assignmentMode == null && data.autoGenerate)
    setAutoGenerate(configuredAuto)
    setGenerationMethod(data.generationMethod === 'RuleBased' || data.generationMethod === 1 ? 'RuleBased' : 'Simple')
    setPrefix(data.prefix)
    setNextNumber(data.nextNumber)
    setPadding(data.padding)
    setSeparator(data.separator || '-')
    setEffectiveFrom(data.effectiveFrom?.slice(0, 10) || effectiveFrom)
    setEffectiveTo(data.effectiveTo?.slice(0, 10) || '')
    setVersionActive(data.isActive !== false)
  }, [data])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSaving(true)
    setConfigurationError(null)
    try {
      const editingSameVersion = data?.effectiveFrom?.slice(0, 10) === effectiveFrom
      await saveEmployeeCodeConfiguration({ versionId: editingSameVersion ? data?.versionId ?? null : null, isActive: versionActive, autoGenerate, assignmentMode: autoGenerate ? 'Auto' : 'Manual', generationMethod: autoGenerate ? generationMethod : null, prefix: prefix.trim(), nextNumber, padding, separator, effectiveFrom, effectiveTo: effectiveTo || null })
      flash.show('Employee Code configuration saved.')
      await refetch()
    } catch (error) {
      setConfigurationError(error instanceof ApiError ? error.message : 'Unable to save Employee Code configuration.')
    } finally {
      setSaving(false)
    }
  }

  async function addRule(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setRuleError(null)
    if (!ruleName.trim()) { setRuleError('Rule name is required.'); return }
    if (!Number.isFinite(rulePriority) || rulePriority < 0) { setRuleError('Priority is required and must be a valid number.'); return }
    if (selectedRuleId && !isRuleLoaded) { setRuleError('The complete rule is still loading. Please wait.'); return }
    const payload: EmployeeCodeRuleRequest = { configurationVersionId: data?.versionId ?? null, name: ruleName.trim(), priority: rulePriority, isDefault: ruleDefault, status: ruleStatus, conditions: ruleDefault ? [] : conditions.map(c => ({ id: c.id, field: c.field, operator: c.operator, value: c.valueCode || null, referenceId: c.valueId || null })), segments: segments.map((segment, index) => ({ id: segment.id, sequenceOrder: index + 1, segmentType: segment.segmentType, fixedValue: segment.segmentType === 0 ? segment.fixedValue.trim() : null, paddingLength: segment.segmentType === 1 ? segment.paddingLength : null })) }
    if (import.meta.env.DEV) console.debug('SAVE RULE CLICKED', { id: selectedRuleId, ruleName: payload.name, priority: payload.priority, status: payload.status, isDefaultFallback: payload.isDefault, conditions: payload.conditions.map(condition => ({ field: condition.field, operator: condition.operator, referenceId: condition.referenceId, value: condition.value })), segments: payload.segments.map(segment => ({ order: segment.sequenceOrder, segmentType: segment.segmentType, fixedValue: segment.fixedValue, paddingLength: segment.paddingLength })) })
    setRuleSaving(true)
    try {
      const savedConditions = payload.conditions
      const request: EmployeeCodeRuleRequest = { ...payload, conditions: savedConditions, segments: segments.map((segment, index) => ({ id: segment.id, sequenceOrder: index + 1, segmentType: segment.segmentType, fixedValue: segment.segmentType === 0 ? segment.fixedValue.trim() : null, paddingLength: segment.segmentType === 1 ? segment.paddingLength : null })) }
      const saved = selectedRuleId ? await updateEmployeeCodeRule(selectedRuleId, request) : await saveEmployeeCodeRule(request)
      const persisted = await getEmployeeCodeRule(saved.id)
      hydrateRuleEditor(persisted)
      flash.show('Employee Code rule saved.')
      rulesQuery.refetch()
    } catch (error) {
      setRuleError(error instanceof ApiError ? error.message : 'Unable to save Employee Code rule.')
    } finally { setRuleSaving(false) }
  }

  function hydrateRuleEditor(rule: EmployeeCodeRule): void {
    setIsRuleLoaded(false)
    setSelectedRuleId(rule.id)
    setRuleName(rule.name)
    setRulePriority(rule.priority)
    setRuleDefault(rule.isDefault)
    const loadedConditions = rule.conditions.map((condition) => { const field = enumNumber(condition.field, fieldValues); return { clientId: crypto.randomUUID(), field, operator: enumNumber(condition.operator, operatorValues), valueId: condition.referenceId ?? '', valueCode: condition.value ?? '', include: rule.segments.some(segment => enumNumber(segment.segmentType, segmentValues) === ({ 0: 2, 1: 3, 2: 4, 3: 5 } as Record<number, number>)[field]) } })
    setRuleStatus(enumNumber(rule.status, { Draft: 0, Active: 1, Inactive: 2 }))
    setConditions(loadedConditions)
    setSegments(rule.segments.sort((a, b) => a.sequenceOrder - b.sequenceOrder).map(segment => ({ id: segment.id, segmentType: enumNumber(segment.segmentType, segmentValues), fixedValue: segment.fixedValue ?? '', paddingLength: segment.paddingLength ?? 0 })))
    setRuleError(null)
    setIsRuleLoaded(true)
  }

  async function editRule(ruleId: string): Promise<void> {
    setIsRuleLoaded(false)
    try { hydrateRuleEditor(await getEmployeeCodeRule(ruleId)) } catch (error) { setRuleError(error instanceof ApiError ? error.message : 'Unable to load Employee Code rule.') }
  }

  function createNewRule(): void {
    setSelectedRuleId(null)
    setIsRuleLoaded(true)
    setRuleName('')
    setRulePriority(10)
    setRuleDefault(false)
    setRuleStatus(0)
    setConditions([newCondition()])
    setSegments([{ segmentType: 1, fixedValue: '', paddingLength: 5 }])
    setRuleError(null)
  }

  async function deleteRule(rule: EmployeeCodeRule): Promise<void> {
    if (!window.confirm(`Delete rule "${rule.name}"?\n\nThis will remove the rule from this screen and stop it being used for Employee Code generation.\n\nExisting Employee Codes will not be affected.`)) return
    try {
      await deleteEmployeeCodeRule(rule.id)
      if (selectedRuleId === rule.id) createNewRule()
      flash.show('Employee Code rule deleted.')
      rulesQuery.refetch()
    } catch (error) {
      setRuleError(error instanceof ApiError ? error.message : 'Unable to delete Employee Code rule.')
    }
  }

  function rulePreview(): string {
    const codeBySegment: Record<number, string> = {}
    const fieldToSegment: Record<number, number> = { 0: 2, 1: 3, 2: 4, 3: 5 }
    for (const condition of conditions) {
      const segmentType = fieldToSegment[condition.field]
      if (segmentType !== undefined) codeBySegment[segmentType] = condition.valueCode || '…'
    }
    const values = segments.map((segment) => {
      if (segment.segmentType === 0) return segment.fixedValue || 'TEXT'
      if (segment.segmentType === 1) return String(Math.max(1, nextNumber)).padStart(Math.max(0, segment.paddingLength), '0')
      return codeBySegment[segment.segmentType] || '…'
    }).filter(Boolean)
    return values.join(separator)
  }

  return (
    <>
      <PageHeader title="Employee Code Configuration" subtitle="Configure how employee codes are assigned for this workspace." />
      {error ? <Notice tone="error">Unable to load Employee Code configuration.</Notice> : null}
      {configurationError ? <Notice tone="error">{configurationError}</Notice> : null}
      <Card title="Employee Code Assignment" subtitle="Choose whether HR users enter codes or the system generates them.">
        {isLoading ? <p className="muted">Loading configuration…</p> : (
          <form className="form-stack" onSubmit={submit}>
            <fieldset className="choice-group">
              <legend>Assignment mode</legend>
              <label className={`choice-card ${!autoGenerate ? 'choice-card-selected' : ''}`}><input type="radio" checked={!autoGenerate} onChange={() => setAutoGenerate(false)} /> <span><strong>Manual Employee Code</strong><small>HR enters the employee code during Initial Employment.</small></span></label>
              <label className={`choice-card ${autoGenerate ? 'choice-card-selected' : ''}`}><input type="radio" checked={autoGenerate} onChange={() => setAutoGenerate(true)} /> <span><strong>Auto Generate Employee Code</strong><small>The system automatically generates the employee code during Initial Employment.</small></span></label>
            </fieldset>

            {autoGenerate ? <fieldset className="choice-group"><legend>Generation method</legend>
              <label className={`choice-card ${generationMethod === 'Simple' ? 'choice-card-selected' : ''}`}><input type="radio" checked={generationMethod === 'Simple'} onChange={() => setGenerationMethod('Simple')} /> <span><strong>Simple Sequence</strong><small>Generate codes using a fixed prefix and running number.</small></span></label>
              <label className={`choice-card ${generationMethod === 'RuleBased' ? 'choice-card-selected' : ''}`}><input type="radio" checked={generationMethod === 'RuleBased'} onChange={() => setGenerationMethod('RuleBased')} /> <span><strong>Advanced Rule-Based</strong><small>Generate codes from employment attributes and configured rules.</small></span></label>
              {generationMethod === 'RuleBased' ? <p className="field-help">Active rules are evaluated from lowest priority number to highest; the default fallback is used when no specific rule matches.</p> : null}
            </fieldset> : null}

            <div className="form-grid form-grid-3">
              <label className="field"><span>Prefix</span><input value={prefix} maxLength={10} onChange={(e) => setPrefix(e.target.value)} disabled={!autoGenerate} /></label>
              <label className="field"><span>Starting / next number</span><input type="number" min={1} value={nextNumber} onChange={(e) => setNextNumber(Number(e.target.value))} disabled={!autoGenerate} /></label>
              <label className="field"><span>Padding length</span><input type="number" min={0} max={10} value={padding} onChange={(e) => setPadding(Number(e.target.value))} disabled={!autoGenerate} /></label>
              <label className="field"><span>Separator</span><select value={separator} onChange={(e) => setSeparator(e.target.value)} disabled={!autoGenerate}><option value="">None</option><option value="-">-</option><option value="/">/</option><option value=".">.</option><option value="_">_</option></select></label>
              <label className="field"><span>Effective from</span><input type="date" value={effectiveFrom} onChange={(e) => setEffectiveFrom(e.target.value)} /></label>
              <label className="field"><span>Effective to</span><input type="date" value={effectiveTo} onChange={(e) => setEffectiveTo(e.target.value)} /></label>
              <label className="checkbox-field"><input type="checkbox" checked={versionActive} onChange={(e) => setVersionActive(e.target.checked)} /> Version active</label>
            </div>
            {autoGenerate && generationMethod === 'Simple' ? <p className="field-help">Example preview: <strong>{[prefix || 'EMP', String(Math.max(1, nextNumber)).padStart(Math.max(0, padding), '0')].join(separator)}</strong></p> : null}
            <div className="form-actions"><button className="button button-primary" type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save Configuration'}</button></div>
          </form>
        )}
      </Card>
      {autoGenerate && generationMethod === 'RuleBased' ? <Card title="Advanced Rule-Based Generation" subtitle="Rules are evaluated by priority during Initial Employment.">
        {rulesQuery.error ? <Notice tone="error">Unable to load Employee Code rules.</Notice> : null}
        {rulesQuery.data?.length ? <div className="stack-list">{rulesQuery.data.map((rule: EmployeeCodeRule) => { const status = statusNumber(rule.status); return <div className={`list-row rule-list-item${selectedRuleId === rule.id ? ' selected' : ''}`} key={rule.id}><button className="rule-select" type="button" onClick={() => void editRule(rule.id)}><div><strong>{rule.name}</strong><span className="muted">Priority {rule.priority}{rule.isDefault ? ' · Default' : ''}</span></div><span className={`status-badge ${status === 1 ? 'status-active' : status === 2 ? 'status-inactive' : 'status-draft'}`}>{status === 1 ? 'Active' : status === 2 ? 'Inactive' : 'Draft'}</span></button><button className="button button-link" type="button" onClick={() => void editRule(rule.id)}>Edit</button><button className="button button-link" type="button" onClick={() => deleteRule(rule)} aria-label={`Delete ${rule.name}`}>Delete</button></div> })}</div> : <p className="muted">No rules configured yet.</p>}
        <form className="form-stack rule-form" onSubmit={addRule}>
          <h3>Rule Builder</h3>
          {ruleError ? <Notice tone="error">{ruleError}</Notice> : null}
          <div className="form-grid form-grid-3">
            <label className="field"><span>Rule name</span><input value={ruleName} onChange={(e) => setRuleName(e.target.value)} /></label>
            <label className="field"><span>Priority</span><input type="number" min={0} value={rulePriority} onChange={(e) => setRulePriority(Number(e.target.value))} /></label>
            <label className="field"><span>Status</span><select value={ruleStatus} onChange={(e) => setRuleStatus(Number(e.target.value))}><option value={0}>Draft</option><option value={1}>Active</option><option value={2}>Inactive</option></select></label>
          </div>
          <label className="checkbox-field"><input type="checkbox" checked={ruleDefault} onChange={(e) => setRuleDefault(e.target.checked)} /> Default fallback rule</label>
          {!ruleDefault ? <div className="condition-list"><p className="field-help">Which employees should use this rule? All conditions must match.</p><div className="condition-header"><span>Field</span><span>Operator</span><span>Value</span><span>Include in Code</span><span /></div>{conditions.map((condition, index) => { const fetcher = condition.field === 0 ? listHoldingCompanies : condition.field === 1 ? listLinesOfBusiness : condition.field === 2 ? listOrganisations : listDepartments; const label = condition.field === 0 ? 'Holding Company' : condition.field === 1 ? 'LOB' : condition.field === 2 ? 'Organization' : 'Department'; const parentId = condition.field === 1 ? conditions.find(c => c.field === 0)?.valueId : undefined; return <div className="condition-row" key={condition.clientId}><select aria-label={`Condition ${index + 1} field`} value={condition.field} onChange={(e) => setConditions(all => all.map((c) => c.clientId === condition.clientId ? { ...c, field: Number(e.target.value), valueId: '', valueCode: '' } : c))}><option value={0}>Holding Company</option><option value={1}>LOB</option><option value={2}>Organization</option><option value={3}>Department</option></select><select aria-label={`Condition ${index + 1} operator`} value={condition.operator} onChange={(e) => setConditions(all => all.map((c) => c.clientId === condition.clientId ? { ...c, operator: Number(e.target.value) } : c))}><option value={0}>Equals</option></select><MasterDropdown id={`rule-condition-${condition.clientId}`} label={label} value={condition.valueId} valueLabel={condition.valueCode} onChange={(valueId) => setConditions(all => all.map((c) => c.clientId === condition.clientId ? { ...c, valueId, valueCode: '' } : c))} onOptionSelected={(item) => setConditions(all => all.map((c) => c.clientId === condition.clientId ? { ...c, valueCode: item.code } : c))} fetcher={fetcher} parentId={parentId} placeholder="Search master" /><label className="checkbox-field"><input type="checkbox" checked={condition.include} onChange={(e) => setConditions(all => all.map((c) => c.clientId === condition.clientId ? { ...c, include: e.target.checked } : c))} /> Include in Code</label><button className="button button-link" type="button" onClick={() => setConditions(all => all.filter((c) => c.clientId !== condition.clientId))} disabled={conditions.length === 1} aria-label={`Delete condition ${index + 1}`}>Delete</button></div> })}<button className="button button-secondary" type="button" onClick={() => setConditions(all => [...all, newCondition()])}>+ Add Condition</button></div> : <p className="field-help">Default rules have no conditions and are used as the fallback.</p>}
          <div className="segment-list"><p className="field-help">Build the code from ordered segments. Master-code segments use the selected Initial Employment values.</p>
            {segments.map((segment, index) => <div className="segment-row" key={`${index}-${segment.segmentType}`}>
              <span className="segment-order">{index + 1}</span>
              <select value={segment.segmentType} aria-label={`Segment ${index + 1} type`} onChange={(e) => setSegments((all) => all.map((item, i) => i === index ? { ...item, segmentType: Number(e.target.value) } : item))}>
                <option value={0}>Fixed text</option><option value={2}>Holding Company code</option><option value={3}>LOB code</option><option value={4}>Organization code</option><option value={5}>Department code</option><option value={6}>Sub Department code</option><option value={7}>Section code</option><option value={8}>Sub Section code</option><option value={9}>Function code</option><option value={10}>Sub Function code</option><option value={11}>Grade code</option><option value={12}>Designation code</option><option value={13}>Employee type code</option><option value={14}>Country code</option><option value={15}>Location code</option><option value={16}>Work Location code</option><option value={17}>Cost Center code</option><option value={18}>Joining year</option><option value={19}>Joining month</option><option value={1}>Sequential number</option>
              </select>
              {segment.segmentType === 0 ? <input aria-label={`Segment ${index + 1} value`} placeholder="Value" value={segment.fixedValue} onChange={(e) => setSegments((all) => all.map((item, i) => i === index ? { ...item, fixedValue: e.target.value } : item))} /> : null}
              {segment.segmentType === 1 ? <input aria-label={`Segment ${index + 1} padding`} type="number" min={0} max={10} placeholder="Padding" value={segment.paddingLength} onChange={(e) => setSegments((all) => all.map((item, i) => i === index ? { ...item, paddingLength: Number(e.target.value) } : item))} /> : null}
              <button className="button button-link" type="button" onClick={() => setSegments((all) => all.filter((_, i) => i !== index))} disabled={segments.length <= 1}>Remove</button>
            </div>)}
            <button className="button button-secondary" type="button" onClick={() => setSegments((all) => [...all, { segmentType: 1, fixedValue: '', paddingLength: 5 }])}>+ Add segment</button>
            <div className="preview-card"><strong>Sample rule preview</strong><div className="preview-value">{rulePreview()}</div><span className="field-help">Display-only sample; it does not reserve or consume the next sequence number. Padding 8 means an eight-digit sequence.</span></div>
          </div>
          <div className="form-actions"><button className="button button-secondary" type="submit" disabled={ruleSaving}>{ruleSaving ? 'Saving…' : 'Save Rule'}</button><button className="button button-link" type="button" onClick={createNewRule}>Create New</button>{selectedRuleId ? <button className="button button-link" type="button" onClick={() => { const selected = rulesQuery.data?.find(rule => rule.id === selectedRuleId); if (selected) void deleteRule(selected) }}>Delete Rule</button> : null}</div>
        </form>
      </Card> : null}
    </>
  )
}
