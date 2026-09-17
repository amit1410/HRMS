import { api, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'

export type RoleAssignmentSource = 'System' | 'Manual' | 'Rule'
export type RoleScopeType =
  | 'HoldingCompany'
  | 'Lob'
  | 'Organisation'
  | 'Department'
  | 'SubDepartment'
  | 'Section'
  | 'SubSection'
  | 'Function'
  | 'SubFunction'
  | 'Country'
  | 'Location'
  | 'WorkLocation'
  | 'CostCenter'
  | 'Grade'
  | 'Designation'
  | 'EmployeeType'
export type RoleAssignmentEventType = 'Assigned' | 'EffectiveDatesChanged' | 'Revoked' | 'ScopeAdded' | 'ScopeRemoved'

export interface RoleSummary { id: number; name: string; description?: string | null }
export interface RoleAssignmentScope { scopeType: RoleScopeType; scopeEntityId: string }
export interface RoleAssignment {
  assignmentId: string
  userId: string
  employeeId?: string | null
  employeeCode?: string | null
  employeeName?: string | null
  roleId: number
  roleName: string
  assignmentSource: RoleAssignmentSource
  effectiveFrom: string
  effectiveTo?: string | null
  isCurrentlyEffective: boolean
  reason?: string | null
  assignedByUserId?: string | null
  scopes: RoleAssignmentScope[]
  isSystemManaged: boolean
  canRevoke: boolean
  status: 'Active' | 'Scheduled' | 'Expired' | 'Revoked'
  isRevoked: boolean
  revokedEffectiveDate?: string | null
  scopeSummary?: string | null
}
export interface RoleAssignmentQuery { search?: string; roleId?: number; status?: RoleAssignment['status']; source?: RoleAssignmentSource; page?: number; pageSize?: number }
export interface RoleManagementCandidate { userId: string; employeeId?: string | null; employeeCode?: string | null; displayName: string; department?: string | null; designation?: string | null; location?: string | null }
export interface RoleAssignmentHistory {
  eventId: string
  assignmentId: string
  userId: string
  roleId: number
  roleName: string
  eventType: RoleAssignmentEventType
  effectiveFrom: string
  effectiveTo?: string | null
  assignmentSource: RoleAssignmentSource
  reason?: string | null
  performedByUserId?: string | null
  occurredAtUtc: string
}
export interface AssignRoleRequest {
  roleId: number
  effectiveFrom: string
  effectiveTo?: string | null
  reason?: string | null
  scopes?: RoleAssignmentScope[] | null
}
export interface RevokeRoleRequest { effectiveTo?: string | null; reason?: string | null }

export function listRoles(assignableOnly = false): Promise<RoleSummary[]> {
  return request<RoleSummary[]>(() => api.get<ApiResponse<RoleSummary[]>>('/api/role-assignments/roles', { params: { assignableOnly } }))
}

export function listAssignments(query: RoleAssignmentQuery = {}): Promise<PagedResult<RoleAssignment>> {
  return request<PagedResult<RoleAssignment>>(() => api.get<ApiResponse<PagedResult<RoleAssignment>>>('/api/role-assignments', { params: query }))
}

export function listRoleCandidates(params: { search?: string; page?: number; pageSize?: number } = {}): Promise<PagedResult<RoleManagementCandidate>> {
  return request<PagedResult<RoleManagementCandidate>>(() => api.get<ApiResponse<PagedResult<RoleManagementCandidate>>>('/api/role-assignments/candidates', { params }))
}

export function getUserAssignments(userId: string): Promise<RoleAssignment[]> {
  return request<RoleAssignment[]>(() => api.get<ApiResponse<RoleAssignment[]>>(`/api/role-assignments/users/${userId}`))
}

export function assignRole(userId: string, body: AssignRoleRequest): Promise<RoleAssignment> {
  return request<RoleAssignment>(() => api.post<ApiResponse<RoleAssignment>>(`/api/role-assignments/users/${userId}`, body))
}

export function revokeRole(assignmentId: string, body: RevokeRoleRequest): Promise<RoleAssignment> {
  return request<RoleAssignment>(() => api.post<ApiResponse<RoleAssignment>>(`/api/role-assignments/${assignmentId}/revoke`, body))
}

export function getAssignmentHistory(assignmentId: string): Promise<RoleAssignmentHistory[]> {
  return request<RoleAssignmentHistory[]>(() => api.get<ApiResponse<RoleAssignmentHistory[]>>(`/api/role-assignments/${assignmentId}/history`))
}

export function getUserRoleHistory(userId: string): Promise<RoleAssignmentHistory[]> {
  return request<RoleAssignmentHistory[]>(() => api.get<ApiResponse<RoleAssignmentHistory[]>>(`/api/role-assignments/users/${userId}/history`))
}
