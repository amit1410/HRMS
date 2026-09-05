import { screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Route, Routes } from 'react-router-dom'
import { MyLeaveRequestDetailPage, MyLeaveRequestsPage } from './MyLeaveRequestsPage.tsx'
import { makeUser } from '../../test/fixtures.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import * as leaveRequestsApi from '../../api/leaveRequests.ts'

const request = {
  requestId: 'request-1',
  leaveTypeId: 'type-1',
  leaveTypeCode: 'CL',
  leaveTypeName: 'Casual Leave',
  startDate: '2026-10-05',
  endDate: '2026-10-06',
  requestedQuantity: 2,
  chargeableQuantity: 2,
  status: 'PendingApproval' as const,
  submittedAtUtc: '2026-10-01T10:00:00Z',
  leavePeriodId: 'period-1',
  leavePolicyVersionId: 'version-1',
}

const detail = {
  ...request,
  leavePeriodCode: 'FY26',
  leavePeriodName: 'Financial Year 2026',
  requestDays: [
    { date: '2026-10-05', requestedQuantity: 1, chargeableQuantity: 1, dayClassification: null, calculationReason: 'Employee requested', isEmployeeRequested: true },
    { date: '2026-10-06', requestedQuantity: 1, chargeableQuantity: 1, dayClassification: null, calculationReason: 'Employee requested', isEmployeeRequested: true },
  ],
  events: [
    { eventType: 'Created' as const, occurredAtUtc: '2026-10-01T09:59:00Z' },
    { eventType: 'Submitted' as const, occurredAtUtc: '2026-10-01T10:00:00Z' },
  ],
}

