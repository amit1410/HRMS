import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { session } from '../../api/session.ts'
import { Permissions } from '../../auth/permissions.ts'
import {
  HR_MANAGER_PERMISSIONS,
  MANAGER_PERMISSIONS,
  makeDepartment,
  makeDesignation,
  makeEmployee,
  makeUser,
  paged,
} from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { EmployeesPage } from './EmployeesPage.tsx'

/**
 * The employee directory.
 *
 * Two themes run through these. The first is that the API does the work: page, search, sort and all three
 * filters have to arrive as query parameters, because a screen that reordered the twenty rows in hand would
 * be sorted within the page and unsorted across it.
 *
 * The second is that a permission decides whether something is *rendered*, not whether it is enabled —
 * rendering the department filter is what fires the request that fills it, so for a Manager, who holds
 * `Employee.View` and neither reference permission, the select must not exist rather than exist and 403.
 */

const ALL_EMPLOYEE_PERMISSIONS = [
  ...Object.values(Permissions.employee),
  Permissions.department.view,
  Permissions.designation.view,
]

const EMPLOYEE_URL = '/api/employees/e1000000-0000-0000-0000-000000000001'

interface RenderOptions {
  route?: string
  permissions?: string[]
  state?: unknown
}

function renderPage({ route = '/employees', permissions = ALL_EMPLOYEE_PERMISSIONS, state }: RenderOptions = {}) {
  return renderAsUser(<EmployeesPage />, { route, state, user: makeUser({ permissions }) })
}

/** The params of the most recent list request — what the screen actually asked the API for. */
function lastQuery(stub: StubAdapter, url = '/api/employees'): Record<string, unknown> {
  const calls = stub.callsTo('get', url)
  return calls[calls.length - 1]?.params ?? {}
}

