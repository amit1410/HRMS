import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'

export interface LinkState { userId: string; status: 'Linked' | 'Unlinked' | 'Invalid'; currentLink: { linkId: string; employeeId: string; displayName: string; employeeCode?: string | null; originalActorUserId: string; originalOccurredAtUtc: string } | null; revision: string | null }
export interface Candidate { id: string; displayName: string; email?: string | null; employeeCode?: string | null; eligibility?: string | null }
export interface LinkEvent { id: string; sequence: number; operation: string; actorUserId: string; beforeEmployeeId?: string | null; afterEmployeeId?: string | null; reason: string; occurredAtUtc: string }
export interface LinkMutation { employeeId: string; expectedRevision?: string | null; reason: string }
export interface UnlinkMutation { expectedLinkId: string; expectedEmployeeId: string; expectedRevision: string; reason: string }
export interface ReplaceMutation extends UnlinkMutation { newEmployeeId: string }

export const getLinkState = (userId: string) => request<LinkState>(() => api.get<ApiResponse<LinkState>>(`/api/account-employee-links/users/${userId}`))
export const getUserCandidates = (params: { search?: string; page?: number; pageSize?: number } = {}) => request<PagedResult<Candidate>>(() => api.get<ApiResponse<PagedResult<Candidate>>>('/api/account-employee-links/candidates/users', { params: cleanParams(params) }))
export const getEmployeeCandidates = (params: { search?: string; page?: number; pageSize?: number } = {}) => request<PagedResult<Candidate>>(() => api.get<ApiResponse<PagedResult<Candidate>>>('/api/account-employee-links/candidates/employees', { params: cleanParams(params) }))
export const getLinkHistory = (userId: string) => request<PagedResult<LinkEvent>>(() => api.get<ApiResponse<PagedResult<LinkEvent>>>(`/api/account-employee-links/users/${userId}/history`))
export const linkAccount = (userId: string, body: LinkMutation) => request<LinkState>(() => api.post<ApiResponse<LinkState>>(`/api/account-employee-links/users/${userId}/link`, body))
export const unlinkAccount = (userId: string, body: UnlinkMutation) => request<LinkState>(() => api.post<ApiResponse<LinkState>>(`/api/account-employee-links/users/${userId}/unlink`, body))
export const replaceAccount = (userId: string, body: ReplaceMutation) => request<LinkState>(() => api.post<ApiResponse<LinkState>>(`/api/account-employee-links/users/${userId}/replace`, body))
