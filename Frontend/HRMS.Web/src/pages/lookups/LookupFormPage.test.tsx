import { fireEvent, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { Route, Routes, useLocation } from 'react-router-dom'
import { session } from '../../api/session.ts'
import { Permissions } from '../../auth/permissions.ts'
import type { FlashState } from '../../hooks/useFlash.ts'
import { makeDepartment, makeDesignation, makeUser } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { LookupFormPage } from './LookupFormPage.tsx'
import { departmentsModule, designationsModule, type LookupModule } from './lookupModules.ts'

/**
 * Create and edit for departments / designations.
 *
 * The form has no validation of its own, so there is nothing to test about *rejecting* input — what
 * matters is the body it sends and what it does with the `errors[]` that come back. Two things get most of
 * the attention here: an empty description has to leave as `null` rather than `''` (the write is a full
 * replacement, so `''` would store a blank string where the column means "no description"), and a
 * successful save has to land back on the exact list view the user left.
 *
 * These render through a real `Routes` table, because the screen reads `:id` from the route to decide
 * between create and edit, and because the redirect after a save is half the behaviour. The list is a probe
 * that prints where it was reached and what it was handed.
 */

const ALL_LOOKUP_PERMISSIONS = [
  ...Object.values(Permissions.department),
  ...Object.values(Permissions.designation),
]

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

function renderForm(module: LookupModule, { route, state }: RenderOptions = {}) {
  return renderAsUser(
    <Routes>
      <Route path={module.basePath} element={<ListProbe />} />
      <Route path={`${module.basePath}/new`} element={<LookupFormPage module={module} />} />
      <Route path={`${module.basePath}/:id/edit`} element={<LookupFormPage module={module} />} />
    </Routes>,
    {
      route: route ?? `${module.basePath}/new`,
      state,
      user: makeUser({ permissions: ALL_LOOKUP_PERMISSIONS }),
    },
  )
}

const DEPARTMENT_URL = '/api/departments/d1000000-0000-0000-0000-000000000001'

/** The fields, by accessible name — which excludes the `*` the required marker adds to the label text. */
function field(name: string): HTMLElement {
  return screen.getByRole('textbox', { name })
}

describe('LookupFormPage', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()
    stub.on('get', DEPARTMENT_URL, () => ({
      data: ok(makeDepartment({ description: 'Builds and runs the product' })),
    }))
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  describe('creating', () => {
    it('opens empty and active, and quotes the code rule before it is broken', () => {
      renderForm(departmentsModule)

      expect(screen.getByRole('heading', { name: 'New department', level: 1 })).toBeInTheDocument()
      expect(field('Code')).toHaveValue('')
      expect(field('Name')).toHaveValue('')
      expect(field('Description')).toHaveValue('')
      // Active by default: a department nobody can be assigned to is not what "add" means.
      expect(screen.getByRole('checkbox', { name: 'Active' })).toBeChecked()
      // The one rule the user hears about up front, because the form does not check it itself.
      expect(screen.getByText(/Letters, digits, and \. _ - \/ after the first character\./)).toBeInTheDocument()
    })

    it('sends the trimmed values, with an empty description as null', async () => {
      stub.on('post', '/api/departments', () => ({ status: 201, data: ok(makeDepartment()) }))
      renderForm(departmentsModule)

      await userEvent.type(field('Code'), '  ENG  ')
      await userEvent.type(field('Name'), '  Engineering  ')
      await userEvent.click(screen.getByRole('button', { name: 'Create department' }))

      await waitFor(() => expect(stub.callsTo('post', '/api/departments')).toHaveLength(1))
      expect(stub.callsTo('post', '/api/departments')[0]?.body).toEqual({
        code: 'ENG',
        name: 'Engineering',
        // `null`, not `''`: the column means "no description", and a blank string is not that.
        description: null,
        isActive: true,
      })
    })

    it('goes back to the list view it was opened from, with something to announce', async () => {
      stub.on('post', '/api/departments', () => ({ status: 201, data: ok(makeDepartment()) }))
      renderForm(departmentsModule, { state: { from: '/departments?page=2&search=eng' } })

      await userEvent.type(field('Code'), 'ENG')
      await userEvent.type(field('Name'), 'Engineering')
      await userEvent.click(screen.getByRole('button', { name: 'Create department' }))

      // The page and the search survive the round trip; landing on an unfiltered page one would lose the
      // place the user was working in.
      expect(await screen.findByText('Landed on /departments?page=2&search=eng')).toBeInTheDocument()
      expect(screen.getByText('Handed over: Engineering was created.')).toBeInTheDocument()
    })

    it('falls back to the list when it was not opened from one', async () => {
      stub.on('post', '/api/departments', () => ({ status: 201, data: ok(makeDepartment()) }))
      renderForm(departmentsModule)

      await userEvent.type(field('Code'), 'ENG')
      await userEvent.type(field('Name'), 'Engineering')
      await userEvent.click(screen.getByRole('button', { name: 'Create department' }))

      // Typed straight into the address bar, so there is no `from` to honour.
      expect(await screen.findByText('Landed on /departments')).toBeInTheDocument()
    })

    it('puts each field error under the input the API named', async () => {
      stub.on('post', '/api/departments', () => ({
        status: 400,
        data: fail('Validation failed.', [
          { field: 'code', message: 'Code is already in use.' },
          { field: 'name', message: 'Name is required.' },
        ]),
      }))
      renderForm(departmentsModule)

      await userEvent.type(field('Code'), 'ENG')
      await userEvent.click(screen.getByRole('button', { name: 'Create department' }))

      expect(await screen.findByText('Code is already in use.')).toBeInTheDocument()
      expect(screen.getByText('Name is required.')).toBeInTheDocument()
      expect(field('Code')).toHaveAttribute('aria-invalid', 'true')
      expect(field('Code')).toHaveAccessibleDescription(/Code is already in use\./)
      // No banner: it would repeat both messages further from the inputs that have to be fixed.
      expect(screen.queryByText('Validation failed.')).not.toBeInTheDocument()
      // Still on the form, with what was typed intact.
      expect(field('Code')).toHaveValue('ENG')
    })

    it('shows a failure that belongs to no field as a banner', async () => {
      stub.on('post', '/api/departments', () => ({
        status: 409,
        data: fail('A department with this code already exists.'),
      }))
      renderForm(departmentsModule)

      await userEvent.type(field('Code'), 'ENG')
      await userEvent.type(field('Name'), 'Engineering')
      await userEvent.click(screen.getByRole('button', { name: 'Create department' }))

      // `alert`, not `status`: the user is about to assume the save worked.
      const banner = await screen.findByRole('alert')
      expect(banner).toHaveTextContent('A department with this code already exists.')
      expect(screen.getByRole('button', { name: 'Create department' })).toBeEnabled()
    })

    it('takes one save at a time, so a double submit is not two departments', async () => {
      stub.on('post', '/api/departments', () => ({ status: 201, data: ok(makeDepartment()) }))
      renderForm(departmentsModule)

      await userEvent.type(field('Code'), 'ENG')
      await userEvent.type(field('Name'), 'Engineering')

      // Submitted at the form rather than clicked at the button, which is what a second Enter keypress or
      // a stuck click does: it goes around `disabled` and has to be turned away by the guard instead.
      const form = screen.getByRole('button', { name: 'Create department' }).closest('form')
      fireEvent.submit(form as HTMLFormElement)
      fireEvent.submit(form as HTMLFormElement)

      await screen.findByText('Landed on /departments')
      expect(stub.callsTo('post', '/api/departments')).toHaveLength(1)
    })

    it('leaves without saving on Cancel', async () => {
      renderForm(departmentsModule, { state: { from: '/departments?dir=desc' } })

      await userEvent.type(field('Name'), 'Engineering')
      await userEvent.click(screen.getByRole('link', { name: 'Cancel' }))

      expect(await screen.findByText('Landed on /departments?dir=desc')).toBeInTheDocument()
      expect(stub.callsTo('post', '/api/departments')).toHaveLength(0)
      // Nothing to announce, because nothing happened.
      expect(screen.queryByText(/Handed over:/)).not.toBeInTheDocument()
    })

    it('ignores a return path that does not address this list', async () => {
      renderForm(departmentsModule, { state: { from: 'https://elsewhere.test/departments' } })

      await userEvent.click(screen.getByRole('link', { name: 'Cancel' }))

      // History state is whatever navigated here chose to put there, so `from` is checked rather than
      // trusted — the same posture the sign-in screen takes with the `from` a route guard hands it.
      expect(await screen.findByText('Landed on /departments')).toBeInTheDocument()
    })

    it('serves the other module with its own wording and its own endpoint', async () => {
      stub.on('post', '/api/designations', () => ({ status: 201, data: ok(makeDesignation()) }))
      renderForm(designationsModule)

      expect(screen.getByRole('heading', { name: 'New designation', level: 1 })).toBeInTheDocument()
      expect(
        screen.getByText('An inactive designation keeps its existing employees but cannot be chosen for new ones.'),
      ).toBeInTheDocument()

      await userEvent.type(field('Code'), 'SSE')
      await userEvent.type(field('Name'), 'Senior Software Engineer')
      await userEvent.click(screen.getByRole('button', { name: 'Create designation' }))

      expect(await screen.findByText('Landed on /designations')).toBeInTheDocument()
      expect(screen.getByText('Handed over: Senior Software Engineer was created.')).toBeInTheDocument()
      expect(stub.callsTo('post', '/api/departments')).toHaveLength(0)
    })
  })

  describe('editing', () => {
    it('fills the form from the record, and says which record it is', async () => {
      renderForm(departmentsModule, { route: `${departmentsModule.basePath}/d1000000-0000-0000-0000-000000000001/edit` })

      expect(await screen.findByRole('heading', { name: 'Edit Engineering', level: 1 })).toBeInTheDocument()
      // The count is why a delete may be refused, so it is worth seeing before editing.
      expect(screen.getByText('ENG · 12 employees')).toBeInTheDocument()
      expect(field('Code')).toHaveValue('ENG')
      expect(field('Name')).toHaveValue('Engineering')
      expect(field('Description')).toHaveValue('Builds and runs the product')
      expect(screen.getByRole('checkbox', { name: 'Active' })).toBeChecked()
      expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument()
    })

    it('sends the whole record, so clearing the description clears the stored one', async () => {
      stub.on('put', DEPARTMENT_URL, () => ({
        data: ok(makeDepartment({ name: 'Engineering', description: null, isActive: false })),
      }))
      renderForm(departmentsModule, {
        route: `${departmentsModule.basePath}/d1000000-0000-0000-0000-000000000001/edit`,
        state: { from: '/departments?page=2' },
      })
      await screen.findByRole('heading', { name: 'Edit Engineering', level: 1 })

      await userEvent.clear(field('Description'))
      await userEvent.click(screen.getByRole('checkbox', { name: 'Active' }))
      await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))

      expect(await screen.findByText('Landed on /departments?page=2')).toBeInTheDocument()
      expect(screen.getByText('Handed over: Engineering was updated.')).toBeInTheDocument()
      // A PUT is a replacement, not a patch: every field goes, including the two that did not change.
      expect(stub.callsTo('put', DEPARTMENT_URL)[0]?.body).toEqual({
        code: 'ENG',
        name: 'Engineering',
        description: null,
        isActive: false,
      })
    })

    it('explains a record it could not read instead of showing a blank form', async () => {
      stub.on('get', DEPARTMENT_URL, () => ({
        status: 404,
        data: fail('That department could not be found.'),
      }))
      renderForm(departmentsModule, { route: `${departmentsModule.basePath}/d1000000-0000-0000-0000-000000000001/edit` })

      expect(await screen.findByText('Could not load this')).toBeInTheDocument()
      expect(screen.getByText('That department could not be found.')).toBeInTheDocument()
      // An empty form here would have offered to overwrite the record with nothing.
      expect(screen.queryByRole('textbox', { name: 'Code' })).not.toBeInTheDocument()
      // A 404 might be a stale link, so a retry is at least plausible.
      expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
    })

    it('shows the failed save without losing the edits', async () => {
      stub.on('put', DEPARTMENT_URL, () => ({
        status: 400,
        data: fail('Validation failed.', [{ field: 'code', message: 'Code is already in use.' }]),
      }))
      renderForm(departmentsModule, { route: `${departmentsModule.basePath}/d1000000-0000-0000-0000-000000000001/edit` })
      await screen.findByRole('heading', { name: 'Edit Engineering', level: 1 })

      await userEvent.clear(field('Code'))
      await userEvent.type(field('Code'), 'PPL')
      await userEvent.click(screen.getByRole('button', { name: 'Save changes' }))

      expect(await screen.findByText('Code is already in use.')).toBeInTheDocument()
      // Re-reading the record would have thrown away what they typed.
      expect(field('Code')).toHaveValue('PPL')
      expect(stub.callsTo('get', DEPARTMENT_URL)).toHaveLength(1)
    })
  })
})
