import { api, cleanParams, request } from './client.ts'
import type {
  ApiResponse,
  State,
  StateQuery,
  StateRequest,
  PagedResult,
} from './types.ts'

export function listStates(
  query: StateQuery = {},
  signal?: AbortSignal,
): Promise<PagedResult<State>> {
  return request<PagedResult<State>>(() =>
    api.get<ApiResponse<PagedResult<State>>>('/api/states', {
      params: cleanParams({ ...query }),
      signal,
    }),
  )
}

export function getState(id: string, signal?: AbortSignal): Promise<State> {
  return request<State>(() =>
    api.get<ApiResponse<State>>(`/api/states/${id}`, { signal }),
  )
}

export function createState(
  body: StateRequest,
  signal?: AbortSignal,
): Promise<State> {
  return request<State>(() =>
    api.post<ApiResponse<State>>('/api/states', body, { signal }),
  )
}

export function updateState(
  id: string,
  body: StateRequest,
  signal?: AbortSignal,
): Promise<State> {
  return request<State>(() =>
    api.put<ApiResponse<State>>(`/api/states/${id}`, body, { signal }),
  )
}

export function deleteState(id: string, signal?: AbortSignal): Promise<boolean> {
  return request<boolean>(() =>
    api.delete<ApiResponse<boolean>>(`/api/states/${id}`, { signal }),
  )
}
