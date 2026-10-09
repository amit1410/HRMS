import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export type ConfirmationReviewCaseStatus = 'Open' | 'InReview' | 'Completed' | 'Cancelled' | 'Decided'
export type ConfirmationOutcome = 'Pending' | 'Confirmed' | 'ProbationExtended' | 'NotConfirmed' | 'AdditionalReview'

export interface HrConfirmationDashboardSummary {
  from: string
  to: string
  currentConfirmationPeriodName?: string | null
  kpis: {
    probationEmployees: number
    upcomingConfirmations: number
    overdueConfirmations: number
    pendingManagerActions: number
    pendingHrbpActions: number
    confirmedEmployees: number
    extendedProbation: number
    rejectedNotConfirmed: number
  }
  statusBreakdown: {
    status: ConfirmationReviewCaseStatus
    count: number
  }[]
  outcomeBreakdown: {
    outcome: ConfirmationOutcome
    count: number
  }[]
  departmentBreakdown: {
    name: string
    employeeCount: number
  }[]
  designationBreakdown: {
    name: string
    employeeCount: number
  }[]
  trend: {
    period: string
    confirmedCount: number
    extendedCount: number
    rejectedCount: number
  }[]
  pendingActions: {
    employeeName: string
    employeeCode?: string | null
    probationCycleKey: string
    dueDate: string
    actionType: string // "Manager" or "HRBP"
    pendingReviewerType: string // "ReportingManager" or "Hrbp"
  }[]
}

export function getHrConfirmationDashboardSummary(
  params?: Record<string, string>,
  signal?: AbortSignal
): Promise<HrConfirmationDashboardSummary> {
  return request(() => api.get<ApiResponse<HrConfirmationDashboardSummary>>('/api/confirmation-dashboard/hr-summary', { params, signal }))
}