import { useEffect, useState } from 'react'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { getMyCompOffBalance, getMyCompOffEarnings, getMyCompOffLedger, type CompOffBalance, type CompOffEarning, type CompOffLedgerEntry } from '../../api/compOff.ts'

export function CompOffPage() {
  const { can } = useAuth()
  const [balance, setBalance] = useState<CompOffBalance>()
  const [earnings, setEarnings] = useState<CompOffEarning[]>([])
  const [ledger, setLedger] = useState<CompOffLedgerEntry[]>([])
  const [error, setError] = useState<string>()
  useEffect(() => { let active = true; Promise.all([getMyCompOffBalance(), getMyCompOffEarnings(), getMyCompOffLedger()]).then(([b, e, l]) => { if (!active) return; setBalance(b); setEarnings(e); setLedger(l) }).catch(e => { if (active) setError(e instanceof Error ? e.message : 'Unable to load Comp-Off balance.') }); return () => { active = false } }, [])
  const expiringSoon = earnings.filter(x => x.expiresOn && x.status === 'Approved').length
  return <section className="page-shell"><PageHeader title="My Comp-Off" subtitle="Entitlement is derived from finalized Attendance and consumed through the existing Leave workflow." />{error ? <Notice tone="error">{error}</Notice> : null}<Card title="Available balance"><div className="detail-list"><div><strong>Available</strong><span>{balance?.availableMinutes ?? 0} minutes</span></div><div><strong>Reserved</strong><span>{balance?.reservedMinutes ?? 0} minutes</span></div><div><strong>Consumed / Used</strong><span>{balance?.consumedMinutes ?? 0} minutes</span></div><div><strong>Expired</strong><span>{balance?.expiredMinutes ?? 0} minutes</span></div><div><strong>Expiring soon</strong><span>{expiringSoon} earning bucket(s)</span></div></div><p className="muted">Comp-Off Leave is requested from the existing Leave screen using this server-calculated entitlement.</p></Card>{can(Permissions.attendance.compOffViewSelf) ? <Card title="Earnings"><table className="data-table"><thead><tr><th>Work date</th><th>Source</th><th>Eligible work</th><th>Credit</th><th>Expiry</th><th>Attendance version</th><th>Status</th></tr></thead><tbody>{earnings.map(x => <tr key={x.id}><td>{x.sourceWorkDate}</td><td>{x.sourceType}</td><td>{x.eligibleMinutes}</td><td>{x.creditedMinutes}</td><td>{x.expiresOn ?? '—'}</td><td>{x.sourceAttendanceVersion}</td><td>{x.correctionStatus ? `${x.status} · ${x.correctionStatus}` : x.status}</td></tr>)}</tbody></table></Card> : null}<Card title="Ledger history"><table className="data-table"><thead><tr><th>Date</th><th>Entry</th><th>Minutes</th><th>Source</th><th>Leave request</th></tr></thead><tbody>{ledger.map(x => <tr key={x.id}><td>{x.effectiveDate}</td><td>{x.entryType}</td><td>{x.minutes}</td><td>{x.sourceReference}</td><td>{x.leaveRequestId ?? '—'}</td></tr>)}</tbody></table></Card></section>
}
