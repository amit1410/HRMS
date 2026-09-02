import { api, request } from './client.ts'
import type {
  ApiResponse,
  EmployeeAdditionalInfo,
  EmployeeAdditionalInfoRequest,
  EmployeeAddress,
  EmployeeAddressRequest,
  EmployeeAuditLog,
  EmployeeBankDetail,
  EmployeeBankDetailEdit,
  EmployeeBankDetailRequest,
  EmployeeContact,
  EmployeeContactRequest,
  EmployeeDocument,
  EmployeeDocumentRequest,
  EmployeeEducation,
  EmployeeEducationRequest,
  EmployeeEmployment,
  EmployeeEmploymentHistory,
  EmployeeEmploymentRequest,
  EmployeeFamily,
  EmployeeFamilyRequest,
  EmployeePreviousEmployment,
  EmployeePreviousEmploymentRequest,
  EmployeeSupervisor,
  EmployeeSupervisorRequest,
  EmploymentChangeRequest,
  PagedResult,
  AuditQuery,
  ImportBatch,
  SupervisorOption,
  SupervisorType,
} from './types.ts'

// ---------------------------------------------------------------------------------------------
// Contact
// ---------------------------------------------------------------------------------------------

export function getContact(employeeId: string, signal?: AbortSignal): Promise<EmployeeContact> {
  return request<EmployeeContact>(() =>
    api.get<ApiResponse<EmployeeContact>>(`/api/employees/${employeeId}/contact`, { signal }),
  )
}

export function upsertContact(
  employeeId: string,
  body: EmployeeContactRequest,
  signal?: AbortSignal,
): Promise<EmployeeContact> {
  return request<EmployeeContact>(() =>
    api.put<ApiResponse<EmployeeContact>>(`/api/employees/${employeeId}/contact`, body, { signal }),
  )
}

// ---------------------------------------------------------------------------------------------
// Addresses
// ---------------------------------------------------------------------------------------------

export function getAddresses(employeeId: string, signal?: AbortSignal): Promise<EmployeeAddress[]> {
  return request<EmployeeAddress[]>(() =>
    api.get<ApiResponse<EmployeeAddress[]>>(`/api/employees/${employeeId}/addresses`, { signal }),
  )
}

export function upsertAddress(
  employeeId: string,
  body: EmployeeAddressRequest,
  signal?: AbortSignal,
): Promise<EmployeeAddress> {
  return request<EmployeeAddress>(() =>
    api.post<ApiResponse<EmployeeAddress>>(`/api/employees/${employeeId}/addresses`, body, { signal }),
  )
}

export function deleteAddress(
  employeeId: string,
  addressId: string,
  signal?: AbortSignal,
): Promise<boolean> {
  return request<boolean>(() =>
    api.delete<ApiResponse<boolean>>(`/api/employees/${employeeId}/addresses/${addressId}`, { signal }),
  )
}

// ---------------------------------------------------------------------------------------------
// Family
// ---------------------------------------------------------------------------------------------

export function getFamilyMembers(employeeId: string, signal?: AbortSignal): Promise<EmployeeFamily[]> {
  return request<EmployeeFamily[]>(() =>
    api.get<ApiResponse<EmployeeFamily[]>>(`/api/employees/${employeeId}/family`, { signal }),
  )
}

export function createFamilyMember(
  employeeId: string,
  body: EmployeeFamilyRequest,
  signal?: AbortSignal,
): Promise<EmployeeFamily> {
  return request<EmployeeFamily>(() =>
    api.post<ApiResponse<EmployeeFamily>>(`/api/employees/${employeeId}/family`, body, { signal }),
  )
}

export function updateFamilyMember(
  employeeId: string,
  familyId: string,
  body: EmployeeFamilyRequest,
  signal?: AbortSignal,
): Promise<EmployeeFamily> {
  return request<EmployeeFamily>(() =>
    api.put<ApiResponse<EmployeeFamily>>(`/api/employees/${employeeId}/family/${familyId}`, body, { signal }),
  )
}

