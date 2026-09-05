import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { LeavePolicyEditorPage } from './LeavePolicyEditorPage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser, paged } from '../../test/fixtures.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'

const policy = { id: 'policy-1', code: 'IND-CORP', name: 'India Corporate', description: 'Corporate policy', isActive: true, versionCount: 3, currentVersionNumber: 2, createdDate: '2027-01-01T00:00:00Z', modifiedDate: null, concurrencyToken: 'policy-token' }
const draft = { id: 'version-3', versionNumber: 3, effectiveFrom: '2027-01-01', effectiveTo: null, status: 'Draft' as const, priority: 10, leaveTypeCount: 1, applicabilityGroupCount: 0, createdDate: '2027-01-01T00:00:00Z', createdBy: 'admin', modifiedDate: null, concurrencyToken: 'draft-token', allowedActions: { canEdit: true, canValidate: true, canPublish: true, canRetire: false, canCreateVersion: true } }
const published = { ...draft, id: 'version-2', versionNumber: 2, status: 'Published' as const, allowedActions: { ...draft.allowedActions, canValidate: false, canPublish: false, canRetire: true } }
const retired = { ...published, id: 'version-1', versionNumber: 1, status: 'Retired' as const, allowedActions: { ...published.allowedActions, canRetire: false } }

describe('Leave Policy lifecycle UI', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())
  function setup(validation?: unknown) {
    stub.on('get', '/api/leave-policies/policy-1/versions', () => ({ data: ok(paged([draft, published, retired])) }))
    stub.on('get', '/api/leave-policies/policy-1/editor', call => { const requestedVersionId = typeof call.params.versionId === 'string' ? call.params.versionId : undefined; const selectedVersion = requestedVersionId === 'version-3' ? draft : requestedVersionId === 'version-2' ? published : requestedVersionId === 'version-1' ? retired : null; return { data: ok({ policy, currentVersion: selectedVersion, leaveTypes: [], applicabilityGroups: [] }) } })
    stub.on('get', '/api/leave-policies/policy-1/versions/version-3/applicability', () => ({ data: ok([]) }))
    stub.on('get', '/api/leave-types', () => ({ data: ok(paged([])) }))
    if (validation) stub.on('post', '/api/leave-policies/policy-1/versions/version-3/validate', () => ({ data: ok(validation) }))
  }
  function renderPage(permissions: string[], versionId = 'version-3') { return renderAsUser(<Routes><Route path="/leave-management/policies/:policyId" element={<LeavePolicyEditorPage />} /></Routes>, { user: makeUser({ permissions }), route: `/leave-management/policies/policy-1?versionId=${versionId}` }) }

  it('shows Validate Draft for PolicyManage and displays structured errors and warnings', async () => {
    setup({ isValid: false, errors: [{ field: 'leaveTypes', message: 'At least one Leave Type is required.' }], warnings: ['Review the effective date.'] })
    renderPage([Permissions.leave.policyView, Permissions.leave.policyManage])
    await screen.findByRole('button', { name: 'Validate Draft' })
    await userEvent.click(screen.getByRole('button', { name: 'Validate Draft' }))
    expect(await screen.findByText('Not ready')).toBeInTheDocument()
    expect(screen.getByText('At least one Leave Type is required.')).toBeInTheDocument()
    expect(screen.getByText('Review the effective date.')).toBeInTheDocument()
    expect(stub.callsTo('post', '/api/leave-policies/policy-1/versions/version-3/validate')).toHaveLength(1)
    expect(screen.getByRole('combobox', { name: 'Policy version' })).toHaveValue('version-3')
  })

  it('shows a valid result and marks it stale after a Draft setting changes', async () => {
    setup({ isValid: true, errors: [], warnings: [] })
    renderPage([Permissions.leave.policyView, Permissions.leave.policyManage])
    await screen.findByLabelText(/^Priority/)
    await screen.findByRole('button', { name: 'Validate Draft' })
    await userEvent.click(screen.getByRole('button', { name: 'Validate Draft' }))
    expect(await screen.findByText('Valid')).toBeInTheDocument()
    await userEvent.clear(screen.getByLabelText(/^Priority/))
    await userEvent.type(screen.getByLabelText(/^Priority/), '20')
    expect(await screen.findByText(/Configuration has changed since the last validation/)).toBeInTheDocument()
  })

  it('keeps Validate available but hides Publish and Retire without PolicyPublish', async () => {
    setup()
    renderPage([Permissions.leave.policyView, Permissions.leave.policyManage])
    await screen.findByRole('button', { name: 'Validate Draft' })
    expect(screen.getByRole('button', { name: 'Validate Draft' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Publish Version' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Retire Version' })).not.toBeInTheDocument()
  })

  it('confirms and publishes, then refreshes the selected version', async () => {
    setup()
    stub.on('post', '/api/leave-policies/policy-1/versions/version-3/publish', () => ({ data: ok({ ...draft, status: 'Published' }) }))
    renderPage([Permissions.leave.policyView, Permissions.leave.policyPublish], 'version-3')
    await screen.findByRole('button', { name: 'Publish Version' })
    await userEvent.click(screen.getByRole('button', { name: 'Publish Version' }))
    expect(await screen.findByRole('alertdialog')).toHaveTextContent(/can no longer be edited/)
    await userEvent.click(screen.getByRole('alertdialog').querySelector('button.button-danger') as HTMLButtonElement)
    await waitFor(() => expect(stub.callsTo('post', '/api/leave-policies/policy-1/versions/version-3/publish')).toHaveLength(1))
  })

  it('shows Retire only for an eligible Published version and preserves history', async () => {
    setup()
    renderPage([Permissions.leave.policyView, Permissions.leave.policyPublish], 'version-2')
    await screen.findByRole('button', { name: 'Retire Version' })
    expect(screen.getByRole('button', { name: 'Retire Version' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Publish Version' })).not.toBeInTheDocument()
    stub.on('post', '/api/leave-policies/policy-1/versions/version-2/retire', () => ({ data: ok({ ...published, status: 'Retired' }) }))
    await userEvent.click(screen.getByRole('button', { name: 'Retire Version' }))
    await userEvent.click(screen.getByRole('alertdialog').querySelector('button.button-danger') as HTMLButtonElement)
    await waitFor(() => expect(stub.callsTo('post', '/api/leave-policies/policy-1/versions/version-2/retire')).toHaveLength(1))
    expect(screen.getByText('Version History')).toBeInTheDocument()
  })

  it('renders validation conflict without discarding the Draft editor', async () => {
    setup()
    stub.on('post', '/api/leave-policies/policy-1/versions/version-3/publish', () => ({ status: 409, data: fail('Configuration changed by another user. Reload before publishing.') }))
    renderPage([Permissions.leave.policyView, Permissions.leave.policyPublish])
    await screen.findByRole('button', { name: 'Publish Version' })
    await userEvent.click(screen.getByRole('button', { name: 'Publish Version' }))
    await userEvent.click(screen.getByRole('alertdialog').querySelector('button.button-danger') as HTMLButtonElement)
    expect(await screen.findByText(/changed by another user/)).toBeInTheDocument()
    expect(screen.getByText('Version History')).toBeInTheDocument()
  })
})
