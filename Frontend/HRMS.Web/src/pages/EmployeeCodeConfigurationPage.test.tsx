import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { EmployeeCodeConfigurationPage } from './EmployeeCodeConfigurationPage.tsx'
import { Permissions } from '../auth/permissions.ts'
import { makeUser } from '../test/fixtures.ts'
import { renderAsUser } from '../test/renderWith.tsx'
import { installStubAdapter, ok } from '../test/stubAdapter.ts'

const configuration = {
  id: 'config-1', versionId: 'version-1', autoGenerate: true, assignmentMode: 'Auto' as const,
  generationMethod: 'Simple' as const, prefix: 'EMP', nextNumber: 42, padding: 5, separator: '-',
  effectiveFrom: '2026-01-01', effectiveTo: null, isActive: true,
}

function setup(permissions: string[]) {
  const stub = installStubAdapter()
  stub.on('get', '/api/employee-code-configuration', () => ({ data: ok(configuration) }))
  stub.on('get', '/api/employee-code-configuration/versions', () => ({ data: ok([configuration]) }))
  stub.on('get', '/api/employee-code-configuration/rules', () => ({ data: ok([]) }))
  return { stub, user: makeUser({ permissions }) }
}

describe('Employee Code Configuration editing', () => {
  let restore: (() => void) | undefined
  afterEach(() => restore?.())

  it('edits the selected version even when its effective start date changes', async () => {
    const { stub, user } = setup([Permissions.employeeCodeConfiguration.view, Permissions.employeeCodeConfiguration.manage])
    restore = stub.restore
    stub.on('put', '/api/employee-code-configuration', () => ({ data: ok(configuration) }))
    renderAsUser(<EmployeeCodeConfigurationPage />, { user })

    fireEvent.click(await screen.findByRole('button', { name: 'Edit configuration starting 2026-01-01' }))
    fireEvent.change(screen.getByLabelText('Effective from'), { target: { value: '2025-12-01' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    await waitFor(() => expect(stub.callsTo('put', '/api/employee-code-configuration')).toHaveLength(1))
    expect(stub.callsTo('put', '/api/employee-code-configuration')[0]?.body).toEqual(expect.objectContaining({ versionId: 'version-1', effectiveFrom: '2025-12-01' }))
  })

  it('hides edit and create actions and disables settings without manage permission', async () => {
    const { stub, user } = setup([Permissions.employeeCodeConfiguration.view])
    restore = stub.restore
    renderAsUser(<EmployeeCodeConfigurationPage />, { user })

    expect(await screen.findByText(/EMP/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'New configuration' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit configuration starting 2026-01-01' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save Changes' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Effective from')).toBeDisabled()
  })
})
