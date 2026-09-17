import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export interface PageAccessRole { id: number; name: string; description?: string | null }
export interface PageAccessAction { code: string; name: string; permission: string; allowed: boolean }
export interface PageAccessPage { code: string; name: string; moduleCode: string; route: string; requiredPermissions: string[]; canView: boolean; actions: PageAccessAction[] }
export interface PageAccessMatrix { role: PageAccessRole; pages: PageAccessPage[]; grantedPermissions: string[] }
export interface PageAccessHistoryItem { id: string; occurredAtUtc: string; eventType: string; action: string; roleId?: number | null; roleName?: string | null; userId?: string | null; userRoleAssignmentId?: string | null; permissionCode?: string | null; scopeDimension?: string | null; scopeValueId?: string | null; scopeValueDisplay?: string | null; oldValue?: string | null; newValue?: string | null; actorUserId: string; actorDisplayName?: string | null; reason?: string | null }
export interface PageAccessHistoryPage { items: PageAccessHistoryItem[]; page: number; pageSize: number; totalCount: number; totalPages: number; hasPreviousPage: boolean; hasNextPage: boolean }

export function listPageAccessRoles(): Promise<PageAccessRole[]> {
  return request<PageAccessRole[]>(() => api.get<ApiResponse<PageAccessRole[]>>('/api/page-access/roles'))
}

export function getPageAccessMatrix(roleId: number): Promise<PageAccessMatrix> {
  return request<PageAccessMatrix>(() => api.get<ApiResponse<PageAccessMatrix>>(`/api/page-access/roles/${roleId}`))
}

export function updatePageAccess(roleId: number, permissions: string[]): Promise<PageAccessMatrix> {
  return request<PageAccessMatrix>(() => api.put<ApiResponse<PageAccessMatrix>>(`/api/page-access/roles/${roleId}`, { permissions }))
}

export function getPageAccessHistory(params: { roleId?: number; userId?: string; eventType?: string; fromDate?: string; toDate?: string; page?: number; pageSize?: number } = {}): Promise<PageAccessHistoryPage> {
  return request<PageAccessHistoryPage>(() => api.get<ApiResponse<PageAccessHistoryPage>>('/api/page-access/history', { params }))
}
