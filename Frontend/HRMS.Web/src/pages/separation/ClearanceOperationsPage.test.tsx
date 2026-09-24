import { fireEvent, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { renderAsUser } from '../../test/renderWith.tsx'
import { ClearanceOperationsPage } from './ClearanceOperationsPage.tsx'
import { MySeparationPage } from './MySeparationPage.tsx'

const apiMocks = vi.hoisted(() => ({
  manager: vi.fn(), functional: vi.fn(), dashboard: vi.fn(), clear: vi.fn(), block: vi.fn(), asset: vi.fn(), current: vi.fn(), notice: vi.fn(), clearance: vi.fn(), history: vi.fn(), reasons: vi.fn(),
}))

vi.mock('../../api/separation.ts', () => ({
  getManagerClearanceInbox: apiMocks.manager, getFunctionalClearanceInbox: apiMocks.functional, getHrClearanceDashboard: apiMocks.dashboard,
  clearClearanceTask: apiMocks.clear, blockClearanceTask: apiMocks.block, updateClearanceAssetReturn: apiMocks.asset,
  getMySeparation: apiMocks.current, getSeparationNotice: apiMocks.notice, getSeparationClearance: apiMocks.clearance, getSeparationHistory: apiMocks.history, listSeparationReasons: apiMocks.reasons,
  createMySeparation: vi.fn(), submitSeparation: vi.fn(), withdrawSeparation: vi.fn(),
}))

const page = { page: 1, pageSize: 25, totalCount: 1, items: [] }
const row = { taskId: 'task-1', clearanceId: 'clearance-1', separationId: 'separation-1', employeeId: 'employee-1', employeeCode: 'EMP-1', employeeName: 'Nadia Farrell', approvedLastWorkingDate: '2026-10-02', taskName: 'Laptop return', category: 'Asset', status: 'Pending', dueDate: '2026-09-30', isOverdue: false, assetRequired: true, assetId: 'asset-1', assetStatus: 'PendingReturn', assetReference: 'LAP-1', assetName: 'Laptop' }

beforeEach(() => {
  vi.clearAllMocks()
  apiMocks.manager.mockResolvedValue({ ...page, items: [row ] })
  apiMocks.functional.mockResolvedValue({ ...page, items: [row] })
  apiMocks.dashboard.mockResolvedValue({ ...page, items: [{ employeeId: 'employee-1', employeeCode: 'EMP-1', employeeName: 'Nadia Farrell', separationId: 'separation-1', approvedLastWorkingDate: '2026-10-02', clearanceId: 'clearance-1', clearanceStatus: 'InProgress', mandatoryTaskCount: 3, mandatoryCompletedCount: 1, pendingCount: 1, blockedCount: 1, pendingAssetCount: 1, overdueCount: 1, readyForExit: false }] })
  apiMocks.clear.mockResolvedValue({}); apiMocks.block.mockResolvedValue({}); apiMocks.asset.mockResolvedValue({})
  apiMocks.current.mockResolvedValue({ id: 'separation-1', employeeId: 'employee-1', separationNumber: 'SEP-1', separationType: 'EmployeeInitiated', reasonId: 'reason-1', reasonName: 'Resignation', initiatedBy: 'Employee', requestDate: '2026-09-01', proposedLastWorkingDate: '2026-10-02', approvedLastWorkingDate: '2026-10-02', status: 'NoticePeriod', createdAtUtc: '2026-09-01T00:00:00Z', waivedNoticeDays: 0, noticeExtensionDays: 0, noticeDisposition: 'None' })
  apiMocks.notice.mockResolvedValue(null); apiMocks.history.mockResolvedValue([]); apiMocks.reasons.mockResolvedValue([]); apiMocks.clearance.mockResolvedValue({ status: 'InProgress', mandatoryResolved: 1, mandatoryTotal: 2, tasks: [{ id: 'task-1', name: 'Laptop return', status: 'Pending', dueDate: '2026-09-30', isOverdue: false }] })
})

describe('Phase 8D clearance operations', () => {
  it('loads manager scope and sends clear action to the backend', async () => {
    renderAsUser(<ClearanceOperationsPage mode="manager" />)
    expect(await screen.findByText('Nadia Farrell')).toBeInTheDocument()
    expect(apiMocks.manager).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 25 }), expect.any(AbortSignal))
    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
    await waitFor(() => expect(apiMocks.clear).toHaveBeenCalledWith('task-1', 'Operational clearance completed'))
  })

  it('loads functional assignment scope and uses the existing asset update endpoint', async () => {
    renderAsUser(<ClearanceOperationsPage mode="functional" />)
    expect(await screen.findByText('Laptop return')).toBeInTheDocument()
    expect(apiMocks.functional).toHaveBeenCalled()
    fireEvent.click(screen.getByRole('button', { name: 'Asset returned' }))
    await waitFor(() => expect(apiMocks.asset).toHaveBeenCalledWith('task-1', 'asset-1', expect.objectContaining({ returnStatus: 'Returned', recoveryRequired: false })))
  })

  it('renders HR blockers, assets, overdue count, readiness, and dashboard paging', async () => {
    apiMocks.dashboard.mockResolvedValue({ ...page, totalCount: 26, items: [{ ...page.items, employeeId: 'employee-1', employeeName: 'Nadia Farrell', clearanceId: 'clearance-1', mandatoryCompletedCount: 1, mandatoryTaskCount: 3, pendingCount: 1, blockedCount: 1, pendingAssetCount: 1, overdueCount: 1, readyForExit: false }] })
    renderAsUser(<ClearanceOperationsPage mode="hr" />)
    expect(await screen.findByText('Nadia Farrell')).toBeInTheDocument()
    expect(screen.getByText('1/3')).toBeInTheDocument()
    expect(screen.getByText('No')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Next' })).not.toBeDisabled()
  })

  it('shows employee clearance progress read-only', async () => {
    renderAsUser(<MySeparationPage />)
    expect(await screen.findByText(/1\/2 mandatory tasks resolved/)).toBeInTheDocument()
    expect(screen.getByText(/Laptop return — Pending/)).toBeInTheDocument()
    expect(apiMocks.clearance).toHaveBeenCalledWith('separation-1', expect.any(AbortSignal))
  })
})
