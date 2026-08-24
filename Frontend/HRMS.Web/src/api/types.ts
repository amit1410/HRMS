/**
 * TypeScript mirror of the API's public contract.
 *
 * Every type here corresponds to a C# DTO under `Backend/HRMS.Application/DTOs` or
 * `Backend/HRMS.Application/Common`. Two serializer settings on the API shape how they are written:
 *
 * - property names are camelCase;
 * - `DefaultIgnoreCondition = WhenWritingNull`, so a null property is *absent* from the JSON rather
 *   than present as `null`. Optional properties below are therefore `?:` — and additionally `| null`
 *   so that flipping that setting server-side could not silently break the client.
 *
 * Enums cross the wire as strings (`JsonStringEnumConverter`), not as their numeric values, which is
 * why they are string unions here.
 */

/** Envelope for every JSON response: `ApiResponse` / `ApiResponse<T>` in `Common/ApiResponse.cs`. */
export interface ApiResponse<T = undefined> {
  success: boolean
  message: string
  data?: T
  errors?: ValidationError[]
}

/** One field-level failure: `Common/ValidationError.cs`. `field` is camelCase, matching the DTO property. */
export interface ValidationError {
  field: string
  message: string
}

/** One page of results plus the totals needed to render pagination: `Common/PagedResult.cs`. */
export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

/** Paging/search/sort inputs accepted by every list endpoint (`Common/PagedQuery.cs`). */
export interface PagedQuery {
  page?: number
  pageSize?: number
  search?: string
  sortBy?: string
  sortDescending?: boolean
}

/** Matches `PagedQuery.DefaultPageSize`. */
export const DEFAULT_PAGE_SIZE = 20

/** Matches `PagedQuery.MaxPageSize`. A larger value is rejected by the API's query validator. */
export const MAX_PAGE_SIZE = 100

// ---------------------------------------------------------------------------------------------
// Auth
// ---------------------------------------------------------------------------------------------

/** `DTOs/Auth/LoginRequest.cs`. The tenant code selects *which* tenant's credentials are checked. */
export interface LoginRequest {
  tenantCode: string
  email: string
  password: string
}

/** `DTOs/Auth/LoginResponse.cs`. Returned by both `/api/auth/login` and `/api/auth/refresh`. */
export interface LoginResponse {
  accessToken: string
  refreshToken: string
  accessTokenExpiresAtUtc: string
  expiresInSeconds: number
  tokenType: string
  user: AuthenticatedUser
}

/**
 * `DTOs/Auth/AuthenticatedUserDto.cs`: profile, tenant, and the effective roles/permissions the
 * server calculated. Permissions are re-read from the database on every login and refresh, so this
 * is the authoritative list — never a cached one.
 */
export interface AuthenticatedUser {
  id: string
  tenantId: string
  tenantCode: string
  tenantName: string
  email: string
  firstName: string
  lastName: string
  fullName: string
  lastLoginDateUtc?: string | null
  roles: string[]
  permissions: string[]
}

// ---------------------------------------------------------------------------------------------
// Organization
// ---------------------------------------------------------------------------------------------

/** `DTOs/Departments/DepartmentDto.cs`. */
export interface Department {
  id: string
  code: string
  name: string
  description?: string | null
  isActive: boolean
  employeeCount: number
  createdDate: string
  modifiedDate?: string | null
}

/** `DTOs/Departments/DepartmentRequest.cs`. No tenant id: the server takes it from the token. */
export interface DepartmentRequest {
  code: string
  name: string
  description?: string | null
  isActive: boolean
}

/** `DTOs/Departments/DepartmentQuery.cs`. */
export interface DepartmentQuery extends PagedQuery {
  isActive?: boolean
}

/** `DTOs/Designations/DesignationDto.cs`. */
export interface Designation {
  id: string
  code: string
  name: string
  description?: string | null
  isActive: boolean
  employeeCount: number
  createdDate: string
  modifiedDate?: string | null
}

/** `DTOs/Designations/DesignationRequest.cs`. */
export interface DesignationRequest {
  code: string
  name: string
  description?: string | null
  isActive: boolean
}

/** `DTOs/Designations/DesignationQuery.cs`. */
export interface DesignationQuery extends PagedQuery {
  isActive?: boolean
}

/** `Domain/Enums/EmployeeStatus.cs`. Leaving the organization is a status change, never a delete. */
export type EmployeeStatus = 'Active' | 'Resigned' | 'Terminated'

export const EMPLOYEE_STATUSES: readonly EmployeeStatus[] = ['Active', 'Resigned', 'Terminated']

/** `Domain/Enums/Gender.cs`. `Unspecified` is the default so the field is never forced. */
export type Gender = 'Unspecified' | 'Male' | 'Female' | 'Other'

export const GENDERS: readonly Gender[] = ['Unspecified', 'Male', 'Female', 'Other']

/**
 * `DTOs/Employees/EmployeeListItemDto.cs` — deliberately narrower than the detail DTO: date of
 * birth, phone and address are not broadcast in a page of rows just because they would fit.
 */
export interface EmployeeListItem {
  id: string
  employeeCode: string
  fullName: string
  email: string
  departmentName: string
  designationName: string
  status: EmployeeStatus
  /** ISO date (`yyyy-MM-dd`) — a C# `DateOnly`, so it carries no time and no zone. */
  dateOfJoining: string
}

/** `DTOs/Employees/EmployeeDto.cs`. Ids *and* display names, so an edit form needs no extra requests. */
export interface Employee {
  id: string
  employeeCode: string
  firstName: string
  lastName: string
  fullName: string
  email: string
  phone?: string | null
  dateOfBirth?: string | null
  gender: Gender
  dateOfJoining: string
  dateOfLeaving?: string | null
  status: EmployeeStatus
  departmentId: string
  departmentName: string
  designationId: string
  designationName: string
  reportingManagerId?: string | null
  reportingManagerName?: string | null
  address?: string | null
  createdDate: string
  modifiedDate?: string | null
}

/** `DTOs/Employees/EmployeeRequest.cs`. */
export interface EmployeeRequest {
  employeeCode: string
  firstName: string
  lastName: string
  email: string
  phone?: string | null
  dateOfBirth?: string | null
  gender: Gender
  dateOfJoining: string
  /** Required once `status` is anything but `Active`, and rejected while it is. */
  dateOfLeaving?: string | null
  status: EmployeeStatus
  departmentId: string
  designationId: string
  reportingManagerId?: string | null
  address?: string | null
}

/** `DTOs/Employees/EmployeeQuery.cs`. `search` matches employee code, first/last name and email. */
export interface EmployeeQuery extends PagedQuery {
  departmentId?: string
  designationId?: string
  status?: EmployeeStatus
  reportingManagerId?: string
}

/**
 * Sort fields each list endpoint accepts, mirroring the `SortFields` arrays on the query DTOs. The
 * API rejects anything else with a 400 that names the permitted values rather than silently falling
 * back, so the UI must not offer a column these lists do not contain.
 */
export const SORT_FIELDS = {
  departments: ['code', 'name', 'employeeCount', 'isActive', 'createdDate'],
  designations: ['code', 'name', 'employeeCount', 'isActive', 'createdDate'],
  employees: [
    'employeeCode',
    'firstName',
    'lastName',
    'email',
    'department',
    'designation',
    'status',
    'dateOfJoining',
    'createdDate',
  ],
} as const satisfies Record<string, readonly string[]>
