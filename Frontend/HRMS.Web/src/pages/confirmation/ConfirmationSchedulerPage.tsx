import { useEffect, useState, type FormEvent } from 'react'
import { ApiError } from '../../api/errors.ts'
import {
  getConfirmationSchedulerSettings,
  listConfirmationReminderExecutions,
  updateConfirmationSchedulerSettings,
  type ConfirmationSchedulerSettingRequest,
} from '../../api/confirmation.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Pagination } from '../../components/Pagination.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

const defaults: ConfirmationSchedulerSettingRequest = {
  enabled: true,
  frequencyMinutes: 60,
  defaultTriggerDaysBeforeEnd: 30,
  maxAttempts: null,
  claimLeaseMinutes: null,
  resendProcessed: false,
  notifyEmployee: true,
  notifyManager: true,
  notifyHrbp: true,
}

export function ConfirmationSchedulerPage() {
  const { can } = useAuth()
  const canManage = can(Permissions.confirmation.schedulerManage)
  const [form, setForm] = useState<ConfirmationSchedulerSettingRequest>(defaults)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [statusFilter, setStatusFilter] = useState<string>('')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(10)

  const settings = useApiQuery((signal) => getConfirmationSchedulerSettings(signal), [])
  const executions = useApiQuery(
    (signal) => listConfirmationReminderExecutions({ status: statusFilter || undefined, page, pageSize }, signal),
    [statusFilter, page, pageSize],
  )

  useEffect(() => {
    if (!settings.data) return
    const s = settings.data
    setForm({
      enabled: s.enabled,
      frequencyMinutes: s.frequencyMinutes,
      defaultTriggerDaysBeforeEnd: s.defaultTriggerDaysBeforeEnd,
      maxAttempts: s.maxAttempts ?? null,
      claimLeaseMinutes: s.claimLeaseMinutes ?? null,
      resendProcessed: s.resendProcessed,
      notifyEmployee: s.notifyEmployee,
      notifyManager: s.notifyManager,
      notifyHrbp: s.notifyHrbp,
    })
  }, [settings.data])

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    setNotice(null)
    try {
      await updateConfirmationSchedulerSettings(form)
      setNotice('Scheduler settings saved.')
      settings.refetch()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught : new ApiError('Unable to save scheduler settings.'))
    } finally {
      setSaving(false)
    }
  }

  function numberOrNull(value: string): number | null {
    const trimmed = value.trim()
    if (trimmed === '') return null
    const parsed = Number(trimmed)
    return Number.isFinite(parsed) ? parsed : null
  }

  const fieldErrors = error?.fieldErrors ?? {}
  const rows = executions.data?.items ?? []
  const lastRun = settings.data?.lastRunAtUtc ? new Date(settings.data.lastRunAtUtc).toLocaleString() : 'never'

  return (
    <div className="leave-admin-page">
      <PageHeader
        title="Confirmation Reminder Scheduler"
        subtitle="Configure when probation-completion review reminders are raised to the employee, their manager and HRBP, and review the execution log. Reminders never change confirmation outcomes."
      />
      {notice ? (
        <Notice tone="success" onDismiss={() => setNotice(null)}>
          {notice}
        </Notice>
      ) : null}
      {error ? <Notice tone="error">{error.message}</Notice> : null}

      <Card title="Scheduler Settings" subtitle={`Last run: ${lastRun}`}>
        {settings.isLoading ? (
          <div className="state-block">
            <Spinner label="Loading settings" />
          </div>
        ) : settings.error ? (
          <Notice tone="error">{settings.error.message}</Notice>
        ) : (
          <form className="form-stack" onSubmit={submit} aria-busy={saving}>
            <label className="checkbox-field">
              <input
                type="checkbox"
                checked={form.enabled}
                disabled={!canManage}
                onChange={(event) => setForm((p) => ({ ...p, enabled: event.target.checked }))}
              />
              <span>Scheduler enabled for this tenant</span>
            </label>
            <label className="field">
              <span>Scan frequency (minutes)</span>
              <input
                className={fieldErrors.frequencyMinutes ? 'input has-error' : 'input'}
                type="number"
                min={1}
                disabled={!canManage}
                value={form.frequencyMinutes}
                onChange={(event) => setForm((p) => ({ ...p, frequencyMinutes: Number(event.target.value) }))}
              />
              {fieldErrors.frequencyMinutes ? <small className="field-error">{fieldErrors.frequencyMinutes}</small> : null}
            </label>
            <label className="field">
              <span>Default trigger days before probation end</span>
              <input
                className={fieldErrors.defaultTriggerDaysBeforeEnd ? 'input has-error' : 'input'}
                type="number"
                min={0}
                disabled={!canManage}
                value={form.defaultTriggerDaysBeforeEnd}
                onChange={(event) => setForm((p) => ({ ...p, defaultTriggerDaysBeforeEnd: Number(event.target.value) }))}
              />
              <small className="field-hint">Used only when a policy does not specify its own trigger window.</small>
            </label>
            <label className="field">
              <span>Max delivery attempts (blank = system default)</span>
              <input
                className={fieldErrors.maxAttempts ? 'input has-error' : 'input'}
                type="number"
                min={1}
                disabled={!canManage}
                value={form.maxAttempts ?? ''}
                onChange={(event) => setForm((p) => ({ ...p, maxAttempts: numberOrNull(event.target.value) }))}
              />
            </label>
            <label className="field">
              <span>Claim lease (minutes, blank = system default)</span>
              <input
                className={fieldErrors.claimLeaseMinutes ? 'input has-error' : 'input'}
                type="number"
                min={1}
                disabled={!canManage}
                value={form.claimLeaseMinutes ?? ''}
                onChange={(event) => setForm((p) => ({ ...p, claimLeaseMinutes: numberOrNull(event.target.value) }))}
              />
            </label>
            <fieldset className="field">
              <legend>Recipients</legend>
              <label className="checkbox-field">
                <input type="checkbox" checked={form.notifyEmployee} disabled={!canManage} onChange={(event) => setForm((p) => ({ ...p, notifyEmployee: event.target.checked }))} />
                <span>Employee</span>
              </label>
              <label className="checkbox-field">
                <input type="checkbox" checked={form.notifyManager} disabled={!canManage} onChange={(event) => setForm((p) => ({ ...p, notifyManager: event.target.checked }))} />
                <span>Reporting manager</span>
              </label>
              <label className="checkbox-field">
                <input type="checkbox" checked={form.notifyHrbp} disabled={!canManage} onChange={(event) => setForm((p) => ({ ...p, notifyHrbp: event.target.checked }))} />
                <span>HRBP</span>
              </label>
            </fieldset>
            <label className="checkbox-field">
              <input type="checkbox" checked={form.resendProcessed} disabled={!canManage} onChange={(event) => setForm((p) => ({ ...p, resendProcessed: event.target.checked }))} />
              <span>Re-send already-notified reminders (not recommended)</span>
            </label>
            {canManage ? (
              <div className="form-actions">
                <button className="button button-primary" type="submit" disabled={saving}>
                  {saving ? <Spinner size={14} label="Saving…" /> : 'Save Settings'}
                </button>
              </div>
            ) : (
              <Notice tone="info">You have read-only access to the scheduler configuration.</Notice>
            )}
          </form>
        )}
      </Card>

      <Card title="Execution Log" subtitle={executions.data ? `${executions.data.totalCount} reminder occurrence(s)` : undefined}>
        <div className="toolbar">
          <select
            aria-label="Status filter"
            className="input toolbar-filter"
            value={statusFilter}
            onChange={(event) => {
              setStatusFilter(event.target.value)
              setPage(1)
            }}
          >
            <option value="">All statuses</option>
            <option value="Sent">Sent</option>
            <option value="Failed">Failed</option>
            <option value="Skipped">Skipped</option>
            <option value="Claimed">Claimed</option>
          </select>
        </div>
        {executions.isLoading ? (
          <div className="state-block">
            <Spinner label="Loading execution log" />
          </div>
        ) : executions.error ? (
          <Notice tone="error">{executions.error.message}</Notice>
        ) : rows.length === 0 ? (
          <EmptyState title="No reminder occurrences yet." message="Reminders appear here once employees approach probation completion." />
        ) : (
          <div className="table-wrap">
            <table className="data-table">
              <caption className="sr-only">Confirmation reminder execution log</caption>
              <thead>
                <tr>
                  <th scope="col">Employee</th>
                  <th scope="col">Recipient</th>
                  <th scope="col">Probation End</th>
                  <th scope="col">Trigger</th>
                  <th scope="col">Status</th>
                  <th scope="col">Attempts</th>
                  <th scope="col">Processed</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={row.id}>
                    <td><code>{row.employeeId.slice(0, 8)}</code></td>
                    <td>
                      {row.recipientType}
                      {row.recipientEmail ? <small className="field-hint"> · {row.recipientEmail}</small> : null}
                    </td>
                    <td>{row.probationEndDate}</td>
                    <td>{row.triggerDate}</td>
                    <td>{row.status}{row.lastError ? <small className="field-error"> · {row.lastError}</small> : null}</td>
                    <td>{row.attemptCount}</td>
                    <td>{row.processedAtUtc ? new Date(row.processedAtUtc).toLocaleString() : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        {executions.data && rows.length > 0 ? (
          <Pagination
            info={executions.data}
            onPageChange={setPage}
            onPageSizeChange={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            disabled={executions.isLoading}
          />
        ) : null}
      </Card>
    </div>
  )
}
