import { api, request } from './client'
import type { ApiResponse } from './types'

export type SeparationExitStatus = 'NotStarted' | 'InProgress' | 'Completed' | 'Failed' | 'CorrectionRequired'
export interface SeparationExitDashboardItem { separationId: string; employeeId: string; employeeCode?: string | null; employeeName: string; approvedLastWorkingDate?: string | null; separationStatus: string; executionStatus: SeparationExitStatus; employmentStatus: string; isReady: boolean; blockerCount: number; closedAtUtc?: string | null }
export interface SeparationExitPage { items: SeparationExitDashboardItem[]; page: number; pageSize: number; totalCount: number }
export interface SeparationExitReadiness { separationId: string; isReady: boolean; blockers: Array<{ code: string; message: string }>; approvedLastWorkingDate?: string | null; businessDate: string; executionStatus: SeparationExitStatus }
export interface SeparationExitExecution { id: string; employeeSeparationId: string; employeeId: string; status: SeparationExitStatus; finalLastWorkingDate?: string | null; startedAtUtc?: string | null; employmentExecutedAtUtc?: string | null; accessDeprovisionedAtUtc?: string | null; completedAtUtc?: string | null; failureCode?: string | null; failureMessage?: string | null; concurrencyVersion: number }

export function getSeparationExitDashboard(values: Record<string, string | number | undefined>, signal?: AbortSignal): Promise<SeparationExitPage> { return request(() => api.get<ApiResponse<SeparationExitPage>>(`/api/separation/exit/dashboard`, { params: values, signal })) }
export function getSeparationExitReadiness(id: string, signal?: AbortSignal): Promise<SeparationExitReadiness> { return request(() => api.get<ApiResponse<SeparationExitReadiness>>(`/api/separation/exit/${id}/readiness`, { signal })) }
export function executeSeparationExit(id: string, expectedConcurrencyVersion = 0): Promise<SeparationExitExecution> { return request(() => api.post<ApiResponse<SeparationExitExecution>>(`/api/separation/exit/${id}/execute`, { expectedConcurrencyVersion, idempotencyKey: crypto.randomUUID() })) }
export function retrySeparationExit(id: string): Promise<SeparationExitExecution> { return request(() => api.post<ApiResponse<SeparationExitExecution>>(`/api/separation/exit/${id}/retry`, { idempotencyKey: crypto.randomUUID(), reason: 'Operator retry' })) }
