import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../api/errors.ts'
import {
  createConfirmationPolicy,
  getConfirmationPolicy,
  listConfirmationPolicies,
  updateConfirmationPolicy,
  type ConfirmationPolicy,
  type ConfirmationPolicyRequest,
} from '../../api/confirmation.ts'
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

const emptyForm: ConfirmationPolicyRequest = { code: '', name: '', description: '', isActive: true }

export function ConfirmationPoliciesPage() {
  const { can } = useAuth()
  const canManage = can(Permissions.confirmation.policyManage)
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<'all' | 'active' | 'inactive'>('all')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [form, setForm] = useState<ConfirmationPolicyRequest>(emptyForm)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const query = useApiQuery(
    (signal) =>
      listConfirmationPolicies(
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

  function openCreate() {
    setEditingId(null)
    setForm({ ...emptyForm })
    setError(null)
    setNotice(null)
    setDrawerOpen(true)
  }

  async function openEdit(item: ConfirmationPolicy) {
    setEditingId(item.id)
    setError(null)
    setNotice(null)
    setDrawerOpen(true)
    try {
      const fresh = await getConfirmationPolicy(item.id)
      setForm({ code: fresh.code, name: fresh.name, description: fresh.description ?? '', isActive: fresh.isActive })
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError('Unable to load policy.'))
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      if (editingId) await updateConfirmationPolicy(editingId, form)
      else await createConfirmationPolicy(form)
      setDrawerOpen(false)
      setNotice(editingId ? 'Policy updated.' : 'Policy created.')
      query.refetch()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError('Unable to save policy.'))
    } finally {
      setSaving(false)
    }
  }

  const fieldErrors = error?.fieldErrors ?? {}
  const rows = query.data?.items ?? []

  return (
    <div className="leave-admin-page">
      <PageHeader
        title="Confirmation & Probation Policies"
        subtitle="Configure probation duration, review triggers and applicability per customer. Confirmation dates are derived from the applicable policy."
        actions={
          canManage ? (
            <button className="button button-primary" type="button" onClick={openCreate}>
              Add Policy
            </button>
          ) : undefined
        }
      />
      {notice ? (
        <Notice tone="success" onDismiss={() => setNotice(null)}>
          {notice}
        </Notice>
      ) : null}
      {error && !drawerOpen ? <Notice tone="error">{error.message}</Notice> : null}
      <Card title="Policies" subtitle={query.data ? `${query.data.totalCount} policy(ies) configured` : undefined}>
        <div className="toolbar">
          <input
            aria-label="Search policies"
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
            <Spinner label="Loading policies" />
          </div>
        ) : query.error ? (
          <Notice tone="error">{query.error.message}</Notice>
        ) : rows.length === 0 ? (
          <EmptyState
            title={search ? 'No policies match your search.' : 'No confirmation policies configured yet.'}
            message={!search && canManage ? 'Add a policy and configure its probation rules.' : undefined}
          />
        ) : (
          <div className="table-wrap">
            <table className="data-table">
              <caption className="sr-only">Confirmation policies</caption>
              <thead>
                <tr>
                  <th scope="col">Code</th>
                  <th scope="col">Name</th>
                  <th scope="col">Versions</th>
                  <th scope="col">Published</th>
                  <th scope="col">Status</th>
                  <th scope="col">Actions</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((item) => (
                  <tr key={item.id}>
                    <td>
                      <code>{item.code}</code>
                    </td>
                    <td>
                      <strong>{item.name}</strong>
                    </td>
                    <td>{item.versionCount}</td>
                    <td>{item.publishedVersionCount}</td>
                    <td>
                      <ActiveBadge isActive={item.isActive} />
                    </td>
                    <td className="row-actions">
                      <Link className="row-action" to={`/confirmation/policies/${item.id}`}>
                        Configure
                      </Link>
                      {canManage ? (
                        <button className="row-action" type="button" onClick={() => void openEdit(item)}>
                          Edit
                        </button>
                      ) : null}
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

      {drawerOpen && canManage ? (
        <div className="modal-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget) setDrawerOpen(false) }}>
          <aside className="modal" role="dialog" aria-modal="true" aria-label="Policy editor">
            <header className="modal-header">
              <h2>{editingId ? 'Edit Policy' : 'Add Policy'}</h2>
              <button type="button" className="modal-close" onClick={() => setDrawerOpen(false)} aria-label="Close">
                ×
              </button>
            </header>
            <form className="form-stack" onSubmit={submit} aria-busy={saving}>
              {error ? <Notice tone="error">{error.message}</Notice> : null}
              <label className="field">
                <span>Code *</span>
                <input
                  className={fieldErrors.code ? 'input has-error' : 'input'}
                  required
                  maxLength={40}
                  value={form.code}
                  readOnly={editingId !== null}
                  onChange={(event) => setForm((previous) => ({ ...previous, code: event.target.value }))}
                />
                {fieldErrors.code ? <small className="field-error">{fieldErrors.code}</small> : null}
              </label>
              <label className="field">
                <span>Name *</span>
                <input
                  className={fieldErrors.name ? 'input has-error' : 'input'}
                  required
                  maxLength={150}
                  value={form.name}
                  onChange={(event) => setForm((previous) => ({ ...previous, name: event.target.value }))}
                />
                {fieldErrors.name ? <small className="field-error">{fieldErrors.name}</small> : null}
              </label>
              <label className="field">
                <span>Description</span>
                <textarea
                  className="input"
                  maxLength={1000}
                  value={form.description ?? ''}
                  onChange={(event) => setForm((previous) => ({ ...previous, description: event.target.value }))}
                />
              </label>
              <label className="checkbox-field">
                <input
                  type="checkbox"
                  checked={form.isActive}
                  onChange={(event) => setForm((previous) => ({ ...previous, isActive: event.target.checked }))}
                />
                <span>Active</span>
              </label>
              <div className="form-actions">
                <button className="button button-secondary" type="button" onClick={() => setDrawerOpen(false)} disabled={saving}>
                  Cancel
                </button>
                <button className="button button-primary" type="submit" disabled={saving}>
                  {saving ? <Spinner size={14} label="Saving…" /> : editingId ? 'Save Changes' : 'Create Policy'}
                </button>
              </div>
            </form>
          </aside>
        </div>
      ) : null}
    </div>
  )
}
