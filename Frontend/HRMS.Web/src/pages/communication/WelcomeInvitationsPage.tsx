import { useEffect, useMemo, useState } from 'react'
import { getWelcomeInvitationBatch, getWelcomeInvitationHistory, getWelcomeInvitations, previewWelcomeInvitations, resendWelcomeInvitation, retryWelcomeInvitation, sendWelcomeInvitations, type WelcomeInvitationFilter, type WelcomeInvitationHistoryRow, type WelcomeInvitationRow } from '../../api/welcomeInvitations.ts'
import { listDepartments, listHoldingCompanies, listLinesOfBusiness, listOrganisations, listWorkLocations } from '../../api/masterData.ts'
import { MasterDropdown } from '../../components/MasterDropdown.tsx'
import { toApiError } from '../../api/errors.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import './WelcomeInvitationsPage.css'

const pageSizes = [10, 25, 50, 100]
const stats = [
  { key: 'notInvited', label: 'Not Invited', description: 'Employees without portal access', icon: 'users', tone: 'neutral' },
  { key: 'pending', label: 'Pending', description: 'Invitations awaiting response', icon: 'clock', tone: 'amber' },
  { key: 'sent', label: 'Sent', description: 'Invitations sent to employees', icon: 'send', tone: 'blue' },
  { key: 'failed', label: 'Failed', description: 'Invitations failed to deliver', icon: 'alert', tone: 'red' },
  { key: 'activated', label: 'Activated', description: 'Portal accounts activated', icon: 'check', tone: 'green' },
] as const

