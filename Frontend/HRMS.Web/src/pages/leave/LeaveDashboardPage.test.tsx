import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { LeaveDashboardPage } from './LeaveDashboardPage.tsx'

const linkedIdentity = { status: 'Linked' as const, revision: '1', linkId: 'link-1', employee: { id: 'employee-1', displayName: 'Priya Raman', employeeCode: 'EMP-1' }, employmentEligibility: 'ActiveEmployment' as const, businessDate: '2026-09-10' }
const emptyPage = { items: [], page: 1, pageSize: 25, totalCount: 0, totalPages: 0, hasPreviousPage: false, hasNextPage: false }

describe('LeaveDashboardPage', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  function seed() {
    stub.on('get', '/api/leave-balances/mine', () => ({ data: ok([{ leaveTypeCode: 'AL', leaveTypeName: 'Annual Leave', entitlementMode: 'Allocated', leavePeriodName: 'FY 2026', grantedQuantity: 20, reservedQuantity: 2, consumedQuantity: 5, availableQuantity: 13 }]) }))
    stub.on('get', '/api/leave-requests', () => ({ data: ok({ ...emptyPage, items: [{ requestId: 'request-1', leaveTypeId: 'type-1', leaveTypeCode: 'AL', leaveTypeName: 'Annual Leave', startDate: '2099-04-01', endDate: '2099-04-03', requestedQuantity: 3, chargeableQuantity: 3, status: 'Approved', submittedAtUtc: '2099-01-01T00:00:00Z', leavePeriodId: 'period-1', leavePolicyVersionId: 'version-1' }, { requestId: 'request-2', leaveTypeId: 'type-1', leaveTypeCode: 'AL', leaveTypeName: 'Annual Leave', startDate: '2099-05-01', endDate: '2099-05-02', requestedQuantity: 2, chargeableQuantity: 2, status: 'PendingApproval', submittedAtUtc: '2099-02-01T00:00:00Z', leavePeriodId: 'period-1', leavePolicyVersionId: 'version-1' }], totalCount: 2, totalPages: 1 }) }))
    stub.on('get', '/api/leave-approvals', () => ({ data: ok({ ...emptyPage, pageSize: 5, items: [{ requestId: 'request-3', employeeId: 'employee-2', employeeCode: 'EMP-2', employeeName: 'Nadia Farrell', leaveTypeId: 'type-1', leaveTypeCode: 'AL', leaveTypeName: 'Annual Leave', startDate: '2099-06-01', endDate: '2099-06-02', requestedQuantity: 2, chargeableQuantity: 2, status: 'PendingApproval', submittedAtUtc: '2099-02-02T00:00:00Z' }], totalCount: 1, totalPages: 1 }) }))
    stub.on('get', '/api/leave-calendar', () => ({ data: ok([{ requestId: 'request-4', employeeId: 'employee-2', employeeCode: 'EMP-2', employeeName: 'Nadia Farrell', leaveTypeCode: 'AL', leaveTypeName: 'Annual Leave', startDate: '2099-06-01', endDate: '2099-06-02', chargeableQuantity: 2, status: 'Approved' }]) }))
  }

  it('renders employee balances, request summaries, and workflow shortcuts', async () => {
    seed()
    renderAsUser(<LeaveDashboardPage />, { user: makeUser({ employeeIdentity: linkedIdentity }) })
    expect((await screen.findAllByText('Annual Leave')).length).toBeGreaterThan(0)
    expect(screen.getByText('13 available')).toBeInTheDocument()
    expect(screen.getByText('Pending requests')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Apply Leave/ })).toHaveAttribute('href', '/leave-management/apply')
    expect(screen.getByRole('link', { name: /My Leave Requests/ })).toHaveAttribute('href', '/leave-management/my-requests')
  })

  it('shows manager-only summaries only with Leave approval permission', async () => {
    seed()
    const user = makeUser({ employeeIdentity: linkedIdentity, permissions: [Permissions.leave.approve] })
    renderAsUser(<LeaveDashboardPage />, { user })
    expect(await screen.findByText('Manager approvals')).toBeInTheDocument()
    expect(screen.getByText('Approvals waiting')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Manager Approvals/ })).toHaveAttribute('href', '/leave-management/approvals')
  })

  it('renders Unlimited without a numeric balance alongside finite balances', async () => {
    seed()
    stub.on('get', '/api/leave-balances/mine', () => ({ data: ok([{ leaveTypeCode: 'AL', leaveTypeName: 'Annual Leave', entitlementMode: 'Allocated', leavePeriodName: 'FY 2026', grantedQuantity: 20, reservedQuantity: 2, consumedQuantity: 5, availableQuantity: 13 }, { leaveTypeCode: 'SL', leaveTypeName: 'Sick Leave', entitlementMode: 'Unlimited' }]) }))
    renderAsUser(<LeaveDashboardPage />, { user: makeUser({ employeeIdentity: linkedIdentity }) })
    expect(await screen.findByText('Unlimited')).toBeInTheDocument()
    expect(screen.getByText('13 available')).toBeInTheDocument()
    expect(screen.queryByText('0 available')).not.toBeInTheDocument()
  })

  it('does not request or render meaningless summaries without an employee link', () => {
    renderAsUser(<LeaveDashboardPage />)
    expect(screen.getByText('Leave access needs an employee link')).toBeInTheDocument()
    expect(stub.calls).toHaveLength(0)
  })
})
