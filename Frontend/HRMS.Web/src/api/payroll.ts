import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'
import type { QueryParams } from './client.ts'

export type SalaryComponentType = 'Earning' | 'Deduction' | 'EmployerContribution' | 'Reimbursement' | 'Information'
export type SalaryCalculationType = 'FixedAmount' | 'Percentage' | 'Formula' | 'ManualInput' | 'AttendanceBased' | 'LeaveBased' | 'Statutory'
export type SalaryStatutoryType = 'None' | 'ProvidentFund' | 'Esi' | 'ProfessionalTax' | 'LabourWelfareFund' | 'IncomeTax' | 'Gratuity' | 'Other'
export type SalaryStructureCalculationType = 'FixedAmount' | 'Percentage' | 'Formula' | 'Manual'

export type StatutoryType = 'ProvidentFund' | 'Esi' | 'ProfessionalTax' | 'IncomeTax'
export type StatutoryConfigurationStatus = 'Draft' | 'Active' | 'Retired'
export interface StatutoryConfigurationVersion { id: string; effectiveFrom: string; effectiveTo?: string | null; status: StatutoryConfigurationStatus; priority: number; configurationJson: string }
export interface StatutoryConfiguration { id: string; jurisdictionCode: string; stateCode?: string | null; statutoryType: StatutoryType; code: string; name: string; isActive: boolean; concurrencyVersion: number; versions: StatutoryConfigurationVersion[] }
export interface StatutoryConfigurationRequest { jurisdictionCode: string; stateCode?: string | null; statutoryType: StatutoryType; code: string; name: string; isActive: boolean }
export interface EmployeeStatutoryProfile { id: string; employeeId: string; jurisdictionCode: string; stateCode?: string | null; pfApplicable: boolean; uan?: string | null; esiApplicable: boolean; esiNumber?: string | null; professionalTaxApplicable: boolean; incomeTaxApplicable: boolean; taxRegime?: string | null; effectiveFrom: string; effectiveTo?: string | null; isActive: boolean }
export interface PayrollStatutoryResult { id: string; statutoryType: StatutoryType; jurisdictionCode: string; configurationId: string; configurationVersionId: string; calculationBasis: number; employeeAmount: number; employerAmount: number; totalAmount: number; appliedRate?: number | null; appliedCeiling?: number | null; calculationMetadata: string }

export function listStatutoryConfigurations(params: QueryParams = {}, signal?: AbortSignal): Promise<PagedResult<StatutoryConfiguration>> { return request<PagedResult<StatutoryConfiguration>>(() => api.get<ApiResponse<PagedResult<StatutoryConfiguration>>>('/api/payroll/statutory-configurations', { params: cleanParams(params), signal })) }
export function createStatutoryConfiguration(body: StatutoryConfigurationRequest): Promise<StatutoryConfiguration> { return request<StatutoryConfiguration>(() => api.post<ApiResponse<StatutoryConfiguration>>('/api/payroll/statutory-configurations', body)) }
export function getEmployeeStatutoryProfile(employeeId: string): Promise<EmployeeStatutoryProfile> { return request<EmployeeStatutoryProfile>(() => api.get<ApiResponse<EmployeeStatutoryProfile>>(`/api/payroll/employees/${employeeId}/statutory-profile`)) }
export function saveEmployeeStatutoryProfile(employeeId: string, body: Omit<EmployeeStatutoryProfile, 'id' | 'employeeId'>): Promise<EmployeeStatutoryProfile> { return request<EmployeeStatutoryProfile>(() => api.put<ApiResponse<EmployeeStatutoryProfile>>(`/api/payroll/employees/${employeeId}/statutory-profile`, body)) }
export function getPayrollStatutoryResults(runId: string, employeeId: string): Promise<PayrollStatutoryResult[]> { return request<PayrollStatutoryResult[]>(() => api.get<ApiResponse<PayrollStatutoryResult[]>>(`/api/payroll/runs/${runId}/results/${employeeId}/statutory`)) }

