import { Link } from 'react-router-dom'
import { listEmployees } from '../../api/employees.ts'
import type { EmployeeListItem } from '../../api/types.ts'
import { StatusBadge } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { DataTable, type Column } from '../../components/DataTable.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { formatDate, initials } from '../../lib/format.ts'

const RECENT_COUNT = 5
const columns: readonly Column<EmployeeListItem>[] = [
  { key: 'employee', header: 'Employee', render: (row) => <div className="employee-table-person"><span className="employee-table-avatar" aria-hidden="true">{initials(row.fullName)}</span><Link className="cell-primary" to={`/employees/${row.id}`}>{row.fullName}</Link></div> },
  { key: 'code', header: 'Employee code', render: (row) => <span className="employee-table-code">{row.employeeCode || 'Pending assignment'}</span> },
  { key: 'department', header: 'Department', render: (row) => row.departmentName || '—', secondary: true },
  { key: 'designation', header: 'Designation', render: (row) => row.designationName || '—', secondary: true },
  { key: 'joined', header: 'Joining date', render: (row) => formatDate(row.dateOfJoining) },
  { key: 'status', header: 'Status', align: 'end', render: (row) => <StatusBadge status={row.status} /> },
]

export function RecentHiresCard() {
  const { data, error, isLoading, isRefreshing, refetch } = useApiQuery((signal) => listEmployees({ pageSize: RECENT_COUNT, sortBy: 'dateOfJoining', sortDescending: true }, signal), [])
  return <Card title="Recent employees" subtitle={`Latest ${RECENT_COUNT} by joining date`} isRefreshing={isRefreshing}>
    <DataTable caption="Most recently joined employees" columns={columns} rows={data?.items ?? null} rowKey={(row) => row.id} isLoading={isLoading} error={error} onRetry={refetch} emptyTitle="No employees yet" emptyMessage="Once employees are added they will appear here, newest first." />
    {data && data.totalCount > RECENT_COUNT && <div className="recent-table-footer"><span>Showing {RECENT_COUNT} most recent employees</span><Link to="/employees">View all</Link></div>}
  </Card>
}
