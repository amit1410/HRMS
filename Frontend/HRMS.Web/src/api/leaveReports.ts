import { api, cleanParams, request } from './client.ts'
import type { ApiResponse } from './types.ts'
import { saveFile, type ExportedFile, fileNameFromContentDisposition } from './employees.ts'

export type LeaveReportKind = 'requests' | 'balances' | 'usage' | 'accounting' | 'pending' | 'organization' | 'calendar'
export interface LeaveReportQuery { fromDate?: string; toDate?: string; employeeId?: string; leaveTypeId?: string; leavePeriodId?: string; status?: string; departmentId?: string; workLocationId?: string; organizationDimension?: 'department' | 'workLocation'; sortBy?: string; descending?: boolean; page?: number; pageSize?: number }
export interface ReportPage { items: Record<string, unknown>[]; page: number; pageSize: number; totalCount: number; totalPages: number; hasPreviousPage: boolean; hasNextPage: boolean }

export function getLeaveReport(kind: LeaveReportKind, query: LeaveReportQuery = {}): Promise<ReportPage | Record<string, unknown>[]> {
  return request(() => api.get<ApiResponse<ReportPage | Record<string, unknown>[]>>(`/api/leave-reports/${kind}`, { params: cleanParams(query as Record<string, string | number | boolean | undefined>) }))
}

export async function exportLeaveReport(kind: LeaveReportKind, query: LeaveReportQuery = {}): Promise<ExportedFile> {
  const response = await api.get<Blob>(`/api/leave-reports/${kind}/export.csv`, { params: cleanParams(query as Record<string, string | number | boolean | undefined>), responseType: 'blob' })
  return { blob: response.data, fileName: fileNameFromContentDisposition(response.headers['content-disposition']) ?? `leave-${kind}.csv` }
}

export { saveFile }
