import { listEmployees } from '../../api/employees.ts'
import type { EmployeeListItem } from '../../api/types.ts'
import { StatusBadge } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { DataTable, type Column } from '../../components/DataTable.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { formatDate } from '../../lib/format.ts'

const RECENT_COUNT = 6

const columns: readonly Column<EmployeeListItem>[] = [
  {
    key: 'employee',
    header: 'Employee',
    render: (row) => (
      <div className="cell-stack">
        <span className="cell-primary">{row.fullName}</span>
        <span className="cell-secondary cell-code">{row.employeeCode}</span>
      </div>
    ),
  },
  { key: 'department', header: 'Department', render: (row) => row.departmentName, secondary: true },
  {
    key: 'designation',
    header: 'Designation',
    render: (row) => row.designationName,
    secondary: true,
  },
  { key: 'joined', header: 'Joined', render: (row) => formatDate(row.dateOfJoining) },
  {
    key: 'status',
    header: 'Status',
    align: 'end',
    render: (row) => <StatusBadge status={row.status} />,
  },
]

/**
 * The most recent joiners.
 *
 * Sorted by the API, not here: `dateOfJoining` is one of the sort fields `EmployeeQuery` accepts, and
 * sorting a page that has already been cut to six rows would only reorder those six.
 */
export function RecentHiresCard() {
  const { data, error, isLoading, isRefreshing, refetch } = useApiQuery(
    (signal) =>
      listEmployees(
        { pageSize: RECENT_COUNT, sortBy: 'dateOfJoining', sortDescending: true },
        signal,
      ),
    [],
  )

  return (
    <Card
      title="Recent hires"
      subtitle={`Latest ${RECENT_COUNT} by joining date`}
      isRefreshing={isRefreshing}
    >
      <DataTable
        caption="Most recently joined employees"
        columns={columns}
        rows={data?.items ?? null}
        rowKey={(row) => row.id}
        isLoading={isLoading}
        error={error}
        onRetry={refetch}
        emptyTitle="No employees yet"
        emptyMessage="Once employees are added they will appear here, newest first."
      />
    </Card>
  )
}
