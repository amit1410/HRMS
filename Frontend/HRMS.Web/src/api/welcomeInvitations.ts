import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'

export type WelcomeInvitationRow = {
  employeeId: string
  employeeCode: string
  employeeName: string
  workEmail: string
  joiningDate: string
  portalAccountStatus: string
  invitationStatus: string
  lastInvitationDateUtc?: string | null
  activationDateUtc?: string | null
  canSend: boolean
  canResend: boolean
  canRetry: boolean
}

export type WelcomeInvitationList = { page: PagedResult<WelcomeInvitationRow>; summary: { notInvited: number; pending: number; sent: number; failed: number; activated: number } }
export type WelcomeInvitationFilter = {
  search?: string
  employeeCode?: string
  workEmail?: string
  holdingCompanyId?: string
  lobId?: string
  organisationId?: string
  departmentId?: string
  workLocationId?: string
  joiningDateFrom?: string
  joiningDateTo?: string
  portalStatus?: string
  invitationStatus?: string
}
export type WelcomeInvitationSelection = { employeeIds: string[]; filter?: WelcomeInvitationFilter; confirmed?: boolean; selectAllMatching?: boolean; resend?: boolean; idempotencyKey?: string }
export type WelcomeInvitationPreview = { selectedCount: number; eligibleCount: number; ineligibleCount: number; missingEmailCount: number; alreadyActiveCount: number; alreadyInvitedCount: number; messages: string[] }
export type WelcomeInvitationSendResult = { batchId: string; totalRequested: number; accepted: number; rejected: number; messages: string[] }
export type WelcomeInvitationHistoryRow = { attemptId: string; requestedAtUtc: string; attemptType: string; deliveryStatus: string; recipientEmail: string; requestedByUserId: string; sentAtUtc?: string | null; errorSummary?: string | null }
export type WelcomeInvitationBatchStatus = { batchId: string; totalRequested: number; accepted: number; rejected: number; queued: number; processing: number; sent: number; failed: number; completed: number; status: string; createdAtUtc: string; completedAtUtc?: string | null }

export function getWelcomeInvitations(params: Record<string, string | number | boolean | undefined>, signal?: AbortSignal) {
  return request<WelcomeInvitationList>(() => api.get<ApiResponse<WelcomeInvitationList>>('/api/communication/welcome-invitations', { params: cleanParams(params), signal }))
}
export function previewWelcomeInvitations(body: WelcomeInvitationSelection) { return request<WelcomeInvitationPreview>(() => api.post<ApiResponse<WelcomeInvitationPreview>>('/api/communication/welcome-invitations/preview', body)) }
export function sendWelcomeInvitations(body: WelcomeInvitationSelection) { return request<WelcomeInvitationSendResult>(() => api.post<ApiResponse<WelcomeInvitationSendResult>>('/api/communication/welcome-invitations/send', body)) }
export function resendWelcomeInvitation(employeeId: string) { return request<WelcomeInvitationSendResult>(() => api.post<ApiResponse<WelcomeInvitationSendResult>>(`/api/communication/welcome-invitations/${employeeId}/resend`)) }
export function retryWelcomeInvitation(employeeId: string) { return request<WelcomeInvitationSendResult>(() => api.post<ApiResponse<WelcomeInvitationSendResult>>(`/api/communication/welcome-invitations/${employeeId}/retry`)) }
export function getWelcomeInvitationBatch(batchId: string) { return request<WelcomeInvitationBatchStatus>(() => api.get<ApiResponse<WelcomeInvitationBatchStatus>>(`/api/communication/welcome-invitations/batches/${batchId}`)) }
export function getWelcomeInvitationHistory(employeeId: string) { return request<PagedResult<WelcomeInvitationHistoryRow>>(() => api.get<ApiResponse<PagedResult<WelcomeInvitationHistoryRow>>>(`/api/communication/welcome-invitations/${employeeId}/history`)) }
