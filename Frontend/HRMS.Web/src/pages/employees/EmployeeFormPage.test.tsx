import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { Route, Routes, useLocation } from 'react-router-dom'
import { session } from '../../api/session.ts'
import type { FlashState } from '../../hooks/useFlash.ts'
import {
  makeDepartment,
  makeDesignation,
  makeEmployee,
  makeEmployeeDetail,
  paged,
} from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { EmployeeFormPage } from './EmployeeFormPage.tsx'

/**
 * Create and edit for an employee — fourteen fields, three references and one cross-field rule.
 *
 * There is no client-side validation to test, because there is none: `EmployeeRequestValidator` holds
 * fifteen rules and the service adds the ones that need stored data, so the form submits and renders what
 * comes back. What is worth testing is everything the form decides *about what the user meant*:
 *
 * - An unanswered required reference is sent as the CLR default rather than `''`, because `''` is a
 *   deserialization failure whose field key (`$.dateOfJoining`) matches no input on this page, while the
 *   default reaches a FluentValidation rule written for exactly this case.
 * - A leaving date exists only for someone who has left, and switching back to Active forgets it.
 * - An emptied optional leaves as `null`, since a PUT replaces the record rather than patching it.
 * - A reference the record already points at stays selectable even when it is not in the active-only list
 *   the form loads — otherwise saving would quietly reassign the employee to whatever was at the top.
 *
 * These render through a real `Routes` table, because the screen reads `:id` to decide between create and
 * edit and because where a save lands is half the behaviour. The list is a probe that prints where it was
 * reached and what it was handed. Permission gating belongs to the route guard, not here.
 */

const EMPLOYEE_ID = 'e1000000-0000-0000-0000-000000000001'
const EMPLOYEE_URL = `/api/employees/${EMPLOYEE_ID}`
const NEW_ROUTE = '/employees/new'
const EDIT_ROUTE = `/employees/${EMPLOYEE_ID}/edit`

const DEPARTMENT_ID = 'd1000000-0000-0000-0000-000000000001'
const DESIGNATION_ID = 'g1000000-0000-0000-0000-000000000001'
const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

/** The second candidate, who is also the only one a search for "byr" finds. */
const COLLEAGUE_ID = 'e2000000-0000-0000-0000-000000000002'

/** Stands in for the list, so a test can see where a save or a Cancel actually went. */
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

interface RenderOptions {
  route?: string
  /** The view the list handed over, which Cancel and a save have to come back to. */
  state?: unknown
}

function renderForm({ route = NEW_ROUTE, state }: RenderOptions = {}) {
  return renderAsUser(
    <Routes>
      <Route path="/employees" element={<ListProbe />} />
      <Route path="/employees/new" element={<EmployeeFormPage />} />
      <Route path="/employees/:id/edit" element={<EmployeeFormPage />} />
    </Routes>,
    { route, state },
  )
}

/**
 * A control by its label, matched as a pattern for two reasons: a required field's label text ends in the
 * `*` the marker adds, and a date input has no ARIA role to look it up by at all.
 */
function field(label: string): HTMLElement {
  return screen.getByLabelText(new RegExp(`^${label}\\*?$`))
}

/**
 * Fills a date input. `userEvent.type` cannot: a date input only commits a complete, valid value, so a
 * keystroke at a time never produces one and the box stays empty.
 */
function setDate(label: string, value: string): void {
  fireEvent.change(field(label), { target: { value } })
}

/** The body of the write the form sent, whichever verb it used. */
function sentBody(stub: StubAdapter, method: 'post' | 'put', url: string): unknown {
  return stub.callsTo(method, url)[0]?.body
}

/** The params of the most recent request to a route — what the form actually asked the API for. */
function lastQuery(stub: StubAdapter, url: string): Record<string, unknown> {
  const calls = stub.callsTo('get', url)
  return calls[calls.length - 1]?.params ?? {}
}

/** Fills the four text fields and two references a create needs, and nothing optional. */
async function fillRequired(): Promise<void> {
  await userEvent.type(field('Employee code'), 'EMP-003')
  await userEvent.type(field('First name'), 'Ravi')
  await userEvent.type(field('Last name'), 'Menon')
  await userEvent.type(field('Email'), 'ravi.menon@demo01.test')
  setDate('Date of joining', '2024-02-01')
  await userEvent.selectOptions(field('Department'), DEPARTMENT_ID)
  await userEvent.selectOptions(field('Designation'), DESIGNATION_ID)
}

