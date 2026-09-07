import { platformApi, request } from './platformClient.ts'
import type { ApiResponse } from './types.ts'

export type DatabaseProvider = 'SqlServer' | 'MySql'
export type TenantStatus = 'Active' | 'Inactive' | 'Suspended'

export interface PlatformTenant {
  id: string
  tenantCode: string
  tenantName: string
  host: string
  databaseProvider: DatabaseProvider
  shardKey: string
  status: TenantStatus
  webUrl: string
  email?: string | null
  phone?: string | null
  address?: string | null
  initialAdminEmail?: string | null
  developmentTemporaryPassword?: string | null
}

export interface CreatePlatformTenantRequest {
  tenantName: string
  tenantCode: string
  host: string
  databaseProvider: DatabaseProvider
  shardKey: string
  email?: string
  phone?: string
  address?: string
  firstName: string
  lastName: string
  initialAdminEmail: string
}

export interface UpdateInactivePlatformTenantRequest {
  tenantName: string
  host: string
}

export interface RetryPlatformTenantRequest {
  firstName: string
  lastName: string
  initialAdminEmail: string
}

export function listPlatformTenants(signal?: AbortSignal) {
  return request<PlatformTenant[]>(() => platformApi.get<ApiResponse<PlatformTenant[]>>('/api/platform/tenants', { signal }))
}

export function getPlatformTenant(id: string) {
  return request<PlatformTenant>(() => platformApi.get<ApiResponse<PlatformTenant>>(`/api/platform/tenants/${id}`))
}

export function createPlatformTenant(body: CreatePlatformTenantRequest) {
  return request<PlatformTenant>(() => platformApi.post<ApiResponse<PlatformTenant>>('/api/platform/tenants', body))
}

export function updateInactivePlatformTenant(id: string, body: UpdateInactivePlatformTenantRequest) {
  return request<PlatformTenant>(() => platformApi.put<ApiResponse<PlatformTenant>>(`/api/platform/tenants/${id}`, body))
}

export function retryPlatformTenant(id: string, body: RetryPlatformTenantRequest) {
  return request<PlatformTenant>(() => platformApi.post<ApiResponse<PlatformTenant>>(`/api/platform/tenants/${id}/retry-provisioning`, body))
}
