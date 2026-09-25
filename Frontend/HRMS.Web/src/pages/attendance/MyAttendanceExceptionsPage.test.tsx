import { afterEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import { MyAttendanceExceptionsPage } from './MyAttendanceExceptionsPage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { installStubAdapter, ok } from '../../test/stubAdapter.ts'

describe('My Attendance exception inbox', () => {
  let restore: (() => void) | undefined
  afterEach(() => restore?.())

  it('Employee_exception_inbox_renders_own_active_items_from_self_endpoint', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/me/exceptions', () => ({ data: ok({ items: [{ id: 'day-1', employeeId: 'self', employeeName: 'Self', businessDate: '2026-09-22', shiftCode: 'D1', exceptionType: 'MissingOutPunch', attendanceStatus: 'Incomplete', firstPunchAtUtc: '2026-09-22T09:00:00Z', lastPunchAtUtc: null, workedMinutes: 300, lateMinutes: 0, earlyDepartureMinutes: 0, ageDays: 1, attendanceVersion: 2, relatedRequestId: null, message: 'Missing out punch' }], page: 1, pageSize: 25, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false }) }))
    renderAsUser(<MyAttendanceExceptionsPage />, { user: makeUser({ permissions: [Permissions.attendance.view] }) })
    expect(await screen.findByText('MissingOutPunch')).toBeInTheDocument()
    expect(screen.getByText('D1')).toBeInTheDocument()
    expect(screen.getByText('300 min')).toBeInTheDocument()
    expect(stub.callsTo('get', '/api/attendance/me/exceptions')).toHaveLength(1)
  })
})
