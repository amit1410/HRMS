import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'

/** Classification of a bank; mirrors the backend `BankType` enum. */
export type BankType =
  | 'Public'
  | 'Private'
  | 'Cooperative'
  | 'Foreign'
  | 'Payments'
  | 'SmallFinance'
  | 'RegionalRural'
  | 'Other'

export const BANK_TYPES: BankType[] = [
  'Public',
  'Private',
  'Cooperative',
  'Foreign',
  'Payments',
  'SmallFinance',
  'RegionalRural',
  'Other',
]

export interface Bank {
  id: string
  code: string
  name: string
  shortName?: string | null
  ifscPrefix?: string | null
  bankType?: BankType | null
  country?: string | null
  effectiveFrom?: string | null
  remarks?: string | null
  isActive: boolean
  employeeAccountCount: number
  createdDate: string
  modifiedDate?: string | null
}

export interface BankRequest {
  code: string
  name: string
  shortName?: string | null
  ifscPrefix?: string | null
  bankType?: BankType | null
  country?: string | null
  effectiveFrom?: string | null
  remarks?: string | null
  isActive: boolean
}

export interface BankQuery {
  page?: number
  pageSize?: number
  search?: string
  isActive?: boolean
  sortBy?: string
  sortDescending?: boolean
}

export function listBanks(query: BankQuery = {}, signal?: AbortSignal): Promise<PagedResult<Bank>> {
  return request<PagedResult<Bank>>(() =>
    api.get<ApiResponse<PagedResult<Bank>>>('/api/banks', { params: cleanParams({ ...query }), signal }),
  )
}

export function getBank(id: string, signal?: AbortSignal): Promise<Bank> {
  return request<Bank>(() => api.get<ApiResponse<Bank>>(`/api/banks/${id}`, { signal }))
}

export function createBank(body: BankRequest, signal?: AbortSignal): Promise<Bank> {
  return request<Bank>(() => api.post<ApiResponse<Bank>>('/api/banks', body, { signal }))
}

export function updateBank(id: string, body: BankRequest, signal?: AbortSignal): Promise<Bank> {
  return request<Bank>(() => api.put<ApiResponse<Bank>>(`/api/banks/${id}`, body, { signal }))
}

export function activateBank(id: string, signal?: AbortSignal): Promise<Bank> {
  return request<Bank>(() => api.post<ApiResponse<Bank>>(`/api/banks/${id}/activate`, null, { signal }))
}

export function deactivateBank(id: string, signal?: AbortSignal): Promise<Bank> {
  return request<Bank>(() => api.post<ApiResponse<Bank>>(`/api/banks/${id}/deactivate`, null, { signal }))
}

/** Deletes an unreferenced bank. Refused with 409 while employee bank records still reference it. */
export function deleteBank(id: string, signal?: AbortSignal): Promise<boolean> {
  return request<boolean>(() => api.delete<ApiResponse<boolean>>(`/api/banks/${id}`, { signal }))
}

export async function exportBanks(query: BankQuery = {}, format: 'csv' | 'xlsx' = 'csv'): Promise<Blob> {
  const response = await api.get('/api/banks/export', {
    params: cleanParams({ ...query, format }),
    responseType: 'blob',
  })
  return response.data as Blob
}
