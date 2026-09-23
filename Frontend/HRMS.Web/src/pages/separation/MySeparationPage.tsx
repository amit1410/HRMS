import { useState } from 'react'
import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { createMySeparation, getMySeparation, getSeparationHistory, listSeparationReasons, submitSeparation, withdrawSeparation } from '../../api/separation.ts'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

export function MySeparationPage() {
  const { can } = useAuth(); const [refresh, setRefresh] = useState(0); const [reasonId, setReasonId] = useState(''); const [lwd, setLwd] = useState(''); const [remarks, setRemarks] = useState('')
  const reasons = useApiQuery(signal => listSeparationReasons(signal), []); const current = useApiQuery(signal => getMySeparation(signal), [refresh]); const history = useApiQuery(signal => current.data ? getSeparationHistory(current.data.id, signal) : Promise.resolve([]), [refresh, current.data?.id]); const activeReasons = reasons.data?.filter(reason => reason.employeeInitiatedAllowed && reason.isActive) ?? []
  const reload = () => setRefresh(value => value + 1)
  const save = async () => { if (reasonId && lwd) { await createMySeparation({ reasonId, requestDate: new Date().toISOString().slice(0, 10), proposedLastWorkingDate: lwd, remarks }); reload() } }
  const action = async (operation: (id: string) => Promise<unknown>) => { if (current.data) { await operation(current.data.id); reload() } }
  return <section className="page-shell"><PageHeader title="My Separation / Resignation" subtitle="Submit a resignation request. Requested and approved last working dates are shown separately." />
    {current.error ? <Card title="Start a request"><div className="form-grid"><label>Reason <select value={reasonId} onChange={event => setReasonId(event.target.value)}><option value="">Select reason</option>{activeReasons.map(reason => <option key={reason.id} value={reason.id}>{reason.name}</option>)}</select></label><label>Requested last working date <input type="date" value={lwd} onChange={event => setLwd(event.target.value)} /></label><label>Remarks <textarea value={remarks} onChange={event => setRemarks(event.target.value)} /></label><button type="button" onClick={() => void save()} disabled={!can(Permissions.separation.createSelf)}>Save draft</button></div></Card> : null}
    {current.data ? <Card title={`Status: ${current.data.status}`}><p>Requested LWD: {current.data.proposedLastWorkingDate}</p><p>Approved LWD: {current.data.approvedLastWorkingDate ?? 'Not confirmed'}</p><p>Notice period: {current.data.noticeStartDate ?? 'Not active'} to {current.data.noticeEndDate ?? 'Not active'}</p><p>Reason: {current.data.reasonName}</p><div><button type="button" onClick={() => void action(submitSeparation)} disabled={current.data.status !== 'Draft' || !can(Permissions.separation.submit)}>Submit</button><button type="button" onClick={() => void action(withdrawSeparation)} disabled={!['Draft', 'Submitted'].includes(current.data.status) || !can(Permissions.separation.withdraw)}>Withdraw</button></div></Card> : null}
    {history.data?.[0] ? <Card title="Timeline"><ol>{history.data.map(event => <li key={event.id}>{event.eventType} — {new Date(event.occurredAtUtc).toLocaleString()}</li>)}</ol></Card> : null}
  </section>
}
