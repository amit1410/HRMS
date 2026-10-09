import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'

export type ReviewQuestionType = 'Rating' | 'YesNo' | 'Text' | 'Dropdown' | 'MultiSelect'

// --- Rating scales ---

export interface ConfirmationRatingScale {
  id: string
  code: string
  name: string
  description?: string | null
  minRating: number
  maxRating: number
  isActive: boolean
  displayOrder: number
  effectiveFrom?: string | null
  effectiveTo?: string | null
  valueCount: number
  createdDate: string
  modifiedDate?: string | null
}

export interface ConfirmationRatingScaleValue {
  id?: string
  ratingValue: number
  label: string
  description?: string | null
  displayOrder: number
  isActive: boolean
}

export interface ConfirmationRatingScaleDetail {
  scale: ConfirmationRatingScale
  values: ConfirmationRatingScaleValue[]
}

export interface ConfirmationRatingScaleRequest {
  code: string
  name: string
  description?: string | null
  minRating: number
  maxRating: number
  isActive: boolean
  displayOrder: number
  effectiveFrom?: string | null
  effectiveTo?: string | null
  values: ConfirmationRatingScaleValue[]
}

export interface ConfirmationRatingScaleQuery {
  page?: number
  pageSize?: number
  search?: string
  isActive?: boolean
  sortBy?: string
  sortDescending?: boolean
}

// --- Review questions ---

export interface ConfirmationReviewQuestion {
  id: string
  code: string
  questionText: string
  helpText?: string | null
  questionType: ReviewQuestionType
  isMandatory: boolean
  isActive: boolean
  displayOrder: number
  effectiveFrom?: string | null
  effectiveTo?: string | null
  confirmationRatingScaleId?: string | null
  ratingScaleName?: string | null
  optionCount: number
  applicabilitySetCount: number
  createdDate: string
  modifiedDate?: string | null
}

export interface ConfirmationReviewQuestionOption {
  id?: string
  code: string
  label: string
  displayOrder: number
  isActive: boolean
}

export interface ConfirmationReviewQuestionApplicability {
  id?: string
  confirmationPolicyId?: string | null
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

export interface ConfirmationReviewQuestionDetail {
  question: ConfirmationReviewQuestion
  options: ConfirmationReviewQuestionOption[]
  applicability: ConfirmationReviewQuestionApplicability[]
}

export interface ConfirmationReviewQuestionRequest {
  code: string
  questionText: string
  helpText?: string | null
  questionType: ReviewQuestionType
  isMandatory: boolean
  isActive: boolean
  displayOrder: number
  effectiveFrom?: string | null
  effectiveTo?: string | null
  confirmationRatingScaleId?: string | null
  options?: ConfirmationReviewQuestionOption[]
  applicability?: ConfirmationReviewQuestionApplicability[]
}

export interface ConfirmationReviewQuestionQuery {
  page?: number
  pageSize?: number
  search?: string
  isActive?: boolean
  questionType?: ReviewQuestionType
  sortBy?: string
  sortDescending?: boolean
}

const scalesBase = '/api/confirmation-rating-scales'
const questionsBase = '/api/confirmation-review-questions'

export function listConfirmationRatingScales(query: ConfirmationRatingScaleQuery = {}, signal?: AbortSignal): Promise<PagedResult<ConfirmationRatingScale>> {
  return request(() => api.get<ApiResponse<PagedResult<ConfirmationRatingScale>>>(scalesBase, { params: cleanParams({ ...query }), signal }))
}

export function getConfirmationRatingScale(id: string, signal?: AbortSignal): Promise<ConfirmationRatingScaleDetail> {
  return request(() => api.get<ApiResponse<ConfirmationRatingScaleDetail>>(`${scalesBase}/${id}`, { signal }))
}

export function createConfirmationRatingScale(body: ConfirmationRatingScaleRequest): Promise<ConfirmationRatingScaleDetail> {
  return request(() => api.post<ApiResponse<ConfirmationRatingScaleDetail>>(scalesBase, body))
}

export function updateConfirmationRatingScale(id: string, body: ConfirmationRatingScaleRequest): Promise<ConfirmationRatingScaleDetail> {
  return request(() => api.put<ApiResponse<ConfirmationRatingScaleDetail>>(`${scalesBase}/${id}`, body))
}

export function listConfirmationReviewQuestions(query: ConfirmationReviewQuestionQuery = {}, signal?: AbortSignal): Promise<PagedResult<ConfirmationReviewQuestion>> {
  return request(() => api.get<ApiResponse<PagedResult<ConfirmationReviewQuestion>>>(questionsBase, { params: cleanParams({ ...query }), signal }))
}

export function getConfirmationReviewQuestion(id: string, signal?: AbortSignal): Promise<ConfirmationReviewQuestionDetail> {
  return request(() => api.get<ApiResponse<ConfirmationReviewQuestionDetail>>(`${questionsBase}/${id}`, { signal }))
}

export function createConfirmationReviewQuestion(body: ConfirmationReviewQuestionRequest): Promise<ConfirmationReviewQuestionDetail> {
  return request(() => api.post<ApiResponse<ConfirmationReviewQuestionDetail>>(questionsBase, body))
}

export function updateConfirmationReviewQuestion(id: string, body: ConfirmationReviewQuestionRequest): Promise<ConfirmationReviewQuestionDetail> {
  return request(() => api.put<ApiResponse<ConfirmationReviewQuestionDetail>>(`${questionsBase}/${id}`, body))
}
