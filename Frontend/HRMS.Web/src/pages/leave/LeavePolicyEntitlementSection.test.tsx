import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { Permissions } from '../../auth/permissions.ts'
import type { LeavePolicyVersion, LeaveTypeSelection } from '../../api/leaveConfiguration.ts'
import { makeUser } from '../../test/fixtures.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { LeavePolicyEntitlementSection } from './LeavePolicyEntitlementSection.tsx'

const version: LeavePolicyVersion = {
  id: 'version-1', versionNumber: 1, effectiveFrom: '2027-01-01', effectiveTo: null,
  status: 'Draft', priority: 10, leaveTypeCount: 1, applicabilityGroupCount: 0,
  createdDate: '2027-01-01T00:00:00Z', modifiedDate: null, concurrencyToken: 'version-token',
  allowedActions: { canEdit: true, canValidate: true, canPublish: true, canRetire: false, canCreateVersion: true },
}
const leaveType: LeaveTypeSelection = { id: 'type-1', code: 'CL', name: 'Casual Leave', isActive: true }
const entitlementUrl = '/api/leave-policies/policy-1/versions/version-1/leave-types/type-1/entitlement'

describe('LeavePolicyEntitlementSection', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  function renderSection(permissions: string[] = [Permissions.leave.policyManage], types = [leaveType], selectedVersion = version) {
    return renderAsUser(<LeavePolicyEntitlementSection policyId="policy-1" version={selectedVersion} leaveTypes={types} canManage={permissions.includes(Permissions.leave.policyManage)} onNotice={() => undefined} />, { user: makeUser({ permissions }) })
  }

  it('shows the baseline and saves an Allocated entitlement with typed values', async () => {
    stub.on('get', entitlementUrl, () => ({ data: ok(null) }))
    stub.on('put', entitlementUrl, () => ({ data: ok({ id: 'ent-1', leavePolicyRuleId: 'rule-1', entitlementMode: 'Allocated', entitlementSource: 'PolicyAccrual', entitlementQuantity: 12, accrualFrequency: 'Annual', accrualTiming: 'StartOfPeriod', concurrencyToken: 'token-2' }) }))
    renderSection()
    const form = await screen.findByRole('form', { name: 'Casual Leave Entitlement' })
    await userEvent.type(within(form).getByLabelText(/Entitlement Quantity/), '12')
    await userEvent.selectOptions(within(form).getByLabelText('Accrual Frequency'), 'Annual')
    await userEvent.click(within(form).getByRole('button', { name: 'Save Entitlement' }))
    await waitFor(() => expect(stub.callsTo('put', entitlementUrl)[0]?.body).toMatchObject({ entitlementMode: 'Allocated', entitlementSource: 'PolicyAccrual', entitlementQuantity: 12, accrualFrequency: 'Annual', accrualTiming: 'StartOfPeriod', concurrencyToken: 'version-token' }))
  })

  it('switches conditional mode fields without sending a stale quantity', async () => {
    stub.on('get', entitlementUrl, () => ({ data: ok(null) }))
    stub.on('put', entitlementUrl, () => ({ data: ok({ id: 'ent-1', leavePolicyRuleId: 'rule-1', entitlementMode: 'Unlimited', entitlementSource: 'ExternalGrant', entitlementQuantity: null, accrualFrequency: 'None', accrualTiming: null, concurrencyToken: 'token-2' }) }))
    renderSection()
    const form = await screen.findByRole('form', { name: 'Casual Leave Entitlement' })
    await userEvent.type(within(form).getByLabelText(/Entitlement Quantity/), '12')
    await userEvent.selectOptions(within(form).getByLabelText('Entitlement Mode'), 'Unlimited')
    await userEvent.selectOptions(within(form).getByLabelText('Entitlement Source'), 'ExternalGrant')
    await userEvent.click(within(form).getByRole('button', { name: 'Save Entitlement' }))
    await waitFor(() => expect(stub.callsTo('put', entitlementUrl)[0]?.body).toMatchObject({ entitlementMode: 'Unlimited', entitlementQuantity: null, accrualFrequency: 'None', accrualTiming: null }))
  })

  it('keeps the section read-only for PolicyView and historical versions', async () => {
    stub.on('get', entitlementUrl, () => ({ data: ok({ id: 'ent-1', leavePolicyRuleId: 'rule-1', entitlementMode: 'NoBalanceRequired', entitlementSource: 'NoBalanceRequired', entitlementQuantity: null, accrualFrequency: 'None', accrualTiming: null, concurrencyToken: 'token' }) }))
    renderSection([Permissions.leave.policyView], [leaveType], { ...version, status: 'Published' })
    const form = await screen.findByRole('form', { name: 'Casual Leave Entitlement' })
    expect(within(form).getByLabelText('Entitlement Mode')).toBeDisabled()
    expect(within(form).queryByRole('button', { name: 'Save Entitlement' })).not.toBeInTheDocument()
    expect(screen.getByText('Published Entitlement is immutable.')).toBeInTheDocument()
  })

  it('preserves values and shows one conflict error on a failed save', async () => {
    stub.on('get', entitlementUrl, () => ({ data: ok(null) }))
    stub.on('put', entitlementUrl, () => ({ status: 409, data: fail('Configuration changed by another user. Reload before saving.') }))
    renderSection()
    const form = await screen.findByRole('form', { name: 'Casual Leave Entitlement' })
    await userEvent.type(within(form).getByLabelText(/Entitlement Quantity/), '12')
    await userEvent.click(within(form).getByRole('button', { name: 'Save Entitlement' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/changed by another user/)
    expect(within(form).getByLabelText(/Entitlement Quantity/)).toHaveValue(12)
    expect(screen.getAllByText(/changed by another user/)).toHaveLength(1)
  })

  it('shows no entitlement configuration when no Leave Type is selected', () => {
    renderSection([], [])
    expect(screen.getByText('Assign at least one Leave Type before configuring Entitlement.')).toBeInTheDocument()
    expect(stub.callsTo('get', entitlementUrl)).toHaveLength(0)
  })
})
