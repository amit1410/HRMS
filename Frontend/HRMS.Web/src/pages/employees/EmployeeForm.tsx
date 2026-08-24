import { useState, type FormEvent } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { hasFieldErrors, toApiError, type ApiError } from '../../api/errors.ts'
import {
  EMPLOYEE_STATUSES,
  GENDERS,
  type Employee,
  type EmployeeRequest,
  type EmployeeStatus,
  type Gender,
} from '../../api/types.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { SelectField, TextAreaField, TextField } from '../../components/fields.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import type { FlashState } from '../../hooks/useFlash.ts'
import { returnPath } from '../../lib/returnTo.ts'
import {
  EMPLOYEES_PATH,
  type CurrentReferences,
  type EmployeeFormValues,
} from './employeeValues.ts'
import { ManagerField } from './ManagerField.tsx'
import { loadDepartmentOptions, loadDesignationOptions, truncationHint, withCurrent } from './referenceOptions.ts'

/**
 * What an unanswered required field is sent as.
 *
 * `DateOfJoining`, `DepartmentId` and `DesignationId` are non-nullable in C# (`DateOnly`, `Guid`), so an
 * empty string for any of them is not a validation failure — it is a JSON deserialization failure, and
 * those come back from `InvalidModelStateResponseFactory` as "The request could not be read." against the
 * field name `$.dateOfJoining`, which matches no input on this form. The user would get a banner naming
 * nothing.
 *
 * Sending the CLR default instead reaches FluentValidation, which has a rule for exactly this case —
 * `NotEqual(default(DateOnly))` → "Date of joining is required.", `NotEmpty()` on the two ids → "Department
 * is required." Those arrive as ordinary field errors and render under the input that is missing.
 *
 * So the form still does no validation of its own; it just spells "empty" in the way the API's own required
 * rules are written to catch.
 */
const EMPTY_DATE = '0001-01-01'
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

interface EmployeeFormProps {
  initial: EmployeeFormValues
  current: CurrentReferences
  /** The record being edited, or `undefined` when creating. Kept out of the manager candidates. */
  employeeId?: string
  submitLabel: string
  onSubmit: (body: EmployeeRequest) => Promise<Employee>
  successMessage: (employee: Employee) => string
}

/**
 * The employee create/edit form.
 *
 * Fourteen fields, grouped into fieldsets rather than run together, because "which of these is the
 * employment record and which is the person" is a question the layout should answer without being asked.
 *
 * As everywhere else in this app there is no client-side validation. `EmployeeRequestValidator` holds
 * fifteen rules including four cross-field ones — a leaving date that must be absent for an active
 * employee and present for anyone else, an age checked against the joining date rather than today — and the
 * service adds the ones that need stored data: that the code is unique in this tenant, that the department
 * exists in it, that the manager is not the employee themselves. A copy of the first half here would drift
 * from the original and could not check the second half at all. So the form submits, and each message in
 * `errors[]` renders under the input it names.
 *
 * Two things the form does decide, because they are about what the user meant rather than whether it is
 * allowed:
 *
 * - **A leaving date only exists for someone who has left.** The input appears when the status is not
 *   Active, and switching back to Active clears the value — otherwise a date typed by mistake would sit in
 *   hidden state and be rejected by a rule the user cannot see the field for.
 * - **Empty optional text is `null`, not `''`.** The write is a full replacement, so clearing the phone box
 *   has to clear the stored number rather than store a blank string.
 */
