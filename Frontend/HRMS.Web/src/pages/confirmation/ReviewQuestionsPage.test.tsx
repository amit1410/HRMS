import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { ReviewQuestionsPage } from './ReviewQuestionsPage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser, paged } from '../../test/fixtures.ts'
import { installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'

const item = {
  id: 'q-1',
  code: 'PERF',
  questionText: 'Overall performance?',
  helpText: null,
  questionType: 'Rating' as const,
  isMandatory: true,
  isActive: true,
  displayOrder: 1,
  effectiveFrom: null,
  effectiveTo: null,
  confirmationRatingScaleId: 'scale-1',
  ratingScaleName: 'Five Point',
  optionCount: 0,
  applicabilitySetCount: 0,
  createdDate: '2026-01-01T00:00:00Z',
  modifiedDate: null,
}

describe('ReviewQuestionsPage', () => {
  let stub: StubAdapter
  beforeEach(() => {
    stub = installStubAdapter()
  })
  afterEach(() => stub.restore())

  function renderPage(permissions = [Permissions.confirmation.reviewQuestionView, Permissions.confirmation.reviewQuestionManage]) {
    return renderAsUser(<ReviewQuestionsPage />, { user: makeUser({ permissions }) })
  }
  function list(rows = [item]) {
    stub.on('get', '/api/confirmation-review-questions', () => ({ data: ok(paged(rows)) }))
  }

  it('renders question rows with type and mandatory flag', async () => {
    list()
    renderPage()
    expect(await screen.findByText('Overall performance?')).toBeInTheDocument()
    const row = screen.getByRole('row', { name: /PERF Overall performance/ })
    expect(within(row).getByText('Rating')).toBeInTheDocument()
  })

  it('sends type and status filters to the backend', async () => {
    list([])
    renderPage()
    await userEvent.selectOptions(await screen.findByLabelText('Question type'), 'Dropdown')
    await waitFor(() =>
      expect(stub.calls.filter((call) => call.method === 'get').at(-1)?.params).toMatchObject({ questionType: 'Dropdown' }),
    )
  })
})
