import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  createConfirmationReviewCase,
  listConfirmationReviewCases,
  type ConfirmationReviewCase,
  type ConfirmationReviewCaseStatus,
} from '../../api/confirmationReviewCase.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Pagination } from '../../components/Pagination.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

const statuses: Array<{ value: ConfirmationReviewCaseStatus | 'all'; label: string }> = [
  { value: 'all', label: 'All statuses' },
  { value: 'Open', label: 'Open' },
  { value: 'InReview', label: 'In review' },
  { value: 'Completed', label: 'Completed' },
  { value: 'Cancelled', label: 'Cancelled' },
]

export function ConfirmationReviewCasesPage() {
  const { can } = useAuth()
  const navigate = useNavigate()
  const canManage = can(Permissions.confirmation.reviewCaseManage)
  const [status, setStatus] = useState<ConfirmationReviewCaseStatus | 'all'>('all')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [employeeId, setEmployeeId] = useState('')
  const [reload, setReload] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const query = useApiQuery(
    (signal) =>
      listConfirmationReviewCases(
        { status: status === 'all' ? undefined : status, page, pageSize },
        signal,
      ),
    [status, page, pageSize, reload],
  )

  async function createCase() {
    const trimmed = employeeId.trim()
    if (!trimmed) {
      setError('Enter an employee id to initialize a review case.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      const detail = await createConfirmationReviewCase(trimmed)
      navigate(`/confirmation/review-cases/${detail.case.id}`)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create review case.')
    } finally {
      setBusy(false)
    }
  }

  const rows = query.data?.items ?? []

  return (
    <div className="leave-admin-page">
      <PageHeader
        title="Confirmation Reviews"
        subtitle="Probation review cases. A case snapshots the applicable questions so the review never changes if the master is later edited."
      />
      {canManage ? (
        <Card title="Initialize a review case" subtitle="Creates (or returns) the case for an employee's current probation cycle.">
          <div className="toolbar">
            <input
              aria-label="Employee id"
              className="input toolbar-search"
              placeholder="Employee id (GUID)"
              value={employeeId}
              onChange={(event) => setEmployeeId(event.target.value)}
            />
            <button className="button button-primary" type="button" disabled={busy} onClick={createCase}>
              {busy ? 'Working…' : 'Initialize case'}
            </button>
          </div>
          {error ? <Notice tone="error">{error}</Notice> : null}
        </Card>
      ) : null}
      <Card title="Review cases" subtitle={query.data ? `${query.data.totalCount} case(s)` : undefined}>
        <div className="toolbar">
          <select
            aria-label="Status"
            className="input toolbar-filter"
            value={status}
            onChange={(event) => {
              setStatus(event.target.value as typeof status)
              setPage(1)
            }}
          >
            {statuses.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
          <button className="button" type="button" onClick={() => setReload((value) => value + 1)}>
            Refresh
          </button>
        </div>
        {query.isLoading ? (
          <div className="state-block">
            <Spinner label="Loading review cases" />
          </div>
        ) : query.error ? (
          <Notice tone="error">{query.error.message}</Notice>
        ) : rows.length === 0 ? (
          <EmptyState title="No review cases yet." message={canManage ? 'Initialize a case for an employee above.' : undefined} />
        ) : (
          <div className="table-wrap">
            <table className="data-table">
              <caption className="sr-only">Confirmation review cases</caption>
              <thead>
                <tr>
                  <th scope="col">Employee</th>
                  <th scope="col">Probation ends</th>
                  <th scope="col">Due</th>
                  <th scope="col">Questions</th>
                  <th scope="col">Status</th>
                  <th scope="col">Actions</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((item: ConfirmationReviewCase) => (
                  <tr key={item.id}>
                    <td>
                      <strong>{item.employeeName ?? '—'}</strong>
                      {item.employeeCode ? <div className="muted"><code>{item.employeeCode}</code></div> : null}
                    </td>
                    <td>{item.probationEndDate}</td>
                    <td>{item.dueDate}</td>
                    <td>{item.questionCount}</td>
                    <td>{item.status}</td>
                    <td className="row-actions">
                      <button className="row-action" type="button" onClick={() => navigate(`/confirmation/review-cases/${item.id}`)}>
                        Open
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
