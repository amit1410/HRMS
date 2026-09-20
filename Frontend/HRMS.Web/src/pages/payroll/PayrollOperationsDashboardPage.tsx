import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { getPayrollOperationsDashboard } from '../../api/payroll.ts'

export function PayrollOperationsDashboardPage() {
  const query = useApiQuery((signal) => getPayrollOperationsDashboard(signal), [])
  const cards = query.data ? Object.entries(query.data) : []
  return <section className="page-shell"><PageHeader title="Payroll Operations" subtitle="Operational queues and controls across the payroll lifecycle." />{query.error ? <p role="alert">{query.error.message}</p> : null}{query.isLoading ? <p>Loading payroll operations…</p> : <div className="summary-grid">{cards.map(([key, value]) => <Card key={key} title={key.replace(/[A-Z]/g, letter => ` ${letter}`).replace(/^./, letter => letter.toUpperCase())}><strong>{value}</strong></Card>)}</div>}</section>
}
