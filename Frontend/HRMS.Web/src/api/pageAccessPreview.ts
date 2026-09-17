import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export interface AccessPreviewUser { userId: string; displayName: string; email: string }
export interface AccessPreviewRole { assignmentId: string; roleId: number; roleName: string; source: string; effectiveFrom: string; effectiveTo?: string | null; tenantWide: boolean; scopes: Array<{ scopeType: string; scopeEntityId: string }> }
export interface UserAccessPreview { userId: string; employeeId?: string | null; roles: AccessPreviewRole[]; permissions: string[]; pages: Array<{ code: string; name: string; route: string; moduleCode: string; children: unknown[] }>; hasManagerAccess: boolean; tenantWideRoleCount: number }

export function searchAccessPreviewUsers(search?: string): Promise<AccessPreviewUser[]> { return request(() => api.get<ApiResponse<AccessPreviewUser[]>>('/api/page-access/users', { params: search ? { search } : undefined })) }
export function getUserAccessPreview(userId: string): Promise<UserAccessPreview> { return request(() => api.get<ApiResponse<UserAccessPreview>>(`/api/page-access/users/${userId}/preview`)) }
