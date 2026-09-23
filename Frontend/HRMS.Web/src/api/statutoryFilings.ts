import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export type StatutoryFilingDefinition = { id: string; code: string; name: string; filingType: string; jurisdictionCode?: string | null; requiresApproval: boolean; destinationType: string; isActive: boolean }
export type StatutoryFilingRun = { id: string; definitionId: string; filingPeriod: string; status: string; rowCount: number; validationErrorCount: number; validationWarningCount: number; packageId?: string | null; packageHash?: string | null; submissionId?: string | null; externalReference?: string | null }
export type StatutoryFilingConnectionProfile = { id: string; name: string; connectorType: string; endpoint?: string | null; isActive: boolean; effectiveFrom: string; effectiveTo?: string | null; lastValidationStatus?: string | null; lastValidatedAtUtc?: string | null; hasSecretReference: boolean }
const path = '/api/payroll/statutory-filings'
export function listStatutoryFilingDefinitions(signal?: AbortSignal) { return request<StatutoryFilingDefinition[]>(() => api.get<ApiResponse<StatutoryFilingDefinition[]>>(`${path}/definitions`, { signal })) }
export function listStatutoryFilingRuns(signal?: AbortSignal) { return request<StatutoryFilingRun[]>(() => api.get<ApiResponse<StatutoryFilingRun[]>>(`${path}/runs`, { signal })) }
export function createStatutoryFilingDefinition(body: Record<string, unknown>) { return request<StatutoryFilingDefinition>(() => api.post<ApiResponse<StatutoryFilingDefinition>>(`${path}/definitions`, body)) }
export function createStatutoryFilingRun(body: Record<string, unknown>) { return request<StatutoryFilingRun>(() => api.post<ApiResponse<StatutoryFilingRun>>(`${path}/runs`, body)) }
export function generateStatutoryFiling(id: string) { return request<StatutoryFilingRun>(() => api.post<ApiResponse<StatutoryFilingRun>>(`${path}/runs/${id}/generate`)) }
export function validateStatutoryFiling(id: string) { return request<StatutoryFilingRun>(() => api.post<ApiResponse<StatutoryFilingRun>>(`${path}/runs/${id}/validate`)) }
export function listStatutoryFilingConnections(signal?: AbortSignal) { return request<StatutoryFilingConnectionProfile[]>(() => api.get<ApiResponse<StatutoryFilingConnectionProfile[]>>(`${path}/connections`, { signal })) }
export function createStatutoryFilingConnection(body: Record<string, unknown>) { return request<StatutoryFilingConnectionProfile>(() => api.post<ApiResponse<StatutoryFilingConnectionProfile>>(`${path}/connections`, body)) }
