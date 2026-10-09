import { screen, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { RatingScalesPage } from './RatingScalesPage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser, paged } from '../../test/fixtures.ts'
import { installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'

const item = {
  id: 'scale-1',
  code: 'PERF5',
  name: 'Five Point',
  description: 'Standard 1-5',
  minRating: 1,
  maxRating: 5,
  isActive: true,
  displayOrder: 0,
  effectiveFrom: null,
  effectiveTo: null,
  valueCount: 5,
  createdDate: '2026-01-01T00:00:00Z',
  modifiedDate: null,
}

describe('RatingScalesPage', () => {
  let stub: StubAdapter
  beforeEach(() => {
    stub = installStubAdapter()
  })
  afterEach(() => stub.restore())

  function renderPage(permissions = [Permissions.confirmation.ratingScaleView, Permissions.confirmation.ratingScaleManage]) {
    return renderAsUser(<RatingScalesPage />, { user: makeUser({ permissions }) })
  }
  function list(rows = [item]) {
    stub.on('get', '/api/confirmation-rating-scales', () => ({ data: ok(paged(rows)) }))
  }

  it('renders scale rows with range and value count', async () => {
    list()
    renderPage()
    expect(await screen.findByText('Five Point')).toBeInTheDocument()
    const row = screen.getByRole('row', { name: /PERF5 Five Point/ })
    expect(within(row).getByText('1–5')).toBeInTheDocument()
    expect(within(row).getByText('Active')).toBeInTheDocument()
  })

  it('shows the add action for managers', async () => {
    list([])
    renderPage()
    expect(await screen.findByRole('link', { name: 'Add Rating Scale' })).toBeInTheDocument()
  })

  it('hides the add action without manage permission', async () => {
    list([])
    renderPage([Permissions.confirmation.ratingScaleView])
    await screen.findByText('No rating scales configured yet.')
    expect(screen.queryByRole('link', { name: 'Add Rating Scale' })).not.toBeInTheDocument()
  })
})