export interface SalaryComponent {
  id: string
  code: string
  name: string
  description?: string | null
  componentType: SalaryComponentType
  calculationType: SalaryCalculationType
  statutoryType: SalaryStatutoryType
  isTaxable: boolean
  isStatutory: boolean
  isRecurring: boolean
  affectsGross: boolean
  affectsNetPay: boolean
  displayOrder: number
  effectiveFrom: string
  effectiveTo?: string | null
  isActive: boolean
  concurrencyVersion: number
}

export type SalaryComponentRequest = Omit<SalaryComponent, 'id' | 'concurrencyVersion'> & { expectedConcurrencyVersion?: number }

export function listSalaryComponents(params: QueryParams = {}, signal?: AbortSignal): Promise<PagedResult<SalaryComponent>> {
  return request<PagedResult<SalaryComponent>>(() => api.get<ApiResponse<PagedResult<SalaryComponent>>>('/api/payroll/salary-components', { params: cleanParams(params), signal }))
}
export function createSalaryComponent(body: SalaryComponentRequest): Promise<SalaryComponent> {
  return request<SalaryComponent>(() => api.post<ApiResponse<SalaryComponent>>('/api/payroll/salary-components', body))
}
export function getSalaryComponent(id: string): Promise<SalaryComponent> {
  return request<SalaryComponent>(() => api.get<ApiResponse<SalaryComponent>>(`/api/payroll/salary-components/${id}`))
}
export function updateSalaryComponent(id: string, body: SalaryComponentRequest): Promise<SalaryComponent> {
  return request<SalaryComponent>(() => api.put<ApiResponse<SalaryComponent>>(`/api/payroll/salary-components/${id}`, body))
}
export function getSalaryComponentHistory(id: string): Promise<SalaryComponentHistory[]> {
  return request<SalaryComponentHistory[]>(() => api.get<ApiResponse<SalaryComponentHistory[]>>(`/api/payroll/salary-components/${id}/history`))
}
export function setSalaryComponentActive(id: string, active: boolean, version: number): Promise<SalaryComponent> {
  return request<SalaryComponent>(() => api.post<ApiResponse<SalaryComponent>>(`/api/payroll/salary-components/${id}/${active ? 'activate' : 'deactivate'}`, undefined, { params: { expectedConcurrencyVersion: version } }))
}

export interface SalaryComponentHistory {
  id: string
  salaryComponentId: string
  changeType: string
  code: string
  name: string
  componentType: SalaryComponentType
  calculationType: SalaryCalculationType
  statutoryType: SalaryStatutoryType
  isTaxable: boolean
  isStatutory: boolean
  isRecurring: boolean
  affectsGross: boolean
  affectsNetPay: boolean
  effectiveFrom: string
  effectiveTo?: string | null
  isActive: boolean
  actorUserId?: string | null
  changedAtUtc: string
}

export interface SalaryStructureComponent {
  id: string
  salaryComponentId: string
  salaryComponentCode: string
  salaryComponentName: string
  componentType: SalaryComponentType
  sequence: number
  calculationType: SalaryStructureCalculationType
  value?: number | null
  percentageOfComponentId?: string | null
  formula?: string | null
  isProratable: boolean
  isEditableAtEmployeeLevel: boolean
  minimumAmount?: number | null
  maximumAmount?: number | null
  isActive: boolean
  effectiveFrom: string
  effectiveTo?: string | null
}

export interface SalaryStructure {
  id: string
  versionId: string
  code: string
  name: string
  description?: string | null
  effectiveFrom: string
  effectiveTo?: string | null
  isActive: boolean
  componentCount: number
  concurrencyVersion: number
  components: SalaryStructureComponent[]
}

export interface SalaryStructureComponentRequest {
  salaryComponentId: string
  sequence: number
  calculationType: SalaryStructureCalculationType
  value?: number | null
  percentageOfComponentId?: string | null
  formula?: string | null
  isProratable: boolean
  isEditableAtEmployeeLevel: boolean
  minimumAmount?: number | null
  maximumAmount?: number | null
  isActive: boolean
  effectiveFrom?: string | null
  effectiveTo?: string | null
}

