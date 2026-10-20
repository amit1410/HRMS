import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { renderAsUser } from '../../test/renderWith.tsx'
import { installStubAdapter, type StubAdapter } from '../../test/stubAdapter.ts'
import { fail, ok } from '../../test/stubAdapter.ts'
import { makeUser } from '../../test/fixtures.ts'
import { Permissions } from '../../auth/permissions.ts'
import { WelcomeInvitationsPage } from './WelcomeInvitationsPage.tsx'

describe('WelcomeInvitationsPage', () => {
  let stub: StubAdapter
  const invitationUser = makeUser({ permissions: [Permissions.welcomeInvitation.view, Permissions.welcomeInvitation.send, Permissions.welcomeInvitation.resend, Permissions.welcomeInvitation.retry, Permissions.welcomeInvitation.viewHistory] })
  beforeEach(() => { stub = installStubAdapter(); stub.on('get', /\/api\/master-data\/.*/, () => ({ data: ok([{ id: 'm1', code: 'M1', name: 'Master', isActive: true }]) })); stub.on('get', '/api/communication/welcome-invitations', () => ({ data: ok({ page: { items: [{ employeeId: 'e1', employeeCode: 'E1', employeeName: 'One Employee', workEmail: 'one@example.test', joiningDate: '2026-01-01', portalAccountStatus: 'NotCreated', invitationStatus: 'NotSent', canSend: true, canResend: false, canRetry: false }], page: 1, pageSize: 25, totalCount: 2, totalPages: 1, hasPreviousPage: false, hasNextPage: false }, summary: { notInvited: 2, pending: 0, sent: 0, failed: 0, activated: 0 } }) })); stub.on('post', '/api/communication/welcome-invitations/preview', () => ({ data: ok({ selectedCount: 1, eligibleCount: 1, ineligibleCount: 0, missingEmailCount: 0, alreadyActiveCount: 0, alreadyInvitedCount: 0, messages: [] }) })); stub.on('post', '/api/communication/welcome-invitations/send', () => ({ data: ok({ batchId: 'b1', totalRequested: 1, accepted: 1, rejected: 0, messages: [] }) })) })
  afterEach(() => stub.restore())

  it('renders API-envelope data, filters, and explicit selection confirmation', async () => {
    renderAsUser(<WelcomeInvitationsPage />, { user: invitationUser })
    expect(await screen.findByText('One Employee')).toBeInTheDocument()
    expect(screen.getByLabelText('Holding Company')).toBeInTheDocument()
    expect(screen.getByLabelText('LOB')).toBeInTheDocument()
    expect(screen.getByLabelText('Organization')).toBeInTheDocument()
    expect(screen.getByLabelText('Department')).toBeInTheDocument()
    expect(screen.getByLabelText('Work Location')).toBeInTheDocument()
    await userEvent.click(screen.getByLabelText('Select One Employee'))
    window.confirm = () => false
    await userEvent.click(screen.getByRole('button', { name: 'Send selected' }))
    await waitFor(() => expect(stub.callsTo('post', '/api/communication/welcome-invitations/preview')).toHaveLength(0))
  })

  it('shows partial batch acceptance after preview confirmation', async () => {
    renderAsUser(<WelcomeInvitationsPage />, { user: invitationUser })
    expect(await screen.findByText('One Employee')).toBeInTheDocument()
    await userEvent.click(screen.getByLabelText('Select One Employee'))
    window.confirm = () => true
    stub.on('post', '/api/communication/welcome-invitations/send', () => ({ data: ok({ batchId: 'b1', totalRequested: 2, accepted: 1, rejected: 1, messages: ['E2: missing work email'] }) }))

    await userEvent.click(screen.getByRole('button', { name: 'Send selected' }))

    expect(await screen.findByText('Batch b1: Queued: 1 accepted, 1 rejected')).toBeInTheDocument()
  })

  it('renders an email-provider failure without clearing the current selection', async () => {
    renderAsUser(<WelcomeInvitationsPage />, { user: invitationUser })
    expect(await screen.findByText('One Employee')).toBeInTheDocument()
    await userEvent.click(screen.getByLabelText('Select One Employee'))
    window.confirm = () => true
    stub.on('post', '/api/communication/welcome-invitations/send', () => ({ status: 503, data: fail('Email provider unavailable') }))

    await userEvent.click(screen.getByRole('button', { name: 'Send selected' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('We couldn’t complete the invitation request. Please try again.')
    expect(screen.getAllByRole('status').some((status) => status.textContent?.includes('1 selected.'))).toBe(true)
  })
})
