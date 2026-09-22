import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { PayrollOffCyclePage } from './PayrollOffCyclePage.tsx'

vi.mock('../../hooks/useApiQuery.ts', () => ({ useApiQuery: () => ({ data: { items: [], totalCount: 0 } }) }))
vi.mock('../../api/payrollAdjustments.ts', () => ({ listOffCycleRuns: vi.fn(), prepareOffCycleRun: vi.fn(), approveOffCycleRun: vi.fn(), processOffCycleRun: vi.fn() }))

describe('PayrollOffCyclePage', () => {
  it('renders the bounded off-cycle run register', () => {
    render(<PayrollOffCyclePage />)
    expect(screen.getByRole('heading', { name: 'Off-Cycle Payroll' })).toBeInTheDocument()
    expect(screen.getByText('Off-cycle run register')).toBeInTheDocument()
  })
})
