import { api, cleanParams, request } from './client.ts'
import type {
  ApiResponse,
  City,
  CityQuery,
  CityRequest,
  PagedResult,
} from './types.ts'

export function listCities(
  query: CityQuery = {},
  signal?: AbortSignal,
): Promise<PagedResult<City>> {
  return request<PagedResult<City>>(() =>
    api.get<ApiResponse<PagedResult<City>>>('/api/cities', {
      params: cleanParams({ ...query }),
      signal,
    }),
  )
}

export function getCity(id: string, signal?: AbortSignal): Promise<City> {
  return request<City>(() =>
    api.get<ApiResponse<City>>(`/api/cities/${id}`, { signal }),
  )
}

export function createCity(
  body: CityRequest,
  signal?: AbortSignal,
): Promise<City> {
  return request<City>(() =>
    api.post<ApiResponse<City>>('/api/cities', body, { signal }),
  )
}

export function updateCity(
  id: string,
  body: CityRequest,
  signal?: AbortSignal,
): Promise<City> {
  return request<City>(() =>
    api.put<ApiResponse<City>>(`/api/cities/${id}`, body, { signal }),
  )
}

export function deleteCity(id: string, signal?: AbortSignal): Promise<boolean> {
  return request<boolean>(() =>
    api.delete<ApiResponse<boolean>>(`/api/cities/${id}`, { signal }),
  )
}
