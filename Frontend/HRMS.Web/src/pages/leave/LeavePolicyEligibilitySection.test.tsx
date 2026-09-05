import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser } from '../../test/fixtures.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { LeavePolicyEligibilitySection } from './LeavePolicyEligibilitySection.tsx'
import type { LeavePolicyVersion, LeaveTypeSelection } from '../../api/leaveConfiguration.ts'

const version: LeavePolicyVersion = {
  id: 'version-1', versionNumber: 1, effectiveFrom: '2027-01-01', effectiveTo: null,
  status: 'Draft', priority: 10, leaveTypeCount: 1, applicabilityGroupCount: 0,
  createdDate: '2027-01-01T00:00:00Z', modifiedDate: null, concurrencyToken: 'version-token',
  allowedActions: { canEdit: true, canValidate: true, canPublish: true, canRetire: false, canCreateVersion: true },
}
const leaveType: LeaveTypeSelection = { id: 'type-1', code: 'CL', name: 'Casual Leave', isActive: true }
const eligibilityUrl = '/api/leave-policies/policy-1/versions/version-1/leave-types/type-1/eligibility'

describe('LeavePolicyEligibilitySection', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  function renderSection(permissions: string[] = [Permissions.leave.policyManage], types = [leaveType]) {
    return renderAsUser(<LeavePolicyEligibilitySection policyId="policy-1" version={version} leaveTypes={types} canManage={permissions.includes(Permissions.leave.policyManage)} onNotice={() => undefined} />, { user: makeUser({ permissions }) })
  }

  it('loads the baseline and saves typed eligibility by ID route', async () => {
    stub.on('get', eligibilityUrl, () => ({ data: ok(null) }))
    stub.on('put', eligibilityUrl, () => ({ data: ok({ id: 'eligibility-1', leavePolicyRuleId: 'rule-1', eligibilityMode: 'MinimumService', minimumServiceValue: 30, minimumServiceUnit: 'Days', probationMode: 'Allowed', noticePeriodMode: 'Allowed', concurrencyToken: 'eligibility-token' }) }))
    renderSection()
    const form = await screen.findByRole('form', { name: 'Casual Leave Eligibility' })
    await userEvent.selectOptions(within(form).getByLabelText('Eligibility Mode'), 'MinimumService')
    await userEvent.type(within(form).getByLabelText(/Minimum Service Value/), '30')
    await userEvent.click(within(form).getByRole('button', { name: 'Save Eligibility' }))
    await waitFor(() => expect(stub.callsTo('put', eligibilityUrl)[0]?.body).toMatchObject({ eligibilityMode: 'MinimumService', minimumServiceValue: 30, minimumServiceUnit: 'Days', concurrencyToken: 'version-token' }))
  })

  it('keeps Eligibility read-only for PolicyView users', async () => {
    stub.on('get', eligibilityUrl, () => ({ data: ok({ id: 'eligibility-1', leavePolicyRuleId: 'rule-1', eligibilityMode: 'MinimumService', minimumServiceValue: 2, minimumServiceUnit: 'Months', probationMode: 'Allowed', noticePeriodMode: 'Allowed', concurrencyToken: 'token' }) }))
    renderSection([Permissions.leave.policyView])
    const form = await screen.findByRole('form', { name: 'Casual Leave Eligibility' })
    expect(within(form).getByLabelText('Eligibility Mode')).toBeDisabled()
    expect(within(form).queryByRole('button', { name: 'Save Eligibility' })).not.toBeInTheDocument()
  })

  it('shows a saved inactive historical Leave Type without offering a new selection', async () => {
    stub.on('get', '/api/leave-policies/policy-1/versions/version-1/leave-types/type-2/eligibility', () => ({ data: ok(null) }))
    renderSection([Permissions.leave.policyView], [{ ...leaveType, id: 'type-2', code: 'OLD', name: 'Old Leave', isActive: false }])
    expect(await screen.findByText('Inactive historical reference')).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /OLD/ })).toBeInTheDocument()
  })

  it('preserves values and shows one section error on a failed save', async () => {
    stub.on('get', eligibilityUrl, () => ({ data: ok(null) }))
    stub.on('put', eligibilityUrl, () => ({ status: 409, data: fail('Configuration changed by another user. Reload before saving.') }))
    renderSection()
    const form = await screen.findByRole('form', { name: 'Casual Leave Eligibility' })
    await userEvent.selectOptions(within(form).getByLabelText('Eligibility Mode'), 'MinimumService')
    await userEvent.type(within(form).getByLabelText(/Minimum Service Value/), '45')
    await userEvent.click(within(form).getByRole('button', { name: 'Save Eligibility' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/changed by another user/)
    expect(within(form).getByLabelText(/Minimum Service Value/)).toHaveValue(45)
    expect(screen.getAllByText(/changed by another user/)).toHaveLength(1)
  })

  it('shows the tenant-wide baseline when no Leave Types are selected', () => {
    renderSection([], [])
    expect(screen.getByText('Assign at least one Leave Type before configuring Eligibility.')).toBeInTheDocument()
  })
})
