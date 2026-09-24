import { screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { MyMonthlyAttendancePage } from './MyMonthlyAttendancePage.tsx'
import { renderAsUser } from '../../test/renderWith.tsx'
import { installStubAdapter, ok } from '../../test/stubAdapter.ts'
import { makeUser } from '../../test/fixtures.ts'
import { Permissions } from '../../auth/permissions.ts'

describe('My monthly Attendance', () => {
  let restore: (() => void) | undefined
  afterEach(() => restore?.())
  it('renders only the server-provided employee summary', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/my/monthly-summary', () => ({ data: ok({ page: 1, pageSize: 50, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false, items: [{ id: 's1', processedAtUtc: '2026-09-30T10:00:00Z', presentDayQuantity: 20.5, paidLeaveDays: 1.5, unpaidLeaveDays: 0.5, holidayDays: 2, weeklyOffDays: 4, onDutyDays: 1, absentDays: 1, lopDays: 1, payableDays: 22, version: 3 }] }) }))
    renderAsUser(<MyMonthlyAttendancePage />, { user: makeUser({ permissions: [Permissions.attendance.monthlyViewSelf] }) })
    expect(await screen.findByText('20.50')).toBeInTheDocument(); expect(screen.getByText('22.00')).toBeInTheDocument(); expect(screen.getByText('3')).toBeInTheDocument()
  })
})
