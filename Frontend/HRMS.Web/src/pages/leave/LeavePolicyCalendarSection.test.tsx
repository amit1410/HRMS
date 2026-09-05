import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { Permissions } from '../../auth/permissions.ts'
import type { LeavePolicyVersion, LeaveTypeSelection } from '../../api/leaveConfiguration.ts'
import { makeUser } from '../../test/fixtures.ts'
import { installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { LeavePolicyCalendarSection } from './LeavePolicyCalendarSection.tsx'

const version: LeavePolicyVersion = { id: 'version-1', versionNumber: 1, effectiveFrom: '2027-01-01', effectiveTo: null, status: 'Draft', priority: 10, leaveTypeCount: 1, applicabilityGroupCount: 0, createdDate: '2027-01-01T00:00:00Z', modifiedDate: null, concurrencyToken: 'version-token', allowedActions: { canEdit: true, canValidate: true, canPublish: true, canRetire: false, canCreateVersion: true } }
const type: LeaveTypeSelection = { id: 'type-1', code: 'CL', name: 'Casual Leave', isActive: true }
const url = '/api/leave-policies/policy-1/versions/version-1/leave-types/type-1/calendar'
describe('LeavePolicyCalendarSection', () => { let stub: StubAdapter; beforeEach(() => { stub = installStubAdapter() }); afterEach(() => stub.restore())
  it('shows baseline and sends normal and sandwich settings', async () => { stub.on('get', url, () => ({ data: ok(null) })); stub.on('put', url, () => ({ data: ok({ id: 'calendar-1', leavePolicyRuleId: 'rule-1', holidayTreatment: 'Include', weekOffTreatment: 'Exclude', sandwichMode: 'Holiday', applyToPrefix: true, applyToSuffix: false, applyToBetween: false, concurrencyToken: 'token-2' }) })); renderAsUser(<LeavePolicyCalendarSection policyId="policy-1" version={version} leaveTypes={[type]} canManage onNotice={() => undefined} />, { user: makeUser({ permissions: [Permissions.leave.policyManage] }) }); const form = await screen.findByRole('form', { name: 'Casual Leave Calendar Rules' }); await userEvent.selectOptions(within(form).getByLabelText('Holidays'), 'Include'); await userEvent.selectOptions(within(form).getByLabelText('Sandwich mode'), 'Holiday'); await userEvent.click(within(form).getByLabelText(/Prefix/)); await userEvent.click(within(form).getByRole('button', { name: 'Save Calendar Rules' })); expect(stub.callsTo('put', url)[0]?.body).toMatchObject({ holidayTreatment: 'Include', sandwichMode: 'Holiday', applyToPrefix: true }) })
  it('keeps historical configuration read-only', async () => { stub.on('get', url, () => ({ data: ok({ id: 'calendar-1', leavePolicyRuleId: 'rule-1', holidayTreatment: 'Include', weekOffTreatment: 'Include', sandwichMode: 'HolidayAndWeekOff', applyToPrefix: true, applyToSuffix: true, applyToBetween: true, concurrencyToken: 'token' }) })); renderAsUser(<LeavePolicyCalendarSection policyId="policy-1" version={{ ...version, status: 'Published' }} leaveTypes={[type]} canManage={false} onNotice={() => undefined} />, { user: makeUser({ permissions: [Permissions.leave.policyView] }) }); const form = await screen.findByRole('form', { name: 'Casual Leave Calendar Rules' }); expect(within(form).getByLabelText('Holidays')).toBeDisabled(); expect(within(form).queryByRole('button', { name: 'Save Calendar Rules' })).not.toBeInTheDocument() })
})
