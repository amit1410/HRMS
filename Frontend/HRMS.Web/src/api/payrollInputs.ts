import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'
import type { QueryParams } from './client.ts'

export type PayrollInputBatch = { id: string; batchNumber: string; name: string; payrollPeriodId?: string | null; effectiveDate: string; sourceType: string; status: string; totalRows: number; validRows: number; invalidRows: number; warningRows: number; totalAmount: number; fileName?: string | null }
export type PayrollInputPreview = { batch: PayrollInputBatch; employeeCount: number; componentCount: number; earnings: number; deductions: number; netInputImpact: number }
export type PayrollInputLine = { id: string; rowNumber: number; employeeCode: string; componentCode: string; inputType: string; amount?: number | null; effectiveDate: string; status: string; validationMessage?: string | null }
export type PayrollInputIssue = { id: string; lineId?: string | null; rowNumber?: number | null; severity: string; code: string; fieldName?: string | null; message: string }
export type PayrollInputTemplateColumn = { id?: string; sourceColumnName: string; targetField: string; required: boolean; position: number; defaultValue?: string | null; transformType: string }
export type PayrollInputTemplate = { id: string; code: string; name: string; description?: string | null; inputType: string; active: boolean; version: number; columns: PayrollInputTemplateColumn[] }
export function listPayrollInputBatches(params: QueryParams = {}, signal?: AbortSignal) { return request<PagedResult<PayrollInputBatch>>(() => api.get<ApiResponse<PagedResult<PayrollInputBatch>>>('/api/payroll/input-batches', { params: cleanParams(params), signal })) }
export function createPayrollInputBatch(body: Record<string, unknown>) { return request<PayrollInputBatch>(() => api.post<ApiResponse<PayrollInputBatch>>('/api/payroll/input-batches', body)) }
export function uploadPayrollInputCsv(id: string, file: File) { const body = new FormData(); body.append('file', file); return request<PayrollInputBatch>(() => api.post<ApiResponse<PayrollInputBatch>>(`/api/payroll/input-batches/${id}/upload`, body)) }
export function validatePayrollInputBatch(id: string) { return request<PayrollInputBatch>(() => api.post<ApiResponse<PayrollInputBatch>>(`/api/payroll/input-batches/${id}/validate`)) }
export function previewPayrollInputBatch(id: string, signal?: AbortSignal) { return request<PayrollInputPreview>(() => api.get<ApiResponse<PayrollInputPreview>>(`/api/payroll/input-batches/${id}/preview`, { signal })) }
export function submitPayrollInputBatch(id: string) { return request<PayrollInputBatch>(() => api.post<ApiResponse<PayrollInputBatch>>(`/api/payroll/input-batches/${id}/submit`)) }
export function approvePayrollInputBatch(id: string) { return request<PayrollInputBatch>(() => api.post<ApiResponse<PayrollInputBatch>>(`/api/payroll/input-batches/${id}/approve`)) }
export function postPayrollInputBatch(id: string) { return request<PayrollInputBatch>(() => api.post<ApiResponse<PayrollInputBatch>>(`/api/payroll/input-batches/${id}/post`)) }
export function getPayrollInputLines(id: string, params: QueryParams = {}, signal?: AbortSignal) { return request<PagedResult<PayrollInputLine>>(() => api.get<ApiResponse<PagedResult<PayrollInputLine>>>(`/api/payroll/input-batches/${id}/lines`, { params: cleanParams(params), signal })) }
export function getPayrollInputIssues(id: string, params: QueryParams = {}, signal?: AbortSignal) { return request<PagedResult<PayrollInputIssue>>(() => api.get<ApiResponse<PagedResult<PayrollInputIssue>>>(`/api/payroll/input-batches/${id}/issues`, { params: cleanParams(params), signal })) }
export function updatePayrollInputLine(batchId: string, lineId: string, body: Record<string, unknown>) { return request<PayrollInputLine>(() => api.put<ApiResponse<PayrollInputLine>>(`/api/payroll/input-batches/${batchId}/lines/${lineId}`, body)) }
export function deletePayrollInputLine(batchId: string, lineId: string) { return request<boolean>(() => api.delete<ApiResponse<boolean>>(`/api/payroll/input-batches/${batchId}/lines/${lineId}`)) }
export function rejectPayrollInputBatch(id: string, reason: string) { return request<PayrollInputBatch>(() => api.post<ApiResponse<PayrollInputBatch>>(`/api/payroll/input-batches/${id}/reject`, reason, { headers: { 'Content-Type': 'application/json' } })) }
export function cancelPayrollInputBatch(id: string, reason: string) { return request<PayrollInputBatch>(() => api.post<ApiResponse<PayrollInputBatch>>(`/api/payroll/input-batches/${id}/cancel`, reason, { headers: { 'Content-Type': 'application/json' } })) }
export function listPayrollInputTemplates(signal?: AbortSignal) { return request<PagedResult<PayrollInputTemplate>>(() => api.get<ApiResponse<PagedResult<PayrollInputTemplate>>>('/api/payroll/input-templates', { signal })) }
export function createPayrollInputTemplate(body: Record<string, unknown>) { return request<PayrollInputTemplate>(() => api.post<ApiResponse<PayrollInputTemplate>>('/api/payroll/input-templates', body)) }
export function updatePayrollInputTemplate(id: string, body: Record<string, unknown>) { return request<PayrollInputTemplate>(() => api.put<ApiResponse<PayrollInputTemplate>>(`/api/payroll/input-templates/${id}`, body)) }
export function payrollInputExportUrl(id: string, kind: 'issues' | 'preview' | 'result') { return `/api/payroll/input-batches/${id}/${kind}/export` }
