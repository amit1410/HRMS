import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, afterEach, describe, expect, it } from 'vitest'
import { LeaveTypesPage } from './LeaveTypesPage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser, paged } from '../../test/fixtures.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'

const token = 'revision-1'
const item = { id: 'type-1', code: 'CL', name: 'Casual Leave', description: 'Short absence', defaultUnit: 'Day' as const, isPaid: true, isActive: true, createdDate: '2027-01-01T00:00:00Z', modifiedDate: null, concurrencyToken: token }

describe('LeaveTypesPage', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  function renderPage(permissions = [Permissions.leave.typeManage]) { return renderAsUser(<LeaveTypesPage />, { user: makeUser({ permissions }) }) }
  function list(rows = [item]) { stub.on('get', '/api/leave-types', () => ({ data: ok(paged(rows)) })) }

  it('renders a loading state before the list arrives', () => {
    stub.on('get', '/api/leave-types', () => ({ data: ok(paged([])), delay: true }))
    renderPage()
    expect(screen.getByRole('status')).toHaveTextContent('Loading Leave Types')
  })

  it('renders rows, paid state, unit and status', async () => {
    list(); renderPage()
    expect(await screen.findByText('Casual Leave')).toBeInTheDocument()
    const row = screen.getByRole('row', { name: /CL Casual Leave/ })
    expect(within(row).getByText('Paid')).toBeInTheDocument()
    expect(within(row).getByText('Day')).toBeInTheDocument()
    expect(within(row).getByText('Active')).toBeInTheDocument()
  })

  it('renders a useful empty state', async () => {
    list([]); renderPage()
    expect(await screen.findByText('No Leave Types have been configured yet.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add Leave Type' })).toBeInTheDocument()
  })

  it('sends search and inactive filters to the backend', async () => {
    list([]); renderPage()
    await userEvent.type(await screen.findByLabelText('Search Leave Types'), 'casual')
    await userEvent.selectOptions(screen.getByLabelText('Leave Type status'), 'inactive')
    await waitFor(() => expect(stub.calls.filter(call => call.method === 'get').at(-1)?.params).toMatchObject({ search: 'casual', isActive: false }))
  })

  it('validates required fields before create', async () => {
    list([]); renderPage(); await userEvent.click(await screen.findByRole('button', { name: 'Add Leave Type' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save Leave Type' }))
    expect(screen.getByLabelText(/Code/)).toBeInvalid()
    expect(stub.callsTo('post', '/api/leave-types')).toHaveLength(0)
  })

  it('creates a Leave Type and refreshes the list', async () => {
    list([]); stub.on('post', '/api/leave-types', call => ({ data: ok({ ...item, code: call.body && typeof call.body === 'object' && 'code' in call.body ? String(call.body.code) : 'EL' }) }))
    renderPage(); await userEvent.click(await screen.findByRole('button', { name: 'Add Leave Type' }))
    await userEvent.type(screen.getByLabelText(/Code/), 'EL'); await userEvent.type(screen.getByLabelText(/Name/), 'Earned Leave'); await userEvent.click(screen.getByRole('button', { name: 'Save Leave Type' }))
    await waitFor(() => expect(stub.callsTo('post', '/api/leave-types')).toHaveLength(1))
    expect(await screen.findByText('Leave Type created.')).toBeInTheDocument()
  })

  it('shows duplicate-code validation from the API', async () => {
    list([]); stub.on('post', '/api/leave-types', () => ({ status: 409, data: fail("LeaveType code 'CL' already exists.", [{ field: 'code', message: 'A Leave Type with this code already exists.' }]) }))
    renderPage(); await userEvent.click(await screen.findByRole('button', { name: 'Add Leave Type' })); await userEvent.type(screen.getByLabelText(/Code/), 'CL'); await userEvent.type(screen.getByLabelText(/Name/), 'Casual Leave'); await userEvent.click(screen.getByRole('button', { name: 'Save Leave Type' }))
    expect(await screen.findByText('A Leave Type with this code already exists.')).toBeInTheDocument()
  })

  it('loads the current record for editing and preserves the token', async () => {
    list(); stub.on('get', '/api/leave-types/type-1', () => ({ data: ok(item) })); stub.on('put', '/api/leave-types/type-1', () => ({ data: ok(item) }))
    renderPage(); await userEvent.click(await screen.findByRole('button', { name: 'Edit' })); await screen.findByDisplayValue('Casual Leave'); await userEvent.clear(screen.getByLabelText(/Name/)); await userEvent.type(screen.getByLabelText(/Name/), 'Updated Leave'); await userEvent.click(screen.getByRole('button', { name: 'Save Leave Type' }))
    await waitFor(() => expect(stub.callsTo('put', '/api/leave-types/type-1')[0]?.body).toMatchObject({ concurrencyToken: token, name: 'Updated Leave' }))
  })

  it('confirms deactivation and preserves history', async () => {
    list(); stub.on('put', '/api/leave-types/type-1', () => ({ data: ok({ ...item, isActive: false }) }))
    renderPage(); const row = await screen.findByRole('row', { name: /CL Casual Leave/ }); await userEvent.click(within(row).getByRole('button', { name: 'Deactivate' })); const dialog = screen.getByRole('alertdialog'); expect(dialog).toHaveTextContent('Historical references remain preserved.'); await userEvent.click(within(dialog).getByRole('button', { name: 'Deactivate' }))
    await waitFor(() => expect(stub.callsTo('put', '/api/leave-types/type-1')[0]?.body).toMatchObject({ isActive: false, concurrencyToken: token }))
  })

  it('shows a 409 conflict without discarding the form', async () => {
    list(); stub.on('get', '/api/leave-types/type-1', () => ({ data: ok(item) })); stub.on('put', '/api/leave-types/type-1', () => ({ status: 409, data: fail('Configuration changed by another user. Reload before saving.') }))
    renderPage(); await userEvent.click(await screen.findByRole('button', { name: 'Edit' })); await screen.findByDisplayValue('Casual Leave'); await userEvent.click(screen.getByRole('button', { name: 'Save Leave Type' })); expect(await screen.findByText(/changed by another user/)).toBeInTheDocument(); expect(screen.getByDisplayValue('Casual Leave')).toBeInTheDocument()
  })

  it('hides mutation controls without TypeManage', async () => {
    list(); renderPage([])
    expect(await screen.findByText('Casual Leave')).toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: '+ Add Leave Type' })).not.toBeInTheDocument()
  })
})
