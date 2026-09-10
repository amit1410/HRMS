import { api, request } from './client.ts'
import type { ApiResponse } from './types.ts'

export interface Holiday { id: string; name: string; date: string; countryLocationId?: string | null; workLocationId?: string | null; isActive: boolean; createdDate: string; modifiedDate?: string | null; concurrencyToken: string }
export interface HolidayRequest { name: string; date: string; countryLocationId?: string | null; workLocationId?: string | null; isActive: boolean; concurrencyToken?: string }
export interface WeeklyOffConfiguration { id: string; effectiveFrom: string; effectiveTo?: string | null; countryLocationId?: string | null; workLocationId?: string | null; days: number[]; isActive: boolean; createdDate: string; modifiedDate?: string | null; concurrencyToken: string }
export interface WeeklyOffConfigurationRequest { effectiveFrom: string; effectiveTo?: string | null; countryLocationId?: string | null; workLocationId?: string | null; days: number[]; isActive: boolean; concurrencyToken?: string }

export function listHolidays(params?: Record<string, unknown>, signal?: AbortSignal): Promise<Holiday[]> { return request(() => api.get<ApiResponse<Holiday[]>>('/api/holidays', { params, signal })) }
export function createHoliday(body: HolidayRequest): Promise<Holiday> { return request(() => api.post<ApiResponse<Holiday>>('/api/holidays', body)) }
export function updateHoliday(id: string, body: HolidayRequest): Promise<Holiday> { return request(() => api.put<ApiResponse<Holiday>>(`/api/holidays/${id}`, body)) }
export function deactivateHoliday(id: string): Promise<Holiday> { return request(() => api.delete<ApiResponse<Holiday>>(`/api/holidays/${id}`)) }
export function listWeeklyOffConfigurations(signal?: AbortSignal): Promise<WeeklyOffConfiguration[]> { return request(() => api.get<ApiResponse<WeeklyOffConfiguration[]>>('/api/weekly-off-configurations', { signal })) }
export function createWeeklyOffConfiguration(body: WeeklyOffConfigurationRequest): Promise<WeeklyOffConfiguration> { return request(() => api.post<ApiResponse<WeeklyOffConfiguration>>('/api/weekly-off-configurations', body)) }
export function updateWeeklyOffConfiguration(id: string, body: WeeklyOffConfigurationRequest): Promise<WeeklyOffConfiguration> { return request(() => api.put<ApiResponse<WeeklyOffConfiguration>>(`/api/weekly-off-configurations/${id}`, body)) }