export function deleteFamilyMember(
  employeeId: string,
  familyId: string,
  signal?: AbortSignal,
): Promise<boolean> {
  return request<boolean>(() =>
    api.delete<ApiResponse<boolean>>(`/api/employees/${employeeId}/family/${familyId}`, { signal }),
  )
}

// ---------------------------------------------------------------------------------------------
// Education
// ---------------------------------------------------------------------------------------------

export function getEducationRecords(employeeId: string, signal?: AbortSignal): Promise<EmployeeEducation[]> {
  return request<EmployeeEducation[]>(() =>
    api.get<ApiResponse<EmployeeEducation[]>>(`/api/employees/${employeeId}/education`, { signal }),
  )
}

export function createEducationRecord(
  employeeId: string,
  body: EmployeeEducationRequest,
  signal?: AbortSignal,
): Promise<EmployeeEducation> {
  return request<EmployeeEducation>(() =>
    api.post<ApiResponse<EmployeeEducation>>(`/api/employees/${employeeId}/education`, body, { signal }),
  )
}

export function updateEducationRecord(
  employeeId: string,
  educationId: string,
  body: EmployeeEducationRequest,
  signal?: AbortSignal,
): Promise<EmployeeEducation> {
  return request<EmployeeEducation>(() =>
    api.put<ApiResponse<EmployeeEducation>>(`/api/employees/${employeeId}/education/${educationId}`, body, { signal }),
  )
}

export function deleteEducationRecord(
  employeeId: string,
  educationId: string,
  signal?: AbortSignal,
): Promise<boolean> {
  return request<boolean>(() =>
    api.delete<ApiResponse<boolean>>(`/api/employees/${employeeId}/education/${educationId}`, { signal }),
  )
}

// ---------------------------------------------------------------------------------------------
// Previous Employment
// ---------------------------------------------------------------------------------------------

export function getPreviousEmployments(employeeId: string, signal?: AbortSignal): Promise<EmployeePreviousEmployment[]> {
  return request<EmployeePreviousEmployment[]>(() =>
    api.get<ApiResponse<EmployeePreviousEmployment[]>>(`/api/employees/${employeeId}/previous-employment`, { signal }),
  )
}

export function createPreviousEmployment(
  employeeId: string,
  body: EmployeePreviousEmploymentRequest,
  signal?: AbortSignal,
): Promise<EmployeePreviousEmployment> {
  return request<EmployeePreviousEmployment>(() =>
    api.post<ApiResponse<EmployeePreviousEmployment>>(`/api/employees/${employeeId}/previous-employment`, body, { signal }),
  )
}

export function updatePreviousEmployment(
  employeeId: string,
  previousEmploymentId: string,
  body: EmployeePreviousEmploymentRequest,
  signal?: AbortSignal,
): Promise<EmployeePreviousEmployment> {
  return request<EmployeePreviousEmployment>(() =>
    api.put<ApiResponse<EmployeePreviousEmployment>>(`/api/employees/${employeeId}/previous-employment/${previousEmploymentId}`, body, { signal }),
  )
}

export function deletePreviousEmployment(
  employeeId: string,
  previousEmploymentId: string,
  signal?: AbortSignal,
): Promise<boolean> {
  return request<boolean>(() =>
    api.delete<ApiResponse<boolean>>(`/api/employees/${employeeId}/previous-employment/${previousEmploymentId}`, { signal }),
  )
}

// ---------------------------------------------------------------------------------------------
// Bank Details
// ---------------------------------------------------------------------------------------------

export function getBankDetails(employeeId: string, signal?: AbortSignal): Promise<EmployeeBankDetail[]> {
  return request<EmployeeBankDetail[]>(() =>
    api.get<ApiResponse<EmployeeBankDetail[]>>(`/api/employees/${employeeId}/bank-details`, { signal }),
  )
}

export function getBankDetailForEdit(
  employeeId: string,
  bankDetailId: string,
  signal?: AbortSignal,
): Promise<EmployeeBankDetailEdit> {
  return request<EmployeeBankDetailEdit>(() =>
    api.get<ApiResponse<EmployeeBankDetailEdit>>(
      `/api/employees/${employeeId}/bank-details/${bankDetailId}/sensitive-details`,
      { signal },
    ),
  )
}

