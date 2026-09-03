import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { session } from '../api/session.ts'
import { Permissions } from '../auth/permissions.ts'
import { DashboardPage } from './DashboardPage.tsx'
import { Header } from '../layout/Header.tsx'
import { createRef } from 'react'
import {
  makeDepartment,
  makeEmployee,
  makeUser,
  MANAGER_PERMISSIONS,
  paged,
} from '../test/fixtures.ts'
import { renderAsUser } from '../test/renderWith.tsx'
import { fail, installStubAdapter, ok, type StubAdapter } from '../test/stubAdapter.ts'

/**
 * What the dashboard shows is decided by permissions, and the decision is whether a panel is *rendered*
 * — because rendering is what issues the request. These tests assert both halves: the panel is absent,
 * and the endpoint was never called.
 */
describe('DashboardPage', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()

    stub.on('get', '/api/employees', (call) => {
      if (call.params.pageSize === 1) {
        return { data: ok(paged([], { totalCount: call.params.status === 'Active' ? 39 : 42 })) }
      }
      return {
        data: ok(
          paged(
            [
              makeEmployee({ fullName: 'Nadia Farrell', departmentName: 'Product' }),
              makeEmployee({
                id: 'e2',
                employeeCode: 'EMP-002',
                fullName: 'Tomas Lind',
                departmentName: 'People',
                status: 'Resigned',
              }),
            ],
            { totalCount: 42 },
          ),
        ),
      }
    })

    stub.on('get', '/api/departments', (call) =>
      call.params.pageSize === 1
        ? { data: ok(paged([], { totalCount: 3 })) }
        : {
            data: ok(
              paged([
                makeDepartment({ name: 'Product', employeeCount: 25 }),
                makeDepartment({ id: 'd2', code: 'PPL', name: 'People', employeeCount: 5 }),
              ]),
            ),
          },
    )

    stub.on('get', '/api/designations', () => ({ data: ok(paged([], { totalCount: 7 })) }))
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  it('shows the counts, the recent hires and the headcount split to an HR manager', async () => {
    renderAsUser(<DashboardPage />)

    expect(await screen.findByText('42')).toBeInTheDocument()
    expect(screen.getByText('39')).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
    expect(screen.getByText('7')).toBeInTheDocument()

    expect(await screen.findByText('Nadia Farrell')).toBeInTheDocument()
    expect(screen.getByText('Tomas Lind')).toBeInTheDocument()
    expect(screen.getByText('Resigned')).toBeInTheDocument()

    expect(await screen.findByText('25')).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()
  })

  it('names the tenant, so a user with accounts in two cannot mistake one for the other', async () => {
    renderAsUser(
      <Header onMenu={() => undefined} menuButtonRef={createRef<HTMLButtonElement>()} menuOpen={false} />,
      { user: makeUser({ tenantName: 'Contoso Retail' }) },
    )

    expect(await screen.findByText(/Contoso Retail/)).toBeInTheDocument()
  })

  it('never sends a tenant id: the server takes it from the token', async () => {
    renderAsUser(<DashboardPage />)

    await screen.findByText('42')
    const params = JSON.stringify(stub.calls.map((call) => call.params)).toLowerCase()
    expect(params).not.toContain('tenant')
  })

  it('offers the export only to a user who holds Employee.Export', async () => {
    renderAsUser(<DashboardPage />)

    expect(await screen.findByRole('button', { name: 'Export CSV' })).toBeInTheDocument()
  })

  it('hides the export from a manager, and issues no export request', async () => {
    renderAsUser(<DashboardPage />, {
      user: makeUser({ roles: ['Manager'], permissions: MANAGER_PERMISSIONS }),
    })

    await screen.findByText('42')
    expect(screen.queryByRole('button', { name: 'Export CSV' })).not.toBeInTheDocument()
    expect(stub.callsTo('get', '/api/employees/export')).toHaveLength(0)
  })

  it('asks for nothing at all when the user holds no view permissions', async () => {
    renderAsUser(<DashboardPage />, {
      user: makeUser({ roles: ['Employee'], permissions: [] }),
    })

    expect(await screen.findByText('Nothing to show yet')).toBeInTheDocument()
    // A rendered-but-empty panel would have fired a request that could only come back 403.
    expect(stub.calls).toHaveLength(0)
  })

  it('shows only the panels a partial permission set covers', async () => {
    renderAsUser(<DashboardPage />, {
      user: makeUser({ permissions: [Permissions.department.view] }),
    })

    expect(await screen.findByText('Headcount by department')).toBeInTheDocument()
    expect(screen.queryByText('Recent hires')).not.toBeInTheDocument()
    expect(stub.callsTo('get', '/api/employees')).toHaveLength(0)
  })

  it('keeps the rest of the page when one panel fails', async () => {
    stub.on('get', '/api/employees', () => ({ status: 500, data: fail('Server error') }))
    renderAsUser(<DashboardPage />)

    // Departments still arrive…
    expect(await screen.findByText('25')).toBeInTheDocument()
    // …while the employee tiles say "unavailable" rather than a wrong number.
    await waitFor(() => expect(screen.getAllByText('—')).toHaveLength(2))
    expect(screen.getByText('Could not load this')).toBeInTheDocument()
  })

  it('downloads the CSV, filename and all', async () => {
    const objectUrl = Object.getOwnPropertyDescriptor(URL, 'createObjectURL')
    const revokeUrl = Object.getOwnPropertyDescriptor(URL, 'revokeObjectURL')
    Object.defineProperty(URL, 'createObjectURL', {
      value: vi.fn(() => 'blob:hrms/csv'),
      configurable: true,
    })
    Object.defineProperty(URL, 'revokeObjectURL', { value: vi.fn(), configurable: true })
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)

    stub.on('get', '/api/employees/export', () => ({
      data: new Blob(['code,name\r\nEMP-001,Nadia Farrell\r\n'], { type: 'text/csv' }),
      headers: { 'content-disposition': 'attachment; filename="employees-2026-08-22.csv"' },
    }))

    try {
      renderAsUser(<DashboardPage />)
      await userEvent.click(await screen.findByRole('button', { name: 'Export CSV' }))

      await waitFor(() => expect(click).toHaveBeenCalledTimes(1))
      expect(stub.callsTo('get', '/api/employees/export')).toHaveLength(1)
    } finally {
      click.mockRestore()
      resetProperty(URL, 'createObjectURL', objectUrl)
      resetProperty(URL, 'revokeObjectURL', revokeUrl)
    }
  })

  it('explains a refused export instead of downloading an error page', async () => {
    stub.on('get', '/api/employees/export', () => ({
      status: 400,
      data: new Blob(
        [JSON.stringify(fail('The export is limited to 10,000 rows. Narrow the filters.'))],
        { type: 'application/json' },
      ),
    }))

    renderAsUser(<DashboardPage />)
    await userEvent.click(await screen.findByRole('button', { name: 'Export CSV' }))

    expect(await screen.findByText(/limited to 10,000 rows/)).toBeInTheDocument()
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
