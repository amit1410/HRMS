import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'
import type { QueryParams } from './client.ts'

export type SalaryComponentType = 'Earning' | 'Deduction' | 'EmployerContribution' | 'Reimbursement' | 'Information'
export type SalaryCalculationType = 'FixedAmount' | 'Percentage' | 'Formula' | 'ManualInput' | 'AttendanceBased' | 'LeaveBased' | 'Statutory'
export type SalaryStatutoryType = 'None' | 'ProvidentFund' | 'Esi' | 'ProfessionalTax' | 'LabourWelfareFund' | 'IncomeTax' | 'Gratuity' | 'Other'

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
