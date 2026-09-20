import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { getPayrollConfigurationHealth } from '../../api/payroll.ts'

export function PayrollConfigurationHealthPage() {
  const query = useApiQuery((signal) => getPayrollConfigurationHealth(signal), [])
  return <section className="page-shell"><PageHeader title="Payroll Configuration Health" subtitle="Review configuration and employee-readiness blockers before payroll runs." />{query.error ? <p role="alert">{query.error.message}</p> : null}{query.isLoading ? <p>Loading configuration health…</p> : <div className="form-grid">{query.data?.categories.map(category => <Card key={category.category} title={category.category}><p><strong>{category.status}</strong> · {category.issueCount} issue(s) · {category.blockingCount} blocking</p>{category.issues.length === 0 ? <p className="field-help">No issues detected.</p> : <div className="table-wrap"><table className="data-table"><thead><tr><th>Severity</th><th>Code</th><th>Message</th><th>Entity</th></tr></thead><tbody>{category.issues.map(issue => <tr key={`${issue.code}-${issue.entityId ?? 'none'}`}><td>{issue.severity}</td><td>{issue.code}</td><td>{issue.message}</td><td>{issue.entityType ?? '—'}</td></tr>)}</tbody></table></div>}</Card>)}</div>}</section>
}
