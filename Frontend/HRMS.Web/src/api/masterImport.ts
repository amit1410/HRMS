import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export type MasterImportMode = 'CreateOnly' | 'CreateOrUpdate'
export interface MasterImportRow { rowNumber: number; code: string; name: string; description?: string | null; isActive: boolean; parentCode?: string | null }
export interface MasterImportRowResult { rowNumber: number; code: string; name: string; parentCode?: string | null; action: string; errors: string[] }
export interface MasterImportPreview { masterType: string; mode: MasterImportMode; inputRows: MasterImportRow[]; rows: MasterImportRowResult[]; totalRows: number; validRows: number; newRows: number; updateRows: number; skippedRows: number; errorRows: number }
export interface MasterImportResult { batchId: string; totalRows: number; createdRows: number; updatedRows: number; skippedRows: number; failedRows: number; status: string; completedAtUtc: string }

export async function downloadMasterTemplate(kind: string): Promise<Blob> {
  const response = await api.get(`/api/master-import/${kind}/template`, { responseType: 'blob' })
  return response.data as Blob
}

export function validateMasterImport(kind: string, file: File, mode: MasterImportMode) {
  const body = new FormData(); body.append('file', file); body.append('mode', mode)
  return request<MasterImportPreview>(() => api.post<ApiResponse<MasterImportPreview>>(`/api/master-import/${kind}/validate`, body, { headers: { 'Content-Type': 'multipart/form-data' } }))
}

export function confirmMasterImport(kind: string, mode: MasterImportMode, fileName: string, rows: MasterImportRow[]) {
  return request<MasterImportResult>(() => api.post<ApiResponse<MasterImportResult>>(`/api/master-import/${kind}/confirm`, { masterType: kind, mode, fileName, rows }))
}
