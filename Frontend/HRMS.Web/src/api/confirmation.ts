import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'

export type ProbationDurationUnit = 'Months' | 'Days'
export type ConfirmationPolicyVersionStatus = 'Draft' | 'Published' | 'Retired'
export type ConfirmationApprovalRequirement = 'Manager' | 'Hrbp' | 'ManagerAndHrbp' | 'ManagerOrHrbp'
export type ApprovalSequence = 'Parallel' | 'ManagerThenHrbp'
export type OverallRatingMethod = 'AverageOfRatings' | 'MinimumRating'
export type ConfirmationOutcome = 'Pending' | 'Confirmed' | 'ProbationExtended' | 'NotConfirmed' | 'AdditionalReview'
export type ConfirmationPolicyResolutionStatus =
  | 'Resolved'
  | 'NoPolicy'
  | 'InvalidTenant'
  | 'EffectiveEmploymentNotFound'
  | 'EffectiveEmploymentAmbiguous'
  | 'ConfigurationAmbiguity'

export interface ConfirmationPolicy {
  id: string
  code: string
  name: string
  description?: string | null
  isActive: boolean
  versionCount: number
  publishedVersionCount: number
  createdDate: string
  modifiedDate?: string | null
}

export interface ConfirmationPolicyQuery {
  page?: number
  pageSize?: number
  search?: string
  isActive?: boolean
  sortBy?: string
  sortDescending?: boolean
}

export interface ConfirmationPolicyRequest {
  code: string
  name: string
  description?: string | null
  isActive: boolean
}

export interface ConfirmationProbationConfig {
  probationDuration: number
  probationDurationUnit: ProbationDurationUnit
  triggerDaysBeforeEnd: number
  maxExtensions: number
  extensionDuration: number
  extensionDurationUnit: ProbationDurationUnit
  requiredApproval: ConfirmationApprovalRequirement
  confirmRatingThreshold?: number | null
  autoConfirmOnApproval: boolean
  overallRatingMethod: OverallRatingMethod
  approvalSequence: ApprovalSequence
  requireReviewerComments: boolean
  confirmedJobStatus?: string | null
  notConfirmedJobStatus?: string | null
}

export interface ConfirmationDecisionRule {
  id?: string
  priority: number
  name: string
  description?: string | null
  minOverallRating?: number | null
  maxOverallRating?: number | null
  outcome: ConfirmationOutcome
  isActive: boolean
}

export interface ConfirmationPolicyApplicability {
  id?: string
  gender?: string | null
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
}

export interface ConfirmationPolicyVersion {
  id: string
  versionNumber: number
  effectiveFrom: string
  effectiveTo?: string | null
  status: ConfirmationPolicyVersionStatus
  priority: number
  applicabilitySetCount: number
  createdDate: string
  createdBy?: string | null
  modifiedDate?: string | null
}

export interface ConfirmationPolicyEditor {
  policy: ConfirmationPolicy
  version: ConfirmationPolicyVersion
  probation: ConfirmationProbationConfig
  applicability: ConfirmationPolicyApplicability[]
  decisionRules: ConfirmationDecisionRule[]
}

export interface ConfirmationPolicyVersionRequest {
  effectiveFrom: string
  effectiveTo?: string | null
  priority: number
  probation: ConfirmationProbationConfig
  applicability?: ConfirmationPolicyApplicability[]
  decisionRules?: ConfirmationDecisionRule[]
}

export interface ConfirmationPolicyResolve {
  status: ConfirmationPolicyResolutionStatus
  employeeId: string
  effectiveDate: string
  confirmationPolicyId?: string | null
  confirmationPolicyVersionId?: string | null
  priority?: number | null
  specificity?: number | null
  probationDuration?: number | null
  probationDurationUnit?: ProbationDurationUnit | null
  triggerDaysBeforeEnd?: number | null
  dateOfJoining?: string | null
  probationEndDate?: string | null
  message: string
}

export interface EmployeeConfirmationStatus {
  employeeId: string
  dateOfJoining: string
  isPolicyManaged: boolean
  confirmationPolicyId?: string | null
  confirmationPolicyName?: string | null
  probationEndDate?: string | null
  reviewTriggerDate?: string | null
  confirmationDate?: string | null
  jobStatus?: string | null
  message: string
}

export type ConfirmationReminderRecipientType = 'Employee' | 'Manager' | 'Hrbp'

export interface ConfirmationSchedulerSetting {
  isConfigured: boolean
  enabled: boolean
  frequencyMinutes: number
  defaultTriggerDaysBeforeEnd: number
  maxAttempts?: number | null
  claimLeaseMinutes?: number | null
  resendProcessed: boolean
  notifyEmployee: boolean
  notifyManager: boolean
  notifyHrbp: boolean
  lastRunAtUtc?: string | null
  modifiedDate?: string | null
  modifiedBy?: string | null
}

