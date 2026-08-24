import {
  createDepartment,
  deleteDepartment,
  getDepartment,
  listDepartments,
  updateDepartment,
} from '../../api/departments.ts'
import {
  createDesignation,
  deleteDesignation,
  getDesignation,
  listDesignations,
  updateDesignation,
} from '../../api/designations.ts'
import { SORT_FIELDS, type PagedQuery, type PagedResult } from '../../api/types.ts'
import { Permissions } from '../../auth/permissions.ts'

/**
 * Departments and designations, described rather than duplicated.
 *
 * The two resources are the same resource twice. Identical DTOs (code, name, description, isActive, plus
 * a server-computed employee count), identical validation rules, identical sort fields, identical
 * endpoints down to the shape of the 409 they refuse a delete with. Writing two list screens and two
 * forms would mean four files that must be kept in step by hand — and the failure mode of that is not a
 * broken build, it is a fix applied to departments and forgotten for designations.
 *
 * So there is one list screen and one form, and this file is what they are pointed at. What genuinely
 * differs is the wording (a designation is *held by* an employee, a department has employees *assigned
 * to* it) and the four permissions, and that is all this configuration carries.
 *
 * If the C# DTOs ever diverge, the module literals below stop compiling: `listDepartments` is only
 * assignable to `LookupModule['list']` while a `DepartmentDto` is still a {@link LookupRecord}.
 */

/** The fields the two DTOs share — which, as they stand, is all of them. */
export interface LookupRecord {
  id: string
  code: string
  name: string
  description?: string | null
  isActive: boolean
  /** Computed by the API. Read-only here, and the reason a delete can be refused. */
  employeeCount: number
  createdDate: string
  modifiedDate?: string | null
}

/** The write body. A full replacement: an omitted description clears the stored one. */
export interface LookupRequest {
  code: string
  name: string
  description?: string | null
  isActive: boolean
}

export interface LookupQuery extends PagedQuery {
  isActive?: boolean
}

export interface LookupModule {
  /** Also the URL segment and the `SORT_FIELDS` key. */
  key: 'departments' | 'designations'
  /** Lower case singular, for sentences: "Delete this department?" */
  noun: string
  /** Title case plural, for the page heading and the nav. */
  title: string
  subtitle: string
  /** Route prefix, without a trailing slash. */
  basePath: string
  permissions: {
    view: string
    create: string
    edit: string
    delete: string
  }
  sortFields: readonly string[]
  /** Column header over `employeeCount` — "Employees" for a unit, "Holders" for a job title. */
  countHeader: string
  /** Shown in the delete dialog, before the request is made, so the likely refusal is not a surprise. */
  deleteHint: string
  /** Guidance under the code input, quoting the format the API's validator enforces. */
  codeHint: string
  emptyTitle: string
  emptyMessage: string
  list: (query: LookupQuery, signal?: AbortSignal) => Promise<PagedResult<LookupRecord>>
  get: (id: string, signal?: AbortSignal) => Promise<LookupRecord>
  create: (body: LookupRequest, signal?: AbortSignal) => Promise<LookupRecord>
  update: (id: string, body: LookupRequest, signal?: AbortSignal) => Promise<LookupRecord>
  remove: (id: string, signal?: AbortSignal) => Promise<boolean>
}

/**
 * Both hints describe the same rule (`CodeFormats.Pattern` in the API), quoted rather than re-implemented
 * — the form does no client-side validation, so this text is the only place the user hears about it
 * before submitting.
 */
const CODE_HINT =
  'Letters, digits, and . _ - / after the first character. Up to 20 characters, and unique within your organization.'

export const departmentsModule: LookupModule = {
  key: 'departments',
  noun: 'department',
  title: 'Departments',
  subtitle: 'The units employees are assigned to',
  basePath: '/departments',
  permissions: {
    view: Permissions.department.view,
    create: Permissions.department.create,
    edit: Permissions.department.edit,
    delete: Permissions.department.delete,
  },
  sortFields: SORT_FIELDS.departments,
  countHeader: 'Employees',
  deleteHint:
    'A department with employees assigned to it cannot be deleted, because their records would lose the unit they worked in. Mark it inactive instead to keep it out of new assignments.',
  codeHint: CODE_HINT,
  emptyTitle: 'No departments yet',
  emptyMessage: 'Add the first department to start assigning employees to it.',
  list: listDepartments,
  get: getDepartment,
  create: createDepartment,
  update: updateDepartment,
  remove: deleteDepartment,
}

export const designationsModule: LookupModule = {
  key: 'designations',
  noun: 'designation',
  title: 'Designations',
  subtitle: 'The job titles employees hold',
  basePath: '/designations',
  permissions: {
    view: Permissions.designation.view,
    create: Permissions.designation.create,
    edit: Permissions.designation.edit,
    delete: Permissions.designation.delete,
  },
  sortFields: SORT_FIELDS.designations,
  countHeader: 'Holders',
  deleteHint:
    'A designation held by an employee cannot be deleted, because their record would lose the title they held. Mark it inactive instead to keep it out of new assignments.',
  codeHint: CODE_HINT,
  emptyTitle: 'No designations yet',
  emptyMessage: 'Add the first job title to start assigning it to employees.',
  list: listDesignations,
  get: getDesignation,
  create: createDesignation,
  update: updateDesignation,
  remove: deleteDesignation,
}
