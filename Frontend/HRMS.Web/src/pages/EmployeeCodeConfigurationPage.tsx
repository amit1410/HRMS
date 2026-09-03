import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Card } from '../components/Card.tsx'
import { Notice } from '../components/Notice.tsx'
import { PageHeader } from '../components/PageHeader.tsx'
import { useApiQuery } from '../hooks/useApiQuery.ts'
import { useFlash } from '../hooks/useFlash.ts'
import { deleteEmployeeCodeRule, getEmployeeCodeConfiguration, getEmployeeCodeRules, getEmployeeCodeRule, previewEmployeeCode, saveEmployeeCodeConfiguration, saveEmployeeCodeRule, updateEmployeeCodeRule, type EmployeeCodePreview, type EmployeeCodePreviewRequest, type EmployeeCodeRule, type EmployeeCodeRuleRequest } from '../api/employeeCodeConfiguration.ts'
import { MasterDropdown } from '../components/MasterDropdown.tsx'
import type { MasterDropdownProps } from '../components/MasterDropdown.tsx'
import { listHoldingCompanies, listLinesOfBusiness, listOrganisations, listDepartments, listSubDepartments, listSections, listSubSections, listFunctions, listSubFunctions, listGrades, listEmployeeTypes, listWorkLocations, listCostCenters } from '../api/masterData.ts'
import { listDesignations } from '../api/designations.ts'
import { listCountries } from '../api/countries.ts'
import { ApiError } from '../api/errors.ts'

type RuleConditionDraft = { id?: string; clientId: string; field: number; operator: number; valueId: string; valueCode: string; include: boolean }

const fieldValues: Record<string, number> = { HoldingCompany: 0, Lob: 1, Organisation: 2, Department: 3, SubDepartment: 4, Section: 5, SubSection: 6, Function: 7, SubFunction: 8, Grade: 9, Designation: 10, EmployeeType: 11, Country: 12, Location: 13, WorkLocation: 14, CostCenter: 15 }
const operatorValues: Record<string, number> = { Equals: 0 }
const segmentValues: Record<string, number> = { FixedText: 0, SequentialNumber: 1, HoldingCompanyCode: 2, LobCode: 3, OrganisationCode: 4, DepartmentCode: 5, SubDepartmentCode: 6, SectionCode: 7, SubSectionCode: 8, FunctionCode: 9, SubFunctionCode: 10, GradeCode: 11, DesignationCode: 12, EmployeeTypeCode: 13, CountryCode: 14, LocationCode: 15, WorkLocationCode: 16, CostCenterCode: 17, JoiningYear: 18, JoiningMonth: 19, JoiningFinancialYear: 20, CustomConstant: 21 }
const fieldToSegment: Record<number, number> = { 0: 2, 1: 3, 2: 4, 3: 5, 4: 6, 5: 7, 6: 8, 7: 9, 8: 10, 9: 11, 10: 12, 11: 13, 12: 14, 13: 15, 14: 16, 15: 17 }
const fieldParents: Record<number, number> = { 1: 0, 4: 3, 5: 4, 6: 5, 8: 7 }
const listDesignationLookups: MasterDropdownProps['fetcher'] = async (_query, signal) => (await listDesignations({ pageSize: 500, isActive: true }, signal)).items
const fieldDefinitions: Array<{ value: number; label: string; fetcher?: MasterDropdownProps['fetcher']; parentField?: number; unavailable?: string }> = [
  { value: 0, label: 'Holding Company', fetcher: listHoldingCompanies }, { value: 1, label: 'LOB', fetcher: listLinesOfBusiness, parentField: 0 }, { value: 2, label: 'Organization', fetcher: listOrganisations }, { value: 3, label: 'Department', fetcher: listDepartments }, { value: 4, label: 'Sub Department', fetcher: listSubDepartments, parentField: 3 }, { value: 5, label: 'Section', fetcher: listSections, parentField: 4 }, { value: 6, label: 'Sub Section', fetcher: listSubSections, parentField: 5 }, { value: 7, label: 'Function', fetcher: listFunctions }, { value: 8, label: 'Sub Function', fetcher: listSubFunctions, parentField: 7 }, { value: 9, label: 'Grade', fetcher: listGrades }, { value: 10, label: 'Designation', fetcher: listDesignationLookups }, { value: 11, label: 'Employee Type', fetcher: listEmployeeTypes }, { value: 12, label: 'Country', fetcher: async (_query, signal) => (await listCountries({ pageSize: 500, isActive: true }, signal)).items }, { value: 13, label: 'Location', unavailable: 'No separate Location master exists in the employment model.' }, { value: 14, label: 'Work Location', fetcher: listWorkLocations }, { value: 15, label: 'Cost Center', fetcher: listCostCenters },
]
const segmentDefinitions = [{ value: 0, label: 'Fixed text' }, ...fieldDefinitions.map((field) => ({ value: fieldToSegment[field.value], label: `${field.label} code${field.unavailable ? ' (unavailable)' : ''}`, unavailable: Boolean(field.unavailable) })), { value: 18, label: 'Joining year' }, { value: 19, label: 'Joining month' }, { value: 1, label: 'Sequential number' }, { value: 21, label: 'Custom constant' }]
function enumNumber(value: number | string, names: Record<string, number>): number {
  return typeof value === 'number' ? value : names[value] ?? Number(value)
}
function statusNumber(status: number | string): number { return enumNumber(status, { Draft: 0, Active: 1, Inactive: 2 }) }

