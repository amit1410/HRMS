import { fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { session } from '../../api/session.ts'
import {
  makeDepartment,
  makeDesignation,
  makeEmploymentHistory,
  makeMasterLookup,
  paged,
} from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { EmploymentSectionForm } from './EmploymentSectionForm.tsx'

const EMPLOYEE_ID = 'e1000000-0000-0000-0000-000000000001'
const HISTORY_URL = `/api/employees/${EMPLOYEE_ID}/employment-history`
const EMPLOYMENT_URL = `/api/employees/${EMPLOYEE_ID}/employment`
const DEPARTMENT_ID = 'd1000000-0000-0000-0000-000000000001'
const DESIGNATION_ID = 'g1000000-0000-0000-0000-000000000001'

const REASON = makeMasterLookup({ id: 'pr1', code: 'NEW_HIRE', name: 'New Hire' })
const GRADE = makeMasterLookup({ id: 'gr1', code: 'G1', name: 'Grade 1' })
const WORK_LOCATION = makeMasterLookup({ id: 'wl1', code: 'WL-MUM', name: 'Mumbai Office' })

function stubMasters(stub: StubAdapter) {
  stub.on('get', '/api/master-data/position-change-reasons', () => ({ data: ok([REASON]) }))
  stub.on('get', '/api/master-data/holding-companies', () => ({ data: ok([]) }))
  stub.on('get', '/api/master-data/organisations', () => ({ data: ok([]) }))
  stub.on('get', '/api/master-data/functions', () => ({ data: ok([]) }))
  stub.on('get', '/api/master-data/grades', () => ({ data: ok([GRADE]) }))
  stub.on('get', '/api/master-data/employee-types', () => ({ data: ok([]) }))
  stub.on('get', '/api/master-data/work-locations', () => ({ data: ok([WORK_LOCATION]) }))
  stub.on('get', '/api/master-data/cost-centers', () => ({ data: ok([]) }))
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
    stub.on('get', EMPLOYMENT_URL, () => ({ status: 404, data: fail('No employment record found.') }))
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
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />)

    // These values appear in the history rows (the pickers render "CODE - Name").
    expect(await screen.findByText(/2026-01-05/)).toBeInTheDocument()
    expect(screen.getByText(/2026-03-01/)).toBeInTheDocument()
    expect(screen.getAllByText('New Hire').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Grade 1').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Mumbai Office').length).toBeGreaterThan(0)
    expect(screen.queryByRole('button', { name: /Edit/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Delete|Deactivate/i })).not.toBeInTheDocument()
  })

  it('posts the master FKs (never a typed-in name) when adding employment', async () => {
    stub.on('post', HISTORY_URL, (call) => ({ data: ok(call.body as never) }))
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />)

    fireEvent.change(await screen.findByLabelText(/^Effective date/), { target: { value: '2026-03-10' } })
    await userEvent.selectOptions(screen.getByLabelText(/^Change reason/), 'pr1')
    await userEvent.selectOptions(screen.getByLabelText(/^Department$/), DEPARTMENT_ID)
    await userEvent.selectOptions(screen.getByLabelText(/^Designation$/), DESIGNATION_ID)
    await userEvent.selectOptions(screen.getByLabelText(/^Grade$/), 'gr1')
    await userEvent.selectOptions(screen.getByLabelText(/^Work location$/), 'wl1')
    await userEvent.selectOptions(screen.getByLabelText(/^Location$/), 'cn1')

    await userEvent.click(screen.getByRole('button', { name: 'ADD EMPLOYMENT' }))

    await waitFor(() => expect(stub.callsTo('post', HISTORY_URL)).toHaveLength(1))
    const body = stub.callsTo('post', HISTORY_URL)[0]!.body as Record<string, unknown>
    expect(body.effectiveFrom).toBe('2026-03-10')
    expect(body.positionChangeReasonId).toBe('pr1')
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

  it('loads and independently saves joining and contractual employment details', async () => {
    stub.on('get', EMPLOYMENT_URL, () => ({
      data: ok({
        id: 'employment-1',
        employeeId: EMPLOYEE_ID,
        firstHiredDate: '2020-01-02',
        dateOfJoining: '2021-02-03',
        groupDateOfJoining: null,
        confirmationDate: null,
        jobStatus: 'Probation',
        probationPeriod: 6,
        probationPeriodUnit: 'Months',
        referredByEmployeeId: null,
        referredByEmployeeName: null,
        noticePeriod: 30,
        noticePeriodUnit: 'Days',
        createdDate: '2026-01-05T09:30:00Z',
        modifiedDate: null,
      }),
    }))
    stub.on('put', EMPLOYMENT_URL, (call) => ({ data: ok(call.body as never) }))

    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />)

    await waitFor(() => expect(screen.getByLabelText(/^First hired date/)).toHaveValue('2020-01-02'))
    expect(screen.getByLabelText(/^Date of joining/)).toHaveValue('2021-02-03')
    expect(screen.getByLabelText(/^Probation period/)).toHaveValue(6)
    expect(screen.getByLabelText(/^Notice period/)).toHaveValue(30)

    fireEvent.change(screen.getByLabelText(/^Job status/), { target: { value: 'Confirmed' } })
    await userEvent.click(screen.getByRole('button', { name: 'Save Joining Details' }))

    await waitFor(() => expect(stub.callsTo('put', EMPLOYMENT_URL)).toHaveLength(1))
    const body = stub.callsTo('put', EMPLOYMENT_URL)[0]!.body as Record<string, unknown>
    expect(body.firstHiredDate).toBe('2020-01-02')
    expect(body.dateOfJoining).toBe('2021-02-03')
    expect(body.jobStatus).toBe('Confirmed')
    expect(body.probationPeriod).toBe(6)
    expect(body.probationPeriodUnit).toBe('Months')
    expect(body.noticePeriod).toBe(30)
    expect(body.noticePeriodUnit).toBe('Days')
    expect(await screen.findByText('Joining and contractual employment details saved.')).toBeInTheDocument()
  })

  it('clears dependent children when a parent is changed', async () => {
    stub.on('get', '/api/master-data/sub-departments', () => ({
      data: ok([makeMasterLookup({ id: 'sd1', code: 'SD1', name: 'Core' })]),
    }))
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />)

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
    renderAsUser(<EmploymentSectionForm employeeId={EMPLOYEE_ID} />)

    fireEvent.change(await screen.findByLabelText(/^Effective date/), { target: { value: '2026-03-10' } })
    await userEvent.selectOptions(screen.getByLabelText(/^Change reason/), 'pr1')
    await userEvent.click(screen.getByRole('button', { name: 'ADD EMPLOYMENT' }))

    expect(
      await screen.findByText('The effective date overlaps an existing employment period.'),
    ).toBeInTheDocument()
  })
})
