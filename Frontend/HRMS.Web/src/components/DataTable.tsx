import type { ReactNode } from 'react'
import type { ApiError } from '../api/errors.ts'
import { EmptyState } from './EmptyState.tsx'
import { ErrorState } from './ErrorState.tsx'
import { Spinner } from './Spinner.tsx'

export interface Column<T> {
  /** Stable key for React and for the header cell. */
  key: string
  header: ReactNode
  render: (row: T) => ReactNode
  align?: 'start' | 'end'
  /** Hidden on narrow screens — for columns that add context rather than identify the row. */
  secondary?: boolean
  /**
   * The API's sort-field name, making this header clickable. It must be one of the values in
   * `SORT_FIELDS` for the endpoint: the query validator rejects anything else with a 400 that lists the
   * permitted names rather than quietly falling back, so an invented field breaks the list outright.
   *
   * Note these are *not* always the column keys — the employee endpoint sorts by `department`, while the
   * column renders `departmentName`.
   */
  sortBy?: string
}

/** The sort the list is currently showing, as the API's two query parameters. */
export interface SortState {
  sortBy: string
  sortDescending: boolean
}

interface DataTableProps<T> {
  columns: readonly Column<T>[]
  rows: readonly T[] | null
  rowKey: (row: T) => string
  /** Describes the table for screen readers; visually hidden. */
  caption: string
  isLoading?: boolean
  error?: ApiError | null
  onRetry?: () => void
  emptyTitle?: string
  emptyMessage?: string
  /** A way forward from the empty state: "Add the first department", "Clear the filters". */
  emptyAction?: ReactNode
  sort?: SortState
  /**
   * Called with the clicked column's `sortBy`. Deciding what that *means* — start ascending on a new
   * column, flip direction on the current one — is the caller's, because the caller is what holds the
   * sort and puts it in the URL.
   */
  onSortChange?: (sortBy: string) => void
}

/**
 * One table rendering, shared by every list in the app, including its loading/empty/failed states —
 * so a screen cannot accidentally show a bare empty table where an error belongs.
 *
 * Generic over the row type rather than taking `unknown[]`: `render` then receives a typed row, and a
 * column that reads a field the DTO does not have fails at compile time.
 *
 * Sorting is server-side, always. A column header asks the API to reorder the whole result set and come
 * back with page one of it; reordering the twenty rows in hand would produce a table that is sorted
 * within the page and unsorted across it, which is worse than no sorting at all.
 */
export function DataTable<T>({
  columns,
  rows,
  rowKey,
  caption,
  isLoading = false,
  error = null,
  onRetry,
  emptyTitle = 'Nothing to show',
  emptyMessage,
  emptyAction,
  sort,
  onSortChange,
}: DataTableProps<T>) {
  if (error) {
    return <ErrorState error={error} onRetry={onRetry} />
  }

  if (isLoading && rows === null) {
    return (
      <div className="table-loading">
        <Spinner label="Loading…" />
      </div>
    )
  }

  if (!rows || rows.length === 0) {
    return <EmptyState title={emptyTitle} message={emptyMessage} action={emptyAction} />
  }

  return (
    <div className="table-scroll">
      <table className="data-table">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr>
            {columns.map((column) => {
              const sortable = column.sortBy !== undefined && onSortChange !== undefined
              const active = sortable && sort?.sortBy === column.sortBy
              const direction = active && sort?.sortDescending ? 'descending' : 'ascending'

              return (
                <th
                  key={column.key}
                  scope="col"
                  className={cellClass(column)}
                  // `none` rather than omitting the attribute on the other sortable columns: it tells a
                  // screen reader the column *can* be sorted and currently is not.
                  aria-sort={sortable ? (active ? direction : 'none') : undefined}
                >
                  {sortable && column.sortBy !== undefined ? (
                    <button
                      type="button"
                      className={active ? 'th-sort is-active' : 'th-sort'}
                      onClick={() => onSortChange(column.sortBy ?? '')}
                    >
                      {column.header}
                      <span className="th-sort-mark" aria-hidden="true">
                        {active ? (direction === 'descending' ? '▾' : '▴') : '⇅'}
                      </span>
                    </button>
                  ) : (
                    column.header
                  )}
                </th>
              )
            })}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={rowKey(row)}>
              {columns.map((column) => (
                <td key={column.key} className={cellClass(column)}>
                  {column.render(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function cellClass<T>(column: Column<T>): string | undefined {
  const classes = [
    column.align === 'end' ? 'align-end' : undefined,
    column.secondary ? 'col-secondary' : undefined,
  ].filter(Boolean)
  return classes.length > 0 ? classes.join(' ') : undefined
}
