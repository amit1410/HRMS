import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { LeavePeriodsPage } from './LeavePeriodsPage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser, paged } from '../../test/fixtures.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'

const token = 'period-revision-1'
const item = { id: 'period-1', code: '2027', name: 'Calendar 2027', startDate: '2027-01-01', endDate: '2027-12-31', isActive: true, createdDate: '2027-01-01T00:00:00Z', modifiedDate: null, concurrencyToken: token }

describe('LeavePeriodsPage', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  function renderPage(permissions = [Permissions.leave.periodManage]) { return renderAsUser(<LeavePeriodsPage />, { user: makeUser({ permissions }) }) }
  function list(rows = [item]) { stub.on('get', '/api/leave-periods', () => ({ data: ok(paged(rows)) })) }

  it('renders an empty state', async () => { list([]); renderPage(); expect(await screen.findByText('No Leave Periods have been configured yet.')).toBeInTheDocument() })

  it('renders date-only values without timezone shifting', async () => {
    list(); renderPage(); const row = await screen.findByRole('row', { name: /2027 Calendar 2027/ }); const cells = within(row).getAllByRole('cell'); expect(cells[2]).toHaveTextContent(/Jan 1, 2027|1 Jan 2027|01 Jan 2027/); expect(cells[3]).toHaveTextContent(/Dec 31, 2027|31 Dec 2027|31 December 2027/)
  })

  it('supports search and status filtering', async () => {
    list([]); renderPage(); await userEvent.type(await screen.findByLabelText('Search Leave Periods'), 'FY27'); await userEvent.selectOptions(screen.getByLabelText('Leave Period status'), 'inactive'); await waitFor(() => expect(stub.calls.filter(call => call.method === 'get').at(-1)?.params).toMatchObject({ search: 'FY27', isActive: false }))
  })

  it('validates required fields before create', async () => {
    list([]); renderPage(); await userEvent.click(await screen.findByRole('button', { name: 'Add Leave Period' })); await userEvent.click(screen.getByRole('button', { name: 'Save Leave Period' })); expect(screen.getByLabelText(/Code/)).toBeInvalid(); expect(stub.callsTo('post', '/api/leave-periods')).toHaveLength(0)
  })

  it('rejects a start date after the end date', async () => {
    list([]); renderPage(); await userEvent.click(await screen.findByRole('button', { name: 'Add Leave Period' })); await userEvent.type(screen.getByLabelText(/Code/), 'CUSTOM'); await userEvent.type(screen.getByLabelText(/Name/), 'Custom period'); await userEvent.type(screen.getByLabelText(/^Start Date/i), '2027-07-01'); await userEvent.type(screen.getByLabelText(/^End Date/i), '2027-06-30'); await userEvent.click(screen.getByRole('button', { name: 'Save Leave Period' })); const editor = screen.getByRole('form', { name: 'Leave Period editor' }); expect(within(editor).getByText('Start Date must be on or before End Date.')).toBeInTheDocument(); expect(screen.getAllByText('Start Date must be on or before End Date.')).toHaveLength(1); expect(within(editor).getByLabelText(/^Start Date/i)).toHaveValue('2027-07-01'); expect(within(editor).getByLabelText(/^End Date/i)).toHaveValue('2027-06-30'); expect(stub.callsTo('post', '/api/leave-periods')).toHaveLength(0)
  })

  it('creates a period', async () => {
    list([]); stub.on('post', '/api/leave-periods', () => ({ data: ok(item) })); renderPage(); await userEvent.click(await screen.findByRole('button', { name: 'Add Leave Period' })); await userEvent.type(screen.getByLabelText(/Code/), '2027'); await userEvent.type(screen.getByLabelText(/Name/), 'Calendar 2027'); await userEvent.type(screen.getByLabelText(/^Start Date/i), '2027-01-01'); await userEvent.type(screen.getByLabelText(/^End Date/i), '2027-12-31'); await userEvent.click(screen.getByRole('button', { name: 'Save Leave Period' })); await waitFor(() => expect(stub.callsTo('post', '/api/leave-periods')).toHaveLength(1)); expect(await screen.findByText('Leave Period created.')).toBeInTheDocument()
  })

  it('shows duplicate and overlap errors from the backend', async () => {
    list([]); stub.on('post', '/api/leave-periods', () => ({ status: 409, data: fail('This Leave Period overlaps an existing active period.', [{ field: 'dates', message: 'This Leave Period overlaps an existing active period.' }]) })); renderPage(); await userEvent.click(await screen.findByRole('button', { name: 'Add Leave Period' })); await userEvent.type(screen.getByLabelText(/Code/), '2027'); await userEvent.type(screen.getByLabelText(/Name/), 'Calendar 2027'); await userEvent.type(screen.getByLabelText(/^Start Date/i), '2027-01-01'); await userEvent.type(screen.getByLabelText(/^End Date/i), '2027-12-31'); await userEvent.click(screen.getByRole('button', { name: 'Save Leave Period' })); expect(await screen.findByText('This Leave Period overlaps an existing active period.')).toBeInTheDocument()
  })

  it('loads and updates an existing period with its concurrency token', async () => {
    list(); stub.on('get', '/api/leave-periods/period-1', () => ({ data: ok(item) })); stub.on('put', '/api/leave-periods/period-1', () => ({ data: ok(item) })); renderPage(); await userEvent.click(await screen.findByRole('button', { name: 'Edit' })); await screen.findByDisplayValue('Calendar 2027'); await userEvent.click(screen.getByRole('button', { name: 'Save Leave Period' })); await waitFor(() => expect(stub.callsTo('put', '/api/leave-periods/period-1')[0]?.body).toMatchObject({ concurrencyToken: token }))
  })

  it('confirms deactivation', async () => {
    list(); stub.on('put', '/api/leave-periods/period-1', () => ({ data: ok({ ...item, isActive: false }) })); renderPage(); const row = await screen.findByRole('row', { name: /2027 Calendar 2027/ }); await userEvent.click(within(row).getByRole('button', { name: 'Deactivate' })); const dialog = screen.getByRole('alertdialog'); expect(dialog).toHaveTextContent('Historical data remains preserved.'); await userEvent.click(within(dialog).getByRole('button', { name: 'Deactivate' })); await waitFor(() => expect(stub.callsTo('put', '/api/leave-periods/period-1')[0]?.body).toMatchObject({ isActive: false, concurrencyToken: token }))
  })

  it('preserves the form when a stale update receives 409', async () => {
    list(); stub.on('get', '/api/leave-periods/period-1', () => ({ data: ok(item) })); stub.on('put', '/api/leave-periods/period-1', () => ({ status: 409, data: fail('Configuration changed by another user. Reload before saving.') })); renderPage(); await userEvent.click(await screen.findByRole('button', { name: 'Edit' })); await screen.findByDisplayValue('Calendar 2027'); await userEvent.click(screen.getByRole('button', { name: 'Save Leave Period' })); expect(await screen.findByText(/changed by another user/)).toBeInTheDocument(); expect(screen.getByDisplayValue('Calendar 2027')).toBeInTheDocument()
  })

  it('hides mutation controls without PeriodManage', async () => { list(); renderPage([]); expect(await screen.findByText('Calendar 2027')).toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: '+ Add Leave Period' })).not.toBeInTheDocument() })
})
