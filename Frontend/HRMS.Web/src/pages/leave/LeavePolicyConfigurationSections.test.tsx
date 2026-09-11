import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser, paged } from '../../test/fixtures.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { LeavePolicyConfigurationSections } from './LeavePolicyConfigurationSections.tsx'

const policyId = 'policy-1'
const version = { id: 'version-1', versionNumber: 1, effectiveFrom: '2027-01-01', effectiveTo: null, status: 'Draft' as const, priority: 10, leaveTypeCount: 1, applicabilityGroupCount: 0, createdDate: '2027-01-01T00:00:00Z', createdBy: 'admin', modifiedDate: null, concurrencyToken: 'version-token', allowedActions: { canEdit: true, canValidate: true, canPublish: true, canRetire: false, canCreateVersion: true } }
const activeType = { id: 'type-active', code: 'CL', name: 'Casual Leave', isActive: true }
const inactiveType = { id: 'type-inactive', code: 'OLD', name: 'Old Leave', isActive: false }

describe('LeavePolicyConfigurationSections', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())
  function renderSections(permissions: string[] = [Permissions.leave.policyManage], groups: unknown[] = [], selected = [inactiveType]) {
    stub.on('get', '/api/leave-types', () => ({ data: ok(paged([activeType, inactiveType])) }))
    stub.on('get', '/api/leave-policies/policy-1/versions/version-1/applicability', () => ({ data: ok(groups) }))
    return renderAsUser(<LeavePolicyConfigurationSections policyId={policyId} version={version} selectedLeaveTypes={selected} canManage={permissions.includes(Permissions.leave.policyManage)} onNotice={() => undefined} />, { user: makeUser({ permissions }) })
  }

  it('loads active options and preserves a selected inactive historical Leave Type', async () => {
    renderSections()
    expect(await screen.findByText('CL — Casual Leave')).toBeInTheDocument()
    expect(screen.getByText('OLD — Old Leave')).toBeInTheDocument()
    expect(screen.getByText('Inactive historical reference')).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: /OLD — Old Leave/ })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: /OLD — Old Leave/ })).toBeDisabled()
  })

  it('saves selected Leave Type IDs through the aggregate endpoint', async () => {
    stub.on('put', '/api/leave-policies/policy-1/versions/version-1/leave-types', () => ({ data: ok([activeType]) }))
    renderSections()
    await screen.findByText('CL — Casual Leave')
    await userEvent.click(screen.getByRole('checkbox', { name: /CL — Casual Leave/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Save Leave Types' }))
    await waitFor(() => expect(stub.callsTo('put', '/api/leave-policies/policy-1/versions/version-1/leave-types')[0]?.body).toEqual({ leaveTypeIds: ['type-inactive', 'type-active'], concurrencyToken: 'version-token' }))
  })

  it('finds active Leave Types by code or name with case-insensitive search', async () => {
    renderSections([Permissions.leave.policyManage], [], [])
    expect(await screen.findByText(/CL.*Casual Leave/)).toBeInTheDocument()
    const search = screen.getByRole('searchbox', { name: 'Search Leave Types' })
    await userEvent.type(search, 'casu')
    expect(screen.getByText(/CL.*Casual Leave/)).toBeInTheDocument()
    await userEvent.clear(search)
    await userEvent.type(search, 'CASUAL')
    expect(screen.getByText(/CL.*Casual Leave/)).toBeInTheDocument()
  })

  it('hides unselected inactive Leave Types and allows selecting active Leave Types', async () => {
    renderSections([Permissions.leave.policyManage], [], [])
    expect(await screen.findByText(/CL.*Casual Leave/)).toBeInTheDocument()
    expect(screen.queryByText(/OLD.*Old Leave/)).not.toBeInTheDocument()
    const activeCheckbox = screen.getByRole('checkbox', { name: /CL.*Casual Leave/ })
    expect(activeCheckbox).not.toBeChecked()
    await userEvent.click(activeCheckbox)
    expect(activeCheckbox).toBeChecked()
  })

  it('keeps selected IDs and shows the version conflict when saving with a stale token', async () => {
    stub.on('put', '/api/leave-policies/policy-1/versions/version-1/leave-types', () => ({ status: 409, data: fail('Configuration changed by another user. Reload before saving.') }))
    renderSections()
    await screen.findByText(/CL.*Casual Leave/)
    await userEvent.click(screen.getByRole('checkbox', { name: /CL.*Casual Leave/ }))
    await userEvent.click(screen.getByRole('button', { name: 'Save Leave Types' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/changed by another user/)
    expect(screen.getByRole('checkbox', { name: /OLD.*Old Leave/ })).toBeChecked()
    expect(stub.callsTo('put', '/api/leave-policies/policy-1/versions/version-1/leave-types')[0]?.body).toEqual({ leaveTypeIds: ['type-inactive', 'type-active'], concurrencyToken: 'version-token' })
  })

  it('renders zero groups as intentional tenant-wide applicability', async () => {
    renderSections()
    expect(await screen.findByText('Tenant-wide applicability')).toBeInTheDocument()
    expect(screen.getByText(/not described as “no employees”|all employees in the tenant/)).toBeInTheDocument()
  })

  it('shows ALL within a group and OR between groups, then removes the group', async () => {
    const group = { id: 'group-1', gender: null, holdingCompanyId: null, lobId: null, organisationId: null, departmentId: null, subDepartmentId: null, sectionId: null, subSectionId: null, functionId: null, subFunctionId: null, gradeId: null, designationId: null, employeeTypeId: null, countryLocationId: null, workLocationId: null, costCenterId: null }
    stub.on('get', /\/api\/(master-data|countries|designations)/, () => ({ data: ok([]) }))
    renderSections([Permissions.leave.policyManage], [group, { ...group, id: 'group-2' }])
    expect(await screen.findByText('Applicability Group 1')).toBeInTheDocument()
    expect(screen.getAllByText('ALL conditions in this group must match.')).toHaveLength(2)
    expect(screen.getByRole('separator', { name: 'OR between applicability groups' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Remove Applicability Group 1' }))
    expect(screen.queryByText('Applicability Group 2')).not.toBeInTheDocument()
  })

  it('is read-only for PolicyView and keeps persisted configuration visible', async () => {
    const group = { id: 'group-1', gender: null, holdingCompanyId: null, lobId: null, organisationId: null, departmentId: null, subDepartmentId: null, sectionId: null, subSectionId: null, functionId: null, subFunctionId: null, gradeId: null, designationId: null, employeeTypeId: null, countryLocationId: null, workLocationId: null, costCenterId: null }
    renderSections([Permissions.leave.policyView], [group])
    expect(await screen.findByText('Applicability Group 1')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Add Applicability Group' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save Applicability' })).not.toBeInTheDocument()
  })

  it('preserves editor state after an applicability validation error', async () => {
    stub.on('put', '/api/leave-policies/policy-1/versions/version-1/applicability', () => ({ status: 400, data: fail('The selected Sub Department does not belong to the selected Department.') }))
    const group = { id: 'group-1', gender: null, holdingCompanyId: null, lobId: null, organisationId: null, departmentId: null, subDepartmentId: null, sectionId: null, subSectionId: null, functionId: null, subFunctionId: null, gradeId: null, designationId: null, employeeTypeId: null, countryLocationId: null, workLocationId: null, costCenterId: null }
    stub.on('get', /\/api\/(master-data|countries|designations)/, () => ({ data: ok([]) }))
    renderSections([Permissions.leave.policyManage], [group])
    const heading = await screen.findByText('Applicability Group 1')
    await userEvent.click(screen.getByRole('button', { name: 'Save Applicability' }))
    expect(await screen.findByText(/does not belong/)).toBeInTheDocument()
    expect(within(heading.closest('.applicability-group') as HTMLElement).getByText('ALL conditions in this group must match.')).toBeInTheDocument()
  })
})
