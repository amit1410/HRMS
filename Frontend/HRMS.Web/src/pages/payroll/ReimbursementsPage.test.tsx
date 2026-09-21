import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ReimbursementsPage } from './ReimbursementsPage.tsx'
import { MyReimbursementsPage } from './MyReimbursementsPage.tsx'

const api = vi.hoisted(() => ({
  category: { id: 'cat-1', code: 'MEAL', name: 'Meals', categoryType: 'Meal', isActive: true, currencyCode: 'INR', requiresReceipt: true, allowsMultipleLines: true, taxTreatment: 'NonTaxable', defaultSettlementMethod: 'Payroll', versions: [] },
  line: { id: 'line-1', categoryId: 'cat-1', categoryCode: 'MEAL', expenseDate: '2026-09-21', description: 'Dinner', claimedAmount: 100, eligibleAmount: 100, approvedAmount: 100, taxableAmount: 0, nonTaxableAmount: 100, receiptRequired: true, receiptStatus: 'Uploaded', status: 'Approved' },
  claim: { id: 'claim-1', employeeId: 'emp-1', claimNumber: 'CLM/2026/000001', claimDate: '2026-09-21', currencyCode: 'INR', totalClaimedAmount: 100, totalEligibleAmount: 100, totalApprovedAmount: 100, taxableAmount: 0, nonTaxableAmount: 100, settledAmount: 0, settlementMethod: 'Payroll', status: 'Submitted', lines: [] as any[], attachments: [{ id: 'att-1', fileName: 'receipt.pdf', contentType: 'application/pdf', storageReference: 'store/1', fileSize: 20, uploadedAtUtc: '2026-09-21T00:00:00Z' }], settlements: [], history: [{ id: 'h-1', eventType: 'Submitted', occurredAtUtc: '2026-09-21T00:00:00Z' }] },
  listReimbursementCategories: vi.fn(), listReimbursementRegister: vi.fn(), getReimbursement: vi.fn(), createReimbursementCategory: vi.fn(), createReimbursementPolicyVersion: vi.fn(), approveReimbursement: vi.fn(), rejectReimbursement: vi.fn(), settleReimbursementManually: vi.fn(), cancelReimbursement: vi.fn(), listMyReimbursementCategories: vi.fn(), listMyReimbursements: vi.fn(), createMyReimbursement: vi.fn(), submitMyReimbursement: vi.fn(), addMyReimbursementAttachment: vi.fn(), getMyReimbursement: vi.fn(),
}))
api.claim.lines = [api.line]
api.listReimbursementCategories.mockResolvedValue([api.category]); api.listReimbursementRegister.mockResolvedValue({ items: [{ id: 'claim-1', claimNumber: api.claim.claimNumber, employeeId: 'emp-1', employeeCode: 'E001', employeeName: 'Employee One', claimDate: api.claim.claimDate, claimedAmount: 100, eligibleAmount: 100, approvedAmount: 100, taxableAmount: 0, nonTaxableAmount: 100, settledAmount: 0, outstandingAmount: 100, status: 'Submitted', settlementMethod: 'Payroll' }], page: 1, pageSize: 50, totalCount: 1 }); api.getReimbursement.mockResolvedValue(api.claim); api.listMyReimbursementCategories.mockResolvedValue([api.category]); api.listMyReimbursements.mockResolvedValue({ items: [api.claim], page: 1, pageSize: 50, totalCount: 1 }); api.createMyReimbursement.mockResolvedValue(api.claim); api.getMyReimbursement.mockResolvedValue(api.claim)
vi.mock('../../api/reimbursements.ts', () => api)

describe('Phase 7O reimbursement UI', () => {
  it('renders category policy controls, register filters and full register columns', async () => {
    render(<ReimbursementsPage />)
    expect(await screen.findByText('Category and policy master')).toBeInTheDocument()
    expect(screen.getByLabelText('Employee filter')).toBeInTheDocument()
    expect(screen.getByLabelText('Settlement method filter')).toBeInTheDocument()
    expect(screen.getByText('Outstanding')).toBeInTheDocument()
    expect(screen.getByText('CLM/2026/000001')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'CLM/2026/000001' }))
    expect(await screen.findByText('receipt.pdf (application/pdf)')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Approve' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Partial approve' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reject' })).toBeInTheDocument()
  })

  it('exposes ESS multi-line editing and receipt metadata', async () => {
    render(<MyReimbursementsPage />)
    expect(await screen.findByText('New draft claim')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add line' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Add line' }))
    expect(screen.getByText('Claim line 2')).toBeInTheDocument()
    expect(screen.getByLabelText('Receipt storage reference')).toBeInTheDocument()
    expect(screen.getByText('CLM/2026/000001')).toBeInTheDocument()
  })
})
