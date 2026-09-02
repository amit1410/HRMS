import { api, cleanParams, request } from './client.ts'
import type { ApiResponse, MasterLookup, MasterLookupQuery } from './types.ts'

/**
 * Master data lookup endpoints. All return `MasterLookup[]` for dropdown population.
 * Each master is displayed as "{Code} - {Name}" in the UI.
 */

type MasterEndpoint =
  | 'holding-companies'
  | 'lines-of-business'
  | 'organisations'
  | 'departments'
  | 'banks'
  | 'sub-departments'
  | 'sections'
  | 'sub-sections'
  | 'functions'
  | 'sub-functions'
  | 'grades'
  | 'designations'
  | 'employee-types'
  | 'work-locations'
  | 'cost-centers'
  | 'position-change-reasons'

function listMasterData(
  endpoint: MasterEndpoint,
  query: MasterLookupQuery = {},
  signal?: AbortSignal,
): Promise<MasterLookup[]> {
  return request<MasterLookup[]>(() =>
    api.get<ApiResponse<MasterLookup[]>>(`/api/master-data/${endpoint}`, {
      params: cleanParams({ ...query }),
      signal,
    }),
  )
}

export function listHoldingCompanies(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('holding-companies', query, signal)
}

export function listDepartments(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('departments', query, signal)
}

export function listLinesOfBusiness(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('lines-of-business', query, signal)
}

export function listOrganisations(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('organisations', query, signal)
}

export function listBanks(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('banks', query, signal)
}

export function listSubDepartments(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('sub-departments', query, signal)
}

export function listSections(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('sections', query, signal)
}

export function listSubSections(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('sub-sections', query, signal)
}

export function listFunctions(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('functions', query, signal)
}

export function listSubFunctions(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('sub-functions', query, signal)
}

export function listGrades(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('grades', query, signal)
}

export function listEmployeeTypes(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('employee-types', query, signal)
}

export function listWorkLocations(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('work-locations', query, signal)
}

export function listCostCenters(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('cost-centers', query, signal)
}

export function listPositionChangeReasons(query?: MasterLookupQuery, signal?: AbortSignal) {
  return listMasterData('position-change-reasons', query, signal)
}
