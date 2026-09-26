import { useState } from 'react'
import { listEmployees } from '../../api/employees.ts'
import { listWorkLocations } from '../../api/masterData.ts'
import {
  activateAttendanceDevice,
  createAttendanceDevice,
  createAttendanceDeviceMapping,
  deactivateAttendanceDevice,
  deactivateAttendanceDeviceMapping,
  listAttendanceDeviceAudit,
  listAttendanceDeviceIssues,
  listAttendanceDeviceMappings,
  listAttendanceDevices,
  listAttendanceDeviceSyncRuns,
  reprocessAttendanceDeviceIssue,
  syncAttendanceDevice,
  updateAttendanceDevice,
  type AttendanceDevice,
  type AttendanceDeviceAudit,
  type AttendanceDeviceConnectionMode,
  type AttendanceDeviceIngestionStatus,
  type AttendanceDeviceIssue,
  type AttendanceDeviceMapping,
  type AttendanceDeviceSyncRun,
  type AttendanceDeviceSyncStatus,
  type AttendanceDeviceRequest,
} from '../../api/attendanceDevices.ts'
import { Permissions } from '../../auth/permissions.ts'
import { useAuth } from '../../auth/useAuth.ts'
import { Badge, type BadgeTone } from '../../components/Badge.tsx'
import { Card } from '../../components/Card.tsx'
import { DataTable, type Column } from '../../components/DataTable.tsx'
import { Notice } from '../../components/Notice.tsx'
import { PageHeader } from '../../components/PageHeader.tsx'
import { Pagination } from '../../components/Pagination.tsx'
import { useApiQuery } from '../../hooks/useApiQuery.ts'
import { useDocumentTitle } from '../../hooks/useDocumentTitle.ts'

type DeviceTab = 'devices' | 'mappings' | 'issues' | 'sync-runs' | 'history'
type MappingFormValue = { deviceId: string; externalEmployeeIdentifier: string; employeeId: string; effectiveFrom: string; effectiveTo: string; status: 'Active' | 'Inactive' }

const CONNECTION_MODES: AttendanceDeviceConnectionMode[] = ['Push', 'Pull', 'FileImport', 'ManualApi']
const ISSUE_STATUSES: AttendanceDeviceIngestionStatus[] = ['Accepted', 'Duplicate', 'Rejected', 'Unmapped', 'RequiresPeriodReopen']
const SYNC_STATUSES: AttendanceDeviceSyncStatus[] = ['Running', 'Succeeded', 'PartiallySucceeded', 'Failed']

const initialDevice: AttendanceDeviceRequest = {
  code: '', name: '', deviceType: '', vendor: '', serialNumber: '', workLocationId: '',
  timeZoneId: 'Asia/Kolkata', connectionMode: 'FileImport', credentialReference: '',
}

