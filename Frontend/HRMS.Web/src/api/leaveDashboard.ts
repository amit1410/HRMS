import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export type DashboardStatus = 'PendingApproval' | 'Approved' | 'Rejected' | 'Withdrawn' | 'Cancelled'
export interface HrLeaveDashboardSummary {
  from: string
  to: string
  currentLeavePeriodName?: string | null
  kpis: { employeesOnLeaveToday: number; activeEmployeeCount: number; upcomingApprovedRequests: number; pendingApprovalRequests: number; approvedRequests: number; rejectedRequests: number; cancelledOrWithdrawnRequests: number }
  statusBreakdown: { status: DashboardStatus; requestCount: number; quantity: number }[]
  leaveTypeUsage: { leaveTypeId: string; code: string; name: string; requestCount: number; quantity: number }[]
  balances: { leaveTypeId: string; code: string; name: string; entitlementMode: 'Allocated' | 'Unlimited' | 'NoBalanceRequired'; granted?: number | null; reserved?: number | null; consumed?: number | null; available?: number | null }[]
  approvalAging: { bucket: string; requestCount: number }[]
  pendingApprovals: { requestId: string; employeeCode: string; employeeName: string; leaveTypeName: string; startDate: string; endDate: string; quantity: number; submittedAtUtc?: string | null; daysPending: number; managerName?: string | null }[]
  upcomingAbsences: { requestId: string; employeeCode: string; employeeName: string; leaveTypeName: string; startDate: string; endDate: string; quantity: number; department?: string | null; workLocation?: string | null }[]
  departments: { name: string; employeeCount: number; quantity: number }[]
  workLocations: { name: string; employeeCount: number; quantity: number }[]
  trend: { period: string; quantity: number }[]
}

export function getHrLeaveDashboardSummary(params?: Record<string, string>, signal?: AbortSignal): Promise<HrLeaveDashboardSummary> {
  return request(() => api.get<ApiResponse<HrLeaveDashboardSummary>>('/api/leave-dashboard/hr-summary', { params, signal }))
}
