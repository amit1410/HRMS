import type {
  AddressType,
  AuthenticatedUser,
  Bank,
  Department,
  Designation,
  Employee,
  EmployeeAddress,
  EmployeeBankDetail,
  EmployeeBankDetailEdit,
  EmployeeContact,
  EmployeeEmploymentHistory,
  EmployeeListItem,
  EmployeeSensitiveDetails,
  LoginResponse,
  MasterLookup,
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
  Permissions.employeeSensitive.view,
  Permissions.employeeSensitive.edit,
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
    bloodGroup: 'Unspecified',
    maritalStatus: 'Unspecified',
    dateOfJoining: '2023-03-14',
    status: 'Active',
    departmentId: 'd1000000-0000-0000-0000-000000000001',
    departmentName: 'Engineering',
    designationId: 'g1000000-0000-0000-0000-000000000001',
    designationName: 'Senior Software Engineer',
    gratuity: false,
    pension: false,
    esicApplicable: false,
    address: '14 Kildare Street, Dublin',
    createdDate: '2026-01-05T09:30:00Z',
    ...overrides,
  }
}

export function makeEmployeeSensitiveDetails(
  overrides: Partial<EmployeeSensitiveDetails> = {},
): EmployeeSensitiveDetails {
  return {
    employeeId: 'e1000000-0000-0000-0000-000000000001',
    aadhaarNumber: null,
    panNumber: null,
    uanNumber: null,
    pfNumber: null,
    esicNumber: null,
    mediclaimNumber: null,
    ...overrides,
  }
}

/** The contact record DTO, keyed to the same employee as the other fixtures. */
export function makeContact(overrides: Partial<EmployeeContact> = {}): EmployeeContact {
  return {
    id: 'c3000000-0000-0000-0000-000000000001',
    employeeId: 'e1000000-0000-0000-0000-000000000001',
    officialEmail: 'nadia.farrell@demo01.test',
    personalEmail: 'personal@example.com',
    alternateEmail: 'alternate@example.com',
    officialPhone: '9876543210',
    personalPhone: '9123456780',
    emergencyNumber: null,
    sameAsCurrentAddress: true,
    createdDate: '2026-01-05T09:30:00Z',
    ...overrides,
  }
}

/** A structured address DTO, keyed to the same employee as the other fixtures. */
export function makeAddress(
  addressType: AddressType,
  overrides: Partial<EmployeeAddress> = {},
): EmployeeAddress {
  const isCurrent = addressType === 'Current'
  return {
    id: isCurrent ? 'a1000000-0000-0000-0000-000000000001' : 'a1000000-0000-0000-0000-000000000002',
    employeeId: 'e1000000-0000-0000-0000-000000000001',
    addressType,
    country: 'India',
    state: 'Maharashtra',
    district: 'Mumbai City',
    city: 'Mumbai',
    zipCode: '400001',
    addressLine1: '14 Kildare Street',
    addressLine2: 'Flat 3',
    houseNumber: null,
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

/** A bank master row as the dropdown supplies it (the unified master lookup shape). */
export function makeBankLookup(overrides: Partial<MasterLookup> = {}): MasterLookup {
  return {
    id: 'b1000000-0000-0000-0000-000000000001',
    code: 'SBI',
    name: 'State Bank of India',
    isActive: true,
    ...overrides,
  }
}

/** Any master-data row as the dropdown supplies it (holding companies, grades, change reasons, …). */
export function makeMasterLookup(overrides: Partial<MasterLookup> = {}): MasterLookup {
  return {
    id: 'm1000000-0000-0000-0000-000000000001',
    code: 'M1',
    name: 'Master item',
    isActive: true,
    ...overrides,
  }
}

/** A position market change history row (FKs to master data plus denormalized names). */
export function makeEmploymentHistory(
  overrides: Partial<EmployeeEmploymentHistory> = {},
): EmployeeEmploymentHistory {
  return {
    id: 'eh1000000-0000-0000-0000-000000000001',
    employeeId: 'e1000000-0000-0000-0000-000000000001',
    effectiveFrom: '2026-01-05',
    effectiveTo: null,
    departmentId: 'd1000000-0000-0000-0000-000000000001',
    departmentName: 'Engineering',
    designationId: 'g1000000-0000-0000-0000-000000000001',
    designationName: 'Software Engineer',
    gradeId: 'gr1000000-0000-0000-0000-000000000001',
    gradeName: 'Grade 1',
    workLocationId: 'wl1000000-0000-0000-0000-000000000001',
    workLocationName: 'Mumbai Office',
    positionChangeReasonId: 'pr1000000-0000-0000-0000-000000000001',
    positionChangeReasonName: 'New Hire',
    employmentType: 'FullTime',
    employmentStatus: 'Active',
    changeReason: 'NewJoining',
    createdDate: '2026-01-05T09:30:00Z',
    ...overrides,
  }
}

/** A bank master entity (used where the Bank-type shape is required). */
export function makeBank(overrides: Partial<Bank> = {}): Bank {
  return {
    id: 'b1000000-0000-0000-0000-000000000001',
    code: 'SBI',
    name: 'State Bank of India',
    isActive: true,
    ...overrides,
  }
}

/** An employee bank detail DTO, keyed to the same employee as the other fixtures. */
export function makeBankDetail(overrides: Partial<EmployeeBankDetail> = {}): EmployeeBankDetail {
  return {
    id: 'bk1000000-0000-0000-0000-000000000001',
    employeeId: 'e1000000-0000-0000-0000-000000000001',
    bankId: 'b1000000-0000-0000-0000-000000000001',
    bankName: 'State Bank of India',
    accountHolderName: 'Nadia Farrell',
    maskedAccountNumber: '********-100',
    accountType: 'Savings',
    accountPurpose: 'Salary',
    status: 'Active',
    maskedIfscCode: 'SBIN*****01',
    branchName: 'Main Branch',
    effectiveFrom: '2026-01-05',
    isActive: true,
    hasDocumentOfProof: false,
    createdDate: '2026-01-05T09:30:00Z',
    ...overrides,
  }
}

export function makeBankDetailEdit(overrides: Partial<EmployeeBankDetailEdit> = {}): EmployeeBankDetailEdit {
  return {
    id: 'bk1000000-0000-0000-0000-000000000001',
    employeeId: 'e1000000-0000-0000-0000-000000000001',
    bankId: 'b1000000-0000-0000-0000-000000000001',
    bankName: 'State Bank of India',
    accountHolderName: 'Nadia Farrell',
    accountNumber: 'ACC-100',
    accountType: 'Savings',
    accountPurpose: 'Salary',
    status: 'Active',
    ifscCode: 'SBIN0000001',
    branchName: 'Main Branch',
    effectiveFrom: '2026-01-05',
    isActive: true,
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
