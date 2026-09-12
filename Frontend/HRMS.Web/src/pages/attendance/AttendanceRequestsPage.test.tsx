import { fireEvent, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AttendanceRequestsPage } from './AttendanceRequestsPage.tsx'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok } from '../../test/stubAdapter.ts'
import { makeUser } from '../../test/fixtures.ts'
import { Permissions } from '../../auth/permissions.ts'

const regularization = { id: 'r1', employeeId: 'e1', businessDate: '2026-09-10', requestType: 'MissingOutPunch', reason: 'Forgot out', status: 'Pending', submittedAtUtc: '2026-09-10T10:00:00Z', events: [] }
const onDuty = { id: 'o1', employeeId: 'e1', startDate: '2026-09-11', endDate: '2026-09-11', reason: 'Client visit', status: 'Pending', submittedAtUtc: '2026-09-10T10:00:00Z', events: [] }

describe('Attendance workflow requests', () => {
  let restore: (() => void) | undefined
  afterEach(() => { restore?.(); vi.restoreAllMocks() })

  function setup() {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/me/regularizations', () => ({ data: ok({ items: [regularization], page: 1, pageSize: 20, totalCount: 1 }) }))
    stub.on('get', '/api/attendance/me/on-duty', () => ({ data: ok({ items: [onDuty], page: 1, pageSize: 20, totalCount: 1 }) }))
    stub.on('get', '/api/attendance/manager/regularizations', () => ({ data: ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }) }))
    stub.on('get', '/api/attendance/manager/on-duty', () => ({ data: ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }) }))
    return stub
  }

  it('renders own pending Regularization and On Duty requests', async () => {
    setup(); renderAsUser(<AttendanceRequestsPage />)
    expect(await screen.findByText(/Forgot out/)).toBeInTheDocument()
    expect(screen.getByText(/Client visit/)).toBeInTheDocument()
  })

  it('submits Regularization through the server API', async () => {
    const stub = setup(); stub.on('post', '/api/attendance/me/regularizations', () => ({ data: ok(regularization) }))
    renderAsUser(<AttendanceRequestsPage />)
    fireEvent.change(screen.getAllByLabelText('Reason')[0]!, { target: { value: 'Forgot out' } })
    fireEvent.click(screen.getByRole('button', { name: 'Submit Regularization' }))
    expect((await screen.findAllByText('Regularization submitted.')).length).toBeGreaterThan(0)
  })

  it('cancels only pending own requests and refreshes state', async () => {
    const stub = setup(); vi.spyOn(window, 'confirm').mockReturnValue(true); stub.on('post', '/api/attendance/me/regularizations/r1/cancel', () => ({ data: ok({ ...regularization, status: 'Cancelled' }) }))
    renderAsUser(<AttendanceRequestsPage />, { user: makeUser({ permissions: [...makeUser().permissions, Permissions.attendance.regularizationApprove] }) })
    fireEvent.click((await screen.findAllByRole('button', { name: 'Cancel' }))[0]!)
    expect(await screen.findByText('Regularization cancelled.')).toBeInTheDocument()
    expect(stub.callsTo('post', '/api/attendance/me/regularizations/r1/cancel')).toHaveLength(1)
  })

  it('shows manager approval actions when pending queue has items', async () => {
    const stub = setup(); vi.spyOn(window, 'confirm').mockReturnValue(true); stub.on('get', '/api/attendance/manager/regularizations', () => ({ data: ok({ items: [regularization], page: 1, pageSize: 20, totalCount: 1 }) }))
    stub.on('post', '/api/attendance/manager/regularizations/r1/approve', () => ({ data: ok({ ...regularization, status: 'Approved' }) }))
    renderAsUser(<AttendanceRequestsPage />, { user: makeUser({ permissions: [...makeUser().permissions, Permissions.attendance.regularizationApprove] }) })
    fireEvent.click(await screen.findByRole('button', { name: 'Approve' }))
    expect(await screen.findByText('Regularization approved.')).toBeInTheDocument()
  })

  it('keeps the request page usable when workflow loading fails', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/me/regularizations', () => ({ status: 503, data: fail('Requests unavailable') }))
    stub.on('get', '/api/attendance/me/on-duty', () => ({ data: ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }) }))
    stub.on('get', '/api/attendance/manager/regularizations', () => ({ data: ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }) }))
    stub.on('get', '/api/attendance/manager/on-duty', () => ({ data: ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }) }))
    renderAsUser(<AttendanceRequestsPage />)
    expect(await screen.findByText('Requests unavailable')).toBeInTheDocument()
    expect(screen.getByText('Attendance Requests')).toBeInTheDocument()
  })

  it('validates the On Duty range and submits a valid request', async () => {
    const stub = setup()
    stub.on('post', '/api/attendance/me/on-duty', () => ({ data: ok(onDuty) }))
    renderAsUser(<AttendanceRequestsPage />)
    fireEvent.change(screen.getByLabelText('On Duty start date'), { target: { value: '2026-09-12' } })
    fireEvent.change(screen.getByLabelText('On Duty end date'), { target: { value: '2026-09-10' } })
    expect(screen.getByRole('button', { name: 'Submit On Duty' })).toBeDisabled()
    expect(screen.getByText('End date must be on or after start date.')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('On Duty end date'), { target: { value: '2026-09-12' } })
    fireEvent.change(screen.getByLabelText('On Duty reason'), { target: { value: 'Client visit' } })
    fireEvent.click(screen.getByRole('button', { name: 'Submit On Duty' }))
    expect(await screen.findByText('On Duty submitted.')).toBeInTheDocument()
  })

  it('opens request detail and renders a safe API error', async () => {
    const stub = setup()
    stub.on('get', '/api/attendance/me/regularizations/r1', () => ({ data: ok({ ...regularization, reviewerComments: 'Looks good', events: [{ eventType: 'Submitted', actorUserId: 'u1', occurredAtUtc: regularization.submittedAtUtc }] }) }))
    renderAsUser(<AttendanceRequestsPage />)
    fireEvent.click(await screen.findByRole('button', { name: '2026-09-10' }))
    expect(await screen.findByText('Looks good')).toBeInTheDocument()
    stub.on('get', '/api/attendance/me/regularizations/r1', () => ({ status: 404, data: fail('That record could not be found.') }))
    fireEvent.click(screen.getByRole('button', { name: 'Close request details' }))
    fireEvent.click(screen.getByRole('button', { name: '2026-09-10' }))
    expect(await screen.findByText('That record could not be found.')).toBeInTheDocument()
  })
})
