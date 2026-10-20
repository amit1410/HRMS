import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import {
  createEmploymentChange,
  getEmploymentHistory,
} from '../../api/employeeSubsections.ts'
import { getEmployeeCodeConfiguration } from '../../api/employeeCodeConfiguration.ts'
import { ApiError, toApiError, hasFieldErrors } from '../../api/errors.ts'
import type {
  EmployeeEmploymentHistory,
  EmploymentChangeRequest,
  EmploymentChangeReason,
  EmploymentType,
  EmployeeStatus,
} from '../../api/types.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { EMPLOYMENT_CHANGE_REASONS } from '../../api/types.ts'
import { Card } from '../../components/Card.tsx'
import { SelectField, TextField, type SelectOption } from '../../components/fields.tsx'
import { ErrorState } from '../../components/ErrorState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { formatDate } from '../../lib/format.ts'
import {
  loadCountryOptions,
  loadDepartmentOptions,
  loadDesignationOptions,
  loadEmployeeTypeOptions,
  loadFunctionOptions,
  loadGradeOptions,
  loadHoldingCompanyOptions,
  loadLobOptions,
  loadOrganisationOptions,
  loadPositionChangeReasonOptions,
  loadSectionOptions,
  loadSubDepartmentOptions,
  loadSubFunctionOptions,
  loadSubSectionOptions,
  loadWorkLocationOptions,
} from './referenceOptions.ts'

interface EmploymentSectionFormProps {
  /** The employee whose employment history is being viewed/appended to. */
  employeeId: string
  /** Refreshes the employee header after Initial Employment assigns a pending code. */
  employeeCode?: string
  onSaved?: () => void | Promise<void>
}

interface FormValues {
  employeeCode: string
  effectiveFrom: string
  changeReason: EmploymentChangeReason
  holdingCompanyId: string
  lobId: string
  organisationId: string
  departmentId: string
  subDepartmentId: string
  sectionId: string
  subSectionId: string
  functionId: string
  subFunctionId: string
  gradeId: string
  designationId: string
  employeeTypeId: string
  countryLocationId: string
  workLocationId: string
  costCenterId: string
  positionChangeReasonId: string
  businessRole: string
  gradeLevel: string
  careerGroup: string
  employmentType: EmploymentType
  employmentStatus: EmployeeStatus
}

const EMPTY: FormValues = {
  employeeCode: '',
  effectiveFrom: '',
  changeReason: 'NewJoining',
  holdingCompanyId: '',
  lobId: '',
  organisationId: '',
  departmentId: '',
  subDepartmentId: '',
  sectionId: '',
  subSectionId: '',
  functionId: '',
  subFunctionId: '',
  gradeId: '',
  designationId: '',
  employeeTypeId: '',
  countryLocationId: '',
  workLocationId: '',
  costCenterId: '',
  positionChangeReasonId: '',
  businessRole: '',
  gradeLevel: '',
  careerGroup: '',
  employmentType: 'FullTime',
  employmentStatus: 'Active',
}

/** When a parent in the dependent chain is cleared, its children must be too. */
const CHILDREN: Partial<Record<keyof FormValues, (keyof FormValues)[]>> = {
  holdingCompanyId: ['lobId'],
  departmentId: ['subDepartmentId'],
  subDepartmentId: ['sectionId'],
  sectionId: ['subSectionId'],
  functionId: ['subFunctionId'],
}

function toRequest(values: FormValues): EmploymentChangeRequest {
  return {
    employeeCode: values.employeeCode || null,
    effectiveFrom: values.effectiveFrom,
    changeReason: values.changeReason,
    holdingCompanyId: values.holdingCompanyId || null,
    lobId: values.lobId || null,
    organisationId: values.organisationId || null,
    departmentId: values.departmentId || null,
    subDepartmentId: values.subDepartmentId || null,
    sectionId: values.sectionId || null,
    subSectionId: values.subSectionId || null,
    functionId: values.functionId || null,
    subFunctionId: values.subFunctionId || null,
    gradeId: values.gradeId || null,
    designationId: values.designationId || null,
    employeeTypeId: values.employeeTypeId || null,
    countryLocationId: values.countryLocationId || null,
    workLocationId: values.workLocationId || null,
    costCenterId: values.costCenterId || null,
    positionChangeReasonId: values.positionChangeReasonId || null,
    businessRole: values.businessRole || null,
    gradeLevel: values.gradeLevel || null,
    careerGroup: values.careerGroup || null,
    employmentType: values.employmentType,
    employmentStatus: values.employmentStatus,
  }
}

