import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { screen } from '@testing-library/react'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { LeaveReportsPage } from './LeaveReportsPage.tsx'

describe('LeaveReportsPage', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter(); stub.on('get', '/api/leave-reports/requests', () => ({ data: ok({ items: [{ requestId: 'r1', employeeCode: 'EMP-1', employeeName: 'Priya Raman', leaveType: 'Annual Leave', startDate: '2026-04-01', quantity: 2, status: 'Approved' }], page: 1, pageSize: 25, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false }) })) })
  afterEach(() => stub.restore())

  it('renders a paged report with filters and export access for authorized HR users', async () => {
    renderAsUser(<LeaveReportsPage />, { user: makeUser({ permissions: [Permissions.leave.reportsView, Permissions.leave.reportsExport] }) })
    expect(screen.getByText('Leave Reports')).toBeInTheDocument()
    expect(screen.getByLabelText('Report')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Export CSV' })).toBeInTheDocument()
    expect(await screen.findByText('Priya Raman')).toBeInTheDocument()
    expect(screen.getByText('Approved')).toBeInTheDocument()
  })

  it('does not expose the report to unauthorized users', () => {
    renderAsUser(<LeaveReportsPage />)
    expect(screen.getByText('Reports are not available')).toBeInTheDocument()
    expect(stub.calls).toHaveLength(0)
  })
})
