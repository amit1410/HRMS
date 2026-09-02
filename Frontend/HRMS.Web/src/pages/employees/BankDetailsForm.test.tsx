import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { session } from '../../api/session.ts'
import { makeBankDetail, makeBankDetailEdit, makeBankLookup } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { BankDetailsForm } from './BankDetailsForm.tsx'

const EMPLOYEE_ID = 'e1000000-0000-0000-0000-000000000001'
const MASTERS_URL = '/api/master-data/banks'
const LIST_URL = `/api/employees/${EMPLOYEE_ID}/bank-details`

const ACTIVE_BANK = makeBankLookup({ id: 'b1', code: 'SBI', name: 'State Bank of India', isActive: true })
const INACTIVE_BANK = makeBankLookup({ id: 'b2', code: 'AXIS', name: 'Axis Bank', isActive: false })

describe('BankDetailsForm', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()
    stub.on('get', MASTERS_URL, () => ({ data: ok([ACTIVE_BANK, INACTIVE_BANK]) }))
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  it('renders only active banks in the picker — an inactive bank is not offered', async () => {
    stub.on('get', LIST_URL, () => ({ data: ok([]) }))
    renderAsUser(<BankDetailsForm employeeId={EMPLOYEE_ID} />)

    expect(await screen.findByText('No bank accounts have been added yet.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Add Bank' }))

    const bank = screen.getByLabelText(/^Bank/)
    const options = within(bank).getAllByRole('option').map((o) => o.textContent)
    expect(options).toContain('SBI - State Bank of India')
    expect(options).not.toContain('AXIS - Axis Bank')
    expect(bank).toBeRequired()

    const statuses = within(screen.getByLabelText(/^Status$/)).getAllByRole('option').map((o) => o.textContent)
    expect(statuses).toEqual(['Active'])
  })

  it('adds a new bank account and posts the bankId (never a typed-in bank name)', async () => {
    stub.on('get', LIST_URL, () => ({ data: ok([]) }))
    stub.on('post', LIST_URL, (call) => ({ data: ok(call.body as never) }))
    renderAsUser(<BankDetailsForm employeeId={EMPLOYEE_ID} />)

    await userEvent.click(await screen.findByRole('button', { name: 'Add Bank' }))
    await userEvent.selectOptions(screen.getByLabelText(/^Bank/), 'b1')
    fireEvent.change(screen.getByLabelText(/^Account holder name/), { target: { value: 'Nadia Farrell' } })
    fireEvent.change(screen.getByLabelText(/^Account number/), { target: { value: 'ACC-100' } })
    fireEvent.change(screen.getByLabelText(/^IFSC code$/), { target: { value: 'SBIN0000001' } })
    fireEvent.change(screen.getByLabelText(/^Branch name$/), { target: { value: 'Main Branch' } })
    fireEvent.change(screen.getByLabelText(/^Effective from$/), { target: { value: '2026-02-01' } })

    await userEvent.click(screen.getByRole('button', { name: 'SAVE' }))

    await waitFor(() => expect(stub.callsTo('post', LIST_URL)).toHaveLength(1))
    const body = stub.callsTo('post', LIST_URL)[0]!.body as Record<string, unknown>
    expect(body.bankId).toBe('b1')
    expect(body.accountHolderName).toBe('Nadia Farrell')
    expect(body.accountNumber).toBe('ACC-100')
    expect(body.accountType).toBe('Savings')
    expect(body.accountPurpose).toBe('Salary')
    expect(body.status).toBe('Active')
    expect(body.ifscCode).toBe('SBIN0000001')
    expect(body.branchName).toBe('Main Branch')
    expect(body.effectiveFrom).toBe('2026-02-01')
    // The request must carry the FK, not a hard-coded bank name.
    expect(body).not.toHaveProperty('bankName')
    expect(await screen.findByText('Bank Details added successfully.')).toBeInTheDocument()
  })

  it('edits an existing record and updates it via PUT on the record endpoint', async () => {
    const record = makeBankDetail({ id: 'bk1', bankId: 'b1', accountPurpose: 'Salary' })
    stub.on('get', LIST_URL, () => ({ data: ok([record]) }))
    stub.on('get', `${LIST_URL}/bk1/sensitive-details`, () => ({
      data: ok(makeBankDetailEdit({ id: 'bk1', bankId: 'b1', accountPurpose: 'Salary' })),
    }))
    stub.on('put', `${LIST_URL}/bk1`, (call) => ({ data: ok(call.body as never) }))
    renderAsUser(<BankDetailsForm employeeId={EMPLOYEE_ID} />)

    await userEvent.click(await screen.findByRole('button', { name: 'Edit' }))
    expect(within(screen.getByLabelText(/^Status$/)).getAllByRole('option').map((o) => o.textContent))
      .toEqual(['Active', 'Frozen', 'Closed'])
    fireEvent.change(screen.getByLabelText(/^Account holder name/), { target: { value: 'Nadia F.' } })

    await userEvent.click(screen.getByRole('button', { name: 'UPDATE' }))

    await waitFor(() => expect(stub.callsTo('put', `${LIST_URL}/bk1`)).toHaveLength(1))
    const body = stub.callsTo('put', `${LIST_URL}/bk1`)[0]!.body as Record<string, unknown>
    expect(body.accountHolderName).toBe('Nadia F.')
    expect(body.bankId).toBe('b1')
    expect(await screen.findByText('Bank Details updated successfully.')).toBeInTheDocument()
    // After an update the editor closes and the list returns.
    expect(screen.queryByRole('button', { name: 'UPDATE' })).not.toBeInTheDocument()
  })

  it('soft-deletes via a confirm dialog, then shows immutable history with no actions', async () => {
    let deactivated = false
    const record = makeBankDetail({ id: 'bk1', bankId: 'b1', accountPurpose: 'Salary' })
    stub.on('get', LIST_URL, () => ({
      data: ok([deactivated ? { ...record, status: 'Closed', isActive: false } : record]),
    }))
    stub.on('delete', `${LIST_URL}/bk1`, () => {
      deactivated = true
      return { data: ok(true) }
    })
    renderAsUser(<BankDetailsForm employeeId={EMPLOYEE_ID} />)

    await userEvent.click(await screen.findByRole('button', { name: 'Deactivate' }))

    const dialog = screen.getByRole('alertdialog')
    expect(
      within(dialog).getByText(
        /Are you sure you want to deactivate this bank account/i,
      ),
    ).toBeInTheDocument()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Deactivate' }))

    await waitFor(() => expect(stub.callsTo('delete', `${LIST_URL}/bk1`)).toHaveLength(1))
    expect(await screen.findByText('Bank account deactivated.')).toBeInTheDocument()
    // Refetched record is now immutable history: no edit or destructive action is available.
    expect(await screen.findByText('Historical (Closed)')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Deactivate' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
  })

  it('shows a historical record without edit or deactivate actions', async () => {
    const record = makeBankDetail({
      id: 'bk1',
      bankId: 'b2',
      accountPurpose: 'Salary',
      status: 'Closed',
      isActive: false,
    })
    stub.on('get', LIST_URL, () => ({ data: ok([record]) }))
    renderAsUser(<BankDetailsForm employeeId={EMPLOYEE_ID} />)

    expect(await screen.findByText('Historical (Closed)')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Deactivate' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
    // The bank label resolves from the master lookup, not a hard-coded string.
    expect(await screen.findByText(/AXIS - Axis Bank/)).toBeInTheDocument()
  })

  it('does not offer an active purpose that is already in use (one active per purpose)', async () => {
    const salary = makeBankDetail({ id: 'bk1', bankId: 'b1', accountPurpose: 'Salary', isActive: true })
    const gratuity = makeBankDetail({ id: 'bk2', bankId: 'b1', accountPurpose: 'Gratuity', isActive: true })
    stub.on('get', LIST_URL, () => ({ data: ok([salary, gratuity]) }))
    stub.on('get', `${LIST_URL}/bk1/sensitive-details`, () => ({
      data: ok(makeBankDetailEdit({ id: 'bk1', bankId: 'b1', accountPurpose: 'Salary' })),
    }))
    renderAsUser(<BankDetailsForm employeeId={EMPLOYEE_ID} />)

    await userEvent.click(await screen.findByRole('button', { name: 'Add Bank' }))

    const purpose = screen.getByLabelText(/^Account purpose$/)
    const options = within(purpose).getAllByRole('option').map((o) => o.textContent)
    expect(options).toContain('Pension')
    expect(options).not.toContain('Salary')
    expect(options).not.toContain('Gratuity')
  })

  it('keeps the editing record’s own purpose selectable (so an edit is not blocked)', async () => {
    const salary = makeBankDetail({ id: 'bk1', bankId: 'b1', accountPurpose: 'Salary', isActive: true })
    const gratuity = makeBankDetail({ id: 'bk2', bankId: 'b1', accountPurpose: 'Gratuity', isActive: true })
    stub.on('get', LIST_URL, () => ({ data: ok([salary, gratuity]) }))
    stub.on('get', `${LIST_URL}/bk1/sensitive-details`, () => ({
      data: ok(makeBankDetailEdit({ id: 'bk1', bankId: 'b1', accountPurpose: 'Salary' })),
    }))
    renderAsUser(<BankDetailsForm employeeId={EMPLOYEE_ID} />)

    const editButtons = await screen.findAllByRole('button', { name: 'Edit' })
    await userEvent.click(editButtons[0]!) // Editing the Salary record.

    const purpose = screen.getByLabelText(/^Account purpose$/)
    const options = within(purpose).getAllByRole('option').map((o) => o.textContent)
    expect(options).toContain('Salary')
    expect(options).not.toContain('Gratuity')
  })

  it('surfaces a server refusal (conflict) as an error instead of saving', async () => {
    stub.on('get', LIST_URL, () => ({ data: ok([]) }))
    stub.on('post', LIST_URL, () => ({
      status: 409,
      data: fail('An active bank account already exists for the Salary purpose.'),
    }))
    renderAsUser(<BankDetailsForm employeeId={EMPLOYEE_ID} />)

    await userEvent.click(await screen.findByRole('button', { name: 'Add Bank' }))
    await userEvent.selectOptions(screen.getByLabelText(/^Bank/), 'b1')
    fireEvent.change(screen.getByLabelText(/^Account holder name/), { target: { value: 'Nadia Farrell' } })
    fireEvent.change(screen.getByLabelText(/^Account number/), { target: { value: 'ACC-100' } })

    await userEvent.click(screen.getByRole('button', { name: 'SAVE' }))

    expect(
      await screen.findByText('An active bank account already exists for the Salary purpose.'),
    ).toBeInTheDocument()
  })
})