function newCondition(field = 3): RuleConditionDraft {
  return { clientId: crypto.randomUUID(), field, operator: 0, valueId: '', valueCode: '', include: true }
}

function clearDescendants(all: RuleConditionDraft[], changedField: number): RuleConditionDraft[] {
  const descendants = new Set<number>()
  let parent = changedField
  while (Object.values(fieldParents).includes(parent)) {
    const child = Number(Object.entries(fieldParents).find(([, value]) => value === parent)?.[0])
    if (!Number.isFinite(child)) break
    descendants.add(child)
    parent = child
  }
  return all.map((condition) => descendants.has(condition.field) ? { ...condition, valueId: '', valueCode: '' } : condition)
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
  const [savedRule, setSavedRule] = useState<EmployeeCodeRule | null>(null)
  const [preview, setPreview] = useState<EmployeeCodePreview | null>(null)
  const [previewSaving, setPreviewSaving] = useState(false)

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

  function updateCondition(conditionId: string, change: Partial<RuleConditionDraft>): void {
    setConditions((all) => {
      const current = all.find((condition) => condition.clientId === conditionId)
      const updated = all.map((condition) => condition.clientId === conditionId ? { ...condition, ...change } : condition)
      if (!current) return updated
      const clearedOld = clearDescendants(updated, current.field)
      return change.field === undefined ? clearedOld : clearDescendants(clearedOld, change.field)
    })
  }

  function toggleInclude(condition: RuleConditionDraft, include: boolean): void {
    setConditions((all) => all.map((item) => item.clientId === condition.clientId ? { ...item, include } : item))
    if (include) {
      const segmentType = fieldToSegment[condition.field]
      if (segmentType !== undefined && !segments.some((segment) => segment.segmentType === segmentType))
        setSegments((all) => [...all, { segmentType, fixedValue: '', paddingLength: 0 }])
    }
  }

  function addSegment(segmentType = 1): void {
    if (segmentType === 1 && segments.some((segment) => segment.segmentType === 1)) return
    setSegments((all) => [...all, { segmentType, fixedValue: '', paddingLength: segmentType === 1 ? 5 : 0 }])
  }

  function moveSegment(index: number, offset: number): void {
    setSegments((all) => {
      const target = index + offset
      if (target < 0 || target >= all.length) return all
      const next = [...all]
      const item = next[index]
      if (!item) return all
      next.splice(index, 1)
      next.splice(target, 0, item)
      return next
    })
  }

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
    if (segments.filter((segment) => segment.segmentType === 1).length !== 1) { setRuleError('Active rules require exactly one sequential-number segment.'); return }
    if (segments.some((segment) => (segment.segmentType === 0 || segment.segmentType === 21) && !segment.fixedValue.trim())) { setRuleError('Fixed text and custom constant segments require a value.'); return }
    if (conditions.some((condition) => !fieldDefinitions.find((field) => field.value === condition.field)?.fetcher) || segments.some((segment) => segment.segmentType === 15)) { setRuleError('Location is unavailable because this model has no separate Location master.'); return }
    const payload: EmployeeCodeRuleRequest = { configurationVersionId: data?.versionId ?? null, name: ruleName.trim(), priority: rulePriority, isDefault: ruleDefault, status: ruleStatus, conditions: ruleDefault ? [] : conditions.map(c => ({ id: c.id, field: c.field, operator: c.operator, value: c.valueCode || null, referenceId: c.valueId || null })), segments: segments.map((segment, index) => ({ id: segment.id, sequenceOrder: index + 1, segmentType: segment.segmentType, fixedValue: segment.segmentType === 0 || segment.segmentType === 21 ? segment.fixedValue.trim() : null, paddingLength: segment.segmentType === 1 ? segment.paddingLength : null })) }
    if (import.meta.env.DEV) console.debug('SAVE RULE CLICKED', { id: selectedRuleId, ruleName: payload.name, priority: payload.priority, status: payload.status, isDefaultFallback: payload.isDefault, conditions: payload.conditions.map(condition => ({ field: condition.field, operator: condition.operator, referenceId: condition.referenceId, value: condition.value })), segments: payload.segments.map(segment => ({ order: segment.sequenceOrder, segmentType: segment.segmentType, fixedValue: segment.fixedValue, paddingLength: segment.paddingLength })) })
    setRuleSaving(true)
    try {
      const savedConditions = payload.conditions
      const request: EmployeeCodeRuleRequest = { ...payload, conditions: savedConditions, segments: segments.map((segment, index) => ({ id: segment.id, sequenceOrder: index + 1, segmentType: segment.segmentType, fixedValue: segment.segmentType === 0 || segment.segmentType === 21 ? segment.fixedValue.trim() : null, paddingLength: segment.segmentType === 1 ? segment.paddingLength : null })) }
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
    setSavedRule(rule)
    setPreview(null)
    setRuleName(rule.name)
    setRulePriority(rule.priority)
    setRuleDefault(rule.isDefault)
    const loadedConditions = rule.conditions.map((condition) => { const field = enumNumber(condition.field, fieldValues); return { id: condition.id, clientId: crypto.randomUUID(), field, operator: enumNumber(condition.operator, operatorValues), valueId: condition.referenceId ?? '', valueCode: condition.value ?? '', include: rule.segments.some(segment => enumNumber(segment.segmentType, segmentValues) === fieldToSegment[field]) } })
    setRuleStatus(enumNumber(rule.status, { Draft: 0, Active: 1, Inactive: 2 }))
    setConditions(loadedConditions)
    setSegments([...rule.segments].sort((a, b) => a.sequenceOrder - b.sequenceOrder).map(segment => ({ id: segment.id, segmentType: enumNumber(segment.segmentType, segmentValues), fixedValue: segment.fixedValue ?? '', paddingLength: segment.paddingLength ?? 0 })))
    setRuleError(null)
    setIsRuleLoaded(true)
  }

  async function editRule(ruleId: string): Promise<void> {
    setIsRuleLoaded(false)
    try { hydrateRuleEditor(await getEmployeeCodeRule(ruleId)) } catch (error) { setRuleError(error instanceof ApiError ? error.message : 'Unable to load Employee Code rule.') }
  }

  function createNewRule(): void {
    setSelectedRuleId(null)
    setSavedRule(null)
    setPreview(null)
    setIsRuleLoaded(true)
    setRuleName('')
    setRulePriority(10)
    setRuleDefault(false)
    setRuleStatus(0)
    setConditions([newCondition()])
    setSegments([{ segmentType: 1, fixedValue: '', paddingLength: 5 }])
    setRuleError(null)
  }

  async function runSavedPreview(): Promise<void> {
    if (!savedRule || !data?.versionId) return
    setPreviewSaving(true)
    setRuleError(null)
    const fieldProperties: Record<number, keyof Omit<EmployeeCodePreviewRequest, 'effectiveFrom'>> = {
      0: 'holdingCompanyId', 1: 'lobId', 2: 'organisationId', 3: 'departmentId', 4: 'subDepartmentId', 5: 'sectionId', 6: 'subSectionId', 7: 'functionId', 8: 'subFunctionId', 9: 'gradeId', 10: 'designationId', 11: 'employeeTypeId', 12: 'countryLocationId', 14: 'workLocationId', 15: 'costCenterId'
    }
    const request: EmployeeCodePreviewRequest = { effectiveFrom: data.effectiveFrom?.slice(0, 10) ?? effectiveFrom }
    for (const condition of savedRule.conditions) {
      const property = fieldProperties[enumNumber(condition.field, fieldValues)]
      if (property && condition.referenceId) request[property] = condition.referenceId
    }
    try { setPreview(await previewEmployeeCode(request)) } catch (error) { setRuleError(error instanceof ApiError ? error.message : 'Unable to generate saved-rule preview.') } finally { setPreviewSaving(false) }
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
          {!ruleDefault ? <div className="condition-list"><p className="field-help">Which employees should use this rule? All conditions must match.</p><div className="condition-header"><span>Field</span><span>Operator</span><span>Value</span><span>Include in Code</span><span /></div>{conditions.map((condition, index) => { const definition = fieldDefinitions.find((field) => field.value === condition.field); const parentId = definition?.parentField === undefined ? undefined : conditions.find((item) => item.field === definition.parentField)?.valueId; return <div className="condition-row" key={condition.clientId}><select aria-label={`Condition ${index + 1} field`} value={condition.field} onChange={(e) => updateCondition(condition.clientId, { field: Number(e.target.value), valueId: '', valueCode: '' })}>{fieldDefinitions.map((field) => <option key={field.value} value={field.value} disabled={Boolean(field.unavailable)}>{field.label}{field.unavailable ? ' (unavailable)' : ''}</option>)}</select><select aria-label={`Condition ${index + 1} operator`} value={condition.operator} onChange={(e) => updateCondition(condition.clientId, { operator: Number(e.target.value) })}><option value={0}>Equals</option></select>{definition?.fetcher ? <MasterDropdown id={`rule-condition-${condition.clientId}`} label={definition.label} value={condition.valueId} valueLabel={condition.valueCode} onChange={(valueId) => updateCondition(condition.clientId, { valueId, valueCode: '' })} onOptionSelected={(item) => updateCondition(condition.clientId, { valueCode: item.code })} fetcher={definition.fetcher} parentId={parentId} placeholder={parentId === undefined && definition.parentField !== undefined ? 'Select parent first' : 'Search master'} disabled={definition.parentField !== undefined && !parentId} hint={condition.valueId && !condition.valueCode ? 'Saved value is inactive or unavailable in the current tenant.' : undefined} /> : <p className="field-error">Location is unavailable: no separate Location master exists.</p>}<label className="checkbox-field"><input type="checkbox" checked={condition.include} onChange={(e) => toggleInclude(condition, e.target.checked)} /> Include in Code</label><button className="button button-link" type="button" onClick={() => setConditions((all) => all.filter((item) => item.clientId !== condition.clientId))} disabled={conditions.length === 1} aria-label={`Delete condition ${index + 1}`}>Delete</button></div> })}<button className="button button-secondary" type="button" onClick={() => setConditions((all) => [...all, newCondition()])}>+ Add Condition</button></div> : <p className="field-help">Default rules have no conditions and are used as the fallback.</p>}
          <div className="segment-list"><p className="field-help">Build the code from ordered segments. Master-code segments use the selected Initial Employment values.</p>
            {segments.map((segment, index) => <div className="segment-row" key={segment.id ?? `${index}-${segment.segmentType}`}>
              <span className="segment-order">{index + 1}</span>
              <select value={segment.segmentType} aria-label={`Segment ${index + 1} type`} onChange={(e) => { const nextType = Number(e.target.value); if (segments.some((item, itemIndex) => itemIndex !== index && item.segmentType === nextType && nextType === 1)) return; setSegments((all) => all.map((item, i) => i === index ? { ...item, segmentType: nextType, fixedValue: nextType === 0 || nextType === 21 ? item.fixedValue : '', paddingLength: nextType === 1 ? item.paddingLength || 5 : 0 } : item)) }}>
                {segmentDefinitions.map((definition) => <option key={definition.value} value={definition.value} disabled={'unavailable' in definition && definition.unavailable}>{definition.label}</option>)}
              </select>
              {segment.segmentType === 0 || segment.segmentType === 21 ? <input aria-label={`Segment ${index + 1} value`} placeholder="Value" value={segment.fixedValue} onChange={(e) => setSegments((all) => all.map((item, i) => i === index ? { ...item, fixedValue: e.target.value } : item))} /> : null}
              {segment.segmentType === 1 ? <input aria-label={`Segment ${index + 1} padding`} type="number" min={0} max={10} placeholder="Padding" value={segment.paddingLength} onChange={(e) => setSegments((all) => all.map((item, i) => i === index ? { ...item, paddingLength: Number(e.target.value) } : item))} /> : null}
              <button className="button button-link" type="button" onClick={() => moveSegment(index, -1)} disabled={index === 0} aria-label={`Move segment ${index + 1} up`}>Move Up</button><button className="button button-link" type="button" onClick={() => moveSegment(index, 1)} disabled={index === segments.length - 1} aria-label={`Move segment ${index + 1} down`}>Move Down</button><button className="button button-link" type="button" onClick={() => setSegments((all) => all.filter((_, i) => i !== index))} disabled={segments.length <= 1}>Remove</button>
            </div>)}
            <button className="button button-secondary" type="button" onClick={() => addSegment()}>+ Add segment</button>
            <div className="preview-card"><strong>Unsaved editor preview</strong><div className="preview-value">{rulePreview()}</div><span className="field-help">This is a local rendering of unsaved edits. Save the rule before using the backend preview.</span></div>
            {savedRule ? <div className="preview-card"><strong>Saved-rule preview</strong><button className="button button-secondary" type="button" onClick={() => void runSavedPreview()} disabled={previewSaving}>{previewSaving ? 'Resolving…' : 'Preview saved rule'}</button>{preview ? <><div className="preview-value">{preview.code}</div><span className="field-help">Version {preview.versionId}; rule “{preview.ruleName}”; preview sequence {preview.previewSequence} is unreserved and may change when an employee is saved.</span></> : <span className="field-help">Uses the last persisted rule and sample values from its saved conditions.</span>}</div> : <p className="field-help">Save this rule before requesting a backend preview.</p>}
          </div>
          <div className="form-actions"><button className="button button-secondary" type="submit" disabled={ruleSaving}>{ruleSaving ? 'Saving…' : 'Save Rule'}</button><button className="button button-link" type="button" onClick={createNewRule}>Create New</button>{selectedRuleId ? <button className="button button-link" type="button" onClick={() => { const selected = rulesQuery.data?.find(rule => rule.id === selectedRuleId); if (selected) void deleteRule(selected) }}>Delete Rule</button> : null}</div>
        </form>
      </Card> : null}
    </>
  )
}
