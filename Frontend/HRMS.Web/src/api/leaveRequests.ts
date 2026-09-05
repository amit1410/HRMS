import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export interface LeaveRequestPreviewRequest {
  leaveTypeId: string
  startDate: string
  endDate: string
  idempotencyKey: string
}

export type EntitlementMode = 'Allocated' | 'Unlimited' | 'NoBalanceRequired'

export interface LeaveRequestPreviewDay {
  date: string
  requestedQuantity: number
  chargeableQuantity: number
  dayClassification?: string | null
  calculationReason?: string | null
  isEmployeeRequested: boolean
}

export interface LeaveRequestPreview {
  employeeId: string
  leaveTypeId: string
  leavePeriodId: string
  leavePolicyVersionId: string
  leavePolicyRuleId: string
  startDate: string
  endDate: string
  requestedQuantity: number
  chargeableQuantity: number
  requestDays: LeaveRequestPreviewDay[]
  entitlementMode: EntitlementMode
  balanceReservationRequired: boolean
  attachmentRequired: boolean
  payloadFingerprint: string
}

export function previewLeaveRequest(body: LeaveRequestPreviewRequest): Promise<LeaveRequestPreview> {
  return request(() => api.post<ApiResponse<LeaveRequestPreview>>('/api/leave-requests/preview', body))
}

export interface LeaveRequestSubmissionRequest extends LeaveRequestPreviewRequest {}

export interface LeaveRequestSubmissionDay {
  date: string
  requestedQuantity: number
  chargeableQuantity: number
  dayClassification?: string | null
  calculationReason?: string | null
  isEmployeeRequested: boolean
}

export type LeaveRequestStatus = 'PendingApproval' | 'Approved' | 'Rejected' | 'Withdrawn' | 'Cancelled'

export interface LeaveRequestSubmission {
  requestId: string
  status: LeaveRequestStatus
  employeeId: string
  leaveTypeId: string
  leavePeriodId: string
  leavePolicyVersionId: string
  leavePolicyRuleId: string
  employeeEmploymentHistoryId: string
  startDate: string
  endDate: string
  requestedQuantity: number
  chargeableQuantity: number
  submittedAtUtc: string
  requestDays: LeaveRequestSubmissionDay[]
  isReplay: boolean
}

export function submitLeaveRequest(body: LeaveRequestSubmissionRequest): Promise<LeaveRequestSubmission> {
  return request(() => api.post<ApiResponse<LeaveRequestSubmission>>('/api/leave-requests', body))
}

export interface LeaveRequestListItem {
  requestId: string
  leaveTypeId: string
  leaveTypeCode: string
  leaveTypeName: string
  startDate: string
  endDate: string
  requestedQuantity: number
  chargeableQuantity: number
  status: LeaveRequestStatus
  submittedAtUtc?: string | null
  leavePeriodId: string
  leavePolicyVersionId: string
}

export interface LeaveRequestPage<T = LeaveRequestListItem> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  hasPreviousPage: boolean
  hasNextPage: boolean
}

export interface LeaveRequestEvent {
  eventType: 'Created' | 'Submitted' | 'Approved' | 'Rejected' | 'Withdrawn' | 'Cancelled'
  occurredAtUtc: string
}

export interface LeaveRequestDetailDay extends LeaveRequestSubmissionDay {}

export interface LeaveRequestDetail {
  requestId: string
  leaveTypeId: string
  leaveTypeCode: string
  leaveTypeName: string
  startDate: string
  endDate: string
  requestedQuantity: number
  chargeableQuantity: number
  status: LeaveRequestStatus
  submittedAtUtc?: string | null
  leavePeriodId: string
  leavePeriodCode: string
  leavePeriodName: string
  leavePolicyVersionId: string
  requestDays: LeaveRequestDetailDay[]
  events: LeaveRequestEvent[]
}

export function listMyLeaveRequests(page = 1, pageSize = 25): Promise<LeaveRequestPage> {
  return request(() => api.get<ApiResponse<LeaveRequestPage>>('/api/leave-requests', { params: { page, pageSize } }))
}

export function getMyLeaveRequest(requestId: string): Promise<LeaveRequestDetail> {
  return request(() => api.get<ApiResponse<LeaveRequestDetail>>(`/api/leave-requests/${requestId}`))
}

export interface LeaveApprovalListItem {
  requestId: string
  employeeId: string
  employeeCode: string
  employeeName: string
  leaveTypeId: string
  leaveTypeCode: string
  leaveTypeName: string
  startDate: string
  endDate: string
  requestedQuantity: number
  chargeableQuantity: number
  status: LeaveRequestStatus
  submittedAtUtc?: string | null
}

export interface LeaveApprovalDetail {
  requestId: string
  employeeId: string
  employeeCode: string
  employeeName: string
  leaveTypeId: string
  leaveTypeCode: string
  leaveTypeName: string
  startDate: string
  endDate: string
  requestedQuantity: number
  chargeableQuantity: number
  status: LeaveRequestStatus
  submittedAtUtc?: string | null
  leavePeriodId: string
  leavePeriodCode: string
  leavePeriodName: string
  leavePolicyVersionId: string
  requestDays: LeaveRequestDetailDay[]
  events: LeaveRequestEvent[]
}

export interface LeaveApprovalResult {
  requestId: string
  status: LeaveRequestStatus
  eventType: 'Approved' | 'Rejected'
  occurredAtUtc: string
}

export interface LeaveRequestWithdrawalResult {
  requestId: string
  status: LeaveRequestStatus
  eventType: 'Withdrawn'
  occurredAtUtc: string
}

export interface LeaveRequestCancellationResult {
  requestId: string
  status: LeaveRequestStatus
  eventType: 'Cancelled'
  occurredAtUtc: string
}

export function listLeaveApprovals(page = 1, pageSize = 25): Promise<LeaveRequestPage<LeaveApprovalListItem>> {
  return request(() => api.get<ApiResponse<LeaveRequestPage<LeaveApprovalListItem>>>('/api/leave-approvals', { params: { page, pageSize } }))
}

export function getLeaveApproval(requestId: string): Promise<LeaveApprovalDetail> {
  return request(() => api.get<ApiResponse<LeaveApprovalDetail>>(`/api/leave-approvals/${requestId}`))
}

export function approveLeaveRequest(requestId: string): Promise<LeaveApprovalResult> {
  return request(() => api.post<ApiResponse<LeaveApprovalResult>>(`/api/leave-requests/${requestId}/approve`))
}

export function rejectLeaveRequest(requestId: string): Promise<LeaveApprovalResult> {
  return request(() => api.post<ApiResponse<LeaveApprovalResult>>(`/api/leave-requests/${requestId}/reject`))
}

export function withdrawLeaveRequest(requestId: string): Promise<LeaveRequestWithdrawalResult> {
  return request(() => api.post<ApiResponse<LeaveRequestWithdrawalResult>>(`/api/leave-requests/${requestId}/withdraw`))
}

export function cancelLeaveRequest(requestId: string): Promise<LeaveRequestCancellationResult> {
  return request(() => api.post<ApiResponse<LeaveRequestCancellationResult>>(`/api/leave-requests/${requestId}/cancel`))
}
