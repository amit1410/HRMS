import type { Employee, EmployeeStatus, Gender } from '../../api/types.ts'
import type { SelectOption } from '../../components/fields.tsx'

/** The list route, and the base every employee URL is built from. */
export const EMPLOYEES_PATH = '/employees'

/**
 * The employee form's state, and the two conversions between it and the API's DTOs.
 *
 * Separate from the form component so the shape of the values can be read — and tested — without
 * rendering anything, and so the component file exports only a component.
 */

/** Every value the form holds, as strings, which is what an `<input>` and a `<select>` deal in. */
export interface EmployeeFormValues {
  employeeCode: string
  firstName: string
  lastName: string
  email: string
  phone: string
  dateOfBirth: string
  gender: Gender
  dateOfJoining: string
  dateOfLeaving: string
  status: EmployeeStatus
  departmentId: string
  designationId: string
  reportingManagerId: string
  address: string
}

/**
 * The three references as the record currently has them, so an edit form can show a name for each before
 * — and regardless of whether — it appears in the options that load.
 */
export interface CurrentReferences {
  department: SelectOption | null
  designation: SelectOption | null
  manager: SelectOption | null
}

/** What a create form has: no record, so no existing references to preserve. */
export const NO_REFERENCES: CurrentReferences = {
  department: null,
  designation: null,
  manager: null,
}

/** A blank employee. Status starts Active, which is what "add an employee" almost always means. */
export function emptyEmployeeValues(): EmployeeFormValues {
  return {
    employeeCode: '',
    firstName: '',
    lastName: '',
    email: '',
    phone: '',
    dateOfBirth: '',
    gender: 'Unspecified',
    dateOfJoining: '',
    dateOfLeaving: '',
    status: 'Active',
    departmentId: '',
    designationId: '',
    reportingManagerId: '',
    address: '',
  }
}

/** An existing employee, unpacked into form values. `null` becomes `''`: an input has no null. */
export function toEmployeeValues(employee: Employee): EmployeeFormValues {
  return {
    employeeCode: employee.employeeCode,
    firstName: employee.firstName,
    lastName: employee.lastName,
    email: employee.email,
    phone: employee.phone ?? '',
    dateOfBirth: employee.dateOfBirth ?? '',
    gender: employee.gender,
    dateOfJoining: employee.dateOfJoining,
    dateOfLeaving: employee.dateOfLeaving ?? '',
    status: employee.status,
    departmentId: employee.departmentId,
    designationId: employee.designationId,
    reportingManagerId: employee.reportingManagerId ?? '',
    address: employee.address ?? '',
  }
}

/** The record's references, named. A manager is only carried when there is both an id and a name. */
export function toCurrentReferences(employee: Employee): CurrentReferences {
  return {
    department: { value: employee.departmentId, label: employee.departmentName },
    designation: { value: employee.designationId, label: employee.designationName },
    manager:
      employee.reportingManagerId && employee.reportingManagerName
        ? { value: employee.reportingManagerId, label: employee.reportingManagerName }
        : null,
  }
}
