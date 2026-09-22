import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { listMyPayrollAdjustments } from '../../api/payrollAdjustments.ts'

export function MyPayrollAdjustmentsPage() {
  const rows = useApiQuery(signal => listMyPayrollAdjustments({ page: 1, pageSize: 50 }, signal), [])
  return <section className="page-shell"><PageHeader title="My Payroll Adjustments" subtitle="View your applied payroll corrections and off-cycle adjustment status." /><Card title="Adjustment history"><div className="table-wrap"><table className="data-table"><thead><tr><th>Number</th><th>Effective date</th><th>Direction</th><th>Amount</th><th>Applied</th><th>Status</th></tr></thead><tbody>{rows.data?.items.map(row => <tr key={row.id}><td>{row.adjustmentNumber}</td><td>{row.effectiveDate}</td><td>{row.direction}</td><td>{row.amount.toFixed(2)}</td><td>{row.appliedAmount.toFixed(2)}</td><td>{row.status}</td></tr>)}</tbody></table></div></Card></section>
}
