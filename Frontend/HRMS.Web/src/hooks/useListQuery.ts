import { useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { DEFAULT_PAGE_SIZE, MAX_PAGE_SIZE, type PagedQuery } from '../api/types.ts'
import type { SortState } from '../components/DataTable.tsx'
import { useDebouncedValue } from './useDebouncedValue.ts'

export interface ListQueryOptions {
  /**
   * The sort fields the endpoint accepts — pass the matching entry from `SORT_FIELDS`. Anything else in
   * the URL is discarded rather than forwarded: the API answers an unknown sort field with a 400, and a
   * hand-edited link should not be able to leave a screen permanently broken.
   */
  sortFields: readonly string[]
  /** Applied when the URL says nothing. Must be one of `sortFields`. */
  defaultSortBy: string
  defaultSortDescending?: boolean
  /** Filter parameters this list understands. Unknown query keys are ignored, not sent on. */
  filterKeys?: readonly string[]
  searchDelayMs?: number
}

export interface ListQueryResult {
  page: number
  pageSize: number
  /** What the search box shows: the URL's value, before the debounce. */
  search: string
  sort: SortState
  /** Current filter values, `''` for "not applied". */
  filters: Readonly<Record<string, string>>
  /** Paging/search/sort ready to send, with the search term debounced. Spread the filters in alongside. */
  pagedQuery: PagedQuery
  /**
   * A stable string identifying the effective query, for `useApiQuery`'s dependency list — one entry
   * that changes exactly when a refetch is due.
   */
  key: string
  setPage: (page: number) => void
  setPageSize: (pageSize: number) => void
  setSearch: (value: string) => void
  /** Sorts by a column: ascending on a new one, the other way round on the one already sorted. */
  toggleSort: (sortBy: string) => void
  setFilter: (name: string, value: string) => void
  /** Drops the search term and every filter, keeping the sort. */
  clearFilters: () => void
  /** True when a search term or any filter is applied, so an empty result can offer to clear them. */
  isFiltered: boolean
}

/**
 * List state — page, page size, search, sort, filters — held in the URL.
 *
 * The URL is the single source of truth rather than component state, which buys three things that
 * `useState` cannot: a filtered list is a link someone can send, the browser's Back button steps through
 * what the user actually did, and returning from a create form lands on the list they left instead of an
 * unfiltered page one.
 *
 * Every write uses `replace: true`. Without it, typing a six-letter search would push six history
 * entries and Back would un-type the search one letter at a time; with it the list occupies one entry
 * and Back leaves the list.
 *
 * Defaults are *absent* from the URL, not spelled out in it — page one with the default sort is a bare
 * `/employees`, and only what the user changed shows up.
 *
 * The search term reaches the API debounced, because each list endpoint runs a `LIKE` over several
 * columns and should not do that per keystroke. The box itself is not debounced: it renders the URL's
 * value immediately, so typing never feels delayed.
 */
export function useListQuery({
  sortFields,
  defaultSortBy,
  defaultSortDescending = false,
  filterKeys = [],
  searchDelayMs = 300,
}: ListQueryOptions): ListQueryResult {
  const [searchParams, setSearchParams] = useSearchParams()

  const page = readPage(searchParams.get('page'))
  const pageSize = readPageSize(searchParams.get('pageSize'))
  const search = searchParams.get('search') ?? ''
  const sortBy = readSortBy(searchParams.get('sortBy'), sortFields, defaultSortBy)
  const sortDescending = readDirection(searchParams.get('dir'), defaultSortDescending)

  // `filterKeys` is a module-level constant at every call site, so its identity is stable; the values are
  // read fresh from the URL on each render and memoised only to keep `filters` referentially stable for
  // the key below.
  const filters = useMemo(() => {
    const current: Record<string, string> = {}
    for (const name of filterKeys) {
      current[name] = searchParams.get(name) ?? ''
    }
    return current
  }, [searchParams, filterKeys])

  const debouncedSearch = useDebouncedValue(search, searchDelayMs)

  /**
   * Applies changes to the URL. Anything set to `''` or `null` is removed, so a cleared filter leaves no
   * trace. The page is dropped unless it is what changed: a new search or filter that kept the user on
   * page 4 would show them an empty table for a result set that has three pages.
   */
  function update(changes: Record<string, string | number | null>): void {
    setSearchParams(
      (current) => {
        const next = new URLSearchParams(current)
        for (const [name, value] of Object.entries(changes)) {
          if (value === null || value === '') next.delete(name)
          else next.set(name, String(value))
        }
        if (!('page' in changes)) next.delete('page')
        return next
      },
      { replace: true },
    )
  }

  const pagedQuery: PagedQuery = {
    page,
    pageSize,
    search: debouncedSearch,
    sortBy,
    sortDescending,
  }

  return {
    page,
    pageSize,
    search,
    sort: { sortBy, sortDescending },
    filters,
    pagedQuery,
    key: JSON.stringify([pagedQuery, filters]),
    setPage: (value) => update({ page: value <= 1 ? null : value }),
    // A larger page holding the same rows: page 3 of 20 starts at row 41, which page 3 of 50 does not
    // contain. Going back to page one is the only answer that is not arbitrary.
    setPageSize: (value) => update({ pageSize: value === DEFAULT_PAGE_SIZE ? null : value }),
    setSearch: (value) => update({ search: value }),
    toggleSort: (field) => {
      if (!sortFields.includes(field)) return
      const descending = field === sortBy ? !sortDescending : false
      const isDefault = field === defaultSortBy && descending === defaultSortDescending
      update({
        sortBy: isDefault ? null : field,
        dir: isDefault ? null : descending ? 'desc' : 'asc',
      })
    },
    setFilter: (name, value) => update({ [name]: value }),
    clearFilters: () =>
      update({ search: null, ...Object.fromEntries(filterKeys.map((name) => [name, null])) }),
    isFiltered: search !== '' || Object.values(filters).some((value) => value !== ''),
  }
}

function readPage(raw: string | null): number {
  const value = Number(raw)
  return Number.isInteger(value) && value >= 1 ? value : 1
}

/** Clamped rather than rejected: the API refuses anything above `MaxPageSize`, so asking is pointless. */
function readPageSize(raw: string | null): number {
  const value = Number(raw)
  if (!Number.isInteger(value) || value < 1) return DEFAULT_PAGE_SIZE
  return Math.min(value, MAX_PAGE_SIZE)
}

function readSortBy(raw: string | null, allowed: readonly string[], fallback: string): string {
  return raw !== null && allowed.includes(raw) ? raw : fallback
}

function readDirection(raw: string | null, fallback: boolean): boolean {
  if (raw === 'desc') return true
  if (raw === 'asc') return false
  return fallback
}
