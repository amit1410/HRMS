import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { Route, Routes, useLocation } from 'react-router-dom'
import { session } from '../../api/session.ts'
import { Permissions } from '../../auth/permissions.ts'
import type { FlashState } from '../../hooks/useFlash.ts'
import {
  makeAddress,
  makeEmployee,
  makeEmployeeDetail,
  makeEmployeeSensitiveDetails,
  makeUser,
  paged,
} from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { EmployeeFormPage } from './EmployeeFormPage.tsx'

const EMPLOYEE_ID = 'e1000000-0000-0000-0000-000000000001'
const EMPLOYEE_URL = `/api/employees/${EMPLOYEE_ID}`
const SENSITIVE_URL = `${EMPLOYEE_URL}/sensitive-details`
const NEW_CREATE_URL = '/api/employees/personal-details'
const NEW_UPDATE_URL = `/api/employees/${EMPLOYEE_ID}/personal-details`
const NEW_ROUTE = '/employees/new'
const EDIT_ROUTE = `/employees/${EMPLOYEE_ID}/edit`

function ListProbe() {
  const location = useLocation()
  const flash = (location.state as FlashState | null)?.flash
  return (
    <div>
      <p>Landed on {`${location.pathname}${location.search}`}</p>
      {flash !== undefined && <p>Handed over: {flash}</p>}
    </div>
  )
}

function renderForm({ route = NEW_ROUTE }: { route?: string } = {}) {
  return renderAsUser(
    <Routes>
      <Route path="/employees" element={<ListProbe />} />
      <Route path="/employees/new" element={<EmployeeFormPage />} />
      <Route path="/employees/:id/edit" element={<EmployeeFormPage />} />
    </Routes>,
    { route },
  )
}

function field(label: string): HTMLElement {
  return screen.getByLabelText(new RegExp(`^${label}\\*?$`))
}

function sentBody(stub: StubAdapter, method: 'post' | 'put', url: string): unknown {
  return stub.callsTo(method, url)[0]?.body
}

