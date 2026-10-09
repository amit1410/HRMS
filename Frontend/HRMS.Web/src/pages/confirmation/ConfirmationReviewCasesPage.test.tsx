import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { ConfirmationReviewCasesPage } from './ConfirmationReviewCasesPage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser, paged } from '../../test/fixtures.ts'
import { installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'

const reviewCase = {
  id: 'case-1',
  employeeId: 'emp-1',
  employeeCode: 'E001',
  employeeName: 'Asha Rao',
  confirmationPolicyId: 'pol-1',
  confirmationPolicyVersionId: 'ver-1',
  probationCycleKey: 'ver-1:2026-01-15:2026-07-15',
  probationStartDate: '2026-01-15',
  probationEndDate: '2026-07-15',
  dueDate: '2026-06-15',
  status: 'Open' as const,
  managerReviewerEmployeeId: null,
  hrbpReviewerEmployeeId: null,
  questionCount: 3,
  createdDate: '2026-06-01T00:00:00Z',
  modifiedDate: null,
}

describe('ConfirmationReviewCasesPage', () => {
  let stub: StubAdapter
  beforeEach(() => {
    stub = installStubAdapter()
  })
  afterEach(() => stub.restore())

  function renderPage(permissions = [Permissions.confirmation.reviewCaseView, Permissions.confirmation.reviewCaseManage]) {
    return renderAsUser(<ConfirmationReviewCasesPage />, { user: makeUser({ permissions }) })
  }

  it('renders review case rows', async () => {
    stub.on('get', '/api/confirmation-review-cases', () => ({ data: ok(paged([reviewCase])) }))
    renderPage()
    expect(await screen.findByText('Asha Rao')).toBeInTheDocument()
    expect(screen.getByText('E001')).toBeInTheDocument()
  })

  it('sends status filter to the backend', async () => {
    stub.on('get', '/api/confirmation-review-cases', () => ({ data: ok(paged([])) }))
    renderPage()
    await userEvent.selectOptions(await screen.findByLabelText('Status'), 'Completed')
    await waitFor(() =>
      expect(stub.calls.filter((call) => call.method === 'get').at(-1)?.params).toMatchObject({ status: 'Completed' }),
    )
  })

  it('hides the initialize card without manage permission', async () => {
    stub.on('get', '/api/confirmation-review-cases', () => ({ data: ok(paged([reviewCase])) }))
    renderPage([Permissions.confirmation.reviewCaseView])
    await screen.findByText('Asha Rao')
    expect(screen.queryByText('Initialize case')).not.toBeInTheDocument()
  })
})
