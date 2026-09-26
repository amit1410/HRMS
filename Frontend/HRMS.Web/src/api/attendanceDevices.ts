import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, PagedResult } from './types.ts'

export type AttendanceDeviceStatus = 'Active' | 'Inactive' | 'Disabled'
export type AttendanceDeviceConnectionMode = 'Push' | 'Pull' | 'FileImport' | 'ManualApi'
export type AttendanceDeviceMappingStatus = 'Active' | 'Inactive'
export type AttendanceDeviceIngestionStatus = 'Accepted' | 'Duplicate' | 'Rejected' | 'Unmapped' | 'RequiresPeriodReopen'
export type AttendanceDeviceSyncStatus = 'Running' | 'Succeeded' | 'PartiallySucceeded' | 'Failed'
export type AttendanceDevicePunchDirection = 'Unknown' | 'In' | 'Out'

export interface AttendanceDevice {
  id: string
  code: string
  name: string
  deviceType: string
  vendor?: string | null
  serialNumber?: string | null
  workLocationId?: string | null
  timeZoneId: string
  connectionMode: AttendanceDeviceConnectionMode
  status: AttendanceDeviceStatus
  lastSuccessfulSyncAtUtc?: string | null
  lastAttemptedSyncAtUtc?: string | null
}

/** CredentialReference is write-only metadata. It is intentionally absent from this response type. */
export interface AttendanceDeviceRequest {
  code: string
  name: string
  deviceType: string
  vendor?: string | null
  serialNumber?: string | null
  workLocationId?: string | null
  timeZoneId: string
  connectionMode: AttendanceDeviceConnectionMode
  credentialReference?: string | null
}

export interface AttendanceDeviceMapping {
  id: string
  deviceId: string
  externalEmployeeIdentifier: string
  employeeId: string
  employeeCode: string
  effectiveFrom: string
  effectiveTo?: string | null
  status: AttendanceDeviceMappingStatus
}

export interface AttendanceDeviceMappingRequest {
  deviceId: string
  externalEmployeeIdentifier: string
  employeeId: string
  effectiveFrom: string
  effectiveTo?: string | null
  status: AttendanceDeviceMappingStatus
}

export interface NormalizedAttendancePunch {
  externalEventId: string
  externalEmployeeIdentifier: string
  occurredAt: string
  direction: AttendanceDevicePunchDirection
  rawMetadata?: string | null
}

export interface AttendanceDeviceIssue {
  id: string
  deviceId?: string | null
  syncRunId?: string | null
  employeeId?: string | null
  attendancePunchId?: string | null
  externalEventId: string
  externalEmployeeIdentifier: string
  occurredAtUtc: string
  receivedAtUtc: string
  direction: AttendanceDevicePunchDirection
  status: AttendanceDeviceIngestionStatus
  sanitizedError?: string | null
}

export interface AttendanceDeviceSyncRun {
  id: string
  deviceId?: string | null
  source: string
  status: AttendanceDeviceSyncStatus
  startedAtUtc: string
  completedAtUtc?: string | null
  received: number
  accepted: number
  duplicate: number
  rejected: number
  unmapped: number
  errorCount: number
  checkpointBefore?: string | null
  checkpointAfter?: string | null
}

export interface AttendanceDeviceAudit {
  id: string
  actorUserId?: string | null
  deviceId?: string | null
  mappingId?: string | null
  employeeId?: string | null
  action: string
  occurredAtUtc: string
  contextJson: string
}

export interface AttendanceDeviceBatchResult {
  syncRunId: string
  received: number
  accepted: number
  duplicate: number
  rejected: number
  unmapped: number
  items: Array<{ externalEventId: string; status: AttendanceDeviceIngestionStatus; message?: string | null }>
  checkpoint?: string | null
}

export interface AttendanceDeviceQuery {
  page?: number
  pageSize?: number
  search?: string
  status?: AttendanceDeviceStatus
}

export interface AttendanceDeviceMappingQuery {
  page?: number
  pageSize?: number
  deviceId?: string
  employeeId?: string
  externalEmployeeIdentifier?: string
}

export interface AttendanceDeviceIssueQuery {
  page?: number
  pageSize?: number
  status?: AttendanceDeviceIngestionStatus
  deviceId?: string
  employeeId?: string
  externalEmployeeIdentifier?: string
  syncRunId?: string
  fromUtc?: string
  toUtc?: string
}

