import { fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { session } from '../../api/session.ts'
import { makeContact } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { ContactDetailsForm } from './ContactDetailsForm.tsx'

const EMPLOYEE_ID = 'e1000000-0000-0000-0000-000000000001'
const CONTACT_URL = `/api/employees/${EMPLOYEE_ID}/contact`

describe('ContactDetailsForm', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  it('loads the existing contact values, with same-as-current checked', async () => {
    stub.on('get', CONTACT_URL, () => ({ data: ok(makeContact()) }))
    renderAsUser(<ContactDetailsForm employeeId={EMPLOYEE_ID} />)

    await waitFor(() => expect(stub.callsTo('get', CONTACT_URL)).toHaveLength(1))

    // Wait for the loaded record to actually populate the fields before asserting their values (the GET
    // may have fired before the effect applied the data, which is racy under parallel load).
    expect(await screen.findByDisplayValue('9876543210')).toBeInTheDocument()
    expect(screen.getByLabelText(/^Official Email$/)).toHaveValue('nadia.farrell@demo01.test')
    expect(screen.getByLabelText(/^Mobile Number$/)).toHaveValue('9876543210')
    expect(screen.getByLabelText(/^Alternate Mobile Number$/)).toHaveValue('9123456780')
    expect(screen.getByLabelText(/^Personal Email$/)).toHaveValue('personal@example.com')
    expect(screen.getByLabelText(/^Alternate Email$/)).toHaveValue('alternate@example.com')
    // The same-as flag is owned by the Address tab, so it is not surfaced here as a checkbox.
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  })

  it('sends every field through the contact upsert request', async () => {
    stub.on('get', CONTACT_URL, () => ({ data: ok(makeContact()) }))
    stub.on('put', CONTACT_URL, (call) => ({ data: ok(call.body as never) }))
    renderAsUser(<ContactDetailsForm employeeId={EMPLOYEE_ID} />)

    await waitFor(() => expect(stub.callsTo('get', CONTACT_URL)).toHaveLength(1))
    await screen.findByLabelText(/^Mobile Number$/)

    fireEvent.change(screen.getByLabelText(/^Official Email$/), { target: { value: 'nadia.updated@demo01.test' } })
    fireEvent.change(screen.getByLabelText(/^Mobile Number$/), { target: { value: '5551002000' } })
    await userEvent.click(screen.getByRole('button', { name: 'UPDATE' }))

    await waitFor(() => expect(stub.callsTo('put', CONTACT_URL)).toHaveLength(1))
    const body = stub.callsTo('put', CONTACT_URL)[0]!.body as Record<string, unknown>
    expect(body.officialEmail).toBe('nadia.updated@demo01.test')
    expect(body.officialPhone).toBe('5551002000')
    expect(body.personalPhone).toBe('9123456780')
    expect(body.personalEmail).toBe('personal@example.com')
    expect(body.alternateEmail).toBe('alternate@example.com')
    expect(body.sameAsCurrentAddress).toBe(true)

    expect(await screen.findByText('Contact Details updated successfully.')).toBeInTheDocument()
  })

  it('shows SAVE when no contact exists yet, then UPDATE after a save', async () => {
    stub.on('get', CONTACT_URL, () => ({ status: 404, data: fail('Contact record not found for this employee.') }))
    stub.on('put', CONTACT_URL, (call) => ({ data: ok(call.body as never) }))
    renderAsUser(<ContactDetailsForm employeeId={EMPLOYEE_ID} />)

    await waitFor(() => expect(stub.callsTo('get', CONTACT_URL)).toHaveLength(1))
    expect(await screen.findByRole('button', { name: 'SAVE' })).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText(/^Mobile Number$/), { target: { value: '5551112222' } })
    await userEvent.click(screen.getByRole('button', { name: 'SAVE' }))

    await waitFor(() => expect(stub.callsTo('put', CONTACT_URL)).toHaveLength(1))
    expect(await screen.findByRole('button', { name: 'UPDATE' })).toBeInTheDocument()
  })
})
