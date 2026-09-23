import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { getPayrollIntegrity, getPayrollProductionHealth } from '../../api/payroll.ts'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

export function PayrollProductionHealthPage() {
  const health = useApiQuery((signal) => getPayrollProductionHealth(signal), [])
  const integrity = useApiQuery((signal) => getPayrollIntegrity(signal), [])
  return <section className="page-shell"><PageHeader title="Payroll Production Readiness" subtitle="Read-only health and integrity checks for safe payroll operations." />
    {health.error ? <p role="alert">{health.error.message}</p> : null}
    {health.isLoading ? <p>Loading payroll health...</p> : <Card title={`Overall status: ${health.data?.status ?? 'Unknown'}`}><p>{health.data?.issues.length ?? 0} configuration issue(s) detected.</p>{health.data?.issues.map(issue => <p key={`${issue.code}-${issue.entityId ?? 'tenant'}`}><strong>{issue.severity}</strong> · {issue.code}: {issue.message}</p>)}</Card>}
    {integrity.error ? <p role="alert">{integrity.error.message}</p> : null}
    {integrity.isLoading ? <p>Loading integrity checks...</p> : <Card title={`Data integrity: ${integrity.data?.status ?? 'Unknown'}`}><div className="table-wrap"><table className="data-table"><thead><tr><th>Check</th><th>Status</th><th>Count</th><th>Message</th></tr></thead><tbody>{integrity.data?.checks.map(check => <tr key={check.code}><td>{check.code}</td><td>{check.status}</td><td>{check.count}</td><td>{check.message}</td></tr>)}</tbody></table></div></Card>}
  </section>
}
