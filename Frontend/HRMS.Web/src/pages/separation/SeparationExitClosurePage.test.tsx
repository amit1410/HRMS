import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { SeparationExitClosurePage } from './SeparationExitClosurePage'

vi.mock('../../api/separationExit', () => ({
  getSeparationExitDashboard: vi.fn(async () => ({ items: [{ separationId: 's1', employeeId: 'e1', employeeCode: 'E001', employeeName: 'Exit Employee', approvedLastWorkingDate: '2026-09-24', separationStatus: 'ReadyForExit', executionStatus: 'NotStarted', employmentStatus: 'Active', isReady: true, blockerCount: 0 }], page: 1, pageSize: 25, totalCount: 1 })),
  executeSeparationExit: vi.fn(),
  retrySeparationExit: vi.fn(),
}))

describe('SeparationExitClosurePage', () => {
  beforeEach(() => vi.clearAllMocks())
  it('shows final LWD and execute action without an editable exit date', async () => {
    render(<SeparationExitClosurePage />)
    expect(await screen.findByRole('heading', { name: 'Separation exit closure' })).toBeInTheDocument()
    expect(screen.getByText('2026-09-24')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Execute Exit' })).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByLabelText(/date of leaving/i)).not.toBeInTheDocument())
  })
})
