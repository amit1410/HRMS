import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'
import type { EntitlementMode } from './leaveRequests.ts'

export interface LeaveBalanceSummary {
  leaveTypeCode: string
  leaveTypeName: string
  entitlementMode: EntitlementMode
  leavePeriodName?: string | null
  grantedQuantity?: number | null
  reservedQuantity?: number | null
  consumedQuantity?: number | null
  availableQuantity?: number | null
}

export function listMyLeaveBalances(signal?: AbortSignal): Promise<LeaveBalanceSummary[]> {
  return request(() => api.get<ApiResponse<LeaveBalanceSummary[]>>('/api/leave-balances/mine', { signal }))
}
