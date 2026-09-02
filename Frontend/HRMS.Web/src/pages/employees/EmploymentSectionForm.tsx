import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import {
  createEmploymentChange,
  getEmployment,
  getEmploymentHistory,
  upsertEmployment,
} from '../../api/employeeSubsections.ts'
import { toApiError, hasFieldErrors, type ApiError } from '../../api/errors.ts'
import type {
  EmployeeEmployment,
  EmployeeEmploymentHistory,
  EmployeeEmploymentRequest,
  EmploymentChangeRequest,
  EmploymentType,
  EmployeeStatus,
} from '../../api/types.ts'
import { EMPLOYEE_STATUSES, EMPLOYMENT_TYPES } from '../../api/types.ts'
import { Card } from '../../components/Card.tsx'
import { SelectField, TextField, type SelectOption } from '../../components/fields.tsx'
import { ErrorState } from '../../components/ErrorState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { formatDate } from '../../lib/format.ts'
import {
  loadCountryOptions,
  loadCostCenterOptions,
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
}

interface FormValues {
  effectiveFrom: string
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
  effectiveFrom: '',
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
    effectiveFrom: values.effectiveFrom,
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

function formValuesFromHistory(record: EmployeeEmploymentHistory): FormValues {
  return {
    effectiveFrom: record.effectiveFrom,
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
export function EmploymentSectionForm({ employeeId }: EmploymentSectionFormProps) {
  const [values, setValues] = useState<FormValues>(EMPTY)
  const [error, setError] = useState<ApiError | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const hydratedEmployeeId = useRef<string | null>(null)

  const history = useApiQuery((signal) => getEmploymentHistory(employeeId, signal), [employeeId])

  useEffect(() => {
    const current = history.data?.find((record) => !record.effectiveTo) ?? history.data?.[0]
    if (!current || hydratedEmployeeId.current === employeeId) return
    setValues(formValuesFromHistory(current))
    hydratedEmployeeId.current = employeeId
  }, [employeeId, history.data])

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
  const costCenters = useApiQuery((signal) => loadCostCenterOptions({ activeOnly: true }, signal), [])
  const changeReasons = useApiQuery(
    (signal) => loadPositionChangeReasonOptions({ activeOnly: true }, signal),
    [],
  )

  const fieldError = (field: string) => error?.fieldErrors[field]

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
      setValues(formValuesFromHistory(saved))
      setSuccess('Employment change recorded.')
      await history.refetch()
    } catch (caught) {
      setError(toApiError(caught))
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
  const current = records.find((record) => !record.effectiveTo) ?? records[0]
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
            <div className="employment-card-heading"><div><p className="eyebrow">Current Employment</p><h2>{display(current?.designationName)}</h2></div><span className="employment-status-badge current">CURRENT</span></div>
            {current ? <div className="employment-summary-grid">
              <Summary label="Department" value={current.departmentName} />
              <Summary label="Grade" value={current.gradeName} />
              <Summary label="Work Location" value={current.workLocationName} />
              <Summary label="Effective From" value={formatDate(current.effectiveFrom)} />
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
                <div className="timeline-content"><div className="timeline-meta"><span>{record.id === current?.id ? 'CURRENT' : 'PREVIOUS'}</span><time>{period(record)}<span className="sr-only"> {record.effectiveFrom}</span></time></div>
                  <div className="timeline-title-row"><h3>{display(record.designationName)}</h3><span className="change-reason-badge">{display(record.positionChangeReasonName)}</span></div>
                  <p className="timeline-subtitle">{display(record.departmentName)} · {display(record.organisationName)}</p>
                  <dl className="timeline-details"><div><dt>Grade</dt><dd>{display(record.gradeName)}</dd></div><div><dt>Location</dt><dd>{display(record.workLocationName)}</dd></div><div><dt>Effective</dt><dd>{period(record)}</dd></div></dl>
                </div>
              </article>)}
            </div> : <Notice tone="info">No employment history recorded yet. Add the first entry below.</Notice>}
          </Card>
        </div>

        <Card className="employment-card change-panel">
          <div className="employment-card-heading"><div><p className="eyebrow">Employment</p><h2>Add Employment Change</h2></div></div>
            <form className="employment-editor" onSubmit={saveEmploymentChange} noValidate>
              <EmploymentAccordion title="1. Employment Details" defaultOpen>
                <div className="form-grid">
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
                    id="positionChangeReasonId"
                    label="Change reason"
                    value={values.positionChangeReasonId}
                    onChange={(value) => update('positionChangeReasonId', value)}
                    options={asOptions(changeReasons.data?.options ?? [], values.positionChangeReasonId)}
                    placeholder="— Select a reason —"
                    required
                    error={fieldError('positionChangeReasonId')}
                  />
                </div>
              </EmploymentAccordion>
              <EmploymentAccordion title="2. Organization Structure">
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
              </EmploymentAccordion>
              <EmploymentAccordion title="3. Position Details">
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
              </EmploymentAccordion>
              <EmploymentAccordion title="4. Location Details">
                <div className="form-grid">
                  <SelectField
                    id="countryLocationId"
                    label="Country"
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
              </EmploymentAccordion>
              <EmploymentAccordion title="5. Additional Details">
                <div className="form-grid">
                  <SelectField
                    id="costCenterId"
                    label="Cost center"
                    value={values.costCenterId}
                    onChange={(value) => update('costCenterId', value)}
                    options={asOptions(costCenters.data?.options ?? [], values.costCenterId)}
                    placeholder="— Select —"
                    error={fieldError('costCenterId')}
                  />
                  <SelectField
                    id="employmentType"
                    label="Employment type"
                    value={values.employmentType}
                    onChange={(value) => update('employmentType', value as EmploymentType)}
                    options={EMPLOYMENT_TYPES.map((t) => ({ value: t, label: t }))}
                    error={fieldError('employmentType')}
                  />
                  <SelectField
                    id="employmentStatus"
                    label="Employment status"
                    value={values.employmentStatus}
                    onChange={(value) => update('employmentStatus', value as EmployeeStatus)}
                    options={EMPLOYEE_STATUSES.map((s) => ({ value: s, label: s }))}
                    error={fieldError('employmentStatus')}
                  />
                  <TextField
                    id="businessRole"
                    label="Business role"
                    value={values.businessRole}
                    onChange={(value) => update('businessRole', value)}
                    maxLength={100}
                    hint="Optional."
                    error={fieldError('businessRole')}
                  />
                  <TextField
                    id="gradeLevel"
                    label="Grade level"
                    value={values.gradeLevel}
                    onChange={(value) => update('gradeLevel', value)}
                    maxLength={50}
                    hint="Optional."
                    error={fieldError('gradeLevel')}
                  />
                  <TextField
                    id="careerGroup"
                    label="Career group"
                    value={values.careerGroup}
                    onChange={(value) => update('careerGroup', value)}
                    maxLength={100}
                    hint="Optional."
                    error={fieldError('careerGroup')}
                  />
                </div>
              </EmploymentAccordion>

                <div className="form-actions">
                <button type="submit" className="button button-primary" aria-label="ADD EMPLOYMENT" disabled={saving}>
                  {saving ? <Spinner size={14} label="Saving…" /> : 'Save Employment Change'}
                </button>
                <button type="button" className="button button-secondary" onClick={reset} disabled={saving}>
                  Reset
                </button>
              </div>
            </form>
        </Card>
      </div>
      <JoiningEmploymentForm employeeId={employeeId} />
      <Notice tone="info">Employment history is maintained as an audit trail and cannot be deleted.</Notice>
    </>
  )
}

