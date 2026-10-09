import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'
import type { ReviewQuestionType } from './confirmationReview.ts'

export type ConfirmationReviewCaseStatus = 'Open' | 'InReview' | 'Completed' | 'Cancelled' | 'Decided'
export type ConfirmationReviewerType = 'ReportingManager' | 'Hrbp'
export type ConfirmationReviewStatus = 'Draft' | 'Submitted' | 'Superseded'
export type ConfirmationOutcome = 'Pending' | 'Confirmed' | 'ProbationExtended' | 'NotConfirmed' | 'AdditionalReview'
export type ConfirmationApprovalRequirement = 'Manager' | 'Hrbp' | 'ManagerAndHrbp' | 'ManagerOrHrbp'
export type ApprovalSequence = 'Parallel' | 'ManagerThenHrbp'
export type OverallRatingMethod = 'AverageOfRatings' | 'MinimumRating'
export type ConfirmationReviewCaseEventType =
  | 'CaseCreated'
  | 'QuestionsSnapshotted'
  | 'ReviewersResolved'
  | 'DraftSaved'
  | 'ReviewSubmitted'
  | 'ReviewSuperseded'
  | 'DecisionCalculated'
  | 'DecisionSubmitted'
  | 'EmployeeConfirmed'
  | 'ProbationExtended'
  | 'MarkedNotConfirmed'
  | 'AdditionalReviewRequested'
  | 'ProbationCycleCreated'

export interface ConfirmationReviewCase {
  id: string
  employeeId: string
  employeeCode?: string | null
  employeeName?: string | null
  confirmationPolicyId: string
  confirmationPolicyVersionId: string
  probationCycleKey: string
  reviewRound: number
  probationStartDate: string
  probationEndDate: string
  dueDate: string
  status: ConfirmationReviewCaseStatus
  managerReviewerEmployeeId?: string | null
  hrbpReviewerEmployeeId?: string | null
  questionCount: number
  createdDate: string
  modifiedDate?: string | null
}

export interface ConfirmationReviewCaseOption {
  code: string
  label: string
  displayOrder: number
}

export interface ConfirmationReviewCaseRatingValue {
  ratingValue: number
  label: string
  displayOrder: number
}

export interface ConfirmationReviewCaseQuestion {
  id: string
  sourceQuestionId: string
  code: string
  questionText: string
  helpText?: string | null
  questionType: ReviewQuestionType
  isMandatory: boolean
  displayOrder: number
  sourceRatingScaleId?: string | null
  ratingScaleCode?: string | null
  ratingScaleName?: string | null
  ratingMin?: number | null
  ratingMax?: number | null
  options: ConfirmationReviewCaseOption[]
  ratingValues: ConfirmationReviewCaseRatingValue[]
}

export interface ConfirmationReviewResponse {
  confirmationReviewCaseQuestionId: string
  ratingValue?: number | null
  boolValue?: boolean | null
  textValue?: string | null
  selectedOptionCodes: string[]
}

export interface ConfirmationReviewerReview {
  id: string
  reviewerType: ConfirmationReviewerType
  reviewerEmployeeId?: string | null
  reviewRound: number
  versionNumber: number
  status: ConfirmationReviewStatus
  overallComments?: string | null
  submittedAtUtc?: string | null
  responses: ConfirmationReviewResponse[]
}

export interface ConfirmationReviewCaseDetail {
  case: ConfirmationReviewCase
  questions: ConfirmationReviewCaseQuestion[]
  reviews: ConfirmationReviewerReview[]
}

export interface ConfirmationReviewerForm {
  case: ConfirmationReviewCase
  reviewerType: ConfirmationReviewerType
  questions: ConfirmationReviewCaseQuestion[]
  currentReview?: ConfirmationReviewerReview | null
}

export interface ConfirmationReviewCaseEvent {
  id: string
  eventType: ConfirmationReviewCaseEventType
  confirmationReviewerReviewId?: string | null
  actorUserId?: string | null
  details?: string | null
  occurredAtUtc: string
}

export interface ConfirmationReviewResponseInput {
  confirmationReviewCaseQuestionId: string
  ratingValue?: number | null
  boolValue?: boolean | null
  textValue?: string | null
  selectedOptionCodes?: string[]
}

export interface SaveReviewerDraftRequest {
  overallComments?: string | null
  responses?: ConfirmationReviewResponseInput[]
}

export interface ConfirmationReviewCaseQuery {
  page?: number
  pageSize?: number
  employeeId?: string
  status?: ConfirmationReviewCaseStatus
  sortBy?: string
  sortDescending?: boolean
}

const base = '/api/confirmation-review-cases'
const reviewerSegment = (reviewerType: ConfirmationReviewerType) => (reviewerType === 'ReportingManager' ? 'manager' : 'hrbp')

export function listConfirmationReviewCases(query: ConfirmationReviewCaseQuery = {}, signal?: AbortSignal): Promise<PagedResult<ConfirmationReviewCase>> {
  return request(() => api.get<ApiResponse<PagedResult<ConfirmationReviewCase>>>(base, { params: cleanParams({ ...query }), signal }))
}

export function getConfirmationReviewCase(id: string, signal?: AbortSignal): Promise<ConfirmationReviewCaseDetail> {
  return request(() => api.get<ApiResponse<ConfirmationReviewCaseDetail>>(`${base}/${id}`, { signal }))
}

