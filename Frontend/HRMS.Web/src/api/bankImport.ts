import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export type BankImportMode = 'CreateOnly' | 'CreateOrUpdate'

export interface BankImportRow {
  rowNumber: number
  bankCode: string
  bankName: string
  shortName?: string | null
  ifscPrefix?: string | null
  bankType?: string | null
  country?: string | null
  active: boolean
  effectiveFrom?: string | null
  remarks?: string | null
}

export interface BankImportRowResult {
  rowNumber: number
  bankCode: string
  bankName: string
  action: string
  errors: string[]
}

export interface BankImportPreview {
  mode: BankImportMode
  inputRows: BankImportRow[]
  rows: BankImportRowResult[]
  totalRows: number
  validRows: number
  newRows: number
  updateRows: number
  skippedRows: number
  errorRows: number
}

export interface BankImportResult {
  batchId: string
  totalRows: number
  createdRows: number
  updatedRows: number
  skippedRows: number
  failedRows: number
  status: string
  completedAtUtc: string
}

export interface BankImportHistory {
  id: string
  fileName?: string | null
  importedBy: string
  totalRows: number
  successfulRows: number
  failedRows: number
  skippedRows: number
  status: string
  startedAtUtc?: string | null
  completedAtUtc?: string | null
  message?: string | null
  createdDate: string
}

export async function downloadBankTemplate(format: 'csv' | 'xlsx' = 'csv'): Promise<Blob> {
  const response = await api.get('/api/bank-import/template', { params: { format }, responseType: 'blob' })
  return response.data as Blob
}

export function validateBankImport(file: File, mode: BankImportMode) {
  const body = new FormData()
  body.append('file', file)
  body.append('mode', mode)
  return request<BankImportPreview>(() =>
    api.post<ApiResponse<BankImportPreview>>('/api/bank-import/validate', body, {
      headers: { 'Content-Type': 'multipart/form-data' },
    }),
  )
}

export function confirmBankImport(mode: BankImportMode, fileName: string, rows: BankImportRow[]) {
  return request<BankImportResult>(() =>
    api.post<ApiResponse<BankImportResult>>('/api/bank-import/confirm', { mode, fileName, rows }),
  )
}

export function listBankImportHistory(signal?: AbortSignal) {
  return request<BankImportHistory[]>(() =>
    api.get<ApiResponse<BankImportHistory[]>>('/api/bank-import/history', { signal }),
  )
}
