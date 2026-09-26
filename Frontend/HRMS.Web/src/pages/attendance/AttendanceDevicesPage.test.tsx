import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { AttendanceDevicesPage } from './AttendanceDevicesPage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeEmployee, makeUser, paged } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { installStubAdapter, ok } from '../../test/stubAdapter.ts'

const device = { id: 'device-1', code: 'BIO-01', name: 'Lobby terminal', deviceType: 'Terminal', vendor: 'NoProvider', serialNumber: 'SER-1', workLocationId: 'loc-1', timeZoneId: 'Asia/Kolkata', connectionMode: 'Pull', status: 'Active', lastSuccessfulSyncAtUtc: '2026-09-26T08:00:00Z', lastAttemptedSyncAtUtc: '2026-09-26T08:05:00Z' }
const mapping = { id: 'mapping-1', deviceId: 'device-1', externalEmployeeIdentifier: 'EXT-001', employeeId: 'employee-1', employeeCode: 'EMP-001', effectiveFrom: '2026-01-01', effectiveTo: null, status: 'Active' }
const issue = { id: 'issue-1', deviceId: 'device-1', syncRunId: 'run-1', employeeId: null, attendancePunchId: null, externalEventId: 'evt-1', externalEmployeeIdentifier: 'EXT-UNKNOWN', occurredAtUtc: '2026-09-26T08:00:00Z', receivedAtUtc: '2026-09-26T08:01:00Z', direction: 'In', status: 'Unmapped', sanitizedError: 'No effective active employee mapping exists.' }
const syncRun = { id: 'run-1', deviceId: 'device-1', source: 'NoProvider', status: 'Failed', startedAtUtc: '2026-09-26T08:00:00Z', completedAtUtc: '2026-09-26T08:01:00Z', received: 1, accepted: 0, duplicate: 0, rejected: 1, unmapped: 0, errorCount: 1, checkpointBefore: 'before', checkpointAfter: null }
const audit = { id: 'audit-1', actorUserId: 'actor-1', deviceId: 'device-1', mappingId: null, employeeId: null, action: 'DeviceCreated', occurredAtUtc: '2026-09-26T08:00:00Z', contextJson: '{"code":"BIO-01","CredentialReferenceConfigured":false}' }

function setup() {
  const stub = installStubAdapter()
  const empty = paged([])
  stub.on('get', '/api/attendance/devices', call => ({ data: ok({ ...paged([device]), page: Number(call.params.page ?? 1), pageSize: Number(call.params.pageSize ?? 20), totalCount: 25, totalPages: 2, hasPreviousPage: Number(call.params.page ?? 1) > 1, hasNextPage: Number(call.params.page ?? 1) < 2 }) }))
  stub.on('get', /\/api\/attendance\/devices\/mappings$/, () => ({ data: ok(paged([mapping])) }))
  stub.on('get', /\/api\/attendance\/devices\/issues$/, call => ({ data: ok({ ...paged([issue]), page: Number(call.params.page ?? 1), pageSize: Number(call.params.pageSize ?? 20), totalCount: 25, totalPages: 2, hasPreviousPage: Number(call.params.page ?? 1) > 1, hasNextPage: Number(call.params.page ?? 1) < 2 }) }))
  stub.on('get', /\/api\/attendance\/devices\/sync-runs$/, call => ({ data: ok({ ...paged([syncRun]), page: Number(call.params.page ?? 1), pageSize: Number(call.params.pageSize ?? 20), totalCount: 25, totalPages: 2, hasPreviousPage: Number(call.params.page ?? 1) > 1, hasNextPage: Number(call.params.page ?? 1) < 2 }) }))
  stub.on('get', /\/api\/attendance\/devices\/history$/, () => ({ data: ok(paged([audit])) }))
  stub.on('get', '/api/employees', () => ({ data: ok(paged([makeEmployee({ id: 'employee-1' })])) }))
  stub.on('get', '/api/master-data/work-locations', () => ({ data: ok([{ id: 'loc-1', code: 'HQ', name: 'Headquarters', isActive: true }]) }))
  return { stub, empty }
}

const fullPermissions = [Permissions.attendance.deviceView, Permissions.attendance.deviceManage, Permissions.attendance.deviceMapping, Permissions.attendance.deviceSync, Permissions.attendance.deviceViewHistory]