function Summary({ label, value }: { label: string; value?: string | null }) { return <div className="employment-summary-item"><span>{label}</span><strong>{value || '—'}</strong></div> }
function EmploymentAccordion({ title, defaultOpen, children }: { title: string; defaultOpen?: boolean; children: ReactNode }) { return <details className="employment-accordion" open={defaultOpen}><summary>{title}</summary><div className="employment-accordion-body">{children}</div></details> }

interface JoiningValues {
  firstHiredDate: string
  dateOfJoining: string
  groupDateOfJoining: string
  confirmationDate: string
  jobStatus: string
  probationPeriod: string
  probationPeriodUnit: string
  referredByEmployeeId: string
  noticePeriod: string
  noticePeriodUnit: string
}

const EMPTY_JOINING: JoiningValues = {
  firstHiredDate: '',
  dateOfJoining: '',
  groupDateOfJoining: '',
  confirmationDate: '',
  jobStatus: '',
  probationPeriod: '',
  probationPeriodUnit: '',
  referredByEmployeeId: '',
  noticePeriod: '',
  noticePeriodUnit: '',
}

function joiningValuesFromEmployment(employment: EmployeeEmployment): JoiningValues {
  return {
    firstHiredDate: employment.firstHiredDate ?? '',
    dateOfJoining: employment.dateOfJoining ?? '',
    groupDateOfJoining: employment.groupDateOfJoining ?? '',
    confirmationDate: employment.confirmationDate ?? '',
    jobStatus: employment.jobStatus ?? '',
    probationPeriod: employment.probationPeriod?.toString() ?? '',
    probationPeriodUnit: employment.probationPeriodUnit ?? '',
    referredByEmployeeId: employment.referredByEmployeeId ?? '',
    noticePeriod: employment.noticePeriod?.toString() ?? '',
    noticePeriodUnit: employment.noticePeriodUnit ?? '',
  }
}

