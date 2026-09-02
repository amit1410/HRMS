import { AxiosError } from 'axios'
import { api, cleanParams, request } from './client.ts'
import { ApiError, toApiError } from './errors.ts'
import type {
  ApiResponse,
  Employee,
  EmployeeListItem,
  EmployeePersonalDetailsRequest,
  EmployeeQuery,
  EmployeeRequest,
  EmployeeSensitiveDetails,
  PagedResult,
} from './types.ts'

/**
 * Employee reads, writes and the CSV export.
 *
 * The list returns {@link EmployeeListItem} — flattened, with department and designation as names — while
 * a single fetch returns the full {@link Employee} with the ids the form needs. So an edit screen fetches
 * the record even when the row it was opened from is already on screen: the row cannot supply
 * `departmentId`, and guessing it from the name would be a lookup that can fail.
 */

export function listEmployees(
  query: EmployeeQuery = {},
  signal?: AbortSignal,
): Promise<PagedResult<EmployeeListItem>> {
  return request<PagedResult<EmployeeListItem>>(() =>
    api.get<ApiResponse<PagedResult<EmployeeListItem>>>('/api/employees', {
      params: cleanParams({ ...query }),
      signal,
    }),
  )
}

export function getEmployee(id: string, signal?: AbortSignal): Promise<Employee> {
  return request<Employee>(() =>
    api.get<ApiResponse<Employee>>(`/api/employees/${id}`, { signal }),
  )
}

export function getEmployeeSensitiveDetails(
  id: string,
  signal?: AbortSignal,
): Promise<EmployeeSensitiveDetails> {
  return request<EmployeeSensitiveDetails>(() =>
    api.get<ApiResponse<EmployeeSensitiveDetails>>(`/api/employees/${id}/sensitive-details`, { signal }),
  )
}

export function createEmployee(body: EmployeeRequest, signal?: AbortSignal): Promise<Employee> {
  return request<Employee>(() =>
    api.post<ApiResponse<Employee>>('/api/employees', body, { signal }),
  )
}

export function updateEmployee(
  id: string,
  body: EmployeeRequest,
  signal?: AbortSignal,
): Promise<Employee> {
  return request<Employee>(() =>
    api.put<ApiResponse<Employee>>(`/api/employees/${id}`, body, { signal }),
  )
}

/**
 * Creates an employee from the Personal Details section only. The backend assigns the employee code
 * according to the tenant's employee-code configuration, so none is sent here.
 */
export function createPersonalDetails(
  body: EmployeePersonalDetailsRequest,
  signal?: AbortSignal,
): Promise<Employee> {
  return request<Employee>(() =>
    api.post<ApiResponse<Employee>>('/api/employees/personal-details', body, { signal }),
  )
}

/** Updates only the Personal Details fields of an existing employee. */
export function updatePersonalDetails(
  id: string,
  body: EmployeePersonalDetailsRequest,
  signal?: AbortSignal,
): Promise<Employee> {
  return request<Employee>(() =>
    api.put<ApiResponse<Employee>>(`/api/employees/${id}/personal-details`, body, { signal }),
  )
}

/**
 * Deletes an employee. Refused with 409 while others still report to them — the message names how many,
 * so the caller can show it verbatim instead of inventing an explanation.
 */
export function deleteEmployee(id: string, signal?: AbortSignal): Promise<boolean> {
  return request<boolean>(() =>
    api.delete<ApiResponse<boolean>>(`/api/employees/${id}`, { signal }),
  )
}

export interface ExportedFile {
  blob: Blob
  fileName: string
}

/**
 * Downloads the employee directory as CSV under the same filters the list would use.
 *
 * Two wrinkles this has to handle:
 *
 * 1. **The response is a file on success and JSON on failure.** The API refuses a result set above
 *    its 10,000-row cap with a normal error envelope, so with `responseType: 'blob'` the failure body
 *    arrives as a `Blob` that the generic error mapper cannot read. It is decoded here so the user
 *    sees "narrow the filter", not "the request failed".
 * 2. **The filename lives in a header the browser hides by default.** `Content-Disposition` is not a
 *    CORS-safelisted response header, so the API's policy exposes it explicitly
 *    (`WithExposedHeaders("Content-Disposition")`). If it is missing anyway, a sensible name is used
 *    rather than letting the download land as "download".
 */
export async function exportEmployees(
  query: EmployeeQuery = {},
  signal?: AbortSignal,
): Promise<ExportedFile> {
  try {
    const response = await api.get<Blob>('/api/employees/export', {
      params: cleanParams({ ...query }),
      responseType: 'blob',
      signal,
    })

    return {
      blob: response.data,
      fileName:
        fileNameFromContentDisposition(response.headers['content-disposition']) ?? 'employees.csv',
    }
  } catch (error) {
    throw await toExportError(error)
  }
}

/**
 * Decodes an error body that arrived as a `Blob` because the request asked for one.
 *
 * The error reaching this point is already an {@link ApiError}: the response interceptor on `api`
 * normalizes every failure before a caller's `catch` runs, so the original `AxiosError` — and with it
 * the untouched response body — is on `cause` rather than in hand. Both shapes are accepted so the
 * function does not depend on where in the chain it is called from.
 */
async function toExportError(error: unknown): Promise<ApiError> {
  const response = axiosErrorFrom(error)?.response
  if (response?.data instanceof Blob) {
    try {
      const text = await response.data.text()
      const envelope = JSON.parse(text) as ApiResponse<unknown>
      return new ApiError(envelope.message?.trim() || 'The export could not be produced.', {
        status: response.status,
        fieldErrors: Object.fromEntries(
          (envelope.errors ?? []).map((entry) => [entry.field, entry.message]),
        ),
        cause: error,
      })
    } catch {
      // Not JSON after all — an HTML error page, say. Fall through to the generic mapping, which at
      // least knows what the status code means.
    }
  }
  return toApiError(error)
}

function axiosErrorFrom(error: unknown): AxiosError | null {
  if (error instanceof AxiosError) return error
  if (error instanceof ApiError && error.cause instanceof AxiosError) return error.cause
  return null
}

/** Reads `filename*=UTF-8''…` in preference to `filename=…`, per RFC 6266. */
export function fileNameFromContentDisposition(header: unknown): string | null {
  if (typeof header !== 'string') return null

  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header)
  if (encoded?.[1]) {
    try {
      return decodeURIComponent(encoded[1].trim())
    } catch {
      // Malformed percent-encoding; try the plain form below.
    }
  }

  const plain = /filename="?([^";]+)"?/i.exec(header)
  return plain?.[1]?.trim() ?? null
}

/**
 * Hands a downloaded blob to the browser. Kept next to the export call because the object URL must be
 * revoked afterwards — skipping that leaks the whole file for as long as the tab is open.
 */
export function saveFile({ blob, fileName }: ExportedFile): void {
  const url = URL.createObjectURL(blob)
  try {
    const link = document.createElement('a')
    link.href = url
    link.download = fileName
    document.body.appendChild(link)
    link.click()
    link.remove()
  } finally {
    URL.revokeObjectURL(url)
  }
}
