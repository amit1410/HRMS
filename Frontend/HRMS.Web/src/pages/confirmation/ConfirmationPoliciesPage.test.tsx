import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { ConfirmationPoliciesPage } from './ConfirmationPoliciesPage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser, paged } from '../../test/fixtures.ts'
import { installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'

const item = {
  id: 'pol-1',
  code: 'STD',
  name: 'Standard Probation',
  description: 'Six-month probation',
  isActive: true,
  versionCount: 2,
  publishedVersionCount: 1,
  createdDate: '2026-01-01T00:00:00Z',
  modifiedDate: null,
}

describe('ConfirmationPoliciesPage', () => {
  let stub: StubAdapter
  beforeEach(() => {
    stub = installStubAdapter()
  })
  afterEach(() => stub.restore())

  function renderPage(permissions = [Permissions.confirmation.policyView, Permissions.confirmation.policyManage]) {
    return renderAsUser(<ConfirmationPoliciesPage />, { user: makeUser({ permissions }) })
  }
  function list(rows = [item]) {
    stub.on('get', '/api/confirmation-policies', () => ({ data: ok(paged(rows)) }))
  }

  it('renders policy rows with version counts and status', async () => {
    list()
    renderPage()
    expect(await screen.findByText('Standard Probation')).toBeInTheDocument()
    const row = screen.getByRole('row', { name: /STD Standard Probation/ })
    expect(within(row).getByText('Active')).toBeInTheDocument()
    expect(within(row).getByRole('link', { name: 'Configure' })).toHaveAttribute('href', '/confirmation/policies/pol-1')
  })

  it('shows an empty state and the add action for managers', async () => {
    list([])
    renderPage()
    expect(await screen.findByText('No confirmation policies configured yet.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add Policy' })).toBeInTheDocument()
  })

  it('hides the add action without manage permission', async () => {
    list([])
    renderPage([Permissions.confirmation.policyView])
    await screen.findByText('No confirmation policies configured yet.')
    expect(screen.queryByRole('button', { name: 'Add Policy' })).not.toBeInTheDocument()
  })

  it('sends search and status filters to the backend', async () => {
    list([])
    renderPage()
    await userEvent.type(await screen.findByLabelText('Search policies'), 'std')
    await userEvent.selectOptions(screen.getByLabelText('Status'), 'inactive')
    await waitFor(() =>
      expect(stub.calls.filter((call) => call.method === 'get').at(-1)?.params).toMatchObject({ search: 'std', isActive: false }),
    )
  })

  it('creates a policy through the drawer', async () => {
    list([])
    stub.on('post', '/api/confirmation-policies', () => ({ data: ok(item) }))
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Add Policy' }))
    await userEvent.type(screen.getByLabelText(/Code/), 'STD')
    await userEvent.type(screen.getByLabelText(/Name/), 'Standard Probation')
    await userEvent.click(screen.getByRole('button', { name: 'Create Policy' }))
    await waitFor(() => expect(stub.callsTo('post', '/api/confirmation-policies')).toHaveLength(1))
  })
})
