import { api, cleanParams, request } from './client.ts'
import type {
  ApiResponse,
  Country,
  CountryQuery,
  CountryRequest,
  PagedResult,
} from './types.ts'

export function listCountries(
  query: CountryQuery = {},
  signal?: AbortSignal,
): Promise<PagedResult<Country>> {
  return request<PagedResult<Country>>(() =>
    api.get<ApiResponse<PagedResult<Country>>>('/api/countries', {
      params: cleanParams({ ...query }),
      signal,
    }),
  )
}

export function getCountry(id: string, signal?: AbortSignal): Promise<Country> {
  return request<Country>(() =>
    api.get<ApiResponse<Country>>(`/api/countries/${id}`, { signal }),
  )
}

export function createCountry(
  body: CountryRequest,
  signal?: AbortSignal,
): Promise<Country> {
  return request<Country>(() =>
    api.post<ApiResponse<Country>>('/api/countries', body, { signal }),
  )
}

export function updateCountry(
  id: string,
  body: CountryRequest,
  signal?: AbortSignal,
): Promise<Country> {
  return request<Country>(() =>
    api.put<ApiResponse<Country>>(`/api/countries/${id}`, body, { signal }),
  )
}

export function deleteCountry(id: string, signal?: AbortSignal): Promise<boolean> {
  return request<boolean>(() =>
    api.delete<ApiResponse<boolean>>(`/api/countries/${id}`, { signal }),
  )
}
