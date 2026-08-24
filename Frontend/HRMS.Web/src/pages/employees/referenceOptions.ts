import { listDepartments } from '../../api/departments.ts'
import { listDesignations } from '../../api/designations.ts'
import { MAX_PAGE_SIZE } from '../../api/types.ts'
import type { SelectOption } from '../../components/fields.tsx'

/**
 * The option lists behind the employee form's department and designation pickers, and the same two
 * filters on the list screen.
 *
 * Both are fetched in one page of `MaxPageSize`, which is what the API will give at most. `total` comes
 * back alongside so a caller can say when there are more than were loaded — a select that silently holds
 * the first hundred of two hundred departments would be a picker that cannot pick some of them, and the
 * user would have no way to know.
 */
export interface ReferenceOptions {
  options: SelectOption[]
  /** How many exist in total, which may be more than `options.length`. */
  total: number
}

interface LoadOptions {
  /**
   * `true` for a form, where assigning an employee to a retired unit is what the API refuses; `false`
   * for a filter, where an inactive department that still has employees is a legitimate thing to look at.
   */
  activeOnly?: boolean
}

export async function loadDepartmentOptions(
  { activeOnly = false }: LoadOptions = {},
  signal?: AbortSignal,
): Promise<ReferenceOptions> {
  const page = await listDepartments(
    { pageSize: MAX_PAGE_SIZE, sortBy: 'name', ...(activeOnly ? { isActive: true } : {}) },
    signal,
  )
  return {
    options: page.items.map((item) => ({ value: item.id, label: labelFor(item) })),
    total: page.totalCount,
  }
}

export async function loadDesignationOptions(
  { activeOnly = false }: LoadOptions = {},
  signal?: AbortSignal,
): Promise<ReferenceOptions> {
  const page = await listDesignations(
    { pageSize: MAX_PAGE_SIZE, sortBy: 'name', ...(activeOnly ? { isActive: true } : {}) },
    signal,
  )
  return {
    options: page.items.map((item) => ({ value: item.id, label: labelFor(item) })),
    total: page.totalCount,
  }
}

function labelFor({ name, isActive }: { name: string; isActive: boolean }): string {
  return isActive ? name : `${name} (inactive)`
}

/**
 * Guarantees the record's current reference is selectable.
 *
 * This exists because of a specific rule in `EmployeeService.UpdateAsync`: an inactive department or
 * designation, or a manager who has left, is rejected **only when the reference changes**. An existing
 * employee may therefore legitimately point at one — and an edit form built from an active-only list
 * would not contain it. A `<select>` whose value matches no option shows the first one instead, so saving
 * would quietly reassign the employee to whatever happened to be at the top of the list.
 *
 * Prepending the current value makes that impossible. It is marked so the user can see why it is there.
 */
export function withCurrent(
  options: readonly SelectOption[],
  current: SelectOption | null,
): SelectOption[] {
  if (!current || current.value === '') return [...options]
  if (options.some((option) => option.value === current.value)) return [...options]
  return [current, ...options]
}

/**
 * A hint naming what was left out, or `undefined` when everything is there. `more` is appended when the
 * caller has a way to reach the rest — the manager picker has a search box, the two selects do not.
 */
export function truncationHint(
  loaded: number,
  total: number,
  noun: string,
  more?: string,
): string | undefined {
  if (total <= loaded) return undefined
  return `Showing the first ${loaded} of ${total} ${noun}.${more ? ` ${more}` : ''}`
}