describe('MyLeaveRequestsPage', () => {
  let stub: StubAdapter

  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => { stub.restore(); vi.restoreAllMocks() })

  it('renders a populated list with authoritative status and quantities', async () => {
    stub.on('get', '/api/leave-requests', () => ({ data: ok({ items: [request], page: 1, pageSize: 25, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false }) }))
    renderAsUser(<MyLeaveRequestsPage />, { user: makeUser() })
    expect(await screen.findByText('Casual Leave')).toBeInTheDocument()
    expect(screen.getByText('Pending Approval')).toBeInTheDocument()
    expect(screen.getByText('View Details')).toBeInTheDocument()
    const row = screen.getByRole('row', { name: /Casual Leave/ })
    expect(within(row).getAllByText('2')).toHaveLength(2)
  })

  it('renders an empty state', async () => {
    stub.on('get', '/api/leave-requests', () => ({ data: ok({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }) }))
    renderAsUser(<MyLeaveRequestsPage />, { user: makeUser() })
    expect(await screen.findByText('You have no leave requests yet.')).toBeInTheDocument()
  })

  it('renders an API error state', async () => {
    stub.on('get', '/api/leave-requests', () => ({ status: 500, data: fail('Unable to load leave requests.') }))
    renderAsUser(<MyLeaveRequestsPage />, { user: makeUser() })
    expect(await screen.findByText('Unable to load leave requests.')).toBeInTheDocument()
  })

  it('renders detail days, persisted history, and the PendingApproval withdraw action', async () => {
    stub.on('get', '/api/leave-requests/request-1', () => ({ data: ok(detail) }))
    renderAsUser(
      <Routes><Route path="/leave-management/my-requests/:requestId" element={<MyLeaveRequestDetailPage />} /></Routes>,
      { user: makeUser(), route: '/leave-management/my-requests/request-1' },
    )
    expect(await screen.findByText('Leave Request Details')).toBeInTheDocument()
    expect(screen.getByRole('row', { name: /2026-10-05.*1.*1/ })).toBeInTheDocument()
    expect(screen.getByRole('row', { name: /2026-10-06.*1.*1/ })).toBeInTheDocument()
    expect(screen.getByText('Submitted')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Withdraw' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
  })

  it.each(['Approved', 'Rejected', 'Withdrawn', 'Cancelled'] as const)('hides Withdraw for %s requests', async status => {
    stub.on('get', '/api/leave-requests/request-1', () => ({ data: ok({ ...detail, status }) }))
    renderAsUser(
      <Routes><Route path="/leave-management/my-requests/:requestId" element={<MyLeaveRequestDetailPage />} /></Routes>,
      { user: makeUser(), route: '/leave-management/my-requests/request-1' },
    )
    expect(await screen.findByText(status)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Withdraw' })).not.toBeInTheDocument()
  })

  it('shows Cancel and hides Withdraw for Approved requests', async () => {
    stub.on('get', '/api/leave-requests/request-1', () => ({ data: ok({ ...detail, status: 'Approved' }) }))
    renderAsUser(<Routes><Route path="/leave-management/my-requests/:requestId" element={<MyLeaveRequestDetailPage />} /></Routes>, { user: makeUser(), route: '/leave-management/my-requests/request-1' })
    expect(await screen.findByRole('button', { name: 'Cancel' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Withdraw' })).not.toBeInTheDocument()
  })

  it.each(['PendingApproval', 'Rejected', 'Withdrawn', 'Cancelled'] as const)('hides Cancel for %s requests', async status => {
    stub.on('get', '/api/leave-requests/request-1', () => ({ data: ok({ ...detail, status }) }))
    renderAsUser(<Routes><Route path="/leave-management/my-requests/:requestId" element={<MyLeaveRequestDetailPage />} /></Routes>, { user: makeUser(), route: '/leave-management/my-requests/request-1' })
    expect(await screen.findByText(status === 'PendingApproval' ? 'Pending Approval' : status)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
  })

  it('confirms cancellation, prevents duplicate clicks, and refreshes authoritative detail', async () => {
    const user = (await import('@testing-library/user-event')).default.setup()
    let resolveCancellation!: (value: { requestId: string; status: 'Cancelled'; eventType: 'Cancelled'; occurredAtUtc: string }) => void
    const cancellation = new Promise<{ requestId: string; status: 'Cancelled'; eventType: 'Cancelled'; occurredAtUtc: string }>(resolve => { resolveCancellation = resolve })
    const cancelSpy = vi.spyOn(leaveRequestsApi, 'cancelLeaveRequest').mockReturnValue(cancellation)
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)
    let detailRead = 0
    const approvedDetail = { ...detail, status: 'Approved' as const, events: [...detail.events, { eventType: 'Approved' as const, occurredAtUtc: '2026-10-01T10:02:00Z' }] }
    const cancelledDetail = { ...approvedDetail, status: 'Cancelled' as const, events: [...approvedDetail.events, { eventType: 'Cancelled' as const, occurredAtUtc: '2026-10-01T10:03:00Z' }] }
    stub.on('get', '/api/leave-requests/request-1', () => ({ data: ok(detailRead++ === 0 ? approvedDetail : cancelledDetail) }))
    renderAsUser(<Routes><Route path="/leave-management/my-requests/:requestId" element={<MyLeaveRequestDetailPage />} /></Routes>, { user: makeUser(), route: '/leave-management/my-requests/request-1' })
    await user.click(await screen.findByRole('button', { name: 'Cancel' }))
    expect(confirmSpy).toHaveBeenCalledWith('Are you sure you want to cancel this approved leave request?')
    expect(screen.getByRole('button', { name: 'Cancelling…' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Cancelling…' }))
    expect(cancelSpy).toHaveBeenCalledTimes(1)
    resolveCancellation({ requestId: 'request-1', status: 'Cancelled', eventType: 'Cancelled', occurredAtUtc: '2026-10-01T10:03:00Z' })
    expect(await screen.findByText('Leave request cancelled successfully.')).toBeInTheDocument()
    expect(await screen.findByText('Cancelled')).toBeInTheDocument()
    const historySection = screen.getByRole('heading', { name: 'History' }).closest('section') as HTMLElement /*
    expect(within(historySection).getByText(/Cancelled\\s+—/)).toBeInTheDocument() /*
    expect(screen.getByText(/Cancelled â€”/)).toBeInTheDocument()
    */ expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
    expect(within(historySection).getByText(/Cancelled\s+\u2014/)).toBeInTheDocument()
    cancelSpy.mockRestore()
    confirmSpy.mockRestore()
  })

  it('does not call the cancellation API when confirmation is canceled', async () => {
    const user = (await import('@testing-library/user-event')).default.setup()
    const cancelSpy = vi.spyOn(leaveRequestsApi, 'cancelLeaveRequest')
    vi.spyOn(window, 'confirm').mockReturnValue(false)
    stub.on('get', '/api/leave-requests/request-1', () => ({ data: ok({ ...detail, status: 'Approved' }) }))
    renderAsUser(<Routes><Route path="/leave-management/my-requests/:requestId" element={<MyLeaveRequestDetailPage />} /></Routes>, { user: makeUser(), route: '/leave-management/my-requests/request-1' })
    await user.click(await screen.findByRole('button', { name: 'Cancel' }))
    expect(cancelSpy).not.toHaveBeenCalled()
    vi.restoreAllMocks()
  })

  it.each([
    ['CancellationNotAllowed: policy', 'This leave request cannot be cancelled under its leave policy.'],
    ['AllocatedConsumptionNotFound: history', 'This approved leave request does not have authoritative consumed-balance history and cannot be cancelled. Please contact HR.'],
    ['InvalidStatusTransition: already processed', 'This leave request has already been processed and can no longer be cancelled.'],
    ['ConcurrencyConflict: changed', 'The request changed while you were reviewing it. Refresh and try again.'],
    ['not found', 'This leave request is no longer available.'],
  ])('shows controlled cancellation errors (%s)', async (message, expected) => {
    const user = (await import('@testing-library/user-event')).default.setup()
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    const error = new (await import('../../api/errors.ts')).ApiError(message, { status: message === 'not found' ? 404 : 409 })
    vi.spyOn(leaveRequestsApi, 'cancelLeaveRequest').mockRejectedValue(error)
    stub.on('get', '/api/leave-requests/request-1', () => ({ data: ok({ ...detail, status: 'Approved' }) }))
    renderAsUser(<Routes><Route path="/leave-management/my-requests/:requestId" element={<MyLeaveRequestDetailPage />} /></Routes>, { user: makeUser(), route: '/leave-management/my-requests/request-1' })
    await user.click(await screen.findByRole('button', { name: 'Cancel' }))
    expect(await screen.findByText(expected)).toBeInTheDocument()
    vi.restoreAllMocks()
  })

  it('confirms withdrawal, prevents duplicate clicks, and refreshes authoritative detail', async () => {
    const user = (await import('@testing-library/user-event')).default.setup()
    let resolveWithdrawal!: (value: { requestId: string; status: 'Withdrawn'; eventType: 'Withdrawn'; occurredAtUtc: string }) => void
    const withdrawal = new Promise<{ requestId: string; status: 'Withdrawn'; eventType: 'Withdrawn'; occurredAtUtc: string }>(resolve => { resolveWithdrawal = resolve })
    const withdrawSpy = vi.spyOn(leaveRequestsApi, 'withdrawLeaveRequest').mockReturnValue(withdrawal)
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(true)
    let detailRead = 0
    stub.on('get', '/api/leave-requests/request-1', () => ({ data: ok(detailRead++ === 0 ? detail : { ...detail, status: 'Withdrawn', events: [...detail.events, { eventType: 'Withdrawn' as const, occurredAtUtc: '2026-10-01T10:01:00Z' }] }) }))
    renderAsUser(
      <Routes><Route path="/leave-management/my-requests/:requestId" element={<MyLeaveRequestDetailPage />} /></Routes>,
      { user: makeUser(), route: '/leave-management/my-requests/request-1' },
    )
    await user.click(await screen.findByRole('button', { name: 'Withdraw' }))
    expect(confirmSpy).toHaveBeenCalledWith('Are you sure you want to withdraw this leave request?')
    expect(screen.getByRole('button', { name: 'Withdrawing…' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Withdrawing…' }))
    expect(withdrawSpy).toHaveBeenCalledTimes(1)
    resolveWithdrawal({ requestId: 'request-1', status: 'Withdrawn', eventType: 'Withdrawn', occurredAtUtc: '2026-10-01T10:01:00Z' })
    expect(await screen.findByText('Leave request withdrawn successfully.')).toBeInTheDocument()
    expect(await screen.findByText('Withdrawn')).toBeInTheDocument()
    expect(screen.getByText(/Withdrawn —/)).toBeInTheDocument()
    withdrawSpy.mockRestore()
    confirmSpy.mockRestore()
  })

  it('does not call the API when confirmation is canceled', async () => {
    const user = (await import('@testing-library/user-event')).default.setup()
    const withdrawSpy = vi.spyOn(leaveRequestsApi, 'withdrawLeaveRequest')
    vi.spyOn(window, 'confirm').mockReturnValue(false)
    stub.on('get', '/api/leave-requests/request-1', () => ({ data: ok(detail) }))
    renderAsUser(<Routes><Route path="/leave-management/my-requests/:requestId" element={<MyLeaveRequestDetailPage />} /></Routes>, { user: makeUser(), route: '/leave-management/my-requests/request-1' })
    await user.click(await screen.findByRole('button', { name: 'Withdraw' }))
    expect(withdrawSpy).not.toHaveBeenCalled()
    vi.restoreAllMocks()
  })

  it.each([
    ['AllocatedReservationNotFound: balance', 'This leave request does not have an authoritative reserved balance and cannot be withdrawn. Please contact HR.'],
    ['InvalidStatusTransition: already processed', 'This leave request has already been processed and can no longer be withdrawn.'],
    ['ConcurrencyConflict: changed', 'The request changed while you were reviewing it. Refresh and try again.'],
    ['not used', 'This leave request is no longer available.'],
  ])('shows controlled withdraw errors (%s)', async (message, expected) => {
    const user = (await import('@testing-library/user-event')).default.setup()
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    const error = new (await import('../../api/errors.ts')).ApiError(message, { status: message === 'not used' ? 404 : 409 })
    vi.spyOn(leaveRequestsApi, 'withdrawLeaveRequest').mockRejectedValue(error)
    stub.on('get', '/api/leave-requests/request-1', () => ({ data: ok(detail) }))
    renderAsUser(<Routes><Route path="/leave-management/my-requests/:requestId" element={<MyLeaveRequestDetailPage />} /></Routes>, { user: makeUser(), route: '/leave-management/my-requests/request-1' })
    await user.click(await screen.findByRole('button', { name: 'Withdraw' }))
    expect(await screen.findByText(expected)).toBeInTheDocument()
    vi.restoreAllMocks()
  })
})