describe('EmployeesPage', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()
    stub.on('get', '/api/employees', () => ({
      data: ok(
        paged([
          makeEmployee(),
          makeEmployee({
            id: 'e2000000-0000-0000-0000-000000000002',
            employeeCode: 'EMP-002',
            fullName: 'Tomás Byrne',
            email: 'tomas.byrne@demo01.test',
            departmentName: 'People',
            designationName: 'Recruiter',
            dateOfJoining: '2024-11-01',
            status: 'Resigned',
          }),
        ]),
      ),
    }))
    stub.on('get', '/api/departments', () => ({
      data: ok(paged([makeDepartment(), makeDepartment({ id: 'd2', code: 'PPL', name: 'People' })])),
    }))
    stub.on('get', '/api/designations', () => ({ data: ok(paged([makeDesignation()])) }))
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  it('shows each person with the details that identify them', async () => {
    renderPage()

    // Scoped to the table throughout: "Engineering" and "Active" are also filter options, and a query that
    // matched either of those would pass whether or not the row rendered.
    const table = within(await screen.findByRole('table'))
    expect(table.getByText('Nadia Farrell')).toBeInTheDocument()
    expect(table.getByText('EMP-001')).toBeInTheDocument()
    // The email under the name, because two people can share a name and nobody shares an address.
    expect(table.getByText('nadia.farrell@demo01.test')).toBeInTheDocument()
    expect(table.getByText('Engineering')).toBeInTheDocument()
    expect(table.getByText('Senior Software Engineer')).toBeInTheDocument()
    // "14 Mar 2023", not the API's "2023-03-14" and not a day out from parsing it as UTC.
    expect(table.getByText('14 Mar 2023')).toBeInTheDocument()
    expect(table.getByText('Active')).toBeInTheDocument()
    expect(table.getByText('Resigned')).toBeInTheDocument()
  })

  it('asks for page one by first name, and fills both reference filters', async () => {
    renderPage()
    await screen.findByText('Nadia Farrell')

    expect(lastQuery(stub)).toEqual({
      page: 1,
      pageSize: 20,
      // The name column a person scans, and the only name field the API will sort on.
      sortBy: 'firstName',
      sortDescending: false,
    })

    // One page of the API's maximum for each select, sorted by name and including inactive ones: an
    // inactive department that still has employees is a legitimate thing to filter a list by.
    expect(lastQuery(stub, '/api/departments')).toEqual({ pageSize: 100, sortBy: 'name' })
    expect(lastQuery(stub, '/api/designations')).toEqual({ pageSize: 100, sortBy: 'name' })
    expect(await screen.findByRole('option', { name: 'People' })).toBeInTheDocument()
  })

  it('sends the paging, search, sort and all three filters it found in the URL', async () => {
    renderPage({
      route:
        '/employees?page=3&pageSize=50&search=far&sortBy=department&dir=desc' +
        '&departmentId=d1000000-0000-0000-0000-000000000001' +
        '&designationId=g1000000-0000-0000-0000-000000000001&status=Resigned',
    })
    await screen.findByText('Nadia Farrell')

    expect(lastQuery(stub)).toEqual({
      page: 3,
      pageSize: 50,
      search: 'far',
      // `department`, not `departmentName`: the validator's whitelist names the navigation property.
      sortBy: 'department',
      sortDescending: true,
      departmentId: 'd1000000-0000-0000-0000-000000000001',
      designationId: 'g1000000-0000-0000-0000-000000000001',
      status: 'Resigned',
    })
  })

  it('drops a status a hand-edited URL invented', async () => {
    renderPage({ route: '/employees?status=OnHoliday' })
    await screen.findByText('Nadia Farrell')

    // The API's enum has three members and answers a fourth with a 400, so this is not forwarded.
    expect(lastQuery(stub)).not.toHaveProperty('status')
  })

  it('filters by department, and offers a way back out of it', async () => {
    renderPage()
    await screen.findByText('Nadia Farrell')
    await screen.findByRole('option', { name: 'People' })

    await userEvent.selectOptions(screen.getByLabelText('Department'), 'd2')
    await waitFor(() => expect(lastQuery(stub).departmentId).toBe('d2'))

    await userEvent.selectOptions(screen.getByLabelText('Status'), 'Terminated')
    await waitFor(() => expect(lastQuery(stub).status).toBe('Terminated'))

    await userEvent.click(screen.getByRole('button', { name: 'Clear filters' }))
    await waitFor(() => expect(lastQuery(stub)).not.toHaveProperty('status'))
    // Both go, not just the one that was changed last.
    expect(lastQuery(stub)).not.toHaveProperty('departmentId')
  })

  it('searches once for a word, not once per keystroke', async () => {
    renderPage()
    await screen.findByText('Nadia Farrell')
    const before = stub.callsTo('get', '/api/employees').length

    await userEvent.type(screen.getByRole('searchbox'), 'far')

    // The endpoint runs a LIKE over code, name and email; three of those for three letters is work the
    // server should not be asked to do.
    await waitFor(() => expect(lastQuery(stub).search).toBe('far'))
    expect(stub.callsTo('get', '/api/employees')).toHaveLength(before + 1)
  })

  it('sorts by asking the server, and flips the column it already sorted by', async () => {
    renderPage()
    await screen.findByText('Nadia Farrell')

    await userEvent.click(screen.getByRole('button', { name: 'Joined' }))
    await waitFor(() => expect(lastQuery(stub).sortBy).toBe('dateOfJoining'))
    expect(lastQuery(stub).sortDescending).toBe(false)

    await userEvent.click(screen.getByRole('button', { name: 'Joined' }))
    await waitFor(() => expect(lastQuery(stub).sortDescending).toBe(true))

    expect(screen.getByRole('columnheader', { name: 'Joined' })).toHaveAttribute(
      'aria-sort',
      'descending',
    )
  })

  it('hides the reference filters from someone who cannot read them', async () => {
    // Only `Employee.View` — a Manager without the two reference permissions.
    renderPage({ permissions: [Permissions.employee.view] })
    await screen.findByText('Nadia Farrell')

    expect(screen.queryByLabelText('Department')).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Designation')).not.toBeInTheDocument()
    // Rendering the select is what would have fired the request, so not rendering it is the enforcement.
    expect(stub.callsTo('get', '/api/departments')).toHaveLength(0)
    expect(stub.callsTo('get', '/api/designations')).toHaveLength(0)
    // Status is derived from the employee record itself, so it stays.
    expect(screen.getByLabelText('Status')).toBeInTheDocument()
  })

  it('gives a Manager the directory and nothing to change in it', async () => {
    renderPage({ permissions: [...MANAGER_PERMISSIONS] })
    await screen.findByText('Nadia Farrell')

    expect(screen.queryByRole('link', { name: 'New employee' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Edit' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
    // Reading a page of rows and walking out with the whole directory are separate permissions.
    expect(screen.queryByRole('button', { name: 'Export CSV' })).not.toBeInTheDocument()
  })

  it('gives an HR Manager everything except the delete', async () => {
    renderPage({ permissions: [...HR_MANAGER_PERMISSIONS] })
    await screen.findByText('Nadia Farrell')

    expect(screen.getByRole('link', { name: 'New employee' })).toHaveAttribute(
      'href',
      '/employees/new',
    )
    expect(screen.getAllByRole('link', { name: 'Edit' })).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Export CSV' })).toBeInTheDocument()
    // Absent rather than disabled: a greyed Delete still says the action exists.
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
  })

  it('exports the view on screen, without its paging', async () => {
    const objectUrl = Object.getOwnPropertyDescriptor(URL, 'createObjectURL')
    const revokeUrl = Object.getOwnPropertyDescriptor(URL, 'revokeObjectURL')
    Object.defineProperty(URL, 'createObjectURL', {
      value: vi.fn(() => 'blob:hrms/csv'),
      configurable: true,
    })
    Object.defineProperty(URL, 'revokeObjectURL', { value: vi.fn(), configurable: true })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
    stub.on('get', '/api/employees/export', () => ({ data: new Blob(['code\r\nEMP-001\r\n']) }))

    try {
      renderPage({ route: '/employees?page=3&search=far&status=Active&sortBy=email&dir=desc' })
      await screen.findByText('Nadia Farrell')

      await userEvent.click(screen.getByRole('button', { name: 'Export CSV' }))

      await waitFor(() => expect(stub.callsTo('get', '/api/employees/export')).toHaveLength(1))
      // `ExportAsync` applies the filters and then takes everything that matches, so page three of the
      // file is not a thing anyone asked for — the file is the whole filtered set.
      expect(lastQuery(stub, '/api/employees/export')).toEqual({
        search: 'far',
        sortBy: 'email',
        sortDescending: true,
        status: 'Active',
      })
    } finally {
      click.mockRestore()
      resetProperty(URL, 'createObjectURL', objectUrl)
      resetProperty(URL, 'revokeObjectURL', revokeUrl)
    }
  })

  it('deletes an employee after naming them, then says so and re-reads the list', async () => {
    stub.on('delete', EMPLOYEE_URL, () => ({ data: ok(true) }))
    renderPage()
    await screen.findByText('Nadia Farrell')

    await userEvent.click(screen.getAllByRole('button', { name: 'Delete' })[0] as HTMLElement)

    const dialog = screen.getByRole('alertdialog')
    expect(dialog).toHaveTextContent('Nadia Farrell')
    expect(dialog).toHaveTextContent('EMP-001')
    // The better answer is offered before the destructive one is taken, along with the refusal that may
    // be coming.
    expect(dialog).toHaveTextContent('usually a status change instead')
    expect(dialog).toHaveTextContent('refused while anyone still reports to them')

    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    expect(await screen.findByText('Nadia Farrell was deleted.')).toBeInTheDocument()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
    expect(stub.callsTo('get', '/api/employees').length).toBeGreaterThan(1)
  })

  it('keeps the question open, in the server’s words, when a delete is refused', async () => {
    stub.on('delete', EMPLOYEE_URL, () => ({
      status: 409,
      data: fail('3 employees report to Nadia Farrell. Reassign them first.'),
    }))
    renderPage()
    await screen.findByText('Nadia Farrell')

    await userEvent.click(screen.getAllByRole('button', { name: 'Delete' })[0] as HTMLElement)
    const dialog = screen.getByRole('alertdialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    // The count comes from the API, so the explanation names what has to be done rather than only what
    // could not be.
    expect(await screen.findByText('3 employees report to Nadia Farrell. Reassign them first.')).toBeInTheDocument()
    expect(screen.getByRole('alertdialog')).toBeInTheDocument()
    expect(within(screen.getByRole('table')).getByText('Nadia Farrell')).toBeInTheDocument()
  })

  it('steps back a page when the last row on it is deleted', async () => {
    stub.on('get', '/api/employees', () => ({
      data: ok(paged([makeEmployee()], { page: 2, totalCount: 21 })),
    }))
    stub.on('delete', EMPLOYEE_URL, () => ({ data: ok(true) }))
    renderPage({ route: '/employees?page=2' })
    await screen.findByText('Nadia Farrell')

    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    await userEvent.click(
      within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Delete' }),
    )

    // Page one, which is the page that still has rows on it.
    await waitFor(() => expect(lastQuery(stub).page).toBe(1))
  })

  it('shows the message a save left behind, once', async () => {
    renderPage({ state: { flash: 'Nadia Farrell was updated.' } })

    expect(await screen.findByText('Nadia Farrell was updated.')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Dismiss' }))
    expect(screen.queryByText('Nadia Farrell was updated.')).not.toBeInTheDocument()
  })

  it('tells an empty directory apart from an empty filter', async () => {
    stub.on('get', '/api/employees', () => ({ data: ok(paged([])) }))

    const { unmount } = renderPage()
    expect(await screen.findByText('No employees yet')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'New employee' })).toHaveLength(2)

    unmount()
    renderPage({ route: '/employees?search=zzz' })

    // "No employees yet" under a search term would say the directory is empty when it is the filter that
    // is too narrow.
    expect(await screen.findByText('Nobody matches those filters')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Clear filters' })).toHaveLength(2)
  })

  it('keeps the filters usable when the directory itself fails to load', async () => {
    stub.on('get', '/api/employees', () => ({ status: 500, data: fail('Server error') }))
    renderPage()

    expect(await screen.findByText('Could not load this')).toBeInTheDocument()
    // A 500 may well be transient, unlike a 403.
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
    // The toolbar is outside the table, so the user can narrow the query and try a smaller one.
    expect(screen.getByRole('searchbox')).toBeInTheDocument()
  })

  it('still lists employees when a reference filter cannot be filled', async () => {
    stub.on('get', '/api/designations', () => ({ status: 500, data: fail('Server error') }))
    renderPage()

    // The list is the point of the screen; a failed option load must not take it down with it.
    expect(await screen.findByText('Nadia Farrell')).toBeInTheDocument()
    const designation = screen.getByLabelText('Designation')
    await waitFor(() => expect(within(designation).getAllByRole('option')).toHaveLength(1))
    expect(within(designation).getByRole('option')).toHaveTextContent('All job titles')
  })

  it('never sends a tenant id: the server takes it from the token', async () => {
    renderPage({ route: '/employees?search=far&status=Active' })
    await screen.findByText('Nadia Farrell')

    const sent = JSON.stringify(stub.calls).toLowerCase()
    expect(sent).not.toContain('tenant')
    expect(stub.calls.every((call) => call.authorization === 'Bearer access-1')).toBe(true)
  })
})

function resetProperty(
  target: object,
  property: string,
  descriptor: PropertyDescriptor | undefined,
): void {
  if (descriptor) {
    Object.defineProperty(target, property, descriptor)
  } else {
    delete (target as Record<string, unknown>)[property]
  }
}
