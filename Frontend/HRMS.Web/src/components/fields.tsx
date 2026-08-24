import type { ReactNode } from 'react'

/**
 * Form controls.
 *
 * Four of them — text, textarea, select, checkbox — sharing one skeleton: a label bound to the input by
 * `htmlFor`, the message from the API underneath, and an optional hint under that. Everything a screen
 * reader needs is wired from the same `id`, so a field cannot end up with a visible error that assistive
 * technology never announces.
 *
 * `error` is a plain string rather than an `ApiError` because it comes straight out of
 * {@link ApiError.fieldErrors}, keyed by the camelCase field name the API sent. That is the whole
 * contract between a form and the server's validator: the form renders `error.fieldErrors.employeeCode`
 * under the employee-code input and adds nothing of its own.
 *
 * There is deliberately no client-side validation mirroring the server's rules. Two copies of
 * "code must match `^[A-Za-z0-9][A-Za-z0-9._\-/]*$`" would drift, and the one that matters is the one in
 * `FluentValidation`. Required fields carry the native `required` attribute — which the a11y tree reports
 * and the browser does not act on, because the forms set `noValidate` — so the submit always reaches the
 * API and the API always decides.
 */

interface CommonProps {
  /** Also the `name`, so a browser autofill heuristic has something to work with. */
  id: string
  label: ReactNode
  /** A message from the API's `errors[]`, or `undefined` when the field is fine. */
  error?: string
  hint?: string
  required?: boolean
  disabled?: boolean
}

/** The `id`s the label, error and hint are published under, and the aria attributes that reference them. */
interface FieldIds {
  hintId: string | undefined
  errorId: string | undefined
  describedBy: string | undefined
}

function fieldIds(id: string, hint: ReactNode, error: string | undefined): FieldIds {
  const hintId = hint ? `${id}-hint` : undefined
  const errorId = error ? `${id}-error` : undefined
  return {
    hintId,
    errorId,
    // Error first: when both are present, the correction is more urgent than the explanation.
    describedBy: [errorId, hintId].filter(Boolean).join(' ') || undefined,
  }
}

/** The label, plus the messages below the control. Shared so the three field types cannot diverge. */
function FieldShell({
  id,
  label,
  required,
  error,
  errorId,
  hint,
  hintId,
  children,
}: {
  id: string
  label: ReactNode
  required?: boolean
  error?: string
  errorId?: string
  hint?: string
  hintId?: string
  children: ReactNode
}) {
  return (
    <div className="field">
      <label htmlFor={id}>
        {label}
        {required && (
          <span className="field-required" aria-hidden="true">
            *
          </span>
        )}
      </label>
      {children}
      {error !== undefined && (
        <p className="field-error" id={errorId}>
          {error}
        </p>
      )}
      {hint !== undefined && (
        <p className="field-hint" id={hintId}>
          {hint}
        </p>
      )}
    </div>
  )
}

export interface TextFieldProps extends CommonProps {
  value: string
  onChange: (value: string) => void
  type?: 'text' | 'email' | 'password' | 'tel' | 'date'
  autoComplete?: string
  autoCapitalize?: string
  /** Matches the server's `MaximumLength` so the input stops where the validator would. */
  maxLength?: number
  /** For `type="date"`: the earliest date the API would accept, used by the native picker. */
  min?: string
  max?: string
  placeholder?: string
}

export function TextField({
  id,
  label,
  value,
  onChange,
  type = 'text',
  autoComplete,
  autoCapitalize,
  maxLength,
  min,
  max,
  placeholder,
  hint,
  error,
  required,
  disabled,
}: TextFieldProps) {
  const { hintId, errorId, describedBy } = fieldIds(id, hint, error)

  return (
    <FieldShell
      id={id}
      label={label}
      required={required}
      error={error}
      errorId={errorId}
      hint={hint}
      hintId={hintId}
    >
      <input
        id={id}
        name={id}
        type={type}
        className={error ? 'input has-error' : 'input'}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        autoComplete={autoComplete}
        autoCapitalize={autoCapitalize}
        maxLength={maxLength}
        min={min}
        max={max}
        placeholder={placeholder}
        required={required}
        disabled={disabled}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
      />
    </FieldShell>
  )
}

export interface TextAreaFieldProps extends CommonProps {
  value: string
  onChange: (value: string) => void
  rows?: number
  maxLength?: number
}

export function TextAreaField({
  id,
  label,
  value,
  onChange,
  rows = 3,
  maxLength,
  hint,
  error,
  required,
  disabled,
}: TextAreaFieldProps) {
  const { hintId, errorId, describedBy } = fieldIds(id, hint, error)

  return (
    <FieldShell
      id={id}
      label={label}
      required={required}
      error={error}
      errorId={errorId}
      hint={hint}
      hintId={hintId}
    >
      <textarea
        id={id}
        name={id}
        className={error ? 'input textarea has-error' : 'input textarea'}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        rows={rows}
        maxLength={maxLength}
        required={required}
        disabled={disabled}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
      />
    </FieldShell>
  )
}

export interface SelectOption {
  value: string
  label: string
  /** Shown but unselectable — used to keep a retired reference visible without offering it to others. */
  disabled?: boolean
}

export interface SelectFieldProps extends CommonProps {
  value: string
  onChange: (value: string) => void
  options: readonly SelectOption[]
  /**
   * The empty-value entry. Present means "no selection" is expressible: a filter offers "All
   * departments", an optional reference offers "None". A required reference omits it, so the field
   * cannot start out looking answered when it is not.
   */
  placeholder?: string
}

export function SelectField({
  id,
  label,
  value,
  onChange,
  options,
  placeholder,
  hint,
  error,
  required,
  disabled,
}: SelectFieldProps) {
  const { hintId, errorId, describedBy } = fieldIds(id, hint, error)

  return (
    <FieldShell
      id={id}
      label={label}
      required={required}
      error={error}
      errorId={errorId}
      hint={hint}
      hintId={hintId}
    >
      <select
        id={id}
        name={id}
        className={error ? 'input select has-error' : 'input select'}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        required={required}
        disabled={disabled}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
      >
        {placeholder !== undefined && <option value="">{placeholder}</option>}
        {options.map((option) => (
          <option key={option.value} value={option.value} disabled={option.disabled}>
            {option.label}
          </option>
        ))}
      </select>
    </FieldShell>
  )
}

export interface CheckboxFieldProps extends Omit<CommonProps, 'required'> {
  checked: boolean
  onChange: (checked: boolean) => void
}

/**
 * A single boolean. The label sits *after* the box rather than above it, which is why this does not use
 * {@link FieldShell} — everything else about it, including how the hint and error are announced, is the
 * same.
 */
export function CheckboxField({
  id,
  label,
  checked,
  onChange,
  hint,
  error,
  disabled,
}: CheckboxFieldProps) {
  const { hintId, errorId, describedBy } = fieldIds(id, hint, error)

  return (
    <div className="field field-check">
      <div className="check-row">
        <input
          id={id}
          name={id}
          type="checkbox"
          className="checkbox"
          checked={checked}
          onChange={(event) => onChange(event.target.checked)}
          disabled={disabled}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy}
        />
        <label htmlFor={id}>{label}</label>
      </div>
      {error !== undefined && (
        <p className="field-error" id={errorId}>
          {error}
        </p>
      )}
      {hint !== undefined && (
        <p className="field-hint" id={hintId}>
          {hint}
        </p>
      )}
    </div>
  )
}