export function AttendanceDevicesPage() {
  const { can } = useAuth()
  const [tab, setTab] = useState<DeviceTab>('devices')
  const [message, setMessage] = useState<string>()
  const [error, setError] = useState<string>()
  const [devicePage, setDevicePage] = useState(1)
  const [devicePageSize, setDevicePageSize] = useState(20)
  const [deviceFilters, setDeviceFilters] = useState({ search: '', status: '' })
  const [appliedDeviceFilters, setAppliedDeviceFilters] = useState(deviceFilters)
  const [editingDevice, setEditingDevice] = useState<AttendanceDevice | null>(null)
  const [showDeviceForm, setShowDeviceForm] = useState(false)
  const [deviceForm, setDeviceForm] = useState<AttendanceDeviceRequest>(initialDevice)
  const [mappingPage, setMappingPage] = useState(1)
  const [mappingPageSize, setMappingPageSize] = useState(20)
  const [mappingFilters, setMappingFilters] = useState({ deviceId: '', externalEmployeeIdentifier: '' })
  const [appliedMappingFilters, setAppliedMappingFilters] = useState(mappingFilters)
  const [showMappingForm, setShowMappingForm] = useState(false)
  const [mappingForm, setMappingForm] = useState({ deviceId: '', externalEmployeeIdentifier: '', employeeId: '', effectiveFrom: '', effectiveTo: '', status: 'Active' as 'Active' | 'Inactive' })
  const [issuePage, setIssuePage] = useState(1)
  const [issuePageSize, setIssuePageSize] = useState(20)
  const [issueFilters, setIssueFilters] = useState({ status: '', deviceId: '', externalEmployeeIdentifier: '', fromUtc: '', toUtc: '' })
  const [appliedIssueFilters, setAppliedIssueFilters] = useState(issueFilters)
  const [syncPage, setSyncPage] = useState(1)
  const [syncPageSize, setSyncPageSize] = useState(20)
  const [syncFilters, setSyncFilters] = useState({ deviceId: '', status: '' })
  const [appliedSyncFilters, setAppliedSyncFilters] = useState(syncFilters)
  const [historyPage, setHistoryPage] = useState(1)
  const [historyPageSize, setHistoryPageSize] = useState(20)
  const [historyDeviceId, setHistoryDeviceId] = useState('')
  const [historyMappingId, setHistoryMappingId] = useState('')
  const [reprocessMessage, setReprocessMessage] = useState<Record<string, string>>({})

  useDocumentTitle('Attendance Device Integration')

  const devices = useApiQuery(
    (signal) => listAttendanceDevices({ page: devicePage, pageSize: devicePageSize, search: appliedDeviceFilters.search || undefined, status: (appliedDeviceFilters.status || undefined) as AttendanceDevice['status'] | undefined }, signal),
    [devicePage, devicePageSize, JSON.stringify(appliedDeviceFilters)],
  )
  const allDevices = useApiQuery((signal) => listAttendanceDevices({ page: 1, pageSize: 100 }, signal), [])
  const employees = useApiQuery((signal) => listEmployees({ page: 1, pageSize: 100, status: 'Active' }, signal), [])
  const locations = useApiQuery((signal) => listWorkLocations({ isActive: true }, signal), [])
  const mappings = useApiQuery(
    (signal) => listAttendanceDeviceMappings({ page: mappingPage, pageSize: mappingPageSize, deviceId: appliedMappingFilters.deviceId || undefined, externalEmployeeIdentifier: appliedMappingFilters.externalEmployeeIdentifier || undefined }, signal),
    [mappingPage, mappingPageSize, JSON.stringify(appliedMappingFilters)],
  )
  const issues = useApiQuery(
    (signal) => listAttendanceDeviceIssues({ page: issuePage, pageSize: issuePageSize, status: (appliedIssueFilters.status || undefined) as AttendanceDeviceIngestionStatus | undefined, deviceId: appliedIssueFilters.deviceId || undefined, externalEmployeeIdentifier: appliedIssueFilters.externalEmployeeIdentifier || undefined, fromUtc: appliedIssueFilters.fromUtc || undefined, toUtc: appliedIssueFilters.toUtc || undefined }, signal),
    [issuePage, issuePageSize, JSON.stringify(appliedIssueFilters)],
  )
  const syncRuns = useApiQuery(
    (signal) => listAttendanceDeviceSyncRuns({ page: syncPage, pageSize: syncPageSize, deviceId: appliedSyncFilters.deviceId || undefined, status: (appliedSyncFilters.status || undefined) as AttendanceDeviceSyncStatus | undefined }, signal),
    [syncPage, syncPageSize, JSON.stringify(appliedSyncFilters)],
  )
  const history = useApiQuery(
    (signal) => listAttendanceDeviceAudit({ page: historyPage, pageSize: historyPageSize, deviceId: historyDeviceId || undefined, mappingId: historyMappingId || undefined }, signal),
    [historyPage, historyPageSize, historyDeviceId, historyMappingId],
  )

  function notifySuccess(value: string): void { setError(undefined); setMessage(value) }
  function notifyError(value: unknown, fallback: string): void { setMessage(undefined); setError(value instanceof Error ? value.message : fallback) }
  function resetDeviceForm(): void { setDeviceForm({ ...initialDevice }); setEditingDevice(null); setShowDeviceForm(false) }
  function beginCreateDevice(): void { setEditingDevice(null); setDeviceForm({ ...initialDevice }); setShowDeviceForm(true) }
  function beginEditDevice(device: AttendanceDevice): void {
    setEditingDevice(device)
    setDeviceForm({ code: device.code, name: device.name, deviceType: device.deviceType, vendor: device.vendor ?? '', serialNumber: device.serialNumber ?? '', workLocationId: device.workLocationId ?? '', timeZoneId: device.timeZoneId, connectionMode: device.connectionMode, credentialReference: '' })
    setShowDeviceForm(true)
  }
  async function saveDevice(): Promise<void> {
    try {
      if (editingDevice) await updateAttendanceDevice(editingDevice.id, deviceForm)
      else await createAttendanceDevice(deviceForm)
      resetDeviceForm(); notifySuccess(editingDevice ? 'Device updated.' : 'Device created.'); devices.refetch(); allDevices.refetch()
    } catch (caught) { notifyError(caught, 'Device could not be saved.') }
  }
  async function changeDeviceStatus(device: AttendanceDevice, action: 'activate' | 'deactivate'): Promise<void> {
    if (!window.confirm(`${action === 'activate' ? 'Activate' : 'Deactivate'} ${device.name}?`)) return
    try {
      if (action === 'activate') await activateAttendanceDevice(device.id)
      else await deactivateAttendanceDevice(device.id)
      notifySuccess(`Device ${action}d.`); devices.refetch(); allDevices.refetch()
    }
    catch (caught) { notifyError(caught, 'Device status could not be changed.') }
  }
  async function syncNow(device: AttendanceDevice): Promise<void> {
    try { const result = await syncAttendanceDevice(device.id); notifySuccess(`Sync completed: ${result.accepted} accepted, ${result.duplicate} duplicate, ${result.unmapped} unmapped, ${result.rejected} rejected.`); devices.refetch(); allDevices.refetch(); syncRuns.refetch(); issues.refetch() }
    catch (caught) { notifyError(caught, 'Device sync could not be started.') }
  }
  async function saveMapping(): Promise<void> {
    try {
      await createAttendanceDeviceMapping({ deviceId: mappingForm.deviceId, externalEmployeeIdentifier: mappingForm.externalEmployeeIdentifier.trim(), employeeId: mappingForm.employeeId, effectiveFrom: mappingForm.effectiveFrom, effectiveTo: mappingForm.effectiveTo || null, status: mappingForm.status })
      setShowMappingForm(false); setMappingForm({ deviceId: '', externalEmployeeIdentifier: '', employeeId: '', effectiveFrom: '', effectiveTo: '', status: 'Active' }); notifySuccess('Employee mapping created.'); mappings.refetch()
    } catch (caught) { notifyError(caught, 'Mapping could not be created.') }
  }
  async function deactivateMapping(mapping: AttendanceDeviceMapping): Promise<void> {
    if (!window.confirm(`Deactivate mapping ${mapping.externalEmployeeIdentifier}?`)) return
    try { await deactivateAttendanceDeviceMapping(mapping.id); notifySuccess('Employee mapping deactivated.'); mappings.refetch(); history.refetch() }
    catch (caught) { notifyError(caught, 'Mapping could not be deactivated.') }
  }
  async function reprocessIssue(issue: AttendanceDeviceIssue): Promise<void> {
    try {
      const result = await reprocessAttendanceDeviceIssue(issue.id)
      const item = result.items[0]
      const text = item?.status === 'RequiresPeriodReopen' ? 'Finalized Attendance is protected. Reopen the period through the Attendance workflow before retrying.' : item?.message ?? 'Issue reprocessed.'
      setReprocessMessage(current => ({ ...current, [issue.id]: text })); issues.refetch(); syncRuns.refetch()
    } catch (caught) { setReprocessMessage(current => ({ ...current, [issue.id]: caught instanceof Error ? caught.message : 'Issue could not be reprocessed.' })) }
  }

  const deviceColumns: readonly Column<AttendanceDevice>[] = [
    { key: 'code', header: 'Device', sortBy: undefined, render: row => <><strong>{row.code}</strong><small className="table-secondary-text">{row.name}</small></> },
    { key: 'vendor', header: 'Vendor / type', render: row => <>{row.vendor || '—'}<small className="table-secondary-text">{row.deviceType}</small></> },
    { key: 'connection', header: 'Connection', render: row => row.connectionMode },
    { key: 'location', header: 'Work location', secondary: true, render: row => locations.data?.find(location => location.id === row.workLocationId)?.name ?? row.workLocationId ?? '—' },
    { key: 'timezone', header: 'Timezone', secondary: true, render: row => row.timeZoneId },
    { key: 'status', header: 'Status', render: row => <StatusBadge status={row.status} /> },
    { key: 'sync', header: 'Last sync', secondary: true, render: row => <>{formatDateTime(row.lastSuccessfulSyncAtUtc) }<small className="table-secondary-text">Attempted {formatDateTime(row.lastAttemptedSyncAtUtc)}</small></> },
    { key: 'actions', header: 'Actions', render: row => <div className="table-actions">
      {can(Permissions.attendance.deviceManage) ? <button className="button button-link" type="button" onClick={() => beginEditDevice(row)}>Edit</button> : null}
      {can(Permissions.attendance.deviceManage) && row.status === 'Inactive' ? <button className="button button-link" type="button" onClick={() => void changeDeviceStatus(row, 'activate')}>Activate</button> : null}
      {can(Permissions.attendance.deviceManage) && row.status === 'Active' ? <button className="button button-link" type="button" onClick={() => void changeDeviceStatus(row, 'deactivate')}>Deactivate</button> : null}
      {can(Permissions.attendance.deviceMapping) ? <button className="button button-link" type="button" onClick={() => { setTab('mappings'); setMappingFilters(current => ({ ...current, deviceId: row.id })); setAppliedMappingFilters(current => ({ ...current, deviceId: row.id })) }}>Mappings</button> : null}
      {can(Permissions.attendance.deviceSync) && row.status === 'Active' && row.connectionMode === 'Pull' ? <button className="button button-link" type="button" onClick={() => void syncNow(row)}>Sync now</button> : null}
      {can(Permissions.attendance.deviceViewHistory) ? <button className="button button-link" type="button" onClick={() => { setTab('history'); setHistoryDeviceId(row.id); setHistoryPage(1) }}>History</button> : null}
    </div> },
  ]

  const mappingColumns: readonly Column<AttendanceDeviceMapping>[] = [
    { key: 'device', header: 'Device', render: row => allDevices.data?.items.find(device => device.id === row.deviceId)?.code ?? row.deviceId },
    { key: 'external', header: 'External identifier', render: row => row.externalEmployeeIdentifier },
    { key: 'employee', header: 'Employee', render: row => <>{row.employeeCode}<small className="table-secondary-text">{employees.data?.items.find(employee => employee.id === row.employeeId)?.fullName ?? row.employeeId}</small></> },
    { key: 'effective', header: 'Effective period', render: row => `${row.effectiveFrom} → ${row.effectiveTo ?? 'Open'}` },
    { key: 'status', header: 'Status', render: row => <StatusBadge status={row.status} /> },
    { key: 'actions', header: 'Actions', render: row => can(Permissions.attendance.deviceMapping) && row.status === 'Active' ? <button className="button button-link" type="button" onClick={() => void deactivateMapping(row)}>Deactivate</button> : <span className="field-help">Identity immutable</span> },
  ]

  const issueColumns: readonly Column<AttendanceDeviceIssue>[] = [
    { key: 'received', header: 'Received', render: row => formatDateTime(row.receivedAtUtc) },
    { key: 'device', header: 'Device', render: row => allDevices.data?.items.find(device => device.id === row.deviceId)?.code ?? row.deviceId ?? '—' },
    { key: 'external', header: 'External employee', render: row => row.externalEmployeeIdentifier },
    { key: 'occurred', header: 'Original timestamp', render: row => formatDateTime(row.occurredAtUtc) },
    { key: 'direction', header: 'Direction', render: row => row.direction },
    { key: 'status', header: 'Status', render: row => <StatusBadge status={row.status} /> },
    { key: 'reason', header: 'Reason', secondary: true, render: row => row.sanitizedError ?? '—' },
    { key: 'action', header: 'Action', render: row => row.status === 'Unmapped' || row.status === 'RequiresPeriodReopen' ? <><button className="button button-link" type="button" onClick={() => void reprocessIssue(row)}>Reprocess</button>{reprocessMessage[row.id] ? <small className="table-secondary-text">{reprocessMessage[row.id]}</small> : null}</> : <span className="field-help">No action</span> },
  ]

  const syncColumns: readonly Column<AttendanceDeviceSyncRun>[] = [
    { key: 'device', header: 'Device', render: row => allDevices.data?.items.find(device => device.id === row.deviceId)?.code ?? row.deviceId ?? '—' },
    { key: 'source', header: 'Source', render: row => row.source },
    { key: 'started', header: 'Started / completed', render: row => <>{formatDateTime(row.startedAtUtc)}<small className="table-secondary-text">{formatDateTime(row.completedAtUtc)}</small></> },
    { key: 'status', header: 'Status', render: row => <StatusBadge status={row.status} /> },
    { key: 'counts', header: 'Results', render: row => `Received ${row.received} · Accepted ${row.accepted} · Duplicate ${row.duplicate} · Unmapped ${row.unmapped} · Errors ${row.errorCount}` },
    { key: 'checkpoint', header: 'Checkpoint', secondary: true, render: row => <>{row.checkpointAfter ?? '—'}<small className="table-secondary-text">Before {row.checkpointBefore ?? '—'}</small></> },
  ]

  const historyColumns: readonly Column<AttendanceDeviceAudit>[] = [
    { key: 'occurred', header: 'Timestamp', render: row => formatDateTime(row.occurredAtUtc) },
    { key: 'action', header: 'Action', render: row => row.action },
    { key: 'actor', header: 'Actor', render: row => row.actorUserId ?? 'System / unlinked' },
    { key: 'entity', header: 'Entity', render: row => row.deviceId ?? row.mappingId ?? row.employeeId ?? '—' },
    { key: 'context', header: 'Safe summary', render: row => <code className="audit-context">{safeContext(row.contextJson)}</code> },
  ]

  return <section className="page-shell attendance-device-page">
    <PageHeader title="Attendance Device Integration" subtitle="Operate biometric and device feeds while AttendancePunch remains the authoritative raw punch record." actions={can(Permissions.attendance.deviceManage) ? <button className="button button-primary" type="button" onClick={beginCreateDevice}>Add device</button> : undefined} />
    {message ? <Notice tone="success" onDismiss={() => setMessage(undefined)}>{message}</Notice> : null}
    {error ? <Notice tone="error" onDismiss={() => setError(undefined)}>{error}</Notice> : null}
    <div className="tabs" role="tablist" aria-label="Device integration areas">
      {(['devices', 'mappings', 'issues', 'sync-runs', 'history'] as DeviceTab[]).map(value => <button key={value} className={tab === value ? 'tab is-active' : 'tab'} type="button" role="tab" aria-selected={tab === value} onClick={() => setTab(value)}>{tabLabel(value)}</button>)}
    </div>

    {tab === 'devices' ? <>
      <Card title="Devices" subtitle={devices.data ? `${devices.data.totalCount} configured device(s)` : 'Server-side search and paging'}>
        <div className="form-grid device-filter-grid"><label className="field"><span>Search</span><input className="input" aria-label="Device search" value={deviceFilters.search} onChange={event => setDeviceFilters(current => ({ ...current, search: event.target.value }))} /></label><label className="field"><span>Status</span><select className="input" value={deviceFilters.status} onChange={event => setDeviceFilters(current => ({ ...current, status: event.target.value }))}><option value="">All statuses</option><option>Active</option><option>Inactive</option><option>Disabled</option></select></label><div className="form-actions"><button className="button button-secondary" type="button" onClick={() => { setDevicePage(1); setAppliedDeviceFilters(deviceFilters) }}>Apply filters</button></div></div>
        <DataTable columns={deviceColumns} rows={devices.data?.items ?? null} rowKey={row => row.id} caption="Attendance devices" isLoading={devices.isLoading} error={devices.error} onRetry={devices.refetch} emptyTitle="No attendance devices" emptyMessage="Add a device to begin receiving biometric events." />
        {devices.data ? <Pagination info={devices.data} onPageChange={setDevicePage} onPageSizeChange={size => { setDevicePage(1); setDevicePageSize(size) }} disabled={devices.isRefreshing} /> : null}
      </Card>
      {showDeviceForm ? <DeviceForm value={deviceForm} editing={Boolean(editingDevice)} locations={locations.data ?? []} onChange={setDeviceForm} onSave={() => void saveDevice()} onCancel={resetDeviceForm} /> : null}
    </> : null}

    {tab === 'mappings' ? <>
      <Card title="Employee mappings" subtitle="External identity and device are immutable after creation. Effective dates and status are enforced by the backend." actions={can(Permissions.attendance.deviceMapping) ? <button className="button button-primary" type="button" onClick={() => setShowMappingForm(true)}>Create mapping</button> : undefined}>
        <div className="form-grid"><label className="field"><span>Device</span><select className="input" aria-label="Mapping device filter" value={mappingFilters.deviceId} onChange={event => setMappingFilters(current => ({ ...current, deviceId: event.target.value }))}><option value="">All devices</option>{allDevices.data?.items.map(device => <option key={device.id} value={device.id}>{device.code} — {device.name}</option>)}</select></label><label className="field"><span>External employee identifier</span><input className="input" value={mappingFilters.externalEmployeeIdentifier} onChange={event => setMappingFilters(current => ({ ...current, externalEmployeeIdentifier: event.target.value }))} /></label><div className="form-actions"><button className="button button-secondary" type="button" onClick={() => { setMappingPage(1); setAppliedMappingFilters(mappingFilters) }}>Apply filters</button></div></div>
        <DataTable columns={mappingColumns} rows={mappings.data?.items ?? null} rowKey={row => row.id} caption="Employee device mappings" isLoading={mappings.isLoading} error={mappings.error} onRetry={mappings.refetch} emptyTitle="No mappings" emptyMessage="Create a mapping after the device and employee are available." />
        {mappings.data ? <Pagination info={mappings.data} onPageChange={setMappingPage} onPageSizeChange={size => { setMappingPage(1); setMappingPageSize(size) }} disabled={mappings.isRefreshing} /> : null}
      </Card>
      {showMappingForm ? <MappingForm value={mappingForm} devices={allDevices.data?.items ?? []} employees={employees.data?.items ?? []} onChange={setMappingForm} onSave={() => void saveMapping()} onCancel={() => setShowMappingForm(false)} /> : null}
    </> : null}

    {tab === 'issues' ? <Card title="Ingestion issues" subtitle="Retained events are server-filtered; reprocessing never reopens finalized Attendance automatically.">
      <div className="form-grid"><label className="field"><span>Status</span><select className="input" value={issueFilters.status} onChange={event => setIssueFilters(current => ({ ...current, status: event.target.value }))}><option value="">All statuses</option>{ISSUE_STATUSES.map(status => <option key={status}>{status}</option>)}</select></label><label className="field"><span>Device</span><select className="input" value={issueFilters.deviceId} onChange={event => setIssueFilters(current => ({ ...current, deviceId: event.target.value }))}><option value="">All devices</option>{allDevices.data?.items.map(device => <option key={device.id} value={device.id}>{device.code}</option>)}</select></label><label className="field"><span>External employee identifier</span><input className="input" value={issueFilters.externalEmployeeIdentifier} onChange={event => setIssueFilters(current => ({ ...current, externalEmployeeIdentifier: event.target.value }))} /></label><label className="field"><span>Received from</span><input className="input" type="datetime-local" value={issueFilters.fromUtc} onChange={event => setIssueFilters(current => ({ ...current, fromUtc: event.target.value }))} /></label><label className="field"><span>Received to</span><input className="input" type="datetime-local" value={issueFilters.toUtc} onChange={event => setIssueFilters(current => ({ ...current, toUtc: event.target.value }))} /></label><div className="form-actions"><button className="button button-secondary" type="button" onClick={() => { setIssuePage(1); setAppliedIssueFilters(issueFilters) }}>Apply filters</button></div></div>
      <DataTable columns={issueColumns} rows={issues.data?.items ?? null} rowKey={row => row.id} caption="Attendance device ingestion issues" isLoading={issues.isLoading} error={issues.error} onRetry={issues.refetch} emptyTitle="No ingestion issues" emptyMessage="No retained device issues match the selected filters." />
      {issues.data ? <Pagination info={issues.data} onPageChange={setIssuePage} onPageSizeChange={size => { setIssuePage(1); setIssuePageSize(size) }} disabled={issues.isRefreshing} /> : null}
    </Card> : null}

    {tab === 'sync-runs' ? <Card title="Sync run history" subtitle="Provider results and checkpoints are read from the server.">
      <div className="form-grid"><label className="field"><span>Device</span><select className="input" value={syncFilters.deviceId} onChange={event => setSyncFilters(current => ({ ...current, deviceId: event.target.value }))}><option value="">All devices</option>{allDevices.data?.items.map(device => <option key={device.id} value={device.id}>{device.code}</option>)}</select></label><label className="field"><span>Status</span><select className="input" value={syncFilters.status} onChange={event => setSyncFilters(current => ({ ...current, status: event.target.value }))}><option value="">All statuses</option>{SYNC_STATUSES.map(status => <option key={status}>{status}</option>)}</select></label><div className="form-actions"><button className="button button-secondary" type="button" onClick={() => { setSyncPage(1); setAppliedSyncFilters(syncFilters) }}>Apply filters</button></div></div>
      <DataTable columns={syncColumns} rows={syncRuns.data?.items ?? null} rowKey={row => row.id} caption="Attendance device sync runs" isLoading={syncRuns.isLoading} error={syncRuns.error} onRetry={syncRuns.refetch} emptyTitle="No sync runs" emptyMessage="A sync run will appear here after a provider or manual import operation." />
      {syncRuns.data ? <Pagination info={syncRuns.data} onPageChange={setSyncPage} onPageSizeChange={size => { setSyncPage(1); setSyncPageSize(size) }} disabled={syncRuns.isRefreshing} /> : null}
    </Card> : null}

    {tab === 'history' ? <Card title="Device administration history" subtitle="Read-only, tenant-scoped audit history. Secret values are never displayed.">
      <div className="form-grid"><label className="field"><span>Device ID</span><input className="input" value={historyDeviceId} onChange={event => { setHistoryPage(1); setHistoryDeviceId(event.target.value) }} /></label><label className="field"><span>Mapping ID</span><input className="input" value={historyMappingId} onChange={event => { setHistoryPage(1); setHistoryMappingId(event.target.value) }} /></label></div>
      <DataTable columns={historyColumns} rows={history.data?.items ?? null} rowKey={row => row.id} caption="Attendance device administration history" isLoading={history.isLoading} error={history.error} onRetry={history.refetch} emptyTitle="No administration history" emptyMessage="Successful device and mapping changes will appear here." />
      {history.data ? <Pagination info={history.data} onPageChange={setHistoryPage} onPageSizeChange={size => { setHistoryPage(1); setHistoryPageSize(size) }} disabled={history.isRefreshing} /> : null}
    </Card> : null}
  </section>
}

