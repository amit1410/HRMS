import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { session } from '../../api/session.ts'
import { makeUser } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { Permissions } from '../../auth/permissions.ts'
import { BankMasterPage } from './BankMasterPage.tsx'

const LIST_URL = '/api/banks'
const HISTORY_URL = '/api/bank-import/history'

const SBI = {
  id: 'b1',
  code: 'SBI',
  name: 'State Bank of India',
  shortName: 'SBI',
  ifscPrefix: 'SBIN',
  bankType: 'Public',
  country: 'India',
  effectiveFrom: null,
  remarks: null,
  isActive: true,
  employeeAccountCount: 0,
  createdDate: '2026-01-01T00:00:00Z',
  modifiedDate: null,
}

function page(items: unknown[]) {
  return ok({ items, page: 1, pageSize: 25, totalCount: items.length, totalPages: 1 })
}

describe('BankMasterPage', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()
    stub.on('get', HISTORY_URL, () => ({ data: ok([]) }))
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  function admin() {
    return makeUser({
      permissions: [
        Permissions.bankMaster.view,
        Permissions.bankMaster.create,
        Permissions.bankMaster.edit,
        Permissions.bankMaster.activate,
        Permissions.bankMaster.import,
        Permissions.bankMaster.export,
      ],
    })
  }

  it('lists banks returned by the API', async () => {
    stub.on('get', LIST_URL, () => ({ data: page([SBI]) }))
    renderAsUser(<BankMasterPage />, { user: admin() })

    expect(await screen.findByText('State Bank of India')).toBeInTheDocument()
    expect(screen.getByText('SBIN')).toBeInTheDocument()
    expect(screen.getByText('Public')).toBeInTheDocument()
  })

  it('shows management actions to a user who can create and import', async () => {
    stub.on('get', LIST_URL, () => ({ data: page([SBI]) }))
    renderAsUser(<BankMasterPage />, { user: admin() })

    await screen.findByText('State Bank of India')
    expect(screen.getByRole('button', { name: '+ Add bank' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Bulk import' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Export CSV' })).toBeInTheDocument()
  })

  it('hides create, import and export from a view-only user', async () => {
    stub.on('get', LIST_URL, () => ({ data: page([SBI]) }))
    renderAsUser(<BankMasterPage />, { user: makeUser({ permissions: [Permissions.bankMaster.view] }) })

    await screen.findByText('State Bank of India')
    expect(screen.queryByRole('button', { name: '+ Add bank' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Bulk import' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Export CSV' })).not.toBeInTheDocument()
  })

  it('opens the editor and creates a bank', async () => {
    stub.on('get', LIST_URL, () => ({ data: page([]) }))
    let posted: unknown = null
    stub.on('post', LIST_URL, (call) => {
      posted = call.body
      return { data: ok({ ...SBI, id: 'b2', code: 'HDFC', name: 'HDFC Bank' }) }
    })
    renderAsUser(<BankMasterPage />, { user: admin() })

    await userEvent.click(await screen.findByRole('button', { name: '+ Add bank' }))
    await userEvent.type(screen.getByLabelText(/Bank code/), 'HDFC')
    await userEvent.type(screen.getByLabelText(/Bank name/), 'HDFC Bank')
    await userEvent.click(screen.getByRole('button', { name: '✓ Create' }))

    await waitFor(() => expect(posted).not.toBeNull())
    expect((posted as { code: string }).code).toBe('HDFC')
  })
})
