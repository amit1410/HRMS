import userEvent from '@testing-library/user-event'
import { fireEvent, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Route, Routes } from 'react-router-dom'
import * as leaveRequestsApi from '../../api/leaveRequests.ts'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser } from '../../test/fixtures.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { LeaveApprovalDetailPage, LeaveApprovalsPage } from './LeaveApprovalsPage.tsx'

const user = makeUser({ permissions: [Permissions.leave.approve] })
const item = {
  requestId: 'request-1', employeeId: 'employee-1', employeeCode: 'EMP-001', employeeName: 'Nadia Farrell',
  leaveTypeId: 'type-1', leaveTypeCode: 'CL', leaveTypeName: 'Casual Leave', startDate: '2026-12-10', endDate: '2026-12-11',
  requestedQuantity: 2, chargeableQuantity: 1.5, status: 'PendingApproval' as const, submittedAtUtc: '2026-12-01T10:00:00Z',
}
const detail = {
  ...item, leavePeriodId: 'period-1', leavePeriodCode: 'FY26', leavePeriodName: 'Financial Year 2026', leavePolicyVersionId: 'version-1',
  requestDays: [
    { date: '2026-12-10', requestedQuantity: 1, chargeableQuantity: 1, dayClassification: 'WorkingDay', calculationReason: 'Authoritative', isEmployeeRequested: true },
    { date: '2026-12-11', requestedQuantity: 1, chargeableQuantity: 0.5, dayClassification: 'WorkingDay', calculationReason: 'Authoritative', isEmployeeRequested: true },
  ],
  events: [
    { eventType: 'Submitted' as const, occurredAtUtc: '2026-12-01T10:00:00Z' },
  ],
}

describe('LeaveApprovalsPage', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => { stub.restore(); vi.restoreAllMocks() })

  it('renders actionable rows and paging data from the backend', async () => {
    stub.on('get', '/api/leave-approvals', () => ({ data: ok({ items: [item], page: 1, pageSize: 25, totalCount: 26, totalPages: 2, hasPreviousPage: false, hasNextPage: true }) }))
    renderAsUser(<LeaveApprovalsPage />, { user })
    expect(await screen.findByText('Nadia Farrell')).toBeInTheDocument()
    const row = screen.getByRole('row', { name: /Nadia Farrell/ })
    expect(within(row).getByText('Casual Leave')).toBeInTheDocument()
    expect(within(row).getByText('Pending Approval')).toBeInTheDocument()
    expect(within(row).getByText('1.5')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'View' })).toHaveAttribute('href', '/leave-management/approvals/request-1')
    expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled()
  })

  it('shows an empty state without treating it as an error', async () => {
    stub.on('get', '/api/leave-approvals', () => ({ data: ok({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }) }))
    renderAsUser(<LeaveApprovalsPage />, { user })
    expect(await screen.findByText('No pending leave requests require your approval.')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows a controlled list error', async () => {
    stub.on('get', '/api/leave-approvals', () => ({ status: 403, data: fail('You are not authorized to approve this request.') }))
    renderAsUser(<LeaveApprovalsPage />, { user })
    expect(await screen.findByText('You are not authorized to approve this request.')).toBeInTheDocument()
  })

  it('renders detail days, history, and both pending actions without a reason field', async () => {
    stub.on('get', '/api/leave-approvals/request-1', () => ({ data: ok(detail) }))
    renderAsUser(<Routes><Route path="/leave-management/approvals/:requestId" element={<LeaveApprovalDetailPage />} /></Routes>, { user, route: '/leave-management/approvals/request-1' })
    expect(await screen.findByText('Leave Approval Details')).toBeInTheDocument()
    expect(screen.getByText('Nadia Farrell')).toBeInTheDocument()
    expect(screen.getByRole('row', { name: /2026-12-10.*1.*1/ })).toBeInTheDocument()
    expect(screen.getByRole('row', { name: /2026-12-11.*1.*0.5/ })).toBeInTheDocument()
    const historySection = screen.getByRole('heading', { name: 'History' }).closest('section')
    expect(historySection).not.toBeNull()
    expect(within(historySection as HTMLElement).getByText(/Submitted —/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Approve' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reject' })).toBeInTheDocument()
    expect(screen.queryByLabelText(/reason|comment/i)).not.toBeInTheDocument()
  })

  it('approves once, disables the action while in flight, and returns to the refreshed inbox', async () => {
    const approvalResult = { requestId: 'request-1', status: 'Approved' as const, eventType: 'Approved' as const, occurredAtUtc: '2026-12-02T10:00:00Z' }
    let resolveApprove!: (result: typeof approvalResult) => void
    const approvePromise = new Promise<typeof approvalResult>(resolve => { resolveApprove = resolve })
    stub.on('get', '/api/leave-approvals/request-1', () => ({ data: ok(detail) }))
    stub.on('get', '/api/leave-approvals', () => ({ data: ok({ items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }) }))
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    const approveSpy = vi.spyOn(leaveRequestsApi, 'approveLeaveRequest').mockReturnValue(approvePromise)
    const userEventInstance = userEvent.setup()
    renderAsUser(<Routes><Route path="/leave-management/approvals" element={<LeaveApprovalsPage />} /><Route path="/leave-management/approvals/:requestId" element={<LeaveApprovalDetailPage />} /></Routes>, { user, route: '/leave-management/approvals/request-1' })
    await userEventInstance.click(await screen.findByRole('button', { name: 'Approve' }))
    const approvingButton = await screen.findByRole('button', { name: 'Approving…' })
    expect(approvingButton).toBeDisabled()
    fireEvent.click(approvingButton)
    expect(approveSpy).toHaveBeenCalledTimes(1)
    resolveApprove(approvalResult)
    expect(await screen.findByText('No pending leave requests require your approval.')).toBeInTheDocument()
  })

  it('maps stale transition and permission errors without retrying', async () => {
    stub.on('get', '/api/leave-approvals/request-1', () => ({ data: ok(detail) }))
    stub.on('post', '/api/leave-requests/request-1/reject', () => ({ status: 409, data: fail('InvalidStatusTransition: already processed') }))
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    renderAsUser(<Routes><Route path="/leave-management/approvals/:requestId" element={<LeaveApprovalDetailPage />} /></Routes>, { user, route: '/leave-management/approvals/request-1' })
    await userEvent.setup().click(await screen.findByRole('button', { name: 'Reject' }))
    expect(await screen.findByText('This leave request has already been processed or is no longer pending.')).toBeInTheDocument()
    expect(stub.calls.filter(call => call.url === '/api/leave-requests/request-1/reject')).toHaveLength(1)
  })

  it('maps missing Allocated reservation without optimistically changing status', async () => {
    stub.on('get', '/api/leave-approvals/request-1', () => ({ data: ok(detail) }))
    stub.on('post', '/api/leave-requests/request-1/approve', () => ({ status: 409, data: fail('AllocatedReservationNotFound: reservation history is missing') }))
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    renderAsUser(<Routes><Route path="/leave-management/approvals/:requestId" element={<LeaveApprovalDetailPage />} /></Routes>, { user, route: '/leave-management/approvals/request-1' })
    await userEvent.setup().click(await screen.findByRole('button', { name: 'Approve' }))
    expect(await screen.findByText('This leave request does not have an authoritative reserved balance and cannot be processed. Please contact HR.')).toBeInTheDocument()
    expect(screen.getByText('Pending Approval')).toBeInTheDocument()
    expect(screen.queryByText('Approved')).not.toBeInTheDocument()
  })
})
