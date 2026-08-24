import type {
  AuthenticatedUser,
  Department,
  Designation,
  Employee,
  EmployeeListItem,
  LoginResponse,
  PagedResult,
} from '../api/types.ts'
import { Permissions } from '../auth/permissions.ts'

/**
 * Sample payloads shaped exactly like the API's DTOs.
 *
 * Written by hand rather than captured from a live server so that a change to the C# contract shows up
 * as a compile error here — these are typed, and `EmployeeListItem` gaining a required field breaks the
 * build rather than a test at runtime.
 */

export const HR_MANAGER_PERMISSIONS = [
  Permissions.employee.view,
  Permissions.employee.create,
  Permissions.employee.edit,
  Permissions.employee.export,
  Permissions.department.view,
  Permissions.designation.view,
]

export const MANAGER_PERMISSIONS = [
  Permissions.employee.view,
  Permissions.department.view,
  Permissions.designation.view,
]

export function makeUser(overrides: Partial<AuthenticatedUser> = {}): AuthenticatedUser {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    tenantId: '22222222-2222-2222-2222-222222222222',
    tenantCode: 'DEMO01',
    tenantName: 'Northwind Demo',
    email: 'hr@demo01.test',
    firstName: 'Priya',
    lastName: 'Raman',
    fullName: 'Priya Raman',
    roles: ['HRManager'],
    permissions: HR_MANAGER_PERMISSIONS,
    ...overrides,
  }
}

export function makeLoginResponse(overrides: Partial<LoginResponse> = {}): LoginResponse {
  return {
    accessToken: 'access-1',
    refreshToken: 'refresh-1',
    accessTokenExpiresAtUtc: '2026-08-22T12:00:00Z',
    expiresInSeconds: 900,
    tokenType: 'Bearer',
    user: makeUser(),
    ...overrides,
  }
}

export function makeEmployee(overrides: Partial<EmployeeListItem> = {}): EmployeeListItem {
  return {
    id: 'e1000000-0000-0000-0000-000000000001',
    employeeCode: 'EMP-001',
    fullName: 'Nadia Farrell',
    email: 'nadia.farrell@demo01.test',
    departmentName: 'Engineering',
    designationName: 'Senior Software Engineer',
    status: 'Active',
    dateOfJoining: '2023-03-14',
    ...overrides,
  }
}

/**
 * The detail DTO, which is a different shape from the row — it carries the reference *ids* the edit
 * form needs, plus the fields a list deliberately leaves out. Same person as {@link makeEmployee} so a
 * test can open a row and assert the form was filled from the record it named.
 */
export function makeEmployeeDetail(overrides: Partial<Employee> = {}): Employee {
  return {
    id: 'e1000000-0000-0000-0000-000000000001',
    employeeCode: 'EMP-001',
    firstName: 'Nadia',
    lastName: 'Farrell',
    fullName: 'Nadia Farrell',
    email: 'nadia.farrell@demo01.test',
    phone: '+353 1 555 0134',
    dateOfBirth: '1991-07-02',
    gender: 'Female',
    dateOfJoining: '2023-03-14',
    status: 'Active',
    departmentId: 'd1000000-0000-0000-0000-000000000001',
    departmentName: 'Engineering',
    designationId: 'g1000000-0000-0000-0000-000000000001',
    designationName: 'Senior Software Engineer',
    address: '14 Kildare Street, Dublin',
    createdDate: '2026-01-05T09:30:00Z',
    ...overrides,
  }
}

export function makeDepartment(overrides: Partial<Department> = {}): Department {
  return {
    id: 'd1000000-0000-0000-0000-000000000001',
    code: 'ENG',
    name: 'Engineering',
    isActive: true,
    employeeCount: 12,
    createdDate: '2026-01-05T09:30:00Z',
    ...overrides,
  }
}

export function makeDesignation(overrides: Partial<Designation> = {}): Designation {
  return {
    id: 'g1000000-0000-0000-0000-000000000001',
    code: 'SSE',
    name: 'Senior Software Engineer',
    isActive: true,
    employeeCount: 4,
    createdDate: '2026-01-05T09:30:00Z',
    ...overrides,
  }
}

/** Wraps items the way `PagedResult<T>` does, deriving the flags from the numbers. */
export function paged<T>(items: T[], overrides: Partial<PagedResult<T>> = {}): PagedResult<T> {
  const page = overrides.page ?? 1
  const pageSize = overrides.pageSize ?? 20
  const totalCount = overrides.totalCount ?? items.length
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize))
  return {
    items,
    page,
    pageSize,
    totalCount,
    totalPages,
    hasPreviousPage: page > 1,
    hasNextPage: page < totalPages,
    ...overrides,
  }
}