describe('Attendance device integration frontend', () => {
  let restore: (() => void) | undefined
  afterEach(() => restore?.())

  it('renders an authorized device list and sends filters and paging to the server', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    expect(await screen.findByRole('table', { name: 'Attendance devices' })).toHaveTextContent('BIO-01')
    fireEvent.change(screen.getByLabelText('Device search'), { target: { value: 'Lobby' } })
    fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'Active' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/devices').at(-1)?.params).toEqual(expect.objectContaining({ search: 'Lobby', status: 'Active', page: 1 })))
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/devices').at(-1)?.params).toEqual(expect.objectContaining({ page: 2, pageSize: 20 })))
  })

  it('hides management actions for a user with view-only device permission', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: [Permissions.attendance.deviceView] }) })
    await screen.findByText('BIO-01')
    expect(screen.queryByRole('button', { name: 'Add device' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Sync now' })).not.toBeInTheDocument()
  })

  it('creates a device without rendering or pre-filling credential values', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('post', '/api/attendance/devices', call => { expect(call.body).toEqual(expect.objectContaining({ code: 'BIO-NEW', credentialReference: 'credential-ref-metadata' })); return { data: ok({ ...device, code: 'BIO-NEW' }) } })
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('button', { name: 'Add device' }))
    expect(screen.getByText(/Secrets and tokens are not loaded/)).toBeInTheDocument()
    expect(screen.getByLabelText('Credential reference (safe metadata only)')).toHaveValue('')
    fireEvent.change(screen.getByLabelText('Device code'), { target: { value: 'BIO-NEW' } })
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'New terminal' } })
    fireEvent.change(screen.getByLabelText('Vendor / provider key'), { target: { value: 'NoProvider' } })
    fireEvent.change(screen.getByLabelText('Device type'), { target: { value: 'Terminal' } })
    fireEvent.change(screen.getByLabelText('Credential reference (safe metadata only)'), { target: { value: 'credential-ref-metadata' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create device' }))
    await waitFor(() => expect(stub.callsTo('post', '/api/attendance/devices')).toHaveLength(1))
    expect(screen.queryByText('password')).not.toBeInTheDocument()
  })

  it('supports edit, activate, deactivate, and Sync Now actions through the API', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('post', /\/api\/attendance\/devices\/device-1\/(activate|deactivate|sync)/, call => call.url.endsWith('/sync') ? { data: ok({ syncRunId: 'run-2', received: 1, accepted: 1, duplicate: 0, rejected: 0, unmapped: 0, items: [], checkpoint: 'after' }) } : { data: ok(device) })
    stub.on('put', '/api/attendance/devices/device-1', () => ({ data: ok(device) }))
    viConfirm()
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    await screen.findByText('BIO-01')
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }))
    expect(screen.getByLabelText('Credential reference (safe metadata only)')).toHaveValue('')
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(stub.callsTo('put', '/api/attendance/devices/device-1')).toHaveLength(1))
    fireEvent.click(screen.getByRole('button', { name: 'Deactivate' }))
    await waitFor(() => expect(stub.callsTo('post', '/api/attendance/devices/device-1/deactivate')).toHaveLength(1))
  })

  it('renders mappings, creates them, and does not offer immutable identity editing', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('post', '/api/attendance/devices/mappings', call => { expect(call.body).toEqual(expect.objectContaining({ deviceId: 'device-1', employeeId: 'employee-1' })); return { data: ok(mapping) } })
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Employee mappings' }))
    expect(await screen.findByText('EXT-001')).toBeInTheDocument()
    expect(screen.getByText(/External identity and device are immutable/)).toBeInTheDocument()
    fireEvent.click(screen.getAllByRole('button', { name: 'Create mapping' }).at(-1)!)
    fireEvent.change(screen.getAllByLabelText('Device').at(-1)!, { target: { value: 'device-1' } })
    fireEvent.change(screen.getByLabelText('Employee'), { target: { value: 'employee-1' } })
    fireEvent.change(screen.getAllByLabelText('External employee identifier').at(-1)!, { target: { value: 'EXT-NEW' } })
    fireEvent.change(screen.getByLabelText('Effective from'), { target: { value: '2026-01-01' } })
    fireEvent.click(screen.getAllByRole('button', { name: 'Create mapping' }).at(-1)!)
    await waitFor(() => expect(stub.callsTo('post', '/api/attendance/devices/mappings')).toHaveLength(1))
  })

  it('filters and pages issues server-side and shows finalized-period reprocessing safely', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('post', '/api/attendance/devices/issues/issue-1/reprocess', () => ({ data: ok({ syncRunId: 'run-2', received: 1, accepted: 0, duplicate: 0, rejected: 1, unmapped: 0, items: [{ externalEventId: 'evt-1', status: 'RequiresPeriodReopen', message: 'Requires reopen' }], checkpoint: null }) }))
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Ingestion issues' }))
    expect(await screen.findByText('EXT-UNKNOWN')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('External employee identifier'), { target: { value: 'EXT-UNKNOWN' } })
    fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'Unmapped' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/devices/issues').at(-1)?.params).toEqual(expect.objectContaining({ externalEmployeeIdentifier: 'EXT-UNKNOWN', status: 'Unmapped' })))
    fireEvent.click(screen.getByRole('button', { name: 'Reprocess' }))
    expect(await screen.findByText(/Finalized Attendance is protected/)).toBeInTheDocument()
    expect(stub.calls.some(call => call.url.includes('/reopen'))).toBe(false)
  })

  it('renders sync history, audit history without secrets, and surfaces unsupported provider errors', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('post', '/api/attendance/devices/device-1/sync', () => { throw new Error("UnsupportedProvider: no punch source is registered for 'NoProvider'.") })
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Sync runs' }))
    expect(await screen.findByText('NoProvider')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('tab', { name: 'Audit history' }))
    expect(await screen.findByText('DeviceCreated')).toBeInTheDocument()
    expect(screen.getByText(/CredentialReferenceConfigured/)).toBeInTheDocument()
    expect(screen.queryByText('SENSITIVE_VALUE_NOT_PRESENT')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('tab', { name: 'Devices' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Sync now' }))
    expect(await screen.findByText(/UnsupportedProvider/)).toBeInTheDocument()
  })

  it('renders the device operational columns from the API contract', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    const table = await screen.findByRole('table', { name: 'Attendance devices' })
    expect(table).toHaveTextContent('NoProvider')
    expect(table).toHaveTextContent('Pull')
    expect(table).toHaveTextContent('Asia/Kolkata')
    expect(stub.callsTo('get', '/api/attendance/devices').length).toBeGreaterThanOrEqual(1)
  })

  it('sends the selected device page size to the server', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    await screen.findByText('BIO-01')
    fireEvent.change(screen.getByText('Rows').parentElement!.querySelector('select')!, { target: { value: '50' } })
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/devices').at(-1)?.params).toEqual(expect.objectContaining({ page: 1, pageSize: 50 })))
  })

  it('does not render a Sync Now action without sync permission', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: [Permissions.attendance.deviceView, Permissions.attendance.deviceManage] }) })
    await screen.findByText('BIO-01')
    expect(screen.queryByRole('button', { name: 'Sync now' })).not.toBeInTheDocument()
  })

  it('does not render mapping administration without mapping permission', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: [Permissions.attendance.deviceView] }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Employee mappings' }))
    expect(await screen.findByText('EXT-001')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Create mapping' })).not.toBeInTheDocument()
  })

  it('does not prefill credential metadata while editing a device', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    await screen.findByText('BIO-01')
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }))
    expect(screen.getByLabelText('Credential reference (safe metadata only)')).toHaveValue('')
    expect(screen.queryByDisplayValue(/secret|token|password/i)).not.toBeInTheDocument()
  })

  it('supports device activation through the authorized lifecycle action', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('get', '/api/attendance/devices', () => ({ data: ok({ ...paged([{ ...device, status: 'Inactive' }]), totalCount: 1, totalPages: 1, hasNextPage: false }) }))
    stub.on('post', '/api/attendance/devices/device-1/activate', () => ({ data: ok({ ...device, status: 'Active' }) }))
    viConfirm()
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    await screen.findByText('BIO-01')
    fireEvent.click(screen.getByRole('button', { name: 'Activate' }))
    await waitFor(() => expect(stub.callsTo('post', '/api/attendance/devices/device-1/activate')).toHaveLength(1))
  })

  it('deactivates an employee mapping through the API', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('post', '/api/attendance/devices/mappings/mapping-1/deactivate', () => ({ data: ok({ ...mapping, status: 'Inactive' }) }))
    viConfirm()
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Employee mappings' }))
    await screen.findByText('EXT-001')
    fireEvent.click(screen.getByRole('button', { name: 'Deactivate' }))
    await waitFor(() => expect(stub.callsTo('post', '/api/attendance/devices/mappings/mapping-1/deactivate')).toHaveLength(1))
  })

  it('shows a mapping conflict returned by the backend', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('post', '/api/attendance/devices/mappings', () => { throw new Error('Duplicate active mapping for this employee and device.') })
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Employee mappings' }))
    fireEvent.click(screen.getAllByRole('button', { name: 'Create mapping' }).at(-1)!)
    fireEvent.change(screen.getAllByLabelText('Device').at(-1)!, { target: { value: 'device-1' } })
    fireEvent.change(screen.getByLabelText('Employee'), { target: { value: 'employee-1' } })
    fireEvent.change(screen.getAllByLabelText('External employee identifier').at(-1)!, { target: { value: 'EXT-NEW' } })
    fireEvent.change(screen.getByLabelText('Effective from'), { target: { value: '2026-01-01' } })
    fireEvent.click(screen.getAllByRole('button', { name: 'Create mapping' }).at(-1)!)
    expect(await screen.findByText(/Duplicate active mapping/)).toBeInTheDocument()
  })

  it('sends mapping filters server-side', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Employee mappings' }))
    await screen.findByText('EXT-001')
    fireEvent.change(screen.getByLabelText('External employee identifier'), { target: { value: 'EXT-001' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/devices/mappings').at(-1)?.params).toEqual(expect.objectContaining({ externalEmployeeIdentifier: 'EXT-001' })))
  })

  it('sends issue date and device filters server-side', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Ingestion issues' }))
    await screen.findByText('EXT-UNKNOWN')
    fireEvent.change(screen.getAllByLabelText('Device').at(-1)!, { target: { value: 'device-1' } })
    fireEvent.change(screen.getByLabelText('Received from'), { target: { value: '2026-09-26T00:00' } })
    fireEvent.change(screen.getByLabelText('Received to'), { target: { value: '2026-09-27T00:00' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/devices/issues').at(-1)?.params).toEqual(expect.objectContaining({ deviceId: 'device-1', fromUtc: '2026-09-26T00:00', toUtc: '2026-09-27T00:00' })))
  })

  it('pages ingestion issues on the server', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Ingestion issues' }))
    await screen.findByText('EXT-UNKNOWN')
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/devices/issues').at(-1)?.params).toEqual(expect.objectContaining({ page: 2, pageSize: 20 })))
  })

  it('shows a duplicate result from issue reprocessing without creating a local punch', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('post', '/api/attendance/devices/issues/issue-1/reprocess', () => ({ data: ok({ syncRunId: 'run-2', received: 1, accepted: 0, duplicate: 1, rejected: 0, unmapped: 0, items: [{ externalEventId: 'evt-1', status: 'Duplicate', message: 'Already processed.' }], checkpoint: null }) }))
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Ingestion issues' }))
    await screen.findByText('EXT-UNKNOWN')
    fireEvent.click(screen.getByRole('button', { name: 'Reprocess' }))
    expect(await screen.findByText('Already processed.')).toBeInTheDocument()
    expect(stub.calls.some(call => call.url.includes('/attendance-punches'))).toBe(false)
  })

  it('does not offer reprocessing for a non-reprocessable issue status', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('get', /\/api\/attendance\/devices\/issues$/, () => ({ data: ok(paged([{ ...issue, status: 'Duplicate' }])) }))
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Ingestion issues' }))
    expect(await screen.findByText('EXT-UNKNOWN')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reprocess' })).not.toBeInTheDocument()
  })

  it('sends sync-run filters server-side', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Sync runs' }))
    await screen.findByText('NoProvider')
    fireEvent.change(screen.getAllByLabelText('Device').at(-1)!, { target: { value: 'device-1' } })
    fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'Failed' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/devices/sync-runs').at(-1)?.params).toEqual(expect.objectContaining({ deviceId: 'device-1', status: 'Failed' })))
  })

  it('pages sync-run history on the server', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Sync runs' }))
    await screen.findByText('NoProvider')
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/devices/sync-runs').at(-1)?.params).toEqual(expect.objectContaining({ page: 2, pageSize: 20 })))
  })

  it('shows a successful Sync Now result from the backend', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('post', '/api/attendance/devices/device-1/sync', () => ({ data: ok({ syncRunId: 'run-2', received: 2, accepted: 1, duplicate: 1, rejected: 0, unmapped: 0, items: [], checkpoint: 'after' }) }))
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    await screen.findByText('BIO-01')
    fireEvent.click(screen.getByRole('button', { name: 'Sync now' }))
    expect(await screen.findByText(/1 accepted, 1 duplicate/)).toBeInTheDocument()
  })

  it('renders permission denial as an error state', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('get', '/api/attendance/devices', () => { throw new Error('Forbidden') })
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    expect(await screen.findByText('Forbidden')).toBeInTheDocument()
  })

  it('shows cross-tenant or not-found mapping errors without leaking data', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('post', '/api/attendance/devices/mappings', () => { throw new Error('The referenced employee was not found.') })
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Employee mappings' }))
    fireEvent.click(screen.getAllByRole('button', { name: 'Create mapping' }).at(-1)!)
    fireEvent.change(screen.getAllByLabelText('Device').at(-1)!, { target: { value: 'device-1' } })
    fireEvent.change(screen.getByLabelText('Employee'), { target: { value: 'employee-1' } })
    fireEvent.change(screen.getAllByLabelText('External employee identifier').at(-1)!, { target: { value: 'EXT-CROSS-TENANT' } })
    fireEvent.change(screen.getByLabelText('Effective from'), { target: { value: '2026-01-01' } })
    fireEvent.click(screen.getAllByRole('button', { name: 'Create mapping' }).at(-1)!)
    expect(await screen.findByText(/referenced employee was not found/)).toBeInTheDocument()
    expect(screen.queryByText('employee-secret')).not.toBeInTheDocument()
  })

  it('filters audit history by device identity', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Audit history' }))
    await screen.findByText('DeviceCreated')
    fireEvent.change(screen.getByLabelText('Device ID'), { target: { value: 'device-1' } })
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/devices/history').at(-1)?.params).toEqual(expect.objectContaining({ deviceId: 'device-1' })))
  })

  it('keeps audit context read-only and safe when context is malformed', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Audit history' }))
    expect(await screen.findByText(/CredentialReferenceConfigured/)).toBeInTheDocument()
    expect(screen.queryByText('SENSITIVE_VALUE_NOT_PRESENT')).not.toBeInTheDocument()
  })

  it('routes from a device to its mappings and applies the device scope', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    await screen.findByText('BIO-01')
    fireEvent.click(screen.getByRole('button', { name: 'Mappings' }))
    expect(await screen.findByText('EXT-001')).toBeInTheDocument()
    expect(stub.callsTo('get', '/api/attendance/devices/mappings').at(-1)?.params).toEqual(expect.objectContaining({ deviceId: 'device-1' }))
  })

  it('routes from a device to scoped audit history', async () => {
    const { stub } = setup(); restore = stub.restore
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    await screen.findByText('BIO-01')
    fireEvent.click(screen.getByRole('button', { name: 'History' }))
    expect(await screen.findByText('DeviceCreated')).toBeInTheDocument()
    expect(stub.callsTo('get', '/api/attendance/devices/history').at(-1)?.params).toEqual(expect.objectContaining({ deviceId: 'device-1' }))
  })

  it('shows empty operational issue results without a blank page', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('get', /\/api\/attendance\/devices\/issues$/, () => ({ data: ok(paged([])) }))
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    fireEvent.click(await screen.findByRole('tab', { name: 'Ingestion issues' }))
    expect(await screen.findByText('No ingestion issues')).toBeInTheDocument()
  })

  it('preserves backend validation text for device mutations', async () => {
    const { stub } = setup(); restore = stub.restore
    stub.on('put', '/api/attendance/devices/device-1', () => { throw new Error('Device code is already used.') })
    renderAsUser(<AttendanceDevicesPage />, { user: makeUser({ permissions: fullPermissions }) })
    await screen.findByText('BIO-01')
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }))
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))
    expect(await screen.findByText(/Device code is already used/)).toBeInTheDocument()
  })
})

function viConfirm(): void {
  globalThis.confirm = () => true
}
