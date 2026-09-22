import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'
import type { QueryParams } from './client.ts'

export type PayrollAdjustment = { id: string; employeeId: string; employeeCode: string; adjustmentNumber: string; adjustmentType: string; sourceType: string; effectiveDate: string; componentCode: string; description: string; amount: number; appliedAmount: number; outstandingAmount: number; direction: string; taxTreatment: string; status: string; reason?: string | null }
export type PayrollAdjustmentReason = { id: string; code: string; name: string; description?: string | null; isActive: boolean; requiresComment: boolean }

export function listPayrollAdjustments(params: QueryParams = {}, signal?: AbortSignal) { return request<PagedResult<PayrollAdjustment>>(() => api.get<ApiResponse<PagedResult<PayrollAdjustment>>>('/api/payroll/adjustments', { params: cleanParams(params), signal })) }
export function listMyPayrollAdjustments(params: QueryParams = {}, signal?: AbortSignal) { return request<PagedResult<PayrollAdjustment>>(() => api.get<ApiResponse<PagedResult<PayrollAdjustment>>>('/api/me/payroll-adjustments', { params: cleanParams(params), signal })) }
export function createPayrollAdjustment(body: Record<string, unknown>) { return request<PayrollAdjustment>(() => api.post<ApiResponse<PayrollAdjustment>>('/api/payroll/adjustments', body)) }
export function submitPayrollAdjustment(id: string) { return request<PayrollAdjustment>(() => api.post<ApiResponse<PayrollAdjustment>>(`/api/payroll/adjustments/${id}/submit`)) }
export function approvePayrollAdjustment(id: string) { return request<PayrollAdjustment>(() => api.post<ApiResponse<PayrollAdjustment>>(`/api/payroll/adjustments/${id}/approve`)) }
export function rejectPayrollAdjustment(id: string, reason: string) { return request<PayrollAdjustment>(() => api.post<ApiResponse<PayrollAdjustment>>(`/api/payroll/adjustments/${id}/reject`, reason)) }
export function cancelPayrollAdjustment(id: string, reason: string) { return request<PayrollAdjustment>(() => api.post<ApiResponse<PayrollAdjustment>>(`/api/payroll/adjustments/${id}/cancel`, reason)) }
export function getPayrollAdjustmentHistory(id: string) { return request<unknown[]>(() => api.get<ApiResponse<unknown[]>>(`/api/payroll/adjustments/${id}/history`)) }
export type PayrollRun = { id: string; runNumber: string; runType: string; status: string; employeeCount: number; eligibleEmployeeCount: number; ineligibleEmployeeCount: number; notes?: string | null }
export function listOffCycleRuns(params: QueryParams = {}, signal?: AbortSignal) { return request<PagedResult<PayrollRun>>(() => api.get<ApiResponse<PagedResult<PayrollRun>>>('/api/payroll/off-cycle-runs', { params: cleanParams(params), signal })) }
export function prepareOffCycleRun(id: string) { return request<PayrollRun>(() => api.post<ApiResponse<PayrollRun>>(`/api/payroll/off-cycle-runs/${id}/prepare`)) }
export function approveOffCycleRun(id: string) { return request<PayrollRun>(() => api.post<ApiResponse<PayrollRun>>(`/api/payroll/off-cycle-runs/${id}/approve`)) }
export function processOffCycleRun(id: string) { return request<PayrollRun>(() => api.post<ApiResponse<PayrollRun>>(`/api/payroll/off-cycle-runs/${id}/process`)) }
