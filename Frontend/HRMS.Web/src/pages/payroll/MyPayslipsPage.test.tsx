import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser, paged } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { MyPayslipsPage } from './MyPayslipsPage.tsx'

const payslip = {
  id: 'payslip-1', payrollRunId: 'run-1', payrollResultId: 'result-1', employeeId: 'employee-1',
  payslipNumber: 'PS/202609/001', periodStartDate: '2026-09-01', periodEndDate: '2026-09-30',
  payDate: '2026-10-05', currencyCode: 'INR', grossEarnings: 10000, totalDeductions: 1000,
  netPay: 9000, employerContributionTotal: 1200, employeeCode: 'EMP-001', employeeName: 'Nadia Farrell',
  designation: 'Engineer', department: 'Engineering', workLocation: 'HQ', status: 'Published' as const,
  version: 1, generatedAtUtc: '2026-10-01T10:00:00Z', lines: [],
}

describe('MyPayslipsPage', () => {
  let stub: StubAdapter

  beforeEach(() => {
    stub = installStubAdapter()
  })

  afterEach(() => stub.restore())

  function renderPage() {
    return renderAsUser(<MyPayslipsPage />, {
      route: '/payroll/my-payslips',
      user: makeUser({ permissions: [Permissions.payroll.payslipViewOwn] }),
    })
  }

  it('renders published payslips and supports printable view', async () => {
    stub.on('get', '/api/me/payslips', () => ({ data: ok(paged([payslip])) }))
    stub.on('get', '/api/me/payslips/payslip-1/document', () => ({ data: '<html><body>Payslip</body></html>' }))
    const popup = { document: { write: vi.fn(), close: vi.fn() }, focus: vi.fn(), print: vi.fn() }
    vi.stubGlobal('open', vi.fn(() => popup))

    renderPage()

    expect(await screen.findByText('PS/202609/001')).toBeInTheDocument()
    expect(screen.getByText('Published')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Print' }))
    await waitFor(() => expect(popup.print).toHaveBeenCalled())
  })

  it('shows the empty state when the API returns no published payslips', async () => {
    stub.on('get', '/api/me/payslips', () => ({ data: ok(paged([])) }))
    renderPage()
    expect(await screen.findByText('No published payslips are available.')).toBeInTheDocument()
  })

  it('reports API failures', async () => {
    stub.on('get', '/api/me/payslips', () => ({ status: 500, data: fail('Payslip service unavailable') }))
    renderPage()
    expect(await screen.findByText('Unable to load payslips.')).toBeInTheDocument()
  })
})