function DeviceForm({ value, editing, locations, onChange, onSave, onCancel }: { value: AttendanceDeviceRequest; editing: boolean; locations: Array<{ id: string; code: string; name: string }>; onChange: (value: AttendanceDeviceRequest) => void; onSave: () => void; onCancel: () => void }) {
  const update = <K extends keyof AttendanceDeviceRequest>(key: K, next: AttendanceDeviceRequest[K]) => onChange({ ...value, [key]: next })
  return <Card title={editing ? 'Edit attendance device' : 'Add attendance device'} subtitle="Credential reference is optional safe metadata and is never returned by the API."><div className="form-grid"><label className="field"><span>Device code</span><input className="input" value={value.code} onChange={event => update('code', event.target.value)} required /></label><label className="field"><span>Name</span><input className="input" value={value.name} onChange={event => update('name', event.target.value)} required /></label><label className="field"><span>Vendor / provider key</span><input className="input" value={value.vendor ?? ''} onChange={event => update('vendor', event.target.value)} /></label><label className="field"><span>Device type</span><input className="input" value={value.deviceType} onChange={event => update('deviceType', event.target.value)} required /></label><label className="field"><span>Serial number</span><input className="input" value={value.serialNumber ?? ''} onChange={event => update('serialNumber', event.target.value)} /></label><label className="field"><span>Work location</span><select className="input" value={value.workLocationId ?? ''} onChange={event => update('workLocationId', event.target.value || null)}><option value="">Not specified</option>{locations.map(location => <option key={location.id} value={location.id}>{location.code} — {location.name}</option>)}</select></label><label className="field"><span>Timezone</span><input className="input" value={value.timeZoneId} onChange={event => update('timeZoneId', event.target.value)} required /></label><label className="field"><span>Connection mode</span><select className="input" value={value.connectionMode} onChange={event => update('connectionMode', event.target.value as AttendanceDeviceConnectionMode)}>{CONNECTION_MODES.map(mode => <option key={mode}>{mode}</option>)}</select></label><label className="field"><span>Credential reference <small>(safe metadata only)</small></span><input aria-label="Credential reference (safe metadata only)" className="input" value={value.credentialReference ?? ''} onChange={event => update('credentialReference', event.target.value)} placeholder={editing ? 'Leave blank to keep current reference' : 'Optional vault reference'} autoComplete="off" /><span className="field-hint">Secrets and tokens are not loaded into this form or shown in the UI.</span></label></div><div className="form-actions"><button className="button button-primary" type="button" disabled={!value.code.trim() || !value.name.trim() || !value.deviceType.trim() || !value.timeZoneId.trim()} onClick={onSave}>{editing ? 'Save changes' : 'Create device'}</button><button className="button button-secondary" type="button" onClick={onCancel}>Cancel</button></div></Card>
}

