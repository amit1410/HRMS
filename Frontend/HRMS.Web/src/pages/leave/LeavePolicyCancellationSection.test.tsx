import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { Permissions } from '../../auth/permissions.ts'
import type { LeavePolicyVersion, LeaveTypeSelection } from '../../api/leaveConfiguration.ts'
import { makeUser } from '../../test/fixtures.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { LeavePolicyCancellationSection } from './LeavePolicyCancellationSection.tsx'

const version: LeavePolicyVersion = {
  id: 'version-1', versionNumber: 1, effectiveFrom: '2027-01-01', effectiveTo: null,
  status: 'Draft', priority: 10, leaveTypeCount: 1, applicabilityGroupCount: 0,
  createdDate: '2027-01-01T00:00:00Z', modifiedDate: null, concurrencyToken: 'version-token',
  allowedActions: { canEdit: true, canValidate: true, canPublish: true, canRetire: false, canCreateVersion: true },
}
const leaveType: LeaveTypeSelection = { id: 'type-1', code: 'CL', name: 'Casual Leave', isActive: true }
const url = '/api/leave-policies/policy-1/versions/version-1/leave-types/type-1/cancellation'

describe('LeavePolicyCancellationSection', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  function renderSection(permissions: string[] = [Permissions.leave.policyManage], selectedVersion = version) {
    return renderAsUser(<LeavePolicyCancellationSection policyId="policy-1" version={selectedVersion} leaveTypes={[leaveType]} canManage={permissions.includes(Permissions.leave.policyManage)} onNotice={() => undefined} />, { user: makeUser({ permissions }) })
  }

  it('loads separate capability values and saves a typed payload', async () => {
    stub.on('get', url, () => ({ data: ok({ id: 'cancel-1', leavePolicyRuleId: 'rule-1', withdrawAllowed: true, cancelAllowed: false, modifyAllowed: true, concurrencyToken: 'rule-token' }) }))
    stub.on('put', url, () => ({ data: ok({ id: 'cancel-1', leavePolicyRuleId: 'rule-1', withdrawAllowed: true, cancelAllowed: true, modifyAllowed: true, concurrencyToken: 'new-token' }) }))
    renderSection()
    const form = await screen.findByRole('form', { name: 'Casual Leave Request Changes and Cancellation' })
    await userEvent.click(within(form).getByRole('checkbox', { name: /^Allow Cancel/ }))
    await userEvent.click(within(form).getByRole('button', { name: 'Save Cancellation Rules' }))
    await waitFor(() => expect(stub.callsTo('put', url)[0]?.body).toMatchObject({ withdrawAllowed: true, cancelAllowed: true, modifyAllowed: true, concurrencyToken: 'rule-token' }))
  })

  it('uses the safe no-row baseline and is read-only for historical versions', async () => {
    stub.on('get', url, () => ({ data: ok(null) }))
    renderSection([Permissions.leave.policyView], { ...version, status: 'Published' })
    const form = await screen.findByRole('form', { name: 'Casual Leave Request Changes and Cancellation' })
    expect(within(form).getByRole('checkbox', { name: /^Allow Withdraw/ })).not.toBeChecked()
    expect(within(form).getByRole('checkbox', { name: /^Allow Withdraw/ })).toBeDisabled()
    expect(within(form).queryByRole('button', { name: 'Save Cancellation Rules' })).not.toBeInTheDocument()
  })

  it('preserves values and renders one scoped conflict error', async () => {
    stub.on('get', url, () => ({ data: ok(null) }))
    stub.on('put', url, () => ({ status: 409, data: fail('Configuration changed by another user. Reload before saving.') }))
    renderSection()
    const form = await screen.findByRole('form', { name: 'Casual Leave Request Changes and Cancellation' })
    await userEvent.click(within(form).getByRole('checkbox', { name: /^Allow Modify/ }))
    await userEvent.click(within(form).getByRole('button', { name: 'Save Cancellation Rules' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/changed by another user/)
    expect(within(form).getByRole('checkbox', { name: /^Allow Modify/ })).toBeChecked()
    expect(screen.getAllByText(/changed by another user/)).toHaveLength(1)
  })
})