describe('EmployeeFormPage (Personal Details)', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()

    stub.on('get', EMPLOYEE_URL, () => ({ data: ok(makeEmployeeDetail()) }))
    stub.on('get', SENSITIVE_URL, () => ({ data: ok(makeEmployeeSensitiveDetails()) }))
    stub.on('get', '/api/countries', () => ({ data: ok(paged([])) }))
    stub.on('get', '/api/states', () => ({ data: ok(paged([])) }))
    stub.on('get', '/api/cities', () => ({ data: ok(paged([])) }))
    stub.on('get', '/api/employees', (call) => {
      const search = String(call.params.search ?? '')
      if (search !== '') return { data: ok(paged([])) }
      return { data: ok(paged([makeEmployee()])) }
    })
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  describe('creating', () => {
    it('allows Personal Details SAVE without the sensitive-edit permission', async () => {
      stub.on('post', NEW_CREATE_URL, () => ({
        status: 201,
        data: ok(makeEmployeeDetail({ employeeCode: '' })),
      }))
      renderAsUser(
        <Routes>
          <Route path="/employees/new" element={<EmployeeFormPage />} />
        </Routes>,
        {
          route: NEW_ROUTE,
          user: makeUser({
            permissions: [
              Permissions.employee.view,
              Permissions.employee.create,
              Permissions.department.view,
              Permissions.designation.view,
            ],
          }),
        },
      )

      await userEvent.type(field('First name'), 'Ravi')
      await userEvent.type(field('Last name'), 'Menon')
      fireEvent.change(field('Date of joining'), { target: { value: '2024-02-01' } })

      const save = screen.getByRole('button', { name: 'SAVE' })
      expect(save).toBeEnabled()
      await userEvent.click(save)

      await waitFor(() => expect(stub.callsTo('post', NEW_CREATE_URL)).toHaveLength(1))
      expect(await screen.findByText(/Employee created/)).toBeInTheDocument()
    })

    it('shows New Hire for the employee code and only the Personal Details sections', () => {
      renderForm()

      expect(screen.getByRole('heading', { name: 'New employee', level: 1 })).toBeInTheDocument()
      expect(screen.getByText('Add someone to the directory')).toBeInTheDocument()
      expect(screen.getByText('New Hire')).toBeInTheDocument()
      expect(field('First name')).toHaveValue('')
      expect(field('Last name')).toHaveValue('')
      expect(screen.getByRole('button', { name: 'SAVE' })).toBeInTheDocument()
      expect(screen.getByRole('link', { name: 'Cancel' })).toBeInTheDocument()
    })

    it('does not offer department, designation, reporting manager or email', async () => {
      renderForm()
      await screen.findByLabelText(/^First name/)

      expect(screen.queryByLabelText(/^Department/)).not.toBeInTheDocument()
      expect(screen.queryByLabelText(/^Designation/)).not.toBeInTheDocument()
      expect(screen.queryByLabelText(/^Reporting manager/)).not.toBeInTheDocument()
      expect(screen.queryByLabelText(/^Email/)).not.toBeInTheDocument()
    })

    it('creates then flips in place to UPDATE showing the assigned code', async () => {
      stub.on('post', NEW_CREATE_URL, () => ({
        status: 201,
        data: ok(makeEmployeeDetail({ employeeCode: 'WE100' })),
      }))
      renderForm()

      await userEvent.type(field('First name'), 'Ravi')
      await userEvent.type(field('Last name'), 'Menon')
      fireEvent.change(field('Date of joining'), { target: { value: '2024-02-01' } })

      // Create is the only SAVE button on screen at this point.
      await userEvent.click(screen.getByRole('button', { name: 'SAVE' }))

      await waitFor(() => expect(stub.callsTo('post', NEW_CREATE_URL)).toHaveLength(1))
      const body = sentBody(stub, 'post', NEW_CREATE_URL) as Record<string, unknown>
      expect(body.firstName).toBe('Ravi')
      expect(body.lastName).toBe('Menon')
      expect(body.dateOfJoining).toBe('2024-02-01')
      // No employee code is sent: the backend assigns it.
      expect(body).not.toHaveProperty('employeeCode')
      expect(body).not.toHaveProperty('departmentId')
      expect(body).not.toHaveProperty('designationId')

      // The form stays on the page: New Hire is replaced by the generated code and SAVE becomes UPDATE,
      // with an inline success message. No navigation to the list.
      expect(await screen.findByText('WE100')).toBeInTheDocument()
      expect(screen.queryByText('New Hire')).not.toBeInTheDocument()
      expect(screen.getByText('Employee created. Employee code WE100 has been assigned.'))
      expect(screen.getByRole('button', { name: 'UPDATE' })).toBeInTheDocument()
      expect(screen.queryByText(/Landed on \/employees$/)).not.toBeInTheDocument()
      expect(stub.callsTo('get', '/api/employees')).toHaveLength(0)
    })

    it('shows the ESIC number when ESIC is applicable', async () => {
      renderForm()

      expect(screen.queryByLabelText(/^ESIC Number/)).not.toBeInTheDocument()
      fireEvent.change(field('ESIC applicable'), { target: { value: 'Yes' } })
      expect(screen.getByLabelText(/^ESIC Number/)).toBeInTheDocument()
    })

    it('shows an API validation error without losing the entry', async () => {
      stub.on('post', NEW_CREATE_URL, () => ({
        status: 400,
        data: fail('Validation failed.', [
          { field: 'dateOfJoining', message: 'Date of joining is required.' },
        ]),
      }))
      renderForm()
      await userEvent.type(field('First name'), 'Ravi')

      await userEvent.click(screen.getByRole('button', { name: 'SAVE' }))

      expect(await screen.findByText('Date of joining is required.')).toBeInTheDocument()
      expect(field('First name')).toHaveValue('Ravi')
      expect(screen.getByRole('button', { name: 'SAVE' })).toBeEnabled()
    })

    it('leaves without saving on Cancel', async () => {
      renderForm({ route: '/employees/new' })
      await userEvent.click(screen.getByRole('link', { name: 'Cancel' }))

      expect(await screen.findByText('Landed on /employees')).toBeInTheDocument()
      expect(stub.callsTo('post', NEW_CREATE_URL)).toHaveLength(0)
    })
  })

  describe('editing', () => {
    it('waits for the record then fills the form without department or designation', async () => {
      renderForm({ route: EDIT_ROUTE })

      expect(screen.getByRole('heading', { name: 'Edit employee', level: 1 })).toBeInTheDocument()
      expect(await screen.findByDisplayValue('Nadia')).toBeInTheDocument()
      expect(screen.getAllByText('EMP-001').length).toBeGreaterThan(0)
      expect(field('First name')).toHaveValue('Nadia')
      expect(field('Last name')).toHaveValue('Farrell')
      expect(field('Date of birth')).toHaveValue('1991-07-02')
      expect(field('Gender')).toHaveValue('Female')
      expect(field('Date of joining')).toHaveValue('2023-03-14')
      expect(screen.getByRole('button', { name: 'UPDATE' })).toBeInTheDocument()
      expect(screen.queryByLabelText(/^Department/)).not.toBeInTheDocument()
      expect(screen.queryByLabelText(/^Reporting manager/)).not.toBeInTheDocument()
    })

    it('updates only the personal-details endpoint and shows an inline success', async () => {
      stub.on('put', NEW_UPDATE_URL, () => ({
        data: ok(makeEmployeeDetail({ firstName: 'Nadia', lastName: 'Farrell' })),
      }))
      renderForm({ route: EDIT_ROUTE })
      await screen.findByDisplayValue('Nadia')

      await userEvent.clear(field('First name'))
      await userEvent.type(field('First name'), 'Nadia-Marie')
      await userEvent.click(screen.getByRole('button', { name: 'UPDATE' }))

      const body = await waitFor(() => {
        expect(stub.callsTo('put', NEW_UPDATE_URL)).toHaveLength(1)
        return sentBody(stub, 'put', NEW_UPDATE_URL) as Record<string, unknown>
      })
      expect(body.firstName).toBe('Nadia-Marie')
      expect(body.lastName).toBe('Farrell')
      expect(body).not.toHaveProperty('employeeCode')
      expect(body).not.toHaveProperty('departmentId')
      expect(body).not.toHaveProperty('email')

      // The form stays on the page with an inline success message instead of navigating away.
      expect(screen.getByText('Personal details updated successfully for Nadia Farrell.'))
      expect(screen.getByRole('button', { name: 'UPDATE' })).toBeInTheDocument()
      expect(screen.queryByText(/Landed on \/employees$/)).not.toBeInTheDocument()
    })

    it('loads and updates every personal detail field, including PF/Religion/Caste/Aadhaar/PAN/UAN', async () => {
      stub.on('get', EMPLOYEE_URL, () => ({
        data: ok(
          makeEmployeeDetail({
            religion: 'Religion-A',
            caste: 'Caste-A',
            maskedAadhaarNumber: 'XXXX-XXXX-3333',
            maskedPanNumber: 'A****F',
            maskedPfNumber: '******-001',
            maskedUanNumber: '******-001',
          }),
        ),
      }))
      stub.on('get', SENSITIVE_URL, () => ({
        data: ok(makeEmployeeSensitiveDetails({
          aadhaarNumber: '111122223333',
          panNumber: 'ABCDE1234F',
          uanNumber: 'UAN-TEST-001',
          pfNumber: 'PF-TEST-001',
        })),
      }))
      stub.on('put', NEW_UPDATE_URL, () => ({
        data: ok(makeEmployeeDetail({ firstName: 'Nadia', lastName: 'Farrell' })),
      }))
      renderForm({ route: EDIT_ROUTE })
      await screen.findByDisplayValue('Nadia')

      // Every field the GET returns is pre-filled in the edit form, including the four sensitive ones.
      expect(field('Religion')).toHaveValue('Religion-A')
      expect(field('Caste')).toHaveValue('Caste-A')
      expect(field('PF Number')).toHaveValue('PF-TEST-001')
      expect(field('UAN Number')).toHaveValue('UAN-TEST-001')
      expect(field('Aadhaar Number')).toHaveValue('111122223333')
      expect(field('PAN Number')).toHaveValue('ABCDE1234F')

      // Change all six fields and save.
      await userEvent.clear(field('Religion'))
      await userEvent.type(field('Religion'), 'Religion-B')
      await userEvent.clear(field('Caste'))
      await userEvent.type(field('Caste'), 'Caste-B')
      await userEvent.clear(field('PF Number'))
      await userEvent.type(field('PF Number'), 'PF-TEST-002')
      await userEvent.clear(field('UAN Number'))
      await userEvent.type(field('UAN Number'), '999988887777')
      await userEvent.clear(field('Aadhaar Number'))
      await userEvent.type(field('Aadhaar Number'), '444455556666')
      await userEvent.clear(field('PAN Number'))
      await userEvent.type(field('PAN Number'), 'ZYXWV0987Q')
      await userEvent.click(screen.getByRole('button', { name: 'UPDATE' }))

      const body = (await waitFor(() => {
        expect(stub.callsTo('put', NEW_UPDATE_URL)).toHaveLength(1)
        return sentBody(stub, 'put', NEW_UPDATE_URL) as Record<string, unknown>
      })) as Record<string, unknown>
      expect(body.religion).toBe('Religion-B')
      expect(body.caste).toBe('Caste-B')
      expect(body.pfNumber).toBe('PF-TEST-002')
      expect(body.uanNumber).toBe('999988887777')
      expect(body.aadhaarNumber).toBe('444455556666')
      expect(body.panNumber).toBe('ZYXWV0987Q')
    })

    it('explains a record it could not read', async () => {
      stub.on('get', EMPLOYEE_URL, () => ({
        status: 404,
        data: fail('That employee could not be found.'),
      }))
      renderForm({ route: EDIT_ROUTE })

      expect(await screen.findByText('That employee could not be found.')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
      expect(screen.queryByLabelText(/^First name/)).not.toBeInTheDocument()
    })
  })

  it('never sends a tenant id: the server takes it from the token', async () => {
    renderForm({ route: EDIT_ROUTE })
    await screen.findByDisplayValue('Nadia')

    const sent = JSON.stringify(stub.calls).toLowerCase()
    expect(sent).not.toContain('tenant')
    expect(stub.calls.every((call) => call.authorization === 'Bearer access-1')).toBe(true)
  })
})