export function createBankDetail(
  employeeId: string,
  body: EmployeeBankDetailRequest,
  signal?: AbortSignal,
): Promise<EmployeeBankDetail> {
  return request<EmployeeBankDetail>(() =>
    api.post<ApiResponse<EmployeeBankDetail>>(`/api/employees/${employeeId}/bank-details`, body, { signal }),
  )
}

export function updateBankDetail(
  employeeId: string,
  bankDetailId: string,
  body: EmployeeBankDetailRequest,
  signal?: AbortSignal,
): Promise<EmployeeBankDetail> {
  return request<EmployeeBankDetail>(() =>
    api.put<ApiResponse<EmployeeBankDetail>>(`/api/employees/${employeeId}/bank-details/${bankDetailId}`, body, { signal }),
  )
}

export function deleteBankDetail(
  employeeId: string,
  bankDetailId: string,
  signal?: AbortSignal,
): Promise<boolean> {
  return request<boolean>(() =>
    api.delete<ApiResponse<boolean>>(`/api/employees/${employeeId}/bank-details/${bankDetailId}`, { signal }),
  )
}

// ---------------------------------------------------------------------------------------------
// Supervisor
// ---------------------------------------------------------------------------------------------

export function getSupervisor(employeeId: string, signal?: AbortSignal): Promise<EmployeeSupervisor> {
  return request<EmployeeSupervisor>(() =>
    api.get<ApiResponse<EmployeeSupervisor>>(`/api/employees/${employeeId}/supervisor`, { signal }),
  )
}

export function upsertSupervisor(
  employeeId: string,
  body: EmployeeSupervisorRequest,
  signal?: AbortSignal,
): Promise<EmployeeSupervisor> {
  return request<EmployeeSupervisor>(() =>
    api.put<ApiResponse<EmployeeSupervisor>>(`/api/employees/${employeeId}/supervisor`, body, { signal }),
  )
}

export function getSupervisorOptions(
  employeeId: string,
  type: SupervisorType,
  signal?: AbortSignal,
): Promise<SupervisorOption[]> {
  return request<SupervisorOption[]>(() =>
    api.get<ApiResponse<SupervisorOption[]>>(`/api/employees/${employeeId}/supervisor-options`, {
      params: { type },
      signal,
    }),
  )
}

// ---------------------------------------------------------------------------------------------
// Additional Info
// ---------------------------------------------------------------------------------------------

export function getAdditionalInfo(employeeId: string, signal?: AbortSignal): Promise<EmployeeAdditionalInfo> {
  return request<EmployeeAdditionalInfo>(() =>
    api.get<ApiResponse<EmployeeAdditionalInfo>>(`/api/employees/${employeeId}/additional-info`, { signal }),
  )
}

export function upsertAdditionalInfo(
  employeeId: string,
  body: EmployeeAdditionalInfoRequest,
  signal?: AbortSignal,
): Promise<EmployeeAdditionalInfo> {
  return request<EmployeeAdditionalInfo>(() =>
    api.put<ApiResponse<EmployeeAdditionalInfo>>(`/api/employees/${employeeId}/additional-info`, body, { signal }),
  )
}

// ---------------------------------------------------------------------------------------------
// Employment (Joining Information)
// ---------------------------------------------------------------------------------------------

export function getEmployment(
  employeeId: string,
  signal?: AbortSignal,
): Promise<EmployeeEmployment> {
  return request<EmployeeEmployment>(() =>
    api.get<ApiResponse<EmployeeEmployment>>(`/api/employees/${employeeId}/employment`, { signal }),
  )
}

export function upsertEmployment(
  employeeId: string,
  body: EmployeeEmploymentRequest,
  signal?: AbortSignal,
): Promise<EmployeeEmployment> {
  return request<EmployeeEmployment>(() =>
    api.put<ApiResponse<EmployeeEmployment>>(`/api/employees/${employeeId}/employment`, body, { signal }),
  )
}

