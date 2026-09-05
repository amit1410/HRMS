import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult, ValidationError } from './types.ts'

export type LeaveUnit = 'Day' | 'Hour'

export interface LeaveType {
  id: string
  code: string
  name: string
  description?: string | null
  defaultUnit: LeaveUnit
  isPaid: boolean
  isActive: boolean
  createdDate: string
  modifiedDate?: string | null
  concurrencyToken: string
}

export type LeaveTypeListItem = LeaveType
export interface LeaveTypeQuery { page?: number; pageSize?: number; search?: string; isActive?: boolean }
export interface LeaveTypeRequest {
  code: string
  name: string
  description?: string | null
  defaultUnit: LeaveUnit
  isPaid: boolean
  isActive: boolean
  concurrencyToken?: string
}

export interface LeavePeriod {
  id: string
  code: string
  name: string
  startDate: string
  endDate: string
  isActive: boolean
  createdDate: string
  modifiedDate?: string | null
  concurrencyToken: string
}

export type LeavePeriodListItem = LeavePeriod
export interface LeavePeriodQuery { page?: number; pageSize?: number; search?: string; isActive?: boolean; onDate?: string }
export interface LeavePeriodRequest {
  code: string
  name: string
  startDate: string
  endDate: string
  isActive: boolean
  concurrencyToken?: string
}

export function listLeaveTypes(query: LeaveTypeQuery = {}, signal?: AbortSignal): Promise<PagedResult<LeaveTypeListItem>> {
  return request(() => api.get<ApiResponse<PagedResult<LeaveTypeListItem>>>('/api/leave-types', { params: cleanParams({ ...query }), signal }))
}

export function getLeaveType(id: string, signal?: AbortSignal): Promise<LeaveType> {
  return request(() => api.get<ApiResponse<LeaveType>>(`/api/leave-types/${id}`, { signal }))
}

export function createLeaveType(body: LeaveTypeRequest): Promise<LeaveType> {
  return request(() => api.post<ApiResponse<LeaveType>>('/api/leave-types', body))
}

export function updateLeaveType(id: string, body: LeaveTypeRequest): Promise<LeaveType> {
  return request(() => api.put<ApiResponse<LeaveType>>(`/api/leave-types/${id}`, body))
}

export function listLeavePeriods(query: LeavePeriodQuery = {}, signal?: AbortSignal): Promise<PagedResult<LeavePeriodListItem>> {
  return request(() => api.get<ApiResponse<PagedResult<LeavePeriodListItem>>>('/api/leave-periods', { params: cleanParams({ ...query }), signal }))
}

export function getLeavePeriod(id: string, signal?: AbortSignal): Promise<LeavePeriod> {
  return request(() => api.get<ApiResponse<LeavePeriod>>(`/api/leave-periods/${id}`, { signal }))
}

export function createLeavePeriod(body: LeavePeriodRequest): Promise<LeavePeriod> {
  return request(() => api.post<ApiResponse<LeavePeriod>>('/api/leave-periods', body))
}

export function updateLeavePeriod(id: string, body: LeavePeriodRequest): Promise<LeavePeriod> {
  return request(() => api.put<ApiResponse<LeavePeriod>>(`/api/leave-periods/${id}`, body))
}

export type LeavePolicyVersionStatus = 'Draft' | 'Published' | 'Retired'

export interface LeavePolicy {
  id: string
  code: string
  name: string
  description?: string | null
  isActive: boolean
  versionCount: number
  currentVersionNumber?: number | null
  createdDate: string
  modifiedDate?: string | null
  concurrencyToken: string
}

export type LeavePolicyListItem = LeavePolicy
export interface LeavePolicyQuery { page?: number; pageSize?: number; search?: string; isActive?: boolean }
export interface LeavePolicyRequest { code: string; name: string; description?: string | null; isActive: boolean; concurrencyToken?: string }

