import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { SupervisorSectionForm } from './SupervisorSectionForm.tsx'

vi.mock('../../auth/useAuth.ts', () => ({
  useAuth: () => ({ can: () => true }),
}))

vi.mock('../../api/employeeSubsections.ts', () => ({
  getSupervisor: vi.fn().mockResolvedValue({
    employeeId: 'employee-1',
    l1ManagerId: 'manager-1',
    l1ManagerCode: 'EMP-002',
    l1ManagerName: 'Owen Brand',
    l1ResolutionStatus: 'Resolved',
    l1ResolutionMessage: null,
    l2ManagerId: null,
    createdDate: '2026-03-04T00:00:00Z',
  }),
  getEmploymentHistory: vi.fn().mockResolvedValue([
    {
      id: 'history-1', employeeId: 'employee-1', effectiveFrom: '2026-03-04', effectiveTo: null,
      managerId: 'manager-1', managerCode: 'EMP-002', managerName: 'Owen Brand',
      employmentType: 'FullTime', employmentStatus: 'Active', changeReason: 'NewJoining',
    },
    {
      id: 'history-2', employeeId: 'employee-1', effectiveFrom: '2026-10-01', effectiveTo: null,
      managerId: 'manager-3', managerCode: 'EMP-003', managerName: 'Maya Singh',
      employmentType: 'FullTime', employmentStatus: 'Active', changeReason: 'Transfer',
    },
  ]),
  getSupervisorOptions: vi.fn().mockResolvedValue([]),
  upsertSupervisor: vi.fn(),
}))

describe('SupervisorSectionForm', () => {
  it('shows resolved and scheduled L1 states and keeps L1 read-only', async () => {
    render(<SupervisorSectionForm employeeId="employee-1" onEmploymentChange={vi.fn()} />)

    const current = await screen.findByLabelText('Current direct manager (L1)')
    await waitFor(() => expect(current).toHaveValue('EMP-002 — Owen Brand'))
    expect(current).toHaveAttribute('readonly')
    expect(screen.getByText(/Scheduled L1: EMP-003/)).toBeInTheDocument()
    expect(screen.getByText('Resolved')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Change through Employment' })).toBeInTheDocument()
  })
})
