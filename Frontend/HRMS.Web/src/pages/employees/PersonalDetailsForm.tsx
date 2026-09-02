import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import {
  createPersonalDetails,
  getEmployee,
  getEmployeeSensitiveDetails,
  updatePersonalDetails,
} from '../../api/employees.ts'
import { hasFieldErrors, toApiError, type ApiError } from '../../api/errors.ts'
import {
  BLOOD_GROUPS,
  GENDERS,
  JOB_STATUSES,
  MARITAL_STATUSES,
  TITLES,
  type BloodGroup,
  type Employee,
  type EmployeeSensitiveDetails,
  type Gender,
  type MaritalStatus,
} from '../../api/types.ts'
import { Card } from '../../components/Card.tsx'
import { ErrorState } from '../../components/ErrorState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { SearchableSelect, SelectField, TextField } from '../../components/fields.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import {
  EMPLOYEES_PATH,
  NEW_HIRE_LABEL,
  emptyPersonalDetailsValues,
  toPersonalDetailsRequest,
  toPersonalDetailsValues,
  type PersonalDetailsValues,
} from './personalDetailsValues.ts'
import {
  loadCityOptions,
  loadCountryOptions,
  loadStateOptions,
  truncationHint,
} from './referenceOptions.ts'

interface PersonalDetailsFormProps {
  /**
   * The employee being edited. Present → edit mode: the existing Personal Details are loaded, the employee
   * code is shown read-only, and the button is UPDATE. Absent → create mode: an empty form shows "New Hire"
   * and the button is SAVE; on success the same form switches to edit mode with the generated code.
   */
  employeeId?: string
  /**
   * Called with the new record's id the moment a create succeeds. Lets a wrapping page that hosts other
   * tabs (Contact/Address) know the employee now exists so those tabs can become active. Ignored when
   * editing an existing employee.
   */
  onCreated?: (id: string) => void
}

/**
 * The Employee → Personal Details section, reused for both creating and editing an employee.
 *
 * One component, two modes. There is no separate create/edit form and no wizard: every section is
 * independent and has its own SAVE/UPDATE button. After a create the form stays put, replaces "New Hire"
 * with the backend-generated code and flips to edit mode, so the user can keep working on the same record.
 */
