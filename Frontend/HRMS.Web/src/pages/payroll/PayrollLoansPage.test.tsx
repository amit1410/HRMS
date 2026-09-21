import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { MyLoansPage, PayrollLoansPage } from './PayrollLoansPage.tsx'

vi.mock('../../api/payroll.ts', () => ({
  listLoanProducts: vi.fn().mockResolvedValue([{ id: 'p1', code: 'LN', name: 'Loan', productType: 'Loan', isActive: true, currencyCode: 'INR', minAmount: 100, maxAmount: 10000, minTenureMonths: 1, maxTenureMonths: 12, interestMethod: 'None', recoveryPolicy: 'RecoverFullOrFail' }]),
  listPayrollLoans: vi.fn().mockResolvedValue([{ id: 'l1', employeeId: 'e1', loanProductId: 'p1', loanNumber: 'LN/001', requestedAmount: 1000, approvedAmount: null, requestedTenureMonths: 12, status: 'Submitted', outstandingPrincipal: 1000, outstandingInterest: 0, outstandingTotal: 1000, currencyCode: 'INR', installments: [] }]),
  listLoanRegister: vi.fn().mockResolvedValue({ items: [{ id: 'l1', loanNumber: 'LN/001', employeeId: 'e1', employeeCode: 'E001', employeeName: 'Employee One', productCode: 'LN', productType: 'Loan', approvedAmount: 1000, principalRecovered: 0, interestRecovered: 0, totalRecovered: 0, outstandingPrincipal: 1000, outstandingInterest: 0, outstandingTotal: 1000, status: 'Submitted' }], page: 1, pageSize: 50, totalCount: 1 }),
  listMyLoanProducts: vi.fn().mockResolvedValue([{ id: 'p1', code: 'LN', name: 'Loan', productType: 'Loan', isActive: true, currencyCode: 'INR', minAmount: 100, maxAmount: 10000, minTenureMonths: 1, maxTenureMonths: 12, interestMethod: 'Flat', interestRate: 5, recoveryPolicy: 'RecoverFullOrFail' }]),
  listMyLoans: vi.fn().mockResolvedValue([{ id: 'l1', employeeId: 'linked-e1', loanProductId: 'p1', loanNumber: 'LN/ESS/001', requestedAmount: 1000, approvedAmount: 1000, approvedTenureMonths: 12, requestedTenureMonths: 12, status: 'Active', outstandingPrincipal: 900, outstandingInterest: 50, outstandingTotal: 950, currencyCode: 'INR', installments: [{ id: 'i1', installmentNumber: 1, dueDate: '2026-10-01', openingPrincipal: 1000, principalAmount: 100, interestAmount: 5, installmentAmount: 105, closingPrincipal: 900, status: 'Scheduled', recoveredAmount: 0 }] }]),
  getPayrollLoanSchedule: vi.fn().mockResolvedValue([{ id: 'i1', installmentNumber: 1, dueDate: '2026-10-01', openingPrincipal: 1000, principalAmount: 100, interestAmount: 5, installmentAmount: 105, closingPrincipal: 900, status: 'Scheduled', recoveredAmount: 0 }]),
  createLoanProduct: vi.fn(), updateLoanProduct: vi.fn(), createMyLoan: vi.fn(), submitMyLoan: vi.fn(),
  approvePayrollLoan: vi.fn(), rejectPayrollLoan: vi.fn(), recordLoanDisbursement: vi.fn(), recordLoanRepayment: vi.fn(), prepayLoan: vi.fn(), closeLoan: vi.fn(), cancelPayrollLoan: vi.fn(),
}))

describe('Phase 7N loan workflows', () => {
  it('renders product CRUD, admin loans and the register', async () => {
    render(<PayrollLoansPage />)
    expect(await screen.findByText('Loan Products')).toBeInTheDocument()
    expect(screen.getAllByText('Loans & Advances').length).toBeGreaterThan(0)
    expect(screen.getByText('Loan Register')).toBeInTheDocument()
    expect(screen.getAllByText('LN/001').length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: 'Approve' })).toBeInTheDocument()
  })

  it('exposes the authoritative admin lifecycle action and refreshes after approval', async () => {
    render(<PayrollLoansPage />)
    fireEvent.click(await screen.findByRole('button', { name: 'Approve' }))
    expect(screen.getByRole('button', { name: 'Approve' })).toBeInTheDocument()
  })

  it('renders the ESS request and own-loans workflow', async () => {
    render(<MyLoansPage />)
    expect(await screen.findByText('Eligible Products')).toBeInTheDocument()
    expect(screen.getByText('My Loans')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Submit request' })).toBeDisabled()
    expect(screen.getByText('LN/ESS/001')).toBeInTheDocument()
  })

  it('renders admin filters and schedule detail without exposing invalid lifecycle actions', async () => {
    render(<PayrollLoansPage />)
    expect(await screen.findByLabelText('Employee filter')).toBeInTheDocument()
    expect(screen.getByLabelText('Product type filter')).toBeInTheDocument()
    expect(screen.getByLabelText('From date')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'LN/001' }))
    expect(await screen.findByRole('list', { name: 'Loan schedule' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Manual repayment' })).not.toBeInTheDocument()
  })

  it('renders ESS loan detail and schedule from the linked employee result', async () => {
    render(<MyLoansPage />)
    fireEvent.click(await screen.findByRole('button', { name: 'LN/ESS/001' }))
    expect(await screen.findByText('Approved terms: ₹1,000.00 over 12 months.')).toBeInTheDocument()
    expect(screen.getByRole('list', { name: 'Loan schedule' })).toBeInTheDocument()
  })
})
