import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PlatformTenantsPage } from './PlatformTenantsPage.tsx'

const { listPlatformTenants, resetTenantAdminPassword, createPlatformTenant, updatePlatformTenant, retryPlatformTenant, activatePlatformTenant, deactivatePlatformTenant, canPermission } = vi.hoisted(() => ({
  listPlatformTenants: vi.fn(),
  resetTenantAdminPassword: vi.fn(),
  createPlatformTenant: vi.fn(),
  updatePlatformTenant: vi.fn(),
  retryPlatformTenant: vi.fn(),
  activatePlatformTenant: vi.fn(),
  deactivatePlatformTenant: vi.fn(),
  canPermission: vi.fn(() => true),
}))

vi.mock('../../auth/PlatformAuthProvider.tsx', () => ({
  usePlatformAuth: () => ({
    can: canPermission,
    status: 'authenticated',
    user: { permissions: ['PlatformTenant.View', 'PlatformTenant.Create'] },
  }),
}))

vi.mock('../../api/platformTenants.ts', () => ({
  listPlatformTenants,
  resetTenantAdminPassword,
  createPlatformTenant,
  retryPlatformTenant,
  updatePlatformTenant,
  activatePlatformTenant,
  deactivatePlatformTenant,
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

  it('opens tenant actions and keeps provisioning identity read-only in the edit form', async () => {
    listPlatformTenants.mockResolvedValue([{ id: 'tenant-1', tenantCode: 'ANEVRA01', tenantName: 'Anevra Technologies', host: 'anevra01.localhost', databaseProvider: 'MySql', shardKey: 'anevra01', status: 'Active', webUrl: 'http://anevra01.localhost', email: 'contact@anevra.test', phone: '555-0100', address: 'Old address' }])
    render(<PlatformTenantsPage />)

    await screen.findByText('ANEVRA01')
    await userEvent.click(screen.getByText('Actions', { selector: 'summary' }))
    await userEvent.click(screen.getByRole('button', { name: 'Edit' }))

    expect(screen.getByRole('heading', { name: 'Edit Tenant' })).toBeInTheDocument()
    expect(screen.getByLabelText('Tenant Code')).toHaveValue('ANEVRA01')
    expect(screen.getByLabelText('Tenant Code')).toHaveAttribute('readonly')
    expect(screen.getByLabelText('Database Provider')).toHaveAttribute('readonly')
    expect(screen.getByLabelText('Shard Key')).toHaveAttribute('readonly')
    expect(screen.getByText('Database provider and shard key cannot be changed after provisioning.')).toBeInTheDocument()
  })

  it('opens Inspect Tenant as a top-level dialog and closes it without removing the tenant list', async () => {
    render(<PlatformTenantsPage />)

    await screen.findByText('ANEVRA01')
    await userEvent.click(screen.getByText('Actions', { selector: 'summary' }))
    await userEvent.click(screen.getByRole('button', { name: 'View / Inspect' }))

    const dialog = screen.getByRole('dialog', { name: 'Inspect Tenant' })
    expect(dialog).toHaveClass('platform-inspect-overlay')
    expect(dialog).toHaveTextContent('anevra01.localhost')
    expect(dialog).toHaveTextContent('MySQL')
    expect(screen.getByText('Actions', { selector: 'summary' }).parentElement).not.toHaveAttribute('open')

    await userEvent.click(screen.getByRole('button', { name: 'Close Inspect Tenant' }))
    expect(screen.queryByRole('dialog', { name: 'Inspect Tenant' })).not.toBeInTheDocument()
    expect(screen.getByText('ANEVRA01')).toBeInTheDocument()
  })

  it('opens Edit Tenant as a drawer and keeps only one major overlay open', async () => {
    render(<PlatformTenantsPage />)

    await screen.findByText('ANEVRA01')
    await userEvent.click(screen.getByText('Actions', { selector: 'summary' }))
    await userEvent.click(screen.getByRole('button', { name: 'View / Inspect' }))
    expect(screen.getByRole('dialog', { name: 'Inspect Tenant' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Close Inspect Tenant' }))
    await userEvent.click(screen.getByText('Actions', { selector: 'summary' }))
    await userEvent.click(screen.getByRole('button', { name: 'Edit' }))

    const drawer = screen.getByRole('dialog', { name: 'Edit Tenant' })
    expect(drawer).toHaveClass('platform-edit-drawer')
    expect(drawer).toHaveTextContent('Update safe tenant metadata')
    expect(screen.queryByRole('dialog', { name: 'Inspect Tenant' })).not.toBeInTheDocument()
  })

  it('saves only editable tenant metadata', async () => {
    const tenant = { id: 'tenant-1', tenantCode: 'ANEVRA01', tenantName: 'Anevra Technologies', host: 'anevra01.localhost', databaseProvider: 'MySql' as const, shardKey: 'anevra01', status: 'Active' as const, webUrl: 'http://anevra01.localhost' }
    listPlatformTenants.mockResolvedValue([tenant])
    updatePlatformTenant.mockResolvedValue({ ...tenant, tenantName: 'Updated Tenant', email: 'new@anevra.test' })
    render(<PlatformTenantsPage />)

    await screen.findByText('ANEVRA01')
    await userEvent.click(screen.getByText('Actions', { selector: 'summary' }))
    await userEvent.click(screen.getByRole('button', { name: 'Edit' }))
    const editTenantName = screen.getAllByLabelText('Tenant Name *')[1]!
    await userEvent.clear(editTenantName)
    await userEvent.type(editTenantName, 'Updated Tenant')
    await userEvent.type(screen.getAllByLabelText('Contact Email')[1]!, 'new@anevra.test')
    await userEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    await waitFor(() => expect(updatePlatformTenant).toHaveBeenCalledWith('tenant-1', expect.objectContaining({ tenantName: 'Updated Tenant', host: 'anevra01.localhost', email: 'new@anevra.test' })))
    expect(updatePlatformTenant.mock.calls[0]?.[1]).not.toHaveProperty('databaseProvider')
    expect(updatePlatformTenant.mock.calls[0]?.[1]).not.toHaveProperty('shardKey')
  })

  it('confirms deactivation and updates the row status and summary', async () => {
    const active = { id: 'tenant-1', tenantCode: 'ANEVRA01', tenantName: 'Anevra Technologies', host: 'anevra01.localhost', databaseProvider: 'MySql' as const, shardKey: 'anevra01', status: 'Active' as const, webUrl: 'http://anevra01.localhost' }
    const inactive = { ...active, status: 'Inactive' as const }
    listPlatformTenants.mockResolvedValue([active])
    deactivatePlatformTenant.mockResolvedValue(inactive)
    render(<PlatformTenantsPage />)

    await screen.findByText('ANEVRA01')
    await userEvent.click(screen.getByText('Actions', { selector: 'summary' }))
    await userEvent.click(screen.getByRole('button', { name: 'Deactivate' }))
    expect(screen.getByRole('heading', { name: 'Deactivate Anevra Technologies?' })).toBeInTheDocument()
    expect(screen.getByText(/tenant data will not be deleted/i)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Deactivate Tenant' }))

    await waitFor(() => expect(deactivatePlatformTenant).toHaveBeenCalledWith('tenant-1'))
    await waitFor(() => expect(document.querySelector('tbody .platform-status-pill')?.textContent).toContain('Inactive'))
    expect(screen.getByText('Active Tenants').parentElement).toHaveTextContent('0')
  })

  it('edits an inactive tenant and keeps provisioning fields read-only', async () => {
    const tenant = { id: 'tenant-1', tenantCode: 'ANEVRA01', tenantName: 'Anevra Technologies', host: 'anevra01.localhost', databaseProvider: 'MySql' as const, shardKey: 'anevra01', status: 'Inactive' as const, webUrl: 'http://anevra01.localhost', email: 'old@anevra.test', phone: '555-0100', address: 'Old address' }
    updatePlatformTenant.mockResolvedValue({ ...tenant, tenantName: 'Updated Tenant', email: 'new@anevra.test', phone: '555-0111', address: 'New address' })
    listPlatformTenants.mockResolvedValue([tenant])
    render(<PlatformTenantsPage />)

    await screen.findByText('ANEVRA01')
    await userEvent.click(screen.getByText('Actions', { selector: 'summary' }))
    await userEvent.click(screen.getByRole('button', { name: 'Edit' }))
    await userEvent.clear(screen.getAllByLabelText('Tenant Name *')[1]!)
    await userEvent.type(screen.getAllByLabelText('Tenant Name *')[1]!, 'Updated Tenant')
    await userEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    await waitFor(() => expect(updatePlatformTenant).toHaveBeenCalledWith('tenant-1', expect.objectContaining({ tenantName: 'Updated Tenant', email: 'old@anevra.test', phone: '555-0100', address: 'Old address' })))
    expect(screen.queryByRole('heading', { name: 'Edit Tenant' })).not.toBeInTheDocument()
  })

  it('keeps edit errors visible in the modal', async () => {
    const tenant = { id: 'tenant-1', tenantCode: 'ANEVRA01', tenantName: 'Anevra Technologies', host: 'anevra01.localhost', databaseProvider: 'MySql' as const, shardKey: 'anevra01', status: 'Active' as const, webUrl: 'http://anevra01.localhost' }
    listPlatformTenants.mockResolvedValue([tenant])
    updatePlatformTenant.mockRejectedValue(new Error('Host is already in use.'))
    render(<PlatformTenantsPage />)

    await screen.findByText('ANEVRA01')
    await userEvent.click(screen.getByText('Actions', { selector: 'summary' }))
    await userEvent.click(screen.getByRole('button', { name: 'Edit' }))
    await userEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Host is already in use.')
    expect(screen.getByRole('heading', { name: 'Edit Tenant' })).toBeInTheDocument()
  })

  it('offers Activate for inactive tenants and preserves the Development reset action', async () => {
    const inactive = { id: 'tenant-1', tenantCode: 'ANEVRA01', tenantName: 'Anevra Technologies', host: 'anevra01.localhost', databaseProvider: 'MySql' as const, shardKey: 'anevra01', status: 'Inactive' as const, webUrl: 'http://anevra01.localhost' }
    const active = { ...inactive, status: 'Active' as const }
    listPlatformTenants.mockResolvedValue([inactive])
    activatePlatformTenant.mockResolvedValue(active)
    render(<PlatformTenantsPage />)

    await screen.findByText('ANEVRA01')
    await userEvent.click(screen.getByText('Actions', { selector: 'summary' }))
    expect(screen.getByRole('button', { name: 'Activate' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reset Tenant Admin Password' })).not.toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Activate' }))
    expect(screen.getByRole('heading', { name: 'Activate Anevra Technologies?' })).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Activate Tenant' }))
    await waitFor(() => expect(activatePlatformTenant).toHaveBeenCalledWith('tenant-1'))
    await waitFor(() => expect(document.querySelector('tbody .platform-status-pill')?.textContent).toContain('Active'))
  })

  it('calculates summary cards from the loaded tenant list', async () => {
    listPlatformTenants.mockResolvedValue([
      { id: 'tenant-1', tenantCode: 'ANEVRA01', tenantName: 'Anevra Technologies', host: 'anevra01.localhost', databaseProvider: 'MySql', shardKey: 'anevra01', status: 'Active', webUrl: 'http://anevra01.localhost' },
      { id: 'tenant-2', tenantCode: 'ACME02', tenantName: 'Acme Corp', host: 'acme.localhost', databaseProvider: 'SqlServer', shardKey: 'acme', status: 'Inactive', webUrl: 'http://acme.localhost' },
      { id: 'tenant-3', tenantCode: 'BETA03', tenantName: 'Beta Ltd', host: 'beta.localhost', databaseProvider: 'MySql', shardKey: 'beta', status: 'Suspended', webUrl: 'http://beta.localhost' },
    ])
    render(<PlatformTenantsPage />)

    expect(await screen.findByText('ANEVRA01')).toBeInTheDocument()
    expect(screen.getByText('Total Tenants').parentElement).toHaveTextContent('3')
    expect(screen.getByText('Active Tenants').parentElement).toHaveTextContent('1')
    expect(screen.getByText('MySQL Tenants').parentElement).toHaveTextContent('2')
    expect(screen.getByText('Pending / Inactive').parentElement).toHaveTextContent('2')
    expect(screen.getByText('Active', { selector: '.platform-status-pill' })).toBeInTheDocument()
  })

  it('renders the create form with required administrator fields and provider preview', async () => {
    render(<PlatformTenantsPage />)

    expect(await screen.findByRole('heading', { name: 'Create tenant' })).toBeInTheDocument()
    expect(screen.getByLabelText('Tenant Name *')).toBeRequired()
    expect(screen.getByLabelText('Tenant Code *')).toBeRequired()
    expect(screen.getByLabelText('Database Provider *')).toHaveValue('MySql')
    expect(screen.getByLabelText('First Name *')).toBeRequired()
    expect(screen.getByLabelText('Last Name *')).toBeRequired()
    expect(screen.getByLabelText('Email *')).toBeRequired()
    expect(screen.getByText('HRMS_<shard-key>')).toBeInTheDocument()
    expect(screen.queryByLabelText(/password/i)).not.toBeInTheDocument()
  })

  it('updates the database preview and submits the existing normalized payload', async () => {
    createPlatformTenant.mockResolvedValue({ id: 'tenant-new', tenantCode: 'NEW01', tenantName: 'New Tenant', host: 'new.localhost', databaseProvider: 'MySql', shardKey: 'newkey', status: 'Active', webUrl: 'http://new.localhost' })
    render(<PlatformTenantsPage />)

    await userEvent.type(await screen.findByLabelText('Tenant Name *'), 'New Tenant')
    await userEvent.type(screen.getByLabelText('Tenant Code *'), 'new01')
    await userEvent.type(screen.getByLabelText('Host *'), 'New.Localhost')
    await userEvent.selectOptions(screen.getByLabelText('Database Provider *'), 'MySql')
    await userEvent.type(screen.getByLabelText('Shard Key *'), 'NewKey')
    expect(screen.getByText('HRMS_newkey')).toBeInTheDocument()
    await userEvent.type(screen.getByLabelText('First Name *'), 'Ada')
    await userEvent.type(screen.getByLabelText('Last Name *'), 'Lovelace')
    await userEvent.type(screen.getByLabelText('Email *'), 'ada@new.test')
    await userEvent.click(screen.getByRole('button', { name: 'Create Tenant' }))

    await waitFor(() => expect(createPlatformTenant).toHaveBeenCalledWith(expect.objectContaining({ tenantName: 'New Tenant', tenantCode: 'NEW01', host: 'new.localhost', shardKey: 'newkey', databaseProvider: 'MySql', firstName: 'Ada', lastName: 'Lovelace', initialAdminEmail: 'ada@new.test' })))
  })

  it('filters tenant rows by search and status without changing the API request', async () => {
    listPlatformTenants.mockResolvedValue([
      { id: 'tenant-1', tenantCode: 'ANEVRA01', tenantName: 'Anevra Technologies', host: 'anevra01.localhost', databaseProvider: 'MySql', shardKey: 'anevra01', status: 'Active', webUrl: 'http://anevra01.localhost' },
      { id: 'tenant-2', tenantCode: 'ACME02', tenantName: 'Acme Corp', host: 'acme.localhost', databaseProvider: 'SqlServer', shardKey: 'acme', status: 'Inactive', webUrl: 'http://acme.localhost' },
    ])
    render(<PlatformTenantsPage />)

    await screen.findByText('ACME02')
    await userEvent.type(screen.getByPlaceholderText('Search tenants'), 'acme')
    expect(screen.getByText('ACME02')).toBeInTheDocument()
    expect(screen.queryByText('ANEVRA01')).not.toBeInTheDocument()
    await userEvent.clear(screen.getByPlaceholderText('Search tenants'))
    await userEvent.selectOptions(screen.getByLabelText('Filter tenant status'), 'Active')
    expect(screen.getByText('ANEVRA01')).toBeInTheDocument()
    expect(screen.queryByText('ACME02')).not.toBeInTheDocument()
    expect(listPlatformTenants).toHaveBeenCalled()
  })
})