export interface SalaryStructureRequest {
  code: string
  name: string
  description?: string | null
  effectiveFrom: string
  effectiveTo?: string | null
  isActive: boolean
  components: SalaryStructureComponentRequest[]
  expectedConcurrencyVersion?: number
}

export interface SalaryStructureHistory {
  id: string
  salaryStructureId: string
  salaryStructureVersionId?: string | null
  changeType: string
  code: string
  name: string
  description?: string | null
  effectiveFrom?: string | null
  effectiveTo?: string | null
  isActive: boolean
  componentsJson: string
  actorUserId?: string | null
  changedAtUtc: string
}

export function listSalaryStructures(params: QueryParams = {}, signal?: AbortSignal): Promise<PagedResult<SalaryStructure>> {
  return request<PagedResult<SalaryStructure>>(() => api.get<ApiResponse<PagedResult<SalaryStructure>>>('/api/payroll/salary-structures', { params: cleanParams(params), signal }))
}
export function getSalaryStructure(id: string): Promise<SalaryStructure> {
  return request<SalaryStructure>(() => api.get<ApiResponse<SalaryStructure>>(`/api/payroll/salary-structures/${id}`))
}
export function createSalaryStructure(body: SalaryStructureRequest): Promise<SalaryStructure> {
  return request<SalaryStructure>(() => api.post<ApiResponse<SalaryStructure>>('/api/payroll/salary-structures', body))
}
export function updateSalaryStructure(id: string, body: SalaryStructureRequest): Promise<SalaryStructure> {
  return request<SalaryStructure>(() => api.put<ApiResponse<SalaryStructure>>(`/api/payroll/salary-structures/${id}`, body))
}
export function setSalaryStructureActive(id: string, active: boolean, version: number): Promise<SalaryStructure> {
  return request<SalaryStructure>(() => api.post<ApiResponse<SalaryStructure>>(`/api/payroll/salary-structures/${id}/${active ? 'activate' : 'deactivate'}`, undefined, { params: { expectedConcurrencyVersion: version } }))
}
export function getSalaryStructureHistory(id: string): Promise<SalaryStructureHistory[]> {
  return request<SalaryStructureHistory[]>(() => api.get<ApiResponse<SalaryStructureHistory[]>>(`/api/payroll/salary-structures/${id}/history`))
}

export type SalaryPayFrequency = 'Monthly' | 'BiWeekly' | 'Weekly' | 'Daily'
export type EmployeeSalaryAssignmentStatus = 'Active' | 'Inactive'
export type SalaryChangeReason = 'NewHire' | 'Confirmation' | 'Increment' | 'Promotion' | 'Demotion' | 'Transfer' | 'Correction' | 'ContractRevision' | 'Other'

export interface EmployeeSalaryComponentOverride {
  id: string
  salaryStructureComponentId: string
  salaryComponentId: string
  salaryComponentCode: string
  salaryComponentName: string
  calculationType: SalaryStructureCalculationType
  isEditableAtEmployeeLevel: boolean
  overrideValue?: number | null
  overridePercentage?: number | null
  overrideFormula?: string | null
  effectiveFrom: string
  effectiveTo?: string | null
  isActive: boolean
  remarks?: string | null
}

export interface EmployeeSalaryAssignment {
  id: string
  employeeId: string
  employeeCode: string
  employeeName: string
  salaryStructureId: string
  salaryStructureCode: string
  salaryStructureName: string
  salaryStructureVersionId: string
  effectiveFrom: string
  effectiveTo?: string | null
  annualCtc?: number | null
  monthlyCtc?: number | null
  currencyCode: string
  payFrequency: SalaryPayFrequency
  status: EmployeeSalaryAssignmentStatus
  changeReason: SalaryChangeReason
  remarks?: string | null
  concurrencyVersion: number
  components: EmployeeSalaryComponentOverride[]
}

