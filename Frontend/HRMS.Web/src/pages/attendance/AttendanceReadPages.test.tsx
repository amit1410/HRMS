import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { MyAttendancePage } from './MyAttendancePage.tsx'
import { ManagerAttendancePage } from './ManagerAttendancePage.tsx'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok } from '../../test/stubAdapter.ts'

const day = (overrides: Record<string, unknown> = {}) => ({
  date: '2026-09-12', dayOfWeek: 'Saturday', dayType: 'Shift', attendanceStatus: 'Present',
  shiftCode: 'A-MORNING', shiftName: 'Morning', assignmentSource: 'Auto', calendarSource: 'WorkingDay',
  firstPunchAtUtc: '2026-09-12T03:30:00Z', lastPunchAtUtc: '2026-09-12T12:00:00Z', workedMinutes: 510,
  expectedWorkMinutes: 480, punchCount: 2, sessionCount: 1, isProcessed: true, isOverride: false,
  isLateIn: false, isEarlyOut: true, isGraceApplied: false, isSinglePunch: false,
  hasMissingInPunch: false, hasMissingOutPunch: false, hasInvalidPunchSequence: false,
  leaveConflict: true, requiresMarkOutApproval: false, ...overrides,
})

const detail = { day: day(), punches: [], sessions: [{ inAtUtc: '2026-09-12T03:30:00Z', outAtUtc: '2026-09-12T12:00:00Z', workedMinutes: 510 }] }

describe('Attendance read views', () => {
  let restore: (() => void) | undefined
  afterEach(() => restore?.())

  it('renders employee monthly statuses and summary', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/me/calendar', () => ({ data: ok([day(), day({ date: '2026-09-13', attendanceStatus: 'WeeklyOff', dayType: 'WeeklyOff', firstPunchAtUtc: null, lastPunchAtUtc: null, workedMinutes: null })]) }))
    renderAsUser(<MyAttendancePage />)
    expect(await screen.findByText('My Attendance')).toBeInTheDocument()
    expect((await screen.findAllByText('A-MORNING')).length).toBe(2)
    expect(screen.getAllByText('Weekly Off').length).toBeGreaterThan(0)
    expect(screen.getAllByText(/Leave Conflict/).length).toBeGreaterThan(0)
    expect(screen.getAllByText(/Early Out/).length).toBeGreaterThan(0)
  })

  it('navigates employee months through the server query', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/me/calendar', call => ({ data: ok([day({ date: `${call.params.year}-${String(call.params.month).padStart(2, '0')}-01` })]) }))
    renderAsUser(<MyAttendancePage />)
    await screen.findAllByText('A-MORNING')
    fireEvent.click(screen.getByRole('button', { name: 'Previous month' }))
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/me/calendar').length).toBeGreaterThan(1))
    expect(stub.callsTo('get', '/api/attendance/me/calendar').at(-1)?.params.month).not.toBe(stub.callsTo('get', '/api/attendance/me/calendar')[0]?.params.month)
  })

  it('opens employee daily detail with sessions and leave conflict', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/me/calendar', () => ({ data: ok([day()]) }))
    stub.on('get', '/api/attendance/me/days/2026-09-12', () => ({ data: ok(detail) }))
    renderAsUser(<MyAttendancePage />)
    fireEvent.click(await screen.findByRole('button', { name: 'Details' }))
    expect(await screen.findByRole('dialog')).toHaveTextContent('Attendance details')
    expect(screen.getByText('Leave Conflict')).toBeInTheDocument()
    expect(screen.getByText(/Punches \/ Sessions/)).toBeInTheDocument()
  })

  it('keeps employee page usable when calendar or detail fails', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/me/calendar', () => ({ status: 503, data: fail('Calendar unavailable') }))
    renderAsUser(<MyAttendancePage />)
    expect(await screen.findByText('Calendar unavailable')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Current month' })).toBeInTheDocument()
  })

  it('renders manager team rows, summary, filters, and pagination', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/manager/team', call => ({ data: ok({ rows: { items: [{ employeeId: 'e1', employeeCode: 'E001', employeeName: 'A Employee', day: day() }], page: Number(call.params.page ?? 1), pageSize: 20, totalCount: 40, totalPages: 2, hasPreviousPage: Number(call.params.page ?? 1) > 1, hasNextPage: Number(call.params.page ?? 1) < 2 }, summary: { present: 1, absent: 0, onLeave: 0, holiday: 0, weeklyOff: 0, incomplete: 0, notProcessed: 0, late: 0, earlyOut: 1, leaveConflict: 1 } }) }))
    renderAsUser(<ManagerAttendancePage />)
    expect(await screen.findByText(/A Employee/)).toBeInTheDocument()
    expect(screen.getByText('Team summary')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'Present' } })
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/manager/team').at(-1)?.params.status).toBe('Present'))
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/manager/team').at(-1)?.params.page).toBe(2))
  })

  it('opens manager daily detail', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/manager/team', () => ({ data: ok({ rows: { items: [{ employeeId: 'e1', employeeCode: 'E001', employeeName: 'A Employee', day: day() }], page: 1, pageSize: 20, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false }, summary: { present: 1, absent: 0, onLeave: 0, holiday: 0, weeklyOff: 0, incomplete: 0, notProcessed: 0, late: 0, earlyOut: 0, leaveConflict: 0 } }) }))
    stub.on('get', '/api/attendance/manager/team/e1/2026-09-12', () => ({ data: ok(detail) }))
    renderAsUser(<ManagerAttendancePage />)
    fireEvent.click(await screen.findByRole('button', { name: 'Details' }))
    expect(await screen.findByRole('dialog')).toHaveTextContent('Team attendance detail')
  })

  it('shows a clear manager query failure instead of an empty success state', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/manager/team', () => ({ status: 403, data: fail('Manager access denied') }))
    renderAsUser(<ManagerAttendancePage />)
    expect(await screen.findByText('Manager access denied')).toBeInTheDocument()
    expect(screen.getByText('Team filters')).toBeInTheDocument()
  })
})
