import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'
import type { QueryParams } from './client.ts'

export interface PayrollDashboard { payrollRunCount: number; employeeCount: number; grossTotal: number; deductionTotal: number; netPayTotal: number; employerContributionTotal: number; taxTotal: number; reimbursementTotal: number; loanRecoveryTotal: number; variablePayTotal: number; adjustmentTotal: number; finalSettlementTotal: number; exceptionCount: number }
export interface PayrollReportRegisterRow { payrollRunId: string; payrollResultId: string; employeeId: string; employeeCode: string; employeeName: string; periodEndDate: string; runType: string; department: string; costCenter: string; earningsTotal: number; grossTotal: number; deductionTotal: number; taxTotal: number; employerContributionTotal: number; netPay: number; reimbursementTotal: number; loanRecoveryTotal: number; variablePayTotal: number; adjustmentTotal: number; status: string }

export function getPayrollReportsDashboard(params: QueryParams = {}, signal?: AbortSignal): Promise<PayrollDashboard> { return request<PayrollDashboard>(() => api.get<ApiResponse<PayrollDashboard>>('/api/payroll/reports/dashboard', { params: cleanParams(params), signal })) }
export function getPayrollRegister(params: QueryParams = {}, signal?: AbortSignal): Promise<PagedResult<PayrollReportRegisterRow>> { return request<PagedResult<PayrollReportRegisterRow>>(() => api.get<ApiResponse<PagedResult<PayrollReportRegisterRow>>>('/api/payroll/reports/payroll-register', { params: cleanParams(params), signal })) }
export function getPayrollRegisterCsv(params: QueryParams = {}, signal?: AbortSignal) { return api.get('/api/payroll/reports/payroll-register.csv', { params: cleanParams(params), responseType: 'blob', signal }) }