export interface EmployeeSalaryComponentRequest {
  salaryStructureComponentId: string
  overrideValue?: number | null
  overridePercentage?: number | null
  overrideFormula?: string | null
  effectiveFrom: string
  effectiveTo?: string | null
  isActive: boolean
  remarks?: string | null
}

export interface EmployeeSalaryAssignmentRequest {
  employeeId: string
  salaryStructureId: string
  effectiveFrom: string
  effectiveTo?: string | null
  annualCtc?: number | null
  monthlyCtc?: number | null
  currencyCode: string
  payFrequency: SalaryPayFrequency
  status: EmployeeSalaryAssignmentStatus
  changeReason: SalaryChangeReason
  remarks?: string | null
  expectedConcurrencyVersion?: number
  components: EmployeeSalaryComponentRequest[]
}

export interface EmployeeSalaryAssignmentHistory {
  id: string
  assignmentId: string
  changeType: string
  effectiveFrom: string
  effectiveTo?: string | null
  annualCtc?: number | null
  monthlyCtc?: number | null
  changeReason: SalaryChangeReason
  componentsJson: string
  changedAtUtc: string
}

export function listEmployeeSalaryAssignments(params: QueryParams = {}, signal?: AbortSignal): Promise<PagedResult<EmployeeSalaryAssignment>> {
  return request<PagedResult<EmployeeSalaryAssignment>>(() => api.get<ApiResponse<PagedResult<EmployeeSalaryAssignment>>>('/api/payroll/employee-salary-assignments', { params: cleanParams(params), signal }))
}
export function listEmployeeSalaryAssignmentsForEmployee(employeeId: string, params: QueryParams = {}): Promise<PagedResult<EmployeeSalaryAssignment>> {
  return request<PagedResult<EmployeeSalaryAssignment>>(() => api.get<ApiResponse<PagedResult<EmployeeSalaryAssignment>>>(`/api/payroll/employee-salary-assignments/by-employee/${employeeId}`, { params: cleanParams(params) }))
}
export function getEmployeeSalaryAssignment(id: string): Promise<EmployeeSalaryAssignment> {
  return request<EmployeeSalaryAssignment>(() => api.get<ApiResponse<EmployeeSalaryAssignment>>(`/api/payroll/employee-salary-assignments/${id}`))
}
export function createEmployeeSalaryAssignment(body: EmployeeSalaryAssignmentRequest): Promise<EmployeeSalaryAssignment> {
  return request<EmployeeSalaryAssignment>(() => api.post<ApiResponse<EmployeeSalaryAssignment>>('/api/payroll/employee-salary-assignments', body))
}
export function updateEmployeeSalaryAssignment(id: string, body: EmployeeSalaryAssignmentRequest): Promise<EmployeeSalaryAssignment> {
  return request<EmployeeSalaryAssignment>(() => api.put<ApiResponse<EmployeeSalaryAssignment>>(`/api/payroll/employee-salary-assignments/${id}`, body))
}
export function setEmployeeSalaryAssignmentActive(id: string, active: boolean, version: number): Promise<EmployeeSalaryAssignment> {
  return request<EmployeeSalaryAssignment>(() => api.post<ApiResponse<EmployeeSalaryAssignment>>(`/api/payroll/employee-salary-assignments/${id}/${active ? 'activate' : 'deactivate'}`, undefined, { params: { expectedConcurrencyVersion: version } }))
}
export function getEmployeeSalaryAssignmentHistory(id: string): Promise<EmployeeSalaryAssignmentHistory[]> {
  return request<EmployeeSalaryAssignmentHistory[]>(() => api.get<ApiResponse<EmployeeSalaryAssignmentHistory[]>>(`/api/payroll/employee-salary-assignments/${id}/history`))
}

