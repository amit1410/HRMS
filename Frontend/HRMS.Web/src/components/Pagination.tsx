import { DEFAULT_PAGE_SIZE, MAX_PAGE_SIZE } from '../api/types.ts'

/** The counts every list endpoint returns — `PagedResult<T>` minus its items. */
export interface PageInfo {
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

interface PaginationProps {
  /** Pass the `PagedResult` straight through: the server computed these, so nothing is recalculated. */
  info: PageInfo
  onPageChange: (page: number) => void
  onPageSizeChange?: (pageSize: number) => void
  /** True during a reload, so a second click cannot queue a page the user did not see. */
  disabled?: boolean
}

/** Offered sizes. Every one is at or below the API's `MaxPageSize`, which rejects anything larger. */
const PAGE_SIZES = [10, DEFAULT_PAGE_SIZE, 50, MAX_PAGE_SIZE] as const

/**
 * Page navigation for a list.
 *
 * The row range ("21–40 of 137") is shown alongside the buttons because the page number alone does not
 * say how much is there. `hasNextPage` comes from the API rather than being inferred from
 * `items.length === pageSize` — the last page of an exactly-divisible result set would otherwise look
 * like there was one more.
 *
 * A `<nav>` with a real list of buttons, not a bare row of divs: the current page carries
 * `aria-current="page"`, so a screen reader announces position without the user counting.
 */
export function Pagination({ info, onPageChange, onPageSizeChange, disabled }: PaginationProps) {
  const { page, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage } = info

  // Nothing to page through, and the empty state already says so.
  if (totalCount === 0) return null

  const firstRow = (page - 1) * pageSize + 1
  const lastRow = Math.min(page * pageSize, totalCount)

  return (
    <nav className="pagination" aria-label="Pagination">
      <p className="pagination-summary">
        Showing <strong>{firstRow.toLocaleString()}</strong>–<strong>{lastRow.toLocaleString()}</strong>{' '}
        of <strong>{totalCount.toLocaleString()}</strong>
      </p>

      <div className="pagination-controls">
        {onPageSizeChange !== undefined && (
          <label className="pagination-size">
            Rows
            <select
              className="input select pagination-select"
              value={pageSize}
              onChange={(event) => onPageSizeChange(Number(event.target.value))}
              disabled={disabled}
            >
              {PAGE_SIZES.map((size) => (
                <option key={size} value={size}>
                  {size}
                </option>
              ))}
            </select>
          </label>
        )}

        <ul className="pagination-list">
          <li>
            <button
              type="button"
              className="button button-secondary pagination-step"
              onClick={() => onPageChange(page - 1)}
              disabled={disabled || !hasPreviousPage}
            >
              Previous
            </button>
          </li>

          {pageWindow(page, totalPages).map((entry, index) =>
            entry === 'gap' ? (
              // Presentational: the gap says "there are pages here" to a sighted user, and the
              // surrounding numbers already say it to everyone else.
              <li key={`gap-${index}`} className="pagination-gap" aria-hidden="true">
                …
              </li>
            ) : (
              <li key={entry}>
                <button
                  type="button"
                  className={
                    entry === page ? 'pagination-page is-current' : 'pagination-page'
                  }
                  onClick={() => onPageChange(entry)}
                  disabled={disabled}
                  aria-current={entry === page ? 'page' : undefined}
                  aria-label={`Page ${entry}`}
                >
                  {entry}
                </button>
              </li>
            ),
          )}

          <li>
            <button
              type="button"
              className="button button-secondary pagination-step"
              onClick={() => onPageChange(page + 1)}
              disabled={disabled || !hasNextPage}
            >
              Next
            </button>
          </li>
        </ul>
      </div>
    </nav>
  )
}

/**
 * The page numbers to show: always the first and last, always the current and its neighbours, with a gap
 * standing in for whatever is skipped. Caps the row at seven entries, so a 500-page result set does not
 * produce a control wider than the table it belongs to.
 */
export function pageWindow(current: number, total: number): readonly (number | 'gap')[] {
  if (total <= 7) {
    return Array.from({ length: Math.max(total, 1) }, (_, index) => index + 1)
  }

  const wanted = [1, total, current - 1, current, current + 1]
  const inRange = [...new Set(wanted)].filter((page) => page >= 1 && page <= total).sort((a, b) => a - b)

  const entries: (number | 'gap')[] = []
  let previous = 0
  for (const page of inRange) {
    if (previous > 0 && page - previous > 1) entries.push('gap')
    entries.push(page)
    previous = page
  }
  return entries
}
