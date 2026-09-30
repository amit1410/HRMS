import { useCallback, useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { ApiError } from '../../api/errors.ts'
import {
  activateBank,
  BANK_TYPES,
  createBank,
  deactivateBank,
  deleteBank,
  exportBanks,
  listBanks,
  updateBank,
  type Bank,
  type BankQuery,
  type BankRequest,
  type BankType,
} from '../../api/banks.ts'
import {
  confirmBankImport,
  downloadBankTemplate,
  listBankImportHistory,
  validateBankImport,
  type BankImportHistory,
  type BankImportMode,
  type BankImportPreview,
} from '../../api/bankImport.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { ActiveBadge } from '../../components/Badge.tsx'
import { useAuth } from '../../auth/useAuth.ts'
import { Permissions } from '../../auth/permissions.ts'

const PAGE_SIZE = 25

const emptyForm: BankRequest = {
  code: '',
  name: '',
  shortName: '',
  ifscPrefix: '',
  bankType: null,
  country: '',
  effectiveFrom: '',
  remarks: '',
  isActive: true,
}

function download(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = filename
  anchor.click()
  URL.revokeObjectURL(url)
}

export function BankMasterPage() {
  const { can } = useAuth()
  const canCreate = can(Permissions.bankMaster.create)
  const canEdit = can(Permissions.bankMaster.edit)
  const canActivate = can(Permissions.bankMaster.activate)
  const canImport = can(Permissions.bankMaster.import)
  const canExport = can(Permissions.bankMaster.export)

  const [rows, setRows] = useState<Bank[]>([])
  const [totalCount, setTotalCount] = useState(0)
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<Bank | null>(null)
  const [form, setForm] = useState<BankRequest>(emptyForm)
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const triggerRef = useRef<HTMLElement | null>(null)

  const [importOpen, setImportOpen] = useState(false)

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE))

  const refresh = useCallback(async (nextPage = page) => {
    setLoading(true)
    try {
      const query: BankQuery = {
        page: nextPage,
        pageSize: PAGE_SIZE,
        search: search || undefined,
        isActive: status === '' ? undefined : status === 'active',
      }
      const result = await listBanks(query)
      setRows(result.items)
      setTotalCount(result.totalCount)
      setError(null)
    } catch (caught) {
      setRows([])
      setTotalCount(0)
      setError(caught instanceof ApiError ? caught.message : 'Unable to load banks.')
    } finally {
      setLoading(false)
    }
  }, [page, search, status])

  useEffect(() => { void refresh() }, [refresh])

  useEffect(() => {
    if (!editorOpen) return
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === 'Escape') closeEditor() }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editorOpen, dirty])

  function startNew(element?: HTMLElement) {
    triggerRef.current = element ?? null
    setEditing(null)
    setForm(emptyForm)
    setDirty(false)
    setEditorOpen(true)
    setError(null)
    setInfo(null)
  }

  function startEdit(row: Bank, element?: HTMLElement) {
    triggerRef.current = element ?? null
    setEditing(row)
    setForm({
      code: row.code,
      name: row.name,
      shortName: row.shortName ?? '',
      ifscPrefix: row.ifscPrefix ?? '',
      bankType: row.bankType ?? null,
      country: row.country ?? '',
      effectiveFrom: row.effectiveFrom ? row.effectiveFrom.slice(0, 10) : '',
      remarks: row.remarks ?? '',
      isActive: row.isActive,
    })
    setDirty(false)
    setEditorOpen(true)
    setError(null)
    setInfo(null)
  }

  function closeEditor() {
    if (dirty && !window.confirm('Discard unsaved changes?')) return
    setEditorOpen(false)
    setEditing(null)
    setDirty(false)
    window.setTimeout(() => triggerRef.current?.focus(), 0)
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    setInfo(null)
    const body: BankRequest = {
      ...form,
      shortName: form.shortName?.trim() || null,
      ifscPrefix: form.ifscPrefix?.trim() || null,
      country: form.country?.trim() || null,
      effectiveFrom: form.effectiveFrom ? form.effectiveFrom : null,
      remarks: form.remarks?.trim() || null,
      bankType: form.bankType ?? null,
    }
    try {
      if (editing) await updateBank(editing.id, body)
      else await createBank(body)
      setEditorOpen(false)
      setEditing(null)
      setDirty(false)
      setInfo('Saved successfully. If the record does not match the current filters, it may remain hidden.')
      await refresh()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Unable to save the bank.')
    } finally {
      setSaving(false)
    }
  }

  async function toggleActive(row: Bank) {
    setError(null)
    try {
      if (row.isActive) await deactivateBank(row.id)
      else await activateBank(row.id)
      await refresh()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Unable to change the bank status.')
    }
  }

  async function remove(row: Bank) {
    if (!window.confirm(`Delete ${row.code} — ${row.name}? This cannot be undone. Referenced banks cannot be deleted; deactivate them instead.`)) return
    setError(null)
    try {
      await deleteBank(row.id)
      if (editing?.id === row.id) closeEditor()
      const nextPage = rows.length === 1 && page > 1 ? page - 1 : page
      setPage(nextPage)
      await refresh(nextPage)
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Unable to delete the bank.')
    }
  }

  async function onExport(format: 'csv' | 'xlsx') {
    setError(null)
    try {
      const blob = await exportBanks({ search: search || undefined, isActive: status === '' ? undefined : status === 'active' }, format)
      download(blob, `banks.${format}`)
    } catch {
      setError('Unable to export banks.')
    }
  }

  return (
    <div className="master-management-page">
      <PageHeader
        title="Bank Master"
        subtitle="Maintain the banks available for employee bank accounts and payroll."
        actions={
          <div className="form-actions">
            {canExport ? <button className="button button-secondary" type="button" onClick={() => void onExport('csv')}>Export CSV</button> : null}
            {canExport ? <button className="button button-secondary" type="button" onClick={() => void onExport('xlsx')}>Export XLSX</button> : null}
            {canImport ? <button className="button button-secondary" type="button" onClick={() => setImportOpen(true)}>Bulk import</button> : null}
            {canCreate ? <button className="button button-primary" type="button" onClick={event => startNew(event.currentTarget)}>+ Add bank</button> : null}
          </div>
        }
      />

      {error ? <Notice tone="error">{error}</Notice> : null}
      {info ? <Notice tone="success">{info}</Notice> : null}

      <div className={`master-workspace${editorOpen ? ' has-editor' : ''}`}>
        <Card className="master-records" title="Banks" subtitle={`${totalCount} ${totalCount === 1 ? 'record' : 'records'}`}>
          <div className="toolbar master-toolbar">
            <label className="sr-only" htmlFor="bank-search">Search code, name, short name or IFSC</label>
            <input id="bank-search" className="input" type="search" placeholder="Search code, name, short name or IFSC"
              value={search} onChange={event => { setPage(1); setSearch(event.target.value) }} />
            <select className="input select" aria-label="Status filter" value={status}
              onChange={event => { setPage(1); setStatus(event.target.value) }}>
              <option value="">All statuses</option>
              <option value="active">Active</option>
              <option value="inactive">Inactive</option>
            </select>
          </div>

          {loading ? <p className="muted">Loading banks…</p>
            : rows.length === 0 ? <p className="muted">{search ? 'No banks match this search.' : 'No banks have been added yet.'}</p>
            : (
              <>
                <div className="table-wrap">
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>Code</th><th>Name</th><th>Short name</th><th>IFSC prefix</th><th>Type</th><th>Country</th><th>Status</th><th>Actions</th>
                      </tr>
                    </thead>
                    <tbody>
                      {rows.map(row => (
                        <tr className={editing?.id === row.id ? 'is-editing' : ''} key={row.id}>
                          <td><code className="master-code">{row.code}</code></td>
                          <td>{row.name}</td>
                          <td>{row.shortName ?? '—'}</td>
                          <td>{row.ifscPrefix ?? '—'}</td>
                          <td>{row.bankType ?? '—'}</td>
                          <td>{row.country ?? '—'}</td>
                          <td><ActiveBadge isActive={row.isActive} /></td>
                          <td className="master-row-actions">
                            {canEdit ? <button className="row-action" type="button" onClick={event => startEdit(row, event.currentTarget)} aria-label={`Edit ${row.code}`}>✎ Edit</button> : null}
                            {canActivate ? <button className="row-action" type="button" onClick={() => void toggleActive(row)} aria-label={`${row.isActive ? 'Deactivate' : 'Activate'} ${row.code}`}>{row.isActive ? '⦸ Deactivate' : '✓ Activate'}</button> : null}
                            {canEdit ? <button className="row-action row-action-danger" type="button" onClick={() => void remove(row)} aria-label={`Delete ${row.code}`}>♲ Delete</button> : null}
                            {!canEdit && !canActivate ? <span className="muted">—</span> : null}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <div className="master-list-footer">
                  <span>Showing {(page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, totalCount)} of {totalCount}</span>
                  <div>
                    <button className="button button-secondary" type="button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button>
                    <button className="button button-secondary" type="button" disabled={page >= totalPages} onClick={() => setPage(value => value + 1)}>Next</button>
                  </div>
                </div>
              </>
            )}
        </Card>

        {editorOpen && (canEdit || canCreate) ? (
          <Card className="master-editor" title={editing ? 'Edit bank' : 'Add bank'} subtitle={editing?.name}>
            <button className="master-editor-close" type="button" onClick={closeEditor} aria-label="Close editor">×</button>
            <form className="form-stack" onSubmit={submit}>
              <label className="field"><span>Bank code <em>(required)</em></span>
                <input required maxLength={20} value={form.code} onChange={event => { setDirty(true); setForm({ ...form, code: event.target.value }) }} /></label>
              <label className="field"><span>Bank name <em>(required)</em></span>
                <input required maxLength={100} value={form.name} onChange={event => { setDirty(true); setForm({ ...form, name: event.target.value }) }} /></label>
              <label className="field"><span>Short name</span>
                <input maxLength={100} value={form.shortName ?? ''} onChange={event => { setDirty(true); setForm({ ...form, shortName: event.target.value }) }} /></label>
              <label className="field"><span>IFSC prefix</span>
                <input maxLength={11} value={form.ifscPrefix ?? ''} onChange={event => { setDirty(true); setForm({ ...form, ifscPrefix: event.target.value }) }} /></label>
              <label className="field"><span>Bank type</span>
                <select value={form.bankType ?? ''} onChange={event => { setDirty(true); setForm({ ...form, bankType: (event.target.value || null) as BankType | null }) }}>
                  <option value="">Not specified</option>
                  {BANK_TYPES.map(type => <option key={type} value={type}>{type}</option>)}
                </select></label>
              <label className="field"><span>Country</span>
                <input maxLength={100} value={form.country ?? ''} onChange={event => { setDirty(true); setForm({ ...form, country: event.target.value }) }} /></label>
              <label className="field"><span>Effective from</span>
                <input type="date" value={form.effectiveFrom ?? ''} onChange={event => { setDirty(true); setForm({ ...form, effectiveFrom: event.target.value }) }} /></label>
              <label className="field"><span>Remarks</span>
                <textarea maxLength={1000} value={form.remarks ?? ''} onChange={event => { setDirty(true); setForm({ ...form, remarks: event.target.value }) }} /></label>
              <label className="checkbox-field"><input type="checkbox" checked={form.isActive} onChange={event => { setDirty(true); setForm({ ...form, isActive: event.target.checked }) }} /> Active</label>
              <div className="form-actions">
                <button className="button button-primary" disabled={saving}>{saving ? 'Saving…' : editing ? '✓ Save changes' : '✓ Create'}</button>
                <button className="button button-secondary" type="button" onClick={closeEditor}>Cancel</button>
              </div>
            </form>
          </Card>
        ) : null}
      </div>

      {canImport ? <BankImportHistoryCard reloadKey={totalCount} /> : null}

      {importOpen && canImport ? (
        <BankImportDialog onClose={() => setImportOpen(false)} onCompleted={async () => { await refresh() }} />
      ) : null}
    </div>
  )
}

function BankImportHistoryCard({ reloadKey }: { reloadKey: number }) {
  const [history, setHistory] = useState<BankImportHistory[]>([])
  const [loaded, setLoaded] = useState(false)

  useEffect(() => {
    let cancelled = false
    void listBankImportHistory()
      .then(rows => { if (!cancelled) { setHistory(rows); setLoaded(true) } })
      .catch(() => { if (!cancelled) setLoaded(true) })
    return () => { cancelled = true }
  }, [reloadKey])

  return (
    <Card className="master-records" title="Import history" subtitle={loaded ? `${history.length} import(s)` : 'Loading…'}>
      {history.length === 0 ? <p className="muted">No bank imports have been run yet.</p> : (
        <div className="table-wrap">
          <table className="data-table">
            <thead><tr><th>File</th><th>By</th><th>Total</th><th>Imported</th><th>Status</th><th>Completed</th></tr></thead>
            <tbody>
              {history.map(row => (
                <tr key={row.id}>
                  <td>{row.fileName ?? '—'}</td>
                  <td>{row.importedBy}</td>
                  <td>{row.totalRows}</td>
                  <td>{row.successfulRows}</td>
                  <td>{row.status}</td>
                  <td>{row.completedAtUtc ? new Date(row.completedAtUtc).toLocaleString() : '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )
}

function BankImportDialog({ onClose, onCompleted }: { onClose: () => void; onCompleted: () => Promise<void> }) {
  const [mode, setMode] = useState<BankImportMode>('CreateOnly')
  const [file, setFile] = useState<File | null>(null)
  const [preview, setPreview] = useState<BankImportPreview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function template(format: 'csv' | 'xlsx') {
    setError(null)
    try {
      const blob = await downloadBankTemplate(format)
      download(blob, `bank-master-template.${format}`)
    } catch {
      setError('Unable to download the template.')
    }
  }

  function select(event: ChangeEvent<HTMLInputElement>) {
    setFile(event.target.files?.[0] ?? null)
    setPreview(null)
    setResult(null)
    setError(null)
  }

  async function validate() {
    if (!file) { setError('Select a CSV or XLSX file first.'); return }
    setBusy(true)
    setError(null)
    setPreview(null)
    try {
      setPreview(await validateBankImport(file, mode))
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Unable to validate this file.')
    } finally {
      setBusy(false)
    }
  }

  async function confirm() {
    if (!file || !preview || preview.errorRows > 0) return
    setBusy(true)
    setError(null)
    try {
      const done = await confirmBankImport(mode, file.name, preview.inputRows)
      setResult(`Import completed: ${done.createdRows} created, ${done.updatedRows} updated.`)
      await onCompleted()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Import failed. No changes were applied.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="import-backdrop" role="presentation">
      <section className="import-dialog" role="dialog" aria-modal="true" aria-labelledby="bank-import-title">
        <header>
          <div>
            <h2 id="bank-import-title">Bulk import banks</h2>
            <p>Validate every row before importing.</p>
          </div>
          <button className="master-editor-close" type="button" aria-label="Close bulk import" onClick={() => !busy && onClose()}>×</button>
        </header>
        {error ? <Notice tone="error">{error}</Notice> : null}
        {result ? <Notice tone="success">{result}</Notice> : null}
        <ol className="import-steps"><li>Download template</li><li>Select and validate</li><li>Review preview</li><li>Confirm import</li></ol>
        <p className="field-help">Accepted formats: UTF-8 CSV or XLSX. Maximum 5 MB and 5,000 rows. Columns: BankCode, BankName, ShortName, IFSCPrefix, BankType, Country, Active, EffectiveFrom, Remarks. Existing banks are only changed under "Create or update".</p>
        <div className="import-actions">
          <button className="button button-secondary" type="button" onClick={() => void template('csv')}>Template CSV</button>
          <button className="button button-secondary" type="button" onClick={() => void template('xlsx')}>Template XLSX</button>
          <label className="button button-secondary">Select file<input hidden type="file" accept=".csv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" onChange={select} /></label>
          <select className="input select" value={mode} onChange={event => { setMode(event.target.value as BankImportMode); setPreview(null) }}>
            <option value="CreateOnly">Create only</option>
            <option value="CreateOrUpdate">Create or update</option>
          </select>
          <button className="button button-primary" type="button" disabled={busy || !file} onClick={() => void validate()}>Validate file</button>
        </div>
        {file ? <p className="field-help">Selected: {file.name}</p> : null}
        {busy ? <p className="muted">Validating or importing…</p> : null}
        {preview ? (
          <>
            <div className="import-summary">
              <span>Total {preview.totalRows}</span>
              <span>Valid {preview.validRows}</span>
              <span>New {preview.newRows}</span>
              <span>Updates {preview.updateRows}</span>
              <span>Errors {preview.errorRows}</span>
            </div>
            <div className="table-wrap">
              <table className="data-table">
                <thead><tr><th>Row</th><th>Code</th><th>Name</th><th>Action</th><th>Message</th></tr></thead>
                <tbody>
                  {preview.rows.map(row => (
                    <tr key={row.rowNumber}>
                      <td>{row.rowNumber}</td>
                      <td>{row.bankCode}</td>
                      <td>{row.bankName}</td>
                      <td>{row.action}</td>
                      <td>{row.errors.join('; ') || 'Ready'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        ) : null}
        <footer>
          <button className="button button-secondary" type="button" disabled={busy} onClick={onClose}>Close</button>
          <button className="button button-primary" type="button" disabled={busy || !preview || preview.errorRows > 0} onClick={() => void confirm()}>Confirm import</button>
        </footer>
      </section>
    </div>
  )
}
