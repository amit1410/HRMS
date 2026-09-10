import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export type LeaveCalendarStatus = 'PendingApproval' | 'Approved'
export interface LeaveCalendarEvent { requestId: string; employeeId: string; employeeCode: string; employeeName: string; leaveTypeCode: string; leaveTypeName: string; startDate: string; endDate: string; chargeableQuantity: number; status: LeaveCalendarStatus }
export function listLeaveCalendar(from: string, to: string, signal?: AbortSignal): Promise<LeaveCalendarEvent[]> { return request(() => api.get<ApiResponse<LeaveCalendarEvent[]>>('/api/leave-calendar', { params: { from, to }, signal })) }