// ---------------------------------------------------------------------------------------------
// Employment History
// ---------------------------------------------------------------------------------------------

export function getEmploymentHistory(employeeId: string, signal?: AbortSignal): Promise<EmployeeEmploymentHistory[]> {
  return request<EmployeeEmploymentHistory[]>(() =>
    api.get<ApiResponse<EmployeeEmploymentHistory[]>>(`/api/employees/${employeeId}/employment-history`, { signal }),
  )
}

export function getCurrentEmployment(employeeId: string, signal?: AbortSignal): Promise<EmployeeEmploymentHistory> {
  return request<EmployeeEmploymentHistory>(() =>
    api.get<ApiResponse<EmployeeEmploymentHistory>>(`/api/employees/${employeeId}/employment-history/current`, { signal }),
  )
}

export function createEmploymentChange(
  employeeId: string,
  body: EmploymentChangeRequest,
  signal?: AbortSignal,
): Promise<EmployeeEmploymentHistory> {
  return request<EmployeeEmploymentHistory>(() =>
    api.post<ApiResponse<EmployeeEmploymentHistory>>(`/api/employees/${employeeId}/employment-history`, body, { signal }),
  )
}

// ---------------------------------------------------------------------------------------------
// Audit Log
// ---------------------------------------------------------------------------------------------

export function getAuditLog(
  employeeId: string,
  query: AuditQuery = {},
  signal?: AbortSignal,
): Promise<PagedResult<EmployeeAuditLog>> {
  return request<PagedResult<EmployeeAuditLog>>(() =>
    api.get<ApiResponse<PagedResult<EmployeeAuditLog>>>(`/api/employees/${employeeId}/audit-log`, {
      params: { ...query },
      signal,
    }),
  )
}

// ---------------------------------------------------------------------------------------------
// Documents
// ---------------------------------------------------------------------------------------------

export function getDocuments(employeeId: string, signal?: AbortSignal): Promise<EmployeeDocument[]> {
  return request<EmployeeDocument[]>(() =>
    api.get<ApiResponse<EmployeeDocument[]>>(`/api/employees/${employeeId}/documents`, { signal }),
  )
}

export function uploadDocument(
  employeeId: string,
  body: EmployeeDocumentRequest,
  signal?: AbortSignal,
): Promise<EmployeeDocument> {
  return request<EmployeeDocument>(() =>
    api.post<ApiResponse<EmployeeDocument>>(`/api/employees/${employeeId}/documents`, body, { signal }),
  )
}

export function deleteDocument(
  employeeId: string,
  documentId: string,
  signal?: AbortSignal,
): Promise<boolean> {
  return request<boolean>(() =>
    api.delete<ApiResponse<boolean>>(`/api/employees/${employeeId}/documents/${documentId}`, { signal }),
  )
}

// ---------------------------------------------------------------------------------------------
// Import Batches
// ---------------------------------------------------------------------------------------------

export function getImportBatches(signal?: AbortSignal): Promise<ImportBatch[]> {
  return request<ImportBatch[]>(() =>
    api.get<ApiResponse<ImportBatch[]>>('/api/import', { signal }),
  )
}

export function getImportBatch(batchId: string, signal?: AbortSignal): Promise<ImportBatch> {
  return request<ImportBatch>(() =>
    api.get<ApiResponse<ImportBatch>>(`/api/import/${batchId}`, { signal }),
  )
}

export function createImportBatch(
  fileName?: string,
  signal?: AbortSignal,
): Promise<ImportBatch> {
  return request<ImportBatch>(() =>
    api.post<ApiResponse<ImportBatch>>('/api/import', null, {
      params: fileName ? { fileName } : undefined,
      signal,
    }),
  )
}

export function deleteImportBatch(batchId: string, signal?: AbortSignal): Promise<boolean> {
  return request<boolean>(() =>
    api.delete<ApiResponse<boolean>>(`/api/import/${batchId}`, { signal }),
  )
}
