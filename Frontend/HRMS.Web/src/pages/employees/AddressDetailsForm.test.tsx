import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { session } from '../../api/session.ts'
import { makeAddress, makeContact, paged } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { AddressDetailsForm } from './AddressDetailsForm.tsx'

const EMPLOYEE_ID = 'e1000000-0000-0000-0000-000000000001'
const ADDRESSES_URL = `/api/employees/${EMPLOYEE_ID}/addresses`
const CONTACT_URL = `/api/employees/${EMPLOYEE_ID}/contact`
const COUNTRIES_URL = '/api/countries'
const STATES_URL = '/api/states'

function stubMasters(stub: StubAdapter): void {
  stub.on('get', COUNTRIES_URL, () => ({
    data: ok(paged([{ id: 'c1', code: 'IN', name: 'India', isActive: true, createdDate: '2026-01-01T00:00:00Z' }])),
  }))
  stub.on('get', STATES_URL, () => ({
    data: ok(paged([{ id: 's1', countryId: 'c1', countryName: 'India', code: 'MH', name: 'Maharashtra', isActive: true, cityCount: 1, createdDate: '2026-01-01T00:00:00Z' }])),
  }))
}

function group(name: string): HTMLElement {
  return screen.getByRole('group', { name })
}

describe('AddressDetailsForm', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()
    stubMasters(stub)
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  it('loads the current and permanent addresses, and disables permanent when same-as-current is set', async () => {
    stub.on('get', ADDRESSES_URL, () => ({ data: ok([makeAddress('Current'), makeAddress('Permanent')]) }))
    stub.on('get', CONTACT_URL, () => ({ data: ok(makeContact()) }))
    renderAsUser(<AddressDetailsForm employeeId={EMPLOYEE_ID} />)

    await waitFor(() => expect(stub.callsTo('get', ADDRESSES_URL)).toHaveLength(1))

    const current = group('Current Address')
    expect(await within(current).findByLabelText(/^Address Line 1$/)).toHaveValue('14 Kildare Street')
    expect(await within(current).findByLabelText(/^City \/ Town$/)).toHaveValue('Mumbai')
    expect(within(current).getByLabelText(/^District$/)).toHaveValue('Mumbai City')
    expect(within(current).getByLabelText(/^Postal Code \/ Pincode$/)).toHaveValue('400001')

    // The contact flag is read on load: permanent mirrors current and is locked.
    await waitFor(() =>
      expect(screen.getByText('Permanent Address is kept the same as Current Address.')).toBeInTheDocument(),
    )
    const permanent = group('Permanent Address')
    await waitFor(() => expect(within(permanent).getByLabelText(/^Address Line 1$/)).toBeDisabled())
  })

  it('when same-as-current is set, saving sends the permanent row mirroring the current one and persists the flag', async () => {
    stub.on('get', ADDRESSES_URL, () => ({ data: ok([makeAddress('Current'), makeAddress('Permanent')]) }))
    stub.on('get', CONTACT_URL, () => ({ data: ok(makeContact()) }))
    stub.on('post', ADDRESSES_URL, (call) => ({ data: ok(call.body as never) }))
    stub.on('put', CONTACT_URL, (call) => ({ data: ok(call.body as never) }))
    renderAsUser(<AddressDetailsForm employeeId={EMPLOYEE_ID} />)

    await waitFor(() => expect(stub.callsTo('get', ADDRESSES_URL)).toHaveLength(1))

    const current = group('Current Address')
    fireEvent.change(await within(current).findByLabelText(/^City \/ Town$/), { target: { value: 'Pune' } })

    await userEvent.click(screen.getByRole('button', { name: 'UPDATE' }))

    await waitFor(() => expect(stub.callsTo('post', ADDRESSES_URL)).toHaveLength(2))
    const posts = stub.callsTo('post', ADDRESSES_URL)
    const currentCall = posts.find((c) => (c.body as Record<string, unknown>).addressType === 'Current')!
    const permanentCall = posts.find((c) => (c.body as Record<string, unknown>).addressType === 'Permanent')!
    expect((currentCall.body as Record<string, unknown>).city).toBe('Pune')
    expect((currentCall.body as Record<string, unknown>).country).toBe('India')
    expect((currentCall.body as Record<string, unknown>).district).toBe('Mumbai City')
    // Permanent mirrors the (edited) current.
    expect((permanentCall.body as Record<string, unknown>).city).toBe('Pune')
    expect((permanentCall.body as Record<string, unknown>).district).toBe('Mumbai City')

    // The same-as flag is persisted via the contact record, echoing the other contact fields so they survive.
    expect(stub.callsTo('put', CONTACT_URL)).toHaveLength(1)
    const contactBody = stub.callsTo('put', CONTACT_URL)[0]!.body as Record<string, unknown>
    expect(contactBody.sameAsCurrentAddress).toBe(true)
    expect(contactBody.officialPhone).toBe('9876543210')

    expect(await screen.findByText('Address Details updated successfully.')).toBeInTheDocument()
  })

  it('when same-as-current is unset, the permanent block is editable and keeps its own values', async () => {
    stub.on('get', ADDRESSES_URL, () => ({ data: ok([makeAddress('Current'), makeAddress('Permanent')]) }))
    stub.on('get', CONTACT_URL, () => ({ data: ok(makeContact({ sameAsCurrentAddress: false })) }))
    stub.on('post', ADDRESSES_URL, (call) => ({ data: ok(call.body as never) }))
    stub.on('put', CONTACT_URL, (call) => ({ data: ok(call.body as never) }))
    renderAsUser(<AddressDetailsForm employeeId={EMPLOYEE_ID} />)

    await waitFor(() => expect(stub.callsTo('get', ADDRESSES_URL)).toHaveLength(1))

    const permanent = group('Permanent Address')
    const addressLine = await within(permanent).findByLabelText(/^Address Line 1$/)
    expect(addressLine).toBeEnabled()
    expect(screen.queryByText('Permanent Address is kept the same as Current Address.')).not.toBeInTheDocument()

    fireEvent.change(await within(permanent).findByLabelText(/^City \/ Town$/), { target: { value: 'Bengaluru' } })

    await userEvent.click(screen.getByRole('button', { name: 'UPDATE' }))

    await waitFor(() => expect(stub.callsTo('post', ADDRESSES_URL)).toHaveLength(2))
    const posts = stub.callsTo('post', ADDRESSES_URL)
    const permanentCall = posts.find((c) => (c.body as Record<string, unknown>).addressType === 'Permanent')!
    expect((permanentCall.body as Record<string, unknown>).city).toBe('Bengaluru')
    expect((permanentCall.body as Record<string, unknown>).district).toBe('Mumbai City')

    // With the flag unset it is persisted as false and the contact fields are still echoed through.
    expect(stub.callsTo('put', CONTACT_URL)).toHaveLength(1)
    expect((stub.callsTo('put', CONTACT_URL)[0]!.body as Record<string, unknown>).sameAsCurrentAddress).toBe(false)
  })

  it('checking the checkbox copies current into permanent, disables it, and keeps it mirrored as current changes', async () => {
    stub.on('get', ADDRESSES_URL, () => ({ data: ok([makeAddress('Current'), makeAddress('Permanent')]) }))
    stub.on('get', CONTACT_URL, () => ({ data: ok(makeContact({ sameAsCurrentAddress: false })) }))
    stub.on('post', ADDRESSES_URL, (call) => ({ data: ok(call.body as never) }))
    stub.on('put', CONTACT_URL, (call) => ({ data: ok(call.body as never) }))
    renderAsUser(<AddressDetailsForm employeeId={EMPLOYEE_ID} />)

    await waitFor(() => expect(stub.callsTo('get', ADDRESSES_URL)).toHaveLength(1))

    // Start independent: the checkbox is off and permanent is editable.
    const checkbox = screen.getByLabelText(/^Permanent Address same as Current Address$/)
    const permanent = group('Permanent Address')
    expect(checkbox).not.toBeChecked()
    expect(await within(permanent).findByLabelText(/^City \/ Town$/)).toBeEnabled()

    // Checking the box copies the current values into the permanent block and locks it.
    await userEvent.click(checkbox)
    expect(checkbox).toBeChecked()
    expect(within(permanent).getByLabelText(/^City \/ Town$/)).toBeDisabled()
    expect(within(permanent).getByLabelText(/^Address Line 1$/)).toHaveValue('14 Kildare Street')

    // Editing current while checked mirrors the change into permanent immediately.
    const current = group('Current Address')
    fireEvent.change(await within(current).findByLabelText(/^City \/ Town$/), { target: { value: 'Pune' } })
    expect(within(permanent).getByLabelText(/^City \/ Town$/)).toHaveValue('Pune')

    // Saving persists the flag as true and mirrors permanent to the current row.
    await userEvent.click(screen.getByRole('button', { name: 'UPDATE' }))
    await waitFor(() => expect(stub.callsTo('post', ADDRESSES_URL)).toHaveLength(2))
    expect((stub.callsTo('put', CONTACT_URL)[0]!.body as Record<string, unknown>).sameAsCurrentAddress).toBe(true)
    const permanentCall = stub
      .callsTo('post', ADDRESSES_URL)
      .find((c) => (c.body as Record<string, unknown>).addressType === 'Permanent')!
    expect((permanentCall.body as Record<string, unknown>).city).toBe('Pune')
  })

  it('shows SAVE when there are no addresses yet, then UPDATE after a save', async () => {
    stub.on('get', ADDRESSES_URL, () => ({ data: ok([]) }))
    stub.on('get', CONTACT_URL, () => ({ status: 404, data: fail('Contact record not found for this employee.') }))
    stub.on('post', ADDRESSES_URL, (call) => ({ data: ok(call.body as never) }))
    stub.on('put', CONTACT_URL, (call) => ({ data: ok(call.body as never) }))
    renderAsUser(<AddressDetailsForm employeeId={EMPLOYEE_ID} />)

    await waitFor(() => expect(stub.callsTo('get', ADDRESSES_URL)).toHaveLength(1))
    expect(await screen.findByRole('button', { name: 'SAVE' })).toBeInTheDocument()

    const current = group('Current Address')
    fireEvent.change(await within(current).findByLabelText(/^City \/ Town$/), { target: { value: 'Mumbai' } })

    await userEvent.click(screen.getByRole('button', { name: 'SAVE' }))

    await waitFor(() => expect(stub.callsTo('post', ADDRESSES_URL)).toHaveLength(2))
    // With no contact yet, saving still persists the (unchecked) flag alongside the addresses.
    expect(stub.callsTo('put', CONTACT_URL)).toHaveLength(1)
    expect(await screen.findByRole('button', { name: 'UPDATE' })).toBeInTheDocument()
  })
})
