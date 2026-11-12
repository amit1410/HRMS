import { useEffect, useRef, useState } from 'react'
import type { ChangeEvent, DragEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  confirmEmployeeBulkUpload,
  downloadEmployeeBulkUploadTemplate,
  getEmployeeBulkImportHistory,
  getEmployeeBulkImportStatus,
  EMPLOYEE_BULK_UPLOAD_MAX_BYTES,
  prepareEmployeeBulkUpload,
  validateEmployeeBulkUpload,
  type EmployeeBulkUploadPreparation,
  type EmployeeBulkUploadPreview,
  type EmployeeBulkImportHistory,
  type EmployeeBulkImportStatus,
} from '../../api/employeeBulkUpload.ts'
import { ApiError } from '../../api/errors.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import './EmployeeBulkUploadPage.css'

const configuredMaximumRows = Number(import.meta.env.VITE_EMPLOYEE_BULK_UPLOAD_MAX_ROWS ?? 10_000)
const MAX_EMPLOYEE_ROWS = Number.isSafeInteger(configuredMaximumRows) && configuredMaximumRows > 0
  ? Math.min(configuredMaximumRows, 10_000)
  : 10_000

/** Masks identifiers defensively in case an API error message ever contains echoed input. */
export function sanitizeBulkUploadText(value: string): string {
  return value
    .replace(/\b\d{12}\b/g, match => `••••••••${match.slice(-4)}`)
    .replace(/\b[A-Z]{5}\d{4}[A-Z]\b/gi, '••••••••••')
    .replace(/\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/gi, '[email hidden]')
    .replace(/(?<!\d)(?:\+?\d[\d\s().-]{7,}\d)(?!\d)/g, match => `••••••${match.replace(/\D/g, '').slice(-2)}`)
}