export function createConfirmationReviewCase(employeeId: string, effectiveDate?: string | null): Promise<ConfirmationReviewCaseDetail> {
  return request(() => api.post<ApiResponse<ConfirmationReviewCaseDetail>>(base, { employeeId, effectiveDate }))
}

export function getConfirmationReviewerForm(id: string, reviewerType: ConfirmationReviewerType, signal?: AbortSignal): Promise<ConfirmationReviewerForm> {
  return request(() => api.get<ApiResponse<ConfirmationReviewerForm>>(`${base}/${id}/reviewer-form`, { params: { reviewerType }, signal }))
}

export function saveConfirmationReviewerDraft(id: string, reviewerType: ConfirmationReviewerType, body: SaveReviewerDraftRequest): Promise<ConfirmationReviewerReview> {
  return request(() => api.put<ApiResponse<ConfirmationReviewerReview>>(`${base}/${id}/reviews/${reviewerSegment(reviewerType)}/draft`, body))
}

export function submitConfirmationReviewerReview(id: string, reviewerType: ConfirmationReviewerType, body: SaveReviewerDraftRequest): Promise<ConfirmationReviewerReview> {
  return request(() => api.post<ApiResponse<ConfirmationReviewerReview>>(`${base}/${id}/reviews/${reviewerSegment(reviewerType)}/submit`, body))
}

export function getConfirmationReviewCaseHistory(id: string, signal?: AbortSignal): Promise<ConfirmationReviewCaseEvent[]> {
  return request(() => api.get<ApiResponse<ConfirmationReviewCaseEvent[]>>(`${base}/${id}/history`, { signal }))
}

// ── Phase 9c-3 decision workflow ───────────────────────────────────────────

export interface ConfirmationReviewerStatus {
  reviewerType: ConfirmationReviewerType
  isRequired: boolean
  isSubmitted: boolean
  hasComments: boolean
  ratingAnswerCount: number
}

export interface ConfirmationDecisionPreview {
  caseId: string
  status: ConfirmationReviewCaseStatus
  caseOutcome: ConfirmationOutcome
  cycleNumber: number
  requiredApproval: ConfirmationApprovalRequirement
  approvalSequence: ApprovalSequence
  overallRatingMethod: OverallRatingMethod
  reviewers: ConfirmationReviewerStatus[]
  isReadyForDecision: boolean
  blockingReasons: string[]
  overallRating?: number | null
  appliedRuleId?: string | null
  appliedRuleName?: string | null
  calculatedOutcome: ConfirmationOutcome
  explanation: string
  permittedOutcomes: ConfirmationOutcome[]
}

export interface SubmitConfirmationDecisionRequest {
  outcome: ConfirmationOutcome
  decisionReason?: string | null
  confirmationDateOverride?: string | null
}

export interface ConfirmationDecisionResult {
  decisionId: string
  caseId: string
  outcome: ConfirmationOutcome
  previousStatus: ConfirmationReviewCaseStatus
  newStatus: ConfirmationReviewCaseStatus
  calculatedOverallRating?: number | null
  appliedRuleId?: string | null
  decidedAtUtc: string
  confirmationDate?: string | null
  jobStatus?: string | null
  newCycleId?: string | null
  newCycleNumber?: number | null
  newProbationEndDate?: string | null
  newCaseId?: string | null
  message: string
}

export interface ConfirmationDecisionRecord {
  id: string
  probationCycleKey: string
  outcome: ConfirmationOutcome
  decisionReason?: string | null
  decisionMakerUserId?: string | null
  decisionMakerEmployeeId?: string | null
  decidedAtUtc: string
  calculatedOverallRating?: number | null
  appliedRuleId?: string | null
  previousCaseStatus: ConfirmationReviewCaseStatus
  newCaseStatus: ConfirmationReviewCaseStatus
}

export interface ConfirmationDecisionCalculationRecord {
  id: string
  overallRating?: number | null
  appliedRuleId?: string | null
  calculatedOutcome: ConfirmationOutcome
  explanation?: string | null
  actorUserId?: string | null
  calculatedAtUtc: string
}

export interface ConfirmationProbationExtensionRecord {
  id: string
  cycleNumber: number
  previousEndDate: string
  extensionDuration: number
  extensionDurationUnit: string
  newEndDate: string
  decisionMakerUserId?: string | null
  reason?: string | null
  extendedAtUtc: string
}

export interface ConfirmationDecisionHistory {
  caseId: string
  decisions: ConfirmationDecisionRecord[]
  calculations: ConfirmationDecisionCalculationRecord[]
  extensions: ConfirmationProbationExtensionRecord[]
}

export function previewConfirmationDecision(id: string): Promise<ConfirmationDecisionPreview> {
  return request(() => api.post<ApiResponse<ConfirmationDecisionPreview>>(`${base}/${id}/decision/preview`, {}))
}

export function submitConfirmationDecision(id: string, body: SubmitConfirmationDecisionRequest): Promise<ConfirmationDecisionResult> {
  return request(() => api.post<ApiResponse<ConfirmationDecisionResult>>(`${base}/${id}/decision`, body))
}

export function getConfirmationDecisionHistory(id: string, signal?: AbortSignal): Promise<ConfirmationDecisionHistory> {
  return request(() => api.get<ApiResponse<ConfirmationDecisionHistory>>(`${base}/${id}/decision/history`, { signal }))
}