function joiningRequestFromValues(values: JoiningValues): EmployeeEmploymentRequest {
  const numberOrNull = (value: string): number | null => {
    const trimmed = value.trim()
    if (!trimmed) return null
    const parsed = Number(trimmed)
    return Number.isFinite(parsed) ? parsed : null
  }

  return {
    firstHiredDate: values.firstHiredDate,
    dateOfJoining: values.dateOfJoining,
    groupDateOfJoining: values.groupDateOfJoining || null,
    confirmationDate: values.confirmationDate || null,
    jobStatus: values.jobStatus.trim() || null,
    probationPeriod: numberOrNull(values.probationPeriod),
    probationPeriodUnit: values.probationPeriodUnit || null,
    referredByEmployeeId: values.referredByEmployeeId.trim() || null,
    noticePeriod: numberOrNull(values.noticePeriod),
    noticePeriodUnit: values.noticePeriodUnit || null,
  }
}

/** Joining and contractual terms are an independent saveable section of the Employee page. */
function JoiningEmploymentForm({ employeeId }: EmploymentSectionFormProps) {
  const [values, setValues] = useState<JoiningValues>(EMPTY_JOINING)
  const [error, setError] = useState<ApiError | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const employment = useApiQuery((signal) => getEmployment(employeeId, signal), [employeeId])

  useEffect(() => {
    if (employment.data) setValues(joiningValuesFromEmployment(employment.data))
  }, [employment.data])

  function update<F extends keyof JoiningValues>(field: F, value: JoiningValues[F]): void {
    setError(null)
    setValues((current) => ({ ...current, [field]: value }))
  }

  async function saveContractualEmployment(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (saving) return

    setSaving(true)
    setError(null)
    setSuccess(null)
    try {
      const saved = await upsertEmployment(employeeId, joiningRequestFromValues(values))
      setValues(joiningValuesFromEmployment(saved))
      setSuccess('Joining and contractual employment details saved.')
      employment.refetch()
    } catch (caught) {
      setError(toApiError(caught))
    } finally {
      setSaving(false)
    }
  }

  const fieldError = (field: string) => error?.fieldErrors[field]
  const noEmploymentYet = employment.error?.status === 404

  if (!employment.data && !noEmploymentYet && employment.isLoading) {
    return (
      <Card className="form-card">
        <div className="table-loading">
          <Spinner label="Loading joining details…" />
        </div>
      </Card>
    )
  }

  if (!employment.data && !noEmploymentYet && employment.error) {
    return (
      <Card className="form-card">
        <ErrorState error={employment.error} onRetry={employment.refetch} />
      </Card>
    )
  }

  return (
    <Card className="form-card">
      {error && !hasFieldErrors(error) && <Notice tone="error">{error.message}</Notice>}
      {success && <Notice tone="success">{success}</Notice>}
      <form onSubmit={saveContractualEmployment} noValidate>
        <fieldset className="form-section">
          <legend>Joining &amp; Contractual Employment</legend>
          <div className="form-grid">
            <TextField
              id="firstHiredDate"
              label="First hired date"
              type="date"
              value={values.firstHiredDate}
              onChange={(value) => update('firstHiredDate', value)}
              required
              error={fieldError('firstHiredDate')}
            />
            <TextField
              id="dateOfJoining"
              label="Date of joining"
              type="date"
              value={values.dateOfJoining}
              onChange={(value) => update('dateOfJoining', value)}
              required
              error={fieldError('dateOfJoining')}
            />
            <TextField
              id="groupDateOfJoining"
              label="Group date of joining"
              type="date"
              value={values.groupDateOfJoining}
              onChange={(value) => update('groupDateOfJoining', value)}
              hint="Optional."
              error={fieldError('groupDateOfJoining')}
            />
            <TextField
              id="confirmationDate"
              label="Confirmation date"
              type="date"
              value={values.confirmationDate}
              onChange={(value) => update('confirmationDate', value)}
              hint="Optional."
              error={fieldError('confirmationDate')}
            />
            <TextField
              id="jobStatus"
              label="Job status"
              value={values.jobStatus}
              onChange={(value) => update('jobStatus', value)}
              maxLength={100}
              hint="Optional."
              error={fieldError('jobStatus')}
            />
            <TextField
              id="probationPeriod"
              label="Probation period"
              type="number"
              value={values.probationPeriod}
              onChange={(value) => update('probationPeriod', value)}
              min="1"
              hint="Optional."
              error={fieldError('probationPeriod')}
            />
            <SelectField
              id="probationPeriodUnit"
              label="Probation unit"
              value={values.probationPeriodUnit}
              onChange={(value) => update('probationPeriodUnit', value)}
              options={[
                { value: 'Days', label: 'Days' },
                { value: 'Months', label: 'Months' },
                { value: 'Years', label: 'Years' },
              ]}
              placeholder="— Select —"
              error={fieldError('probationPeriodUnit')}
            />
            <TextField
              id="noticePeriod"
              label="Notice period"
              type="number"
              value={values.noticePeriod}
              onChange={(value) => update('noticePeriod', value)}
              min="1"
              hint="Optional."
              error={fieldError('noticePeriod')}
            />
            <SelectField
              id="noticePeriodUnit"
              label="Notice unit"
              value={values.noticePeriodUnit}
              onChange={(value) => update('noticePeriodUnit', value)}
              options={[
                { value: 'Days', label: 'Days' },
                { value: 'Months', label: 'Months' },
              ]}
              placeholder="— Select —"
              error={fieldError('noticePeriodUnit')}
            />
            <TextField
              id="referredByEmployeeId"
              label="Referred by employee ID"
              value={values.referredByEmployeeId}
              onChange={(value) => update('referredByEmployeeId', value)}
              maxLength={36}
              hint="Optional employee identifier."
              error={fieldError('referredByEmployeeId')}
            />
          </div>
        </fieldset>
        <div className="form-actions">
          <button type="submit" className="button button-primary" disabled={saving}>
            {saving ? <Spinner size={14} label="Saving…" /> : 'Save Joining Details'}
          </button>
        </div>
      </form>
    </Card>
  )
}

/** Options with the selected (possibly inactive) reference kept selectable regardless of the active list. */
function asOptions(options: readonly SelectOption[], selected: string): SelectOption[] {
  if (!selected || selected === '') return [...options]
  return options.some((o) => o.value === selected) ? [...options] : [...options, { value: selected, label: selected }]
}
