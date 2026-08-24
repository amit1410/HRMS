import { useState } from 'react'
import { listEmployees } from '../../api/employees.ts'
import type { SelectOption } from '../../components/fields.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts'
import { truncationHint, withCurrent } from './referenceOptions.ts'

/** How many candidates one search returns. Small on purpose: a select of 100 names is not a picker. */
const CANDIDATE_PAGE_SIZE = 20

interface ManagerFieldProps {
  /** The chosen manager's id, or `''` for nobody. */
  value: string
  onChange: (value: string) => void
  /**
   * The employee being edited. Excluded from the candidates because `EmployeeService` refuses an employee
   * who reports to themselves — offering it would be offering a save that cannot succeed.
   */
  excludeId?: string
  /** The manager already on the record, so an edit form opens showing a name rather than an id. */
  current: SelectOption | null
  error?: string
}

/**
 * The reporting-manager picker.
 *
 * Every other reference on this form is a plain `<select>` of one page of options, because a tenant has
 * tens of departments and tens of job titles. It has as many employees as it has employees, and a select
 * holding the first hundred of them is a control that cannot express most of the answers. So this one
 * searches: the box narrows the list server-side — the same `search` the employee list uses, across code,
 * name and email — and the select offers what came back.
 *
 * Two things keep the select honest about what is selected:
 *
 * - The chosen option is carried in state and prepended when the current search does not contain it.
 *   Without that, picking someone and then typing a different search would leave the select's value
 *   matching no option, which renders as blank while the form still holds the id — the user would see an
 *   empty manager field and save a manager anyway.
 * - A manager who has since left is kept selectable the same way, via {@link withCurrent}. The API
 *   rejects an inactive manager only when the reference *changes*, so an existing record may point at one
 *   legitimately, and dropping it from the options would silently reassign them on the next save.
 *
 * The search is only a way to find a candidate; it does not decide anything. The API is what validates
 * that the id belongs to this tenant.
 */
export function ManagerField({ value, onChange, excludeId, current, error }: ManagerFieldProps) {
  const [term, setTerm] = useState('')
  const search = useDebouncedValue(term)

  // The selected option rather than just its id, because the label has to survive a search that no longer
  // returns it. Seeded from the record so an edit form is correct before any request finishes.
  const [selected, setSelected] = useState<SelectOption | null>(current)

  const { data, error: loadError } = useApiQuery(
    (signal) =>
      listEmployees(
        {
          search,
          page: 1,
          pageSize: CANDIDATE_PAGE_SIZE,
          sortBy: 'firstName',
          // Only current employees can be given reports. Someone resigned is exactly who should not
          // appear in this list.
          status: 'Active',
        },
        signal,
      ),
    [search],
  )

  const candidates = (data?.items ?? [])
    .filter((item) => item.id !== excludeId)
    .map((item) => ({ value: item.id, label: `${item.fullName} · ${item.employeeCode}` }))

  const options = withCurrent(candidates, selected)

  function choose(id: string): void {
    // Read the label out of what is on screen now; `''` is the "no manager" entry.
    setSelected(id === '' ? null : (options.find((option) => option.value === id) ?? null))
    onChange(id)
  }

  const hint =
    loadError?.message ??
    truncationHint(
      data?.items.length ?? 0,
      data?.totalCount ?? 0,
      'employees',
      'Search to find someone further down the list.',
    ) ??
    'Optional. Leave as "No reporting manager" for someone at the top of the reporting line.'

  const errorId = error ? 'reportingManagerId-error' : undefined
  const hintId = 'reportingManagerId-hint'

  return (
    <div className="field manager-field">
      <label htmlFor="reportingManagerId">Reporting manager</label>

      <label className="sr-only" htmlFor="manager-search">
        Search employees
      </label>
      <input
        id="manager-search"
        type="search"
        className="input"
        placeholder="Search by name, code or email"
        value={term}
        onChange={(event) => setTerm(event.target.value)}
      />

      <select
        id="reportingManagerId"
        name="reportingManagerId"
        className={error ? 'input select has-error' : 'input select'}
        value={value}
        onChange={(event) => choose(event.target.value)}
        aria-invalid={error ? true : undefined}
        aria-describedby={[errorId, hintId].filter(Boolean).join(' ')}
      >
        <option value="">No reporting manager</option>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>

      {error !== undefined && (
        <p className="field-error" id={errorId}>
          {error}
        </p>
      )}
      <p className="field-hint" id={hintId}>
        {hint}
      </p>
    </div>
  )
}
