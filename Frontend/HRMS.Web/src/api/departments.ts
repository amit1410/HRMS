import { api, cleanParams, request } from './client.ts'
import type {
  ApiResponse,
  Department,
  DepartmentQuery,
  DepartmentRequest,
  PagedResult,
} from './types.ts'

/**
 * Department endpoints.
 *
 * `PUT` is a full replacement, matching the API: `DepartmentRequest` carries every field, so an omitted
 * optional one is cleared rather than left as it was. The forms therefore submit the whole record, never
 * a patch of the fields that changed.
 */

export function listDepartments(
  query: DepartmentQuery = {},
  signal?: AbortSignal,
): Promise<PagedResult<Department>> {
  return request<PagedResult<Department>>(() =>
    api.get<ApiResponse<PagedResult<Department>>>('/api/departments', {
      params: cleanParams({ ...query }),
      signal,
    }),
  )
}

export function getDepartment(id: string, signal?: AbortSignal): Promise<Department> {
  return request<Department>(() =>
    api.get<ApiResponse<Department>>(`/api/departments/${id}`, { signal }),
  )
}

export function createDepartment(
  body: DepartmentRequest,
  signal?: AbortSignal,
): Promise<Department> {
  return request<Department>(() =>
    api.post<ApiResponse<Department>>('/api/departments', body, { signal }),
  )
}

export function updateDepartment(
  id: string,
  body: DepartmentRequest,
  signal?: AbortSignal,
): Promise<Department> {
  return request<Department>(() =>
    api.put<ApiResponse<Department>>(`/api/departments/${id}`, body, { signal }),
  )
}

/**
 * Deletes a department. Refused with 409 while employees are still assigned — the caller shows the
 * message, which names the count and suggests deactivating instead.
 */
export function deleteDepartment(id: string, signal?: AbortSignal): Promise<boolean> {
  return request<boolean>(() =>
    api.delete<ApiResponse<boolean>>(`/api/departments/${id}`, { signal }),
  )
}
