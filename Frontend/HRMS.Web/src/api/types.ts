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

/**
 * `DTOs/Auth/LoginRequest.cs`. There is deliberately no organization field here: the organization is
 * decided by the host the request arrives at, resolved server-side before the credentials are
 * checked — so it is not something a caller can state or mistype.
 */
export interface LoginRequest {
  email: string
  password: string
}

/**
 * `DTOs/Tenants/TenantBrandingDto.cs` — what the sign-in screen shows for the organization at the
 * current address, read before anyone has authenticated. Every string field is optional because an
 * address no organization uses, one that is suspended and one that has not opted in all produce the
 * same all-null answer (`Neutral`), which a caller cannot tell apart. `ssoEnabled` alone is always
 * present, but a client must have an implemented provider before it offers anything.
 */
export interface TenantBranding {
  displayName?: string | null
  /** An absolute `https` URL — the API refuses every other scheme. */
  logoUrl?: string | null
  /** An accent colour as `#RRGGBB` — the API refuses any other shape. */
  primaryColor?: string | null
  welcomeMessage?: string | null
  supportEmail?: string | null
  ssoEnabled: boolean
  ssoProviderName?: string | null
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

// ---------------------------------------------------------------------------------------------
// Global Reference Data (Country / State / City)
// ---------------------------------------------------------------------------------------------

/** `DTOs/Countries/CountryDto.cs`. Global reference — no tenant scoping. */
export interface Country {
  id: string
  code: string
  name: string
  isActive: boolean
  createdDate: string
  modifiedDate?: string | null
}

export interface CountryRequest {
  code: string
  name: string
  isActive: boolean
}

export interface CountryQuery extends PagedQuery {
  isActive?: boolean
}

/** `DTOs/States/StateDto.cs`. */
export interface State {
  id: string
  countryId: string
  countryName: string
  code: string
  name: string
  isActive: boolean
  cityCount: number
  createdDate: string
  modifiedDate?: string | null
}

export interface StateRequest {
  countryId: string
  code: string
  name: string
  isActive: boolean
}

export interface StateQuery extends PagedQuery {
  countryId?: string
  isActive?: boolean
}

/** `DTOs/Cities/CityDto.cs`. */
export interface City {
  id: string
  stateId: string
  stateName: string
  code: string
  name: string
  isActive: boolean
  createdDate: string
  modifiedDate?: string | null
}

export interface CityRequest {
  stateId: string
  code: string
  name: string
  isActive: boolean
}

export interface CityQuery extends PagedQuery {
  stateId?: string
  isActive?: boolean
}

/** `Domain/Enums/Gender.cs`. `Unspecified` is the default so the field is never forced. */
export type Gender = 'Unspecified' | 'Male' | 'Female' | 'Other'

export const GENDERS: readonly Gender[] = ['Unspecified', 'Male', 'Female', 'Other']

/** `Domain/Enums/MaritalStatus.cs`. */
export type MaritalStatus = 'Unspecified' | 'Single' | 'Married' | 'Divorced' | 'Widowed' | 'Separated'

export const MARITAL_STATUSES: readonly MaritalStatus[] = ['Unspecified', 'Single', 'Married', 'Divorced', 'Widowed', 'Separated']

/** Employee type options matching backend business rules. */
export type EmployeeTypeOption =
  | 'Staff-Regular' | 'Worker-Regular' | 'Worker-Contractual'
  | 'Trainee Staff' | 'Regular Worker Unionized' | 'Regular Worker Commercial'
  | 'Trainee - AET' | 'Regular substaff and T Workers'
  | 'Apprentice' | 'Trainee Associate' | 'Consultant'

export const EMPLOYEE_TYPE_OPTIONS: readonly EmployeeTypeOption[] = [
  'Staff-Regular', 'Worker-Regular', 'Worker-Contractual',
  'Trainee Staff', 'Regular Worker Unionized', 'Regular Worker Commercial',
  'Trainee - AET', 'Regular substaff and T Workers',
  'Apprentice', 'Trainee Associate', 'Consultant',
]

/** Job status options. */
export type JobStatus = 'Probation' | 'Confirmed' | 'Contractual'

export const JOB_STATUSES: readonly JobStatus[] = ['Probation', 'Confirmed', 'Contractual']

/** Title/salutation options. */
export type Title = 'Brig.' | 'Capt.' | 'Col.' | 'Comm.' | 'Dr.' | 'Late' | 'Lieut.' | 'Major' | 'Major General' | 'Mr.' | 'Mrs.' | 'Ms.' | 'Prof.' | 'WgCom.'

export const TITLES: readonly Title[] = [
  'Brig.', 'Capt.', 'Col.', 'Comm.', 'Dr.', 'Late', 'Lieut.', 'Major', 'Major General', 'Mr.', 'Mrs.', 'Ms.', 'Prof.', 'WgCom.',
]

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
  salutation?: string | null
  firstName: string
  middleName?: string | null
  lastName: string
  fullName: string
  email: string
  phone?: string | null
  dateOfBirth?: string | null
  gender: Gender
  bloodGroup: BloodGroup
  maritalStatus: MaritalStatus
  birthCountry?: string | null
  birthCountryId?: string | null
  birthState?: string | null
  birthStateId?: string | null
  birthCity?: string | null
  birthCityId?: string | null
  religion?: string | null
  caste?: string | null
  dateOfJoining: string
  groupDateOfJoining?: string | null
  dateOfLeaving?: string | null
  status: EmployeeStatus
  jobStatus?: string | null
  groupId?: string | null
  departmentId: string
  departmentName: string
  designationId: string
  designationName: string
  reportingManagerId?: string | null
  reportingManagerName?: string | null
  employeeType?: string | null
  /** Display-only masked identifiers; raw values are available only from the protected edit endpoint. */
  maskedAadhaarNumber?: string | null
  maskedPanNumber?: string | null
  maskedPfNumber?: string | null
  maskedUanNumber?: string | null
  maskedEsicNumber?: string | null
  maskedMediclaimNumber?: string | null
  gratuity: boolean
  pension: boolean
  costCenterCode?: string | null
  payrollLocation?: string | null
  esicApplicable: boolean
  citizenship?: string | null
  languageKnown?: string | null
  profilePictureUrl?: string | null
  address?: string | null
  createdDate: string
  modifiedDate?: string | null
}

