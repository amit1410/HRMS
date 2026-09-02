import type { ReactNode } from 'react'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { SupervisorOption, SupervisorType } from '../api/types.ts'
import { getSupervisorOptions } from '../api/employeeSubsections.ts'

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
  type?: 'text' | 'email' | 'password' | 'tel' | 'date' | 'number'
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

// ---------------------------------------------------------------------------------------------
// SupervisorField — searchable dropdown for supervisor selection
// ---------------------------------------------------------------------------------------------

export interface SupervisorFieldProps extends CommonProps {
  /** The employee whose supervisor options we are fetching. */
  employeeId: string
  /** Which supervisor type to query (L1, L2, L3, Other, HR, Time). */
  supervisorType: SupervisorType
  /** Currently selected supervisor employee ID. */
  value: string
  /** Callback when a supervisor is selected or cleared. */
  onChange: (employeeId: string, option: SupervisorOption | null) => void
}

/**
 * A debounced, searchable dropdown that fetches eligible supervisors from the API
 * filtered by the employee's ManagerCategories flags.
 */
export function SupervisorField({
  id,
  label,
  employeeId,
  supervisorType,
  value,
  onChange,
  hint,
  error,
  disabled,
}: SupervisorFieldProps) {
  const { hintId, errorId, describedBy } = fieldIds(id, hint, error)
  const [query, setQuery] = useState('')
  const [options, setOptions] = useState<SupervisorOption[]>([])
  const [loading, setLoading] = useState(false)
  const [open, setOpen] = useState(false)
  const [highlightIndex, setHighlightIndex] = useState(-1)
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const listRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  const selectedOption = options.find((o) => o.employeeId === value) ?? null

  const fetchOptions = useCallback(
    (search: string) => {
      const controller = new AbortController()
      setLoading(true)
      getSupervisorOptions(employeeId, supervisorType, controller.signal)
        .then((data) => {
          const filtered = search
            ? data.filter(
                (o) =>
                  o.employeeCode.toLowerCase().includes(search.toLowerCase()) ||
                  o.fullName.toLowerCase().includes(search.toLowerCase()),
              )
            : data
          setOptions(filtered)
        })
        .catch(() => {
          setOptions([])
        })
        .finally(() => setLoading(false))
      return controller
    },
    [employeeId, supervisorType],
  )

  useEffect(() => {
    const controller = fetchOptions('')
    return () => controller.abort()
  }, [fetchOptions])

  function handleInputChange(event: React.ChangeEvent<HTMLInputElement>): void {
    const newValue = event.target.value
    setQuery(newValue)
    setHighlightIndex(-1)
    if (debounceRef.current) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(() => {
      const filtered = newValue
        ? options.filter(
            (o) =>
              o.employeeCode.toLowerCase().includes(newValue.toLowerCase()) ||
              o.fullName.toLowerCase().includes(newValue.toLowerCase()),
          )
        : options
      setOptions(filtered.length > 0 ? filtered : options)
    }, 150)
  }

  function handleKeyDown(event: React.KeyboardEvent<HTMLInputElement>): void {
    if (!open) return
    const items = options
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault()
        setHighlightIndex((prev) => (prev + 1) % items.length)
        break
      case 'ArrowUp':
        event.preventDefault()
        setHighlightIndex((prev) => (prev - 1 + items.length) % items.length)
        break
      case 'Enter':
        event.preventDefault()
        if (highlightIndex >= 0 && highlightIndex < items.length && items[highlightIndex]) {
          selectOption(items[highlightIndex])
        }
        break
      case 'Escape':
        setOpen(false)
        inputRef.current?.blur()
        break
    }
  }

  function selectOption(option: SupervisorOption): void {
    onChange(option.employeeId, option)
    setQuery('')
    setOpen(false)
    setHighlightIndex(-1)
  }

  function clearSelection(): void {
    onChange('', null)
    setQuery('')
    setOpen(false)
  }

  const displayValue = selectedOption
    ? `${selectedOption.employeeCode} — ${selectedOption.fullName}`
    : query

  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <div className="supervisor-field">
        <input
          ref={inputRef}
          id={id}
          name={id}
          type="text"
          className={error ? 'input has-error' : 'input'}
          value={displayValue}
          placeholder={loading ? 'Loading…' : 'Search by name or code…'}
          onChange={handleInputChange}
          onFocus={() => setOpen(true)}
          onKeyDown={handleKeyDown}
          disabled={disabled}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy}
          aria-expanded={open}
          aria-autocomplete="list"
        />
        {selectedOption && (
          <button
            type="button"
            className="supervisor-field-clear"
            onClick={clearSelection}
            aria-label="Clear selection"
          >
            ×
          </button>
        )}
        {open && options.length > 0 && (
          <div ref={listRef} className="supervisor-field-list" role="listbox">
            {options.map((option, index) => (
              <div
                key={option.employeeId}
                role="option"
                aria-selected={option.employeeId === value}
                className={`supervisor-field-option${index === highlightIndex ? ' highlighted' : ''}${option.employeeId === value ? ' selected' : ''}`}
                onMouseDown={(e) => e.preventDefault()}
                onMouseEnter={() => setHighlightIndex(index)}
                onClick={() => selectOption(option)}
              >
                <span className="supervisor-field-option-code">{option.employeeCode}</span>
                <span className="supervisor-field-option-name">{option.fullName}</span>
                {option.departmentName && (
                  <span className="supervisor-field-option-meta">{option.departmentName}</span>
                )}
                {option.designationName && (
                  <span className="supervisor-field-option-meta">{option.designationName}</span>
                )}
              </div>
            ))}
          </div>
        )}
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

