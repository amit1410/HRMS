import { useState } from 'react'
import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { blockClearanceTask, clearClearanceTask, getFunctionalClearanceInbox, getHrClearanceDashboard, getManagerClearanceInbox, updateClearanceAssetReturn, type ClearanceDashboardItem, type ClearanceInboxItem } from '../../api/separation.ts'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

type Mode = 'manager' | 'functional' | 'hr'

export function ClearanceOperationsPage({ mode }: { mode: Mode }) {
  const [page, setPage] = useState(1)
  const [refresh, setRefresh] = useState(0)
  const [search, setSearch] = useState('')
  const query = { page, pageSize: 25, search: search || undefined }
  const inbox = useApiQuery(signal => mode === 'manager' ? getManagerClearanceInbox(query, signal) : getFunctionalClearanceInbox(query, signal), [mode, page, search, refresh])
  const dashboard = useApiQuery(signal => mode === 'hr' ? getHrClearanceDashboard(query, signal) : Promise.resolve(null), [mode, page, search, refresh])
  const result = mode === 'hr' ? dashboard.data : inbox.data
  const act = async (task: ClearanceInboxItem, action: 'clear' | 'block') => {
    if (action === 'clear') await clearClearanceTask(task.taskId, 'Operational clearance completed')
    else await blockClearanceTask(task.taskId, 'Operational blocker recorded')
    setRefresh(value => value + 1)
  }
  const assetAct = async (task: ClearanceInboxItem, status: 'Returned' | 'Lost') => {
    if (!task.assetId || !task.assetReference || !task.assetName) return
    await updateClearanceAssetReturn(task.taskId, task.assetId, { assetReference: task.assetReference, assetType: 'Operational', assetName: task.assetName, returnStatus: status, condition: status === 'Returned' ? 'Good' : 'Unknown', recoveryRequired: false })
    setRefresh(value => value + 1)
  }
  const title = mode === 'manager' ? 'Manager Clearance Inbox' : mode === 'functional' ? 'Functional Clearance Inbox' : 'HR Clearance Dashboard'
  return <section className="page-shell">
    <PageHeader title={title} subtitle="Permission-gated operational clearance with server-side scope and paging." />
    <Card title="Filters"><input aria-label="Employee search" placeholder="Employee search" value={search} onChange={event => { setPage(1); setSearch(event.target.value) }} /></Card>
    {mode === 'hr' ? <Dashboard rows={dashboard.data?.items ?? []} /> : <Inbox rows={inbox.data?.items ?? []} onAction={act} onAssetAction={assetAct} />}
    <div className="button-row"><button type="button" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button><span>Page {page}</span><button type="button" disabled={!result || page * 25 >= result.totalCount} onClick={() => setPage(value => value + 1)}>Next</button></div>
  </section>
}

function Inbox({ rows, onAction, onAssetAction }: { rows: ClearanceInboxItem[]; onAction: (row: ClearanceInboxItem, action: 'clear' | 'block') => Promise<void>; onAssetAction: (row: ClearanceInboxItem, status: 'Returned' | 'Lost') => Promise<void> }) {
  return <Card title="Assigned tasks"><div className="table-wrap"><table className="data-table"><thead><tr><th>Employee</th><th>Task</th><th>Category</th><th>Status</th><th>Due</th><th>Action</th></tr></thead><tbody>{rows.map(row => <tr key={row.taskId}><td>{row.employeeName}</td><td>{row.taskName}</td><td>{row.category}</td><td>{row.status}{row.isOverdue ? ' · overdue' : ''}</td><td>{row.dueDate ?? '—'}</td><td><button type="button" onClick={() => void onAction(row, 'clear')}>Clear</button><button type="button" onClick={() => void onAction(row, 'block')}>Block</button>{row.assetRequired && row.assetId && row.assetStatus === 'PendingReturn' ? <><button type="button" onClick={() => void onAssetAction(row, 'Returned')}>Asset returned</button><button type="button" onClick={() => void onAssetAction(row, 'Lost')}>Asset lost</button></> : null}</td></tr>)}</tbody></table></div></Card>
}

function Dashboard({ rows }: { rows: ClearanceDashboardItem[] }) {
  return <Card title="Clearance readiness"><div className="table-wrap"><table className="data-table"><thead><tr><th>Employee</th><th>LWD</th><th>Progress</th><th>Pending</th><th>Blocked</th><th>Assets</th><th>Overdue</th><th>Ready</th></tr></thead><tbody>{rows.map(row => <tr key={row.clearanceId}><td>{row.employeeName}</td><td>{row.approvedLastWorkingDate ?? '—'}</td><td>{row.mandatoryCompletedCount}/{row.mandatoryTaskCount}</td><td>{row.pendingCount}</td><td>{row.blockedCount}</td><td>{row.pendingAssetCount}</td><td>{row.overdueCount}</td><td>{row.readyForExit ? 'Yes' : 'No'}</td></tr>)}</tbody></table></div></Card>
}
