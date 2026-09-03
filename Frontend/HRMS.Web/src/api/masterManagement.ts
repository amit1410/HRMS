import { api, cleanParams, request } from './client.ts'
import type { ApiResponse } from './types.ts'
import { createDepartment, deleteDepartment, listDepartments, updateDepartment } from './departments.ts'
import { createDesignation, deleteDesignation, listDesignations, updateDesignation } from './designations.ts'

export interface MasterRecord {
  id: string
  code: string
  name: string
  description?: string | null
  isActive: boolean
  parentId?: string | null
  parentCode?: string | null
  parentName?: string | null
}

export interface MasterPage { items: MasterRecord[]; page: number; pageSize: number; totalCount: number; totalPages: number }
export interface MasterRequest { code: string; name: string; description?: string | null; isActive: boolean; parentId?: string | null }

export function listManagedMasters(kind: string, query: Record<string, string | number | boolean | undefined> = {}, signal?: AbortSignal) {
  if (kind === 'departments') return listDepartments(query as never, signal).then(result => ({ ...result, items: result.items.map(toMasterRecord) }))
  if (kind === 'designations') return listDesignations(query as never, signal).then(result => ({ ...result, items: result.items.map(toMasterRecord) }))
  return request<MasterPage>(() => api.get<ApiResponse<MasterPage>>(`/api/masters/${kind}`, { params: cleanParams(query), signal }))
}

export function getManagedMaster(kind: string, id: string, signal?: AbortSignal) {
  return request<MasterRecord>(() => api.get<ApiResponse<MasterRecord>>(`/api/masters/${kind}/${id}`, { signal }))
}

export function createManagedMaster(kind: string, body: MasterRequest) {
  if (kind === 'departments') return createDepartment(body)
  if (kind === 'designations') return createDesignation(body)
  return request<MasterRecord>(() => api.post<ApiResponse<MasterRecord>>(`/api/masters/${kind}`, body))
}

export function updateManagedMaster(kind: string, id: string, body: MasterRequest) {
  if (kind === 'departments') return updateDepartment(id, body)
  if (kind === 'designations') return updateDesignation(id, body)
  return request<MasterRecord>(() => api.put<ApiResponse<MasterRecord>>(`/api/masters/${kind}/${id}`, body))
}

export function deleteManagedMaster(kind: string, id: string) {
  if (kind === 'departments') return deleteDepartment(id)
  if (kind === 'designations') return deleteDesignation(id)
  return request<boolean>(() => api.delete<ApiResponse<boolean>>(`/api/masters/${kind}/${id}`))
}

function toMasterRecord(record: { id: string; code: string; name: string; description?: string | null; isActive: boolean }): MasterRecord {
  return { id: record.id, code: record.code, name: record.name, description: record.description, isActive: record.isActive }
}
