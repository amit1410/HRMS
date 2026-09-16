import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { LeaveBalanceImportPage } from './LeaveBalanceImportPage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser } from '../../test/fixtures.ts'
import { fail, installStubAdapter, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'

describe('LeaveBalanceImportPage template download', () => {
  let stub: StubAdapter
  const permissions = [Permissions.leave.balanceImport, Permissions.leave.balanceViewImportHistory]

  beforeEach(() => {
    stub = installStubAdapter()
    stub.on('get', '/api/leave-balances/import/history', () => ({ data: { success: true, message: 'OK', data: [] } }))
  })

  afterEach(() => {
    stub.restore()
  })

  it('requests the tenant-aware CSV endpoint as a blob and starts a download', async () => {
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
    const originalCreate = Object.getOwnPropertyDescriptor(URL, 'createObjectURL')
    const originalRevoke = Object.getOwnPropertyDescriptor(URL, 'revokeObjectURL')
    const createObjectURL = vi.fn(() => 'blob:leave-template')
    const revokeObjectURL = vi.fn()
    Object.defineProperty(URL, 'createObjectURL', { value: createObjectURL, configurable: true })
    Object.defineProperty(URL, 'revokeObjectURL', { value: revokeObjectURL, configurable: true })
    stub.on('get', '/api/leave-balances/import/template', () => ({ data: new Blob(['EmployeeCode,LeaveTypeCode']) }))
    try {
      renderAsUser(<LeaveBalanceImportPage />, { user: makeUser({ permissions }) })

      await userEvent.click(screen.getByRole('button', { name: 'Download Template' }))

      expect(stub.callsTo('get', '/api/leave-balances/import/template')).toHaveLength(1)
      expect(stub.callsTo('get', '/api/leave-balances/import/template')[0]?.responseType).toBe('blob')
      expect(click).toHaveBeenCalledOnce()
      expect(createObjectURL).toHaveBeenCalledOnce()
    } finally {
      click.mockRestore()
      if (originalCreate) Object.defineProperty(URL, 'createObjectURL', originalCreate)
      else Reflect.deleteProperty(URL, 'createObjectURL')
      if (originalRevoke) Object.defineProperty(URL, 'revokeObjectURL', originalRevoke)
      else Reflect.deleteProperty(URL, 'revokeObjectURL')
    }
  })

  it('shows a useful error when the template request fails', async () => {
    stub.on('get', '/api/leave-balances/import/template', () => ({ status: 403, data: fail('Forbidden') }))
    renderAsUser(<LeaveBalanceImportPage />, { user: makeUser({ permissions }) })

    await userEvent.click(screen.getByRole('button', { name: 'Download Template' }))

    expect(await screen.findByText('Forbidden')).toBeInTheDocument()
  })
})