export interface AttendanceDeviceSyncRunQuery {
  page?: number
  pageSize?: number
  deviceId?: string
  status?: AttendanceDeviceSyncStatus
  fromUtc?: string
  toUtc?: string
}

export interface AttendanceDeviceAuditQuery {
  page?: number
  pageSize?: number
  deviceId?: string
  mappingId?: string
}

const endpoint = '/api/attendance/devices'

export function listAttendanceDevices(params: AttendanceDeviceQuery = {}, signal?: AbortSignal): Promise<PagedResult<AttendanceDevice>> {
  return request(() => api.get<ApiResponse<PagedResult<AttendanceDevice>>>(endpoint, { params: cleanParams(params as Record<string, string | number | boolean | undefined>), signal }))
}

export function getAttendanceDevice(id: string, signal?: AbortSignal): Promise<AttendanceDevice> {
  return request(() => api.get<ApiResponse<AttendanceDevice>>(`${endpoint}/${id}`, { signal }))
}

export function createAttendanceDevice(value: AttendanceDeviceRequest): Promise<AttendanceDevice> {
  return request(() => api.post<ApiResponse<AttendanceDevice>>(endpoint, value))
}

export function updateAttendanceDevice(id: string, value: AttendanceDeviceRequest): Promise<AttendanceDevice> {
  return request(() => api.put<ApiResponse<AttendanceDevice>>(`${endpoint}/${id}`, value))
}

export function activateAttendanceDevice(id: string): Promise<AttendanceDevice> {
  return request(() => api.post<ApiResponse<AttendanceDevice>>(`${endpoint}/${id}/activate`))
}

export function deactivateAttendanceDevice(id: string): Promise<AttendanceDevice> {
  return request(() => api.post<ApiResponse<AttendanceDevice>>(`${endpoint}/${id}/deactivate`))
}

export function listAttendanceDeviceMappings(params: AttendanceDeviceMappingQuery = {}, signal?: AbortSignal): Promise<PagedResult<AttendanceDeviceMapping>> {
  return request(() => api.get<ApiResponse<PagedResult<AttendanceDeviceMapping>>>(`${endpoint}/mappings`, { params: cleanParams(params as Record<string, string | number | boolean | undefined>), signal }))
}

export function createAttendanceDeviceMapping(value: AttendanceDeviceMappingRequest): Promise<AttendanceDeviceMapping> {
  return request(() => api.post<ApiResponse<AttendanceDeviceMapping>>(`${endpoint}/mappings`, value))
}

export function deactivateAttendanceDeviceMapping(id: string): Promise<boolean> {
  return request(() => api.post<ApiResponse<boolean>>(`${endpoint}/mappings/${id}/deactivate`))
}

export function listAttendanceDeviceIssues(params: AttendanceDeviceIssueQuery = {}, signal?: AbortSignal): Promise<PagedResult<AttendanceDeviceIssue>> {
  return request(() => api.get<ApiResponse<PagedResult<AttendanceDeviceIssue>>>(`${endpoint}/issues`, { params: cleanParams(params as Record<string, string | number | boolean | undefined>), signal }))
}

export function reprocessAttendanceDeviceIssue(id: string): Promise<AttendanceDeviceBatchResult> {
  return request(() => api.post<ApiResponse<AttendanceDeviceBatchResult>>(`${endpoint}/issues/${id}/reprocess`))
}

export function listAttendanceDeviceSyncRuns(params: AttendanceDeviceSyncRunQuery = {}, signal?: AbortSignal): Promise<PagedResult<AttendanceDeviceSyncRun>> {
  return request(() => api.get<ApiResponse<PagedResult<AttendanceDeviceSyncRun>>>(`${endpoint}/sync-runs`, { params: cleanParams(params as Record<string, string | number | boolean | undefined>), signal }))
}

export function syncAttendanceDevice(id: string): Promise<AttendanceDeviceBatchResult> {
  return request(() => api.post<ApiResponse<AttendanceDeviceBatchResult>>(`${endpoint}/${id}/sync`))
}

export function listAttendanceDeviceAudit(params: AttendanceDeviceAuditQuery = {}, signal?: AbortSignal): Promise<PagedResult<AttendanceDeviceAudit>> {
  return request(() => api.get<ApiResponse<PagedResult<AttendanceDeviceAudit>>>(`${endpoint}/history`, { params: cleanParams(params as Record<string, string | number | boolean | undefined>), signal }))
}
