import { useCallback, useEffect, useId, useRef, useState, type ChangeEvent, type FormEvent, type ReactNode } from 'react'
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
import { ActiveBadge, Badge } from '../../components/Badge.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { Pagination, type PageInfo } from '../../components/Pagination.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { StatTile } from '../../components/StatTile.tsx'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { Modal } from '../../components/Modal.tsx'
import { useAuth } from '../../auth/useAuth.ts'
import { Permissions } from '../../auth/permissions.ts'

const DEFAULT_PAGE_SIZE = 20

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
  const [meta, setMeta] = useState<PageInfo | null>(null)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [searchInput, setSearchInput] = useState('')
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState('')
  const [bankType, setBankType] = useState<'' | BankType>('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [info, setInfo] = useState<string | null>(null)

  const [counts, setCounts] = useState<{ active: number | null; inactive: number | null }>({ active: null, inactive: null })
  const [countsLoading, setCountsLoading] = useState(true)

  const [editorOpen, setEditorOpen] = useState(false)
  const [editing, setEditing] = useState<Bank | null>(null)
  const [form, setForm] = useState<BankRequest>(emptyForm)
  const [dirty, setDirty] = useState(false)
  const [saving, setSaving] = useState(false)
  const triggerRef = useRef<HTMLElement | null>(null)

  const [importOpen, setImportOpen] = useState(false)
  const [deleting, setDeleting] = useState<Bank | null>(null)

  const refresh = useCallback(async (nextPage = page) => {
    setLoading(true)
    try {
      const query: BankQuery = {
        page: nextPage,
        pageSize,
        search: search || undefined,
        isActive: status === '' ? undefined : status === 'active',
      }
      const result = await listBanks(query)
      setRows(result.items)
      setMeta({
        page: result.page,
        pageSize: result.pageSize,
        totalCount: result.totalCount,
        totalPages: result.totalPages,
        hasPreviousPage: result.hasPreviousPage,
        hasNextPage: result.hasNextPage,
      })
      setError(null)
    } catch (caught) {
      setRows([])
      setMeta(null)
      setError(caught instanceof ApiError ? caught.message : 'Unable to load banks.')
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, search, status])

  const refreshCounts = useCallback(async () => {
    setCountsLoading(true)
    try {
      const [active, inactive] = await Promise.all([
        listBanks({ page: 1, pageSize: 1, isActive: true }),
        listBanks({ page: 1, pageSize: 1, isActive: false }),
      ])
      setCounts({ active: active.totalCount, inactive: inactive.totalCount })
    } catch {
      setCounts({ active: null, inactive: null })
    } finally {
      setCountsLoading(false)
    }
  }, [])

  useEffect(() => { void refresh() }, [refresh])
  useEffect(() => { void refreshCounts() }, [refreshCounts])

  useEffect(() => {
    const timer = window.setTimeout(() => { setSearch(searchInput); setPage(1) }, 300)
    return () => window.clearTimeout(timer)
  }, [searchInput])

  async function reload(nextPage = page) {
    await Promise.all([refresh(nextPage), refreshCounts()])
  }

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

  function updateForm<K extends keyof BankRequest>(key: K, value: BankRequest[K]) {
    setDirty(true)
    setForm(previous => ({ ...previous, [key]: value }))
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
      await reload()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Unable to save the bank.')
    } finally {
      setSaving(false)
    }
  }

  async function toggleActive(row: Bank) {
    setError(null)
    setInfo(null)
    try {
      if (row.isActive) await deactivateBank(row.id)
      else await activateBank(row.id)
      await reload()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Unable to change the bank status.')
    }
  }

  async function confirmDelete() {
    if (!deleting) return
    const row = deleting
    await deleteBank(row.id)
    if (editing?.id === row.id) closeEditor()
    const nextPage = rows.length === 1 && page > 1 ? page - 1 : page
    setPage(nextPage)
    setInfo(`${row.code} — ${row.name} was deleted.`)
    await reload(nextPage)
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

  function clearFilters() {
    setSearchInput('')
    setSearch('')
    setStatus('')
    setBankType('')
    setPage(1)
  }

  const filtersActive = search !== '' || status !== '' || bankType !== ''
  const visibleRows = bankType ? rows.filter(row => row.bankType === bankType) : rows
  const totalBanks = counts.active !== null && counts.inactive !== null ? counts.active + counts.inactive : null

  return (
    <div className="master-management-page bank-master-page">
      <PageHeader
        title="Bank Master"
        subtitle="Manage banks available for employee bank accounts and payroll."
        actions={
          <div className="bank-header-actions">
            {canImport ? <button className="button button-secondary" type="button" onClick={() => setImportOpen(true)}>Bulk import</button> : null}
            {canExport ? <button className="button button-secondary" type="button" onClick={() => void onExport('csv')}>Export CSV</button> : null}
            {canExport ? <button className="button button-secondary" type="button" onClick={() => void onExport('xlsx')}>Export XLSX</button> : null}
            {canCreate ? <button className="button button-primary" type="button" onClick={event => startNew(event.currentTarget)}>+ Add bank</button> : null}
          </div>
        }
      />

      {error ? <Notice tone="error" onDismiss={() => setError(null)}>{error}</Notice> : null}
      {info ? <Notice tone="success" onDismiss={() => setInfo(null)}>{info}</Notice> : null}

      <div className="stat-grid bank-summary-grid">
        <StatTile label="Total banks" value={totalBanks} isLoading={countsLoading} error={null} icon="▣" hint="All banks on record" />
        <StatTile label="Active" value={counts.active} isLoading={countsLoading} error={null} icon="✓" hint="Selectable for accounts" />
        <StatTile label="Inactive" value={counts.inactive} isLoading={countsLoading} error={null} icon="⦸" hint="Retained for history" />
      </div>

      <Card
        className="bank-master-card"
        title="Banks"
        subtitle={meta ? `${meta.totalCount} ${meta.totalCount === 1 ? 'record' : 'records'}` : undefined}
        isRefreshing={loading && rows.length > 0}
      >
        <div className="toolbar bank-toolbar">
          <label className="bank-toolbar-search">
            <span className="sr-only">Search code, name, short name or IFSC</span>
            <SearchIcon />
            <input
              id="bank-search"
              className="input"
              type="search"
              placeholder="Search code, name, short name or IFSC"
              value={searchInput}
              onChange={event => setSearchInput(event.target.value)}
            />
          </label>
          <label className="bank-toolbar-field">
            <span>Status</span>
            <select className="input select" aria-label="Status filter" value={status} onChange={event => { setPage(1); setStatus(event.target.value) }}>
              <option value="">All statuses</option>
              <option value="active">Active</option>
              <option value="inactive">Inactive</option>
            </select>
          </label>
          <label className="bank-toolbar-field">
            <span>Bank type</span>
            <select className="input select" aria-label="Bank type filter" value={bankType} onChange={event => setBankType((event.target.value || '') as '' | BankType)}>
              <option value="">All types</option>
              {BANK_TYPES.map(type => <option key={type} value={type}>{type}</option>)}
            </select>
          </label>
          <button className="button button-secondary bank-toolbar-clear" type="button" onClick={clearFilters} disabled={!filtersActive}>
            <ResetIcon />Clear filters
          </button>
        </div>

        {loading && rows.length === 0 ? (
          <div className="state-block"><Spinner label="Loading banks" /></div>
        ) : visibleRows.length === 0 ? (
          <EmptyState
            title={filtersActive ? 'No banks match these filters.' : 'No banks have been added yet.'}
            message={filtersActive ? 'Try a different search term, or clear the filters to see every bank.' : (canCreate ? 'Add a bank to make it available for employee accounts and payroll.' : undefined)}
            action={filtersActive ? <button className="button button-secondary" type="button" onClick={clearFilters}>Clear filters</button> : undefined}
          />
        ) : (
          <>
            <div className="table-wrap">
              <table className="data-table bank-table">
                <caption className="sr-only">Banks</caption>
                <thead>
                  <tr>
                    <th scope="col">Bank code</th>
                    <th scope="col">Bank details</th>
                    <th scope="col">Short name</th>
                    <th scope="col">IFSC prefix</th>
                    <th scope="col">Type</th>
                    <th scope="col">Status</th>
                    <th scope="col" className="bank-table-actions-head">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {visibleRows.map(row => (
                    <tr className={editing?.id === row.id ? 'is-editing' : ''} key={row.id}>
                      <td><code className="bank-code">{row.code}</code></td>
                      <td>
                        <div className="bank-details">
                          <strong className="bank-name">{row.name}</strong>
                          <span className="bank-country">{row.country || 'Country not set'}</span>
                        </div>
                      </td>
                      <td>{row.shortName ?? '—'}</td>
                      <td>{row.ifscPrefix ? <code className="bank-ifsc">{row.ifscPrefix}</code> : '—'}</td>
                      <td>{row.bankType ?? '—'}</td>
                      <td><ActiveBadge isActive={row.isActive} /></td>
                      <td className="bank-table-actions">
                        {canEdit || canActivate ? (
                          <BankRowMenu
                            row={row}
                            canEdit={canEdit}
                            canActivate={canActivate}
                            onEdit={element => startEdit(row, element)}
                            onToggle={() => void toggleActive(row)}
                            onDelete={() => { setError(null); setInfo(null); setDeleting(row) }}
                          />
                        ) : <span className="muted">—</span>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            {meta && !bankType ? (
              <Pagination
                info={meta}
                onPageChange={setPage}
                onPageSizeChange={size => { setPageSize(size); setPage(1) }}
                disabled={loading}
              />
            ) : bankType ? (
              <p className="bank-filter-note">Showing {visibleRows.length} {bankType} {visibleRows.length === 1 ? 'bank' : 'banks'} on this page.</p>
            ) : null}
          </>
        )}
      </Card>

      {canImport ? <BankImportHistoryCard reloadKey={meta?.totalCount ?? 0} /> : null}

      {editorOpen && (canEdit || canCreate) ? (
        <BankDrawer
          title={editing ? 'Edit bank' : 'Add bank'}
          subtitle={editing ? editing.name : 'Create a new bank for employee accounts and payroll.'}
          onClose={closeEditor}
        >
          <form className="form-stack bank-drawer-form" onSubmit={submit} aria-busy={saving}>
            {error ? <Notice tone="error">{error}</Notice> : null}
            <div className="bank-drawer-grid">
              <label className="field bank-drawer-field">
                <span>Bank code <em>*</em></span>
                <input required maxLength={20} placeholder="e.g. SBI" value={form.code} onChange={event => updateForm('code', event.target.value)} />
              </label>
              <label className="field bank-drawer-field">
                <span>Short name</span>
                <input maxLength={100} placeholder="e.g. SBI" value={form.shortName ?? ''} onChange={event => updateForm('shortName', event.target.value)} />
              </label>
              <label className="field bank-drawer-field bank-drawer-field-full">
                <span>Bank name <em>*</em></span>
                <input required maxLength={100} placeholder="e.g. State Bank of India" value={form.name} onChange={event => updateForm('name', event.target.value)} />
              </label>
              <label className="field bank-drawer-field">
                <span>IFSC prefix</span>
                <input maxLength={11} placeholder="e.g. SBIN" value={form.ifscPrefix ?? ''} onChange={event => updateForm('ifscPrefix', event.target.value)} />
              </label>
              <label className="field bank-drawer-field">
                <span>Bank type</span>
                <select value={form.bankType ?? ''} onChange={event => updateForm('bankType', (event.target.value || null) as BankType | null)}>
                  <option value="">Not specified</option>
                  {BANK_TYPES.map(type => <option key={type} value={type}>{type}</option>)}
                </select>
              </label>
              <label className="field bank-drawer-field">
                <span>Country</span>
                <input maxLength={100} placeholder="e.g. India" value={form.country ?? ''} onChange={event => updateForm('country', event.target.value)} />
              </label>
              <label className="field bank-drawer-field">
                <span>Effective from</span>
                <input type="date" value={form.effectiveFrom ?? ''} onChange={event => updateForm('effectiveFrom', event.target.value)} />
              </label>
              <label className="field bank-drawer-field bank-drawer-field-full">
                <span>Remarks</span>
                <textarea maxLength={1000} placeholder="Optional notes about this bank" value={form.remarks ?? ''} onChange={event => updateForm('remarks', event.target.value)} />
              </label>
            </div>
            <label className="bank-drawer-active">
              <span><input type="checkbox" checked={form.isActive} onChange={event => updateForm('isActive', event.target.checked)} /><strong>Active</strong></span>
              <small>Inactive banks stay on record for history but cannot be chosen for new employee accounts.</small>
            </label>
            <div className="form-actions bank-drawer-footer">
              <button className="button button-secondary" type="button" onClick={closeEditor} disabled={saving}>Cancel</button>
              <button className="button button-primary" disabled={saving}>{saving ? 'Saving…' : editing ? '✓ Save changes' : '✓ Create'}</button>
            </div>
          </form>
        </BankDrawer>
      ) : null}

      {importOpen && canImport ? (
        <BankImportDialog onClose={() => setImportOpen(false)} onCompleted={async () => { await reload() }} />
      ) : null}

      {deleting ? (
        <ConfirmDialog
          title="Delete bank?"
          message={<>Delete <strong>{deleting.code} — {deleting.name}</strong>? This cannot be undone.</>}
          hint="Banks referenced by employee accounts cannot be deleted; deactivate them instead."
          confirmLabel="Delete bank"
          onConfirm={confirmDelete}
          onClose={() => setDeleting(null)}
        />
      ) : null}
    </div>
  )
}

function BankRowMenu({ row, canEdit, canActivate, onEdit, onToggle, onDelete }: {
  row: Bank
  canEdit: boolean
  canActivate: boolean
  onEdit: (element?: HTMLElement) => void
  onToggle: () => void
  onDelete: () => void
}) {
  const [open, setOpen] = useState(false)
  const detailsRef = useRef<HTMLDetailsElement>(null)
  const close = () => setOpen(false)

  useEffect(() => {
    if (!open) return
    const onDocClick = (event: MouseEvent) => {
      if (detailsRef.current && !detailsRef.current.contains(event.target as Node)) setOpen(false)
    }
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', onDocClick)
    document.addEventListener('keydown', onKey)
    return () => { document.removeEventListener('mousedown', onDocClick); document.removeEventListener('keydown', onKey) }
  }, [open])

  return (
    <details className="master-actions-menu" ref={detailsRef} open={open} onToggle={event => setOpen(event.currentTarget.open)}>
      <summary aria-label={`Actions for ${row.code}`} aria-haspopup="menu" />
      <div role="menu">
        {canEdit ? <button type="button" role="menuitem" onClick={event => { close(); onEdit(event.currentTarget) }}>Edit</button> : null}
        {canActivate ? <button type="button" role="menuitem" onClick={() => { close(); onToggle() }}>{row.isActive ? 'Deactivate' : 'Activate'}</button> : null}
        {canEdit ? <button type="button" role="menuitem" className="is-danger" onClick={() => { close(); onDelete() }}>Delete</button> : null}
      </div>
    </details>
  )
}

function BankDrawer({ title, subtitle, onClose, children }: { title: string; subtitle: string; onClose: () => void; children: ReactNode }) {
  const titleId = useId()
  const drawerRef = useRef<HTMLElement>(null)
  const onCloseRef = useRef(onClose)
  useEffect(() => { onCloseRef.current = onClose }, [onClose])
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null
    const overflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    drawerRef.current?.focus()
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') { event.stopPropagation(); onCloseRef.current(); return }
      if (event.key !== 'Tab' || !drawerRef.current) return
      const focusable = [...drawerRef.current.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])')]
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (!first || !last) { event.preventDefault(); return }
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
    }
    document.addEventListener('keydown', onKeyDown, true)
    return () => { document.removeEventListener('keydown', onKeyDown, true); document.body.style.overflow = overflow; previous?.focus() }
  }, [])
  return (
    <div className="bank-drawer-backdrop" onMouseDown={event => { if (event.target === event.currentTarget) onClose() }}>
      <aside className="bank-drawer" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1} ref={drawerRef}>
        <header className="bank-drawer-header">
          <div className="bank-drawer-heading">
            <span className="bank-drawer-title-icon" aria-hidden="true"><BankIcon /></span>
            <div>
              <h2 id={titleId}>{title}</h2>
              <p>{subtitle}</p>
            </div>
          </div>
          <button type="button" className="modal-close" onClick={onClose} aria-label="Close editor">×</button>
        </header>
        <div className="bank-drawer-body">{children}</div>
      </aside>
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
    <Card className="bank-history-card" title="Import history" subtitle={loaded ? `${history.length} import${history.length === 1 ? '' : 's'}` : 'Loading…'}>
      {history.length === 0 ? (
        <EmptyState title="No bank imports have been run yet." message="Imported files appear here once a bulk import completes." />
      ) : (
        <div className="table-wrap">
          <table className="data-table">
            <thead><tr><th scope="col">File</th><th scope="col">By</th><th scope="col">Total</th><th scope="col">Imported</th><th scope="col">Status</th><th scope="col">Completed</th></tr></thead>
            <tbody>
              {history.map(row => (
                <tr key={row.id}>
                  <td>{row.fileName ?? '—'}</td>
                  <td>{row.importedBy}</td>
                  <td>{row.totalRows}</td>
                  <td>{row.successfulRows}</td>
                  <td><Badge tone={row.status === 'Completed' ? 'success' : row.status === 'Failed' ? 'danger' : 'neutral'}>{row.status}</Badge></td>
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

  const step = result ? 3 : preview ? 2 : 1

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

  const footer = (
    <>
      <button className="button button-secondary" type="button" disabled={busy} onClick={onClose}>{result ? 'Done' : 'Cancel'}</button>
      {step === 1 ? <button className="button button-primary" type="button" disabled={busy || !file} onClick={() => void validate()}>Validate file</button> : null}
      {step === 2 ? <button className="button button-primary" type="button" disabled={busy || !preview || preview.errorRows > 0} onClick={() => void confirm()}>Import banks</button> : null}
    </>
  )

  return (
    <Modal title="Bulk import banks" subtitle="Validate every row before importing." className="bank-import-modal" onClose={() => { if (!busy) onClose() }} footer={footer}>
      <ol className="bank-import-stepper" aria-hidden="true">
        <li className={step >= 1 ? 'is-active' : ''}><span>1</span>Upload</li>
        <li className={step >= 2 ? 'is-active' : ''}><span>2</span>Validate</li>
        <li className={step >= 3 ? 'is-active' : ''}><span>3</span>Import</li>
      </ol>

      {error ? <Notice tone="error">{error}</Notice> : null}
      {result ? <Notice tone="success">{result}</Notice> : null}

      {!result ? (
        <>
          <p className="field-help">Accepted formats: UTF-8 CSV or XLSX. Maximum 5 MB and 5,000 rows. Columns: BankCode, BankName, ShortName, IFSCPrefix, BankType, Country, Active, EffectiveFrom, Remarks. Existing banks are only changed under “Create or update”.</p>
          <div className="bank-import-upload">
            <div className="bank-import-templates">
              <span className="bank-import-label">1 · Download a template</span>
              <div className="import-actions">
                <button className="button button-secondary" type="button" onClick={() => void template('csv')}>Template CSV</button>
                <button className="button button-secondary" type="button" onClick={() => void template('xlsx')}>Template XLSX</button>
              </div>
            </div>
            <div className="bank-import-file">
              <span className="bank-import-label">2 · Choose a file and mode</span>
              <div className="import-actions">
                <label className="button button-secondary">{file ? 'Change file' : 'Select file'}<input hidden type="file" accept=".csv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" onChange={select} /></label>
                <select className="input select" value={mode} onChange={event => { setMode(event.target.value as BankImportMode); setPreview(null) }}>
                  <option value="CreateOnly">Create only</option>
                  <option value="CreateOrUpdate">Create or update</option>
                </select>
              </div>
              {file ? <p className="field-help">Selected: {file.name}</p> : null}
            </div>
          </div>
        </>
      ) : null}

      {busy ? <p className="muted">Validating or importing…</p> : null}

      {preview ? (
        <>
          <div className="bank-import-counts">
            <div className="bank-import-count is-valid"><strong>{preview.validRows}</strong><span>Valid</span></div>
            <div className="bank-import-count is-existing"><strong>{preview.updateRows}</strong><span>Existing</span></div>
            <div className="bank-import-count is-errors"><strong>{preview.errorRows}</strong><span>Errors</span></div>
          </div>
          <div className="table-wrap">
            <table className="data-table">
              <thead><tr><th scope="col">Row</th><th scope="col">Code</th><th scope="col">Name</th><th scope="col">Action</th><th scope="col">Message</th></tr></thead>
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
    </Modal>
  )
}

function SearchIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="bank-icon"><circle cx="10.5" cy="10.5" r="6.5" /><path d="m16 16 5 5" /></svg> }
function ResetIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="bank-icon"><path d="M4 7v5h5M5 12a7 7 0 1 0 2-5" /></svg> }
function BankIcon() { return <svg aria-hidden="true" viewBox="0 0 24 24" className="bank-icon"><path d="M4 10h16M5 10 12 4l7 6M6 10v8M10 10v8M14 10v8M18 10v8M4 21h16" /></svg> }
