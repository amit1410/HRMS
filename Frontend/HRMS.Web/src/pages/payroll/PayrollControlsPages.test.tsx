import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { PayrollConfigurationHealthPage } from './PayrollConfigurationHealthPage.tsx'
import { PayrollOperationsDashboardPage } from './PayrollOperationsDashboardPage.tsx'

vi.mock('../../api/payroll.ts', () => ({
  getPayrollConfigurationHealth: vi.fn().mockResolvedValue({ categories: [{ category: 'Controls', status: 'Healthy', issueCount: 0, blockingCount: 0, issues: [] }] }),
  getPayrollOperationsDashboard: vi.fn().mockResolvedValue({ openPeriods: 2, lockedPeriods: 1, runsWithReadinessErrors: 0, runsAwaitingCalculation: 1, runsAwaitingApproval: 1, unpublishedPayslips: 0, bankAdviceAwaitingApproval: 0, bankAdviceAwaitingExport: 0, accountingAwaitingApproval: 0, accountingAwaitingPosting: 0, statutoryReturnsAwaitingValidation: 0, statutoryReturnsAwaitingFiling: 0, retroCasesPending: 0, finalSettlementsPending: 0 }),
}))

describe('Payroll controls pages', () => {
  it('renders configuration health categories', async () => { render(<PayrollConfigurationHealthPage />); expect(await screen.findByText('Payroll Configuration Health')).toBeInTheDocument(); expect(await screen.findByText('Controls')).toBeInTheDocument() })
  it('renders operational lifecycle counts', async () => { render(<PayrollOperationsDashboardPage />); expect(await screen.findByText('Payroll Operations')).toBeInTheDocument(); expect(await screen.findByText('2')).toBeInTheDocument() })
})