function formValuesFromHistory(record: EmployeeEmploymentHistory, employeeCode = ''): FormValues {
  return {
    employeeCode,
    effectiveFrom: record.effectiveFrom,
    changeReason: record.changeReason,
    holdingCompanyId: record.holdingCompanyId ?? '',
    lobId: record.lobId ?? '',
    organisationId: record.organisationId ?? '',
    departmentId: record.departmentId ?? '',
    subDepartmentId: record.subDepartmentId ?? '',
    sectionId: record.sectionId ?? '',
    subSectionId: record.subSectionId ?? '',
    functionId: record.functionId ?? '',
    subFunctionId: record.subFunctionId ?? '',
    gradeId: record.gradeId ?? '',
    designationId: record.designationId ?? '',
    employeeTypeId: record.employeeTypeId ?? '',
    countryLocationId: record.countryLocationId ?? '',
    workLocationId: record.workLocationId ?? '',
    costCenterId: record.costCenterId ?? '',
    positionChangeReasonId: record.positionChangeReasonId ?? '',
    businessRole: record.businessRole ?? '',
    gradeLevel: record.gradeLevel ?? '',
    careerGroup: record.careerGroup ?? '',
    employmentType: record.employmentType,
    employmentStatus: record.employmentStatus,
  }
}

/**
 * An employee's employment history — an append-only, effective-dated trail of position changes.
 *
 * Every change (New Hire, Promotion, Transfer, …) creates a *new* transaction effective on the given
 * date; the service atomically closes whatever is current and opens the new period, so the table always
 * reads as a clean timeline in effective-From-descending order. There is deliberately no Edit or Delete
 * here — history is immutable. The reason for a change is picked from the tenant's own change-reason
 * master, never typed in.
 */
