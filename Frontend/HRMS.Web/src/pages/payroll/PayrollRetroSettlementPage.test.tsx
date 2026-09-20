import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { PayrollRetroPage } from './PayrollRetroPage.tsx'
import { FinalSettlementPage } from './FinalSettlementPage.tsx'

vi.mock('../../api/payroll.ts', () => ({
  createPayrollRetroCase: vi.fn(),
  evaluatePayrollRetroCase: vi.fn(),
  addFinalSettlementLine: vi.fn(),
  calculateFinalSettlement: vi.fn(),
  createFinalSettlement: vi.fn(),
}))

describe('Payroll retro and final settlement pages', () => {
  it('renders the retro case foundation', () => {
    render(<PayrollRetroPage />)
    expect(screen.getByRole('heading', { name: 'Retro / Arrears' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create case' })).toBeDisabled()
  })

  it('renders the final settlement foundation', () => {
    render(<FinalSettlementPage />)
    expect(screen.getByRole('heading', { name: 'Final Settlement' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create settlement' })).toBeDisabled()
  })
})
