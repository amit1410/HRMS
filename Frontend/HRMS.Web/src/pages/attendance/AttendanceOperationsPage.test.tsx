import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AttendanceOperationsPage } from './AttendanceOperationsPage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { installStubAdapter, ok } from '../../test/stubAdapter.ts'

const emptyPage = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }
const dashboardData = { fromDate: '2026-09-22', toDate: '2026-09-22', employees: 1, processedDays: 1, presentDays: 1, absentDays: 0, leaveDays: 0, onDutyDays: 0, weeklyOffDays: 0, holidayDays: 0, exceptionDays: 1, lateDays: 1, earlyDepartureDays: 0, pendingRegularizations: 1, pendingOnDuty: 0, openPeriods: 1, finalizedPeriods: 0, activeAbsentExceptions: 0, missedPunchExceptions: 0, pendingCorrections: 1, missingInPunchExceptions: 0, missingOutPunchExceptions: 0 }
const exceptionRow = (exceptionType: string) => ({ id: 'day-1', employeeId: 'emp-1', employeeCode: 'E001', employeeName: 'Team Member', businessDate: '2026-09-22', shiftCode: 'DAY', exceptionType, attendanceStatus: exceptionType === 'Absent' ? 'Absent' : 'Present', scheduledStartUtc: '2026-09-22T09:00:00Z', scheduledEndUtc: '2026-09-22T17:00:00Z', firstPunchAtUtc: '2026-09-22T09:20:00Z', lastPunchAtUtc: '2026-09-22T16:40:00Z', workedMinutes: 440, lateMinutes: 20, earlyDepartureMinutes: 20, isBlocking: false, isResolved: false, ageDays: 0, attendanceVersion: 4, message: 'Operational exception' })

function setupWorkbench(exceptionType = 'LateArrival', requests: unknown[] = []) {
  const stub = installStubAdapter()
  stub.on('get', /\/api\/attendance\/operations\/dashboard/, () => ({ data: ok(dashboardData) }))
  stub.on('get', /\/api\/attendance\/operations\/exceptions/, () => ({ data: ok({ ...emptyPage, items: [exceptionRow(exceptionType)], totalCount: 1, totalPages: 1 }) }))
  stub.on('get', '/api/attendance/manager/regularizations', () => ({ data: ok({ ...emptyPage, items: requests, totalCount: requests.length }) }))
  return stub
}

