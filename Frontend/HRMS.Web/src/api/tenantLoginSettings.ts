import { api, request } from './client.ts'
import type { ApiResponse, TenantLoginIdentifierMode } from './types.ts'

export interface TenantLoginSettings { loginIdentifierMode: TenantLoginIdentifierMode }

export function getTenantLoginSettings(signal?: AbortSignal): Promise<TenantLoginSettings> {
  return request<TenantLoginSettings>(() => api.get<ApiResponse<TenantLoginSettings>>('/api/tenants/current/login-settings', { signal }))
}

export function saveTenantLoginSettings(loginIdentifierMode: TenantLoginIdentifierMode): Promise<TenantLoginSettings> {
  return request<TenantLoginSettings>(() => api.put<ApiResponse<TenantLoginSettings>>('/api/tenants/current/login-settings', { loginIdentifierMode }))
}
