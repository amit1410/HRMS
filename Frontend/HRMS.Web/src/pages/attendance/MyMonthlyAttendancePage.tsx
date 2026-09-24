import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { Card } from '../../components/Card.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { getMyMonthlyAttendanceSummary } from '../../api/attendance.ts'

export function MyMonthlyAttendancePage() {
  const query = useApiQuery(() => getMyMonthlyAttendanceSummary({ page: 1, pageSize: 50 }), [])
  return <section className="page-shell"><PageHeader title="My Monthly Attendance" subtitle="Your finalized monthly Attendance summary and Payroll-facing day values." /><Card title="Monthly summary" isRefreshing={query.isLoading}>{query.error ? <p role="alert">{query.error.message}</p> : null}{query.data?.items.length === 0 ? <p>No monthly Attendance summaries are available.</p> : <div className="table-wrap"><table className="data-table"><thead><tr><th>Period</th><th>Status source</th><th>Present</th><th>Leave</th><th>Holiday</th><th>Week off</th><th>On duty</th><th>Absent</th><th>LOP</th><th>Payable</th><th>Version</th></tr></thead><tbody>{query.data?.items.map(row => <tr key={row.id}><td>{row.processedAtUtc.slice(0, 7)}</td><td>Finalized</td><td>{row.presentDayQuantity.toFixed(2)}</td><td>{(row.paidLeaveDays + row.unpaidLeaveDays).toFixed(2)}</td><td>{row.holidayDays}</td><td>{row.weeklyOffDays}</td><td>{row.onDutyDays}</td><td>{row.absentDays}</td><td>{row.lopDays.toFixed(2)}</td><td>{row.payableDays.toFixed(2)}</td><td>{row.version}</td></tr>)}</tbody></table></div>}</Card></section>
}
