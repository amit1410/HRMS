import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export interface EmployeeCodeConfiguration {
  id: string
  autoGenerate: boolean
  assignmentMode?: 'Manual' | 'Auto' | number
  generationMethod?: 'Simple' | 'RuleBased' | number | null
  prefix: string
  nextNumber: number
  padding: number
  separator: string
  effectiveFrom: string
  effectiveTo?: string | null
  versionId?: string | null
  isActive: boolean
}

export interface EmployeeCodeConfigurationRequest {
  versionId?: string | null
  isActive?: boolean
  autoGenerate: boolean
  assignmentMode?: 'Manual' | 'Auto' | number
  generationMethod?: 'Simple' | 'RuleBased' | number | null
  prefix: string
  nextNumber: number
  padding: number
  separator: string
  effectiveFrom: string
  effectiveTo?: string | null
}

export interface EmployeeCodeRule {
  id: string
  name: string
  priority: number
  isDefault: boolean
  status: number | string
  conditions: Array<{ id: string; field: number | string; operator: number | string; referenceId?: string | null; value?: string | null }>
  segments: Array<{ id: string; sequenceOrder: number; segmentType: number | string; fixedValue?: string | null; paddingLength?: number | null }>
  configurationVersionId?: string | null
}

export interface EmployeeCodeRuleRequest {
  configurationVersionId?: string | null
  name: string
  priority: number
  isDefault: boolean
  status: number
  conditions: Array<{ id?: string; field: number; operator: number; value?: string | null; referenceId?: string | null }>
  segments: Array<{ id?: string; sequenceOrder: number; segmentType: number; fixedValue?: string | null; paddingLength?: number | null }>
}

export function getEmployeeCodeConfiguration(signal?: AbortSignal) {
  return request<EmployeeCodeConfiguration>(() =>
    api.get<ApiResponse<EmployeeCodeConfiguration>>('/api/employee-code-configuration', { signal }),
  )
}

export function saveEmployeeCodeConfiguration(body: EmployeeCodeConfigurationRequest) {
  return request<EmployeeCodeConfiguration>(() =>
    api.put<ApiResponse<EmployeeCodeConfiguration>>('/api/employee-code-configuration', body),
  )
}

export function getEmployeeCodeRules(signal?: AbortSignal) {
  return request<EmployeeCodeRule[]>(() => api.get<ApiResponse<EmployeeCodeRule[]>>('/api/employee-code-configuration/rules', { signal }))
}

export function saveEmployeeCodeRule(body: EmployeeCodeRuleRequest) {
  return request<EmployeeCodeRule>(() => api.post<ApiResponse<EmployeeCodeRule>>('/api/employee-code-configuration/rules', body))
}

export function updateEmployeeCodeRule(id: string, body: EmployeeCodeRuleRequest) {
  return request<EmployeeCodeRule>(() => api.put<ApiResponse<EmployeeCodeRule>>(`/api/employee-code-configuration/rules/${id}`, body))
}

export function getEmployeeCodeRule(id: string, signal?: AbortSignal) {
  return request<EmployeeCodeRule>(() => api.get<ApiResponse<EmployeeCodeRule>>(`/api/employee-code-configuration/rules/${id}`, { signal }))
}

export function deleteEmployeeCodeRule(id: string) {
  return request<EmployeeCodeRule>(() => api.delete<ApiResponse<EmployeeCodeRule>>(`/api/employee-code-configuration/rules/${id}`))
}
