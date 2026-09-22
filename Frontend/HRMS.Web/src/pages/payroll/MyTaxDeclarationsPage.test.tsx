import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const useApiQuery = vi.hoisted(() => vi.fn())
const apiActions = vi.hoisted(() => ({ update: vi.fn(), remove: vi.fn(), replace: vi.fn(), resubmit: vi.fn() }))
vi.mock('../../hooks/useApiQuery.ts', () => ({ useApiQuery }))
vi.mock('../../api/taxDeclarations.ts', () => ({ getMyTaxDeclaration: vi.fn(), updateMyTaxDeclarationLine: apiActions.update, deleteMyTaxDeclarationLine: apiActions.remove, replaceMyTaxDeclarationProof: apiActions.replace, resubmitMyTaxDeclaration: apiActions.resubmit }))

import { MyTaxDeclarationsPage } from './MyTaxDeclarationsPage.tsx'

describe('MyTaxDeclarationsPage', () => {
  beforeEach(() => vi.clearAllMocks())

  it('renders declared, approved, proof, and reviewer state', () => {
    useApiQuery.mockReturnValue({ data: { cycleCode: 'FY2026', financialYear: 2026, status: 'PartiallyApproved', lines: [{ id: 'line-1', categoryCode: 'INV', itemCode: 'PF', declaredAmount: 100000, approvedAmount: 75000, status: 'PartiallyApproved', reviewerComment: 'Accepted in part', proofs: [{ id: 'proof-1' }] }] } })
    render(<MyTaxDeclarationsPage />)
    expect(screen.getByText('FY2026 · FY 2026')).toBeInTheDocument()
    expect(screen.getAllByText('PartiallyApproved')).toHaveLength(2)
    expect(screen.getByText('100000.00')).toBeInTheDocument()
    expect(screen.getByText('75000.00')).toBeInTheDocument()
    expect(screen.getByText('Accepted in part')).toBeInTheDocument()
  })

  it('renders a safe empty state when no active declaration exists', () => {
    useApiQuery.mockReturnValue({ data: undefined })
    render(<MyTaxDeclarationsPage />)
    expect(screen.getByText('No active declaration was found.')).toBeInTheDocument()
  })

  it('exposes draft edit/delete controls but keeps approved amount read-only', async () => {
    const refetch = vi.fn()
    apiActions.update.mockResolvedValue({})
    apiActions.remove.mockResolvedValue({})
    useApiQuery.mockReturnValue({ refetch, data: { id: 'declaration-1', cycleCode: 'FY2026', financialYear: 2026, status: 'Draft', lines: [{ id: 'line-1', categoryCode: 'INV', itemCode: 'PF', declaredAmount: 100000, approvedAmount: 0, status: 'Draft', proofs: [] }] } })
    render(<MyTaxDeclarationsPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }))
    fireEvent.change(screen.getByLabelText('Declared amount PF'), { target: { value: '90000' } })
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    expect(apiActions.update).toHaveBeenCalledWith('declaration-1', 'line-1', expect.objectContaining({ declaredAmount: 90000 }))
    expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument()
    expect(screen.getByText('0.00')).toBeInTheDocument()
  })

  it('exposes rejected-proof replacement and explicit resubmission', async () => {
    const refetch = vi.fn()
    apiActions.replace.mockResolvedValue({})
    apiActions.resubmit.mockResolvedValue({})
    useApiQuery.mockReturnValue({ refetch, data: { id: 'declaration-2', cycleCode: 'FY2026', financialYear: 2026, status: 'ResubmissionRequired', lines: [{ id: 'line-2', categoryCode: 'INV', itemCode: 'PF', declaredAmount: 100000, approvedAmount: 0, status: 'Rejected', reviewerComment: 'Use a clearer proof', proofs: [{ id: 'proof-2', fileName: 'old.pdf', status: 'Rejected', reviewerComment: 'Unreadable' }] }] } })
    render(<MyTaxDeclarationsPage />)
    expect(screen.getByRole('button', { name: 'Resubmit declaration' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Replace proof' }))
    expect(apiActions.replace).toHaveBeenCalledWith('declaration-2', 'line-2', 'proof-2', expect.objectContaining({ lineId: 'line-2' }))
    fireEvent.click(screen.getByRole('button', { name: 'Resubmit declaration' }))
    expect(apiActions.resubmit).toHaveBeenCalledWith('declaration-2')
  })
})
