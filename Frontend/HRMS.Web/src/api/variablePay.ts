import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'
import type { QueryParams } from './client.ts'

export type VariablePayPlan = { id: string; code: string; name: string; planType: string; currencyCode: string; isActive: boolean; versions: VariablePayPlanVersion[] }
export type VariablePayPlanVersion = { id: string; effectiveFrom: string; effectiveTo?: string | null; status: string; calculationMethod: string; salaryBasisType: string; fixedAmount?: number | null; percentage?: number | null; targetPercentage?: number | null; maximumAmount?: number | null; taxTreatment: string; finalSettlementTreatment: string }
export type VariablePayAward = { id: string; employeeId: string; awardNumber: string; awardPeriodFrom: string; awardPeriodTo: string; calculatedAmount: number; approvedAmount?: number | null; taxableAmount: number; nonTaxableAmount: number; settledAmount: number; outstandingAmount: number; status: string; payoutDate?: string | null }

export function listVariablePayPlans(signal?: AbortSignal) { return request<VariablePayPlan[]>(() => api.get<ApiResponse<VariablePayPlan[]>>('/api/payroll/variable-pay-plans', { signal })) }
export function createVariablePayPlan(body: { code: string; name: string; planType: string; currencyCode?: string }) { return request<VariablePayPlan>(() => api.post<ApiResponse<VariablePayPlan>>('/api/payroll/variable-pay-plans', body)) }
export function listVariablePayAwards(params: QueryParams = {}, signal?: AbortSignal) { return request<PagedResult<VariablePayAward>>(() => api.get<ApiResponse<PagedResult<VariablePayAward>>>('/api/payroll/variable-pay-awards', { params: cleanParams(params), signal })) }
export function listMyVariablePay(signal?: AbortSignal) { return request<VariablePayAward[]>(() => api.get<ApiResponse<VariablePayAward[]>>('/api/me/variable-pay', { signal })) }
