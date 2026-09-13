import { fireEvent, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { Permissions } from '../../auth/permissions.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { makeUser } from '../../test/fixtures.ts'
import { AttendanceReportPage, AttendanceReportsLandingPage } from './AttendanceReportsPage.tsx'

const apiMocks = vi.hoisted(() => ({
  daily: vi.fn(),
  monthly: vi.fn(),
  exceptions: vi.fn(),
  export: vi.fn(),
}))

vi.mock('../../api/attendance.ts', () => ({
  getDailyAttendanceReport: apiMocks.daily,
  getMonthlyAttendanceReport: apiMocks.monthly,
  getAttendanceExceptionReport: apiMocks.exceptions,
  exportAttendanceReport: apiMocks.export,
}))

const page = { page: 1, pageSize: 20, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false }
const dailyRow = { employeeId: 'employee-1', employeeCode: 'EMP-1', employeeName: 'Nadia Farrell', businessDate: '2026-09-10', status: 'Present', inTimeUtc: '2026-09-10T08:00:00Z', outTimeUtc: '2026-09-10T17:00:00Z', actualWorkMinutes: 480, expectedWorkMinutes: 480, isLateIn: false, isEarlyOut: false, shiftCode: 'DAY', department: 'Engineering', workLocation: 'HQ' }

beforeEach(() => {
  vi.clearAllMocks()
  apiMocks.daily.mockResolvedValue({ ...page, items: [dailyRow] })
  apiMocks.monthly.mockResolvedValue({ ...page, items: [{ periodId: 'period-1', year: 2026, month: 9, periodStatus: 'Closed', employeeCode: 'EMP-1', employeeName: 'Nadia Farrell', workingDays: 22, presentDays: 20, absentDays: 1, onLeaveDays: 1, onDutyDays: 0, incompleteDays: 0, notProcessedDays: 0, expectedWorkMinutes: 10560, actualWorkMinutes: 9600, exceptionCount: 0, sourceDataVersion: 3 }] })
  apiMocks.exceptions.mockResolvedValue({ ...page, items: [{ key: 'exception-1', periodId: 'period-1', employeeId: 'employee-1', businessDate: '2026-09-10', exceptionType: 'MissingOut', isBlocking: true, message: 'Missing out punch' }] })
  apiMocks.export.mockResolvedValue({ blob: new Blob(['EmployeeCode\nEMP-1'], { type: 'text/csv' }), filename: 'attendance.csv' })
})

describe('Attendance Phase 5F reports', () => {
  it('renders the landing links for report pages', () => {
    renderAsUser(<AttendanceReportsLandingPage />, { user: makeUser({ permissions: [Permissions.attendance.reportView] }) })
    expect(screen.getByRole('link', { name: /Daily Attendance/ })).toHaveAttribute('href', '/attendance/reports/daily')
    expect(screen.getByRole('link', { name: /Monthly Summary/ })).toHaveAttribute('href', '/attendance/reports/monthly')
  })

  it('renders authoritative daily rows and sends filters server-side', async () => {
    renderAsUser(<AttendanceReportPage kind="daily" />, { user: makeUser({ permissions: [Permissions.attendance.reportView] }) })
    expect(await screen.findByText('Nadia Farrell')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('From Date'), { target: { value: '2026-09-01' } })
    fireEvent.change(screen.getByLabelText('To Date'), { target: { value: '2026-09-30' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply Filters' }))
    await waitFor(() => expect(apiMocks.daily).toHaveBeenLastCalledWith(expect.objectContaining({ fromDate: '2026-09-01', toDate: '2026-09-30', page: 1, pageSize: 20 })))
  })

  it('renders monthly persisted metrics and only shows export with permission', async () => {
    renderAsUser(<AttendanceReportPage kind="monthly" />, { user: makeUser({ permissions: [Permissions.attendance.reportView] }) })
    expect(await screen.findByText('Closed')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Export CSV' })).not.toBeInTheDocument()
  })

  it('renders exceptions and requires the exception permission at the route boundary', async () => {
    renderAsUser(<AttendanceReportPage kind="exceptions" />, { user: makeUser({ permissions: [Permissions.attendance.reportView] }) })
    expect(await screen.findByText('Missing out punch')).toBeInTheDocument()
  })

  it('exports with the currently applied filters when export permission is granted', async () => {
    renderAsUser(<AttendanceReportPage kind="daily" />, { user: makeUser({ permissions: [Permissions.attendance.reportView, Permissions.attendance.reportExport] }) })
    await screen.findByText('Nadia Farrell')
    fireEvent.change(screen.getByLabelText('Employee ID'), { target: { value: 'employee-1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply Filters' }))
    await waitFor(() => expect(apiMocks.daily).toHaveBeenLastCalledWith(expect.objectContaining({ employeeId: 'employee-1' })))
    fireEvent.click(screen.getByRole('button', { name: 'Export CSV' }))
    await waitFor(() => expect(apiMocks.export).toHaveBeenCalledWith('daily', expect.objectContaining({ employeeId: 'employee-1' })))
  })
})