describe('EmployeeFormPage', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()

    stub.on('get', EMPLOYEE_URL, () => ({ data: ok(makeEmployeeDetail()) }))
    stub.on('get', '/api/departments', () => ({
      data: ok(paged([makeDepartment(), makeDepartment({ id: 'd2', code: 'PPL', name: 'People' })])),
    }))
    stub.on('get', '/api/designations', () => ({ data: ok(paged([makeDesignation()])) }))

    // The manager picker's candidate search. Answers the search term so a test can watch the select keep
    // a chosen name that the current search no longer returns.
    stub.on('get', '/api/employees', (call) => {
      const search = String(call.params.search ?? '')
      const colleague = makeEmployee({
        id: COLLEAGUE_ID,
        employeeCode: 'EMP-002',
        fullName: 'Tomás Byrne',
        email: 'tomas.byrne@demo01.test',
      })
      if (search === 'byr') return { data: ok(paged([colleague])) }
      if (search !== '') return { data: ok(paged([])) }
      return { data: ok(paged([makeEmployee(), colleague])) }
    })
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  describe('creating', () => {
    it('opens blank and active, with no leaving date to fill in', () => {
      renderForm()

      expect(screen.getByRole('heading', { name: 'New employee', level: 1 })).toBeInTheDocument()
      expect(screen.getByText('Add someone to the directory')).toBeInTheDocument()
      expect(field('Employee code')).toHaveValue('')
      expect(field('Date of joining')).toHaveValue('')
      // The two answers that are not blank, because "add an employee" almost always means a current one
      // and a gender nobody asked for should not have to be corrected.
      expect(field('Status')).toHaveValue('Active')
      expect(field('Gender')).toHaveValue('Unspecified')
      // Absent, not merely empty: an active employee has no leaving date, so there is nothing to type.
      expect(screen.queryByLabelText(/^Date of leaving/)).not.toBeInTheDocument()
      expect(
        screen.getByText('Leaving the organization is a status change, never a delete — the record stays.'),
      ).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Create employee' })).toBeInTheDocument()
    })

    it('offers only references an employee can actually be assigned to', async () => {
      renderForm()

      expect(await screen.findByRole('option', { name: 'Engineering' })).toBeInTheDocument()
      // `isActive: true` on both, unlike the list screen's filters: assigning someone to a retired unit
      // or job title is what the API refuses, so it is not offered here in the first place.
      expect(lastQuery(stub, '/api/departments')).toEqual({
        pageSize: 100,
        sortBy: 'name',
        isActive: true,
      })
      expect(lastQuery(stub, '/api/designations')).toEqual({
        pageSize: 100,
        sortBy: 'name',
        isActive: true,
      })

      // Required, so there is no "None" — but there is a prompt, because a select that opens on the first
      // department would look answered before anyone answered it.
      const department = field('Department')
      expect(department).toHaveValue('')
      expect(within(department).getByRole('option', { name: 'Select a department' })).toBeInTheDocument()
      expect(within(field('Designation')).getByRole('option', { name: 'Select a job title' })).toBeInTheDocument()

      // The manager candidates are a page of *current* employees: someone who has resigned is exactly who
      // should not be given reports.
      expect(lastQuery(stub, '/api/employees')).toEqual({
        page: 1,
        pageSize: 20,
        sortBy: 'firstName',
        status: 'Active',
      })
    })

    it('sends the trimmed record, with everything unanswered as null', async () => {
      stub.on('post', '/api/employees', () => ({
        status: 201,
        data: ok(makeEmployeeDetail({ fullName: 'Ravi Menon' })),
      }))
      renderForm()
      await screen.findByRole('option', { name: 'Engineering' })

      await userEvent.type(field('Employee code'), '  EMP-003  ')
      await userEvent.type(field('First name'), 'Ravi')
      await userEvent.type(field('Last name'), 'Menon')
      await userEvent.type(field('Email'), '  ravi.menon@demo01.test  ')
      setDate('Date of joining', '2024-02-01')
      await userEvent.selectOptions(field('Department'), DEPARTMENT_ID)
      await userEvent.selectOptions(field('Designation'), DESIGNATION_ID)
      await userEvent.click(screen.getByRole('button', { name: 'Create employee' }))

      await waitFor(() => expect(stub.callsTo('post', '/api/employees')).toHaveLength(1))
      expect(sentBody(stub, 'post', '/api/employees')).toEqual({
        employeeCode: 'EMP-003',
        firstName: 'Ravi',
        lastName: 'Menon',
        email: 'ravi.menon@demo01.test',
        // `null` rather than `''` for each optional left alone: the column means "not recorded", and a
        // blank string is not that.
        phone: null,
        dateOfBirth: null,
        gender: 'Unspecified',
        dateOfJoining: '2024-02-01',
        // Forced, not merely hidden — the rule is that an active employee has no leaving date at all.
        dateOfLeaving: null,
        status: 'Active',
        departmentId: DEPARTMENT_ID,
        designationId: DESIGNATION_ID,
        reportingManagerId: null,
        address: null,
      })
    })

    it('spells an unanswered required field the way the API’s own required rules are written', async () => {
      stub.on('post', '/api/employees', () => ({
        status: 400,
        data: fail('Validation failed.', [
          { field: 'employeeCode', message: 'Employee code is required.' },
          { field: 'dateOfJoining', message: 'Date of joining is required.' },
          { field: 'departmentId', message: 'Department is required.' },
        ]),
      }))
      renderForm()

      await userEvent.click(screen.getByRole('button', { name: 'Create employee' }))

      await waitFor(() => expect(stub.callsTo('post', '/api/employees')).toHaveLength(1))
      // `DateOnly` and `Guid` are non-nullable in C#, so `''` for any of these is not a validation
      // failure — it is a deserialization failure, reported against `$.dateOfJoining`, which matches no
      // input on this form. The CLR default reaches FluentValidation instead, which has a rule for it.
      expect(sentBody(stub, 'post', '/api/employees')).toMatchObject({
        dateOfJoining: '0001-01-01',
        departmentId: EMPTY_GUID,
        designationId: EMPTY_GUID,
      })

      // Which is the whole point: the answer comes back as ordinary field errors, under the inputs that
      // are missing rather than in a banner naming nothing.
      expect(await screen.findByText('Date of joining is required.')).toBeInTheDocument()
      expect(field('Date of joining')).toHaveAttribute('aria-invalid', 'true')
      expect(field('Date of joining')).toHaveAccessibleDescription(/Date of joining is required\./)
      expect(field('Department')).toHaveAccessibleDescription(/Department is required\./)
      expect(field('Employee code')).toHaveAccessibleDescription(/Employee code is required\./)
      // No banner, because every message is already beside the input it belongs to.
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })

    it('asks for a leaving date only from someone who has left, and forgets it on the way back', async () => {
      renderForm()

      setDate('Date of joining', '2024-02-01')
      await userEvent.selectOptions(field('Status'), 'Resigned')

      const leaving = field('Date of leaving')
      expect(leaving).toBeInTheDocument()
      // The picker will not offer a last working day before the first one, which is a rule the validator
      // also has — so the field agrees with it instead of leading the user into it.
      expect(leaving).toHaveAttribute('min', '2024-02-01')
      expect(screen.getByText('Last working day. On or after the date of joining.')).toBeInTheDocument()

      setDate('Date of leaving', '2026-06-30')
      await userEvent.selectOptions(field('Status'), 'Active')
      expect(screen.queryByLabelText(/^Date of leaving/)).not.toBeInTheDocument()

      await userEvent.selectOptions(field('Status'), 'Terminated')
      // Cleared rather than hidden. Had the value survived, it would be submitted by a rule the user can
      // no longer see the field for, and rejected for a reason they cannot act on.
      expect(field('Date of leaving')).toHaveValue('')
    })

    it('keeps a chosen manager visible when the search no longer returns them', async () => {
      renderForm()
      const picker = field('Reporting manager')
      await waitFor(() =>
        expect(within(picker).getByRole('option', { name: 'Tomás Byrne · EMP-002' })).toBeInTheDocument(),
      )

      await userEvent.selectOptions(picker, COLLEAGUE_ID)
      await userEvent.type(screen.getByRole('searchbox', { name: 'Search employees' }), 'zzz')

      // "No reporting manager" and the one who was picked: the search found nobody, and the selection is
      // carried anyway. Dropping it would leave the select showing blank while the form still held the id,
      // so the user would see no manager and save one.
      await waitFor(() => expect(within(picker).getAllByRole('option')).toHaveLength(2))
      expect(picker).toHaveValue(COLLEAGUE_ID)
      expect(within(picker).getByRole('option', { name: 'Tomás Byrne · EMP-002' })).toBeInTheDocument()
      expect(lastQuery(stub, '/api/employees').search).toBe('zzz')
    })

    it('searches for a candidate once for a word, not once per keystroke', async () => {
      renderForm()
      await screen.findByRole('option', { name: 'Tomás Byrne · EMP-002' })
      const before = stub.callsTo('get', '/api/employees').length

      await userEvent.type(screen.getByRole('searchbox', { name: 'Search employees' }), 'byr')

      await waitFor(() => expect(lastQuery(stub, '/api/employees').search).toBe('byr'))
      expect(stub.callsTo('get', '/api/employees')).toHaveLength(before + 1)
    })

    it('goes back to the list view it was opened from, with something to announce', async () => {
      stub.on('post', '/api/employees', () => ({
        status: 201,
        data: ok(makeEmployeeDetail({ fullName: 'Ravi Menon' })),
      }))
      renderForm({ state: { from: '/employees?page=2&status=Active' } })
      await screen.findByRole('option', { name: 'Engineering' })

      await fillRequired()
      await userEvent.click(screen.getByRole('button', { name: 'Create employee' }))

      // The page and the filter survive the round trip; an unfiltered page one would lose the place the
      // user was working in.
      expect(await screen.findByText('Landed on /employees?page=2&status=Active')).toBeInTheDocument()
      // The name comes from the response, not from what was typed — the API is what decides `fullName`.
      expect(screen.getByText('Handed over: Ravi Menon was added.')).toBeInTheDocument()
    })

    it('falls back to the list when it was not opened from one', async () => {
      stub.on('post', '/api/employees', () => ({ status: 201, data: ok(makeEmployeeDetail()) }))
      renderForm()
      await screen.findByRole('option', { name: 'Engineering' })

      await fillRequired()
      await userEvent.click(screen.getByRole('button', { name: 'Create employee' }))

      // Typed straight into the address bar, so there is no `from` to honour.
      expect(await screen.findByText('Landed on /employees')).toBeInTheDocument()
    })

    it('shows a failure that belongs to no field as a banner', async () => {
      stub.on('post', '/api/employees', () => ({
        status: 409,
        data: fail('An employee with code EMP-003 already exists.'),
      }))
      renderForm()
      await screen.findByRole('option', { name: 'Engineering' })

      await fillRequired()
      await userEvent.click(screen.getByRole('button', { name: 'Create employee' }))

      // `alert`, not `status`: the user is about to assume the save worked.
      const banner = await screen.findByRole('alert')
      expect(banner).toHaveTextContent('An employee with code EMP-003 already exists.')
      // Still on the form, still able to fix the code and try again.
      expect(field('Employee code')).toHaveValue('EMP-003')
      expect(screen.getByRole('button', { name: 'Create employee' })).toBeEnabled()
    })

    it('takes one save at a time, so a double submit is not two employees', async () => {
      stub.on('post', '/api/employees', () => ({ status: 201, data: ok(makeEmployeeDetail()) }))
      renderForm()
      await screen.findByRole('option', { name: 'Engineering' })
      await fillRequired()

      // Submitted at the form rather than clicked at the button, which is what a second Enter keypress or
      // a stuck click does: it goes around `disabled` and has to be turned away by the guard instead.
      const form = screen.getByRole('button', { name: 'Create employee' }).closest('form')
      fireEvent.submit(form as HTMLFormElement)
      fireEvent.submit(form as HTMLFormElement)

      await screen.findByText('Landed on /employees')
      expect(stub.callsTo('post', '/api/employees')).toHaveLength(1)
    })

    it('leaves without saving on Cancel', async () => {
      renderForm({ state: { from: '/employees?search=far' } })

      await userEvent.type(field('First name'), 'Ravi')
      await userEvent.click(screen.getByRole('link', { name: 'Cancel' }))

      expect(await screen.findByText('Landed on /employees?search=far')).toBeInTheDocument()
      expect(stub.callsTo('post', '/api/employees')).toHaveLength(0)
      // Nothing to announce, because nothing happened.
      expect(screen.queryByText(/Handed over:/)).not.toBeInTheDocument()
    })

    it('ignores a return path that does not address this list', async () => {
      renderForm({ state: { from: 'https://elsewhere.test/employees' } })

      await userEvent.click(screen.getByRole('link', { name: 'Cancel' }))

      // History state is whatever navigated here chose to put there, so `from` is checked rather than
      // trusted — the same posture the sign-in screen takes with the `from` a route guard hands it.
      expect(await screen.findByText('Landed on /employees')).toBeInTheDocument()
    })
  })

  describe('editing', () => {
    it('waits for the record rather than offering an empty form', async () => {
      renderForm({ route: EDIT_ROUTE })

      // The heading is already there, so the page does not jump when the record arrives.
      expect(screen.getByRole('heading', { name: 'Edit employee', level: 1 })).toBeInTheDocument()
      expect(screen.getByRole('status')).toHaveTextContent('Loading employee…')
      expect(screen.queryByRole('textbox', { name: /^Employee code/ })).not.toBeInTheDocument()

      expect(await screen.findByRole('heading', { name: 'Edit Nadia Farrell', level: 1 })).toBeInTheDocument()
    })

    it('fills every field from the record, and says which record it is', async () => {
      renderForm({ route: EDIT_ROUTE })
      await screen.findByRole('heading', { name: 'Edit Nadia Farrell', level: 1 })

      // Enough of the record to be sure it is the right one before anything is changed.
      expect(screen.getByText('EMP-001 · Engineering · Senior Software Engineer')).toBeInTheDocument()
      expect(field('Employee code')).toHaveValue('EMP-001')
      // First and last separately, not the `fullName` the list shows: the API stores them apart.
      expect(field('First name')).toHaveValue('Nadia')
      expect(field('Last name')).toHaveValue('Farrell')
      expect(field('Email')).toHaveValue('nadia.farrell@demo01.test')
      expect(field('Phone')).toHaveValue('+353 1 555 0134')
      expect(field('Address')).toHaveValue('14 Kildare Street, Dublin')
      expect(field('Date of birth')).toHaveValue('1991-07-02')
      expect(field('Gender')).toHaveValue('Female')
      expect(field('Date of joining')).toHaveValue('2023-03-14')
      expect(field('Status')).toHaveValue('Active')
      // The ids, which is why the record is fetched at all — the row that opened this carries names.
      expect(field('Department')).toHaveValue(DEPARTMENT_ID)
      expect(field('Designation')).toHaveValue(DESIGNATION_ID)
      expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
      // Active, so there is no leaving date on the record and no field for one.
      expect(screen.queryByLabelText(/^Date of leaving/)).not.toBeInTheDocument()
    })

    it('keeps a reference the record already points at, even one no longer offered', async () => {
      // Engineering has been retired since, so the active-only list the form loads does not contain it.
      stub.on('get', '/api/departments', () => ({
        data: ok(paged([makeDepartment({ id: 'd2', code: 'PPL', name: 'People' })])),
      }))
      renderForm({ route: EDIT_ROUTE })
      await screen.findByRole('heading', { name: 'Edit Nadia Farrell', level: 1 })

      const department = field('Department')
      // `UpdateAsync` rejects an inactive reference only when it *changes*, so this record legitimately
      // points at one. A select whose value matches no option shows the first instead — saving would have
      // moved her to People without anyone asking for it.
      await waitFor(() => expect(department).toHaveValue(DEPARTMENT_ID))
      expect(within(department).getByRole('option', { name: 'Engineering' })).toBeInTheDocument()
    })

    it('keeps a manager who has since left selectable', async () => {
      stub.on('get', EMPLOYEE_URL, () => ({
        data: ok(
          makeEmployeeDetail({
            reportingManagerId: 'e3000000-0000-0000-0000-000000000003',
            reportingManagerName: 'Owen Pike',
          }),
        ),
      }))
      renderForm({ route: EDIT_ROUTE })
      await screen.findByRole('heading', { name: 'Edit Nadia Farrell', level: 1 })

      const picker = field('Reporting manager')
      // Named from the record, so the field is right before the candidate search finishes — and stays
      // right afterwards, even though a search for current employees cannot return him.
      expect(picker).toHaveValue('e3000000-0000-0000-0000-000000000003')
      expect(within(picker).getByRole('option', { name: 'Owen Pike' })).toBeInTheDocument()
      await waitFor(() =>
        expect(within(picker).getByRole('option', { name: 'Tomás Byrne · EMP-002' })).toBeInTheDocument(),
      )
      expect(picker).toHaveValue('e3000000-0000-0000-0000-000000000003')
    })

    it('does not offer the employee as their own manager', async () => {
      renderForm({ route: EDIT_ROUTE })
      await screen.findByRole('heading', { name: 'Edit Nadia Farrell', level: 1 })

      const picker = field('Reporting manager')
      await waitFor(() =>
        expect(within(picker).getByRole('option', { name: 'Tomás Byrne · EMP-002' })).toBeInTheDocument(),
      )
      // She is in the candidate page the API returned, and taken out here: `EmployeeService` refuses an
      // employee who reports to themselves, so offering it would be offering a save that cannot succeed.
      expect(within(picker).queryByRole('option', { name: 'Nadia Farrell · EMP-001' })).not.toBeInTheDocument()
    })

    it('sends the whole record, so an emptied optional clears the stored one', async () => {
      stub.on('put', EMPLOYEE_URL, () => ({
        data: ok(makeEmployeeDetail({ phone: null, address: null })),
      }))
      renderForm({ route: EDIT_ROUTE, state: { from: '/employees?page=2' } })
      await screen.findByRole('heading', { name: 'Edit Nadia Farrell', level: 1 })

      await userEvent.clear(field('Phone'))
      await userEvent.clear(field('Address'))
      await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))

      expect(await screen.findByText('Landed on /employees?page=2')).toBeInTheDocument()
      expect(screen.getByText('Handed over: Nadia Farrell was updated.')).toBeInTheDocument()
      // A PUT is a replacement, not a patch: every field goes, including the eleven that did not change.
      expect(sentBody(stub, 'put', EMPLOYEE_URL)).toEqual({
        employeeCode: 'EMP-001',
        firstName: 'Nadia',
        lastName: 'Farrell',
        email: 'nadia.farrell@demo01.test',
        phone: null,
        dateOfBirth: '1991-07-02',
        gender: 'Female',
        dateOfJoining: '2023-03-14',
        dateOfLeaving: null,
        status: 'Active',
        departmentId: DEPARTMENT_ID,
        designationId: DESIGNATION_ID,
        reportingManagerId: null,
        address: null,
      })
    })

    it('sends the leaving date the new status requires', async () => {
      stub.on('put', EMPLOYEE_URL, () => ({
        data: ok(makeEmployeeDetail({ status: 'Resigned', dateOfLeaving: '2026-06-30' })),
      }))
      renderForm({ route: EDIT_ROUTE })
      await screen.findByRole('heading', { name: 'Edit Nadia Farrell', level: 1 })

      await userEvent.selectOptions(field('Status'), 'Resigned')
      setDate('Date of leaving', '2026-06-30')
      await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))

      await waitFor(() => expect(stub.callsTo('put', EMPLOYEE_URL)).toHaveLength(1))
      // Which is how someone leaves: the status changes and the record stays. There is no delete here.
      expect(sentBody(stub, 'put', EMPLOYEE_URL)).toMatchObject({
        status: 'Resigned',
        dateOfLeaving: '2026-06-30',
      })
    })

    it('explains a record it could not read instead of showing a blank form', async () => {
      stub.on('get', EMPLOYEE_URL, () => ({
        status: 404,
        data: fail('That employee could not be found.'),
      }))
      renderForm({ route: EDIT_ROUTE })

      expect(await screen.findByText('Could not load this')).toBeInTheDocument()
      expect(screen.getByText('That employee could not be found.')).toBeInTheDocument()
      // An empty form here would have offered to overwrite the record with nothing.
      expect(screen.queryByRole('textbox', { name: /^Employee code/ })).not.toBeInTheDocument()
      // A 404 might be a stale link, so a retry is at least plausible.
      expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
    })

    it('shows the failed save without losing the edits', async () => {
      stub.on('put', EMPLOYEE_URL, () => ({
        status: 400,
        data: fail('Validation failed.', [
          { field: 'employeeCode', message: 'Employee code is already in use.' },
        ]),
      }))
      renderForm({ route: EDIT_ROUTE })
      await screen.findByRole('heading', { name: 'Edit Nadia Farrell', level: 1 })

      await userEvent.clear(field('Employee code'))
      await userEvent.type(field('Employee code'), 'EMP-002')
      await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))

      expect(await screen.findByText('Employee code is already in use.')).toBeInTheDocument()
      expect(field('Employee code')).toHaveValue('EMP-002')
      // Re-reading the record would have thrown away thirteen other fields' worth of edits with it.
      expect(stub.callsTo('get', EMPLOYEE_URL)).toHaveLength(1)
    })
  })

  it('never sends a tenant id: the server takes it from the token', async () => {
    renderForm({ route: EDIT_ROUTE })
    await screen.findByRole('heading', { name: 'Edit Nadia Farrell', level: 1 })

    const sent = JSON.stringify(stub.calls).toLowerCase()
    expect(sent).not.toContain('tenant')
    expect(stub.calls.every((call) => call.authorization === 'Bearer access-1')).toBe(true)
  })
})