describe('EmployeeFormPage (tabbed Add/Edit)', () => {
  let stub: StubAdapter

  const ADDRESS_URL = `/api/employees/${EMPLOYEE_ID}/addresses`
  const CONTACT_URL = `/api/employees/${EMPLOYEE_ID}/contact`

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()

    stub.on('get', '/api/countries', () => ({ data: ok(paged([])) }))
    stub.on('get', '/api/states', () => ({ data: ok(paged([])) }))
    stub.on('get', '/api/cities', () => ({ data: ok(paged([])) }))
    stub.on('get', EMPLOYEE_URL, () => ({ data: ok(makeEmployeeDetail()) }))
    stub.on('get', SENSITIVE_URL, () => ({ data: ok(makeEmployeeSensitiveDetails()) }))
    stub.on('get', ADDRESS_URL, () => ({ data: ok([]) }))
    stub.on('get', CONTACT_URL, () => ({ status: 404, data: fail('Contact record not found for this employee.') }))
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  it('shows the three tabs on the Add form, with Address inert until the employee is created', async () => {
    stub.on('post', NEW_CREATE_URL, () => ({
      status: 201,
      data: ok(makeEmployeeDetail({ employeeCode: 'WE100' })),
    }))
    renderForm()

    expect(screen.getByRole('tab', { name: 'Personal Details' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Contact Details' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Address Details' })).toBeInTheDocument()

    // Before the employee row exists the Address tab explains why it is not yet usable.
    await userEvent.click(screen.getByRole('tab', { name: 'Address Details' }))
    expect(await screen.findByText(/Save the Personal Details section first/)).toBeInTheDocument()

    // Back on Personal, create the employee; the Address tab then becomes a real form.
    await userEvent.click(screen.getByRole('tab', { name: 'Personal Details' }))
    await userEvent.type(field('First name'), 'Ravi')
    await userEvent.type(field('Last name'), 'Menon')
    fireEvent.change(field('Date of joining'), { target: { value: '2024-02-01' } })
    await userEvent.click(screen.getByRole('button', { name: 'SAVE' }))
    expect(await screen.findByText('WE100')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('tab', { name: 'Address Details' }))
    expect(await screen.findByRole('button', { name: 'SAVE' })).toBeInTheDocument()
    expect(screen.queryByText(/Save the Personal Details section first/)).not.toBeInTheDocument()
  })

  it('saves address details from the Add form against the newly created employee', async () => {
    stub.on('post', NEW_CREATE_URL, () => ({
      status: 201,
      data: ok(makeEmployeeDetail({ employeeCode: 'WE100' })),
    }))
    stub.on('post', ADDRESS_URL, (call) => ({ data: ok(call.body as never) }))
    stub.on('put', CONTACT_URL, (call) => ({ data: ok(call.body as never) }))
    renderForm()

    await userEvent.click(screen.getByRole('tab', { name: 'Personal Details' }))
    await userEvent.type(field('First name'), 'Ravi')
    await userEvent.type(field('Last name'), 'Menon')
    fireEvent.change(field('Date of joining'), { target: { value: '2024-02-01' } })
    await userEvent.click(screen.getByRole('button', { name: 'SAVE' }))
    expect(await screen.findByText('WE100')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('tab', { name: 'Address Details' }))
    const current = screen.getByRole('group', { name: 'Current Address' })
    fireEvent.change(await within(current).findByLabelText(/^City \/ Town$/), { target: { value: 'Mumbai' } })
    await userEvent.click(screen.getByRole('button', { name: 'SAVE' }))

    await waitFor(() => expect(stub.callsTo('post', ADDRESS_URL)).toHaveLength(2))
    // The address save also persists the (unchecked) same-as flag against the new employee.
    expect(stub.callsTo('put', CONTACT_URL)).toHaveLength(1)
  })

  it('Edit form Address tab loads the existing addresses for that employee', async () => {
    stub.on('get', ADDRESS_URL, () => ({ data: ok([makeAddress('Current')]) }))
    renderForm({ route: EDIT_ROUTE })
    await screen.findByDisplayValue('Nadia')

    await userEvent.click(screen.getByRole('tab', { name: 'Address Details' }))
    const current = screen.getByRole('group', { name: 'Current Address' })
    expect(await within(current).findByLabelText(/^City \/ Town$/)).toHaveValue('Mumbai')
  })
})
