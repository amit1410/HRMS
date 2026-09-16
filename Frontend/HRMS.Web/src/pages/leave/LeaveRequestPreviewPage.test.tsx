import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LeaveRequestPreviewPage } from './LeaveRequestPreviewPage.tsx'
import { makeUser } from '../../test/fixtures.ts'
import { Permissions } from '../../auth/permissions.ts'
import { installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'

describe('LeaveRequestPreviewPage', () => {
  let stub: StubAdapter
  const employee = makeUser({ permissions: [Permissions.leave.requestCreate, Permissions.leave.typeViewAvailable], employeeIdentity: {
    status: 'Linked', revision: null, linkId: 'link-1', employmentEligibility: 'ActiveEmployment', businessDate: '2027-01-01',
    employee: { id: 'employee-1', displayName: 'Rahul Sharma', employeeCode: 'EMP-1' },
  } })
  const leaveType = { id: 'leave-1', code: 'AL', name: 'Annual Leave', description: null, defaultUnit: 'Day' as const, isPaid: true, isActive: true, createdDate: '2027-01-01T00:00:00Z', modifiedDate: null, concurrencyToken: 'v1' }

  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  it('loads self-service leave types without the administration policy endpoint', async () => {
    stub.on('get', '/api/leave-types/available', () => ({ data: ok({ items: [leaveType], page: 1, pageSize: 100, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false }) }))
    renderAsUser(<LeaveRequestPreviewPage />, { user: employee })

    expect(await screen.findByRole('option', { name: /AL/ })).toBeInTheDocument()
    expect(stub.callsTo('get', '/api/leave-types/available')).toHaveLength(1)
    expect(stub.callsTo('get', '/api/leave-types')).toHaveLength(0)
  })

  it('previews using the linked employee context', async () => {
    stub.on('get', '/api/leave-types/available', () => ({ data: ok({ items: [leaveType], page: 1, pageSize: 100, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false }) }))
    stub.on('post', '/api/leave-requests/preview', call => ({ data: ok({ employeeId: 'employee-1', leaveTypeId: call.body && typeof call.body === 'object' && 'leaveTypeId' in call.body ? String(call.body.leaveTypeId) : 'leave-1', leavePeriodId: 'period-1', leavePolicyVersionId: 'version-1', leavePolicyRuleId: 'rule-1', startDate: '2027-01-04', endDate: '2027-01-04', requestedQuantity: 1, chargeableQuantity: 1, requestDays: [], entitlementMode: 'Allocated', balanceReservationRequired: true, attachmentRequired: false, payloadFingerprint: 'fingerprint' }) }))
    renderAsUser(<LeaveRequestPreviewPage />, { user: employee })

    await userEvent.selectOptions(await screen.findByLabelText(/Leave Type/), 'leave-1')
    fireEvent.change(screen.getByLabelText(/Start Date/), { target: { value: '2027-01-04' } })
    fireEvent.change(screen.getByLabelText(/End Date/), { target: { value: '2027-01-04' } })
    await userEvent.click(screen.getByRole('button', { name: /Preview Leave/ }))

    expect(await screen.findByText('Preview result')).toBeInTheDocument()
    expect(stub.callsTo('post', '/api/leave-requests/preview')).toHaveLength(1)
  })
})
