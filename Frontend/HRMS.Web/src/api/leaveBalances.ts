import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export interface LeaveBalanceSummary {
  balanceId: string
  leaveTypeId: string
  leaveTypeCode: string
  leaveTypeName: string
  leavePeriodId: string
  leavePeriodCode: string
  leavePeriodName: string
  periodStartDate: string
  periodEndDate: string
  grantedQuantity: number
  reservedQuantity: number
  consumedQuantity: number
  availableQuantity: number
}

export function listMyLeaveBalances(signal?: AbortSignal): Promise<LeaveBalanceSummary[]> {
  return request(() => api.get<ApiResponse<LeaveBalanceSummary[]>>('/api/leave-balances/mine', { signal }))
}
