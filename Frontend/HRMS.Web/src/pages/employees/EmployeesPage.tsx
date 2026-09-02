import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { deleteEmployee, listEmployees } from '../../api/employees.ts'
import {
  EMPLOYEE_STATUSES,
  SORT_FIELDS,
  type EmployeeListItem,
  type EmployeeQuery,
  type EmployeeStatus,
} from '../../api/types.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Card } from '../../components/Card.tsx'
import { ConfirmDialog } from '../../components/ConfirmDialog.tsx'
import { DataTable, type Column } from '../../components/DataTable.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Pagination } from '../../components/Pagination.tsx'
import { StatusBadge } from '../../components/Badge.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'
import { useFlash } from '../../hooks/useFlash.ts'
import { useListQuery } from '../../hooks/useListQuery.ts'
import { formatDate } from '../../lib/format.ts'
import { ExportEmployeesButton } from '../dashboard/ExportEmployeesButton.tsx'
import { EMPLOYEES_PATH } from './personalDetailsValues.ts'
import {
  loadDepartmentOptions,
  loadDesignationOptions,
  type ReferenceOptions,
} from './referenceOptions.ts'

const FILTER_KEYS = ['departmentId', 'designationId', 'status'] as const

/** Nothing loaded, and nothing requested — what the two reference lists are without permission to read them. */
const NO_OPTIONS: ReferenceOptions = { options: [], total: 0 }

/**
 * The employee directory.
 *
 * Paging, searching, sorting and all three filters are the API's work. The screen holds them in the URL, so
 * a filtered view is a link that can be sent to someone, Back steps through what the user actually did, and
 * returning from the form lands on the page they left rather than an unfiltered page one.
 *
 * The department and designation filters are only *rendered* for someone who holds the matching View
 * permission, and that is the whole enforcement: rendering the select is what fires the request that fills
 * it. A Manager holds `Employee.View` and neither `Department.View` nor `Designation.View`, so for them
 * those two selects do not exist — rather than existing, failing with a 403, and showing an error for
 * something they were never meant to use. Row actions work the same way: an action they cannot perform is
 * absent, not disabled, because a disabled Delete still claims the action exists.
 */
