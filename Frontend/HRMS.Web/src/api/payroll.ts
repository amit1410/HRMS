import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'
import type { QueryParams } from './client.ts'

export type SalaryComponentType = 'Earning' | 'Deduction' | 'EmployerContribution' | 'Reimbursement' | 'Information'
export type SalaryCalculationType = 'FixedAmount' | 'Percentage' | 'Formula' | 'ManualInput' | 'AttendanceBased' | 'LeaveBased' | 'Statutory'
export type SalaryStatutoryType = 'None' | 'ProvidentFund' | 'Esi' | 'ProfessionalTax' | 'LabourWelfareFund' | 'IncomeTax' | 'Gratuity' | 'Other'
export type SalaryStructureCalculationType = 'FixedAmount' | 'Percentage' | 'Formula' | 'Manual'

export interface SalaryComponent {
  id: string
  code: string
  name: string
  description?: string | null
  componentType: SalaryComponentType
  calculationType: SalaryCalculationType
  statutoryType: SalaryStatutoryType
  isTaxable: boolean
  isStatutory: boolean
  isRecurring: boolean
  affectsGross: boolean
  affectsNetPay: boolean
  displayOrder: number
  effectiveFrom: string
  effectiveTo?: string | null
  isActive: boolean
  concurrencyVersion: number
}

export type SalaryComponentRequest = Omit<SalaryComponent, 'id' | 'concurrencyVersion'> & { expectedConcurrencyVersion?: number }

export function listSalaryComponents(params: QueryParams = {}, signal?: AbortSignal): Promise<PagedResult<SalaryComponent>> {
  return request<PagedResult<SalaryComponent>>(() => api.get<ApiResponse<PagedResult<SalaryComponent>>>('/api/payroll/salary-components', { params: cleanParams(params), signal }))
}
export function createSalaryComponent(body: SalaryComponentRequest): Promise<SalaryComponent> {
  return request<SalaryComponent>(() => api.post<ApiResponse<SalaryComponent>>('/api/payroll/salary-components', body))
}
export function getSalaryComponent(id: string): Promise<SalaryComponent> {
  return request<SalaryComponent>(() => api.get<ApiResponse<SalaryComponent>>(`/api/payroll/salary-components/${id}`))
}
export function updateSalaryComponent(id: string, body: SalaryComponentRequest): Promise<SalaryComponent> {
  return request<SalaryComponent>(() => api.put<ApiResponse<SalaryComponent>>(`/api/payroll/salary-components/${id}`, body))
}
export function getSalaryComponentHistory(id: string): Promise<SalaryComponentHistory[]> {
  return request<SalaryComponentHistory[]>(() => api.get<ApiResponse<SalaryComponentHistory[]>>(`/api/payroll/salary-components/${id}/history`))
}
export function setSalaryComponentActive(id: string, active: boolean, version: number): Promise<SalaryComponent> {
  return request<SalaryComponent>(() => api.post<ApiResponse<SalaryComponent>>(`/api/payroll/salary-components/${id}/${active ? 'activate' : 'deactivate'}`, undefined, { params: { expectedConcurrencyVersion: version } }))
}

export interface SalaryComponentHistory {
  id: string
  salaryComponentId: string
  changeType: string
  code: string
  name: string
  componentType: SalaryComponentType
  calculationType: SalaryCalculationType
  statutoryType: SalaryStatutoryType
  isTaxable: boolean
  isStatutory: boolean
  isRecurring: boolean
  affectsGross: boolean
  affectsNetPay: boolean
  effectiveFrom: string
  effectiveTo?: string | null
  isActive: boolean
  actorUserId?: string | null
  changedAtUtc: string
}

export interface SalaryStructureComponent {
  id: string
  salaryComponentId: string
  salaryComponentCode: string
  salaryComponentName: string
  componentType: SalaryComponentType
  sequence: number
  calculationType: SalaryStructureCalculationType
  value?: number | null
  percentageOfComponentId?: string | null
  formula?: string | null
  isProratable: boolean
  isEditableAtEmployeeLevel: boolean
  minimumAmount?: number | null
  maximumAmount?: number | null
  isActive: boolean
  effectiveFrom: string
  effectiveTo?: string | null
}

export interface SalaryStructure {
  id: string
  versionId: string
  code: string
  name: string
  description?: string | null
  effectiveFrom: string
  effectiveTo?: string | null
  isActive: boolean
  componentCount: number
  concurrencyVersion: number
  components: SalaryStructureComponent[]
}

export interface SalaryStructureComponentRequest {
  salaryComponentId: string
  sequence: number
  calculationType: SalaryStructureCalculationType
  value?: number | null
  percentageOfComponentId?: string | null
  formula?: string | null
  isProratable: boolean
  isEditableAtEmployeeLevel: boolean
  minimumAmount?: number | null
  maximumAmount?: number | null
  isActive: boolean
  effectiveFrom?: string | null
  effectiveTo?: string | null
}

export interface SalaryStructureRequest {
  code: string
  name: string
  description?: string | null
  effectiveFrom: string
  effectiveTo?: string | null
  isActive: boolean
  components: SalaryStructureComponentRequest[]
  expectedConcurrencyVersion?: number
}

export interface SalaryStructureHistory {
  id: string
  salaryStructureId: string
  salaryStructureVersionId?: string | null
  changeType: string
  code: string
  name: string
  description?: string | null
  effectiveFrom?: string | null
  effectiveTo?: string | null
  isActive: boolean
  componentsJson: string
  actorUserId?: string | null
  changedAtUtc: string
}

export function listSalaryStructures(params: QueryParams = {}, signal?: AbortSignal): Promise<PagedResult<SalaryStructure>> {
  return request<PagedResult<SalaryStructure>>(() => api.get<ApiResponse<PagedResult<SalaryStructure>>>('/api/payroll/salary-structures', { params: cleanParams(params), signal }))
}
export function getSalaryStructure(id: string): Promise<SalaryStructure> {
  return request<SalaryStructure>(() => api.get<ApiResponse<SalaryStructure>>(`/api/payroll/salary-structures/${id}`))
}
export function createSalaryStructure(body: SalaryStructureRequest): Promise<SalaryStructure> {
  return request<SalaryStructure>(() => api.post<ApiResponse<SalaryStructure>>('/api/payroll/salary-structures', body))
}
export function updateSalaryStructure(id: string, body: SalaryStructureRequest): Promise<SalaryStructure> {
  return request<SalaryStructure>(() => api.put<ApiResponse<SalaryStructure>>(`/api/payroll/salary-structures/${id}`, body))
}
export function setSalaryStructureActive(id: string, active: boolean, version: number): Promise<SalaryStructure> {
  return request<SalaryStructure>(() => api.post<ApiResponse<SalaryStructure>>(`/api/payroll/salary-structures/${id}/${active ? 'activate' : 'deactivate'}`, undefined, { params: { expectedConcurrencyVersion: version } }))
}
export function getSalaryStructureHistory(id: string): Promise<SalaryStructureHistory[]> {
  return request<SalaryStructureHistory[]>(() => api.get<ApiResponse<SalaryStructureHistory[]>>(`/api/payroll/salary-structures/${id}/history`))
}
