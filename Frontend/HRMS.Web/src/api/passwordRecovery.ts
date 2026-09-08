import { api, request } from './client.ts'
import type { ApiResponse, PasswordRecoveryChannel, RecoveryChallenge, RecoverySendOtp, RecoveryVerification } from './types.ts'

export function identifyPasswordRecovery(identifier: string) { return request<RecoveryChallenge>(() => api.post<ApiResponse<RecoveryChallenge>>('/api/auth/forgot-password', { identifier })) }
export function sendPasswordRecoveryOtp(challengeId: string, channel: PasswordRecoveryChannel) { return request<RecoverySendOtp>(() => api.post<ApiResponse<RecoverySendOtp>>('/api/auth/forgot-password/send-otp', { challengeId, channel })) }
export function resendPasswordRecoveryOtp(challengeId: string) { return request<RecoverySendOtp>(() => api.post<ApiResponse<RecoverySendOtp>>('/api/auth/forgot-password/resend-otp', { challengeId })) }
export function verifyPasswordRecoveryOtp(challengeId: string, otp: string) { return request<RecoveryVerification>(() => api.post<ApiResponse<RecoveryVerification>>('/api/auth/forgot-password/verify-otp', { challengeId, otp })) }
export function resetPassword(resetToken: string, newPassword: string, confirmPassword: string) { return request<boolean>(() => api.post<ApiResponse<boolean>>('/api/auth/forgot-password/reset', { resetToken, newPassword, confirmPassword })) }
export function changePassword(currentPassword: string, newPassword: string, confirmPassword: string) { return request<boolean>(() => api.post<ApiResponse<boolean>>('/api/me/change-password', { currentPassword, newPassword, confirmPassword })) }