export function WelcomeInvitationsPage() {
  const { can } = useAuth()
  const [rows, setRows] = useState<WelcomeInvitationRow[]>([])
  const [summary, setSummary] = useState({ notInvited: 0, pending: 0, sent: 0, failed: 0, activated: 0 })
  const [filter, setFilter] = useState<WelcomeInvitationFilter>({})
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [pageCount, setPageCount] = useState(1)
  const [totalCount, setTotalCount] = useState(0)
  const [hasLoaded, setHasLoaded] = useState(false)
  const [loading, setLoading] = useState(true)
  const [selected, setSelected] = useState<string[]>([])
  const [selectAll, setSelectAll] = useState(false)
  const [busy, setBusy] = useState(false)
  const [batch, setBatch] = useState<{ id: string; text: string } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [history, setHistory] = useState<{ employee: string; rows: WelcomeInvitationHistoryRow[] } | null>(null)
  const [historyError, setHistoryError] = useState<string | null>(null)

  const update = (key: keyof WelcomeInvitationFilter, value: string) => {
    setPage(1); setSelectAll(false)
    setFilter((current) => ({ ...current, [key]: value || undefined }))
  }
  const params = useMemo(() => ({ ...filter, page, pageSize }), [filter, page, pageSize])
  const load = async () => {
    setLoading(true); setError(null)
    try {
      const result = await getWelcomeInvitations(params)
      setRows(result.page.items); setPageCount(result.page.totalPages); setTotalCount(result.page.totalCount); setSummary(result.summary); setHasLoaded(true)
    } catch (reason) {
      console.error('Welcome invitations list request failed', reason)
      const apiError = toApiError(reason)
      setError(apiError.status === 403 ? 'You do not have permission to view invitations.' : apiError.status === 401 ? 'Your session has ended. Sign in again, then retry.' : 'We couldn’t load welcome invitations. Please try again.')
    } finally { setLoading(false) }
  }
  useEffect(() => { const handle = window.setTimeout(() => void load(), 250); return () => window.clearTimeout(handle) }, [params])
  useEffect(() => { if (!batch) return; const timer = window.setInterval(() => void getWelcomeInvitationBatch(batch.id).then((result) => setBatch((current) => current && { ...current, text: `${result.status}: ${result.sent} sent, ${result.failed} failed, ${result.queued + result.processing} pending` })).catch((reason) => console.error('Welcome invitation batch status request failed', reason)), 1500); return () => window.clearInterval(timer) }, [batch?.id])

  const pageIds = useMemo(() => rows.map((row) => row.employeeId), [rows])
  const allPageSelected = pageIds.length > 0 && pageIds.every((id) => selected.includes(id))
  const canSendSelected = selectAll || rows.some((row) => selected.includes(row.employeeId) && row.canSend)
  const canResendSelected = selectAll || rows.some((row) => selected.includes(row.employeeId) && (row.canResend || row.canRetry))
  const toggle = (id: string) => { setSelectAll(false); setSelected((current) => current.includes(id) ? current.filter((item) => item !== id) : [...current, id]) }
  const selectPage = () => setSelected((current) => allPageSelected ? current.filter((id) => !pageIds.includes(id)) : [...new Set([...current, ...pageIds])])
  const clearParent = (key: keyof WelcomeInvitationFilter, value: string, children: (keyof WelcomeInvitationFilter)[]) => { setPage(1); setSelectAll(false); setFilter((current) => ({ ...current, [key]: value || undefined, ...Object.fromEntries(children.map((child) => [child, undefined])) })) }
  const clearFilters = () => { setFilter({}); setPage(1); setSelected([]); setSelectAll(false) }
  const send = async (resend: boolean) => {
    if ((!selectAll && !selected.length) || !window.confirm(`${resend ? 'Resend' : 'Send'} invitations to ${selectAll ? `${totalCount} matching employees` : `${selected.length} selected employee(s)`}?`)) return
    setBusy(true); setError(null)
    try {
      const selection = { employeeIds: selectAll ? [] : selected, filter: selectAll ? filter : undefined, selectAllMatching: selectAll, confirmed: true, resend }
      const preview = await previewWelcomeInvitations(selection)
      if (!window.confirm(`${preview.selectedCount} selected; ${preview.eligibleCount} eligible and ${preview.ineligibleCount} ineligible. Continue?`)) return
      const result = await sendWelcomeInvitations({ ...selection, idempotencyKey: crypto.randomUUID() })
      setBatch({ id: result.batchId, text: `Queued: ${result.accepted} accepted, ${result.rejected} rejected` }); setSelected([]); setSelectAll(false); await load()
    } catch (reason) { console.error('Welcome invitation send request failed', reason); setError('We couldn’t complete the invitation request. Please try again.') } finally { setBusy(false) }
  }
  const rowAction = async (row: WelcomeInvitationRow, action: 'send' | 'resend' | 'retry') => {
    setBusy(true); setError(null)
    try {
      const result = action === 'send'
        ? await sendWelcomeInvitations({ employeeIds: [row.employeeId], confirmed: true, resend: false, idempotencyKey: crypto.randomUUID() })
        : action === 'resend' ? await resendWelcomeInvitation(row.employeeId) : await retryWelcomeInvitation(row.employeeId)
      setBatch({ id: result.batchId, text: `Queued: ${result.accepted} accepted, ${result.rejected} rejected` }); await load()
    }
    catch (reason) { console.error(`Welcome invitation ${action} request failed`, reason); setError('We couldn’t complete that invitation action. Please try again.') }
    finally { setBusy(false) }
  }
  const showHistory = async (row: WelcomeInvitationRow) => {
    setHistoryError(null)
    try { const result = await getWelcomeInvitationHistory(row.employeeId); setHistory({ employee: row.employeeName, rows: result.items }) }
    catch (reason) { console.error('Welcome invitation history request failed', reason); setHistoryError('Invitation history could not be loaded. Please try again.') }
  }

  return <main className="page-shell welcome-page">
    <header className="welcome-header"><div><h1>Welcome Invitations</h1><p>Manage employee portal invitations and account activation.</p></div><div className="welcome-header-actions"><button className="welcome-primary" type="button" disabled={!can(Permissions.welcomeInvitation.send) || !canSendSelected || busy} onClick={() => void send(false)}><Icon name="send" /> Send Invitations</button><details className="welcome-action-menu"><summary aria-label="More invitation actions" aria-disabled={busy}>⌄</summary><div className="welcome-action-menu-items"><button type="button" disabled={!can(Permissions.welcomeInvitation.send) || !canSendSelected || busy} onClick={() => void send(false)}>Send eligible selected</button><button type="button" disabled={!can(Permissions.welcomeInvitation.resend) || !canResendSelected || busy} onClick={() => void send(true)}>Resend eligible selected</button></div></details></div></header>

    <section className="welcome-stats" aria-label="Invitation summary">{stats.map((stat) => <article className={`welcome-stat tone-${stat.tone}`} key={stat.key}><span className="welcome-stat-icon"><Icon name={stat.icon} /></span><div className="welcome-stat-content"><span className="welcome-stat-label">{stat.label}</span><strong>{hasLoaded && !error ? summary[stat.key] : '—'}</strong><span className="welcome-stat-description">{stat.description}</span></div></article>)}</section>

    <section className="welcome-card welcome-filters" aria-labelledby="welcome-filter-title"><div className="welcome-card-heading"><div className="welcome-heading-title"><span className="welcome-heading-icon"><Icon name="filter" /></span><div><h2 id="welcome-filter-title">Filter Employees</h2><p>Find employees by profile, organization, or invitation status.</p></div></div><button type="button" className="welcome-text-button" onClick={clearFilters} disabled={!Object.values(filter).some(Boolean)}>Clear all</button></div>
      <div className="welcome-filter-grid"><label className="welcome-field welcome-search"><span>Search employee</span><div className="welcome-input-icon"><Icon name="search" /><input aria-label="Search employees" placeholder="Name, code, or email" value={filter.search ?? ''} onChange={(event) => update('search', event.target.value)} /></div></label>
        <MasterDropdown id="holding-company" label="Holding Company" value={filter.holdingCompanyId ?? ''} onChange={(value) => clearParent('holdingCompanyId', value, ['lobId', 'organisationId', 'departmentId'])} fetcher={listHoldingCompanies} />
        <MasterDropdown id="line-of-business" label="LOB" value={filter.lobId ?? ''} onChange={(value) => clearParent('lobId', value, ['organisationId', 'departmentId'])} fetcher={listLinesOfBusiness} parentId={filter.holdingCompanyId} />
        <MasterDropdown id="organisation" label="Organization" value={filter.organisationId ?? ''} onChange={(value) => clearParent('organisationId', value, ['departmentId'])} fetcher={listOrganisations} parentId={filter.lobId} />
        <MasterDropdown id="department" label="Department" value={filter.departmentId ?? ''} onChange={(value) => update('departmentId', value)} fetcher={listDepartments} parentId={filter.organisationId} />
        <MasterDropdown id="work-location" label="Work Location" value={filter.workLocationId ?? ''} onChange={(value) => update('workLocationId', value)} fetcher={listWorkLocations} />
        <label className="welcome-field"><span>Joining From</span><input aria-label="Joining date from" type="date" value={filter.joiningDateFrom ?? ''} onChange={(event) => update('joiningDateFrom', event.target.value)} /></label>
        <label className="welcome-field"><span>Joining To</span><input aria-label="Joining date to" type="date" value={filter.joiningDateTo ?? ''} onChange={(event) => update('joiningDateTo', event.target.value)} /></label>
        <label className="welcome-field"><span>Invitation Status</span><select aria-label="Invitation status" value={filter.invitationStatus ?? ''} onChange={(event) => update('invitationStatus', event.target.value)}><option value="">All invitations</option><option value="NotSent">Not sent</option><option value="Pending">Pending</option><option value="Sent">Sent</option><option value="Failed">Failed</option></select></label>
        <label className="welcome-field"><span>Portal Account Status</span><select aria-label="Portal status" value={filter.portalStatus ?? ''} onChange={(event) => update('portalStatus', event.target.value)}><option value="">All portal accounts</option><option value="NotCreated">Not created</option><option value="InvitationPending">Pending</option><option value="Active">Active</option></select></label>
      </div>
    </section>

    <section className="welcome-card welcome-table-card" aria-labelledby="welcome-employee-title"><div className="welcome-table-heading"><div className="welcome-heading-title"><span className="welcome-heading-icon"><Icon name="users" /></span><div><h2 id="welcome-employee-title">Employees <span className="welcome-count">{hasLoaded && !error ? totalCount : '—'}</span></h2><p>Review access and invitation activity.</p></div></div><div className="welcome-bulk-actions"><button type="button" className="welcome-secondary" disabled={!can(Permissions.welcomeInvitation.send) || !canSendSelected || busy} onClick={() => void send(false)}><Icon name="send" /> Send selected</button><button type="button" className="welcome-secondary" disabled={!can(Permissions.welcomeInvitation.resend) || !canResendSelected || busy} onClick={() => void send(true)}><Icon name="refresh" /> Resend selected</button></div></div>
      {batch && <div className="welcome-notice" role="status">Batch {batch.id}: {batch.text}</div>}
      {error ? <><div className="welcome-error" role="alert"><span>{error}</span><button type="button" className="welcome-secondary" onClick={() => void load()}>Retry</button></div><p className="welcome-selection" role="status">{selectAll ? 'All matching employees selected.' : `${selected.length} selected.`}</p></> : <>
        <div className="welcome-selection" role="status" aria-live="polite"><span>{totalCount} employees match the current filters · {selectAll ? 'All matching employees selected' : `${selected.length} selected`}</span>{totalCount > pageIds.length && !selectAll && <button type="button" className="welcome-text-button" onClick={() => setSelectAll(true)}>Select all {totalCount} matching employees</button>}{selectAll && <button type="button" className="welcome-text-button" onClick={() => setSelectAll(false)}>Cancel select-all</button>}</div>
        <div className="welcome-table-wrap" aria-busy={loading}><table className="welcome-table"><thead><tr><th className="welcome-checkbox-cell"><input type="checkbox" aria-label="Select current page" checked={allPageSelected} onChange={selectPage} /></th><th>Employee Code</th><th>Name</th><th>Work Email</th><th>Joining Date</th><th>Portal Invitation</th><th>Last Invitation</th><th>Status</th><th>Actions</th></tr></thead><tbody>{rows.map((row) => <tr key={row.employeeId}><td className="welcome-checkbox-cell"><input type="checkbox" aria-label={`Select ${row.employeeName}`} checked={selected.includes(row.employeeId)} onChange={() => toggle(row.employeeId)} /></td><td className="welcome-code">{row.employeeCode || '—'}</td><td><strong>{row.employeeName}</strong></td><td>{row.workEmail || 'Missing email'}</td><td>{row.joiningDate || '—'}</td><td>{row.portalAccountStatus}</td><td>{row.lastInvitationDateUtc ? new Date(row.lastInvitationDateUtc).toLocaleString() : '—'}</td><td><span className={`welcome-badge status-${row.invitationStatus.toLowerCase()}`}>{row.invitationStatus === 'NotSent' ? 'Not invited' : row.invitationStatus}</span></td><td><div className="welcome-row-actions">{row.canSend && can(Permissions.welcomeInvitation.send) && <button type="button" onClick={() => void rowAction(row, 'send')} disabled={busy} aria-label={`Send invitation to ${row.employeeName}`}>Send</button>}{row.canResend && can(Permissions.welcomeInvitation.resend) && <button type="button" onClick={() => void rowAction(row, 'resend')} disabled={busy} aria-label={`Resend invitation to ${row.employeeName}`}>Resend</button>}{row.canRetry && can(Permissions.welcomeInvitation.retry) && <button type="button" onClick={() => void rowAction(row, 'retry')} disabled={busy} aria-label={`Retry invitation to ${row.employeeName}`}>Retry</button>}{can(Permissions.welcomeInvitation.viewHistory) && <button type="button" onClick={() => void showHistory(row)} aria-label={`View invitation history for ${row.employeeName}`}>History</button>}</div></td></tr>)}</tbody></table>
          {!loading && rows.length === 0 && <div className="welcome-empty"><span className="welcome-empty-icon"><Icon name="search" /></span><h3>{!Object.values(filter).some(Boolean) ? 'No employees available' : 'No employees match the current filters'}</h3><p>{!Object.values(filter).some(Boolean) ? 'Employees will appear here when they are available to invite.' : 'Try adjusting your search or filter criteria to see results.'}</p>{Object.values(filter).some(Boolean) && <button type="button" className="welcome-secondary" onClick={clearFilters}>Clear all filters</button>}</div>}
          {loading && <div className="welcome-loading" role="status">Loading employees…</div>}</div>
        <footer className="welcome-pagination"><span>{totalCount} employees · {selectAll ? totalCount : selected.length} selected</span><div className="welcome-page-controls"><label>Rows per page <select aria-label="Rows per page" value={pageSize} onChange={(event) => { setPageSize(Number(event.target.value)); setPage(1) }}>{pageSizes.map((size) => <option key={size} value={size}>{size}</option>)}</select></label><button type="button" className="welcome-page-button" aria-label="Previous page" disabled={page <= 1 || loading} onClick={() => setPage((value) => value - 1)}>‹</button><span>Page {page} of {pageCount}</span><button type="button" className="welcome-page-button" aria-label="Next page" disabled={page >= pageCount || loading} onClick={() => setPage((value) => value + 1)}>›</button></div></footer>
      </>}
    </section>
    {(history || historyError) && <div className="welcome-modal-backdrop" role="presentation" onClick={() => { setHistory(null); setHistoryError(null) }}><section className="welcome-history-modal" role="dialog" aria-modal="true" aria-labelledby="welcome-history-title" onClick={(event) => event.stopPropagation()}><div className="welcome-card-heading"><h2 id="welcome-history-title">Invitation history{history ? ` · ${history.employee}` : ''}</h2><button type="button" className="welcome-text-button" onClick={() => { setHistory(null); setHistoryError(null) }}>Close</button></div>{historyError ? <p role="alert">{historyError}</p> : <div className="welcome-history-list">{history?.rows.length ? history.rows.map((item) => <article key={item.attemptId}><strong>{item.attemptType} · {item.deliveryStatus}</strong><span>{new Date(item.requestedAtUtc).toLocaleString()} · {item.recipientEmail}</span>{item.errorSummary && <p>{item.errorSummary}</p>}</article>) : <p>No invitation history is available.</p>}</div>}</section></div>}
  </main>
}

function Icon({ name }: { name: string }) {
  const common = { width: 19, height: 19, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 1.8, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const, 'aria-hidden': true as const }
  const paths: Record<string, React.ReactNode> = {
    users: <><path d="M16 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="10" cy="7" r="4"/><path d="M20 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/></>,
    clock: <><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></>,
    send: <><path d="m22 2-7 20-4-9-9-4Z"/><path d="M22 2 11 13"/></>,
    alert: <><circle cx="12" cy="12" r="9"/><path d="M12 8v4m0 4h.01"/></>,
    check: <><circle cx="12" cy="12" r="9"/><path d="m8 12 2.5 2.5L16 9"/></>,
    filter: <><path d="M4 6h16M7 12h10m-7 6h4"/></>,
    search: <><circle cx="10.8" cy="10.8" r="6.8"/><path d="m16 16 5 5"/></>,
    refresh: <><path d="M20 7v5h-5M4 17v-5h5"/><path d="M5.6 9A7 7 0 0 1 18 6l2 2M4 16l2 2a7 7 0 0 0 12.4-3"/></>,
  }
  return <svg {...common}>{paths[name]}</svg>
}
