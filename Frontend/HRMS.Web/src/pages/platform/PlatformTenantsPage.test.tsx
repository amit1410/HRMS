import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PlatformTenantsPage } from './PlatformTenantsPage.tsx'

const { listPlatformTenants, resetTenantAdminPassword } = vi.hoisted(() => ({
  listPlatformTenants: vi.fn(),
  resetTenantAdminPassword: vi.fn(),
}))

vi.mock('../../auth/PlatformAuthProvider.tsx', () => ({
  usePlatformAuth: () => ({
    can: () => true,
    status: 'authenticated',
    user: { permissions: ['PlatformTenant.View', 'PlatformTenant.Create'] },
  }),
}))

vi.mock('../../api/platformTenants.ts', () => ({
  listPlatformTenants,
  resetTenantAdminPassword,
  createPlatformTenant: vi.fn(),
  retryPlatformTenant: vi.fn(),
  updateInactivePlatformTenant: vi.fn(),
}))

describe('PlatformTenantsPage Development password reset', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    listPlatformTenants.mockResolvedValue([{
      id: 'tenant-1', tenantCode: 'ANEVRA01', tenantName: 'Anevra Technologies',
      host: 'anevra01.localhost', databaseProvider: 'MySql', shardKey: 'anevra01',
      status: 'Active', webUrl: 'http://anevra01.localhost',
    }])
    resetTenantAdminPassword.mockResolvedValue({
      tenantId: 'tenant-1', tenantCode: 'ANEVRA01', tenantName: 'Anevra Technologies',
      adminEmail: 'admin@anevra.test', temporaryPassword: 'temporary-only-value',
      message: 'Temporary password generated. Copy it now; it will not be shown again.',
    })
  })

  it('requires confirmation before resetting', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false)
    render(<PlatformTenantsPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Reset Tenant Admin Password' }))
    fireEvent.click(screen.getByRole('button', { name: 'Confirm reset' }))

    expect(confirm).toHaveBeenCalled()
    expect(resetTenantAdminPassword).not.toHaveBeenCalled()
    confirm.mockRestore()
  })

  it('shows the returned password only in memory until the modal closes', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<PlatformTenantsPage />)

    fireEvent.click(await screen.findByRole('button', { name: 'Reset Tenant Admin Password' }))
    fireEvent.click(screen.getByRole('button', { name: 'Confirm reset' }))

    expect(await screen.findByText('temporary-only-value')).toBeInTheDocument()
    expect(screen.getByText('admin@anevra.test')).toBeInTheDocument()
    expect(window.localStorage.length).toBe(0)
    await waitFor(() => expect(resetTenantAdminPassword).toHaveBeenCalledWith('tenant-1', undefined))

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByText('temporary-only-value')).not.toBeInTheDocument()
    vi.restoreAllMocks()
  })
})