export interface LeavePolicyVersion {
  id: string
  versionNumber: number
  effectiveFrom: string
  effectiveTo?: string | null
  status: LeavePolicyVersionStatus
  priority: number
  leaveTypeCount: number
  applicabilityGroupCount: number
  createdDate: string
  createdBy?: string | null
  modifiedDate?: string | null
  concurrencyToken: string
  allowedActions: { canEdit: boolean; canValidate: boolean; canPublish: boolean; canRetire: boolean; canCreateVersion: boolean }
}

export interface LeavePolicyVersionRequest { effectiveFrom: string; effectiveTo?: string | null; priority: number; copyFromVersionId?: string | null }
export interface LeavePolicyVersionUpdateRequest { effectiveFrom: string; effectiveTo?: string | null; priority: number; concurrencyToken?: string }
export interface LeaveTypeSelection { id: string; code: string; name: string; isActive: boolean }
export interface LeaveTypeSelectionRequest { leaveTypeIds: string[]; concurrencyToken?: string | null }

export interface LeaveApplicabilityGroup {
  id: string
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

export type LeaveApplicabilityGroupRequest = Omit<LeaveApplicabilityGroup, 'id' | 'gender'> & { gender?: string | null }
export interface LeaveApplicabilityRequest { groups: LeaveApplicabilityGroupRequest[]; concurrencyToken?: string | null }
export interface LeavePolicyValidation { isValid: boolean; errors: ValidationError[]; warnings: string[] }

export interface LeavePolicyEditor { policy: LeavePolicy; currentVersion?: LeavePolicyVersion | null; leaveTypes: LeaveTypeSelection[]; applicabilityGroups: LeaveApplicabilityGroup[] }
export type EligibilityMode = 'Immediate' | 'MinimumService'
export type EligibilityServiceUnit = 'Days' | 'Months'
export type ProbationMode = 'Allowed' | 'NotAllowed' | 'AfterConfirmation'
export type NoticePeriodMode = 'Allowed' | 'NotAllowed' | 'AllowedWithApproval'
export interface LeavePolicyEligibilityRule {
  id: string
  leavePolicyRuleId: string
  eligibilityMode: EligibilityMode
  minimumServiceValue?: number | null
  minimumServiceUnit?: EligibilityServiceUnit | null
  probationMode: ProbationMode
  noticePeriodMode: NoticePeriodMode
  concurrencyToken: string
}
export interface LeavePolicyEligibilityRuleRequest {
  eligibilityMode: EligibilityMode
  minimumServiceValue?: number | null
  minimumServiceUnit?: EligibilityServiceUnit | null
  probationMode: ProbationMode
  noticePeriodMode: NoticePeriodMode
  concurrencyToken?: string | null
}
export type EntitlementMode = 'Allocated' | 'Unlimited' | 'NoBalanceRequired'
export type EntitlementSource = 'PolicyAccrual' | 'ExternalGrant' | 'NoBalanceRequired'
export type AccrualFrequency = 'None' | 'Upfront' | 'Monthly' | 'Quarterly' | 'SemiAnnual' | 'Annual'
export type AccrualTiming = 'StartOfPeriod' | 'EndOfPeriod'
export interface LeavePolicyEntitlementRule {
  id: string
  leavePolicyRuleId: string
  entitlementMode: EntitlementMode
  entitlementSource: EntitlementSource
  entitlementQuantity?: number | null
  accrualFrequency: AccrualFrequency
  accrualTiming?: AccrualTiming | null
  concurrencyToken: string
}
export interface LeavePolicyEntitlementRuleRequest {
  entitlementMode: EntitlementMode
  entitlementSource: EntitlementSource
  entitlementQuantity?: number | null
  accrualFrequency: AccrualFrequency
  accrualTiming?: AccrualTiming | null
  concurrencyToken?: string | null
}
export type PartialDayMode = 'FullDayOnly' | 'HalfDayAllowed'
export type BackdatedRequestMode = 'NotAllowed' | 'Allowed' | 'AllowedUpToDays'
export type RequestLimitPeriod = 'Month' | 'LeavePeriod'
export interface LeavePolicyRequestRule {
  id: string
  leavePolicyRuleId: string
  minimumRequestQuantity?: number | null
  maximumRequestQuantity?: number | null
  maximumConsecutiveQuantity?: number | null
  minimumAdvanceNoticeDays: number
  backdatedRequestMode: BackdatedRequestMode
  maximumBackdatedDays?: number | null
  maximumRequestsPerPeriod?: number | null
  maximumQuantityPerPeriod?: number | null
  requestLimitPeriod?: RequestLimitPeriod | null
  partialDayMode: PartialDayMode
  concurrencyToken: string
}
export type LeavePolicyRequestRuleRequest = Omit<LeavePolicyRequestRule, 'id' | 'leavePolicyRuleId' | 'concurrencyToken'> & { concurrencyToken?: string | null }

export function listLeavePolicies(query: LeavePolicyQuery = {}, signal?: AbortSignal): Promise<PagedResult<LeavePolicyListItem>> {
  return request(() => api.get<ApiResponse<PagedResult<LeavePolicyListItem>>>('/api/leave-policies', { params: cleanParams({ ...query }), signal }))
}

export function getLeavePolicy(id: string, signal?: AbortSignal): Promise<LeavePolicy> {
  return request(() => api.get<ApiResponse<LeavePolicy>>(`/api/leave-policies/${id}`, { signal }))
}

export function createLeavePolicy(body: LeavePolicyRequest): Promise<LeavePolicy> {
  return request(() => api.post<ApiResponse<LeavePolicy>>('/api/leave-policies', body))
}

export function updateLeavePolicy(id: string, body: LeavePolicyRequest): Promise<LeavePolicy> {
  return request(() => api.put<ApiResponse<LeavePolicy>>(`/api/leave-policies/${id}`, body))
}

export function listLeavePolicyVersions(policyId: string, signal?: AbortSignal): Promise<PagedResult<LeavePolicyVersion>> {
  return request(() => api.get<ApiResponse<PagedResult<LeavePolicyVersion>>>(`/api/leave-policies/${policyId}/versions`, { signal }))
}

export function getLeavePolicyEditor(policyId: string, versionId?: string, signal?: AbortSignal): Promise<LeavePolicyEditor> {
  return request(() => api.get<ApiResponse<LeavePolicyEditor>>(`/api/leave-policies/${policyId}/editor`, { params: cleanParams({ versionId }), signal }))
}

export function createLeavePolicyVersion(policyId: string, body: LeavePolicyVersionRequest): Promise<LeavePolicyVersion> {
  return request(() => api.post<ApiResponse<LeavePolicyVersion>>(`/api/leave-policies/${policyId}/versions`, body))
}

export function updateLeavePolicyVersion(policyId: string, versionId: string, body: LeavePolicyVersionUpdateRequest): Promise<LeavePolicyVersion> {
  return request(() => api.put<ApiResponse<LeavePolicyVersion>>(`/api/leave-policies/${policyId}/versions/${versionId}`, body))
}

export function listVersionLeaveTypes(policyId: string, versionId: string, signal?: AbortSignal): Promise<LeaveTypeSelection[]> {
  return request(() => api.get<ApiResponse<LeaveTypeSelection[]>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types`, { signal }))
}

export function setVersionLeaveTypes(policyId: string, versionId: string, body: LeaveTypeSelectionRequest): Promise<LeaveTypeSelection[]> {
  return request(() => api.put<ApiResponse<LeaveTypeSelection[]>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types`, body))
}