function MappingForm({ value, devices, employees, onChange, onSave, onCancel }: { value: MappingFormValue; devices: AttendanceDevice[]; employees: Array<{ id: string; employeeCode: string; fullName: string }>; onChange: (value: MappingFormValue) => void; onSave: () => void; onCancel: () => void }) {
  return <Card title="Create employee mapping" subtitle="Device and external identity are immutable after creation; the backend validates overlap and tenant ownership."><div className="form-grid"><label className="field"><span>Device</span><select className="input" value={value.deviceId} onChange={event => onChange({ ...value, deviceId: event.target.value })}><option value="">Select device</option>{devices.map(device => <option key={device.id} value={device.id}>{device.code} — {device.name} ({device.status})</option>)}</select></label><label className="field"><span>Employee</span><select className="input" value={value.employeeId} onChange={event => onChange({ ...value, employeeId: event.target.value })}><option value="">Select active employee</option>{employees.map(employee => <option key={employee.id} value={employee.id}>{employee.employeeCode} — {employee.fullName}</option>)}</select></label><label className="field"><span>External employee identifier</span><input className="input" value={value.externalEmployeeIdentifier} onChange={event => onChange({ ...value, externalEmployeeIdentifier: event.target.value })} /></label><label className="field"><span>Effective from</span><input className="input" type="date" value={value.effectiveFrom} onChange={event => onChange({ ...value, effectiveFrom: event.target.value })} /></label><label className="field"><span>Effective to</span><input className="input" type="date" value={value.effectiveTo} min={value.effectiveFrom || undefined} onChange={event => onChange({ ...value, effectiveTo: event.target.value })} /></label><label className="field"><span>Status</span><select className="input" value={value.status} onChange={event => onChange({ ...value, status: event.target.value as 'Active' | 'Inactive' })}><option>Active</option><option>Inactive</option></select></label></div><div className="form-actions"><button className="button button-primary" type="button" disabled={!value.deviceId || !value.employeeId || !value.externalEmployeeIdentifier.trim() || !value.effectiveFrom} onClick={onSave}>Create mapping</button><button className="button button-secondary" type="button" onClick={onCancel}>Cancel</button></div></Card>
}

function StatusBadge({ status }: { status: string }) {
  const tone: BadgeTone = status === 'Active' || status === 'Succeeded' || status === 'Accepted' ? 'success' : status === 'Inactive' || status === 'Duplicate' || status === 'PartiallySucceeded' ? 'warning' : status === 'RequiresPeriodReopen' || status === 'Unmapped' ? 'info' : status === 'Disabled' || status === 'Failed' || status === 'Rejected' ? 'danger' : 'neutral'
  return <Badge tone={tone}>{status}</Badge>
}

function formatDateTime(value?: string | null): string {
  return value ? new Date(value).toLocaleString() : '—'
}

function safeContext(value: string): string {
  try {
    const parsed = JSON.parse(value) as Record<string, unknown>
    return JSON.stringify(parsed)
  } catch { return value }
}

function tabLabel(tab: DeviceTab): string {
  return ({ devices: 'Devices', mappings: 'Employee mappings', issues: 'Ingestion issues', 'sync-runs': 'Sync runs', history: 'Audit history' })[tab]
}
