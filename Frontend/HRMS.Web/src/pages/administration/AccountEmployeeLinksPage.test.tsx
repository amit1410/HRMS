import { fireEvent, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { ApiError } from '../../api/errors.ts'
import { Permissions } from '../../auth/permissions.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { AccountEmployeeLinksPage } from './AccountEmployeeLinksPage.tsx'

const api = vi.hoisted(() => ({
  getUserCandidates: vi.fn(), getEmployeeCandidates: vi.fn(), getLinkState: vi.fn(), getLinkHistory: vi.fn(),
  linkAccount: vi.fn(), replaceAccount: vi.fn(), unlinkAccount: vi.fn(),
}))

vi.mock('../../api/accountEmployeeLinks.ts', () => api)

const user = { id: 'actor', displayName: 'Subject User', email: 'subject@test', employeeCode: null, eligibility: null }
const employee = { id: 'employee', displayName: 'Future Joiner', email: 'future@test', employeeCode: 'EMP-1', eligibility: 'Eligible' }
const linkedState = { userId: user.id, status: 'Linked' as const, currentLink: { linkId: 'link', employeeId: employee.id, displayName: employee.displayName, employeeCode: employee.employeeCode, originalActorUserId: 'admin', originalOccurredAtUtc: '2026-01-01T00:00:00Z' }, revision: 'revision' }
const unlinkedState = { userId: user.id, status: 'Unlinked' as const, currentLink: null, revision: null }

beforeEach(() => {
  vi.clearAllMocks()
  api.getUserCandidates.mockResolvedValue({ items: [user] })
  api.getEmployeeCandidates.mockResolvedValue({ items: [employee] })
  api.getLinkState.mockResolvedValue(unlinkedState)
  api.getLinkHistory.mockResolvedValue({ items: [{ id: 'event', sequence: 1, operation: 'Link', actorUserId: 'admin', afterEmployeeId: employee.id, reason: 'verified', occurredAtUtc: '2026-01-01T00:00:00Z' }] })
})

function renderManager(extraPermissions: string[] = []) {
  return renderAsUser(<AccountEmployeeLinksPage />, { user: { id: 'different-actor', permissions: [Permissions.accountEmployeeLink.manage, ...extraPermissions] } as never })
}

async function selectAndPrepare() {
  await waitFor(() => expect(screen.getByRole('option', { name: /Future Joiner/ })).toBeTruthy())
  fireEvent.change(screen.getByLabelText('Account'), { target: { value: user.id } })
  fireEvent.change(screen.getByLabelText('Employee'), { target: { value: employee.id } })
  fireEvent.change(screen.getByLabelText('Reason'), { target: { value: 'verified onboarding' } })
}

it('hides mutation controls without Manage even when View is present', async () => {
  renderAsUser(<AccountEmployeeLinksPage />, { user: { permissions: [Permissions.accountEmployeeLink.view] } as never })
  await waitFor(() => expect(screen.getByText('Account–Employee Links')).toBeTruthy())
  expect(screen.queryByLabelText('Employee')).toBeNull()
  expect(screen.queryByRole('button', { name: 'Link / replace' })).toBeNull()
})

it('keeps the account selected, refreshes current state/history, and preserves the linked employee outside candidates', async () => {
  api.linkAccount.mockResolvedValue(linkedState)
  api.getLinkState.mockResolvedValueOnce(unlinkedState).mockResolvedValue(linkedState)
  api.getEmployeeCandidates.mockResolvedValueOnce({ items: [employee] }).mockResolvedValue({ items: [] })
  renderManager([Permissions.accountEmployeeLink.viewHistory])
  await selectAndPrepare()
  fireEvent.click(screen.getByRole('button', { name: 'Link / replace' }))

  await waitFor(() => expect(api.linkAccount).toHaveBeenCalledWith(user.id, { employeeId: employee.id, expectedRevision: null, reason: 'verified onboarding' }))
  await waitFor(() => expect(screen.getByLabelText('Account')).toHaveValue(user.id))
  expect(screen.getByText('Future Joiner (EMP-1)')).toBeTruthy()
  expect(screen.getByText('Link: verified')).toBeTruthy()
  expect(screen.queryByRole('option', { name: /Future Joiner/ })).toBeNull()
  expect(api.getLinkState).toHaveBeenCalledWith(user.id)
  expect(api.getLinkHistory).toHaveBeenCalledWith(user.id)
})

it('reloads with an already-linked account and reselecting it retrieves persisted state', async () => {
  api.getLinkState.mockResolvedValue(linkedState)
  api.getEmployeeCandidates.mockResolvedValue({ items: [] })
  renderManager([Permissions.accountEmployeeLink.viewHistory])
  await waitFor(() => expect(screen.getByRole('option', { name: /Subject User/ })).toBeTruthy())
  fireEvent.change(screen.getByLabelText('Account'), { target: { value: user.id } })
  await waitFor(() => expect(screen.getByText('Future Joiner (EMP-1)')).toBeTruthy())
  expect(api.getLinkState).toHaveBeenCalledWith(user.id)
  expect(screen.queryByRole('option', { name: /Future Joiner/ })).toBeNull()
})

it('does not request history when ViewHistory is absent and explains the permission', async () => {
  renderManager()
  await waitFor(() => expect(screen.getByRole('option', { name: /Subject User/ })).toBeTruthy())
  fireEvent.change(screen.getByLabelText('Account'), { target: { value: user.id } })
  await waitFor(() => expect(screen.getByText(/lacks AccountEmployeeLink.ViewHistory/)).toBeTruthy())
  expect(api.getLinkHistory).not.toHaveBeenCalled()
})

it('shows a history 403 as forbidden instead of empty history', async () => {
  api.getLinkHistory.mockRejectedValue(new ApiError('History permission denied.', { status: 403 }))
  renderManager([Permissions.accountEmployeeLink.viewHistory])
  await waitFor(() => expect(screen.getByRole('option', { name: /Subject User/ })).toBeTruthy())
  fireEvent.change(screen.getByLabelText('Account'), { target: { value: user.id } })
  await waitFor(() => expect(screen.getByText('You do not have permission to view this data.')).toBeTruthy())
  expect(screen.queryByText('No link history recorded.')).toBeNull()
})

it('distinguishes current-link request errors from an unlinked result', async () => {
  api.getLinkState.mockRejectedValue(new ApiError('Current link request failed.', { status: 500 }))
  renderManager()
  await waitFor(() => expect(screen.getByRole('option', { name: /Subject User/ })).toBeTruthy())
  fireEvent.change(screen.getByLabelText('Account'), { target: { value: user.id } })
  await waitFor(() => expect(screen.getByText('Current link request failed.')).toBeTruthy())
  expect(screen.queryByText('No current employee link.')).toBeNull()
})
