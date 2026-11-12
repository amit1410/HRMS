import { api, request } from './client.ts'
import { toApiError } from './errors.ts'
import type { ApiResponse } from './types.ts'

export const EMPLOYEE_BULK_UPLOAD_MAX_BYTES = 10 * 1024 * 1024

export interface EmployeeBulkUploadIssue {
  fieldName: string
  columnName: string
  columnNumber: number
  errorCode: string
  message: string
  guidance: string
}

export interface EmployeeBulkUploadPreviewRow {
  rowNumber: number
  status: string
  issues: EmployeeBulkUploadIssue[]
}

export interface EmployeeBulkUploadPreview {
  totalRows: number
  validRows: number
  invalidRows: number
  warningRows: number
  rows: EmployeeBulkUploadPreviewRow[]
}

export interface EmployeeBulkUploadPreparation {
  operationId: string
  confirmationToken: string
  totalRows: number
  summary: string
}

export interface EmployeeBulkImportStatus {
  operationId: string
  state: string
  totalRows: number
  succeededRows: number
  failedRows: number
  skippedRows: number
  startedAtUtc: string | null
  completedAtUtc: string | null
  lastUpdatedAtUtc: string
  summary: string | null
  rows: Array<{ excelRowNumber: number; status: string; employeeCode: string | null; errorCode: string | null; message: string | null }>
}

export interface EmployeeBulkImportHistoryItem {
  operationId: string
  state: string
  totalRows: number
  succeededRows: number
  failedRows: number
  skippedRows: number
  preparedAtUtc: string
  completedAtUtc: string | null
  summary: string | null
}

export interface EmployeeBulkImportHistory {
  items: EmployeeBulkImportHistoryItem[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface EmployeeBulkImportAccepted {
  operationId: string
  state: string
  statusUrl: string
}

export async function downloadEmployeeBulkUploadTemplate(): Promise<Blob> {
  try {
    return (await api.get<Blob>('/api/employees/bulk-upload/template', { responseType: 'blob' })).data
  } catch (error) {
    throw toApiError(error)
  }
}

/** The validation endpoint returns the preview DTO directly rather than the standard API envelope. */
export async function validateEmployeeBulkUpload(file: File): Promise<EmployeeBulkUploadPreview> {
  const form = new FormData()
  form.append('file', file)
  try {
    return (await api.post<EmployeeBulkUploadPreview>('/api/employees/bulk-upload/validate', form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })).data
  } catch (error) {
    throw toApiError(error)
  }
}

export function prepareEmployeeBulkUpload(file: File): Promise<EmployeeBulkUploadPreparation> {
  const form = new FormData()
  form.append('file', file)
  return request(() => api.post<ApiResponse<EmployeeBulkUploadPreparation>>('/api/employees/bulk-upload/prepare', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  }))
}

export function confirmEmployeeBulkUpload(operationId: string, confirmationToken: string): Promise<EmployeeBulkImportAccepted> {
  return request(() => api.post<ApiResponse<EmployeeBulkImportAccepted>>('/api/employees/bulk-upload/confirm', {
    operationId,
    confirmationToken,
    confirm: true,
  }))
}

export async function getEmployeeBulkImportStatus(operationId: string, signal?: AbortSignal): Promise<EmployeeBulkImportStatus> {
  return request(() => api.get<ApiResponse<EmployeeBulkImportStatus>>(`/api/employees/bulk-upload/${encodeURIComponent(operationId)}`, { signal }))
}

export async function getEmployeeBulkImportHistory(page: number, pageSize: number, signal?: AbortSignal): Promise<EmployeeBulkImportHistory> {
  return request(() => api.get<ApiResponse<EmployeeBulkImportHistory>>('/api/employees/bulk-upload/history', {
    params: { page, pageSize },
    signal,
  }))
}
