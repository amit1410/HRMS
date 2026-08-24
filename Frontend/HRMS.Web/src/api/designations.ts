import { api, cleanParams, request } from './client.ts'
import type {
  ApiResponse,
  Designation,
  DesignationQuery,
  DesignationRequest,
  PagedResult,
} from './types.ts'

/** Designation (job title) endpoints. Same shape as departments, down to the full-replacement `PUT`. */

export function listDesignations(
  query: DesignationQuery = {},
  signal?: AbortSignal,
): Promise<PagedResult<Designation>> {
  return request<PagedResult<Designation>>(() =>
    api.get<ApiResponse<PagedResult<Designation>>>('/api/designations', {
      params: cleanParams({ ...query }),
      signal,
    }),
  )
}

export function getDesignation(id: string, signal?: AbortSignal): Promise<Designation> {
  return request<Designation>(() =>
    api.get<ApiResponse<Designation>>(`/api/designations/${id}`, { signal }),
  )
}

export function createDesignation(
  body: DesignationRequest,
  signal?: AbortSignal,
): Promise<Designation> {
  return request<Designation>(() =>
    api.post<ApiResponse<Designation>>('/api/designations', body, { signal }),
  )
}

export function updateDesignation(
  id: string,
  body: DesignationRequest,
  signal?: AbortSignal,
): Promise<Designation> {
  return request<Designation>(() =>
    api.put<ApiResponse<Designation>>(`/api/designations/${id}`, body, { signal }),
  )
}

/** Deletes a designation. Refused with 409 while the title is still held by an employee. */
export function deleteDesignation(id: string, signal?: AbortSignal): Promise<boolean> {
  return request<boolean>(() =>
    api.delete<ApiResponse<boolean>>(`/api/designations/${id}`, { signal }),
  )
}
