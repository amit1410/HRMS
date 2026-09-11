import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export type Shift = { id: string; shiftCode: string; shiftName: string; description?: string | null; startTime: string; endTime: string; breakDurationMinutes: number; minimumWorkMinutes: number; fullDayWorkMinutes: number; graceInMinutes: number; graceOutMinutes: number; lateThresholdMinutes: number; earlyOutThresholdMinutes: number; isNightShift: boolean; crossesMidnight: boolean; captureMode: string; isActive: boolean; effectiveFrom: string; effectiveTo?: string | null; concurrencyToken: string }
export type Pattern = { id: string; code: string; name: string; cycleLengthDays: number; isActive: boolean; effectiveFrom: string; effectiveTo?: string | null; days: { sequenceDay: number; shiftId?: string | null; dayType: string }[] }
export type ShiftRequest = Omit<Shift, 'id' | 'concurrencyToken'> & { concurrencyToken?: string }
export type PatternRequest = Omit<Pattern, 'id' | 'days'> & { days: Pattern['days'] }
export type RosterBatch = { id: string; fileName: string; status: string; totalRows: number; validRows: number; invalidRows: number; committedRows: number; rows: { rowNumber: number; employeeCode: string; rosterDate: string; shiftCode?: string | null; dayType: string; isValid: boolean; errorMessage?: string | null }[] }
export function listShifts(): Promise<Shift[]> { return request(() => api.get<ApiResponse<Shift[]>>('/api/attendance/shifts')) }
export function createShift(value: ShiftRequest): Promise<Shift> { return request(() => api.post<ApiResponse<Shift>>('/api/attendance/shifts', value)) }
export function updateShift(id: string, value: ShiftRequest): Promise<Shift> { return request(() => api.put<ApiResponse<Shift>>(`/api/attendance/shifts/${id}`, value)) }
export function listPatterns(): Promise<Pattern[]> { return request(() => api.get<ApiResponse<Pattern[]>>('/api/attendance/patterns')) }
export function createPattern(value: PatternRequest): Promise<Pattern> { return request(() => api.post<ApiResponse<Pattern>>('/api/attendance/patterns', value)) }
export function downloadRosterTemplate(): Promise<Blob> { return api.get<Blob>('/api/attendance/roster/template', { responseType: 'blob' }).then(response => response.data) }
export function validateRoster(file: File): Promise<RosterBatch> { const form = new FormData(); form.append('file', file); return request(() => api.post<ApiResponse<RosterBatch>>('/api/attendance/roster/upload/validate', form, { headers: { 'Content-Type': 'multipart/form-data' } })) }
export function commitRoster(id: string): Promise<RosterBatch> { return request(() => api.post<ApiResponse<RosterBatch>>(`/api/attendance/roster/upload/${id}/commit`)) }
