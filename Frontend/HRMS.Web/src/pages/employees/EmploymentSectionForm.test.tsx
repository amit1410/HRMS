import { fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { session } from '../../api/session.ts'
import { Permissions } from '../../auth/permissions.ts'
import {
  makeDepartment,
  makeDesignation,
  makeEmploymentHistory,
  makeMasterLookup,
  makeUser,
  paged,
} from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { EmploymentSectionForm } from './EmploymentSectionForm.tsx'

const EMPLOYEE_ID = 'e1000000-0000-0000-0000-000000000001'
const HISTORY_URL = `/api/employees/${EMPLOYEE_ID}/employment-history`
const DEPARTMENT_ID = 'd1000000-0000-0000-0000-000000000001'
const DESIGNATION_ID = 'g1000000-0000-0000-0000-000000000001'

const REASON = makeMasterLookup({ id: 'pr1', code: 'NEW_HIRE', name: 'New Hire' })
const GRADE = makeMasterLookup({ id: 'gr1', code: 'G1', name: 'Grade 1' })
const WORK_LOCATION = makeMasterLookup({ id: 'wl1', code: 'WL-MUM', name: 'Mumbai Office' })
const EDIT_USER = { permissions: [Permissions.employmentHistory.change] }

function stubMasters(stub: StubAdapter) {
  stub.on('get', '/api/master-data/position-change-reasons', () => ({ data: ok([REASON]) }))
  stub.on('get', '/api/master-data/holding-companies', () => ({ data: ok([]) }))
  stub.on('get', '/api/master-data/organisations', () => ({ data: ok([]) }))
  stub.on('get', '/api/master-data/functions', () => ({ data: ok([]) }))
  stub.on('get', '/api/master-data/grades', () => ({ data: ok([GRADE]) }))
  stub.on('get', '/api/master-data/employee-types', () => ({ data: ok([]) }))
  stub.on('get', '/api/master-data/work-locations', () => ({ data: ok([WORK_LOCATION]) }))
  stub.on('get', '/api/departments', () => ({ data: ok(paged([makeDepartment({ id: DEPARTMENT_ID })])) }))
  stub.on('get', '/api/designations', () => ({ data: ok(paged([makeDesignation({ id: DESIGNATION_ID })])) }))
  stub.on('get', '/api/countries', () => ({
    data: ok(paged([{ id: 'cn1', name: 'India', isActive: true }])),
  }))
}

describe('EmploymentSectionForm', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()
    stub.on('get', HISTORY_URL, () => ({ data: ok([]) }))
    stubMasters(stub)
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  it('lists the history and offers no Edit or Delete (append-only)', async () => {
    stub.on('get', HISTORY_URL, () => ({
      data: ok([
        makeEmploymentHistory({ id: 'eh2', effectiveFrom: '2026-03-01' }),
        makeEmploymentHistory({ id: 'eh1', effectiveFrom: '2026-01-05' }),
      ]),
    }))
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />, { user: { ...makeUser(), ...EDIT_USER } })

    // These values appear in the history rows (the pickers render "CODE - Name").
    expect(await screen.findByText(/2026-01-05/)).toBeInTheDocument()
    expect(screen.getByText(/2026-03-01/)).toBeInTheDocument()
    expect(screen.getAllByText('New Hire').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Grade 1').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Mumbai Office').length).toBeGreaterThan(0)
    expect(screen.queryByRole('button', { name: /Edit/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Delete|Deactivate/i })).not.toBeInTheDocument()
  })

  it('keeps the change editor unavailable without EmploymentHistory.Change', async () => {
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />)
    expect(await screen.findByText('You do not have permission to add employment changes.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument()
  })

  it('renders exactly four steps without Additional Details or its lookup', async () => {
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />, { user: { ...makeUser(), ...EDIT_USER } })

    expect(await screen.findByRole('button', { name: /Employment Details/ })).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /Employment Details|Organization Structure|Position Details|Location Details/ })).toHaveLength(4)
    expect(screen.queryByText('Additional Details')).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/^Cost center$/)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/^Business role$/)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/^Grade level$/)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/^Career group$/)).not.toBeInTheDocument()
    expect(stub.callsTo('get', '/api/master-data/cost-centers')).toHaveLength(0)
  })

  it('supports Back/Next navigation while preserving values and makes Save final', async () => {
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />, { user: { ...makeUser(), ...EDIT_USER } })

    fireEvent.change(await screen.findByLabelText(/^Effective date/), { target: { value: '2026-03-10' } })
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.selectOptions(screen.getByLabelText(/^Department$/), DEPARTMENT_ID)
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'Back' }))

    expect(screen.getByRole('button', { name: /Organization Structure/ })).toHaveAttribute('aria-current', 'step')
    expect((screen.getByLabelText(/^Department$/) as HTMLSelectElement).value).toBe(DEPARTMENT_ID)
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    expect(screen.getByRole('button', { name: /Location Details/ })).toHaveAttribute('aria-current', 'step')
    expect(screen.getByRole('button', { name: 'ADD EMPLOYMENT' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument()
  })

  it('allows saving without an optional change reason and posts master FKs only', async () => {
    stub.on('post', HISTORY_URL, (call) => ({ data: ok(call.body as never) }))
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />, { user: { ...makeUser(), ...EDIT_USER } })

    fireEvent.change(await screen.findByLabelText(/^Effective date/), { target: { value: '2026-03-10' } })
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.selectOptions(screen.getByLabelText(/^Department$/), DEPARTMENT_ID)
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.selectOptions(screen.getByLabelText(/^Designation$/), DESIGNATION_ID)
    await userEvent.selectOptions(screen.getByLabelText(/^Grade$/), 'gr1')
    await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.selectOptions(screen.getByLabelText(/^Work location$/), 'wl1')
    await userEvent.selectOptions(screen.getByLabelText(/^Location$/), 'cn1')
    await userEvent.click(screen.getByRole('button', { name: 'ADD EMPLOYMENT' }))

    await waitFor(() => expect(stub.callsTo('post', HISTORY_URL)).toHaveLength(1))
    const body = stub.callsTo('post', HISTORY_URL)[0]!.body as Record<string, unknown>
    expect(body.effectiveFrom).toBe('2026-03-10')
    expect(body.positionChangeReasonId).toBeNull()
    expect(body).not.toHaveProperty('managerId')
    expect(body.departmentId).toBe(DEPARTMENT_ID)
    expect(body.designationId).toBe(DESIGNATION_ID)
    expect(body.gradeId).toBe('gr1')
    expect(body.workLocationId).toBe('wl1')
    expect(body.countryLocationId).toBe('cn1')
    expect(body.employmentType).toBe('FullTime')
    expect(body.employmentStatus).toBe('Active')
    expect(body).not.toHaveProperty('departmentName')
    expect(body).not.toHaveProperty('designationName')
    expect(await screen.findByText('Employment change recorded.')).toBeInTheDocument()
  })

  it('renders Change Reason as optional and does not trigger manager lookup', async () => {
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />, { user: { ...makeUser(), ...EDIT_USER } })

    const reason = await screen.findByLabelText(/^Change Reason \(Optional\)$/)
    expect(reason).not.toBeRequired()
    expect(screen.queryByLabelText(/^Direct manager$/i)).not.toBeInTheDocument()
    expect(stub.callsTo('get', `/api/employees/${EMPLOYEE_ID}/supervisor-options`)).toHaveLength(0)
  })

  it('still blocks navigation when the effective date is missing', async () => {
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />, { user: { ...makeUser(), ...EDIT_USER } })

    await userEvent.click(await screen.findByRole('button', { name: 'Next' }))

    expect(screen.getByText('This field is required.')).toBeInTheDocument()
    expect(screen.getByLabelText(/^Effective date/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Organization Structure/ })).not.toHaveAttribute('aria-current', 'step')
  })

  it('leaves contractual, separation and confirmation fields to their modules', async () => {
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />, { user: { ...makeUser(), ...EDIT_USER } })

    await screen.findByLabelText(/^Effective date/)
    for (const label of [
      /^Confirmation date/,
      /^Job status/,
      /^Notice period/,
      /^Notice unit/,
      /^Notice status/,
      /^Notice start date/,
      /^Notice end date/,
      /^Referred by employee ID/,
    ]) {
      expect(screen.queryByLabelText(label)).not.toBeInTheDocument()
    }
    expect(screen.queryByText('Joining & Contractual Employment')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save Joining Details' })).not.toBeInTheDocument()
  })

  it('clears dependent children when a parent is changed', async () => {
    stub.on('get', '/api/master-data/sub-departments', () => ({
      data: ok([makeMasterLookup({ id: 'sd1', code: 'SD1', name: 'Core' })]),
    }))
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />, { user: { ...makeUser(), ...EDIT_USER } })
    await userEvent.click(await screen.findByRole('button', { name: 'Next' }))

    await userEvent.selectOptions(await screen.findByLabelText(/^Department$/), DEPARTMENT_ID)
    const subDept = (await screen.findByLabelText(/^Sub-department$/)) as HTMLSelectElement
    await userEvent.selectOptions(subDept, 'sd1')
    expect(subDept.value).toBe('sd1')

    await userEvent.selectOptions(screen.getByLabelText(/^Department$/), '')
    expect((screen.getByLabelText(/^Sub-department$/) as HTMLSelectElement).value).toBe('')
  })

  it('surfaces a server refusal (overlapping period) as an error instead of saving', async () => {
    stub.on('post', HISTORY_URL, () => ({
      status: 400,
      data: fail('The effective date overlaps an existing employment period.'),
    }))
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />, { user: { ...makeUser(), ...EDIT_USER } })

    fireEvent.change(await screen.findByLabelText(/^Effective date/), { target: { value: '2026-03-10' } })
    await userEvent.selectOptions(screen.getByLabelText(/^Change Reason \(Optional\)$/), 'pr1')
    for (let index = 0; index < 3; index += 1) await userEvent.click(screen.getByRole('button', { name: 'Next' }))
    await userEvent.click(screen.getByRole('button', { name: 'ADD EMPLOYMENT' }))

    expect(
      await screen.findByText('The effective date overlaps an existing employment period.'),
    ).toBeInTheDocument()
  })
})