export function getApplicability(policyId: string, versionId: string, signal?: AbortSignal): Promise<LeaveApplicabilityGroup[]> {
  return request(() => api.get<ApiResponse<LeaveApplicabilityGroup[]>>(`/api/leave-policies/${policyId}/versions/${versionId}/applicability`, { signal }))
}

export function setApplicability(policyId: string, versionId: string, body: LeaveApplicabilityRequest): Promise<LeaveApplicabilityGroup[]> {
  return request(() => api.put<ApiResponse<LeaveApplicabilityGroup[]>>(`/api/leave-policies/${policyId}/versions/${versionId}/applicability`, body))
}

export function getLeaveTypeEligibility(policyId: string, versionId: string, leaveTypeId: string): Promise<LeavePolicyEligibilityRule | null> {
  return request(() => api.get<ApiResponse<LeavePolicyEligibilityRule | null>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types/${leaveTypeId}/eligibility`))
}

export function saveLeaveTypeEligibility(policyId: string, versionId: string, leaveTypeId: string, body: LeavePolicyEligibilityRuleRequest): Promise<LeavePolicyEligibilityRule | null> {
  return request(() => api.put<ApiResponse<LeavePolicyEligibilityRule | null>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types/${leaveTypeId}/eligibility`, body))
}

export function getLeaveTypeEntitlement(policyId: string, versionId: string, leaveTypeId: string, signal?: AbortSignal): Promise<LeavePolicyEntitlementRule | null> {
  return request(() => api.get<ApiResponse<LeavePolicyEntitlementRule | null>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types/${leaveTypeId}/entitlement`, { signal }))
}

export function saveLeaveTypeEntitlement(policyId: string, versionId: string, leaveTypeId: string, body: LeavePolicyEntitlementRuleRequest): Promise<LeavePolicyEntitlementRule | null> {
  return request(() => api.put<ApiResponse<LeavePolicyEntitlementRule | null>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types/${leaveTypeId}/entitlement`, body))
}
export function getLeaveTypeRequestRule(policyId: string, versionId: string, leaveTypeId: string, signal?: AbortSignal): Promise<LeavePolicyRequestRule | null> {
  return request(() => api.get<ApiResponse<LeavePolicyRequestRule | null>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types/${leaveTypeId}/request-rules`, { signal }))
}
export function saveLeaveTypeRequestRule(policyId: string, versionId: string, leaveTypeId: string, body: LeavePolicyRequestRuleRequest): Promise<LeavePolicyRequestRule | null> {
  return request(() => api.put<ApiResponse<LeavePolicyRequestRule | null>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types/${leaveTypeId}/request-rules`, body))
}
export type HolidayTreatment = 'Exclude' | 'Include'
export type WeekOffTreatment = 'Exclude' | 'Include'
export type SandwichMode = 'Disabled' | 'Holiday' | 'WeekOff' | 'HolidayAndWeekOff'
export interface LeavePolicyCalendarRule { id: string; leavePolicyRuleId: string; holidayTreatment: HolidayTreatment; weekOffTreatment: WeekOffTreatment; sandwichMode: SandwichMode; applyToPrefix: boolean; applyToSuffix: boolean; applyToBetween: boolean; concurrencyToken: string }
export type LeavePolicyCalendarRuleRequest = Omit<LeavePolicyCalendarRule, 'id' | 'leavePolicyRuleId' | 'concurrencyToken'> & { concurrencyToken?: string | null }
export function getLeaveTypeCalendarRule(policyId: string, versionId: string, leaveTypeId: string, signal?: AbortSignal): Promise<LeavePolicyCalendarRule | null> { return request(() => api.get<ApiResponse<LeavePolicyCalendarRule | null>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types/${leaveTypeId}/calendar`, { signal })) }
export function saveLeaveTypeCalendarRule(policyId: string, versionId: string, leaveTypeId: string, body: LeavePolicyCalendarRuleRequest): Promise<LeavePolicyCalendarRule | null> { return request(() => api.put<ApiResponse<LeavePolicyCalendarRule | null>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types/${leaveTypeId}/calendar`, body)) }
export type AttachmentRequirement = 'None' | 'Optional' | 'Required' | 'RequiredAboveQuantity'
export interface LeavePolicyAttachmentRule { id: string; leavePolicyRuleId: string; attachmentRequirement: AttachmentRequirement; thresholdQuantity?: number | null; documentLabel?: string | null; concurrencyToken: string }
export type LeavePolicyAttachmentRuleRequest = Omit<LeavePolicyAttachmentRule, 'id' | 'leavePolicyRuleId' | 'concurrencyToken'> & { concurrencyToken?: string | null }
export function getLeaveTypeAttachmentRule(policyId: string, versionId: string, leaveTypeId: string, signal?: AbortSignal): Promise<LeavePolicyAttachmentRule | null> { return request(() => api.get<ApiResponse<LeavePolicyAttachmentRule | null>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types/${leaveTypeId}/attachments`, { signal })) }
export function saveLeaveTypeAttachmentRule(policyId: string, versionId: string, leaveTypeId: string, body: LeavePolicyAttachmentRuleRequest): Promise<LeavePolicyAttachmentRule | null> { return request(() => api.put<ApiResponse<LeavePolicyAttachmentRule | null>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types/${leaveTypeId}/attachments`, body)) }
export type ClubbingRelation = 'NotAllowed'
export interface LeavePolicyClubbingRule { id: string; leavePolicyVersionId: string; leaveTypeAId: string; leaveTypeBId: string; relation: ClubbingRelation }
export interface LeavePolicyClubbingRequest { rules: Array<{ leaveTypeAId: string; leaveTypeBId: string; relation: ClubbingRelation }>; concurrencyToken?: string | null }
export function getLeavePolicyClubbing(policyId: string, versionId: string, signal?: AbortSignal): Promise<LeavePolicyClubbingRule[]> { return request(() => api.get<ApiResponse<LeavePolicyClubbingRule[]>>(`/api/leave-policies/${policyId}/versions/${versionId}/clubbing`, { signal })) }
export function saveLeavePolicyClubbing(policyId: string, versionId: string, body: LeavePolicyClubbingRequest): Promise<LeavePolicyClubbingRule[]> { return request(() => api.put<ApiResponse<LeavePolicyClubbingRule[]>>(`/api/leave-policies/${policyId}/versions/${versionId}/clubbing`, body)) }
export interface LeavePolicyCancellationRule { id: string; leavePolicyRuleId: string; withdrawAllowed: boolean; cancelAllowed: boolean; modifyAllowed: boolean; concurrencyToken: string }
export type LeavePolicyCancellationRuleRequest = Omit<LeavePolicyCancellationRule, 'id' | 'leavePolicyRuleId' | 'concurrencyToken'> & { concurrencyToken?: string | null }
export function getLeaveTypeCancellationRule(policyId: string, versionId: string, leaveTypeId: string, signal?: AbortSignal): Promise<LeavePolicyCancellationRule | null> { return request(() => api.get<ApiResponse<LeavePolicyCancellationRule | null>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types/${leaveTypeId}/cancellation`, { signal })) }
export function saveLeaveTypeCancellationRule(policyId: string, versionId: string, leaveTypeId: string, body: LeavePolicyCancellationRuleRequest): Promise<LeavePolicyCancellationRule | null> { return request(() => api.put<ApiResponse<LeavePolicyCancellationRule | null>>(`/api/leave-policies/${policyId}/versions/${versionId}/leave-types/${leaveTypeId}/cancellation`, body)) }

export function validateLeavePolicyVersion(policyId: string, versionId: string): Promise<LeavePolicyValidation> {
  return request(() => api.post<ApiResponse<LeavePolicyValidation>>(`/api/leave-policies/${policyId}/versions/${versionId}/validate`))
}

export function publishLeavePolicyVersion(policyId: string, versionId: string): Promise<LeavePolicyVersion> {
  return request(() => api.post<ApiResponse<LeavePolicyVersion>>(`/api/leave-policies/${policyId}/versions/${versionId}/publish`))
}

export function retireLeavePolicyVersion(policyId: string, versionId: string): Promise<LeavePolicyVersion> {
  return request(() => api.post<ApiResponse<LeavePolicyVersion>>(`/api/leave-policies/${policyId}/versions/${versionId}/retire`))
}
