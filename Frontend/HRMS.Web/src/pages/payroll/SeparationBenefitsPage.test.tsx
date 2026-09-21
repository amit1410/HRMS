import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { MySeparationBenefitsPage, SeparationBenefitsPage } from './SeparationBenefitsPage.tsx'

const api = vi.hoisted(() => ({
  listGratuityPolicies: vi.fn().mockResolvedValue([{ id: 'p1', code: 'STANDARD', name: 'Standard', isActive: true, currencyCode: 'INR', versions: [{ id: 'v1', effectiveFrom: '2026-01-01', formulaType: 'FixedAmount', wageBasisType: 'FixedConfiguredAmount' }] }]),
  listSeparationBenefitRegister: vi.fn().mockResolvedValue({ items: [{ employeeId: 'e1', employeeCode: 'E001', employeeName: 'Employee One', serviceEndDate: '2026-09-30', separationReason: 'Resignation', totalServiceMonths: 24, gratuityAmount: 10000, leaveEncashmentAmount: 0, noticePayAmount: 0, noticeRecoveryAmount: 0, taxableAmount: 0, nonTaxableAmount: 10000, netBenefit: 10000, finalSettlementStatus: 'Calculated' }], page: 1, pageSize: 50, totalCount: 1 }),
  getMySeparationBenefits: vi.fn().mockResolvedValue({ employeeId: 'e1', gratuity: { finalGratuityAmount: 10000, taxableAmount: 0, nonTaxableAmount: 10000, wageBasisAmount: 50000, serviceLength: { totalServiceDays: 730, totalServiceMonths: 24, eligibleServiceUnits: 2 } }, leaveEncashment: [], finalSettlementStatus: 'Finalized' }),
  getMySeparationBenefitHistory: vi.fn().mockResolvedValue([{ id: 'mh1', eventType: 'Finalized', occurredAtUtc: '2026-09-21T00:00:00Z', reason: 'Finalized' }]),
  getSeparationBenefits: vi.fn().mockResolvedValue({ gratuity: { finalGratuityAmount: 10000, taxableAmount: 0, nonTaxableAmount: 10000 }, finalSettlementStatus: 'Calculated' }),
  getSeparationBenefitHistory: vi.fn().mockResolvedValue([{ id: 'h1', eventType: 'GratuityCalculated', occurredAtUtc: '2026-09-21T00:00:00Z', reason: 'Configured policy' }]),
}))
vi.mock('../../api/separationBenefits.ts', () => api)

describe('Phase 7P separation benefits UI', () => {
  it('renders policy and register evidence', async () => {
    render(<SeparationBenefitsPage />)
    expect(await screen.findByText('Standard')).toBeInTheDocument()
    expect(screen.getByText('Employee One')).toBeInTheDocument()
    expect(screen.getByText('Service months')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'View details' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'View details' }))
    expect(await screen.findByText('Benefit detail — Employee One')).toBeInTheDocument()
    expect(await screen.findByText(/GratuityCalculated/)).toBeInTheDocument()
  })

  it('renders only the linked employee benefit summary', async () => {
    render(<MySeparationBenefitsPage />)
    expect(await screen.findByText('My Separation Benefits')).toBeInTheDocument()
    expect(screen.getByText('Gratuity: 10000.00')).toBeInTheDocument()
    expect(screen.getByText('Status: Finalized')).toBeInTheDocument()
  })
})
