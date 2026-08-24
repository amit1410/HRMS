import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { session } from '../../api/session.ts'
import { Permissions } from '../../auth/permissions.ts'
import { makeDepartment, makeDesignation, makeUser, paged } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { LookupListPage } from './LookupListPage.tsx'
import { departmentsModule, designationsModule, type LookupModule } from './lookupModules.ts'

/**
 * The departments / designations list.
 *
 * Most of these run against `departmentsModule`, because one screen serves both and asserting the shared
 * behaviour twice would be the same test twice over. What genuinely differs per module — the wording and
 * the four permissions — is covered for each.
 *
 * The other thing under test is that none of the work is done client-side: every page, search, sort and
 * filter has to become a query parameter, so most of these assert the *request* rather than a rearrangement
 * of the twenty rows already in hand.
 */

const ALL_LOOKUP_PERMISSIONS = [
  ...Object.values(Permissions.department),
  ...Object.values(Permissions.designation),
]

interface RenderOptions {
  route?: string
  permissions?: string[]
  /** History state, for the message a save leaves behind. */
  state?: unknown
}

function renderList(module: LookupModule, options: RenderOptions = {}) {
  const { route = module.basePath, permissions = ALL_LOOKUP_PERMISSIONS, state } = options
  return renderAsUser(<LookupListPage module={module} />, {
    route,
    state,
    user: makeUser({ permissions }),
  })
}

/** The params of the most recent list request — what the screen actually asked the API for. */
function lastQuery(stub: StubAdapter, url = '/api/departments'): Record<string, unknown> {
  const calls = stub.callsTo('get', url)
  return calls[calls.length - 1]?.params ?? {}
}

/** The first row's action, since two rows means two of each button. */
function firstAction(name: string): HTMLElement {
  return screen.getAllByRole(name === 'Edit' ? 'link' : 'button', { name })[0] as HTMLElement
}

