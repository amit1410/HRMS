import { listDepartments } from '../api/departments.ts'
import { listDesignations } from '../api/designations.ts'
import { listEmployees } from '../api/employees.ts'
import { Permissions } from '../auth/permissions.ts'
import { useAuth } from '../auth/useAuth.ts'
import { Card } from '../components/Card.tsx'
import { EmptyState } from '../components/EmptyState.tsx'
import { PageHeader } from '../components/PageHeader.tsx'
import { useDocumentTitle } from '../hooks/useDocumentTitle.ts'
import { CountTile } from './dashboard/CountTile.tsx'
import { ExportEmployeesButton } from './dashboard/ExportEmployeesButton.tsx'
import { HeadcountByDepartmentCard } from './dashboard/HeadcountByDepartmentCard.tsx'
import { RecentHiresCard } from './dashboard/RecentHiresCard.tsx'

/**
 * The landing screen: the shape of the tenant at a glance.
 *
 * Every panel is gated on the permission its endpoint requires, and the gate decides whether the panel
 * is *rendered* — not whether it is greyed out. That matters because rendering is what fires the
 * request: a Manager, who holds only the three view permissions, must not have an export request sent on
 * their behalf just so the button can be disabled afterwards.
 *
 * Nothing here is tenant-parameterised. Each request carries the access token and the API derives the
 * tenant from it, so there is no tenant id in this file — and no way for one to be tampered with.
 */
export function DashboardPage() {
  useDocumentTitle('Dashboard')

  const { user, can } = useAuth()

  const canViewEmployees = can(Permissions.employee.view)
  const canViewDepartments = can(Permissions.department.view)
  const canViewDesignations = can(Permissions.designation.view)
  const canExport = can(Permissions.employee.export)
  const hasAnything = canViewEmployees || canViewDepartments || canViewDesignations

  return (
    <>
      <PageHeader
        title="Dashboard"
        subtitle={user ? `${user.tenantName} · signed in as ${user.firstName}` : ''}
        actions={canExport ? <ExportEmployeesButton /> : undefined}
      />

      {!hasAnything ? (
        <Card>
          <EmptyState
            title="Nothing to show yet"
            message={
              'Your roles do not yet include permission to view employees, departments or designations. ' +
              'An administrator can grant them.'
            }
          />
        </Card>
      ) : (
        <>
          <div className="stat-grid">
            {canViewEmployees && (
              <>
                <CountTile
                  label="Employees"
                  hint="All records, any status"
                  load={(signal) => listEmployees({ pageSize: 1 }, signal)}
                />
                <CountTile
                  label="Active"
                  hint="Currently employed"
                  load={(signal) => listEmployees({ pageSize: 1, status: 'Active' }, signal)}
                />
              </>
            )}
            {canViewDepartments && (
              <CountTile
                label="Departments"
                hint="Active only"
                load={(signal) => listDepartments({ pageSize: 1, isActive: true }, signal)}
              />
            )}
            {canViewDesignations && (
              <CountTile
                label="Designations"
                hint="Active only"
                load={(signal) => listDesignations({ pageSize: 1, isActive: true }, signal)}
              />
            )}
          </div>

          <div className="dashboard-grid">
            {canViewEmployees && <RecentHiresCard />}
            {canViewDepartments && <HeadcountByDepartmentCard />}
          </div>
        </>
      )}
    </>
  )
}