/** Raw statutory identifiers returned only by the protected employee edit endpoint. */
export interface EmployeeSensitiveDetails {
  employeeId: string
  aadhaarNumber?: string | null
  panNumber?: string | null
  uanNumber?: string | null
  pfNumber?: string | null
  esicNumber?: string | null
  mediclaimNumber?: string | null
}

/** `DTOs/Employees/EmployeeRequest.cs`. */
export interface EmployeeRequest {
  employeeCode: string
  firstName: string
  middleName?: string | null
  lastName: string
  salutation?: string | null
  email: string
  phone?: string | null
  dateOfBirth?: string | null
  gender: Gender
  bloodGroup: BloodGroup
  maritalStatus: MaritalStatus
  birthCountry?: string | null
  birthCountryId?: string | null
  birthState?: string | null
  birthStateId?: string | null
  birthCity?: string | null
  birthCityId?: string | null
  religion?: string | null
  caste?: string | null
  employeeType?: string | null
  dateOfJoining: string
  groupDateOfJoining?: string | null
  /** Required once `status` is anything but `Active`, and rejected while it is. */
  dateOfLeaving?: string | null
  status: EmployeeStatus
  jobStatus?: string | null
  groupId?: string | null
  departmentId: string
  designationId: string
  reportingManagerId?: string | null
  aadhaarNumber?: string | null
  panNumber?: string | null
  pfNumber?: string | null
  uanNumber?: string | null
  esicNumber?: string | null
  mediclaimNumber?: string | null
  gratuity: boolean
  pension: boolean
  costCenterCode?: string | null
  payrollLocation?: string | null
  esicApplicable: boolean
  citizenship?: string | null
  languageKnown?: string | null
  profilePictureUrl?: string | null
  address?: string | null
}

