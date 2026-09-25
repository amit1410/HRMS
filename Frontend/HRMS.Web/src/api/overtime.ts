import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export type OvertimeCategory = 'NormalDay' | 'WeekOff' | 'Holiday'
export type OvertimeRequest = { employeeId: string; workDate: string; requestedMinutes: number; category: OvertimeCategory; reason?: string | null }
export type OvertimeRequestResult = { id: string; employeeId: string; workDate: string; requestedMinutes: number; actualEligibleMinutes: number; approvedMinutes: number; category: number | string; status: number | string; policyId: string; reason?: string | null; concurrencyVersion: number }

export function createOvertimeRequest(value: OvertimeRequest): Promise<OvertimeRequestResult> {
  return request(() => api.post<ApiResponse<OvertimeRequestResult>>('/api/attendance/overtime/requests', value))
}

export function submitOvertimeRequest(id: string): Promise<OvertimeRequestResult> {
  return request(() => api.post<ApiResponse<OvertimeRequestResult>>(`/api/attendance/overtime/requests/${id}/submit`))
}

export function approveOvertimeRequest(id: string, expectedConcurrencyVersion?: number): Promise<OvertimeRequestResult> {
  return request(() => api.post<ApiResponse<OvertimeRequestResult>>(`/api/attendance/overtime/requests/${id}/approve`, { expectedConcurrencyVersion: expectedConcurrencyVersion ?? null }))
}

export function rejectOvertimeRequest(id: string, reason: string, expectedConcurrencyVersion?: number): Promise<OvertimeRequestResult> {
  return request(() => api.post<ApiResponse<OvertimeRequestResult>>(`/api/attendance/overtime/requests/${id}/reject`, { reason, expectedConcurrencyVersion: expectedConcurrencyVersion ?? null }))
}

export function finalizeOvertimePeriod(periodId: string): Promise<unknown> {
  return request(() => api.post<ApiResponse<unknown>>(`/api/attendance/overtime/periods/${periodId}/finalize`))
}

export function reopenOvertimePeriod(periodId: string, reason: string): Promise<boolean> {
  return request(() => api.post<ApiResponse<boolean>>(`/api/attendance/overtime/periods/${periodId}/reopen`, { reason }))
}
