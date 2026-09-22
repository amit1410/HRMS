import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { getPayrollReportsDashboard, getPayrollRegister } from '../../api/payrollReports.ts'

export function PayrollReportsPage() {
  const dashboard = useApiQuery((signal) => getPayrollReportsDashboard({}, signal), [])
  const register = useApiQuery((signal) => getPayrollRegister({ page: 1, pageSize: 25 }, signal), [])
  return <section className="page-shell"><PageHeader title="Payroll Reports & Insights" subtitle="Read-only management reporting over authoritative payroll evidence." />{dashboard.error ? <p role="alert">{dashboard.error.message}</p> : null}{dashboard.data ? <div className="summary-grid">{[['Employees paid', dashboard.data.employeeCount], ['Gross', dashboard.data.grossTotal], ['Deductions', dashboard.data.deductionTotal], ['Net pay', dashboard.data.netPayTotal], ['Employer contributions', dashboard.data.employerContributionTotal], ['Exceptions', dashboard.data.exceptionCount]].map(([title, value]) => <Card key={String(title)} title={String(title)}><strong>{value}</strong></Card>)}</div> : <p>Loading dashboard…</p>}<Card title="Payroll Register"><div className="table-scroll"><table><thead><tr><th>Employee</th><th>Period</th><th>Department</th><th>Cost center</th><th>Gross</th><th>Deductions</th><th>Net</th></tr></thead><tbody>{register.data?.items.map((row) => <tr key={row.payrollResultId}><td>{row.employeeCode} — {row.employeeName}</td><td>{row.periodEndDate}</td><td>{row.department}</td><td>{row.costCenter}</td><td>{row.grossTotal}</td><td>{row.deductionTotal}</td><td>{row.netPay}</td></tr>)}</tbody></table></div>{register.error ? <p role="alert">{register.error.message}</p> : null}{register.data && register.data.items.length === 0 ? <p>No persisted payroll results match the current report filters.</p> : null}</Card></section>
}