// ---------------------------------------------------------------------------------------------
// SearchableSelect — for large option lists (Country, State, City, etc.)
// ---------------------------------------------------------------------------------------------

export interface SearchableSelectProps extends CommonProps {
  value: string
  onChange: (value: string) => void
  options: readonly SelectOption[]
  placeholder?: string
  loading?: boolean
}

/**
 * A text input that filters a dropdown list as the user types. Designed for large option lists
 * (countries, states, cities) where scrolling through a native `<select>` is impractical.
 * Keyboard navigation (Arrow keys, Enter, Escape) is supported.
 */
export function SearchableSelect({
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
  loading = false,
}: SearchableSelectProps) {
  const { hintId, errorId, describedBy } = fieldIds(id, hint, error)
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [highlightIndex, setHighlightIndex] = useState(-1)
  const inputRef = useRef<HTMLInputElement>(null)
  const listRef = useRef<HTMLDivElement>(null)

  const selectedLabel = options.find((o) => o.value === value)?.label ?? ''

  const filtered = query.trim() === ''
    ? options
    : options.filter((o) => o.label.toLowerCase().includes(query.toLowerCase()))

  const items = filtered.slice(0, 100)

  function handleInputChange(e: React.ChangeEvent<HTMLInputElement>): void {
    setQuery(e.target.value)
    setOpen(true)
    setHighlightIndex(-1)
  }

  function handleKeyDown(e: React.KeyboardEvent): void {
    if (!open) {
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault()
        setOpen(true)
      }
      return
    }
    switch (e.key) {
      case 'ArrowDown':
        e.preventDefault()
        setHighlightIndex((prev) => Math.min(prev + 1, items.length - 1))
        break
      case 'ArrowUp':
        e.preventDefault()
        setHighlightIndex((prev) => Math.max(prev - 1, 0))
        break
      case 'Enter':
        e.preventDefault()
        if (highlightIndex >= 0 && highlightIndex < items.length && items[highlightIndex]) {
          selectOption(items[highlightIndex])
        }
        break
      case 'Escape':
        setOpen(false)
        inputRef.current?.blur()
        break
    }
  }

  function selectOption(option: SelectOption): void {
    onChange(option.value)
    setQuery('')
    setOpen(false)
    setHighlightIndex(-1)
  }

  function clearSelection(): void {
    onChange('')
    setQuery('')
    setOpen(false)
  }

  const displayValue = open ? query : selectedLabel

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
      <div className="supervisor-field">
        <input
          ref={inputRef}
          id={id}
          name={id}
          type="text"
          className={error ? 'input has-error' : 'input'}
          value={displayValue}
          placeholder={loading ? 'Loading…' : (placeholder ?? 'Search…')}
          onChange={handleInputChange}
          onFocus={() => setOpen(true)}
          onBlur={() => setTimeout(() => setOpen(false), 150)}
          onKeyDown={handleKeyDown}
          required={required}
          disabled={disabled}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy}
          aria-expanded={open}
          aria-autocomplete="list"
          autoComplete="off"
        />
        {selectedLabel && !open && (
          <button
            type="button"
            className="supervisor-field-clear"
            onClick={clearSelection}
            aria-label="Clear selection"
          >
            ×
          </button>
        )}
        {open && items.length > 0 && (
          <div ref={listRef} className="supervisor-field-list" role="listbox">
            {items.map((option, index) => (
              <div
                key={option.value}
                role="option"
                aria-selected={option.value === value}
                className={`supervisor-field-option${index === highlightIndex ? ' highlighted' : ''}${option.value === value ? ' selected' : ''}`}
                onMouseDown={(e) => e.preventDefault()}
                onMouseEnter={() => setHighlightIndex(index)}
                onClick={() => selectOption(option)}
              >
                <span className="supervisor-field-option-name">{option.label}</span>
              </div>
            ))}
            {filtered.length > 100 && (
              <div className="supervisor-field-option">
                <span className="supervisor-field-option-meta">
                  Showing first 100 of {filtered.length} matches.
                </span>
              </div>
            )}
          </div>
        )}
      </div>
    </FieldShell>
  )
}