export function EmployeeForm({
  initial,
  current,
  employeeId,
  submitLabel,
  onSubmit,
  successMessage,
}: EmployeeFormProps) {
  const navigate = useNavigate()
  const location = useLocation()

  const [values, setValues] = useState<EmployeeFormValues>(initial)
  const [error, setError] = useState<ApiError | null>(null)
  const [saving, setSaving] = useState(false)

  const returnTo = returnPath(location.state, EMPLOYEES_PATH)

  // Active only: assigning someone to a retired unit or job title is what the API refuses. The record's own
  // reference is added back below, because for an existing employee it may legitimately be an inactive one.
  const departments = useApiQuery((signal) => loadDepartmentOptions({ activeOnly: true }, signal), [])
  const designations = useApiQuery(
    (signal) => loadDesignationOptions({ activeOnly: true }, signal),
    [],
  )

  function set<K extends keyof EmployeeFormValues>(field: K, value: EmployeeFormValues[K]): void {
    setValues((previous) => ({ ...previous, [field]: value }))
  }

  function setStatus(next: EmployeeStatus): void {
    setValues((previous) => ({
      ...previous,
      status: next,
      // Back to Active means there is no leaving date, and the input that held it is about to disappear.
      dateOfLeaving: next === 'Active' ? '' : previous.dateOfLeaving,
    }))
  }

  async function submit(event: FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault()
    if (saving) return

    setSaving(true)
    setError(null)
    try {
      const employee = await onSubmit({
        employeeCode: values.employeeCode.trim(),
        firstName: values.firstName.trim(),
        lastName: values.lastName.trim(),
        email: values.email.trim(),
        phone: values.phone.trim() || null,
        dateOfBirth: values.dateOfBirth || null,
        gender: values.gender,
        dateOfJoining: values.dateOfJoining || EMPTY_DATE,
        // Forced, not merely hidden: the rule is that an active employee has no leaving date at all.
        dateOfLeaving: values.status === 'Active' ? null : values.dateOfLeaving || null,
        status: values.status,
        departmentId: values.departmentId || EMPTY_GUID,
        designationId: values.designationId || EMPTY_GUID,
        reportingManagerId: values.reportingManagerId || null,
        address: values.address.trim() || null,
      })
      const state: FlashState = { flash: successMessage(employee) }
      navigate(returnTo, { replace: true, state })
    } catch (caught) {
      setError(toApiError(caught))
    } finally {
      setSaving(false)
    }
  }

  const fieldError = (field: string) => error?.fieldErrors[field]

  const departmentOptions = withCurrent(departments.data?.options ?? [], current.department)
  const designationOptions = withCurrent(designations.data?.options ?? [], current.designation)

  return (
    <Card className="form-card">
      {error && !hasFieldErrors(error) && <Notice tone="error">{error.message}</Notice>}

      <form onSubmit={submit} noValidate>
        <fieldset className="form-section">
          <legend>Identity</legend>
          <div className="form-grid">
            <TextField
              id="employeeCode"
              label="Employee code"
              value={values.employeeCode}
              onChange={(value) => set('employeeCode', value)}
              maxLength={20}
              required
              autoCapitalize="characters"
              error={fieldError('employeeCode')}
              hint="Your organization's own identifier for this employee. Unique within your organization."
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
              id="lastName"
              label="Last name"
              value={values.lastName}
              onChange={(value) => set('lastName', value)}
              maxLength={100}
              required
              autoComplete="family-name"
              error={fieldError('lastName')}
            />
          </div>
        </fieldset>

        <fieldset className="form-section">
          <legend>Contact</legend>
          <div className="form-grid">
            <TextField
              id="email"
              label="Email"
              type="email"
              value={values.email}
              onChange={(value) => set('email', value)}
              maxLength={256}
              required
              autoComplete="email"
              error={fieldError('email')}
            />
            <TextField
              id="phone"
              label="Phone"
              type="tel"
              value={values.phone}
              onChange={(value) => set('phone', value)}
              maxLength={30}
              autoComplete="tel"
              error={fieldError('phone')}
              hint="Optional. Digits and + - ( ) . and spaces."
            />
          </div>
          <TextAreaField
            id="address"
            label="Address"
            value={values.address}
            onChange={(value) => set('address', value)}
            maxLength={500}
            rows={3}
            error={fieldError('address')}
            hint="Optional. Up to 500 characters."
          />
        </fieldset>

        <fieldset className="form-section">
          <legend>Role</legend>
          <div className="form-grid">
            <SelectField
              id="departmentId"
              label="Department"
              value={values.departmentId}
              onChange={(value) => set('departmentId', value)}
              options={departmentOptions}
              placeholder="Select a department"
              required
              // A list that failed to load leaves the field unanswerable, which is worth saying in the same
              // place a rejected value would be said. A message from the API about this field wins.
              error={fieldError('departmentId') ?? departments.error?.message}
              hint={truncationHint(
                departments.data?.options.length ?? 0,
                departments.data?.total ?? 0,
                'departments',
              )}
            />
            <SelectField
              id="designationId"
              label="Designation"
              value={values.designationId}
              onChange={(value) => set('designationId', value)}
              options={designationOptions}
              placeholder="Select a job title"
              required
              error={fieldError('designationId') ?? designations.error?.message}
              hint={truncationHint(
                designations.data?.options.length ?? 0,
                designations.data?.total ?? 0,
                'job titles',
              )}
            />
          </div>
          <ManagerField
            value={values.reportingManagerId}
            onChange={(value) => set('reportingManagerId', value)}
            excludeId={employeeId}
            current={current.manager}
            error={fieldError('reportingManagerId')}
          />
        </fieldset>

        <fieldset className="form-section">
          <legend>Employment</legend>
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
              id="status"
              label="Status"
              value={values.status}
              onChange={(value) => setStatus(asStatus(value))}
              options={EMPLOYEE_STATUSES.map((status) => ({ value: status, label: status }))}
              required
              error={fieldError('status')}
              hint="Leaving the organization is a status change, never a delete — the record stays."
            />
            {values.status !== 'Active' && (
              <TextField
                id="dateOfLeaving"
                label="Date of leaving"
                type="date"
                value={values.dateOfLeaving}
                onChange={(value) => set('dateOfLeaving', value)}
                required
                min={values.dateOfJoining || undefined}
                error={fieldError('dateOfLeaving')}
                hint="Last working day. On or after the date of joining."
              />
            )}
          </div>
        </fieldset>

        <fieldset className="form-section">
          <legend>Personal</legend>
          <div className="form-grid">
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
              options={GENDERS.map((gender) => ({ value: gender, label: gender }))}
              error={fieldError('gender')}
            />
          </div>
        </fieldset>

        <div className="form-actions">
          <Link className="button button-secondary" to={returnTo}>
            Cancel
          </Link>
          <button type="submit" className="button button-primary" disabled={saving}>
            {saving ? <Spinner size={14} label="Saving…" /> : submitLabel}
          </button>
        </div>
      </form>
    </Card>
  )
}

/**
 * A `yyyy-MM-dd` bound for the native date picker, `offset` years from today.
 *
 * These only shape the calendar widget — the form sets `noValidate`, so a typed value outside the range
 * still reaches the API, and the API is what decides. The validator measures against UTC today while this
 * measures against the browser's, so on either side of midnight they can disagree by a day; a picker that
 * is one day generous is not a problem, a picker that silently enforces a rule the server does not have
 * would be.
 */
function yearsFromToday(offset: number): string {
  const date = new Date()
  date.setFullYear(date.getFullYear() + offset)
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}

/**
 * The two enum selects can only produce values they were given, so these are checks that should never
 * fire. They exist because the alternative is an unchecked cast, and an unchecked cast is a promise the
 * compiler stops verifying the moment the options list changes.
 */
function asStatus(value: string): EmployeeStatus {
  const match = EMPLOYEE_STATUSES.find((status) => status === value)
  return match ?? 'Active'
}

function asGender(value: string): Gender {
  const match = GENDERS.find((gender) => gender === value)
  return match ?? 'Unspecified'
}
