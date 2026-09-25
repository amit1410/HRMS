import { useEffect, useState } from 'react'
import { approveCompOffEarning, getCompOffOperations, rejectCompOffEarning, type CompOffOperationalEarning } from '../../api/compOff.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'

export function CompOffOperationsPage() {
  const { can, canAny } = useAuth()
  const [items, setItems] = useState<CompOffOperationalEarning[]>([])
  const [page, setPage] = useState(1)
  const [totalPages, setTotalPages] = useState(1)
  const [error, setError] = useState<string>()
  const [message, setMessage] = useState<string>()
  const [busy, setBusy] = useState<string>()
  const canOperate = canAny([Permissions.attendance.compOffViewTeam, Permissions.attendance.compOffViewAll, Permissions.attendance.compOffManage, Permissions.attendance.compOffApprove])

  async function load() {
    try {
      setError(undefined)
      const result = await getCompOffOperations({ page, pageSize: 100 })
      setItems(result.items)
      setTotalPages(result.totalPages)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to load Comp-Off operations.')
    }
  }

  useEffect(() => { void load() }, [page])

  async function decide(item: CompOffOperationalEarning, action: 'approve' | 'reject') {
    try {
      setBusy(item.earningId)
      setError(undefined)
      if (action === 'approve') await approveCompOffEarning(item.earningId)
      else await rejectCompOffEarning(item.earningId)
      setMessage(`Comp-Off credit ${action}d.`)
      await load()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to update Comp-Off credit.')
    } finally {
      setBusy(undefined)
    }
  }

  if (!canOperate) return <section className="page-shell"><PageHeader title="Comp-Off Operations" subtitle="Backend authorization is required for operational access." /><Notice tone="error">You do not have access to Comp-Off operations.</Notice></section>

  const pending = items.filter(x => x.status === 'PendingApproval')
  return <section className="page-shell">
    <PageHeader title="Comp-Off Operations" subtitle="Server-authorized, scoped and paged credit review." />
    {message ? <Notice tone="success">{message}</Notice> : null}
    {error ? <Notice tone="error">{error}</Notice> : null}
    <div className="detail-list"><div><strong>Pending credits</strong><span>{pending.length}</span></div><div><strong>Approved credits</strong><span>{items.filter(x => x.status === 'Approved').length}</span></div><div><strong>Expired credits</strong><span>{items.filter(x => x.status === 'Expired').length}</span></div><div><strong>Corrections</strong><span>{items.filter(x => x.correctionStatus).length}</span></div></div>
    <Card title="Pending Credits"><div className="table-wrap"><table className="data-table"><thead><tr><th>Employee</th><th>Work date</th><th>Source</th><th>Eligible</th><th>Credit</th><th>Expiry</th><th>Status</th><th>Action</th></tr></thead><tbody>{pending.map(item => <tr key={item.earningId}><td>{item.employeeName} ({item.employeeCode})</td><td>{item.workDate}</td><td>{item.sourceType}</td><td>{item.eligibleWorkedMinutes}</td><td>{item.creditedMinutes}</td><td>{item.expiryDate ?? '—'}</td><td>{item.status}</td><td>{can(Permissions.attendance.compOffApprove) ? <><button className="button button-link" disabled={busy === item.earningId} onClick={() => void decide(item, 'approve')}>Approve</button><button className="button button-link" disabled={busy === item.earningId} onClick={() => void decide(item, 'reject')}>Reject</button></> : 'Read-only'}</td></tr>)}</tbody></table>{pending.length === 0 ? <p className="muted">No pending credits in the authorized scope.</p> : null}</div></Card>
    <Card title="Balance and correction detail"><div className="table-wrap"><table className="data-table"><thead><tr><th>Employee</th><th>Source date</th><th>Attendance version</th><th>Policy version</th><th>Original credit</th><th>Available</th><th>Reserved</th><th>Consumed</th><th>Expiry</th><th>Correction</th></tr></thead><tbody>{items.map(item => <tr key={item.earningId}><td>{item.employeeName}</td><td>{item.workDate}</td><td>{item.attendanceVersion}</td><td>{item.policyVersion}</td><td>{item.creditedMinutes}</td><td>{item.availableMinutes}</td><td>{item.reservedMinutes}</td><td>{item.consumedMinutes}</td><td>{item.expiryDate ?? '—'}</td><td>{item.correctionStatus ? `${item.correctionStatus}${item.correctionDeficitMinutes ? ` (${item.correctionDeficitMinutes} min)` : ''}` : 'None'}</td></tr>)}</tbody></table></div></Card>
    <div className="page-actions"><button className="button button-secondary" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</button><span>Page {page} of {totalPages}</span><button className="button button-secondary" disabled={page >= totalPages} onClick={() => setPage(value => value + 1)}>Next</button></div>
  </section>
}
