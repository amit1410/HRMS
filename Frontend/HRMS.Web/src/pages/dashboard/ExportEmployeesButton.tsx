import { useState } from 'react'
import { exportEmployees, saveFile } from '../../api/employees.ts'
import { toApiError, type ApiError } from '../../api/errors.ts'
import type { EmployeeQuery } from '../../api/types.ts'
import { Spinner } from '../../components/Spinner.tsx'

/**
 * Downloads the employee directory as CSV.
 *
 * Rendered only for holders of `Employee.Export`, which is a separate permission from `Employee.View`
 * server-side — reading a page of rows and walking out with the whole directory are different acts, and
 * the API treats them as such.
 *
 * The same filters the caller is looking at are sent along, so an export matches what is on screen
 * instead of quietly widening to everything.
 */
export function ExportEmployeesButton({ query = {} }: { query?: EmployeeQuery }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<ApiError | null>(null)

  async function onExport() {
    setBusy(true)
    setError(null)
    try {
      saveFile(await exportEmployees(query))
    } catch (caught) {
      setError(toApiError(caught))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="export-control">
      <button
        type="button"
        className="button button-secondary"
        onClick={onExport}
        disabled={busy}
      >
        {busy ? <Spinner size={14} label="Preparing…" /> : 'Export CSV'}
      </button>
      {error && (
        <p className="form-error export-error" role="alert">
          {error.message}
        </p>
      )}
    </div>
  )
}
