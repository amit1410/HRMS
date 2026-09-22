import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { getPayrollAnalyticsOverview } from '../../api/payrollAnalytics.ts'

export function PayrollAnalyticsPage() {
  const runId = new URLSearchParams(window.location.search).get('runId')
  const query = useApiQuery((signal) => runId ? getPayrollAnalyticsOverview(runId, signal) : Promise.reject(new Error('Select a payroll run to view analytics.')), [runId])
  return <section className="page-shell"><PageHeader title="Payroll Analytics & Controls" subtitle="Persisted payroll evidence, variance signals, and reconciliation workflow." />{query.error ? <p role="alert">{query.error.message}</p> : null}{query.data ? <div className="summary-grid">{[['Employees', query.data.employeeCount], ['Gross', query.data.grossTotal], ['Deductions', query.data.deductionTotal], ['Net pay', query.data.netPayTotal], ['Open findings', query.data.openFindings], ['Critical findings', query.data.criticalFindings], ['Anomalies', query.data.anomalyCount]].map(([title, value]) => <Card key={String(title)} title={String(title)}><strong>{value}</strong></Card>)}</div> : <p>Choose a payroll run with <code>?runId=...</code> to inspect its persisted analytics.</p>}</section>
}
