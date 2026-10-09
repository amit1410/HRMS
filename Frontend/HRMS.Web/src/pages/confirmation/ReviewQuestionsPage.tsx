import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import {
  listConfirmationReviewQuestions,
  type ConfirmationReviewQuestion,
  type ReviewQuestionType,
} from '../../api/confirmationReview.ts'
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

const questionTypes: Array<{ value: ReviewQuestionType | 'all'; label: string }> = [
  { value: 'all', label: 'All types' },
  { value: 'Rating', label: 'Rating' },
  { value: 'YesNo', label: 'Yes / No' },
  { value: 'Text', label: 'Text' },
  { value: 'Dropdown', label: 'Dropdown' },
  { value: 'MultiSelect', label: 'Multi-select' },
]

export function ReviewQuestionsPage() {
  const { can } = useAuth()
  const navigate = useNavigate()
  const canManage = can(Permissions.confirmation.reviewQuestionManage)
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'all' | 'active' | 'inactive'>('all')
  const [type, setType] = useState<ReviewQuestionType | 'all'>('all')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)

  const query = useApiQuery(
    (signal) =>
      listConfirmationReviewQuestions(
        {
          search: search || undefined,
          isActive: status === 'all' ? undefined : status === 'active',
          questionType: type === 'all' ? undefined : type,
          page,
          pageSize,
        },
        signal,
      ),
    [search, status, type, page, pageSize],
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
        title="Confirmation Review Questions"
        subtitle="Configure the questions used on confirmation review forms. Questions are per-customer and never hard-coded."
        actions={
          canManage ? (
            <Link className="button button-primary" to="/confirmation/review-questions/new">
              Add Question
            </Link>
          ) : undefined
        }
      />
      <Card title="Review questions" subtitle={query.data ? `${query.data.totalCount} question(s) configured` : undefined}>
        <div className="toolbar">
          <input
            aria-label="Search review questions"
            className="input toolbar-search"
            type="search"
            placeholder="Search code or text..."
            value={searchInput}
            onChange={(event) => setSearchInput(event.target.value)}
          />
          <select
            aria-label="Question type"
            className="input toolbar-filter"
            value={type}
            onChange={(event) => {
              setType(event.target.value as ReviewQuestionType | 'all')
              setPage(1)
            }}
          >
            {questionTypes.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
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
            <Spinner label="Loading review questions" />
          </div>
        ) : query.error ? (
          <Notice tone="error">{query.error.message}</Notice>
        ) : rows.length === 0 ? (
          <EmptyState
            title={search ? 'No questions match your search.' : 'No review questions configured yet.'}
            message={!search && canManage ? 'Add a question to build the confirmation review form.' : undefined}
          />
        ) : (
          <div className="table-wrap">
            <table className="data-table">
              <caption className="sr-only">Confirmation review questions</caption>
              <thead>
                <tr>
                  <th scope="col">Code</th>
                  <th scope="col">Question</th>
                  <th scope="col">Type</th>
                  <th scope="col">Mandatory</th>
                  <th scope="col">Order</th>
                  <th scope="col">Status</th>
                  <th scope="col">Actions</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((item: ConfirmationReviewQuestion) => (
                  <tr key={item.id}>
                    <td>
                      <code>{item.code}</code>
                    </td>
                    <td>
                      <strong>{item.questionText}</strong>
                    </td>
                    <td>{item.questionType}</td>
                    <td>{item.isMandatory ? 'Yes' : 'No'}</td>
                    <td>{item.displayOrder}</td>
                    <td>
                      <ActiveBadge isActive={item.isActive} />
                    </td>
                    <td className="row-actions">
                      <button className="row-action" type="button" onClick={() => navigate(`/confirmation/review-questions/${item.id}`)}>
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
