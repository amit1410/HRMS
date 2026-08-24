import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { ActiveBadge } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { DataTable, type Column } from '../../components/DataTable.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Pagination } from '../../components/Pagination.tsx'
import { useAuth } from '../../auth/useAuth.ts'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'
import { useFlash } from '../../hooks/useFlash.ts'
import { useListQuery } from '../../hooks/useListQuery.ts'
import { formatNumber } from '../../lib/format.ts'
import type { LookupModule, LookupRecord } from './lookupModules.ts'

/** The one filter these lists take, as the three values the select can hold. */
const FILTER_KEYS = ['isActive'] as const

const ACTIVE_OPTIONS = [
  { value: '', label: 'All statuses' },
  { value: 'true', label: 'Active only' },
  { value: 'false', label: 'Inactive only' },
]

/**
 * The departments / designations list, driven by a {@link LookupModule}.
 *
 * Paging, searching and sorting are all the API's work — the screen sends `page`, `search`, `sortBy` and
 * `sortDescending` and renders what comes back. Nothing is filtered or reordered client-side, so what the
 * user sees is a true page of the whole result set rather than a page that has been quietly rearranged.
 *
 * Row actions are rendered per permission, not disabled per permission. An HRManager holds
 * `Department.View` and nothing else for departments, and the difference matters: a disabled Delete button
 * still tells them the action exists and invites a support question, while its absence says the truth.
 */
export function LookupListPage({ module }: { module: LookupModule }) {
  useDocumentTitle(module.title)

  const { can } = useAuth()
  const flash = useFlash()
  const location = useLocation()

  const canCreate = can(module.permissions.create)
  const canEdit = can(module.permissions.edit)
  const canDelete = can(module.permissions.delete)

  const list = useListQuery({
    sortFields: module.sortFields,
    // Name rather than code: a person scanning for "Engineering" is not thinking about "ENG".
    defaultSortBy: 'name',
    filterKeys: FILTER_KEYS,
  })

  const { data, error, isLoading, isRefreshing, refetch } = useApiQuery(
    (signal) => module.list({ ...list.pagedQuery, isActive: readIsActive(list.filters.isActive) }, signal),
    // `module.key` is in here because the two lists take the same query shape: navigating from
    // `/departments?page=1` to `/designations?page=1` produces an identical `list.key`, and without this
    // the screen would keep the departments it already had.
    [module.key, list.key],
  )

  const [pendingDelete, setPendingDelete] = useState<LookupRecord | null>(null)

  // Handed to the form so Cancel and a successful save come back to *this* view — same page, same search,
  // same sort — instead of an unfiltered list.
  const returnState = { from: `${location.pathname}${location.search}` }

  async function confirmDelete(record: LookupRecord): Promise<void> {
    await module.remove(record.id)
    flash.show(`${record.name} was deleted.`)

    // The row was the only one left on this page, so staying here would show an empty table for a result
    // set that still has rows. Stepping back re-reads the list as a side effect of the URL changing.
    if (data?.items.length === 1 && list.page > 1) {
      list.setPage(list.page - 1)
    } else {
      refetch()
    }
  }

  const columns: readonly Column<LookupRecord>[] = [
    { key: 'code', header: 'Code', sortBy: 'code', render: (row) => <span className="cell-code">{row.code}</span> },
    {
      key: 'name',
      header: 'Name',
      sortBy: 'name',
      render: (row) => (
        <div className="cell-stack">
          <span className="cell-primary">{row.name}</span>
          {row.description ? <span className="cell-secondary">{row.description}</span> : null}
        </div>
      ),
    },
    {
      key: 'employeeCount',
      header: module.countHeader,
      sortBy: 'employeeCount',
      align: 'end',
      secondary: true,
      render: (row) => formatNumber(row.employeeCount),
    },
    {
      key: 'isActive',
      header: 'Status',
      sortBy: 'isActive',
      align: 'end',
      render: (row) => <ActiveBadge isActive={row.isActive} />,
    },
    {
      key: 'actions',
      // The column exists for the buttons; a visible "Actions" heading only adds noise, but a screen
      // reader still needs to be told what the cell is.
      header: <span className="sr-only">Actions</span>,
      align: 'end',
      render: (row) => (
        <div className="row-actions">
          {canEdit && (
            <Link className="row-action" to={`${module.basePath}/${row.id}/edit`} state={returnState}>
              Edit
            </Link>
          )}
          {canDelete && (
            <button
              type="button"
              className="row-action row-action-danger"
              onClick={() => setPendingDelete(row)}
            >
              Delete
            </button>
          )}
        </div>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title={module.title}
        subtitle={module.subtitle}
        actions={
          canCreate ? (
            <Link className="button button-primary" to={`${module.basePath}/new`} state={returnState}>
              New {module.noun}
            </Link>
          ) : undefined
        }
      />

      {flash.message !== null && (
        <Notice tone="success" onDismiss={flash.dismiss}>
          {flash.message}
        </Notice>
      )}

      <Card isRefreshing={isRefreshing}>
        <div className="toolbar">
          <div className="toolbar-search">
            <label className="sr-only" htmlFor="list-search">
              Search {module.title.toLowerCase()}
            </label>
            <input
              id="list-search"
              type="search"
              className="input"
              placeholder={`Search ${module.noun} code or name`}
              value={list.search}
              onChange={(event) => list.setSearch(event.target.value)}
            />
          </div>

          <label className="toolbar-filter">
            <span className="toolbar-filter-label">Status</span>
            <select
              className="input select"
              value={list.filters.isActive ?? ''}
              onChange={(event) => list.setFilter('isActive', event.target.value)}
            >
              {ACTIVE_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>

          {list.isFiltered && (
            <button type="button" className="button button-secondary" onClick={list.clearFilters}>
              Clear filters
            </button>
          )}
        </div>

        <DataTable
          caption={`${module.title}, page ${list.page}`}
          columns={columns}
          rows={data?.items ?? null}
          rowKey={(row) => row.id}
          isLoading={isLoading}
          error={error}
          onRetry={refetch}
          sort={list.sort}
          onSortChange={list.toggleSort}
          emptyTitle={list.isFiltered ? 'Nothing matches those filters' : module.emptyTitle}
          emptyMessage={
            list.isFiltered
              ? 'Try a different search term, or clear the filters to see everything.'
              : module.emptyMessage
          }
          emptyAction={
            list.isFiltered ? (
              <button type="button" className="button button-secondary" onClick={list.clearFilters}>
                Clear filters
              </button>
            ) : canCreate ? (
              <Link className="button button-primary" to={`${module.basePath}/new`} state={returnState}>
                New {module.noun}
              </Link>
            ) : undefined
          }
        />

        {data && (
          <Pagination
            info={data}
            onPageChange={list.setPage}
            onPageSizeChange={list.setPageSize}
            disabled={isRefreshing}
          />
        )}
      </Card>

      {pendingDelete && (
        <ConfirmDialog
          title={`Delete ${module.noun}?`}
          message={
            <>
              <strong>{pendingDelete.name}</strong> ({pendingDelete.code}) will be removed permanently.
            </>
          }
          hint={module.deleteHint}
          onConfirm={() => confirmDelete(pendingDelete)}
          onClose={() => setPendingDelete(null)}
        />
      )}
    </>
  )
}

/** `''` means the filter is off, and the parameter must then be absent rather than `false`. */
function readIsActive(raw: string | undefined): boolean | undefined {
  if (raw === 'true') return true
  if (raw === 'false') return false
  return undefined
}
