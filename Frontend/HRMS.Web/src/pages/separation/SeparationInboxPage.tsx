import { useState } from 'react'
import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { getHrSeparationInbox, getManagerSeparationInbox, hrApproveSeparation, hrRejectSeparation, managerApproveSeparation, managerRejectSeparation, reviseSeparationLwd, type SeparationCase } from '../../api/separation.ts'

export function SeparationInboxPage({ kind }: { kind: 'manager' | 'hr' }) {
  const [refresh, setRefresh] = useState(0)
  const query = useApiQuery(signal => kind === 'manager' ? getManagerSeparationInbox(signal) : getHrSeparationInbox(signal), [kind, refresh])
  const act = async (operation: (item: SeparationCase) => Promise<unknown>, item: SeparationCase) => { await operation(item); setRefresh(value => value + 1) }
  return <section className="page-shell"><PageHeader title={kind === 'manager' ? 'Separation Manager Inbox' : 'Separation HR Inbox'} subtitle="Review separation requests within your authorized scope." />
    {query.error ? <Card title="Unable to load inbox"><p>{query.error.message}</p></Card> : null}
    <div className="card-grid">{(query.data ?? []).map(item => <Card key={item.id} title={`${item.separationNumber} · ${item.status}`}>
      <p>Employee: {item.employeeId}</p><p>Requested LWD: {item.proposedLastWorkingDate}</p><p>Reason: {item.reasonName}</p>
      {kind === 'manager' ? <div><button type="button" onClick={() => void act(x => managerApproveSeparation(x.id), item)}>Approve</button><button type="button" onClick={() => { const reason = window.prompt('Rejection reason') ?? ''; if (reason) void act(x => managerRejectSeparation(x.id, reason), item) }}>Reject</button></div> : <div><button type="button" onClick={() => { const date = window.prompt('Approved last working date', item.proposedLastWorkingDate) ?? ''; const reason = window.prompt('Reason for revision') ?? ''; if (date && reason) void act(x => reviseSeparationLwd(x.id, date, reason), item) }}>Revise LWD</button><button type="button" onClick={() => void act(x => hrApproveSeparation(x.id), item)}>Approve</button><button type="button" onClick={() => { const reason = window.prompt('Rejection reason') ?? ''; if (reason) void act(x => hrRejectSeparation(x.id, reason), item) }}>Reject</button></div>}
    </Card>)}</div>
  </section>
}
