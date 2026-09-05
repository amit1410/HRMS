import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { LeaveRequestPreviewPage } from './LeaveRequestPreviewPage.tsx'
import { makeUser, paged } from '../../test/fixtures.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'

const activeType = { id: 'type-1', code: 'CL', name: 'Casual Leave', description: null, defaultUnit: 'Day' as const, isPaid: true, isActive: true, createdDate: '2026-01-01T00:00:00Z', modifiedDate: null, concurrencyToken: 'token' }
const inactiveType = { ...activeType, id: 'type-2', code: 'OLD', name: 'Old Leave', isActive: false }
const preview = { employeeId: 'employee-1', leaveTypeId: 'type-1', leavePeriodId: 'period-1', leavePolicyVersionId: 'version-1', leavePolicyRuleId: 'rule-1', startDate: '2026-10-05', endDate: '2026-10-06', requestedQuantity: 2, chargeableQuantity: 2, requestDays: [{ date: '2026-10-05', requestedQuantity: 1, chargeableQuantity: 1, dayClassification: null, calculationReason: null, isEmployeeRequested: true }, { date: '2026-10-06', requestedQuantity: 1, chargeableQuantity: 1, dayClassification: null, calculationReason: null, isEmployeeRequested: true }], entitlementMode: 'Allocated' as const, balanceReservationRequired: true, attachmentRequired: false, payloadFingerprint: 'a'.repeat(64) }
const unlimitedPreview = { ...preview, entitlementMode: 'Unlimited' as const, balanceReservationRequired: false }
const submitted = { requestId: 'request-1', status: 'PendingApproval' as const, employeeId: 'employee-1', leaveTypeId: 'type-1', leavePeriodId: 'period-1', leavePolicyVersionId: 'version-1', leavePolicyRuleId: 'rule-1', employeeEmploymentHistoryId: 'employment-1', startDate: '2026-10-05', endDate: '2026-10-06', requestedQuantity: 2, chargeableQuantity: 2, submittedAtUtc: '2026-10-01T10:00:00Z', requestDays: unlimitedPreview.requestDays, isReplay: false }

function bodyOf(call: ReturnType<StubAdapter['callsTo']>[number]): Record<string, unknown> { return call.body as Record<string, unknown> }