describe('Attendance operations dashboard drill-down', () => {
  let restore: (() => void) | undefined
  afterEach(() => restore?.())

  it('routes dashboard exception counts into the canonical filtered workbench', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', /\/api\/attendance\/operations\/dashboard/, () => ({ data: ok({ fromDate: '2026-09-22', toDate: '2026-09-22', employees: 1, processedDays: 1, presentDays: 1, absentDays: 0, leaveDays: 0, onDutyDays: 0, weeklyOffDays: 0, holidayDays: 0, exceptionDays: 2, lateDays: 2, earlyDepartureDays: 0, pendingRegularizations: 0, pendingOnDuty: 0, openPeriods: 1, finalizedPeriods: 0, activeAbsentExceptions: 0, missedPunchExceptions: 0, pendingCorrections: 0, missingInPunchExceptions: 0, missingOutPunchExceptions: 0 }) }))
    stub.on('get', /\/api\/attendance\/operations\/exceptions/, () => ({ data: ok({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }) }))
    stub.on('get', '/api/attendance/manager/regularizations', () => ({ data: ok({ items: [], page: 1, pageSize: 25, totalCount: 0 }) }))
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView] }) })

    fireEvent.click(await screen.findByRole('button', { name: 'Late (2)' }))

    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/operations/exceptions').at(-1)?.params.exceptionType).toBe('LateArrival'))
    expect(stub.callsTo('get', '/api/attendance/operations/exceptions').at(-1)?.params.pageSize).toBe(25)
    expect(stub.callsTo('get', '/api/attendance/manager/regularizations')[0]?.params.fromDate).toBeTruthy()
    expect(stub.callsTo('get', '/api/attendance/manager/regularizations')[0]?.params.toDate).toBeTruthy()
  })

  it('submits a bounded correction item with its expected version and renders per-item failure', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', /\/api\/attendance\/operations\/dashboard/, () => ({ data: ok({ fromDate: '2026-09-22', toDate: '2026-09-22', employees: 1, processedDays: 1, presentDays: 1, absentDays: 0, leaveDays: 0, onDutyDays: 0, weeklyOffDays: 0, holidayDays: 0, exceptionDays: 0, lateDays: 0, earlyDepartureDays: 0, pendingRegularizations: 0, pendingOnDuty: 0, openPeriods: 1, finalizedPeriods: 0, activeAbsentExceptions: 0, missedPunchExceptions: 0, pendingCorrections: 0, missingInPunchExceptions: 0, missingOutPunchExceptions: 0 }) }))
    stub.on('get', /\/api\/attendance\/operations\/exceptions/, () => ({ data: ok({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }) }))
    stub.on('get', '/api/attendance/manager/regularizations', () => ({ data: ok({ items: [], page: 1, pageSize: 25, totalCount: 0 }) }))
    stub.on('post', '/api/attendance/operations/bulk-corrections', call => {
      expect(call.body).toEqual([expect.objectContaining({ employeeId: 'emp-1', expectedAttendanceVersion: 4, reason: 'Punch correction' })])
      return { data: ok({ items: [{ employeeId: 'emp-1', businessDate: '2026-09-22', success: false, failureCode: 'StaleVersion', message: 'Attendance changed', currentVersion: 5 }], succeeded: 0, failed: 1 }) }
    })
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView] }) })
    fireEvent.change(screen.getByLabelText(/Correction items/), { target: { value: 'emp-1 | 2026-09-22 | 4' } })
    fireEvent.change(screen.getByLabelText('Correction reason'), { target: { value: 'Punch correction' } })
    fireEvent.click(screen.getByRole('button', { name: 'Submit bulk Attendance corrections' }))
    expect(await screen.findByText(/StaleVersion/)).toBeInTheDocument()
  })

  it('renders read-only Attendance history with actor, reason, and version traceability', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', /\/api\/attendance\/operations\/dashboard/, () => ({ data: ok({ fromDate: '2026-09-22', toDate: '2026-09-22', employees: 1, processedDays: 1, presentDays: 1, absentDays: 0, leaveDays: 0, onDutyDays: 0, weeklyOffDays: 0, holidayDays: 0, exceptionDays: 0, lateDays: 0, earlyDepartureDays: 0, pendingRegularizations: 0, pendingOnDuty: 0, openPeriods: 1, finalizedPeriods: 0, activeAbsentExceptions: 0, missedPunchExceptions: 0, pendingCorrections: 0, missingInPunchExceptions: 0, missingOutPunchExceptions: 0 }) }))
    stub.on('get', /\/api\/attendance\/operations\/exceptions/, () => ({ data: ok({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }) }))
    stub.on('get', '/api/attendance/manager/regularizations', () => ({ data: ok({ items: [], page: 1, pageSize: 25, totalCount: 0 }) }))
    stub.on('get', /\/api\/attendance\/operations\/employees\/emp-1\/history/, () => ({ data: ok({ items: [{ id: 'audit-1', employeeId: 'emp-1', attendanceDayId: 'day-1', businessDate: '2026-09-22', action: 'RegularizationApproved', actorUserId: 'actor-1', occurredAtUtc: '2026-09-22T10:00:00Z', reason: 'Verified evidence', referenceId: 'request-1', attendanceVersion: 4, oldAttendanceVersion: 3, newAttendanceVersion: 4, oldValue: 'Absent', newValue: 'Present' }], page: 1, pageSize: 50, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false }) }))
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView] }) })
    fireEvent.change(screen.getByLabelText('Employee ID'), { target: { value: 'emp-1' } })
    expect(await screen.findByText(/RegularizationApproved · version 3 to 4 · Absent to Present · actor actor-1 · Verified evidence/)).toBeInTheDocument()
  })
  it('Manual_attendance_form_submits_valid_request', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', /\/api\/attendance\/operations\/dashboard/, () => ({ data: ok({ fromDate: '2026-09-22', toDate: '2026-09-22', employees: 1, processedDays: 1, presentDays: 1, absentDays: 0, leaveDays: 0, onDutyDays: 0, weeklyOffDays: 0, holidayDays: 0, exceptionDays: 0, lateDays: 0, earlyDepartureDays: 0, pendingRegularizations: 0, pendingOnDuty: 0, openPeriods: 1, finalizedPeriods: 0, activeAbsentExceptions: 0, missedPunchExceptions: 0, pendingCorrections: 0, missingInPunchExceptions: 0, missingOutPunchExceptions: 0 }) }))
    stub.on('get', /\/api\/attendance\/operations\/exceptions/, () => ({ data: ok({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }) }))
    stub.on('get', '/api/attendance/manager/regularizations', () => ({ data: ok({ items: [], page: 1, pageSize: 25, totalCount: 0 }) }))
    stub.on('post', '/api/attendance/operations/manual', call => { expect(call.body).toEqual(expect.objectContaining({ employeeId: 'emp-manual', reason: 'Evidence reviewed', expectedAttendanceVersion: 3 })); return { data: ok({ id: 'request-manual' }) } })
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView, Permissions.attendance.adminCorrectionManage] }) })
    fireEvent.change(screen.getAllByLabelText('Employee ID')[0]!, { target: { value: 'emp-manual' } })
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Evidence reviewed' } })
    fireEvent.change(screen.getByLabelText('Expected Attendance version'), { target: { value: '3' } })
    fireEvent.click(screen.getByRole('button', { name: 'Submit manual request' }))
    expect(await screen.findByText('Manual Attendance request submitted for maker-checker review.')).toBeInTheDocument()
    expect(stub.callsTo('post', '/api/attendance/operations/manual')).toHaveLength(1)
  })

  it('Manager_exception_inbox_renders_team_only', async () => {
    const stub = setupWorkbench(); restore = stub.restore
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ roles: ['Manager'], permissions: [Permissions.attendance.exceptionView, Permissions.attendance.regularizationApprove] }) })
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/operations/exceptions')).toHaveLength(1))
    expect(screen.getByRole('table', { name: 'Attendance exception workbench' }).textContent).toContain('Team Member')
    expect(stub.callsTo('get', '/api/attendance/manager/regularizations')).toHaveLength(1)
    expect(screen.queryByText('Non-team member')).not.toBeInTheDocument()
  })

  it('TimeManager_operations_workbench_renders_scoped_exception_and_history_controls', async () => {
    const stub = setupWorkbench(); restore = stub.restore
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ roles: ['TimeManager'], permissions: [Permissions.attendance.exceptionView, Permissions.attendance.regularizationApprove] }) })
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/operations/exceptions')).toHaveLength(1))
    expect(screen.getByRole('table', { name: 'Attendance exception workbench' }).textContent).toContain('Team Member')
    expect(screen.getByRole('heading', { name: 'Attendance audit and version history' })).toBeInTheDocument()
    expect(stub.callsTo('get', '/api/attendance/operations/exceptions')).toHaveLength(1)
  })

  it('Exception_filters_are_sent_server_side', async () => {
    const stub = setupWorkbench(); restore = stub.restore
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView] }) })
    fireEvent.change(screen.getByLabelText('Employee search'), { target: { value: 'E001' } })
    fireEvent.change(screen.getByLabelText('Employee ID filter'), { target: { value: 'emp-1' } })
    fireEvent.change(screen.getByLabelText('Attendance status'), { target: { value: 'Present' } })
    fireEvent.change(screen.getByLabelText('Exception type'), { target: { value: 'LateArrival' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/operations/exceptions').at(-1)?.params).toEqual(expect.objectContaining({ search: 'E001', employeeId: 'emp-1', attendanceStatus: 'Present', exceptionType: 'LateArrival' })))
  })

  it('Exception_paging_is_server_side', async () => {
    const stub = setupWorkbench(); restore = stub.restore
    stub.on('get', /\/api\/attendance\/operations\/exceptions/, call => ({ data: ok({ ...emptyPage, items: [exceptionRow('LateArrival')], page: Number(call.params.page), pageSize: 25, totalCount: 50, totalPages: 2, hasPreviousPage: Number(call.params.page) > 1, hasNextPage: Number(call.params.page) < 2 }) }))
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView] }) })
    fireEvent.click(await screen.findByRole('button', { name: 'Next' }))
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/operations/exceptions').at(-1)?.params).toEqual(expect.objectContaining({ page: 2, pageSize: 25 })))
  })

  it('Missed_punch_action_opens_correct_workflow', async () => {
    const stub = setupWorkbench('MissingInPunch'); restore = stub.restore
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView] }) })
    expect(await screen.findByRole('link', { name: 'Open correction workflow' })).toHaveAttribute('href', '/attendance/requests')
    expect(screen.queryByRole('button', { name: /resolve/i })).not.toBeInTheDocument()
  })

  it('Maker_checker_queue_renders_pending_requests', async () => {
    const request = { id: 'req-1', businessDate: '2026-09-22', requestType: 'Regularization', reason: 'Punch evidence', status: 'Pending', events: [{ eventType: 'Submitted', actorUserId: 'maker-1' }] }
    const stub = setupWorkbench('LateArrival', [request]); restore = stub.restore
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView, Permissions.attendance.regularizationApprove] }) })
    expect(await screen.findByText(/Regularization · Punch evidence · Pending/)).toBeInTheDocument()
    expect(screen.getByLabelText('Select req-1')).toBeInTheDocument()
  })

  it('Submitter_cannot_approve_own_request', async () => {
    const user = makeUser({ permissions: [Permissions.attendance.exceptionView, Permissions.attendance.regularizationApprove] })
    const request = { id: 'own-req', businessDate: '2026-09-22', requestType: 'Regularization', reason: 'Own request', status: 'Pending', events: [{ eventType: 'Submitted', actorUserId: user.id }] }
    const stub = setupWorkbench('LateArrival', [request]); restore = stub.restore
    renderAsUser(<AttendanceOperationsPage />, { user })
    expect(await screen.findByText(/Maker cannot approve own request/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Select own-req')).not.toBeInTheDocument()
  })

  it('Late_resolution_action_sends_acknowledgement_and_displays_calculated_facts', async () => {
    const stub = setupWorkbench('LateArrival'); restore = stub.restore
    stub.on('post', '/api/attendance/operations/exceptions/resolve', call => { expect(call.body).toEqual(expect.objectContaining({ attendanceDayId: 'day-1', exceptionType: 'LateArrival', action: 'Acknowledge', reason: 'Reviewed evidence', expectedAttendanceVersion: 4 })); return { data: ok({}) } })
    vi.stubGlobal('prompt', vi.fn(() => 'Reviewed evidence'))
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView] }) })
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/operations/exceptions')).toHaveLength(1))
    const table = screen.getByRole('table', { name: 'Attendance exception workbench' })
    expect(table.textContent).toContain('20 min')
    expect(table.textContent).toContain('LateArrival')
    expect(table.textContent).toContain('09:00:00')
    expect(table.textContent).toContain('09:20:00')
    fireEvent.click(screen.getByRole('button', { name: 'Acknowledge' }))
    await waitFor(() => expect(stub.calls.some(call => call.method === 'post' && call.url.includes('/resolve'))).toBe(true))
    vi.unstubAllGlobals()
  })

  it('Early_resolution_action_sends_waiver_and_displays_calculated_facts', async () => {
    const stub = setupWorkbench('EarlyDeparture'); restore = stub.restore
    stub.on('post', /resolve/, call => { expect(call.body).toEqual(expect.objectContaining({ attendanceDayId: 'day-1', exceptionType: 'EarlyDeparture', action: 'Waive', reason: 'Approved waiver', expectedAttendanceVersion: 4 })); return { data: ok({}) } })
    vi.stubGlobal('prompt', vi.fn(() => 'Approved waiver'))
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView] }) })
    expect(await screen.findByText('EarlyDeparture')).toBeInTheDocument()
    expect(screen.getByText('17:00:00')).toBeInTheDocument()
    expect(screen.getByRole('table', { name: 'Attendance exception workbench' }).textContent).toContain('16:40:00')
    expect(screen.getByRole('table', { name: 'Attendance exception workbench' }).textContent).toContain('20 min')
    fireEvent.click(screen.getByRole('button', { name: 'Waive' }))
    await waitFor(() => expect(stub.calls.some(call => call.method === 'post' && call.url.includes('/resolve'))).toBe(true))
    vi.unstubAllGlobals()
  })

  it('Absence_actions_use_authoritative_workflows', async () => {
    const stub = setupWorkbench('Absent'); restore = stub.restore
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView] }) })
    expect(await screen.findByRole('link', { name: 'Open Leave / On Duty / correction workflows' })).toHaveAttribute('href', '/attendance/requests')
    expect(screen.queryByRole('button', { name: /resolve absence/i })).not.toBeInTheDocument()
  })

  it('Bulk_approve_submits_selected_rows_and_surfaces_result', async () => {
    const request = { id: 'req-approve', businessDate: '2026-09-22', requestType: 'Regularization', reason: 'Approve', status: 'Pending', events: [{ eventType: 'Submitted', actorUserId: 'other-maker' }] }
    const stub = setupWorkbench('LateArrival', [request]); restore = stub.restore
    stub.on('post', '/api/attendance/operations/bulk', () => ({ data: ok({ items: [{ requestId: 'req-approve', success: true, message: 'Approved' }] }) }))
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView, Permissions.attendance.regularizationApprove] }) })
    fireEvent.click(await screen.findByLabelText('Select req-approve'))
    fireEvent.click(screen.getByRole('button', { name: 'Bulk approve selected' }))
    expect(await screen.findByText(/req-approve: Success/)).toBeInTheDocument()
    expect(stub.callsTo('post', '/api/attendance/operations/bulk')).toHaveLength(1)
  })

  it('Bulk_reject_submits_selected_rows_with_required_reason', async () => {
    const request = { id: 'req-reject', businessDate: '2026-09-22', requestType: 'Regularization', reason: 'Reject', status: 'Pending', events: [{ eventType: 'Submitted', actorUserId: 'other-maker' }] }
    const stub = setupWorkbench('LateArrival', [request]); restore = stub.restore
    stub.on('post', '/api/attendance/operations/bulk', call => { expect(call.body).toEqual([expect.objectContaining({ requestId: 'req-reject', approve: false, comments: 'Not supported' })]); return { data: ok({ items: [{ requestId: 'req-reject', success: true, message: 'Rejected' }] }) } })
    vi.stubGlobal('prompt', vi.fn(() => 'Not supported'))
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView, Permissions.attendance.regularizationApprove] }) })
    fireEvent.click(await screen.findByLabelText('Select req-reject'))
    fireEvent.click(screen.getByRole('button', { name: 'Bulk reject selected' }))
    expect(await screen.findByText(/req-reject: Success/)).toBeInTheDocument()
    vi.unstubAllGlobals()
  })

  it('Export_uses_server_filters_not_visible_page_rows', async () => {
    const stub = setupWorkbench(); restore = stub.restore
    stub.on('get', /\/api\/attendance\/operations\/export/, () => ({ data: ok(new Blob(['csv'])) }))
    const create = vi.fn(() => 'blob:test'); vi.stubGlobal('URL', { ...URL, createObjectURL: create, revokeObjectURL: vi.fn() })
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView] }) })
    fireEvent.change(screen.getByLabelText('Employee search'), { target: { value: 'E001' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))
    fireEvent.click(screen.getByRole('button', { name: 'Export filtered exceptions' }))
    await waitFor(() => expect(stub.calls.some(call => call.method === 'get' && call.url.includes('/export') && call.params.search === 'E001')).toBe(true))
    vi.unstubAllGlobals()
  })

  it('Locked_period_correction_is_blocked_and_backend_conflict_is_shown', async () => {
    const stub = setupWorkbench(); restore = stub.restore
    stub.on('post', '/api/attendance/operations/manual', () => { throw new Error('Finalized period is locked; reopen is required.') })
    renderAsUser(<AttendanceOperationsPage />, { user: makeUser({ permissions: [Permissions.attendance.exceptionView, Permissions.attendance.adminCorrectionManage] }) })
    fireEvent.change(screen.getAllByLabelText('Employee ID')[0]!, { target: { value: 'emp-1' } })
    fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'Correction' } })
    fireEvent.click(screen.getByRole('button', { name: 'Submit manual request' }))
    expect(await screen.findByText(/Finalized period is locked/)).toBeInTheDocument()
  })
})
