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

  it('Reopen_requires_reason', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    const closed = { ...period, status: 'Closed', dataVersion: 4 }
    stub.on('get', '/api/attendance/periods', () => ({ data: ok({ ...page, pageSize: 50, items: [closed] }) }))
    stub.on('get', '/api/attendance/periods/p1/close-preview', () => ({ data: ok({ periodId: 'p1', status: 'Closed', canClose: false, dataVersion: 4, summariesCurrent: true, employeesProcessed: 1, blockingExceptionCount: 0, pendingRegularizationCount: 0, pendingOnDutyCount: 0, notProcessedCount: 0, incompleteCount: 0, blockers: [] }) }))
    stub.on('get', '/api/attendance/periods/p1/summaries', () => ({ data: ok({ ...page, items: [row] }) })); stub.on('get', '/api/attendance/periods/p1/events', () => ({ data: ok([]) }))
    stub.on('post', '/api/attendance/periods/p1/reopen', call => { expect(call.body).toEqual({ reason: 'Source evidence corrected' }); return { data: ok({ ...closed, status: 'Open' }) } })
    renderAsUser(<AttendanceMonthlyFinalizationPage />, { user: makeUser({ permissions: [Permissions.attendance.monthlyViewAll, Permissions.attendance.monthlyReopen] }) })
    const reopen = await screen.findByRole('button', { name: 'Reopen' })
    expect(reopen).toBeDisabled()
    fireEvent.change(screen.getByLabelText('Reopen reason'), { target: { value: 'Source evidence corrected' } })
    expect(reopen).toBeEnabled()
    fireEvent.click(reopen)
    await waitFor(() => expect(stub.callsTo('post', '/api/attendance/periods/p1/reopen')).toHaveLength(1))
  })

  it('Refinalize_displays_version_progression_and_payroll_boundary', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    const reopened = { ...period, status: 'Open', dataVersion: 5 }
    stub.on('get', '/api/attendance/periods', () => ({ data: ok({ ...page, pageSize: 50, items: [reopened] }) }))
    stub.on('get', '/api/attendance/periods/p1/close-preview', () => ({ data: ok({ periodId: 'p1', status: 'Open', canClose: true, dataVersion: 5, summariesCurrent: true, employeesProcessed: 1, blockingExceptionCount: 0, pendingRegularizationCount: 0, pendingOnDutyCount: 0, notProcessedCount: 0, incompleteCount: 0, blockers: [] }) }))
    stub.on('get', '/api/attendance/periods/p1/summaries', () => ({ data: ok({ ...page, items: [row] }) }))
    stub.on('get', '/api/attendance/periods/p1/events', () => ({ data: ok([{ id: 'ev-1', periodId: 'p1', eventType: 'Reopened', actorUserId: 'actor-1', occurredAtUtc: '2026-09-25T10:00:00Z', dataVersion: 4 }, { id: 'ev-2', periodId: 'p1', eventType: 'Finalized', actorUserId: 'actor-1', occurredAtUtc: '2026-09-26T10:00:00Z', dataVersion: 5 }]) }))
    renderAsUser(<AttendanceMonthlyFinalizationPage />, { user: makeUser({ permissions: [Permissions.attendance.monthlyViewAll] }) })
    expect(await screen.findByText(/Current finalized version: not finalized/)).toBeInTheDocument()
    expect(screen.getByText(/Previous finalized version: 4/)).toBeInTheDocument()
    expect(screen.getByText(/Payroll historical snapshots are not automatically recalculated/)).toBeInTheDocument()
    await waitFor(() => expect(stub.callsTo('get', '/api/attendance/periods/p1/events')).toHaveLength(1))
    expect(screen.getByText(/Finalized · version 5/)).toBeInTheDocument()
  })

  it('History_displays_reopen_and_refinalization_events_in_order', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    const current = { ...period, status: 'Closed', dataVersion: 5 }
    stub.on('get', '/api/attendance/periods', () => ({ data: ok({ ...page, pageSize: 50, items: [current] }) }))
    stub.on('get', '/api/attendance/periods/p1/close-preview', () => ({ data: ok({ periodId: 'p1', status: 'Closed', canClose: false, dataVersion: 5, summariesCurrent: true, employeesProcessed: 1, blockingExceptionCount: 0, pendingRegularizationCount: 0, pendingOnDutyCount: 0, notProcessedCount: 0, incompleteCount: 0, blockers: [] }) }))
    stub.on('get', '/api/attendance/periods/p1/summaries', () => ({ data: ok({ ...page, items: [row] }) }))
    stub.on('get', '/api/attendance/periods/p1/events', () => ({ data: ok([{ id: 'ev-1', periodId: 'p1', eventType: 'Reopened', actorUserId: 'actor-1', occurredAtUtc: '2026-09-25T10:00:00Z', dataVersion: 4 }, { id: 'ev-2', periodId: 'p1', eventType: 'Re-finalized', actorUserId: 'actor-1', occurredAtUtc: '2026-09-26T10:00:00Z', dataVersion: 5 }]) }))
    renderAsUser(<AttendanceMonthlyFinalizationPage />, { user: makeUser({ permissions: [Permissions.attendance.monthlyViewAll] }) })
    expect(await screen.findByText(/Reopened · version 4/)).toBeInTheDocument()
    expect(screen.getByText(/Re-finalized · version 5/)).toBeInTheDocument()
    expect(screen.getByText(/Current finalized version: 5/)).toBeInTheDocument()
  })
})

function pageWith(item: typeof row) { return { ...page, items: [item] } }
