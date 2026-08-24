import { listDepartments } from '../../api/departments.ts'
import { Card } from '../../components/Card.tsx'
import { EmptyState } from '../../components/EmptyState.tsx'
import { ErrorState } from '../../components/ErrorState.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { formatNumber } from '../../lib/format.ts'

const TOP_N = 6

/**
 * Headcount split by department, as proportional bars.
 *
 * `employeeCount` comes down on the department DTO already — computed server-side in the projection —
 * so this needs one request rather than one per department. Bars are scaled against the largest
 * department rather than the total, which keeps small teams visible.
 */
export function HeadcountByDepartmentCard() {
  const { data, error, isLoading, isRefreshing, refetch } = useApiQuery(
    (signal) =>
      listDepartments(
        { pageSize: 100, sortBy: 'employeeCount', sortDescending: true, isActive: true },
        signal,
      ),
    [],
  )

  const departments = (data?.items ?? []).slice(0, TOP_N)
  const largest = Math.max(1, ...departments.map((department) => department.employeeCount))
  const hidden = (data?.items.length ?? 0) - departments.length

  return (
    <Card
      title="Headcount by department"
      subtitle="Active departments"
      isRefreshing={isRefreshing}
    >
      {error ? (
        <ErrorState error={error} onRetry={refetch} />
      ) : isLoading ? (
        <div className="table-loading">
          <Spinner label="Loading…" />
        </div>
      ) : departments.length === 0 ? (
        <EmptyState
          title="No active departments"
          message="Departments appear here as soon as one is active."
        />
      ) : (
        <>
          <ul className="bar-list">
            {departments.map((department) => (
              <li key={department.id} className="bar-row">
                <span className="bar-label" title={department.name}>
                  {department.name}
                </span>
                <span className="bar-track">
                  <span
                    className="bar-fill"
                    style={{ width: `${(department.employeeCount / largest) * 100}%` }}
                  />
                </span>
                <span className="bar-value">{formatNumber(department.employeeCount)}</span>
              </li>
            ))}
          </ul>
          {hidden > 0 && (
            <p className="card-note">
              {hidden} more {hidden === 1 ? 'department' : 'departments'} not shown.
            </p>
          )}
        </>
      )}
    </Card>
  )
}
