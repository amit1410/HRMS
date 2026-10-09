import { unwrap } from './client.ts'
import type { ApiResponse } from './types.ts'
import { platformApi } from './platformClient.ts'
export interface PlatformRecoveryResponse { message: string }
export async function forgotUsername(email: string) { const r = await platformApi.post<ApiResponse<PlatformRecoveryResponse>>('/api/platform/auth/forgot-username', { email }); return unwrap(r.data, r.status) }
export async function forgotPassword(identifier: string) { const r = await platformApi.post<ApiResponse<PlatformRecoveryResponse>>('/api/platform/auth/forgot-password', { identifier }); return unwrap(r.data, r.status) }
export async function resetPlatformPassword(token: string, newPassword: string, confirmPassword: string) { const r = await platformApi.post<ApiResponse<PlatformRecoveryResponse>>('/api/platform/auth/reset-password', { token, newPassword, confirmPassword }); return unwrap(r.data, r.status) }
