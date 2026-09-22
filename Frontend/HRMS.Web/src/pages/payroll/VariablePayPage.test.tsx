import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { MyVariablePayPage, VariablePayPage } from './VariablePayPage.tsx'

const api = vi.hoisted(() => ({ listVariablePayPlans: vi.fn(), createVariablePayPlan: vi.fn(), listVariablePayAwards: vi.fn(), listMyVariablePay: vi.fn() }))
api.listVariablePayPlans.mockResolvedValue([{ id: 'plan-1', code: 'BONUS', name: 'Annual bonus', planType: 'FixedBonus', currencyCode: 'INR', isActive: true, versions: [] }])
api.listVariablePayAwards.mockResolvedValue({ items: [{ id: 'award-1', employeeId: 'emp-1', awardNumber: 'VPA/2026/000001', awardPeriodFrom: '2026-01-01', awardPeriodTo: '2026-12-31', calculatedAmount: 1000, approvedAmount: 1000, taxableAmount: 1000, nonTaxableAmount: 0, settledAmount: 0, outstandingAmount: 1000, status: 'Approved' }], page: 1, pageSize: 50, totalCount: 1 })
api.listMyVariablePay.mockResolvedValue([{ id: 'award-1', employeeId: 'emp-1', awardNumber: 'VPA/2026/000001', awardPeriodFrom: '2026-01-01', awardPeriodTo: '2026-12-31', calculatedAmount: 1000, approvedAmount: 1000, taxableAmount: 1000, nonTaxableAmount: 0, settledAmount: 0, outstandingAmount: 1000, status: 'Approved' }])
vi.mock('../../api/variablePay.ts', () => api)

describe('Phase 7Q variable-pay UI', () => {
  it('renders plan and award register data', async () => {
    render(<VariablePayPage />)
    expect(await screen.findByText(/Annual bonus/)).toBeInTheDocument()
    expect(screen.getByText('VPA/2026/000001')).toBeInTheDocument()
  })

  it('renders ESS awards without an employee selector', async () => {
    render(<MyVariablePayPage />)
    expect(await screen.findByText(/VPA\/2026\/000001/)).toBeInTheDocument()
    expect(screen.queryByLabelText('Employee')).not.toBeInTheDocument()
  })
})