export function EmployeeBulkUploadPage({ readOnly = false }: { readOnly?: boolean }) {
  const navigate = useNavigate()
  const { operationId: routeOperationId } = useParams<{ operationId: string }>()
  const fileInput = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<EmployeeBulkUploadPreview | null>(null)
  const [prepared, setPrepared] = useState<EmployeeBulkUploadPreparation | null>(null)
  // Kept only in component memory for the next stage. Never write this token to browser storage or logs.
  const [confirmationToken, setConfirmationToken] = useState<string | null>(null)
  const [busy, setBusy] = useState<'template' | 'validate' | 'prepare' | 'confirm' | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [existingOperationId, setExistingOperationId] = useState<string | null>(null)
  const [dragging, setDragging] = useState(false)
  const [confirmDialogOpen, setConfirmDialogOpen] = useState(false)
  const [status, setStatus] = useState<EmployeeBulkImportStatus | null>(null)
  const [statusError, setStatusError] = useState<string | null>(null)
  const [history, setHistory] = useState<EmployeeBulkImportHistory | null>(null)
  const [historyPage, setHistoryPage] = useState(1)
  const [historyError, setHistoryError] = useState<string | null>(null)
  const [historyLoading, setHistoryLoading] = useState(true)

  useEffect(() => {
    if (!routeOperationId) { setStatus(null); return }
    const controller = new AbortController()
    let delay = 2000
    let timer = 0
    let active = true
    const terminal = new Set(['completed', 'failed', 'partiallycompleted', 'cancelled'])
    const poll = async () => {
      try {
        const latest = await getEmployeeBulkImportStatus(routeOperationId, controller.signal)
        if (!active) return
        setStatus(latest)
        setStatusError(null)
        if (terminal.has(latest.state.toLowerCase())) return
        delay = 2000
      } catch (caught) {
        if (!active || controller.signal.aborted) return
        setStatusError(sanitizeBulkUploadText(caught instanceof ApiError ? caught.message : 'Import status is temporarily unavailable.'))
        delay = Math.min(delay * 2, 10000)
      }
      if (active) timer = window.setTimeout(() => { void poll() }, delay)
    }
    void poll()
    return () => { active = false; controller.abort(); window.clearTimeout(timer) }
  }, [routeOperationId])

  useEffect(() => {
    const controller = new AbortController()
    setHistoryLoading(true)
    setHistoryError(null)
    void getEmployeeBulkImportHistory(historyPage, 10, controller.signal).then(value => {
      if (!controller.signal.aborted) setHistory(value)
    }).catch(caught => {
      if (!controller.signal.aborted) setHistoryError(sanitizeBulkUploadText(caught instanceof ApiError ? caught.message : 'Import history is temporarily unavailable.'))
    }).finally(() => { if (!controller.signal.aborted) setHistoryLoading(false) })
    return () => controller.abort()
  }, [historyPage, routeOperationId])

  function resetForFileChange() {
    setPreview(null)
    setPrepared(null)
    setConfirmationToken(null)
    setError(null)
    setExistingOperationId(null)
  }

  function selectFile(candidate: File | null) {
    resetForFileChange()
    if (!candidate) {
      setFile(null)
      return
    }
    if (!candidate.name.toLowerCase().endsWith('.xlsx')) {
      setFile(null)
      setError('Choose an Excel workbook with the .xlsx extension.')
      if (fileInput.current) fileInput.current.value = ''
      return
    }
    if (candidate.size <= 0) {
      setFile(null)
      setError('The selected workbook is empty.')
      if (fileInput.current) fileInput.current.value = ''
      return
    }
    if (candidate.size > EMPLOYEE_BULK_UPLOAD_MAX_BYTES) {
      setFile(null)
      setError('The workbook exceeds the 10 MB limit. Choose a smaller file.')
      if (fileInput.current) fileInput.current.value = ''
      return
    }
    setFile(candidate)
  }

  function onFileChange(event: ChangeEvent<HTMLInputElement>) {
    selectFile(event.currentTarget.files?.[0] ?? null)
  }

  function onDrop(event: DragEvent<HTMLLabelElement>) {
    event.preventDefault()
    setDragging(false)
    selectFile(event.dataTransfer.files?.[0] ?? null)
  }

  async function downloadTemplate() {
    setBusy('template')
    setError(null)
    try {
      const blob = await downloadEmployeeBulkUploadTemplate()
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = 'employee-bulk-upload-template.xlsx'
      document.body.appendChild(link)
      link.click()
      link.remove()
      window.setTimeout(() => URL.revokeObjectURL(url), 0)
    } catch (caught) {
      setError(sanitizeBulkUploadText(caught instanceof ApiError ? caught.message : 'Unable to download the employee template.'))
    } finally {
      setBusy(null)
    }
  }

  async function validateFile() {
    if (!file || busy) return
    setBusy('validate')
    setError(null)
    setPreview(null)
    setPrepared(null)
    setConfirmationToken(null)
    try {
      const result = await validateEmployeeBulkUpload(file)
      setPreview(result)
    } catch (caught) {
      setError(sanitizeBulkUploadText(caught instanceof ApiError ? caught.message : 'Unable to validate this workbook.'))
    } finally {
      setBusy(null)
    }
  }

  async function prepareImport() {
    if (!file || !preview || preview.totalRows < 1 || preview.validRows !== preview.totalRows || preview.invalidRows !== 0 || busy) return
    setBusy('prepare')
    setError(null)
    setExistingOperationId(null)
    try {
      const result = await prepareEmployeeBulkUpload(file)
      setPrepared(result)
      setConfirmationToken(result.confirmationToken)
    } catch (caught) {
      const existingId = caught instanceof ApiError ? caught.fieldErrors.existingOperationId : undefined
      if (existingId && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(existingId)) {
        setExistingOperationId(existingId)
        setError('This workbook already has an import operation. Open its status and results before preparing another copy.')
      } else {
        setError(sanitizeBulkUploadText(caught instanceof ApiError ? caught.message : 'Unable to prepare this import.'))
      }
    } finally {
      setBusy(null)
    }
  }

  async function confirmImport() {
    if (!prepared || !confirmationToken || busy) return
    setBusy('confirm')
    setError(null)
    try {
      const accepted = await confirmEmployeeBulkUpload(prepared.operationId, confirmationToken)
      setConfirmationToken(null)
      setConfirmDialogOpen(false)
      setPrepared(null)
      navigate(`/employees/bulk-upload/${accepted.operationId}`)
    } catch (caught) {
      const message = caught instanceof ApiError && caught.status === 409
        ? 'This confirmation token was rejected or may already have been consumed. Check Import History before preparing again.'
        : caught instanceof ApiError && caught.status === 410
          ? 'This confirmation token has expired. Prepare the workbook again to get a fresh confirmation token.'
        : caught instanceof ApiError ? caught.message : 'The import could not be accepted. Check the operation status before trying again.'
      setError(sanitizeBulkUploadText(message))
    } finally {
      setBusy(null)
    }
  }

  const canPrepare = Boolean(file && preview && preview.totalRows > 0 && preview.invalidRows === 0 && preview.validRows === preview.totalRows && !busy)
  const invalidRows = preview?.rows.filter(row => row.status.toLowerCase() === 'invalid' || row.issues.length > 0) ?? []

  return (
    <main className="employee-bulk-upload page-shell">
      <PageHeader
        title="Employee Bulk Upload"
        subtitle="Download your tenant’s template, validate employee rows, and prepare a secure import."
      />

      <ol className="employee-bulk-upload__steps" aria-label="Employee bulk upload steps">
        <li aria-current={!routeOperationId && !prepared ? 'step' : undefined}>1. Validate workbook</li>
        <li aria-current={prepared && !routeOperationId ? 'step' : undefined}>2. Confirm import</li>
        <li aria-current={routeOperationId && !['completed', 'failed', 'partiallycompleted', 'cancelled'].includes(status?.state.toLowerCase() ?? '') ? 'step' : undefined}>3. Background processing</li>
        <li aria-current={routeOperationId && ['completed', 'failed', 'partiallycompleted', 'cancelled'].includes(status?.state.toLowerCase() ?? '') ? 'step' : undefined}>4. Results</li>
      </ol>

      {error ? <Notice tone="error">
        {error}
        {existingOperationId ? <button className="button button-quiet" type="button" onClick={() => navigate(`/employees/bulk-upload/${existingOperationId}`)}>Open existing import results</button> : null}
      </Notice> : null}

      {!readOnly ? <>
      <Card title="Before you start" subtitle="Use the current tenant template so employee and master values match this workspace.">
        <ul className="employee-bulk-upload__instructions">
          <li>Complete every column marked with an asterisk. Keep optional fields blank when they do not apply.</li>
          <li>Use master codes from the reference sheets and their dropdowns. Do not add or edit internal IDs.</li>
          <li>Employee codes are generated from this tenant’s configured rules; do not enter an employee code.</li>
          <li>Managers must already exist as active employees. A manager created in this same workbook cannot be referenced.</li>
          <li>Upload no more than {MAX_EMPLOYEE_ROWS.toLocaleString()} employee rows. Aadhaar and PAN are sent securely for server-side validation and are not shown in this preview.</li>
        </ul>
        <button className="button button-secondary" type="button" onClick={() => { void downloadTemplate() }} disabled={busy !== null}>
          {busy === 'template' ? <Spinner size={14} label="Downloading template…" /> : 'Download Excel Template'}
        </button>
      </Card>

      <Card title="Choose a workbook" subtitle="Only .xlsx files up to 10 MB are accepted. The server verifies the actual workbook format and row limits.">
        <label
          className={`employee-bulk-upload__dropzone${dragging ? ' is-dragging' : ''}`}
          htmlFor="employee-bulk-upload-file"
          onDragOver={event => { event.preventDefault(); setDragging(true) }}
          onDragLeave={() => setDragging(false)}
          onDrop={onDrop}
        >
          <input
            ref={fileInput}
            id="employee-bulk-upload-file"
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            onChange={onFileChange}
            disabled={busy !== null}
            aria-describedby="employee-bulk-upload-file-help"
          />
          <span className="employee-bulk-upload__dropzone-title" aria-live="polite">{file ? sanitizeBulkUploadText(file.name) : 'Choose or drop an Excel workbook'}</span>
          <span id="employee-bulk-upload-file-help" className="employee-bulk-upload__dropzone-help">
            {file ? `${(file.size / 1024 / 1024).toFixed(2)} MB · Select another file to replace it` : 'The workbook stays on this page until you validate or prepare it.'}
          </span>
        </label>
        {file ? <button className="button button-quiet employee-bulk-upload__replace" type="button" onClick={() => { selectFile(null); if (fileInput.current) fileInput.current.value = '' }} disabled={busy !== null}>Clear selected workbook</button> : null}
        <div className="form-actions employee-bulk-upload__actions">
          <button className="button button-primary" type="button" onClick={() => { void validateFile() }} disabled={!file || busy !== null}>
            {busy === 'validate' ? <Spinner size={14} label="Validating workbook…" /> : 'Validate Workbook'}
          </button>
          {preview ? <button className="button button-primary" type="button" onClick={() => { void prepareImport() }} disabled={!canPrepare}>
            {busy === 'prepare' ? <Spinner size={14} label="Preparing import…" /> : 'Prepare Import'}
          </button> : null}
        </div>
      </Card>

      {preview ? (
        <Card title="Validation preview" subtitle="Validation uses current tenant data. Correct the workbook and validate it again if any row is invalid.">
          <dl className="employee-bulk-upload__counts" aria-label="Validation counts">
            <div><dt>Total rows</dt><dd>{preview.totalRows}</dd></div>
            <div><dt>Valid rows</dt><dd>{preview.validRows}</dd></div>
            <div><dt>Invalid rows</dt><dd>{preview.invalidRows}</dd></div>
            <div><dt>Warning rows</dt><dd>{preview.warningRows}</dd></div>
          </dl>
          {invalidRows.length > 0 ? (
            <div className="table-wrap employee-bulk-upload__error-table-wrap">
              <table className="data-table employee-bulk-upload__error-table">
                <caption className="sr-only">Employee workbook validation errors by Excel row and column</caption>
                <thead><tr><th scope="col">Excel row</th><th scope="col">Column</th><th scope="col">Field</th><th scope="col">Issue</th><th scope="col">How to correct</th></tr></thead>
                <tbody>
                  {invalidRows.flatMap(row => row.issues.map((issue, index) => (
                    <tr key={`${row.rowNumber}-${issue.columnNumber}-${issue.errorCode}-${index}`}>
                      <td>{row.rowNumber}</td>
                      <td>{sanitizeBulkUploadText(issue.columnName)}{issue.columnNumber > 0 ? ` (${issue.columnNumber})` : ''}</td>
                      <td>{sanitizeBulkUploadText(issue.fieldName)}</td>
                      <td>{sanitizeBulkUploadText(issue.message)}</td>
                      <td>{sanitizeBulkUploadText(issue.guidance)}</td>
                    </tr>
                  ))) }
                </tbody>
              </table>
            </div>
          ) : <p className="employee-bulk-upload__valid-message" role="status">All {preview.validRows} {preview.validRows === 1 ? 'row' : 'rows'} passed validation. You can prepare this workbook.</p>}
          {preview.invalidRows > 0 ? <p className="employee-bulk-upload__blocked-message">Preparation is disabled until every row is valid.</p> : null}
        </Card>
      ) : null}

      {prepared ? (
        <Card title="Import prepared" subtitle="This is preparation only. No employees have been created yet.">
          <Notice tone="success">{sanitizeBulkUploadText(prepared.summary)} No employee records have been created.</Notice>
          <dl className="employee-bulk-upload__prepared-details">
            <div><dt>Operation ID</dt><dd><code>{prepared.operationId}</code></dd></div>
            <div><dt>Rows prepared</dt><dd>{prepared.totalRows}</dd></div>
            <div><dt>Status</dt><dd>Prepared — awaiting confirmation</dd></div>
          </dl>
          <p className="employee-bulk-upload__token-note" aria-live="polite">
            {confirmationToken ? 'The one-use confirmation credential is held only in this page’s memory for the next step. It is not stored in browser storage or shown here.' : 'The confirmation credential is no longer available in this page.'}
          </p>
          <p className="employee-bulk-upload__refresh-note">Refreshing or leaving this page loses the credential. It cannot be recovered from this browser; a secure re-preparation or recovery workflow is required.</p>
          <p>Confirming will enqueue creation of {prepared.totalRows} employees for background processing. Employees are not created by preparation.</p>
          <button className="button button-primary" type="button" disabled={!confirmationToken || busy !== null} onClick={() => setConfirmDialogOpen(true)}>Review confirmation</button>
        </Card>
      ) : null}

      {confirmDialogOpen && prepared ? (
        <div className="employee-bulk-upload__dialog-backdrop">
          <section role="alertdialog" aria-modal="true" aria-labelledby="bulk-confirm-title" aria-describedby="bulk-confirm-description" className="employee-bulk-upload__dialog">
            <h2 id="bulk-confirm-title">Enqueue employee creation?</h2>
            <p id="bulk-confirm-description">This accepts {prepared.totalRows} validated rows for background processing. Acceptance does not mean employees have been created. The one-use confirmation token will be consumed.</p>
            <div className="form-actions">
              <button type="button" className="button button-quiet" disabled={busy !== null} onClick={() => setConfirmDialogOpen(false)}>Cancel</button>
              <button type="button" className="button button-primary" disabled={busy !== null || !confirmationToken} onClick={() => { void confirmImport() }}>
                {busy === 'confirm' ? <Spinner size={14} label="Accepting import…" /> : 'Confirm and enqueue'}
              </button>
            </div>
          </section>
        </div>
      ) : null}
      </> : null}

      {routeOperationId ? (
        <Card title="Import progress" subtitle="Status refreshes automatically while the operation is active. You can bookmark this operation URL; it contains no confirmation token.">
          {status ? <>
            <p className="employee-bulk-upload__state" role="status">{status.state}</p>
            <dl className="employee-bulk-upload__counts" aria-label="Import progress counts">
              <div><dt>Total</dt><dd>{status.totalRows}</dd></div><div><dt>Created</dt><dd>{status.succeededRows}</dd></div>
              <div><dt>Failed</dt><dd>{status.failedRows}</dd></div><div><dt>Skipped</dt><dd>{status.skippedRows}</dd></div>
              <div><dt>Remaining</dt><dd>{Math.max(0, status.totalRows - status.succeededRows - status.failedRows - status.skippedRows)}</dd></div>
              <div><dt>Progress</dt><dd>{status.totalRows ? Math.round(((status.succeededRows + status.failedRows + status.skippedRows) / status.totalRows) * 100) : 0}%</dd></div>
            </dl>
            <progress aria-label="Import progress" max={status.totalRows || 1} value={status.succeededRows + status.failedRows + status.skippedRows} />
            <p>Last updated: {new Date(status.lastUpdatedAtUtc).toLocaleString()}</p>
            {status.completedAtUtc ? <p>Completed: {new Date(status.completedAtUtc).toLocaleString()}</p> : null}
            {status.summary ? <p>{sanitizeBulkUploadText(status.summary)}</p> : null}
            {status.rows.some(row => row.status.toLowerCase() === 'failed' || row.message) ? <div className="table-wrap"><table className="data-table"><caption className="sr-only">Sanitized import row results</caption><thead><tr><th scope="col">Excel row</th><th scope="col">Status</th><th scope="col">Error</th></tr></thead><tbody>
              {status.rows.filter(row => row.status.toLowerCase() === 'failed' || row.message).map(row => <tr key={row.excelRowNumber}><td>{row.excelRowNumber}</td><td>{sanitizeBulkUploadText(row.status)}</td><td>{sanitizeBulkUploadText(row.message ?? row.errorCode ?? 'Review this row in the workbook.')}</td></tr>)}
            </tbody></table></div> : null}
            {status.failedRows > 0 || status.state.toLowerCase() === 'partiallycompleted' ? <p>Successful rows remain created. Correct failed rows and prepare a new workbook; there is no blind retry for completed rows.</p> : null}
          </> : <Spinner label="Loading import status…" />}
          {statusError ? <Notice tone="error">{statusError} Status will be retried automatically.</Notice> : null}
        </Card>
      ) : null}

      <Card title="Import history" subtitle="Recent operations for your tenant. Reopen an operation to view its current status and results.">
        {historyError ? <Notice tone="error">{historyError}</Notice> : null}
        {historyLoading ? <Spinner label="Loading import history…" /> : null}
        {history && history.items.length === 0 ? <p>No employee imports yet.</p> : null}
        {history && history.items.length > 0 ? <div className="table-wrap"><table className="data-table"><caption className="sr-only">Employee import history</caption><thead><tr><th scope="col">Prepared</th><th scope="col">State</th><th scope="col">Rows</th><th scope="col">Created</th><th scope="col">Failed</th><th scope="col">Skipped</th><th scope="col">Completed</th><th scope="col">Operation</th></tr></thead><tbody>
          {history.items.map(item => <tr key={item.operationId}><td>{new Date(item.preparedAtUtc).toLocaleString()}</td><td>{sanitizeBulkUploadText(item.state)}</td><td>{item.totalRows}</td><td>{item.succeededRows}</td><td>{item.failedRows}</td><td>{item.skippedRows}</td><td>{item.completedAtUtc ? new Date(item.completedAtUtc).toLocaleString() : '—'}</td><td><button type="button" className="button button-quiet" onClick={() => navigate(`/employees/bulk-upload/${item.operationId}`)}>Open results</button></td></tr>)}
        </tbody></table></div> : null}
        {history && history.totalPages > 1 ? <div className="form-actions"><button className="button button-quiet" type="button" disabled={historyPage <= 1} onClick={() => setHistoryPage(page => page - 1)}>Previous</button><span>Page {history.page} of {history.totalPages}</span><button className="button button-quiet" type="button" disabled={historyPage >= history.totalPages} onClick={() => setHistoryPage(page => page + 1)}>Next</button></div> : null}
      </Card>
    </main>
  )
}
