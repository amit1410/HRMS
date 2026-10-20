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
  getEmploymentHistory: vi.fn().mockResolvedValue([]),
  getSupervisorOptions: vi.fn().mockResolvedValue([]),
  upsertSupervisor: vi.fn(),
}))

describe('SupervisorSectionForm', () => {
  it('renders L1 alongside the other editable supervisor roles', async () => {
    render(<SupervisorSectionForm employeeId="employee-1" onEmploymentChange={vi.fn()} />)

    const l1 = await screen.findByLabelText('L1 manager')
    await waitFor(() => expect(l1).toBeInTheDocument())
    expect(l1).not.toHaveAttribute('readonly')
    expect(screen.getByLabelText('L2 manager')).toBeInTheDocument()
    expect(screen.getByLabelText('L3 manager')).toBeInTheDocument()
    expect(screen.getByText('Resolved')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Change through Employment' })).not.toBeInTheDocument()
  })
})
