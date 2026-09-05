import { useEffect, useMemo, useState } from 'react'
import { listCountries } from '../../api/countries.ts'
import { listDesignations } from '../../api/designations.ts'
import {
  getApplicability,
  listLeaveTypes,
  setApplicability,
  setVersionLeaveTypes,
  type LeaveApplicabilityGroup,
  type LeaveApplicabilityGroupRequest,
  type LeavePolicyVersion,
  type LeaveTypeSelection,
} from '../../api/leaveConfiguration.ts'
import { listHoldingCompanies, listLinesOfBusiness, listOrganisations, listDepartments, listSubDepartments, listSections, listSubSections, listFunctions, listSubFunctions, listGrades, listEmployeeTypes, listWorkLocations, listCostCenters } from '../../api/masterData.ts'
import { ApiError } from '../../api/errors.ts'
import { MasterDropdown } from '../../components/MasterDropdown.tsx'
import { Notice } from '../../components/Notice.tsx'
import { Spinner } from '../../components/Spinner.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'

type Group = Omit<LeaveApplicabilityGroup, 'id'>
type GroupKey = keyof Group
type Lookup = { id: string; code: string; name: string; isActive: boolean }
type Fetcher = (query?: { parentId?: string; isActive?: boolean }, signal?: AbortSignal) => Promise<Lookup[]>

const fetchCache = new Map<string, Promise<Lookup[]>>()
function cached(key: string, fetcher: Fetcher): Fetcher {
  return (query, signal) => {
    const cacheKey = `${key}:${query?.parentId ?? ''}:${query?.isActive ?? ''}`
    const existing = fetchCache.get(cacheKey)
    if (existing) return existing
    const result = fetcher(query, signal)
    fetchCache.set(cacheKey, result)
    return result
  }
}

const lookupFetchers: Record<string, Fetcher> = {
  holdingCompanyId: cached('holding', (query, signal) => listHoldingCompanies(query, signal)),
  lobId: cached('lob', (query, signal) => listLinesOfBusiness(query, signal)),
  organisationId: cached('organisation', (query, signal) => listOrganisations(query, signal)),
  departmentId: cached('department', (query, signal) => listDepartments(query, signal)),
  subDepartmentId: cached('subDepartment', (query, signal) => listSubDepartments(query, signal)),
  sectionId: cached('section', (query, signal) => listSections(query, signal)),
  subSectionId: cached('subSection', (query, signal) => listSubSections(query, signal)),
  functionId: cached('function', (query, signal) => listFunctions(query, signal)),
  subFunctionId: cached('subFunction', (query, signal) => listSubFunctions(query, signal)),
  gradeId: cached('grade', (query, signal) => listGrades(query, signal)),
  designationId: cached('designation', (query, signal) => listDesignations({ pageSize: 100, isActive: query?.isActive, search: undefined }, signal).then(page => page.items)),
  employeeTypeId: cached('employeeType', (query, signal) => listEmployeeTypes(query, signal)),
  countryLocationId: cached('country', (query, signal) => listCountries({ pageSize: 100, isActive: query?.isActive }, signal).then(page => page.items)),
  workLocationId: cached('workLocation', (query, signal) => listWorkLocations(query, signal)),
  costCenterId: cached('costCenter', (query, signal) => listCostCenters(query, signal)),
}

const dimensions: Array<{ key: GroupKey; label: string; parent?: GroupKey }> = [
  { key: 'holdingCompanyId', label: 'Holding Company' },
  { key: 'lobId', label: 'LOB', parent: 'holdingCompanyId' },
  { key: 'organisationId', label: 'Organization' },
  { key: 'departmentId', label: 'Department' },
  { key: 'subDepartmentId', label: 'Sub Department', parent: 'departmentId' },
  { key: 'sectionId', label: 'Section', parent: 'subDepartmentId' },
  { key: 'subSectionId', label: 'Sub Section', parent: 'sectionId' },
  { key: 'functionId', label: 'Function' },
  { key: 'subFunctionId', label: 'Sub Function', parent: 'functionId' },
  { key: 'gradeId', label: 'Grade' },
  { key: 'designationId', label: 'Designation' },
  { key: 'employeeTypeId', label: 'Employee Type' },
  { key: 'countryLocationId', label: 'Country' },
  { key: 'workLocationId', label: 'Work Location' },
  { key: 'costCenterId', label: 'Cost Center' },
]