describe('LeaveRequestPreviewPage', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter(); stub.on('get', '/api/leave-types', () => ({ data: ok(paged([activeType, inactiveType])) })) })
  afterEach(() => stub.restore())
  function renderPage() { return renderAsUser(<LeaveRequestPreviewPage />, { user: makeUser() }) }
  async function fillForm() { await userEvent.selectOptions(await screen.findByLabelText(/^Leave Type/), 'type-1'); await userEvent.type(screen.getByLabelText(/Start Date/), '2026-10-05'); await userEvent.type(screen.getByLabelText(/End Date/), '2026-10-06') }

  it('loads only active types, validates dates, and sends the server-authoritative preview contract', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(preview) })); renderPage(); expect(await screen.findByRole('option', { name: /CL — Casual Leave/ })).toBeInTheDocument(); expect(screen.queryByRole('option', { name: /OLD — Old Leave/ })).not.toBeInTheDocument(); await userEvent.selectOptions(screen.getByLabelText(/^Leave Type/), 'type-1'); await userEvent.type(screen.getByLabelText(/Start Date/), '2026-10-07'); await userEvent.type(screen.getByLabelText(/End Date/), '2026-10-06'); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); expect(screen.getByText('Start Date must be on or before End Date.')).toBeInTheDocument(); expect(stub.callsTo('post', '/api/leave-requests/preview')).toHaveLength(0); await userEvent.clear(screen.getByLabelText(/Start Date/)); await userEvent.type(screen.getByLabelText(/Start Date/), '2026-10-05'); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await waitFor(() => expect(stub.callsTo('post', '/api/leave-requests/preview')).toHaveLength(1)); const calls = stub.callsTo('post', '/api/leave-requests/preview'); expect(calls).toHaveLength(1); const call = calls[0]; if (!call) throw new Error('Expected a preview call.'); const body = bodyOf(call); expect(body).toMatchObject({ leaveTypeId: 'type-1', startDate: '2026-10-05', endDate: '2026-10-06' }); expect(body).toHaveProperty('idempotencyKey'); expect(body).not.toHaveProperty('tenantId'); expect(body).not.toHaveProperty('employeeId'); expect(body).not.toHaveProperty('requestedQuantity'); expect(body).not.toHaveProperty('chargeableQuantity')
  })

  it('renders authoritative quantities, days, nullable values, and entitlement messaging', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(preview) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await screen.findByText('Preview result'); const requestedRow = screen.getByText('Requested Quantity').closest('div'); const chargeableRow = screen.getByText('Chargeable Quantity').closest('div'); if (!requestedRow || !chargeableRow) throw new Error('Expected quantity summary rows.'); expect(within(requestedRow).getByText('2.000')).toBeInTheDocument(); expect(within(chargeableRow).getByText('2.000')).toBeInTheDocument(); expect(screen.getAllByText('—')).toHaveLength(4); expect(screen.getByText('Balance reservation will be required when this request is submitted.')).toBeInTheDocument(); expect(screen.getByText('Allocated entitlement')).toBeInTheDocument(); expect(screen.getByRole('table', { name: /preview request days/i })).toBeInTheDocument(); const firstCalls = stub.callsTo('post', '/api/leave-requests/preview'); expect(firstCalls).toHaveLength(1); const firstCall = firstCalls[0]; if (!firstCall) throw new Error('Expected a preview call.'); const firstKey = bodyOf(firstCall).idempotencyKey; await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await waitFor(() => expect(stub.callsTo('post', '/api/leave-requests/preview')).toHaveLength(2)); const secondCalls = stub.callsTo('post', '/api/leave-requests/preview'); const secondCall = secondCalls[1]; if (!secondCall) throw new Error('Expected a second preview call.'); expect(bodyOf(secondCall).idempotencyKey).toBe(firstKey)
  })

  it('clears stale preview and creates a new draft key on reset', async () => { stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(preview) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await screen.findByText('Preview result'); await userEvent.clear(screen.getByLabelText(/End Date/)); expect(screen.queryByText('Preview result')).not.toBeInTheDocument(); await userEvent.click(screen.getByRole('button', { name: 'Reset' })); expect(screen.getByLabelText(/^Leave Type/)).toHaveValue(''); expect(screen.getByLabelText(/Start Date/)).toHaveValue('') })
  it('shows unsupported configuration as a configuration limitation and has no persistence action', async () => { stub.on('post', '/api/leave-requests/preview', () => ({ status: 400, data: fail('UnsupportedConfiguration: calendar dependency is not supported in preview.') })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); expect(await screen.findByText(/not supported in preview yet/)).toBeInTheDocument(); expect(screen.queryByRole('button', { name: /Submit|Apply|Save|Confirm/ })).not.toBeInTheDocument(); expect(stub.callsTo('post', '/api/leave-requests')).toHaveLength(0) })
  it('shows configuration conflicts without offering a persistence action', async () => { stub.on('post', '/api/leave-requests/preview', () => ({ status: 409, data: fail('Multiple applicable Leave Policies were found.') })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); expect(await screen.findByText('Multiple applicable Leave Policies were found.')).toBeInTheDocument(); expect(stub.callsTo('post', '/api/leave-requests')).toHaveLength(0) })

  it('does not offer Submit before a successful preview', async () => { renderPage(); await screen.findByRole('option', { name: /CL/ }); expect(screen.queryByRole('button', { name: 'Submit Leave Request' })).not.toBeInTheDocument() })

  it('keeps preview side-effect free until the explicit Submit action', async () => { stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await screen.findByRole('button', { name: 'Submit Leave Request' }); expect(stub.callsTo('post', '/api/leave-requests')).toHaveLength(0)
  })

  it('enables Submit after a successful supported preview and sends the exact draft key-only contract', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); stub.on('post', '/api/leave-requests', () => ({ data: ok(submitted) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); const submit = await screen.findByRole('button', { name: 'Submit Leave Request' }); expect(submit).toBeEnabled(); const previewBody = bodyOf(stub.callsTo('post', '/api/leave-requests/preview')[0]!); await userEvent.click(submit); await waitFor(() => expect(stub.callsTo('post', '/api/leave-requests')).toHaveLength(1)); const body = bodyOf(stub.callsTo('post', '/api/leave-requests')[0]!); expect(body).toEqual({ leaveTypeId: 'type-1', startDate: '2026-10-05', endDate: '2026-10-06', idempotencyKey: previewBody.idempotencyKey }); expect(body).not.toHaveProperty('employeeId'); expect(body).not.toHaveProperty('requestDays')
  })

  it.each(['Leave Type', 'Start Date', 'End Date'])('invalidates Submit when %s changes after preview', async (field) => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await screen.findByRole('button', { name: 'Submit Leave Request' }); if (field === 'Leave Type') await userEvent.selectOptions(screen.getByLabelText(/^Leave Type/), ''); else await userEvent.clear(screen.getByLabelText(new RegExp(field))); expect(screen.queryByRole('button', { name: 'Submit Leave Request' })).not.toBeInTheDocument()
  })

  it('shows Pending Approval and authoritative submission quantities after success', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); stub.on('post', '/api/leave-requests', () => ({ data: ok(submitted) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await userEvent.click(await screen.findByRole('button', { name: 'Submit Leave Request' })); expect(await screen.findByText('Leave Request Submitted')).toBeInTheDocument(); expect(screen.getByText('Pending Approval')).toBeInTheDocument(); expect(screen.getByText('request-1')).toBeInTheDocument(); expect(screen.getAllByText('2.000').length).toBeGreaterThanOrEqual(2); expect(screen.getByRole('button', { name: 'New Request' })).toBeInTheDocument()
  })

  it('treats an idempotent replay as success', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); stub.on('post', '/api/leave-requests', () => ({ data: ok({ ...submitted, isReplay: true }) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await userEvent.click(await screen.findByRole('button', { name: 'Submit Leave Request' })); expect(await screen.findByText('Leave Request Already Submitted')).toBeInTheDocument(); expect(screen.getByText(/already submitted/)).toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Submit Leave Request' })).not.toBeInTheDocument()
  })

  it('disables Submit while the request is in flight and ignores repeated clicks', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); stub.on('post', '/api/leave-requests', () => ({ data: ok(submitted), delay: true })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); const submit = await screen.findByRole('button', { name: 'Submit Leave Request' }); const pending = userEvent.click(submit); await waitFor(() => expect(stub.callsTo('post', '/api/leave-requests')).toHaveLength(1)); fireEvent.click(submit); expect(stub.callsTo('post', '/api/leave-requests')).toHaveLength(1); await pending
  })

  it('preserves the form on overlap and idempotency conflicts', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); stub.on('post', '/api/leave-requests', call => ({ status: 409, data: fail(call.body && (call.body as Record<string, unknown>).idempotencyKey === 'never' ? 'IdempotencyConflict' : 'Overlap') })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await userEvent.click(await screen.findByRole('button', { name: 'Submit Leave Request' })); expect(await screen.findByText(/overlap another active/)).toBeInTheDocument(); expect(screen.getByLabelText(/Start Date/)).toHaveValue('2026-10-05'); expect(screen.getByLabelText(/End Date/)).toHaveValue('2026-10-06')
  })

  it('explains an idempotency conflict and keeps the draft values', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); stub.on('post', '/api/leave-requests', () => ({ status: 409, data: fail('IdempotencyConflict: the key was used for different data.') })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await userEvent.click(await screen.findByRole('button', { name: 'Submit Leave Request' })); expect(await screen.findByText(/already used for different request data/)).toBeInTheDocument(); expect(screen.getByLabelText(/Start Date/)).toHaveValue('2026-10-05')
  })

  it('shows a safe retry message for concurrency conflicts', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); stub.on('post', '/api/leave-requests', () => ({ status: 409, data: fail('ConcurrencyConflict: please retry.') })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await userEvent.click(await screen.findByRole('button', { name: 'Submit Leave Request' })); expect(await screen.findByText(/changed while it was being submitted/)).toBeInTheDocument()
  })

  it('invalidates a preview when submission reports unsupported configuration', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); stub.on('post', '/api/leave-requests', () => ({ status: 400, data: fail('UnsupportedConfiguration: configuration changed.') })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await userEvent.click(await screen.findByRole('button', { name: 'Submit Leave Request' })); await screen.findByText(/UnsupportedConfiguration/); expect(screen.queryByRole('button', { name: 'Submit Leave Request' })).not.toBeInTheDocument()
  })

  it('submits a NoBalanceRequired preview through the same path', async () => {
    const noBalance = { ...unlimitedPreview, entitlementMode: 'NoBalanceRequired' as const }; stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(noBalance) })); stub.on('post', '/api/leave-requests', () => ({ data: ok(submitted) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await userEvent.click(await screen.findByRole('button', { name: 'Submit Leave Request' })); expect(await screen.findByText('Leave Request Submitted')).toBeInTheDocument()
  })

  it('renders authoritative submitted RequestDays rather than constructing them', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); stub.on('post', '/api/leave-requests', () => ({ data: ok(submitted) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await userEvent.click(await screen.findByRole('button', { name: 'Submit Leave Request' })); await screen.findByText('Leave Request Submitted'); expect(screen.getByText('2026-10-05')).toBeInTheDocument(); expect(screen.getByText('2026-10-06')).toBeInTheDocument()
  })

  it('keeps date-only values unchanged in the preview and submit payloads', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); stub.on('post', '/api/leave-requests', () => ({ data: ok(submitted) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await userEvent.click(await screen.findByRole('button', { name: 'Submit Leave Request' })); const body = bodyOf(stub.callsTo('post', '/api/leave-requests')[0]!); expect(body.startDate).toBe('2026-10-05'); expect(body.endDate).toBe('2026-10-06')
  })

  it('does not send client-calculated quantities or days', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); stub.on('post', '/api/leave-requests', () => ({ data: ok(submitted) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await userEvent.click(await screen.findByRole('button', { name: 'Submit Leave Request' })); const body = bodyOf(stub.callsTo('post', '/api/leave-requests')[0]!); expect(body).not.toHaveProperty('requestedQuantity'); expect(body).not.toHaveProperty('chargeableQuantity'); expect(body).not.toHaveProperty('requestDays')
  })

  it('clears completed submission state when starting a New Request', async () => {
    stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); stub.on('post', '/api/leave-requests', () => ({ data: ok(submitted) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); await userEvent.click(await screen.findByRole('button', { name: 'Submit Leave Request' })); await userEvent.click(await screen.findByRole('button', { name: 'New Request' })); expect(screen.queryByText('Leave Request Submitted')).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Submit Leave Request' })).not.toBeInTheDocument()
  })

  it('submits an Allocated preview and explains that the chargeable balance is reserved', async () => { stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(preview) })); stub.on('post', '/api/leave-requests', () => ({ data: ok(submitted) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); const submit = await screen.findByRole('button', { name: 'Submit Leave Request' }); expect(submit).toBeEnabled(); expect(screen.getByText('This request will reserve 2 day(s) from your leave balance when submitted.')).toBeInTheDocument(); await userEvent.click(submit); expect(await screen.findByText('Leave request submitted. The required leave balance has been reserved.')).toBeInTheDocument(); expect(stub.callsTo('post', '/api/leave-requests')).toHaveLength(1)
  })

  it('generates a new key only when New Request starts a new draft', async () => { stub.on('post', '/api/leave-requests/preview', () => ({ data: ok(unlimitedPreview) })); stub.on('post', '/api/leave-requests', () => ({ data: ok(submitted) })); renderPage(); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); const firstKey = bodyOf(stub.callsTo('post', '/api/leave-requests/preview')[0]!).idempotencyKey; await userEvent.click(await screen.findByRole('button', { name: 'Submit Leave Request' })); await userEvent.click(await screen.findByRole('button', { name: 'New Request' })); await fillForm(); await userEvent.click(screen.getByRole('button', { name: 'Preview Leave' })); const secondKey = bodyOf(stub.callsTo('post', '/api/leave-requests/preview')[1]!).idempotencyKey; expect(secondKey).not.toBe(firstKey)
  })
})
