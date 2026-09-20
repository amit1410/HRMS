import { screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser, paged } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { BankAdvicePage } from './BankAdvicePage.tsx'

const batch = { id: 'batch-1', payrollRunId: 'run-1', payrollPeriodId: 'period-1', batchNumber: 'BA/202610/001', batchDate: '2026-10-01', payDate: '2026-10-05', currencyCode: 'INR', status: 'Prepared' as const, totalEmployees: 1, totalAmount: 9000, generatedAtUtc: '2026-10-01T10:00:00Z', approvedAtUtc: null, exportedAtUtc: null, payments: [{ id: 'payment-1', employeeId: 'employee-1', employeeCode: 'EMP-001', employeeName: 'Nadia Farrell', netPay: 9000, currencyCode: 'INR', paymentStatus: 'Ready', validationStatus: 'Valid' as const, validationMessage: null, sequence: 1, paymentReference: 'BA/202610/001/0001', accountHolderName: 'Nadia Farrell', bankName: 'Test Bank', maskedAccountNumber: 'XXXXXX1234', ifscCode: 'TEST0001', branchName: null }] }

describe('BankAdvicePage', () => {
  let stub: StubAdapter
  beforeEach(() => { stub = installStubAdapter() })
  afterEach(() => stub.restore())

  it('renders batches and payment validation state', async () => {
    stub.on('get', '/api/payroll/bank-advice', () => ({ data: ok(paged([batch])) }))
    stub.on('get', '/api/payroll/bank-advice/batch-1', () => ({ data: ok(batch) }))
    renderAsUser(<BankAdvicePage />, { route: '/payroll/bank-advice', user: makeUser({ permissions: [Permissions.payroll.bankAdviceView] }) })
    expect(await screen.findByText(/BA\/202610\/001/)).toBeInTheDocument()
    expect(screen.getByText(/Prepared/)).toBeInTheDocument()
  })

  it('shows empty state', async () => {
    stub.on('get', '/api/payroll/bank-advice', () => ({ data: ok(paged([])) }))
    renderAsUser(<BankAdvicePage />, { route: '/payroll/bank-advice', user: makeUser({ permissions: [Permissions.payroll.bankAdviceView] }) })
    expect(await screen.findByText('No bank advice batches found.')).toBeInTheDocument()
  })
})
