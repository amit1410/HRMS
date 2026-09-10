import { screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { makeUser } from '../../test/fixtures.ts'
import { installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { TeamLeaveCalendarPage } from './TeamLeaveCalendarPage.tsx'

describe('TeamLeaveCalendarPage', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  it('loads the visible month and renders authorized events with readable details', async () => {
    stub.on('get', '/api/leave-calendar', () => ({ data: ok([{ requestId: 'request-1', employeeId: 'employee-1', employeeCode: 'EMP-1', employeeName: 'Nadia Farrell', leaveTypeCode: 'CL', leaveTypeName: 'Casual Leave', startDate: '2026-09-10', endDate: '2026-09-12', chargeableQuantity: 2, status: 'Approved' }]) }))
    renderAsUser(<TeamLeaveCalendarPage />, { user: makeUser() })
    expect((await screen.findAllByText('Nadia Farrell')).length).toBeGreaterThan(0)
    expect(screen.getAllByText('Casual Leave').length).toBeGreaterThan(0)
    expect(screen.getAllByRole('button', { name: /Nadia Farrell/ }).length).toBeGreaterThan(0)
  })

  it('shows an explicit empty state and requests another range when navigating', async () => {
    stub.on('get', '/api/leave-calendar', () => ({ data: ok([]) }))
    renderAsUser(<TeamLeaveCalendarPage />, { user: makeUser() })
    expect(await screen.findByText('No team leave this month')).toBeInTheDocument()
    expect(screen.getByText('There are no authorized team absences in the selected date range.')).toBeInTheDocument()
    expect(screen.getByText('Showing leave for your authorized team members only. Private leave request details are not exposed.')).toBeInTheDocument()
    await screen.getByRole('button', { name: 'Next month' }).click()
    await waitFor(() => expect(stub.calls.filter(call => call.method === 'get' && call.url === '/api/leave-calendar').length).toBeGreaterThan(1))
  })
})