describe('LookupListPage', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()
    stub.on('get', '/api/departments', () => ({
      data: ok(
        paged([
          makeDepartment({ description: 'Builds and runs the product' }),
          makeDepartment({
            id: 'd2000000-0000-0000-0000-000000000002',
            code: 'PPL',
            name: 'People',
            employeeCount: 0,
            isActive: false,
          }),
        ]),
      ),
    }))
    stub.on('get', '/api/designations', () => ({ data: ok(paged([makeDesignation()])) }))
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  it('shows each record with its code, description, count and status', async () => {
    renderList(departmentsModule)

    expect(await screen.findByText('Engineering')).toBeInTheDocument()
    expect(screen.getByText('ENG')).toBeInTheDocument()
    expect(screen.getByText('Builds and runs the product')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()

    // A unit nobody is assigned to is not the same as one that has been retired, so both are shown and
    // the count is not mistaken for the status.
    expect(screen.getByText('People')).toBeInTheDocument()
    expect(screen.getByText('0')).toBeInTheDocument()
    expect(screen.getByText('Inactive')).toBeInTheDocument()
  })

  it('names the count column for what it counts, per module', async () => {
    const { unmount } = renderList(departmentsModule)
    expect(await screen.findByRole('button', { name: 'Employees' })).toBeInTheDocument()

    unmount()
    renderList(designationsModule)

    // A designation is *held by* an employee rather than having employees assigned to it.
    expect(await screen.findByRole('button', { name: 'Holders' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Employees' })).not.toBeInTheDocument()
  })

  it('asks its own endpoint, sorted by name rather than by code', async () => {
    renderList(departmentsModule)
    await screen.findByText('Engineering')

    expect(lastQuery(stub)).toEqual({
      page: 1,
      pageSize: 20,
      // Someone scanning for "Engineering" is not thinking about "ENG".
      sortBy: 'name',
      sortDescending: false,
    })
    // The two lists take the same query shape, so a screen that ignored `module` would still look right.
    expect(stub.callsTo('get', '/api/designations')).toHaveLength(0)
  })

  it('sends the paging, search, sort and filter it found in the URL', async () => {
    renderList(departmentsModule, {
      route: '/departments?page=2&pageSize=50&search=eng&sortBy=code&dir=desc&isActive=false',
    })
    await screen.findByText('Engineering')

    expect(lastQuery(stub)).toEqual({
      page: 2,
      pageSize: 50,
      search: 'eng',
      sortBy: 'code',
      sortDescending: true,
      // `false` and "not filtering" are different requests, so this one has to survive to the wire.
      isActive: false,
    })
    // A shared link shows its search term straight away rather than after the debounce.
    expect(screen.getByRole('searchbox')).toHaveValue('eng')
  })

  it('discards a sort field the API would refuse instead of forwarding it', async () => {
    renderList(departmentsModule, { route: '/departments?sortBy=salary&dir=desc' })
    await screen.findByText('Engineering')

    // The API answers an unknown sort field with a 400, so forwarding it would leave a hand-edited link
    // permanently broken rather than merely unsorted.
    expect(lastQuery(stub).sortBy).toBe('name')
  })

  it('clamps an oversized page size rather than being refused for it', async () => {
    renderList(departmentsModule, { route: '/departments?pageSize=5000' })
    await screen.findByText('Engineering')

    expect(lastQuery(stub).pageSize).toBe(100)
  })

  it('sorts by asking the server, and flips the column it is already sorted by', async () => {
    renderList(departmentsModule)
    await screen.findByText('Engineering')

    await userEvent.click(screen.getByRole('button', { name: 'Code' }))
    await waitFor(() => expect(lastQuery(stub).sortBy).toBe('code'))
    expect(lastQuery(stub).sortDescending).toBe(false)

    await userEvent.click(screen.getByRole('button', { name: 'Code' }))
    await waitFor(() => expect(lastQuery(stub).sortDescending).toBe(true))

    // Announced, not only arrowed: the header says which way it is sorted.
    expect(screen.getByRole('columnheader', { name: 'Code' })).toHaveAttribute(
      'aria-sort',
      'descending',
    )
    expect(screen.getByRole('columnheader', { name: 'Name' })).toHaveAttribute('aria-sort', 'none')
  })

  it('searches once for a word, not once per keystroke', async () => {
    renderList(departmentsModule)
    await screen.findByText('Engineering')
    const before = stub.callsTo('get', '/api/departments').length

    await userEvent.type(screen.getByRole('searchbox'), 'eng')

    // Each list endpoint runs a LIKE over several columns; three of those for three letters is work the
    // server should not be asked to do.
    await waitFor(() => expect(lastQuery(stub).search).toBe('eng'))
    expect(stub.callsTo('get', '/api/departments')).toHaveLength(before + 1)
  })

  it('filters by status, and offers a way back out of it', async () => {
    renderList(departmentsModule)
    await screen.findByText('Engineering')

    await userEvent.selectOptions(screen.getByLabelText('Status'), 'false')
    await waitFor(() => expect(lastQuery(stub).isActive).toBe(false))

    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }))
    // Absent rather than `isActive=true`: "all statuses" is the parameter not being sent at all.
    await waitFor(() => expect(lastQuery(stub)).not.toHaveProperty('isActive'))
  })

  it('returns to page one when the filter changes', async () => {
    renderList(departmentsModule, { route: '/departments?page=4' })
    await screen.findByText('Engineering')

    await userEvent.selectOptions(screen.getByLabelText('Status'), 'true')

    // Page 4 of a narrower result set is an empty table for a result set that has rows.
    await waitFor(() => expect(lastQuery(stub).isActive).toBe(true))
    expect(lastQuery(stub).page).toBe(1)
  })

  it('offers only the actions the user’s permissions cover', async () => {
    renderList(departmentsModule, { permissions: [Permissions.department.view] })
    await screen.findByText('Engineering')

    // Absent rather than disabled: a greyed-out Delete still says the action exists and invites a support
    // question. This is the HRManager's actual view of departments.
    expect(screen.queryByRole('link', { name: 'Edit' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'New department' })).not.toBeInTheDocument()
  })

  it('reads its own module’s permissions, not any lookup permission', async () => {
    renderList(departmentsModule, {
      // Everything for designations, view-only for departments.
      permissions: [Permissions.department.view, ...Object.values(Permissions.designation)],
    })
    await screen.findByText('Engineering')

    expect(screen.queryByRole('link', { name: 'New department' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
  })

  it('opens the form at the module’s own route', async () => {
    renderList(departmentsModule, { route: '/departments?page=2&search=eng' })
    await screen.findByText('Engineering')

    // The view they are looking at travels in history state rather than the href, so Cancel and a save
    // come back to this page of this search — asserted where that return path is read, in returnTo.
    expect(screen.getByRole('link', { name: 'New department' })).toHaveAttribute(
      'href',
      '/departments/new',
    )
    expect(firstAction('Edit')).toHaveAttribute(
      'href',
      '/departments/d1000000-0000-0000-0000-000000000001/edit',
    )
  })

  it('deletes a record after naming it, then says so and re-reads the list', async () => {
    stub.on('delete', '/api/departments/d1000000-0000-0000-0000-000000000001', () => ({
      data: ok(true),
    }))
    renderList(departmentsModule)
    await screen.findByText('Engineering')

    await userEvent.click(firstAction('Delete'))

    const dialog = screen.getByRole('alertdialog')
    // Named, so it cannot be the wrong row: "this item" would not have told them which.
    expect(dialog).toHaveTextContent('Engineering')
    expect(dialog).toHaveTextContent('ENG')
    // The refusal that may be coming is explained before the request, not after it comes back.
    expect(dialog).toHaveTextContent('Mark it inactive instead')

    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    expect(await screen.findByText('Engineering was deleted.')).toBeInTheDocument()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    // Re-read rather than spliced out of the array in hand, which would leave the page nineteen rows long
    // while the count above it still said twenty.
    expect(stub.callsTo('get', '/api/departments').length).toBeGreaterThan(1)
  })

  it('keeps the question open, in the server’s words, when a delete is refused', async () => {
    stub.on('delete', '/api/departments/d1000000-0000-0000-0000-000000000001', () => ({
      status: 409,
      data: fail('Engineering has 12 employees assigned to it.'),
    }))
    renderList(departmentsModule)
    await screen.findByText('Engineering')

    await userEvent.click(firstAction('Delete'))
    const dialog = screen.getByRole('alertdialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    // The count comes from the API, so the explanation is exact; a generic "could not delete" would send
    // them looking for the reason.
    expect(await screen.findByText('Engineering has 12 employees assigned to it.')).toBeInTheDocument()
    expect(screen.getByRole('alertdialog')).toBeInTheDocument()
    // The row is still there, because it was not deleted.
    expect(within(screen.getByRole('table')).getByText('Engineering')).toBeInTheDocument()
  })

  it('steps back a page when the last row on it is deleted', async () => {
    stub.on('get', '/api/departments', () => ({
      data: ok(paged([makeDepartment()], { page: 3, totalCount: 41 })),
    }))
    stub.on('delete', '/api/departments/d1000000-0000-0000-0000-000000000001', () => ({
      data: ok(true),
    }))
    renderList(departmentsModule, { route: '/departments?page=3' })
    await screen.findByText('Engineering')

    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    await userEvent.click(
      within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Delete' }),
    )

    // Staying on page 3 of 40 rows would show an empty table for a result set that still has rows.
    await waitFor(() => expect(lastQuery(stub).page).toBe(2))
  })

  it('shows the message a save left behind, once', async () => {
    renderList(departmentsModule, { state: { flash: 'Engineering was updated.' } })

    expect(await screen.findByText('Engineering was updated.')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Dismiss' }))
    expect(screen.queryByText('Engineering was updated.')).not.toBeInTheDocument()
  })

  it('offers a way forward from an empty list and a way back from an empty filter', async () => {
    stub.on('get', '/api/departments', () => ({ data: ok(paged([])) }))

    const { unmount } = renderList(departmentsModule)
    expect(await screen.findByText('No departments yet')).toBeInTheDocument()
    // In the header and in the empty state, because the empty state is where the user is looking.
    expect(screen.getAllByRole('link', { name: 'New department' })).toHaveLength(2)

    unmount()
    renderList(departmentsModule, { route: '/departments?search=zzz' })

    // A filtered empty result is a different situation: the answer is to widen the search, not to add a
    // department that may already exist under another name. The header's New stays — it always applies.
    expect(await screen.findByText('Nothing matches those filters')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Clear filters' })).toHaveLength(2)
    expect(screen.getAllByRole('link', { name: 'New department' })).toHaveLength(1)
  })

  it('explains a refused read instead of showing an empty table', async () => {
    stub.on('get', '/api/departments', () => ({
      status: 403,
      data: fail('You do not have permission to perform this action.'),
    }))
    renderList(departmentsModule)

    expect(await screen.findByText('Could not load this')).toBeInTheDocument()
    // "No departments yet" would have been a lie about the data rather than about the permission.
    expect(screen.queryByText('No departments yet')).not.toBeInTheDocument()
    // A 403 will not resolve itself, so there is nothing to retry.
    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument()
  })

  it('never sends a tenant id: the server takes it from the token', async () => {
    renderList(departmentsModule, { route: '/departments?search=eng&isActive=true' })
    await screen.findByText('Engineering')

    const sent = JSON.stringify(stub.calls).toLowerCase()
    expect(sent).not.toContain('tenant')
    expect(stub.calls.every((call) => call.authorization === 'Bearer access-1')).toBe(true)
  })
})
