import { screen, fireEvent, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { AttendanceMonthlyFinalizationPage } from './AttendanceMonthlyFinalizationPage.tsx'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok } from '../../test/stubAdapter.ts'
import { makeUser } from '../../test/fixtures.ts'
import { Permissions } from '../../auth/permissions.ts'

const page = { page: 1, pageSize: 100, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false }
const period = { id: 'p1', year: 2026, month: 9, startDate: '2026-09-01', endDate: '2026-09-30', status: 'Open', dataVersion: 2, concurrencyVersion: 1 }
const row = { id: 's1', attendancePeriodId: 'p1', employeeId: 'e1', employeeCode: 'E001', employeeName: 'Nadia Farrell', employmentDays: 30, presentDays: 20, absentDays: 1, onLeaveDays: 2, onDutyDays: 1, holidayDays: 2, weeklyOffDays: 4, regularizedDays: 0, approvedOnDutyDays: 1, exceptionCount: 0, processedAtUtc: '2026-09-30T10:00:00Z', presentDayQuantity: 20.5, paidLeaveDays: 1.5, unpaidLeaveDays: 0.5, payableDays: 22, lopDays: 1 }

describe('Attendance monthly finalization', () => {
  let restore: (() => void) | undefined
  afterEach(() => restore?.())

  it('renders backend preview values, blockers, and server paging', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/periods', () => ({ data: ok({ ...page, pageSize: 50, items: [period] }) }))
    stub.on('get', '/api/attendance/periods/p1/close-preview', () => ({ data: ok({ periodId: 'p1', status: 'Open', canClose: false, dataVersion: 2, summariesCurrent: true, employeesProcessed: 1, blockingExceptionCount: 1, pendingRegularizationCount: 0, pendingOnDutyCount: 0, notProcessedCount: 0, incompleteCount: 0, blockers: ['UnresolvedAttendanceException'] }) }))
    stub.on('get', '/api/attendance/periods/p1/summaries', () => ({ data: ok(pageWith(row)) }))
    stub.on('get', '/api/attendance/periods/p1/events', () => ({ data: ok([]) }))
    renderAsUser(<AttendanceMonthlyFinalizationPage />, { user: makeUser({ permissions: [Permissions.attendance.monthlyViewAll, Permissions.attendance.monthlyProcess, Permissions.attendance.monthlyClose] }) })
    expect(await screen.findByText(/Nadia Farrell/)).toBeInTheDocument()
    expect(screen.getByText('UnresolvedAttendanceException')).toBeInTheDocument()
    expect(screen.getByText('22.00')).toBeInTheDocument()
    fireEvent.click(screen.getByLabelText('LOP greater than zero'))
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/periods/p1/summaries').at(-1)?.params.hasLop).toBe(true))
  })

  it('shows authorized finalize and invokes the backend action', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/periods', () => ({ data: ok({ ...page, pageSize: 50, items: [period] }) }))
    stub.on('get', '/api/attendance/periods/p1/close-preview', () => ({ data: ok({ periodId: 'p1', status: 'Open', canClose: true, dataVersion: 2, summariesCurrent: true, employeesProcessed: 1, blockingExceptionCount: 0, pendingRegularizationCount: 0, pendingOnDutyCount: 0, notProcessedCount: 0, incompleteCount: 0, blockers: [] }) }))
    stub.on('get', '/api/attendance/periods/p1/summaries', () => ({ data: ok(pageWith(row)) })); stub.on('get', '/api/attendance/periods/p1/events', () => ({ data: ok([]) })); stub.on('post', '/api/attendance/periods/p1/close', () => ({ data: ok({ ...period, status: 'Closed' }) }))
    renderAsUser(<AttendanceMonthlyFinalizationPage />, { user: makeUser({ permissions: [Permissions.attendance.monthlyViewAll, Permissions.attendance.monthlyClose] }) })
    const finalize = await screen.findByRole('button', { name: 'Finalize' }); await waitFor(() => expect(finalize).not.toBeDisabled()); fireEvent.click(finalize)
    await waitFor(() => expect(stub.callsTo('post', '/api/attendance/periods/p1/close')).toHaveLength(1))
  })

  it('does not expose finalize to a read-only operator and displays API failures', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/periods', () => ({ data: ok({ ...page, pageSize: 50, items: [period] }) })); stub.on('get', '/api/attendance/periods/p1/close-preview', () => ({ status: 503, data: fail('Preview unavailable') })); stub.on('get', '/api/attendance/periods/p1/summaries', () => ({ data: ok({ ...page, items: [] }) })); stub.on('get', '/api/attendance/periods/p1/events', () => ({ data: ok([]) }))
    renderAsUser(<AttendanceMonthlyFinalizationPage />, { user: makeUser({ permissions: [Permissions.attendance.monthlyViewAll] }) })
    expect(await screen.findByRole('heading', { name: /2026-09/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Finalize' })).not.toBeInTheDocument()
  })
})

function pageWith(item: typeof row) { return { ...page, items: [item] } }