function blankGroup(): Group { return Object.fromEntries(dimensions.map(dimension => [dimension.key, null])) as Group }
function toGroup(group: LeaveApplicabilityGroup): Group { const next = blankGroup(); for (const dimension of dimensions) next[dimension.key] = group[dimension.key] ?? null; return next }
function payload(groups: Group[]): LeaveApplicabilityGroupRequest[] { return groups.filter(group => dimensions.some(dimension => group[dimension.key])).map(group => ({ ...group })) }
function groupSignature(group: Group): string { return JSON.stringify(payload([group])[0] ?? {}) }

interface Props { policyId: string; version: LeavePolicyVersion; selectedLeaveTypes: LeaveTypeSelection[]; canManage: boolean; onNotice: (message: string) => void; onChanged?: () => void }

export function LeavePolicyConfigurationSections({ policyId, version, selectedLeaveTypes, canManage, onNotice, onChanged }: Props) {
  const editable = canManage && version.status === 'Draft'
  const leaveTypesQuery = useApiQuery(signal => listLeaveTypes({ pageSize: 100, isActive: false }, signal), [])
  const [leaveTypeIds, setLeaveTypeIds] = useState(() => selectedLeaveTypes.map(item => item.id))
  const [leaveTypeSearch, setLeaveTypeSearch] = useState('')
  const [savingTypes, setSavingTypes] = useState(false)
  const [typeError, setTypeError] = useState<ApiError | null>(null)
  const [groups, setGroups] = useState<Group[]>([])
  const [groupsLoaded, setGroupsLoaded] = useState(false)
  const [groupError, setGroupError] = useState<ApiError | null>(null)
  const [savingGroups, setSavingGroups] = useState(false)

  useEffect(() => {
    setLeaveTypeIds(selectedLeaveTypes.map(item => item.id))
  }, [selectedLeaveTypes])
  useEffect(() => {
    setGroupsLoaded(false)
    setGroupError(null)
    void getApplicability(policyId, version.id).then(items => { setGroups(items.map(toGroup)); setGroupsLoaded(true) }).catch(error => { setGroupError(error instanceof ApiError ? error : new ApiError('Unable to load applicability.')); setGroupsLoaded(true) })
  }, [policyId, version.id])

  const availableTypes = useMemo(() => {
    const items = leaveTypesQuery.data?.items ?? []
    return items.filter(item => item.isActive || leaveTypeIds.includes(item.id)).filter(item => `${item.code} ${item.name}`.toLowerCase().includes(leaveTypeSearch.toLowerCase()))
  }, [leaveTypesQuery.data, leaveTypeIds, leaveTypeSearch])

  async function saveTypes() {
    setSavingTypes(true); setTypeError(null)
    try { await setVersionLeaveTypes(policyId, version.id, { leaveTypeIds, concurrencyToken: version.concurrencyToken }); onChanged?.(); onNotice('Leave Types saved.') } catch (error) { setTypeError(error instanceof ApiError ? error : new ApiError('Unable to save Leave Types.')) } finally { setSavingTypes(false) }
  }
  function updateGroup(index: number, key: GroupKey, value: string) {
    onChanged?.()
    setGroups(current => current.map((group, groupIndex) => {
      if (groupIndex !== index) return group
      const next = { ...group, [key]: value || null }
      const descendants = new Set<GroupKey>()
      const pending = [key]
      while (pending.length > 0) {
        const parent = pending.shift()
        if (!parent) continue
        for (const dimension of dimensions) {
          if (dimension.parent === parent && !descendants.has(dimension.key)) {
            descendants.add(dimension.key)
            pending.push(dimension.key)
          }
        }
      }
      for (const child of descendants) next[child] = null
      return next
    }))
  }
  async function saveGroups() {
    const intended = payload(groups)
    if (new Set(intended.map(group => groupSignature(group as Group))).size !== intended.length) { setGroupError(new ApiError('Duplicate applicability groups are not allowed.')); return }
    setSavingGroups(true); setGroupError(null)
    try { await setApplicability(policyId, version.id, { groups: intended, concurrencyToken: version.concurrencyToken }); onChanged?.(); onNotice('Applicability saved.') } catch (error) { setGroupError(error instanceof ApiError ? error : new ApiError('Unable to save applicability.')) } finally { setSavingGroups(false) }
  }

  return <>
    <section className="policy-configuration-section" aria-labelledby="leave-types-heading">
      <div className="section-heading"><div><h3 id="leave-types-heading">3. Leave Types</h3><p>Select the Leave Types governed by this Policy version.</p></div>{editable ? <button className="button button-primary" type="button" onClick={() => void saveTypes()} disabled={savingTypes}>{savingTypes ? <Spinner size={14} label="Saving…" /> : 'Save Leave Types'}</button> : null}</div>
      {typeError ? <Notice tone="error">{typeError.message}{typeError.isConflict ? ' Reload the latest version before saving.' : ''}</Notice> : null}
      <label className="field"><span>Search Leave Types</span><input className="input" type="search" value={leaveTypeSearch} onChange={event => setLeaveTypeSearch(event.target.value)} placeholder="Search by code or name" /></label>
      {leaveTypesQuery.isLoading ? <Spinner label="Loading Leave Types" /> : <div className="leave-type-selection" role="group" aria-label="Leave Type selection">{availableTypes.map(item => <label className="leave-type-option" key={item.id}><input type="checkbox" checked={leaveTypeIds.includes(item.id)} disabled={!editable || !item.isActive} onChange={event => { onChanged?.(); setLeaveTypeIds(current => event.target.checked ? [...current, item.id] : current.filter(id => id !== item.id)) }} /><span><strong>{item.code} — {item.name}</strong><small>{item.isActive ? 'Active' : 'Inactive historical reference'}</small></span></label>)}{availableTypes.length === 0 ? <p className="muted">No active Leave Types match this search.</p> : null}</div>}
    </section>
    <section className="policy-configuration-section" aria-labelledby="applicability-heading">
      <div className="section-heading"><div><h3 id="applicability-heading">4. Applicability</h3><p>Within a group, <strong>ALL</strong> selected conditions must match. Across groups, <strong>OR</strong> applies.</p></div>{editable ? <button className="button button-secondary" type="button" onClick={() => { onChanged?.(); setGroups(current => [...current, blankGroup()]) }}>+ Add Applicability Group</button> : null}</div>
      {groupError ? <Notice tone="error">{groupError.message}{groupError.isConflict ? ' Reload the latest version before saving.' : ''}</Notice> : null}
      {!groupsLoaded ? <Spinner label="Loading applicability" /> : groups.length === 0 ? <div className="tenant-wide-notice"><strong>Tenant-wide applicability</strong><span>This Policy version applies to all employees in the tenant, subject to later eligibility rules.</span></div> : <div className="applicability-groups">{groups.map((group, index) => <div className="applicability-group" key={index}><div className="section-heading"><div><h4>Applicability Group {index + 1}</h4><p>ALL conditions in this group must match.</p></div>{editable ? <button className="button button-link" type="button" onClick={() => { onChanged?.(); setGroups(current => current.filter((_, groupIndex) => groupIndex !== index)) }} aria-label={`Remove Applicability Group ${index + 1}`}>Remove group</button> : null}</div><div className="applicability-grid">{dimensions.map(dimension => <MasterDropdown key={dimension.key} id={`applicability-${index}-${dimension.key}`} label={dimension.label} value={group[dimension.key] ?? ''} onChange={value => updateGroup(index, dimension.key, value)} fetcher={lookupFetchers[dimension.key] as Fetcher} parentId={dimension.parent ? group[dimension.parent] ?? undefined : undefined} disabled={!editable || Boolean(dimension.parent && !group[dimension.parent])} includeInactive valueLabel={group[dimension.key] ? 'Saved selection' : undefined} />)}</div></div>)}<div className="applicability-or" role="separator" aria-label="OR between applicability groups">OR</div></div>}
      {editable ? <><p className="field-help">Saving with zero groups makes this version tenant-wide. Empty groups are not persisted.</p><div className="form-actions"><button className="button button-primary" type="button" onClick={() => void saveGroups()} disabled={savingGroups}>{savingGroups ? <Spinner size={14} label="Saving…" /> : 'Save Applicability'}</button></div></> : null}
    </section>
  </>
}