export function PersonalDetailsForm({ employeeId, onCreated }: PersonalDetailsFormProps) {
  const { can } = useAuth()
  const canViewSensitive = can(Permissions.employeeSensitive.view)
  const canEditSensitive = can(Permissions.employeeSensitive.edit)
  // Editable identifiers. `employeeId` is the route (edit) or undefined (create); `currentId` becomes the
  // new record's id once a create succeeds, which is what flips the form into edit mode.
  const [currentId, setCurrentId] = useState<string | undefined>(employeeId)
  const [employeeCode, setEmployeeCode] = useState<string | undefined>(undefined)
  const [values, setValues] = useState<PersonalDetailsValues>(emptyPersonalDetailsValues())
  const [error, setError] = useState<ApiError | null>(null)
  const [success, setSuccess] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const initialSnapshot = useRef(JSON.stringify(emptyPersonalDetailsValues()))

  // On a direct edit route the record is fetched; there is no fetch on create (the create response is used).
  const query = useApiQuery<{ employee: Employee; sensitive: EmployeeSensitiveDetails | null } | null>(
    async (signal) => {
      if (!employeeId) return null
      const employee = await getEmployee(employeeId, signal)
      const sensitive = canViewSensitive
        ? await getEmployeeSensitiveDetails(employeeId, signal)
        : null
      return { employee, sensitive }
    },
    [employeeId, canViewSensitive],
  )

  // Populate the form from the fetched record once it arrives. Only runs for a direct edit; a create
  // populates from its own response and never reaches here.
  useEffect(() => {
    if (employeeId && query.data) {
      const loaded = toPersonalDetailsValues(query.data.employee, query.data.sensitive)
      setValues(loaded)
      initialSnapshot.current = JSON.stringify(loaded)
      setEmployeeCode(query.data.employee.employeeCode)
    }
  }, [employeeId, query.data])

  const isEdit = currentId !== undefined
  const displayedCode = isEdit ? employeeCode : undefined

  const countries = useApiQuery((signal) => loadCountryOptions({ activeOnly: true }, signal), [])
  const states = useApiQuery(
    (signal) =>
      values.birthCountryId
        ? loadStateOptions(values.birthCountryId, { activeOnly: true }, signal)
        : Promise.resolve({ options: [], total: 0 }),
    [values.birthCountryId],
  )
  const cities = useApiQuery(
    (signal) =>
      values.birthStateId
        ? loadCityOptions(values.birthStateId, { activeOnly: true }, signal)
        : Promise.resolve({ options: [], total: 0 }),
    [values.birthStateId],
  )

  function set<K extends keyof PersonalDetailsValues>(field: K, value: PersonalDetailsValues[K]): void {
    setValues((previous) => ({ ...previous, [field]: value }))
  }

  async function submit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (saving) return

    const body = toPersonalDetailsRequest(values)

    setSaving(true)
    setError(null)
    setSuccess(null)
    try {
      if (isEdit) {
        // In edit mode the update is applied to the record we already target and returns the fresh one.
        const updated = await updatePersonalDetails(currentId!, body)
        const loaded = toPersonalDetailsValues(updated, body)
        setValues(loaded)
        initialSnapshot.current = JSON.stringify(loaded)
        setSuccess(`Personal details updated successfully for ${updated.fullName}.`)
      } else {
        const created = await createPersonalDetails(body)
        // Create → the same form becomes an edit form: code replaces "New Hire", SAVE becomes UPDATE.
        setCurrentId(created.id)
        onCreated?.(created.id)
        setEmployeeCode(created.employeeCode)
        const loaded = toPersonalDetailsValues(created, body)
        setValues(loaded)
        initialSnapshot.current = JSON.stringify(loaded)
        setSuccess(
          `Employee created. Employee code ${created.employeeCode} has been assigned.`,
        )
      }
    } catch (caught) {
      setError(toApiError(caught))
    } finally {
      setSaving(false)
    }
  }

  const fieldError = (field: string) => error?.fieldErrors[field]
  const hasUnsavedChanges = JSON.stringify(values) !== initialSnapshot.current

  // Citizenship is rendered as a country dropdown but stored as the country name, so the options are
  // keyed by name. The record's current value is always offered so an edit never shows a blank picker.
  const citizenshipValue = values.citizenship.trim()
  const citizenshipOptions = (() => {
    const seen = new Set<string>()
    const options = (countries.data?.options ?? []).map((option) => ({ value: option.label, label: option.label }))
    for (const option of options) seen.add(option.label)
    if (citizenshipValue && !seen.has(citizenshipValue)) {
      options.unshift({ value: citizenshipValue, label: `${citizenshipValue} (current)` })
    }
    return options
  })()

  if (employeeId && !query.data && query.isLoading) {
    return (
      <Card className="form-card">
        <div className="table-loading">
          <Spinner label="Loading employee\u2026" />
        </div>
      </Card>
    )
  }

  if (employeeId && query.error && !query.data) {
    return (
      <Card className="form-card">
        <ErrorState error={query.error} onRetry={query.refetch} />
      </Card>
    )
  }

  return (
    <Card className="form-card employee-detail-form-card employee-personal-form-card">
      {error && !hasFieldErrors(error) && <Notice tone="error">{error.message}</Notice>}
      {success && <div className="employee-alert" role="status"><span aria-hidden="true">✓</span> {success}</div>}

      <form onSubmit={submit} noValidate>
        <fieldset className="form-section">
          <legend>Basic Information</legend>
          <div className="employee-subsection-title">Birth &amp; Citizenship<span>Birth country, state, city and citizenship</span></div>
          <div className="form-grid">
            <SelectField
              id="salutation"
              label="Title"
              value={values.salutation}
              onChange={(value) => set('salutation', value)}
              options={[{ value: '', label: '— Select —' }, ...TITLES.map((t) => ({ value: t, label: t }))]}
              hint="Optional."
            />
            <TextField
              id="firstName"
              label="First name"
              value={values.firstName}
              onChange={(value) => set('firstName', value)}
              maxLength={100}
              required
              autoComplete="given-name"
              error={fieldError('firstName')}
            />
            <TextField
              id="middleName"
              label="Middle name"
              value={values.middleName}
              onChange={(value) => set('middleName', value)}
              maxLength={100}
              autoComplete="additional-name"
              hint="Optional."
            />
            <TextField
              id="lastName"
              label="Last name"
              value={values.lastName}
              onChange={(value) => set('lastName', value)}
              maxLength={100}
              required
              autoComplete="family-name"
              error={fieldError('lastName')}
            />
            <TextField
              id="dateOfBirth"
              label="Date of birth"
              type="date"
              value={values.dateOfBirth}
              onChange={(value) => set('dateOfBirth', value)}
              min={yearsFromToday(-120)}
              max={yearsFromToday(0)}
              error={fieldError('dateOfBirth')}
              hint="Optional."
            />
            <SelectField
              id="gender"
              label="Gender"
              value={values.gender}
              onChange={(value) => set('gender', asGender(value))}
              options={GENDERS.filter((g) => g !== 'Unspecified').map((gender) => ({ value: gender, label: gender }))}
              error={fieldError('gender')}
            />
            <SelectField
              id="bloodGroup"
              label="Blood group"
              value={values.bloodGroup}
              onChange={(value) => set('bloodGroup', value as BloodGroup)}
              options={BLOOD_GROUPS.map((bg) => ({ value: bg, label: bg }))}
              hint="Optional."
            />
            <SelectField
              id="maritalStatus"
              label="Marital status"
              value={values.maritalStatus}
              onChange={(value) => set('maritalStatus', value as MaritalStatus)}
              options={MARITAL_STATUSES.filter((ms) => ms !== 'Unspecified').map((ms) => ({ value: ms, label: ms }))}
              error={fieldError('maritalStatus')}
            />
          </div>
          <div className="employee-subsection-title">Demographics<span>Additional demographic information</span></div>
          <div className="form-grid">
            <SearchableSelect
              id="birthCountryId"
              label="Birth country"
              value={values.birthCountryId}
              onChange={(value) => {
                set('birthCountryId', value)
                set('birthStateId', '')
                set('birthCityId', '')
              }}
              options={[{ value: '', label: '— None —' }, ...(countries.data?.options ?? [])]}
              placeholder="Search countries…"
              loading={countries.isLoading}
              hint={truncationHint(countries.data?.options.length ?? 0, countries.data?.total ?? 0, 'countries')}
            />
            <SearchableSelect
              id="birthStateId"
              label="Birth state"
              value={values.birthStateId}
              onChange={(value) => {
                set('birthStateId', value)
                set('birthCityId', '')
              }}
              options={[{ value: '', label: '— None —' }, ...(states.data?.options ?? [])]}
              placeholder={values.birthCountryId ? 'Search states…' : 'Select a country first'}
              disabled={!values.birthCountryId}
              loading={states.isLoading}
              hint={truncationHint(states.data?.options.length ?? 0, states.data?.total ?? 0, 'states')}
            />
            <SearchableSelect
              id="birthCityId"
              label="Birth city"
              value={values.birthCityId}
              onChange={(value) => set('birthCityId', value)}
              options={[{ value: '', label: '— None —' }, ...(cities.data?.options ?? [])]}
              placeholder={values.birthStateId ? 'Search cities…' : 'Select a state first'}
              disabled={!values.birthStateId}
              loading={cities.isLoading}
              hint={truncationHint(cities.data?.options.length ?? 0, cities.data?.total ?? 0, 'cities')}
            />
            <SearchableSelect
              id="citizenship"
              label="Citizenship country"
              value={citizenshipValue}
              onChange={(value) => set('citizenship', value)}
              options={[{ value: '', label: '— None —' }, ...citizenshipOptions]}
              placeholder="Search countries…"
              loading={countries.isLoading}
              hint="Stored as the country name."
            />
          </div>
          <div className="form-grid">
            <TextField
              id="religion"
              label="Religion"
              value={values.religion}
              onChange={(value) => set('religion', value)}
              maxLength={100}
              hint="Optional."
            />
            <TextField
              id="caste"
              label="Caste"
              value={values.caste}
              onChange={(value) => set('caste', value)}
              maxLength={100}
              hint="Optional."
            />
          </div>
        </fieldset>

        <fieldset className="form-section">
          <legend>Government & Identification</legend>
          <Notice tone="info">
            Sensitive identification information. These fields are masked in reports.
          </Notice>
          {!canViewSensitive && currentId && (
            <Notice tone="info">Sensitive values are hidden because you do not have sensitive-data access.</Notice>
          )}
          <div className="form-grid">
            <TextField
              id="aadhaarNumber"
              label="Aadhaar Number"
              value={values.aadhaarNumber}
              onChange={(value) => set('aadhaarNumber', value)}
              maxLength={12}
              error={fieldError('aadhaarNumber')}
              disabled={!canEditSensitive}
              hint="12 digits. Optional. Left blank when already recorded."
            />
            <TextField
              id="panNumber"
              label="PAN Number"
              value={values.panNumber}
              onChange={(value) => set('panNumber', value.toUpperCase())}
              maxLength={10}
              error={fieldError('panNumber')}
              disabled={!canEditSensitive}
              hint="Format: ABCDE1234F. Optional."
            />
            <TextField
              id="uanNumber"
              label="UAN Number"
              value={values.uanNumber}
              onChange={(value) => set('uanNumber', value)}
              maxLength={12}
              error={fieldError('uanNumber')}
              disabled={!canEditSensitive}
              hint="12 digits. Optional."
            />
          </div>
        </fieldset>

        <fieldset className="form-section">
          <legend>Statutory & Benefits</legend>
          <div className="form-grid">
            <SelectField
              id="esicApplicable"
              label="ESIC applicable"
              value={values.esicApplicable ? 'Yes' : 'No'}
              onChange={(value) => set('esicApplicable', value === 'Yes')}
              options={[{ value: 'No', label: 'No' }, { value: 'Yes', label: 'Yes' }]}
              disabled={!canEditSensitive}
            />
            <SelectField
              id="gratuity"
              label="Gratuity applicable"
              value={values.gratuity ? 'Yes' : 'No'}
              onChange={(value) => set('gratuity', value === 'Yes')}
              options={[{ value: 'No', label: 'No' }, { value: 'Yes', label: 'Yes' }]}
            />
            <SelectField
              id="pension"
              label="Pension applicable"
              value={values.pension ? 'Yes' : 'No'}
              onChange={(value) => set('pension', value === 'Yes')}
              options={[{ value: 'No', label: 'No' }, { value: 'Yes', label: 'Yes' }]}
            />
          </div>
          {values.esicApplicable && (
            <div className="form-grid">
              <TextField
                id="esicNumber"
                label="ESIC Number"
                value={values.esicNumber}
                onChange={(value) => set('esicNumber', value)}
                maxLength={50}
                required
                error={fieldError('esicNumber')}
                disabled={!canEditSensitive}
                hint="Required when ESIC is applicable."
              />
            </div>
          )}
          <div className="form-grid">
            <TextField
              id="pfNumber"
              label="PF Number"
              value={values.pfNumber}
              onChange={(value) => set('pfNumber', value)}
              maxLength={50}
              error={fieldError('pfNumber')}
              disabled={!canEditSensitive}
              hint="Optional. Left blank when already recorded."
            />
            <TextField
              id="mediclaimNumber"
              label="Mediclaim Number"
              value={values.mediclaimNumber}
              onChange={(value) => set('mediclaimNumber', value)}
              maxLength={50}
              disabled={!canEditSensitive}
              hint="Optional."
            />
          </div>
        </fieldset>

        <fieldset className="form-section">
          <legend>Joining</legend>
          <div className="form-grid">
            <TextField
              id="dateOfJoining"
              label="Date of joining"
              type="date"
              value={values.dateOfJoining}
              onChange={(value) => set('dateOfJoining', value)}
              required
              min={yearsFromToday(-100)}
              max={yearsFromToday(1)}
              error={fieldError('dateOfJoining')}
            />
            <SelectField
              id="jobStatus"
              label="Job status"
              value={values.jobStatus}
              onChange={(value) => set('jobStatus', value)}
              options={[{ value: '', label: '— Select —' }, ...JOB_STATUSES.map((s) => ({ value: s, label: s }))]}
              hint="Optional."
            />
          </div>
        </fieldset>

        <div className="employee-code-banner" aria-live="polite">
          <span className="employee-code-label">Employee code</span>
          <span className="employee-code-value">
            {displayedCode ? displayedCode : NEW_HIRE_LABEL}
          </span>
          {!displayedCode && (
            <span className="employee-code-hint">Assigned by the system when this employee is saved.</span>
          )}
        </div>

        <div className="form-actions employee-form-actions">
          <Link className="button button-secondary" to={EMPLOYEES_PATH}>
            Cancel
          </Link>
          {hasUnsavedChanges && <span className="unsaved-indicator" role="status"><span aria-hidden="true">●</span> You have unsaved changes</span>}
          <button type="submit" className="button button-primary" disabled={saving}>
            {saving ? <Spinner size={14} label="Saving\u2026" /> : isEdit ? 'UPDATE' : 'SAVE'}
          </button>
        </div>
      </form>
    </Card>
  )
}

function yearsFromToday(offset: number): string {
  const date = new Date()
  date.setFullYear(date.getFullYear() + offset)
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}

function asGender(value: string): Gender {
  const match = GENDERS.find((gender) => gender === value)
  return match ?? 'Unspecified'
}
