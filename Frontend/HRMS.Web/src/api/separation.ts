import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export type SeparationStatus = 'Draft' | 'Submitted' | 'ManagerReview' | 'HrReview' | 'Approved' | 'Rejected' | 'Withdrawn' | 'NoticePeriod' | 'ReadyForExit' | 'Exited' | 'Cancelled'
export interface SeparationReason { id: string; code: string; name: string; description?: string | null; category: string; employeeInitiatedAllowed: boolean; employerInitiatedAllowed: boolean; isActive: boolean; effectiveFrom: string; effectiveTo?: string | null; displayOrder: number }
export interface SeparationCase { id: string; employeeId: string; separationNumber: string; separationType: string; reasonId: string; reasonName: string; initiatedBy: string; requestDate: string; proposedLastWorkingDate: string; approvedLastWorkingDate?: string | null; noticeStartDate?: string | null; noticeEndDate?: string | null; noticePeriodDays?: number | null; status: SeparationStatus; createdAtUtc: string }
export interface SeparationEvent { id: string; eventType: string; fromStatus?: SeparationStatus | null; toStatus: SeparationStatus; actorUserId?: string | null; occurredAtUtc: string; reason?: string | null; comment?: string | null }

export function listSeparationReasons(signal?: AbortSignal): Promise<SeparationReason[]> { return request<SeparationReason[]>(() => api.get<ApiResponse<SeparationReason[]>>('/api/separation/reasons', { signal })) }
export function getMySeparation(signal?: AbortSignal): Promise<SeparationCase> { return request<SeparationCase>(() => api.get<ApiResponse<SeparationCase>>('/api/separation/me/current', { signal })) }
export function getMySeparationHistory(signal?: AbortSignal): Promise<SeparationCase[]> { return request<SeparationCase[]>(() => api.get<ApiResponse<SeparationCase[]>>('/api/separation/me/history', { signal })) }
export function getSeparationHistory(id: string, signal?: AbortSignal): Promise<SeparationEvent[]> { return request<SeparationEvent[]>(() => api.get<ApiResponse<SeparationEvent[]>>(`/api/separation/${id}/history`, { signal })) }
export function createMySeparation(body: { reasonId: string; requestDate: string; proposedLastWorkingDate: string; remarks?: string }): Promise<SeparationCase> { return request<SeparationCase>(() => api.post<ApiResponse<SeparationCase>>('/api/separation/me', body)) }
export function submitSeparation(id: string): Promise<SeparationCase> { return request<SeparationCase>(() => api.post<ApiResponse<SeparationCase>>(`/api/separation/${id}/submit`)) }
export function withdrawSeparation(id: string): Promise<SeparationCase> { return request<SeparationCase>(() => api.post<ApiResponse<SeparationCase>>(`/api/separation/${id}/withdraw`)) }
export function getManagerSeparationInbox(signal?: AbortSignal): Promise<SeparationCase[]> { return request<SeparationCase[]>(() => api.get<ApiResponse<SeparationCase[]>>('/api/separation/inbox/manager', { signal })) }
export function getHrSeparationInbox(signal?: AbortSignal): Promise<SeparationCase[]> { return request<SeparationCase[]>(() => api.get<ApiResponse<SeparationCase[]>>('/api/separation/inbox/hr', { signal })) }
export function managerApproveSeparation(id: string): Promise<SeparationCase> { return request<SeparationCase>(() => api.post<ApiResponse<SeparationCase>>(`/api/separation/${id}/manager-approve`)) }
export function managerRejectSeparation(id: string, comments: string): Promise<SeparationCase> { return request<SeparationCase>(() => api.post<ApiResponse<SeparationCase>>(`/api/separation/${id}/manager-reject`, { comments })) }
export function hrApproveSeparation(id: string): Promise<SeparationCase> { return request<SeparationCase>(() => api.post<ApiResponse<SeparationCase>>(`/api/separation/${id}/hr-approve`)) }
export function hrRejectSeparation(id: string, comments: string): Promise<SeparationCase> { return request<SeparationCase>(() => api.post<ApiResponse<SeparationCase>>(`/api/separation/${id}/hr-reject`, { comments })) }
export function reviseSeparationLwd(id: string, newLastWorkingDate: string, reason: string): Promise<SeparationCase> { return request<SeparationCase>(() => api.post<ApiResponse<SeparationCase>>(`/api/separation/${id}/revise-lwd`, { newLastWorkingDate, reason })) }
