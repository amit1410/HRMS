import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { listConfirmationRatingScales, type ConfirmationRatingScale } from '../../api/confirmationReview.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { ActiveBadge } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Pagination } from '../../components/Pagination.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

export function RatingScalesPage() {
  const { can } = useAuth()
  const navigate = useNavigate()
  const canManage = can(Permissions.confirmation.ratingScaleManage)
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'all' | 'active' | 'inactive'>('all')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)

  const query = useApiQuery(
    (signal) =>
      listConfirmationRatingScales(
        { search: search || undefined, isActive: status === 'all' ? undefined : status === 'active', page, pageSize },
        signal,
      ),
    [search, status, page, pageSize],
  )

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setSearch(searchInput)
      setPage(1)
    }, 300)
    return () => window.clearTimeout(timer)
  }, [searchInput])

  const rows = query.data?.items ?? []

  return (
    <div className="leave-admin-page">
      <PageHeader
        title="Confirmation Rating Scales"
        subtitle="Configure the rating scales used by confirmation review questions (e.g. 1 = Unsatisfactory … 5 = Outstanding)."
        actions={
          canManage ? (
            <Link className="button button-primary" to="/confirmation/rating-scales/new">
              Add Rating Scale
            </Link>
          ) : undefined
        }
      />
      <Card title="Rating scales" subtitle={query.data ? `${query.data.totalCount} scale(s) configured` : undefined}>
        <div className="toolbar">
          <input
            aria-label="Search rating scales"
            className="input toolbar-search"
            type="search"
            placeholder="Search code or name..."
            value={searchInput}
            onChange={(event) => setSearchInput(event.target.value)}
          />
          <select
            aria-label="Status"
            className="input toolbar-filter"
            value={status}
            onChange={(event) => {
              setStatus(event.target.value as typeof status)
              setPage(1)
            }}
          >
            <option value="all">All statuses</option>
            <option value="active">Active</option>
            <option value="inactive">Inactive</option>
          </select>
        </div>
        {query.isLoading ? (
          <div className="state-block">
            <Spinner label="Loading rating scales" />
          </div>
        ) : query.error ? (
          <Notice tone="error">{query.error.message}</Notice>
        ) : rows.length === 0 ? (
          <EmptyState
            title={search ? 'No rating scales match your search.' : 'No rating scales configured yet.'}
            message={!search && canManage ? 'Add a rating scale to use in rating questions.' : undefined}
          />
        ) : (
          <div className="table-wrap">
            <table className="data-table">
              <caption className="sr-only">Confirmation rating scales</caption>
              <thead>
                <tr>
                  <th scope="col">Code</th>
                  <th scope="col">Name</th>
                  <th scope="col">Range</th>
                  <th scope="col">Values</th>
                  <th scope="col">Status</th>
                  <th scope="col">Actions</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((item: ConfirmationRatingScale) => (
                  <tr key={item.id}>
                    <td>
                      <code>{item.code}</code>
                    </td>
                    <td>
                      <strong>{item.name}</strong>
                    </td>
                    <td>
                      {item.minRating}–{item.maxRating}
                    </td>
                    <td>{item.valueCount}</td>
                    <td>
                      <ActiveBadge isActive={item.isActive} />
                    </td>
                    <td className="row-actions">
                      <button className="row-action" type="button" onClick={() => navigate(`/confirmation/rating-scales/${item.id}`)}>
                        {canManage ? 'Edit' : 'View'}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {query.data && rows.length > 0 ? (
          <Pagination
            info={query.data}
            onPageChange={setPage}
            onPageSizeChange={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            disabled={query.isLoading}
          />
        ) : null}
      </Card>
    </div>
  )
}
