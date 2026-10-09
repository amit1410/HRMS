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
  id: 'case-1', employeeId: 'emp-1', employeeCode: 'E001', employeeName: 'Asha Rao',
  confirmationPolicyId: 'pol-1', confirmationPolicyVersionId: 'ver-1', probationCycleKey: 'ver-1:x',
  probationStartDate: '2026-01-15', probationEndDate: '2026-07-15', dueDate: '2026-06-15',
  status: 'Completed' as const, managerReviewerEmployeeId: null, hrbpReviewerEmployeeId: null,
  questionCount: 1, createdDate: '2026-06-01T00:00:00Z', modifiedDate: null,
}

const preview = {
  caseId: 'case-1', status: 'Completed', caseOutcome: 'Pending', cycleNumber: 1,
  requiredApproval: 'ManagerAndHrbp', approvalSequence: 'Parallel', overallRatingMethod: 'AverageOfRatings',
  reviewers: [
    { reviewerType: 'ReportingManager', isRequired: true, isSubmitted: true, hasComments: false, ratingAnswerCount: 1 },
    { reviewerType: 'Hrbp', isRequired: true, isSubmitted: true, hasComments: false, ratingAnswerCount: 1 },
  ],
  isReadyForDecision: true, blockingReasons: [], overallRating: 4,
  appliedRuleId: 'rule-1', appliedRuleName: 'Confirm', calculatedOutcome: 'Confirmed',
  explanation: 'Overall rating 4 matched rule Confirm → Confirmed.',
  permittedOutcomes: ['Confirmed', 'NotConfirmed', 'AdditionalReview'],
}

describe('Confirmation decision panel', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  function seedReads() {
    stub.on('get', '/api/confirmation-review-cases/case-1', () => ({ data: ok({ case: caseDto, questions: [], reviews: [] }) }))
    stub.on('get', '/api/confirmation-review-cases/case-1/history', () => ({ data: ok([]) }))
    stub.on('get', '/api/confirmation-review-cases/case-1/decision/history', () => ({ data: ok({ caseId: 'case-1', decisions: [], calculations: [], extensions: [] }) }))
  }

  function renderPage() {
    return renderAsUser(
      <Routes>
        <Route path="/confirmation/review-cases/:caseId" element={<ConfirmationReviewCaseDetailPage />} />
      </Routes>,
      { user: makeUser({ permissions: [Permissions.confirmation.reviewCaseView, Permissions.confirmation.decide] }), route: '/confirmation/review-cases/case-1' },
    )
  }

  it('previews the calculated outcome and submits a decision', async () => {
    seedReads()
    stub.on('post', '/api/confirmation-review-cases/case-1/decision/preview', () => ({ data: ok(preview) }))
    stub.on('post', '/api/confirmation-review-cases/case-1/decision', () => ({
      data: ok({ decisionId: 'd-1', caseId: 'case-1', outcome: 'Confirmed', previousStatus: 'Completed', newStatus: 'Decided', decidedAtUtc: '2026-07-10T00:00:00Z', message: 'Employee confirmed.' }),
    }))
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Calculate / preview' }))
    expect(await screen.findByText(/matched rule Confirm/)).toBeInTheDocument()

    await userEvent.selectOptions(screen.getByLabelText('Outcome'), 'Confirmed')
    await userEvent.click(screen.getByRole('button', { name: 'Submit decision' }))
    expect(await screen.findByText('Employee confirmed.')).toBeInTheDocument()
  })

  it('shows blocking reasons and hides the submit form when not ready', async () => {
    seedReads()
    stub.on('post', '/api/confirmation-review-cases/case-1/decision/preview', () => ({
      data: ok({ ...preview, isReadyForDecision: false, blockingReasons: ['The required reviewer review(s) have not all been submitted.'] }),
    }))
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Calculate / preview' }))
    expect(await screen.findByText(/have not all been submitted/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit decision' })).not.toBeInTheDocument()
  })

  it('does not render the decision panel without the decide permission', async () => {
    seedReads()
    renderAsUser(
      <Routes>
        <Route path="/confirmation/review-cases/:caseId" element={<ConfirmationReviewCaseDetailPage />} />
      </Routes>,
      { user: makeUser({ permissions: [Permissions.confirmation.reviewCaseView] }), route: '/confirmation/review-cases/case-1' },
    )
    await screen.findByText('Asha Rao')
    expect(screen.queryByRole('button', { name: 'Calculate / preview' })).not.toBeInTheDocument()
  })
})
