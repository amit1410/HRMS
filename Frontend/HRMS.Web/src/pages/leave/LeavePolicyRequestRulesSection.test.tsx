import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { Permissions } from '../../auth/permissions.ts'
import type { LeavePolicyVersion, LeaveTypeSelection } from '../../api/leaveConfiguration.ts'
import { makeUser } from '../../test/fixtures.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { LeavePolicyRequestRulesSection } from './LeavePolicyRequestRulesSection.tsx'

const version: LeavePolicyVersion = { id: 'version-1', versionNumber: 1, effectiveFrom: '2027-01-01', effectiveTo: null, status: 'Draft', priority: 10, leaveTypeCount: 1, applicabilityGroupCount: 0, createdDate: '2027-01-01T00:00:00Z', modifiedDate: null, concurrencyToken: 'version-token', allowedActions: { canEdit: true, canValidate: true, canPublish: true, canRetire: false, canCreateVersion: true } }
const type: LeaveTypeSelection = { id: 'type-1', code: 'CL', name: 'Casual Leave', isActive: true }
const url = '/api/leave-policies/policy-1/versions/version-1/leave-types/type-1/request-rules'

describe('LeavePolicyRequestRulesSection', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())
  const renderSection = (permissions: string[] = [Permissions.leave.policyManage], selectedVersion = version) => renderAsUser(<LeavePolicyRequestRulesSection policyId="policy-1" version={selectedVersion} leaveTypes={[type]} canManage={permissions.includes(Permissions.leave.policyManage)} onNotice={() => undefined} />, { user: makeUser({ permissions }) })

  it('loads and saves typed request constraints', async () => {
    stub.on('get', url, () => ({ data: ok(null) })); stub.on('put', url, () => ({ data: ok({ id: 'request-1', leavePolicyRuleId: 'rule-1', minimumRequestQuantity: 0.5, maximumRequestQuantity: 3, maximumConsecutiveQuantity: 3, minimumAdvanceNoticeDays: 1, backdatedRequestMode: 'NotAllowed', maximumBackdatedDays: null, maximumRequestsPerPeriod: null, maximumQuantityPerPeriod: null, requestLimitPeriod: null, partialDayMode: 'HalfDayAllowed', concurrencyToken: 'token-2' }) }))
    renderSection(); const form = await screen.findByRole('form', { name: 'Casual Leave Request Rules' }); await userEvent.type(within(form).getByLabelText('Minimum request quantity'), '0.5'); await userEvent.selectOptions(within(form).getByLabelText('Partial-day mode'), 'HalfDayAllowed'); await userEvent.click(within(form).getByRole('button', { name: 'Save Request Rules' })); await waitFor(() => expect(stub.callsTo('put', url)[0]?.body).toMatchObject({ minimumRequestQuantity: 0.5, partialDayMode: 'HalfDayAllowed', concurrencyToken: 'version-token' }))
  })
  it('keeps PolicyView and historical versions read-only', async () => { stub.on('get', url, () => ({ data: ok(null) })); renderSection([Permissions.leave.policyView], { ...version, status: 'Published' }); const form = await screen.findByRole('form', { name: 'Casual Leave Request Rules' }); expect(within(form).getByLabelText('Partial-day mode')).toBeDisabled(); expect(within(form).queryByRole('button', { name: 'Save Request Rules' })).not.toBeInTheDocument() })
  it('preserves values and shows one conflict message', async () => { stub.on('get', url, () => ({ data: ok(null) })); stub.on('put', url, () => ({ status: 409, data: fail('Configuration changed by another user. Reload before saving.') })); renderSection(); const form = await screen.findByRole('form', { name: 'Casual Leave Request Rules' }); await userEvent.type(within(form).getByLabelText('Maximum request quantity'), '3'); await userEvent.click(within(form).getByRole('button', { name: 'Save Request Rules' })); expect(await screen.findByRole('alert')).toHaveTextContent(/changed by another user/); expect(within(form).getByLabelText('Maximum request quantity')).toHaveValue(3); expect(screen.getAllByText(/changed by another user/)).toHaveLength(1) })
})