/**
 * `DTOs/Employees/EmployeePersonalDetailsRequest.cs` — create/update payload for the Employee →
 * Personal Details section only. There is no employee code (the backend assigns it and shows as
 * "New Hire" before save), no department/designation/reporting manager, and no email/phone/address.
 */
export interface EmployeePersonalDetailsRequest {
  salutation?: string | null
  firstName: string
  middleName?: string | null
  lastName: string
  dateOfBirth?: string | null
  gender: Gender
  bloodGroup: BloodGroup
  maritalStatus: MaritalStatus
  birthCountryId?: string | null
  birthStateId?: string | null
  birthCityId?: string | null
  religion?: string | null
  caste?: string | null
  /** Country of citizenship, stored as the country name. */
  citizenship?: string | null
  esicApplicable: boolean
  esicNumber?: string | null
  pfNumber?: string | null
  mediclaimNumber?: string | null
  uanNumber?: string | null
  gratuity: boolean
  pension: boolean
  aadhaarNumber?: string | null
  panNumber?: string | null
  dateOfJoining: string
  jobStatus?: string | null
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

// ---------------------------------------------------------------------------------------------
// Employee Sub-section Enums
// ---------------------------------------------------------------------------------------------

export type AddressType = 'Current' | 'Permanent'
export const ADDRESS_TYPES: readonly AddressType[] = ['Current', 'Permanent']

export type BloodGroup =
  | 'Unspecified' | 'APositive' | 'ANegative'
  | 'BPositive' | 'BNegative'
  | 'OPositive' | 'ONegative'
  | 'ABPositive' | 'ABNegative'
export const BLOOD_GROUPS: readonly BloodGroup[] = [
  'Unspecified', 'APositive', 'ANegative', 'BPositive', 'BNegative',
  'OPositive', 'ONegative', 'ABPositive', 'ABNegative',
]

export type EducationType = 'FullTime' | 'PartTime' | 'Distance' | 'Online' | 'Correspondence'
export const EDUCATION_TYPES: readonly EducationType[] = ['FullTime', 'PartTime', 'Distance', 'Online', 'Correspondence']

export type EmploymentType = 'Unspecified' | 'FullTime' | 'PartTime' | 'Contract' | 'Intern' | 'Temporary' | 'Consultant'
export const EMPLOYMENT_TYPES: readonly EmploymentType[] = ['Unspecified', 'FullTime', 'PartTime', 'Contract', 'Intern', 'Temporary', 'Consultant']

export type EmploymentChangeReason = 'Unspecified' | 'NewJoining' | 'Promotion' | 'Transfer' | 'DepartmentChange' | 'RoleChange' | 'LocationChange' | 'GradeChange' | 'ManagerChange' | 'OrganizationalRestructure' | 'Correction' | 'Other'
export const EMPLOYMENT_CHANGE_REASONS: readonly EmploymentChangeReason[] = ['Unspecified', 'NewJoining', 'Promotion', 'Transfer', 'DepartmentChange', 'RoleChange', 'LocationChange', 'GradeChange', 'ManagerChange', 'OrganizationalRestructure', 'Correction', 'Other']

export type AccountType = 'Unspecified' | 'Savings' | 'Current' | 'Salary' | 'NRE' | 'NRO'
export const ACCOUNT_TYPES: readonly AccountType[] = ['Unspecified', 'Savings', 'Current', 'Salary', 'NRE', 'NRO']

export type AccountPurpose = 'Unspecified' | 'Salary' | 'Gratuity' | 'Pension' | 'Reimbursement'
export const ACCOUNT_PURPOSES: readonly AccountPurpose[] = ['Unspecified', 'Salary', 'Gratuity', 'Pension', 'Reimbursement']

export type BankAccountStatus = 'Active' | 'Frozen' | 'Closed'
export const BANK_ACCOUNT_STATUSES: readonly BankAccountStatus[] = ['Active', 'Frozen', 'Closed']

export type DocumentCategory =
  | 'Unspecified' | 'Identity' | 'Address' | 'Education' | 'Experience'
  | 'Salary' | 'OfferLetter' | 'AppointmentLetter' | 'RelievingLetter'
  | 'ExperienceLetter' | 'Photo' | 'Signature' | 'Other'
export const DOCUMENT_CATEGORIES: readonly DocumentCategory[] = [
  'Unspecified', 'Identity', 'Address', 'Education', 'Experience',
  'Salary', 'OfferLetter', 'AppointmentLetter', 'RelievingLetter',
  'ExperienceLetter', 'Photo', 'Signature', 'Other',
]

export type AuditChangeType = 'Create' | 'Update' | 'Delete' | 'Import' | 'StatusChange' | 'EmploymentChange' | 'DocumentUpload' | 'DocumentDelete'
export const AUDIT_CHANGE_TYPES: readonly AuditChangeType[] = ['Create', 'Update', 'Delete', 'Import', 'StatusChange', 'EmploymentChange', 'DocumentUpload', 'DocumentDelete']

// ---------------------------------------------------------------------------------------------
// Employee Contact
// ---------------------------------------------------------------------------------------------

export interface EmployeeContact {
  id: string
  employeeId: string
  officialEmail?: string | null
  personalEmail?: string | null
  alternateEmail?: string | null
  officialPhone?: string | null
  personalPhone?: string | null
  emergencyNumber?: string | null
  sameAsCurrentAddress: boolean
  createdDate: string
  modifiedDate?: string | null
}

export interface EmployeeContactRequest {
  officialEmail?: string | null
  personalEmail?: string | null
  alternateEmail?: string | null
  officialPhone?: string | null
  personalPhone?: string | null
  emergencyNumber?: string | null
  sameAsCurrentAddress: boolean
}

// ---------------------------------------------------------------------------------------------
// Employee Address
// ---------------------------------------------------------------------------------------------

export interface EmployeeAddress {
  id: string
  employeeId: string
  addressType: AddressType
  country?: string | null
  state?: string | null
  district?: string | null
  city?: string | null
  zipCode?: string | null
  addressLine1?: string | null
  addressLine2?: string | null
  houseNumber?: string | null
  createdDate: string
  modifiedDate?: string | null
}

export interface EmployeeAddressRequest {
  addressType: AddressType
  country?: string | null
  state?: string | null
  district?: string | null
  city?: string | null
  zipCode?: string | null
  addressLine1?: string | null
  addressLine2?: string | null
  houseNumber?: string | null
}

// ---------------------------------------------------------------------------------------------
// Employee Family
// ---------------------------------------------------------------------------------------------

export interface EmployeeFamily {
  id: string
  employeeId: string
  salutation?: string | null
  firstName: string
  middleName?: string | null
  lastName: string
  relationship: string
  gender: Gender
  dateOfBirth?: string | null
  bloodGroup: BloodGroup
  nationality?: string | null
  occupation?: string | null
  isNominee: boolean
  isDependent: boolean
  nomineePercentage?: number | null
  createdDate: string
  modifiedDate?: string | null
}

export interface EmployeeFamilyRequest {
  salutation?: string | null
  firstName: string
  middleName?: string | null
  lastName: string
  relationship: string
  gender: Gender
  dateOfBirth?: string | null
  bloodGroup: BloodGroup
  nationality?: string | null
  occupation?: string | null
  isNominee: boolean
  isDependent: boolean
  nomineePercentage?: number | null
}

// ---------------------------------------------------------------------------------------------
// Employee Education
// ---------------------------------------------------------------------------------------------

export interface EmployeeEducation {
  id: string
  employeeId: string
  educationLevel: string
  qualification: string
  university?: string | null
  institute?: string | null
  educationType: EducationType
  areaOfSpecialization?: string | null
  yearOfPassing?: number | null
  score?: string | null
  documentOfProof?: string | null
  createdDate: string
  modifiedDate?: string | null
}

export interface EmployeeEducationRequest {
  educationLevel: string
  qualification: string
  university?: string | null
  institute?: string | null
  educationType: EducationType
  areaOfSpecialization?: string | null
  yearOfPassing?: number | null
  score?: string | null
  documentOfProof?: string | null
}

// ---------------------------------------------------------------------------------------------
// Employee Previous Employment
// ---------------------------------------------------------------------------------------------

export interface EmployeePreviousEmployment {
  id: string
  employeeId: string
  company: string
  designation?: string | null
  location?: string | null
  employmentType: EmploymentType
  tenureFrom?: string | null
  tenureTill?: string | null
  documentOfProof?: string | null
  createdDate: string
  modifiedDate?: string | null
}

export interface EmployeePreviousEmploymentRequest {
  company: string
  designation?: string | null
  location?: string | null
  employmentType: EmploymentType
  tenureFrom?: string | null
  tenureTill?: string | null
  documentOfProof?: string | null
}

// ---------------------------------------------------------------------------------------------
// Bank (master) / Employee Bank Detail
// ---------------------------------------------------------------------------------------------

/** A bank from the tenant-scoped bank master, used to populate the bank dropdown. */
export interface Bank {
  id: string
  code: string
  name: string
  isActive: boolean
}

export interface EmployeeBankDetail {
  id: string
  employeeId: string
  bankId: string
  /** Denormalized bank name resolved from the bank master (read-only). */
  bankName: string
  accountHolderName: string
  maskedAccountNumber: string
  accountType: AccountType
  accountPurpose: AccountPurpose
  status: BankAccountStatus
  maskedIfscCode?: string | null
  branchName?: string | null
  effectiveFrom?: string | null
  isActive: boolean
  hasDocumentOfProof: boolean
  createdDate: string
  modifiedDate?: string | null
}

/** Full bank values returned only by the protected bank-detail edit endpoint. */
export interface EmployeeBankDetailEdit {
  id: string
  employeeId: string
  bankId: string
  bankName: string
  accountHolderName: string
  accountNumber: string
  accountType: AccountType
  accountPurpose: AccountPurpose
  status: BankAccountStatus
  ifscCode?: string | null
  branchName?: string | null
  effectiveFrom?: string | null
  isActive: boolean
  documentOfProof?: string | null
  createdDate: string
  modifiedDate?: string | null
}

export interface EmployeeBankDetailRequest {
  bankId: string
  accountHolderName: string
  accountNumber: string
  accountType: AccountType
  accountPurpose: AccountPurpose
  status: BankAccountStatus
  ifscCode?: string | null
  branchName?: string | null
  effectiveFrom?: string | null
  documentOfProof?: string | null
}

// ---------------------------------------------------------------------------------------------
// Employee Supervisor
// ---------------------------------------------------------------------------------------------

export type SupervisorType = 'L1' | 'L2' | 'L3' | 'Other' | 'HR' | 'Time'
export const SUPERVISOR_TYPES: readonly SupervisorType[] = ['L1', 'L2', 'L3', 'Other', 'HR', 'Time']

export const SUPERVISOR_TYPE_LABELS: Record<SupervisorType, string> = {
  L1: 'L1 Manager',
  L2: 'L2 Manager',
  L3: 'L3 Manager',
  Other: 'Other Manager (ERO)',
  HR: 'CHRO Manager',
  Time: 'Time Manager',
}

export interface SupervisorOption {
  employeeId: string
  employeeCode: string
  fullName: string
  departmentName?: string | null
  designationName?: string | null
}

export interface EmployeeSupervisor {
  id: string
  employeeId: string
  l1ManagerCode?: string | null
  l1ManagerName?: string | null
  l1ManagerId?: string | null
  l2ManagerCode?: string | null
  l2ManagerName?: string | null
  l2ManagerId?: string | null
  l3ManagerCode?: string | null
  l3ManagerName?: string | null
  l3ManagerId?: string | null
  l4ManagerCode?: string | null
  l4ManagerName?: string | null
  l4ManagerId?: string | null
  l5ManagerCode?: string | null
  l5ManagerName?: string | null
  l5ManagerId?: string | null
  timeManagerCode?: string | null
  timeManagerName?: string | null
  timeManagerId?: string | null
  eroCode?: string | null
  eroName?: string | null
  eroId?: string | null
  chroManagerCode?: string | null
  chroManagerName?: string | null
  chroManagerId?: string | null
  createdDate: string
  modifiedDate?: string | null
}

export interface EmployeeSupervisorRequest {
  l1ManagerCode?: string | null
  l1ManagerName?: string | null
  l1ManagerId?: string | null
  l2ManagerCode?: string | null
  l2ManagerName?: string | null
  l2ManagerId?: string | null
  l3ManagerCode?: string | null
  l3ManagerName?: string | null
  l3ManagerId?: string | null
  l4ManagerCode?: string | null
  l4ManagerName?: string | null
  l4ManagerId?: string | null
  l5ManagerCode?: string | null
  l5ManagerName?: string | null
  l5ManagerId?: string | null
  timeManagerCode?: string | null
  timeManagerName?: string | null
  timeManagerId?: string | null
  eroCode?: string | null
  eroName?: string | null
  eroId?: string | null
  chroManagerCode?: string | null
  chroManagerName?: string | null
  chroManagerId?: string | null
}

// ---------------------------------------------------------------------------------------------
// Employee Additional Info
// ---------------------------------------------------------------------------------------------

export interface EmployeeAdditionalInfo {
  id: string
  employeeId: string
  division?: string | null
  paPsa?: string | null
  additionalEmployeeCode?: string | null
  contractId?: string | null
  createdDate: string
  modifiedDate?: string | null
}

export interface EmployeeAdditionalInfoRequest {
  division?: string | null
  paPsa?: string | null
  additionalEmployeeCode?: string | null
  contractId?: string | null
}

// ---------------------------------------------------------------------------------------------
// Employee Employment (Joining Information — 1:1 with Employee)
// ---------------------------------------------------------------------------------------------

export interface EmployeeEmployment {
  id: string
  employeeId: string
  firstHiredDate: string
  dateOfJoining: string
  groupDateOfJoining?: string | null
  confirmationDate?: string | null
  jobStatus?: string | null
  probationPeriod?: number | null
  probationPeriodUnit?: string | null
  referredByEmployeeId?: string | null
  referredByEmployeeName?: string | null
  noticePeriod?: number | null
  noticePeriodUnit?: string | null
  createdDate: string
  modifiedDate?: string | null
}

export interface EmployeeEmploymentRequest {
  firstHiredDate: string
  dateOfJoining: string
  groupDateOfJoining?: string | null
  confirmationDate?: string | null
  jobStatus?: string | null
  probationPeriod?: number | null
  probationPeriodUnit?: string | null
  referredByEmployeeId?: string | null
  noticePeriod?: number | null
  noticePeriodUnit?: string | null
}

// ---------------------------------------------------------------------------------------------
// Employee Employment History (FK-based — references master data tables)
// ---------------------------------------------------------------------------------------------

export interface EmployeeEmploymentHistory {
  id: string
  employeeId: string
  effectiveFrom: string
  effectiveTo?: string | null
  // FK references to master data (nullable = optional)
  holdingCompanyId?: string | null
  lobId?: string | null
  organisationId?: string | null
  departmentId?: string | null
  subDepartmentId?: string | null
  sectionId?: string | null
  subSectionId?: string | null
  functionId?: string | null
  subFunctionId?: string | null
  gradeId?: string | null
  designationId?: string | null
  employeeTypeId?: string | null
  countryLocationId?: string | null
  workLocationId?: string | null
  costCenterId?: string | null
  managerId?: string | null
  positionChangeReasonId?: string | null
  // Snapshot strings (kept for historical display)
  holdingCompanyName?: string | null
  lobName?: string | null
  organisationName?: string | null
  departmentName?: string | null
  subDepartmentName?: string | null
  sectionName?: string | null
  subSectionName?: string | null
  functionName?: string | null
  subFunctionName?: string | null
  gradeName?: string | null
  designationName?: string | null
  employeeTypeName?: string | null
  countryLocationName?: string | null
  workLocationName?: string | null
  costCenterName?: string | null
  managerName?: string | null
  managerCode?: string | null
  positionChangeReasonName?: string | null
  // Additional info
  businessRole?: string | null
  gradeLevel?: string | null
  careerGroup?: string | null
  employmentType: EmploymentType
  employmentStatus: EmployeeStatus
  changeReason: EmploymentChangeReason
  changeReasonDescription?: string | null
  createdBy?: string | null
  createdDate: string
  modifiedDate?: string | null
}

export interface EmploymentChangeRequest {
  employeeCode?: string | null
  effectiveFrom: string
  holdingCompanyId?: string | null
  lobId?: string | null
  organisationId?: string | null
  departmentId?: string | null
  subDepartmentId?: string | null
  sectionId?: string | null
  subSectionId?: string | null
  functionId?: string | null
  subFunctionId?: string | null
  gradeId?: string | null
  designationId?: string | null
  employeeTypeId?: string | null
  countryLocationId?: string | null
  workLocationId?: string | null
  costCenterId?: string | null
  managerId?: string | null
  positionChangeReasonId?: string | null
  changeReason: EmploymentChangeReason
  businessRole?: string | null
  gradeLevel?: string | null
  careerGroup?: string | null
  employmentType: EmploymentType
  employmentStatus: EmployeeStatus
  changeReasonDescription?: string | null
}

// ---------------------------------------------------------------------------------------------
// Employee Audit Log
// ---------------------------------------------------------------------------------------------

export interface EmployeeAuditLog {
  id: string
  employeeId: string
  employeeCode?: string | null
  module: string
  section?: string | null
  entityName?: string | null
  recordId?: string | null
  fieldName?: string | null
  oldValue?: string | null
  newValue?: string | null
  changeType: AuditChangeType
  effectiveDate?: string | null
  changedBy: string
  reason?: string | null
  source?: string | null
  importBatchId?: string | null
  ipAddress?: string | null
  createdDate: string
}

export interface AuditQuery extends PagedQuery {
  dateFrom?: string
  dateTo?: string
  module?: string
  section?: string
  changeType?: AuditChangeType
  user?: string
}

// ---------------------------------------------------------------------------------------------
// Employee Document
// ---------------------------------------------------------------------------------------------

export interface EmployeeDocument {
  id: string
  documentName: string
  documentCategory: DocumentCategory
  documentNumber?: string | null
  filePath: string
  fileSize: number
  contentType: string
  uploadedBy?: string | null
  createdDate: string
}

export interface EmployeeDocumentRequest {
  documentName: string
  documentCategory: DocumentCategory
  documentNumber?: string | null
  filePath: string
  fileSize: number
  contentType: string
}

// ---------------------------------------------------------------------------------------------
// Import Batch
// ---------------------------------------------------------------------------------------------

export interface ImportBatch {
  id: string
  fileName?: string | null
  importedBy: string
  totalRows: number
  successfulRows: number
  failedRows: number
  skippedRows: number
  status: string
  startedAtUtc?: string | null
  completedAtUtc?: string | null
  message?: string | null
  createdDate: string
}

// ---------------------------------------------------------------------------------------------
// Master Lookup (Tenant-scoped reference data for dropdowns)
// ---------------------------------------------------------------------------------------------

/** `DTOs/Masters/MasterLookupDto.cs`. Unified shape for all master dropdowns. */
export interface MasterLookup {
  id: string
  code: string
  name: string
  isActive: boolean
}

/** `DTOs/Masters/MasterLookupQuery.cs`. `parentId` filters hierarchical children. */
export interface MasterLookupQuery {
  parentId?: string
  isActive?: boolean
}
