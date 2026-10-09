import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { ConfirmationSchedulerPage } from './ConfirmationSchedulerPage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser, paged } from '../../test/fixtures.ts'
import { installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'

const settings = {
  isConfigured: true,
  enabled: true,
  frequencyMinutes: 60,
  defaultTriggerDaysBeforeEnd: 30,
  maxAttempts: null,
  claimLeaseMinutes: null,
  resendProcessed: false,
  notifyEmployee: true,
  notifyManager: true,
  notifyHrbp: true,
  lastRunAtUtc: null,
  modifiedDate: null,
  modifiedBy: null,
}

const execution = {
  id: 'rem-1',
  employeeId: 'emp12345-abcd-0000-0000-000000000000',
  confirmationPolicyId: 'pol-1',
  confirmationPolicyVersionId: 'ver-1',
  probationEndDate: '2026-07-15',
  triggerDate: '2026-06-15',
  recipientType: 'Employee' as const,
  recipientEmployeeId: null,
  recipientEmail: 'employee@test.local',
  status: 'Sent',
  attemptCount: 1,
  sentAtUtc: '2026-06-20T00:00:00Z',
  processedAtUtc: '2026-06-20T00:00:00Z',
  nextAttemptAtUtc: null,
  lastError: null,
  createdDate: '2026-06-20T00:00:00Z',
}

describe('ConfirmationSchedulerPage', () => {
  let stub: StubAdapter
  beforeEach(() => {
    stub = installStubAdapter()
  })
  afterEach(() => stub.restore())

  function wire(rows = [execution]) {
    stub.on('get', '/api/confirmation-scheduler/settings', () => ({ data: ok(settings) }))
    stub.on('get', '/api/confirmation-scheduler/executions', () => ({ data: ok(paged(rows)) }))
  }

  function renderPage(permissions = [Permissions.confirmation.schedulerView, Permissions.confirmation.schedulerManage]) {
    return renderAsUser(<ConfirmationSchedulerPage />, { user: makeUser({ permissions }) })
  }

  it('renders settings and the execution log', async () => {
    wire()
    renderPage()
    expect(await screen.findByText('Scheduler enabled for this tenant')).toBeInTheDocument()
    expect(await screen.findByText('employee@test.local', { exact: false })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save Settings' })).toBeInTheDocument()
  })

  it('hides the save action and shows read-only notice without manage permission', async () => {
    wire([])
    renderPage([Permissions.confirmation.schedulerView])
    await screen.findByText('Scheduler enabled for this tenant')
    expect(screen.queryByRole('button', { name: 'Save Settings' })).not.toBeInTheDocument()
    expect(screen.getByText(/read-only access/i)).toBeInTheDocument()
  })

  it('saves settings through the API', async () => {
    wire([])
    stub.on('put', '/api/confirmation-scheduler/settings', () => ({ data: ok(settings) }))
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Save Settings' }))
    await waitFor(() => expect(stub.callsTo('put', '/api/confirmation-scheduler/settings')).toHaveLength(1))
  })

  it('sends the status filter to the backend', async () => {
    wire([])
    renderPage()
    await userEvent.selectOptions(await screen.findByLabelText('Status filter'), 'Failed')
    await waitFor(() =>
      expect(
        stub.calls.filter((call) => call.method === 'get' && call.url === '/api/confirmation-scheduler/executions').at(-1)?.params,
      ).toMatchObject({ status: 'Failed' }),
    )
  })
})
