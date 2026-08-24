import { useParams } from 'react-router-dom'
import { createEmployee, getEmployee, updateEmployee } from '../../api/employees.ts'
import { Card } from '../../components/Card.tsx'
import { ErrorState } from '../../components/ErrorState.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'
import {
  NO_REFERENCES,
  emptyEmployeeValues,
  toCurrentReferences,
  toEmployeeValues,
} from './employeeValues.ts'
import { EmployeeForm } from './EmployeeForm.tsx'

/**
 * Create and edit for an employee.
 *
 * Split into two components rather than branched inside one, because the edit mode has a fetch that the
 * create mode must not make and a hook cannot be called conditionally. The `key` on {@link EditEmployee}
 * restarts the form when the route moves from one employee to another, so typed-but-unsaved values cannot
 * leak from the record the user left onto the one they opened.
 */
export function EmployeeFormPage() {
  const { id } = useParams()

  return id === undefined ? <NewEmployee /> : <EditEmployee key={id} id={id} />
}

function NewEmployee() {
  useDocumentTitle('New employee')

  return (
    <>
      <PageHeader title="New employee" subtitle="Add someone to the directory" />
      <EmployeeForm
        initial={emptyEmployeeValues()}
        current={NO_REFERENCES}
        submitLabel="Create employee"
        onSubmit={(body) => createEmployee(body)}
        successMessage={(employee) => `${employee.fullName} was added.`}
      />
    </>
  )
}

/**
 * The edit mode fetches the employee even when the row it was opened from is already on screen. The list
 * returns `EmployeeListItem`, which carries department and designation as *names* — the form needs the ids,
 * and there is no way to turn a name back into one that cannot pick the wrong record.
 */
function EditEmployee({ id }: { id: string }) {
  useDocumentTitle('Edit employee')

  const { data, error, isLoading, refetch } = useApiQuery((signal) => getEmployee(id, signal), [id])

  if (error) {
    return (
      <>
        <PageHeader title="Edit employee" />
        <Card>
          <ErrorState error={error} onRetry={refetch} />
        </Card>
      </>
    )
  }

  if (isLoading || !data) {
    return (
      <>
        <PageHeader title="Edit employee" />
        <Card>
          <div className="table-loading">
            <Spinner label="Loading employee…" />
          </div>
        </Card>
      </>
    )
  }

  return (
    <>
      <PageHeader
        title={`Edit ${data.fullName}`}
        subtitle={`${data.employeeCode} · ${data.departmentName} · ${data.designationName}`}
      />
      <EmployeeForm
        initial={toEmployeeValues(data)}
        current={toCurrentReferences(data)}
        employeeId={data.id}
        submitLabel="Save changes"
        onSubmit={(body) => updateEmployee(id, body)}
        successMessage={(employee) => `${employee.fullName} was updated.`}
      />
    </>
  )
}