export function EmployeesPage() {
  useDocumentTitle('Employees')

  const { can } = useAuth()
  const flash = useFlash()
  const location = useLocation()

  const canCreate = can(Permissions.employee.create)
  const canDelete = can(Permissions.employee.delete)
  const canExport = can(Permissions.employee.export)
  const canViewDepartments = can(Permissions.department.view)
  const canViewDesignations = can(Permissions.designation.view)

  const list = useListQuery({
    sortFields: SORT_FIELDS.employees,
    // First name: the column a person scans when they are looking for someone by name.
    defaultSortBy: 'firstName',
    filterKeys: FILTER_KEYS,
  })

  // The filter values, narrowed to what the API accepts. `''` means the filter is off, and `cleanParams`
  // drops it before the request rather than sending an empty parameter.
  const filterQuery: EmployeeQuery = {
    departmentId: list.filters.departmentId || undefined,
    designationId: list.filters.designationId || undefined,
    status: asStatus(list.filters.status),
  }

  const { data, error, isLoading, isRefreshing, refetch } = useApiQuery(
    (signal) => listEmployees({ ...list.pagedQuery, ...filterQuery }, signal),
    [list.key],
  )

  // Not permitted means not requested: the fetcher resolves to an empty list without a call, so the
  // absence of the select and the absence of the 403 are the same decision.
  const departments = useApiQuery(
    (signal) => (canViewDepartments ? loadDepartmentOptions({}, signal) : Promise.resolve(NO_OPTIONS)),
    [canViewDepartments],
  )
  const designations = useApiQuery(
    (signal) =>
      canViewDesignations ? loadDesignationOptions({}, signal) : Promise.resolve(NO_OPTIONS),
    [canViewDesignations],
  )

  const [pendingDelete, setPendingDelete] = useState<EmployeeListItem | null>(null)

  const returnState = { from: `${location.pathname}${location.search}` }

  async function confirmDelete(employee: EmployeeListItem): Promise<void> {
    await deleteEmployee(employee.id)
    flash.show(`${employee.fullName} was deleted.`)

    // Last row on this page: staying would show an empty table for a result set that still has rows.
    if (data?.items.length === 1 && list.page > 1) {
      list.setPage(list.page - 1)
    } else {
      refetch()
    }
  }

  const columns: readonly Column<EmployeeListItem>[] = [
    {
      key: 'employeeCode',
      header: 'Code',
      sortBy: 'employeeCode',
      render: (row) => <span className="cell-code">{row.employeeCode}</span>,
    },
    {
      key: 'name',
      header: 'Name',
      // The API sorts employees by first name; there is no `fullName` to sort on, because it is composed
      // in the projection rather than stored.
      sortBy: 'firstName',
      render: (row) => (
        <div className="cell-stack">
          <Link className="cell-primary" to={`${EMPLOYEES_PATH}/${row.id}`}>
            {row.fullName}
          </Link>
          <span className="cell-secondary">{row.email}</span>
        </div>
      ),
    },
    {
      key: 'departmentName',
      header: 'Department',
      // `department`, not `departmentName`: the query validator's whitelist names the navigation property.
      sortBy: 'department',
      secondary: true,
      render: (row) => row.departmentName,
    },
    {
      key: 'designationName',
      header: 'Designation',
      sortBy: 'designation',
      secondary: true,
      render: (row) => row.designationName,
    },
    {
      key: 'dateOfJoining',
      header: 'Joined',
      sortBy: 'dateOfJoining',
      secondary: true,
      render: (row) => formatDate(row.dateOfJoining),
    },
    {
      key: 'status',
      header: 'Status',
      sortBy: 'status',
      align: 'end',
      render: (row) => <StatusBadge status={row.status} />,
    },
    {
      key: 'actions',
      header: <span className="sr-only">Actions</span>,
      align: 'end',
      render: (row) => (
        <div className="row-actions">
          {canDelete && (
            <button
              type="button"
              className="row-action row-action-danger"
              onClick={() => setPendingDelete(row)}
            >
              Delete
            </button>
          )}
        </div>
      ),
    },
  ]

  return (
    <>
      <PageHeader
        title="Employees"
        subtitle="Everyone in your organization's directory"
        actions={
          canCreate || canExport ? (
            <>
              {/* The export carries the filters on screen, so the file matches the view it came from. */}
              {canExport && <ExportEmployeesButton query={exportQuery(list.pagedQuery, filterQuery)} />}
              {canCreate && (
                <Link className="button button-primary" to={`${EMPLOYEES_PATH}/new`} state={returnState}>
                  New employee
                </Link>
              )}
            </>
          ) : undefined
        }
      />

      {flash.message !== null && (
        <Notice tone="success" onDismiss={flash.dismiss}>
          {flash.message}
        </Notice>
      )}

      <Card isRefreshing={isRefreshing}>
        <div className="toolbar">
          <div className="toolbar-search">
            <label className="sr-only" htmlFor="list-search">
              Search employees
            </label>
            <input
              id="list-search"
              type="search"
              className="input"
              placeholder="Search name, code or email"
              value={list.search}
              onChange={(event) => list.setSearch(event.target.value)}
            />
          </div>

          {canViewDepartments && (
            <label className="toolbar-filter">
              <span className="toolbar-filter-label">Department</span>
              <select
                className="input select"
                value={list.filters.departmentId ?? ''}
                onChange={(event) => list.setFilter('departmentId', event.target.value)}
              >
                <option value="">All departments</option>
                {departments.data?.options.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>
          )}

          {canViewDesignations && (
            <label className="toolbar-filter">
              <span className="toolbar-filter-label">Designation</span>
              <select
                className="input select"
                value={list.filters.designationId ?? ''}
                onChange={(event) => list.setFilter('designationId', event.target.value)}
              >
                <option value="">All job titles</option>
                {designations.data?.options.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>
          )}

          <label className="toolbar-filter">
            <span className="toolbar-filter-label">Status</span>
            <select
              className="input select"
              value={list.filters.status ?? ''}
              onChange={(event) => list.setFilter('status', event.target.value)}
            >
              <option value="">All statuses</option>
              {EMPLOYEE_STATUSES.map((status) => (
                <option key={status} value={status}>
                  {status}
                </option>
              ))}
            </select>
          </label>

          {list.isFiltered && (
            <button type="button" className="button button-secondary" onClick={list.clearFilters}>
              Clear filters
            </button>
          )}
        </div>

        <DataTable
          caption={`Employees, page ${list.page}`}
          columns={columns}
          rows={data?.items ?? null}
          rowKey={(row) => row.id}
          isLoading={isLoading}
          error={error}
          onRetry={refetch}
          sort={list.sort}
          onSortChange={list.toggleSort}
          emptyTitle={list.isFiltered ? 'Nobody matches those filters' : 'No employees yet'}
          emptyMessage={
            list.isFiltered
              ? 'Try a different search term, or clear the filters to see everyone.'
              : 'Add the first employee to start building the directory.'
          }
          emptyAction={
            list.isFiltered ? (
              <button type="button" className="button button-secondary" onClick={list.clearFilters}>
                Clear filters
              </button>
            ) : canCreate ? (
              <Link className="button button-primary" to={`${EMPLOYEES_PATH}/new`} state={returnState}>
                New employee
              </Link>
            ) : undefined
          }
        />

        {data && (
          <Pagination
            info={data}
            onPageChange={list.setPage}
            onPageSizeChange={list.setPageSize}
            disabled={isRefreshing}
          />
        )}
      </Card>

      {pendingDelete && (
        <ConfirmDialog
          title="Delete employee?"
          message={
            <>
              <strong>{pendingDelete.fullName}</strong> ({pendingDelete.employeeCode}) will be removed
              permanently.
            </>
          }
          hint="Someone who has left the organization is usually a status change instead — that keeps their record, and their history, intact. A delete is refused while anyone still reports to them."
          onConfirm={() => confirmDelete(pendingDelete)}
          onClose={() => setPendingDelete(null)}
        />
      )}
    </>
  )
}

/**
 * The query behind the Export button: the same search, sort and filters, without the paging.
 *
 * `ExportAsync` applies the filters and the sort and then takes everything that matches, so `page` and
 * `pageSize` would be parameters it has no use for — the file is the whole filtered set, and page two of it
 * is not a thing anyone asked for.
 */
function exportQuery(paged: EmployeeQuery, filters: EmployeeQuery): EmployeeQuery {
  return {
    search: paged.search,
    sortBy: paged.sortBy,
    sortDescending: paged.sortDescending,
    ...filters,
  }
}

/** `''` means the filter is off, and an unrecognized value in a hand-edited URL is treated the same way. */
function asStatus(raw: string | undefined): EmployeeStatus | undefined {
  return EMPLOYEE_STATUSES.find((status) => status === raw)
}