export type PayrollPeriodType = 'Monthly' | 'BiWeekly' | 'Weekly' | 'SemiMonthly' | 'Custom'
export type PayrollPeriodStatus = 'Draft' | 'Open' | 'Closed' | 'Locked'
export type PayrollRunType = 'Regular' | 'Supplementary' | 'OffCycle'
export type PayrollRunStatus = 'Draft' | 'Prepared' | 'Processing' | 'Calculated' | 'Approved' | 'Finalized' | 'Cancelled'
export interface PayrollPeriod { id: string; code: string; name: string; periodType: PayrollPeriodType; startDate: string; endDate: string; payDate: string; fiscalYear: number; periodNumber: number; status: PayrollPeriodStatus; isActive: boolean; runCount: number; concurrencyVersion: number }
export interface PayrollPeriodRequest { code: string; name: string; periodType: PayrollPeriodType; startDate: string; endDate: string; payDate: string; fiscalYear: number; periodNumber: number; isActive: boolean; expectedConcurrencyVersion?: number }
export interface PayrollRun { id: string; payrollPeriodId: string; payrollPeriodCode: string; runNumber: string; runType: PayrollRunType; status: PayrollRunStatus; startedAtUtc?: string | null; startedByUserId?: string | null; employeeCount: number; eligibleCount: number; excludedCount: number; notes?: string | null; concurrencyVersion: number }
export interface PayrollRunEmployee { id: string; employeeId: string; employeeCode: string; employeeName: string; employeeSalaryAssignmentId?: string | null; salaryStructureId?: string | null; salaryStructureVersionId?: string | null; employmentSnapshotDate: string; isEligible: boolean; exclusionReason?: string | null; status: string }
export interface PayrollRunRequest { payrollPeriodId: string; runType: PayrollRunType; notes?: string | null }
export interface PayrollCalculationSummary { payrollRunId: string; status: PayrollRunStatus; employeeCount: number; calculatedCount: number; failedCount: number; grossEarnings: number; totalDeductions: number; netPay: number }
export interface PayrollResultComponent { id: string; salaryComponentId: string; salaryStructureComponentId?: string | null; componentCode: string; componentName: string; componentType: string; calculationType: string; baseAmount?: number | null; rate?: number | null; calculatedAmount: number; isEarning: boolean; isDeduction: boolean; isProrated: boolean; calculationSequence: number; calculationSource: string; calculationMetadata?: string | null }
export interface PayrollResult { id: string; payrollRunId: string; employeeId: string; employeeCode: string; employeeName: string; employeeSalaryAssignmentId: string; salaryStructureId: string; salaryStructureVersionId: string; periodStartDate: string; periodEndDate: string; currencyCode: string; grossEarnings: number; totalDeductions: number; netPay: number; status: string; calculationVersion: number; components: PayrollResultComponent[] }
export interface PayrollCalculationError { id: string; payrollRunId: string; payrollRunEmployeeId: string; employeeId: string; employeeCode: string; errorCode: string; message: string; salaryComponentId?: string | null; createdAtUtc: string }
export function listPayrollPeriods(params: QueryParams = {}): Promise<PagedResult<PayrollPeriod>> { return request<PagedResult<PayrollPeriod>>(() => api.get<ApiResponse<PagedResult<PayrollPeriod>>>('/api/payroll/periods', { params: cleanParams(params) })) }
export function createPayrollPeriod(body: PayrollPeriodRequest): Promise<PayrollPeriod> { return request<PayrollPeriod>(() => api.post<ApiResponse<PayrollPeriod>>('/api/payroll/periods', body)) }
export function transitionPayrollPeriod(id: string, action: string, version: number): Promise<PayrollPeriod> { return request<PayrollPeriod>(() => api.post<ApiResponse<PayrollPeriod>>(`/api/payroll/periods/${id}/${action}`, undefined, { params: { expectedConcurrencyVersion: version } })) }
export function listPayrollRuns(params: QueryParams = {}): Promise<PagedResult<PayrollRun>> { return request<PagedResult<PayrollRun>>(() => api.get<ApiResponse<PagedResult<PayrollRun>>>('/api/payroll/runs', { params: cleanParams(params) })) }
export function createPayrollRun(body: PayrollRunRequest): Promise<PayrollRun> { return request<PayrollRun>(() => api.post<ApiResponse<PayrollRun>>('/api/payroll/runs', body)) }
export function preparePayrollRun(id: string, rebuild = false): Promise<PayrollRun> { return request<PayrollRun>(() => api.post<ApiResponse<PayrollRun>>(`/api/payroll/runs/${id}/prepare`, undefined, { params: { rebuild } })) }
export function transitionPayrollRun(id: string, target: PayrollRunStatus): Promise<PayrollRun> { return request<PayrollRun>(() => api.post<ApiResponse<PayrollRun>>(`/api/payroll/runs/${id}/transition`, undefined, { params: { target } })) }
export function calculatePayrollRun(id: string): Promise<PayrollCalculationSummary> { return request<PayrollCalculationSummary>(() => api.post<ApiResponse<PayrollCalculationSummary>>(`/api/payroll/runs/${id}/calculate`)) }
export function recalculatePayrollRun(id: string): Promise<PayrollCalculationSummary> { return request<PayrollCalculationSummary>(() => api.post<ApiResponse<PayrollCalculationSummary>>(`/api/payroll/runs/${id}/recalculate`)) }
export function listPayrollResults(id: string, params: QueryParams = {}): Promise<PagedResult<PayrollResult>> { return request<PagedResult<PayrollResult>>(() => api.get<ApiResponse<PagedResult<PayrollResult>>>(`/api/payroll/runs/${id}/results`, { params: cleanParams(params) })) }
export function listPayrollCalculationErrors(id: string): Promise<PayrollCalculationError[]> { return request<PayrollCalculationError[]>(() => api.get<ApiResponse<PayrollCalculationError[]>>(`/api/payroll/runs/${id}/calculation-errors`)) }
export type PayslipStatus = 'Generated' | 'Published' | 'Superseded' | 'Void'
export interface PayslipLine { id: string; componentCode: string; componentName: string; componentType: string; displayGroup: string; amount: number; sequence: number; source: string; isStatutory: boolean; employerAmount?: number | null }
export interface Payslip { id: string; payrollRunId: string; payrollResultId: string; employeeId: string; payslipNumber: string; periodStartDate: string; periodEndDate: string; payDate: string; currencyCode: string; grossEarnings: number; totalDeductions: number; netPay: number; employerContributionTotal: number; employeeCode: string; employeeName: string; designation?: string | null; department?: string | null; workLocation?: string | null; status: PayslipStatus; version: number; generatedAtUtc: string; lines: PayslipLine[] }
export interface PayrollRegisterRow { employeeId: string; employeeCode: string; employeeName: string; department?: string | null; workLocation?: string | null; grossEarnings: number; totalDeductions: number; employerContributions: number; netPay: number; currencyCode: string; status: string }
export function listMyPayslips(params: QueryParams = {}): Promise<PagedResult<Payslip>> { return request<PagedResult<Payslip>>(() => api.get<ApiResponse<PagedResult<Payslip>>>('/api/me/payslips', { params: cleanParams(params) })) }
export function getMyPayslip(id: string): Promise<Payslip> { return request<Payslip>(() => api.get<ApiResponse<Payslip>>(`/api/me/payslips/${id}`)) }
export async function getMyPayslipDocument(id: string): Promise<string> { const response = await api.get<string>(`/api/me/payslips/${id}/document`); return response.data }
export function generatePayslips(runId: string): Promise<Payslip[]> { return request<Payslip[]>(() => api.post<ApiResponse<Payslip[]>>(`/api/payroll/runs/${runId}/payslips/generate`)) }
export function publishPayslips(runId: string): Promise<Payslip[]> { return request<Payslip[]>(() => api.post<ApiResponse<Payslip[]>>(`/api/payroll/runs/${runId}/payslips/publish`)) }
export function listPayrollRegister(runId: string, params: QueryParams = {}): Promise<PagedResult<PayrollRegisterRow>> { return request<PagedResult<PayrollRegisterRow>>(() => api.get<ApiResponse<PagedResult<PayrollRegisterRow>>>(`/api/payroll/runs/${runId}/register`, { params: cleanParams(params) })) }