export function EmploymentSectionForm({ employeeId, employeeCode = '', onSaved }: EmploymentSectionFormProps) {
  const { can } = useAuth()
  const [values, setValues] = useState<FormValues>(() => ({ ...EMPTY, employeeCode }))
  const [error, setError] = useState<ApiError | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [step, setStep] = useState(0)
  const hydratedEmployeeId = useRef<string | null>(null)

  const history = useApiQuery((signal) => getEmploymentHistory(employeeId, signal), [employeeId])
  const codeConfiguration = useApiQuery((signal) => getEmployeeCodeConfiguration(signal), [employeeId])

  const manualCode = codeConfiguration.data?.assignmentMode === 'Manual' || codeConfiguration.data?.assignmentMode === 0

  useEffect(() => {
    const current = history.data?.find((record) => isEffectiveToday(record))
    if (!current || hydratedEmployeeId.current === employeeId) return
    setValues((previous) => formValuesFromHistory(current, employeeCode || previous.employeeCode))
    hydratedEmployeeId.current = employeeId
  }, [employeeId, employeeCode, history.data])

  // Dependent org hierarchy — each level loads only once its parent is chosen.
  const holdingCompanies = useApiQuery((signal) => loadHoldingCompanyOptions({ activeOnly: true }, signal), [])
  const lobs = useApiQuery(
    (signal) =>
      values.holdingCompanyId
        ? loadLobOptions(values.holdingCompanyId, { activeOnly: true }, signal)
        : Promise.resolve(null),
    [values.holdingCompanyId],
  )
  const organisations = useApiQuery((signal) => loadOrganisationOptions({ activeOnly: true }, signal), [])
  const departments = useApiQuery((signal) => loadDepartmentOptions({ activeOnly: true }, signal), [])
  const subDepartments = useApiQuery(
    (signal) =>
      values.departmentId
        ? loadSubDepartmentOptions(values.departmentId, { activeOnly: true }, signal)
        : Promise.resolve(null),
    [values.departmentId],
  )
  const sections = useApiQuery(
    (signal) =>
      values.subDepartmentId
        ? loadSectionOptions(values.subDepartmentId, { activeOnly: true }, signal)
        : Promise.resolve(null),
    [values.subDepartmentId],
  )
  const subSections = useApiQuery(
    (signal) =>
      values.sectionId
        ? loadSubSectionOptions(values.sectionId, { activeOnly: true }, signal)
        : Promise.resolve(null),
    [values.sectionId],
  )
  const functions = useApiQuery((signal) => loadFunctionOptions({ activeOnly: true }, signal), [])
  const subFunctions = useApiQuery(
    (signal) =>
      values.functionId
        ? loadSubFunctionOptions(values.functionId, { activeOnly: true }, signal)
        : Promise.resolve(null),
    [values.functionId],
  )

  const grades = useApiQuery((signal) => loadGradeOptions({ activeOnly: true }, signal), [])
  const designations = useApiQuery((signal) => loadDesignationOptions({ activeOnly: true }, signal), [])
  const employeeTypes = useApiQuery((signal) => loadEmployeeTypeOptions({ activeOnly: true }, signal), [])
  const countries = useApiQuery((signal) => loadCountryOptions({ activeOnly: true }, signal), [])
  const workLocations = useApiQuery((signal) => loadWorkLocationOptions({ activeOnly: true }, signal), [])
  const changeReasons = useApiQuery(
    (signal) => loadPositionChangeReasonOptions({ activeOnly: true }, signal),
    [],
  )

  const fieldError = (field: string) => error?.fieldErrors[field]
  const stepLabels = ['Employment Details', 'Organization Structure', 'Position Details', 'Location Details']
  const stepFields: (keyof FormValues)[][] = [['effectiveFrom', 'employeeCode'], [], [], []]

  function validateStep(nextStep: number): void {
    const missing = (stepFields[step] ?? []).filter((field) => {
      if (field === 'employeeCode' && !(manualCode && !employeeCode)) return false
      return !String(values[field] ?? '').trim()
    })
    if (missing.length) {
      setError(new ApiError('Complete the required fields before continuing.', { status: 400, fieldErrors: Object.fromEntries(missing.map((field) => [field, 'This field is required.'])) }))
      return
    }
    setError(null)
    setStep(nextStep)
  }

  function update<F extends keyof FormValues>(field: F, value: FormValues[F]): void {
    setError(null)
    setValues((current) => {
      const next = { ...current, [field]: value }
      const clear = next as unknown as Record<string, unknown>
      for (const child of CHILDREN[field] ?? []) {
        clear[child] = ''
      }
      return next
    })
  }

  function reset(): void {
    setValues(EMPTY)
    setStep(0)
    setError(null)
    setSuccess(null)
  }

  async function saveEmploymentChange(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (saving) return

    setSaving(true)
    setError(null)
    setSuccess(null)
    try {
      if (import.meta.env.DEV) console.debug('EMPLOYMENT SAVE CLICKED', { employeeId, effectiveDate: values.effectiveFrom, changeReason: values.positionChangeReasonId, holdingCompanyId: values.holdingCompanyId, lobId: values.lobId, organisationId: values.organisationId, departmentId: values.departmentId, designationId: values.designationId })
      const saved = await createEmploymentChange(employeeId, toRequest(values))
      setValues((previous) => formValuesFromHistory(saved, employeeCode || previous.employeeCode))
      setSuccess('Employment change recorded.')
      await history.refetch()
      await onSaved?.()
    } catch (caught) {
      const apiError = toApiError(caught)
      const firstField = Object.keys(apiError.fieldErrors)[0] as keyof FormValues | undefined
      if (firstField) {
        const fieldStep = stepFields.findIndex((fields) => fields.includes(firstField))
        if (fieldStep >= 0) setStep(fieldStep)
      }
      setError(apiError)
    } finally {
      setSaving(false)
    }
  }

  if (history.error && !history.data) {
    return (
      <Card className="form-card">
        <ErrorState error={history.error} onRetry={history.refetch} />
      </Card>
    )
  }

  const records = history.data ?? []
  const current = records.find((record) => isEffectiveToday(record))
  const sameDayCorrection = values.changeReason === 'Correction' && records.some((record) => record.effectiveFrom === values.effectiveFrom && record.isSuperseded !== true)
  const display = (value: string | null | undefined) => value || '—'
  const period = (record: (typeof records)[number]) =>
    `${formatDate(record.effectiveFrom)} – ${record.effectiveTo ? formatDate(record.effectiveTo) : 'Present'}`

  return (
    <>
      {error && !hasFieldErrors(error) && <Notice tone="error">{error.message}</Notice>}
      {success && (
        <div className="employment-alert" role="status">
          <span aria-hidden="true">✓</span> Employment change recorded successfully.<span className="sr-only"> Employment change recorded.</span>
          <button type="button" className="employment-alert-dismiss" onClick={() => setSuccess(null)} aria-label="Dismiss notification">×</button>
        </div>
      )}
      <div className="employment-layout">
        <div className="employment-overview">
          <Card className="employment-card current-employment-card">
            <div className="employment-card-heading"><div><p className="eyebrow">Current Employment</p><h2>{display(current?.designationName)}</h2></div>{current && <span className="employment-status-badge current">{current.employmentStatus}</span>}</div>
            {current ? <div className="employment-summary-grid">
              <Summary label="Department" value={current.departmentName} />
              <Summary label="Location" value={current.workLocationName || current.countryLocationName} />
              <Summary label="Effective From" value={formatDate(current.effectiveFrom)} />
              <Summary label="Employment Status" value={current.employmentStatus} />
              <Summary label="Grade" value={current.gradeName} />
              <Summary label="Reporting Manager" value={managerDisplay(current)} />
              <Summary label="Change Reason" value={current.positionChangeReasonName} />
              <Summary label="Employee Type" value={current.employeeTypeName} />
              <Summary label="Cost Center" value={current.costCenterName} />
              <Summary label="Organization" value={current.organisationName} />
            </div> : <Notice tone="info">No current employment recorded yet.</Notice>}
          </Card>

          <Card className="employment-card history-card">
            <div className="employment-card-heading"><div><p className="eyebrow">Employment History</p><h2>Position changes</h2></div><span className="history-count">{records.length} record{records.length === 1 ? '' : 's'}</span></div>
            {history.isLoading && !history.data ? <div className="table-loading"><Spinner label="Loading employment history…" /></div> : records.length ? <div className="employment-timeline">
              {records.map((record) => <article key={record.id} className={`employment-timeline-item${record.id === current?.id ? ' is-current' : ''}`}>
                <div className="timeline-marker" aria-hidden="true">{record.id === current?.id ? '●' : '○'}</div>
                <div className="timeline-content"><div className="timeline-meta"><span>{employmentState(record, current?.id)}</span><time>{period(record)}<span className="sr-only"> {record.effectiveFrom}</span></time></div>
                  <div className="timeline-title-row"><h3>{display(record.designationName)}</h3><span className="change-reason-badge">{display(record.positionChangeReasonName)}</span></div>
                  <p className="timeline-subtitle">{display(record.departmentName)} · {display(record.organisationName)}</p>
                  <dl className="timeline-details"><div><dt>Grade</dt><dd>{display(record.gradeName)}</dd></div><div><dt>Location</dt><dd>{display(record.workLocationName)}</dd></div><div><dt>Reporting Manager</dt><dd>{managerDisplay(record)}</dd></div><div><dt>Effective</dt><dd>{period(record)}</dd></div></dl>
                </div>
              </article>)}
            </div> : <Notice tone="info">No employment history recorded yet. Add the first entry below.</Notice>}
          </Card>
        </div>

        <Card className="employment-card change-panel">
          <div className="employment-card-heading"><div><p className="eyebrow">Employment</p><h2>Add Employment Change</h2></div></div>
            {!can(Permissions.employmentHistory.change) ? <Notice tone="info">You do not have permission to add employment changes.</Notice> : <>
            {sameDayCorrection && <Notice tone="info">This will create a new correction revision for the employment record effective {formatDate(values.effectiveFrom)}.</Notice>}
            <div className="employment-stepper" aria-label="Employment change steps">
              {stepLabels.map((label, index) => <button key={label} type="button" className={step === index ? 'is-active' : ''} aria-current={step === index ? 'step' : undefined} onClick={() => index <= step && setStep(index)}>{index + 1}<span>{label}</span></button>)}
            </div>
            <form className="employment-editor" onSubmit={saveEmploymentChange} noValidate>
              <div hidden={step !== 0}><EmploymentAccordion title="1. Employment Details" defaultOpen>
                <div className="form-grid">
                  {manualCode && !employeeCode && (
                    <TextField
                      id="employeeCode"
                      label="Employee code"
                      value={values.employeeCode}
                      onChange={(value) => update('employeeCode', value)}
                      maxLength={50}
                      required
                      hint="Required for the first employment record when manual assignment is enabled."
                      error={fieldError('employeeCode')}
                    />
                  )}
                  <TextField
                    id="effectiveFrom"
                    label="Effective date"
                    type="date"
                    value={values.effectiveFrom}
                    onChange={(value) => update('effectiveFrom', value)}
                    required
                    error={fieldError('effectiveFrom')}
                  />
                  <SelectField
                    id="changeReason"
                    label="Change type"
                    value={values.changeReason}
                    onChange={(value) => update('changeReason', value as EmploymentChangeReason)}
                    options={EMPLOYMENT_CHANGE_REASONS.map((reason) => ({ value: reason, label: reason }))}
                    error={fieldError('changeReason')}
                    required
                  />
                  <SelectField
                    id="positionChangeReasonId"
                    label="Change Reason (Optional)"
                    value={values.positionChangeReasonId}
                    onChange={(value) => update('positionChangeReasonId', value)}
                    options={asOptions(changeReasons.data?.options ?? [], values.positionChangeReasonId)}
                    placeholder="— Select a reason —"
                    error={fieldError('positionChangeReasonId')}
                  />
                </div>
              </EmploymentAccordion></div>
              <div hidden={step !== 1}><EmploymentAccordion title="2. Organization Structure" defaultOpen>
                <div className="form-grid">
                  <SelectField
                    id="holdingCompanyId"
                    label="Holding company"
                    value={values.holdingCompanyId}
                    onChange={(value) => update('holdingCompanyId', value)}
                    options={asOptions(holdingCompanies.data?.options ?? [], values.holdingCompanyId)}
                    placeholder="— Select —"
                    error={fieldError('holdingCompanyId')}
                  />
                  <SelectField
                    id="lobId"
                    label="Line of business"
                    value={values.lobId}
                    onChange={(value) => update('lobId', value)}
                    options={asOptions(lobs.data?.options ?? [], values.lobId)}
                    placeholder={values.holdingCompanyId ? '— Select —' : 'Choose a holding company first'}
                    disabled={!values.holdingCompanyId}
                    error={fieldError('lobId')}
                  />
                  <SelectField
                    id="organisationId"
                    label="Organisation"
                    value={values.organisationId}
                    onChange={(value) => update('organisationId', value)}
                    options={asOptions(organisations.data?.options ?? [], values.organisationId)}
                    placeholder="— Select —"
                    error={fieldError('organisationId')}
                  />
                  <SelectField
                    id="departmentId"
                    label="Department"
                    value={values.departmentId}
                    onChange={(value) => update('departmentId', value)}
                    options={asOptions(departments.data?.options ?? [], values.departmentId)}
                    placeholder="— Select —"
                    error={fieldError('departmentId')}
                  />
                  <SelectField
                    id="subDepartmentId"
                    label="Sub-department"
                    value={values.subDepartmentId}
                    onChange={(value) => update('subDepartmentId', value)}
                    options={asOptions(subDepartments.data?.options ?? [], values.subDepartmentId)}
                    placeholder={values.departmentId ? '— Select —' : 'Choose a department first'}
                    disabled={!values.departmentId}
                    error={fieldError('subDepartmentId')}
                  />
                  <SelectField
                    id="sectionId"
                    label="Section"
                    value={values.sectionId}
                    onChange={(value) => update('sectionId', value)}
                    options={asOptions(sections.data?.options ?? [], values.sectionId)}
                    placeholder={values.subDepartmentId ? '— Select —' : 'Choose a sub-department first'}
                    disabled={!values.subDepartmentId}
                    error={fieldError('sectionId')}
                  />
                  <SelectField
                    id="subSectionId"
                    label="Sub-section"
                    value={values.subSectionId}
                    onChange={(value) => update('subSectionId', value)}
                    options={asOptions(subSections.data?.options ?? [], values.subSectionId)}
                    placeholder={values.sectionId ? '— Select —' : 'Choose a section first'}
                    disabled={!values.sectionId}
                    error={fieldError('subSectionId')}
                  />
                  <SelectField
                    id="functionId"
                    label="Function"
                    value={values.functionId}
                    onChange={(value) => update('functionId', value)}
                    options={asOptions(functions.data?.options ?? [], values.functionId)}
                    placeholder="— Select —"
                    error={fieldError('functionId')}
                  />
                  <SelectField
                    id="subFunctionId"
                    label="Sub-function"
                    value={values.subFunctionId}
                    onChange={(value) => update('subFunctionId', value)}
                    options={asOptions(subFunctions.data?.options ?? [], values.subFunctionId)}
                    placeholder={values.functionId ? '— Select —' : 'Choose a function first'}
                    disabled={!values.functionId}
                    error={fieldError('subFunctionId')}
                  />
                </div>
              </EmploymentAccordion></div>
              <div hidden={step !== 2}><EmploymentAccordion title="3. Position Details" defaultOpen>
                <div className="form-grid">
                  <SelectField
                    id="gradeId"
                    label="Grade"
                    value={values.gradeId}
                    onChange={(value) => update('gradeId', value)}
                    options={asOptions(grades.data?.options ?? [], values.gradeId)}
                    placeholder="— Select —"
                    error={fieldError('gradeId')}
                  />
                  <SelectField
                    id="designationId"
                    label="Designation"
                    value={values.designationId}
                    onChange={(value) => update('designationId', value)}
                    options={asOptions(designations.data?.options ?? [], values.designationId)}
                    placeholder="— Select —"
                    error={fieldError('designationId')}
                  />
                  <SelectField
                    id="employeeTypeId"
                    label="Employee type"
                    value={values.employeeTypeId}
                    onChange={(value) => update('employeeTypeId', value)}
                    options={asOptions(employeeTypes.data?.options ?? [], values.employeeTypeId)}
                    placeholder="— Select —"
                    error={fieldError('employeeTypeId')}
                  />
                </div>
              </EmploymentAccordion></div>
              <div hidden={step !== 3}><EmploymentAccordion title="4. Location Details" defaultOpen>
                <div className="form-grid">
                  <SelectField
                    id="countryLocationId"
                    label="Location"
                    value={values.countryLocationId}
                    onChange={(value) => update('countryLocationId', value)}
                    options={asOptions(countries.data?.options ?? [], values.countryLocationId)}
                    placeholder="— Select a country —"
                    hint="A country used as the location."
                    error={fieldError('countryLocationId')}
                  />
                  <SelectField
                    id="workLocationId"
                    label="Work location"
                    value={values.workLocationId}
                    onChange={(value) => update('workLocationId', value)}
                    options={asOptions(workLocations.data?.options ?? [], values.workLocationId)}
                    placeholder="— Select —"
                    error={fieldError('workLocationId')}
                  />
                </div>
              </EmploymentAccordion></div>
                <div className="form-actions">
                {step > 0 && <button type="button" className="button button-secondary" onClick={() => setStep((currentStep) => currentStep - 1)} disabled={saving}>Back</button>}
                {step < 3 ? <button type="button" className="button button-primary" onClick={() => validateStep(step + 1)}>Next</button> : <button type="submit" className="button button-primary" aria-label="ADD EMPLOYMENT" disabled={saving}>{saving ? <Spinner size={14} label="Saving…" /> : 'Save Employment Change'}</button>}
                <button type="button" className="button button-secondary" onClick={reset} disabled={saving}>
                  Reset
                </button>
              </div>
            </form>
            </>}
        </Card>
      </div>
      <Notice tone="info">Employment history is maintained as an audit trail and cannot be deleted.</Notice>
    </>
  )
}

