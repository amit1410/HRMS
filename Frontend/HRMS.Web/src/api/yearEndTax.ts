import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'
import type { QueryParams } from './client.ts'

export type YearEndTaxRun = { id: string; taxYear: number; taxYearCode: string; startDate: string; endDate: string; status: string; employeeCount: number; blockingIssueCount: number; totalTaxDue: number; totalExcessTax: number }
export type YearEndTaxEmployee = { id: string; employeeId: string; ytdGross: number; ytdTaxableIncome: number; ytdTaxDeducted: number; approvedDeclarationAmount: number; approvedProofAmount: number; previousEmployerTaxableIncome: number; previousEmployerTaxDeducted: number; projectedAnnualTax: number; estimatedTaxDue: number; estimatedExcessTax: number; finalTaxableIncome: number; finalTaxLiability: number; status: string; blockingIssueCode?: string | null; blockingIssueMessage?: string | null }
export type YearEndTaxPreviousEmployer = { id: string; employeeId: string; employerName: string; employerReference?: string | null; taxableIncome: number; taxDeducted: number; eligibleDeductionAmount: number; status: string }

const path = '/api/payroll/year-end-tax'
export function listYearEndTaxRuns(params: QueryParams = {}, signal?: AbortSignal) { return request<PagedResult<YearEndTaxRun>>(() => api.get<ApiResponse<PagedResult<YearEndTaxRun>>>(`${path}/runs`, { params: cleanParams(params), signal })) }
export function createYearEndTaxRun(body: Record<string, unknown>) { return request<YearEndTaxRun>(() => api.post<ApiResponse<YearEndTaxRun>>(`${path}/runs`, body)) }
export function calculateYearEndTaxRun(id: string) { return request<YearEndTaxRun>(() => api.post<ApiResponse<YearEndTaxRun>>(`${path}/runs/${id}/calculate`)) }
export function submitYearEndTaxRun(id: string) { return request<YearEndTaxRun>(() => api.post<ApiResponse<YearEndTaxRun>>(`${path}/runs/${id}/submit`)) }
export function approveYearEndTaxRun(id: string) { return request<YearEndTaxRun>(() => api.post<ApiResponse<YearEndTaxRun>>(`${path}/runs/${id}/approve`)) }
export function closeYearEndTaxRun(id: string) { return request<YearEndTaxRun>(() => api.post<ApiResponse<YearEndTaxRun>>(`${path}/runs/${id}/close`)) }
export function getYearEndTaxEmployees(id: string, params: QueryParams = {}, signal?: AbortSignal) { return request<PagedResult<YearEndTaxEmployee>>(() => api.get<ApiResponse<PagedResult<YearEndTaxEmployee>>>(`${path}/runs/${id}/employees`, { params: cleanParams(params), signal })) }
export function addPreviousEmployerInput(runId: string, body: Record<string, unknown>) { return request<YearEndTaxPreviousEmployer>(() => api.post<ApiResponse<YearEndTaxPreviousEmployer>>(`${path}/runs/${runId}/previous-employer`, body)) }
