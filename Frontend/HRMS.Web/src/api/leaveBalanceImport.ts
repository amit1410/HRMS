import { api, request } from './client.ts'
import { toApiError } from './errors.ts'
import type { ApiResponse } from './types.ts'

export type ImportStatus = 'Validated' | 'Invalid' | 'Committed' | 'Failed'
export interface LeaveBalanceImportBatch { id: string; fileName: string; status: ImportStatus; totalRows: number; validRows: number; invalidRows: number; importedRows: number; uploadedByUserId: string; uploadedAtUtc: string; completedAtUtc?: string | null; failureReason?: string | null }
export interface LeaveBalanceImportRow { rowNumber: number; employeeCode: string; leaveTypeCode: string; leavePeriod: string; openingBalance: string; effectiveDate: string; remarks?: string | null; status: string; errorCode?: string | null; errorMessage?: string | null }
export async function downloadLeaveBalanceTemplate(): Promise<Blob> { try { return (await api.get<Blob>('/api/leave-balances/import/template', { responseType: 'blob' })).data } catch (error) { throw toApiError(error) } }
export function validateLeaveBalanceImport(file: File): Promise<LeaveBalanceImportBatch> { const body = new FormData(); body.append('file', file); return request(() => api.post<ApiResponse<LeaveBalanceImportBatch>>('/api/leave-balances/import/validate', body, { headers: { 'Content-Type': 'multipart/form-data' } })) }
export function commitLeaveBalanceImport(id: string): Promise<LeaveBalanceImportBatch> { return request(() => api.post<ApiResponse<LeaveBalanceImportBatch>>(`/api/leave-balances/import/${id}/commit`)) }
export function getLeaveBalanceImport(id: string): Promise<LeaveBalanceImportBatch> { return request(() => api.get<ApiResponse<LeaveBalanceImportBatch>>(`/api/leave-balances/import/${id}`)) }
export function getLeaveBalanceImportErrors(id: string): Promise<LeaveBalanceImportRow[]> { return request(() => api.get<ApiResponse<LeaveBalanceImportRow[]>>(`/api/leave-balances/import/${id}/errors`)) }
export function listLeaveBalanceImportHistory(): Promise<LeaveBalanceImportBatch[]> { return request(() => api.get<ApiResponse<LeaveBalanceImportBatch[]>>('/api/leave-balances/import/history')) }