function isEffectiveToday(record: EmployeeEmploymentHistory): boolean {
  const today = new Date()
  const date = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
  return record.effectiveFrom <= date && (record.effectiveTo == null || record.effectiveTo >= date)
}

function employmentState(record: EmployeeEmploymentHistory, currentId?: string): 'CURRENT' | 'SCHEDULED' | 'HISTORICAL' {
  if (record.id === currentId) return 'CURRENT'
  const today = new Date()
  const date = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
  return record.effectiveFrom > date ? 'SCHEDULED' : 'HISTORICAL'
}

function Summary({ label, value }: { label: string; value?: string | null }) { return <div className="employment-summary-item"><span>{label}</span><strong>{value || '—'}</strong></div> }

export function managerDisplay(record: EmployeeEmploymentHistory): string {
  const code = record.managerEmployeeCode
  const name = record.managerFullName
  if (code && name) return `${code} — ${name}`
  return code ?? name ?? '—'
}
function EmploymentAccordion({ title, defaultOpen, children }: { title: string; defaultOpen?: boolean; children: ReactNode }) { return <details className="employment-accordion" open={defaultOpen}><summary>{title}</summary><div className="employment-accordion-body">{children}</div></details> }


/** Options with the selected (possibly inactive) reference kept selectable regardless of the active list. */
function asOptions(options: readonly SelectOption[], selected: string): SelectOption[] {
  if (!selected || selected === '') return [...options]
  return options.some((o) => o.value === selected) ? [...options] : [...options, { value: selected, label: selected }]
}
