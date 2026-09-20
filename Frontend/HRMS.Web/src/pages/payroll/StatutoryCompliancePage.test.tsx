import { screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { installStubAdapter, type StubAdapter } from '../../test/stubAdapter.ts'
import { StatutoryCompliancePage } from './StatutoryCompliancePage.tsx'

describe('StatutoryCompliancePage', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  it('renders the compliance period and return actions', () => {
    renderAsUser(<StatutoryCompliancePage />, { route: '/payroll/statutory-compliance', user: makeUser({ permissions: [Permissions.payroll.statutoryComplianceView] }) })
    expect(screen.getByRole('heading', { name: 'Statutory Compliance' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create open period' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Generate return' })).toBeDisabled()
  })
})