export interface ConfirmationSchedulerSettingRequest {
  enabled: boolean
  frequencyMinutes: number
  defaultTriggerDaysBeforeEnd: number
  maxAttempts?: number | null
  claimLeaseMinutes?: number | null
  resendProcessed: boolean
  notifyEmployee: boolean
  notifyManager: boolean
  notifyHrbp: boolean
}

export interface ConfirmationReminderDelivery {
  id: string
  employeeId: string
  confirmationPolicyId: string
  confirmationPolicyVersionId: string
  probationEndDate: string
  triggerDate: string
  recipientType: ConfirmationReminderRecipientType
  recipientEmployeeId?: string | null
  recipientEmail?: string | null
  status: string
  attemptCount: number
  sentAtUtc?: string | null
  processedAtUtc?: string | null
  nextAttemptAtUtc?: string | null
  lastError?: string | null
  createdDate: string
}

export interface ConfirmationReminderLogQuery {
  page?: number
  pageSize?: number
  employeeId?: string
  status?: string
}

const base = '/api/confirmation-policies'
const schedulerBase = '/api/confirmation-scheduler'

export function getConfirmationSchedulerSettings(signal?: AbortSignal): Promise<ConfirmationSchedulerSetting> {
  return request(() => api.get<ApiResponse<ConfirmationSchedulerSetting>>(`${schedulerBase}/settings`, { signal }))
}

export function updateConfirmationSchedulerSettings(body: ConfirmationSchedulerSettingRequest): Promise<ConfirmationSchedulerSetting> {
  return request(() => api.put<ApiResponse<ConfirmationSchedulerSetting>>(`${schedulerBase}/settings`, body))
}

export function listConfirmationReminderExecutions(query: ConfirmationReminderLogQuery = {}, signal?: AbortSignal): Promise<PagedResult<ConfirmationReminderDelivery>> {
  return request(() => api.get<ApiResponse<PagedResult<ConfirmationReminderDelivery>>>(`${schedulerBase}/executions`, { params: cleanParams({ ...query }), signal }))
}

export function listConfirmationPolicies(query: ConfirmationPolicyQuery = {}, signal?: AbortSignal): Promise<PagedResult<ConfirmationPolicy>> {
  return request(() => api.get<ApiResponse<PagedResult<ConfirmationPolicy>>>(base, { params: cleanParams({ ...query }), signal }))
}

export function getConfirmationPolicy(id: string, signal?: AbortSignal): Promise<ConfirmationPolicy> {
  return request(() => api.get<ApiResponse<ConfirmationPolicy>>(`${base}/${id}`, { signal }))
}

export function createConfirmationPolicy(body: ConfirmationPolicyRequest): Promise<ConfirmationPolicy> {
  return request(() => api.post<ApiResponse<ConfirmationPolicy>>(base, body))
}

export function updateConfirmationPolicy(id: string, body: ConfirmationPolicyRequest): Promise<ConfirmationPolicy> {
  return request(() => api.put<ApiResponse<ConfirmationPolicy>>(`${base}/${id}`, body))
}

export function listConfirmationPolicyVersions(policyId: string, signal?: AbortSignal): Promise<PagedResult<ConfirmationPolicyVersion>> {
  return request(() => api.get<ApiResponse<PagedResult<ConfirmationPolicyVersion>>>(`${base}/${policyId}/versions`, { signal }))
}

export function getConfirmationPolicyEditor(policyId: string, versionId?: string, signal?: AbortSignal): Promise<ConfirmationPolicyEditor> {
  return request(() => api.get<ApiResponse<ConfirmationPolicyEditor>>(`${base}/${policyId}/editor`, { params: cleanParams({ versionId }), signal }))
}

export function createConfirmationPolicyVersion(policyId: string, body: ConfirmationPolicyVersionRequest): Promise<ConfirmationPolicyEditor> {
  return request(() => api.post<ApiResponse<ConfirmationPolicyEditor>>(`${base}/${policyId}/versions`, body))
}

export function updateConfirmationPolicyVersion(policyId: string, versionId: string, body: ConfirmationPolicyVersionRequest): Promise<ConfirmationPolicyEditor> {
  return request(() => api.put<ApiResponse<ConfirmationPolicyEditor>>(`${base}/${policyId}/versions/${versionId}`, body))
}

export function publishConfirmationPolicyVersion(policyId: string, versionId: string): Promise<ConfirmationPolicyVersion> {
  return request(() => api.post<ApiResponse<ConfirmationPolicyVersion>>(`${base}/${policyId}/versions/${versionId}/publish`))
}

export function resolveConfirmationPolicy(employeeId: string, effectiveDate?: string): Promise<ConfirmationPolicyResolve> {
  return request(() => api.post<ApiResponse<ConfirmationPolicyResolve>>(`${base}/resolve`, { employeeId, effectiveDate }))
}

export function getEmployeeConfirmationStatus(employeeId: string, signal?: AbortSignal): Promise<EmployeeConfirmationStatus> {
  return request(() => api.get<ApiResponse<EmployeeConfirmationStatus>>(`${base}/employees/${employeeId}/status`, { signal }))
}
