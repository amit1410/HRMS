import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { Route, Routes } from 'react-router-dom'
import { ConfirmationReviewCaseDetailPage } from './ConfirmationReviewCaseDetailPage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser } from '../../test/fixtures.ts'
import { installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'

const caseDto = {
  id: 'case-1',
  employeeId: 'emp-1',
  employeeCode: 'E001',
  employeeName: 'Asha Rao',
  confirmationPolicyId: 'pol-1',
  confirmationPolicyVersionId: 'ver-1',
  probationCycleKey: 'ver-1:2026-01-15:2026-07-15',
  reviewRound: 1,
  probationStartDate: '2026-01-15',
  probationEndDate: '2026-07-15',
  dueDate: '2026-06-15',
  status: 'Open' as const,
  managerReviewerEmployeeId: null,
  hrbpReviewerEmployeeId: null,
  questionCount: 1,
  createdDate: '2026-06-01T00:00:00Z',
  modifiedDate: null,
}

const ratingQuestion = {
  id: 'cq-1',
  sourceQuestionId: 'q-1',
  code: 'PERF',
  questionText: 'Overall performance?',
  helpText: null,
  questionType: 'Rating' as const,
  isMandatory: true,
  displayOrder: 1,
  sourceRatingScaleId: 'scale-1',
  ratingScaleCode: 'P5',
  ratingScaleName: 'Five Point',
  ratingMin: 1,
  ratingMax: 5,
  options: [],
  ratingValues: [
    { ratingValue: 1, label: 'Low', displayOrder: 1 },
    { ratingValue: 3, label: 'Meets', displayOrder: 3 },
  ],
}

describe('ConfirmationReviewCaseDetailPage', () => {
  let stub: StubAdapter
  beforeEach(() => {
    stub = installStubAdapter()
  })
  afterEach(() => stub.restore())

  function renderPage(permissions: string[] = [Permissions.confirmation.reviewCaseView, Permissions.confirmation.reviewManagerSubmit]) {
    return renderAsUser(
      <Routes>
        <Route path="/confirmation/review-cases/:caseId" element={<ConfirmationReviewCaseDetailPage />} />
      </Routes>,
      { user: makeUser({ permissions }), route: '/confirmation/review-cases/case-1' },
    )
  }

  function seedReads() {
    stub.on('get', '/api/confirmation-review-cases/case-1', () => ({ data: ok({ case: caseDto, questions: [ratingQuestion], reviews: [] }) }))
    stub.on('get', '/api/confirmation-review-cases/case-1/history', () => ({ data: ok([]) }))
  }

  it('renders the snapshot question for the reviewer form', async () => {
    seedReads()
    renderPage()
    expect(await screen.findByText('Overall performance?')).toBeInTheDocument()
    expect(screen.getByText('Reporting Manager review')).toBeInTheDocument()
  })

  it('submits a review with the chosen rating', async () => {
    seedReads()
    stub.on('post', '/api/confirmation-review-cases/case-1/reviews/manager/submit', () => ({
      data: ok({ id: 'rev-1', reviewerType: 'ReportingManager', versionNumber: 1, status: 'Submitted', responses: [] }),
    }))
    renderPage()
    await userEvent.selectOptions(await screen.findByLabelText('Overall performance?'), '3')
    await userEvent.click(screen.getByRole('button', { name: 'Submit review' }))
    expect(await screen.findByText('Review submitted.')).toBeInTheDocument()
  })

  it('hides the form when the user cannot submit', async () => {
    seedReads()
    renderPage([Permissions.confirmation.reviewCaseView])
    expect(await screen.findByText('You do not have permission to submit a review for this case.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit review' })).not.toBeInTheDocument()
  })

  it('lets a dual-role user choose the HRBP review context', async () => {
    seedReads()
    renderPage([
      Permissions.confirmation.reviewCaseView,
      Permissions.confirmation.reviewManagerSubmit,
      Permissions.confirmation.reviewHrbpSubmit,
    ])

    await userEvent.selectOptions(await screen.findByLabelText('Review as'), 'Hrbp')
    expect(screen.getByText('HR / HRBP review')).toBeInTheDocument()
  })
})
