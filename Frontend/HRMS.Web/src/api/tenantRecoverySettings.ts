import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export interface TenantRecoverySettings { passwordRecoveryEnabled: boolean; allowEmailOtp: boolean; allowSmsOtp: boolean; otpExpiryMinutes: number; otpMaxAttempts: number; otpResendCooldownSeconds: number; otpMaxResends: number }
export function fetchTenantRecoverySettings() { return request<TenantRecoverySettings>(() => api.get<ApiResponse<TenantRecoverySettings>>('/api/tenants/current/password-recovery-settings')) }
export function saveTenantRecoverySettings(settings: TenantRecoverySettings) { return request<TenantRecoverySettings>(() => api.put<ApiResponse<TenantRecoverySettings>>('/api/tenants/current/password-recovery-settings', settings)) }
