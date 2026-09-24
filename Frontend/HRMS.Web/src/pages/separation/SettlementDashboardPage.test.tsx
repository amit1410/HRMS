import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { SettlementDashboardPage } from './SettlementDashboardPage.tsx'

const mocks = vi.hoisted(() => ({
  getSettlementDashboard: vi.fn(),
  initiateSettlement: vi.fn(),
  retrySettlement: vi.fn(),
}))

vi.mock('../../api/separation.ts', () => ({
  getSettlementDashboard: mocks.getSettlementDashboard,
  initiateSettlement: mocks.initiateSettlement,
  retrySettlement: mocks.retrySettlement,
}))

describe('SettlementDashboardPage', () => {
  it('renders server-paged readiness, blockers, and completion state', async () => {
    mocks.getSettlementDashboard.mockResolvedValue({
      items: [{
        separationId: 'separation-1',
        employeeId: 'employee-1',
        employeeCode: 'E-001',
        employeeName: 'Settlement Employee',
        approvedLastWorkingDate: '2026-10-02',
        orchestrationStatus: 'NotReady',
        payrollSettlementStatus: null,
        isReady: true,
        blockerCount: 0,
        initiatedAtUtc: null,
        completedAtUtc: null,
        readyForFinalExitClosure: false,
      }],
      totalCount: 1,
      page: 1,
      pageSize: 25,
    })

    render(<SettlementDashboardPage />)

    expect(screen.getByRole('heading', { name: 'Separation settlement readiness' })).toBeInTheDocument()
    await waitFor(() => expect(screen.getByText('Settlement Employee (E-001)')).toBeInTheDocument())
    expect(screen.getByText('Ready')).toBeInTheDocument()
    expect(screen.getByText('Showing 1 of 1.')).toBeInTheDocument()
    expect(mocks.getSettlementDashboard).toHaveBeenCalledWith({ page: 1, pageSize: 25, status: undefined, employeeSearch: undefined })
  })
})
