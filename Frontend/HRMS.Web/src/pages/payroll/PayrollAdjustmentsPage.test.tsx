import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { PayrollAdjustmentsPage } from './PayrollAdjustmentsPage.tsx'

vi.mock('../../hooks/useApiQuery.ts', () => ({ useApiQuery: () => ({ data: { items: [], totalCount: 0 } }) }))
vi.mock('../../api/payrollAdjustments.ts', () => ({ listPayrollAdjustments: vi.fn(), createPayrollAdjustment: vi.fn() }))

describe('PayrollAdjustmentsPage', () => {
  it('renders controlled adjustment form and register', () => {
    render(<PayrollAdjustmentsPage />)
    expect(screen.getByRole('heading', { name: 'Payroll Adjustments' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create draft' })).toBeInTheDocument()
    expect(screen.getByText('Adjustment register')).toBeInTheDocument()
  })
})
