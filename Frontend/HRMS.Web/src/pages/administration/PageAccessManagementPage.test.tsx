import { fireEvent, screen, waitFor } from '@testing-library/react'
import { Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { Permissions } from '../../auth/permissions.ts'
import { RequirePermission } from '../../auth/RequirePermission.tsx'
import { renderAsUser } from '../../test/renderWith.tsx'
import { makeUser } from '../../test/fixtures.ts'
import { PageAccessManagementPage } from './PageAccessManagementPage.tsx'

const api = vi.hoisted(() => ({
  roles: [
    { id: 1, name: 'Employee', description: 'System role' },
    { id: 2, name: 'HRBP', description: 'Human resources business partner' },
  ],
  matrices: new Map(),
  listRoles: vi.fn(),
  getMatrix: vi.fn(),
  update: vi.fn(),
  searchUsers: vi.fn(),
  preview: vi.fn(),
  history: vi.fn(),
}))

const employeeMatrix = {
  role: api.roles[0],
  grantedPermissions: [Permissions.pageAccess.view],
  pages: [{
    code: 'page-access',
    name: 'Page Access Management',
    moduleCode: 'Administration',
    route: '/page-access-management',
    requiredPermissions: [Permissions.pageAccess.view],
    canView: true,
    actions: [{ code: 'manage', name: 'Manage', permission: Permissions.pageAccess.manage, allowed: false }],
  }],
}

const hrbpMatrix = {
  ...employeeMatrix,
  role: api.roles[1],
  grantedPermissions: [Permissions.pageAccess.view, Permissions.pageAccess.manage],
  pages: [{ ...employeeMatrix.pages[0], actions: [{ ...employeeMatrix.pages[0]!.actions[0]!, allowed: true }] }],
}

vi.mock('../../api/pageAccess.ts', () => ({
  listPageAccessRoles: api.listRoles,
  getPageAccessMatrix: api.getMatrix,
  updatePageAccess: api.update,
  getPageAccessHistory: api.history,
}))

vi.mock('../../api/pageAccessPreview.ts', () => ({
  searchAccessPreviewUsers: api.searchUsers,
  getUserAccessPreview: api.preview,
}))

describe('PageAccessManagementPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.listRoles.mockResolvedValue(api.roles)
    api.getMatrix.mockImplementation(async (roleId: number) => roleId === 2 ? hrbpMatrix : employeeMatrix)
    api.update.mockImplementation(async (_roleId: number, permissions: string[]) => ({ ...hrbpMatrix, grantedPermissions: permissions }))
    api.searchUsers.mockResolvedValue([{ userId: 'user-2', displayName: 'Rahul Mehta', email: 'rahul@example.test' }])
    api.preview.mockResolvedValue({
      userId: 'user-2',
      employeeId: 'employee-2',
      roles: [{ roleId: 2, roleName: 'HRBP', assignmentSource: 'Direct', scopeSummary: 'Tenant-wide' }],
      permissions: [Permissions.pageAccess.view, Permissions.pageAccess.manage],
      pages: [{ code: 'page-access', name: 'Page Access Management', route: '/page-access-management', moduleCode: 'Administration', children: [] }],
      hasManagerAccess: true,
      tenantWideRoleCount: 1,
    })
    api.history.mockResolvedValue({ items: [{ id: 'event-1', occurredAtUtc: '2026-09-17T10:00:00Z', eventType: 'RolePermissionGranted', action: 'Granted', roleId: 2, roleName: 'HRBP', permissionCode: Permissions.pageAccess.manage, oldValue: null, newValue: 'Granted', actorUserId: 'actor-1', actorDisplayName: 'Admin User', reason: 'Phase 2' }], page: 1, pageSize: 25, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false })
  })

  it('renders the authorized page and loads the role matrix', async () => {
    renderAsUser(<PageAccessManagementPage />, { user: makeUser({ permissions: [Permissions.pageAccess.view, Permissions.pageAccess.manage] }) })

    expect(screen.getByRole('heading', { name: 'Page Access Management' })).toBeInTheDocument()
    expect(await screen.findByText('/page-access-management')).toBeInTheDocument()
    expect(api.listRoles).toHaveBeenCalledTimes(1)
    expect(api.getMatrix).toHaveBeenCalledWith(1)
  })

  it('changes roles, toggles actions, and saves the expected permission codes', async () => {
    renderAsUser(<PageAccessManagementPage />, { user: makeUser({ permissions: [Permissions.pageAccess.view, Permissions.pageAccess.manage] }) })
    await screen.findByText('/page-access-management')

    fireEvent.change(screen.getByRole('combobox', { name: 'Role' }), { target: { value: '2' } })
    await waitFor(() => expect(api.getMatrix).toHaveBeenLastCalledWith(2))

    const manage = screen.getByRole('checkbox', { name: 'Manage' })
    fireEvent.click(manage)
    fireEvent.click(screen.getByRole('button', { name: 'Save Configuration' }))

    await waitFor(() => expect(api.update).toHaveBeenCalledWith(2, expect.arrayContaining([Permissions.pageAccess.view])))
    expect(api.update.mock.calls[0]![1]).not.toContain(Permissions.pageAccess.manage)
    expect(screen.getByRole('status')).toHaveTextContent('Page access saved.')
  })

  it('previews a selected user with effective roles, permissions, pages, scopes, and manager access', async () => {
    renderAsUser(<PageAccessManagementPage />, { user: makeUser({ permissions: [Permissions.pageAccess.view] }) })
    await screen.findByText('/page-access-management')

    fireEvent.change(screen.getByPlaceholderText('Search user'), { target: { value: 'Rahul' } })
    await waitFor(() => expect(api.searchUsers).toHaveBeenCalledWith('Rahul'))
    fireEvent.change(screen.getAllByRole('combobox').at(-1)!, { target: { value: 'user-2' } })

    expect(await screen.findByText('HRBP')).toBeInTheDocument()
    expect(screen.getByText('Permissions:').parentElement).toHaveTextContent(Permissions.pageAccess.manage)
    expect(screen.getByText('Tenant-wide grants:').parentElement).toHaveTextContent('1')
    expect(screen.getByText('Manager access:').parentElement).toHaveTextContent('Yes')
    expect(screen.getByText('Accessible pages:').parentElement).toHaveTextContent('Page Access Management')
    expect(api.preview).toHaveBeenCalledWith('user-2')
  })

  it('denies a direct route visit without PageAccess.View', () => {
    renderAsUser(
      <Routes>
        <Route path="/forbidden" element={<p>no access</p>} />
        <Route path="/page-access-management" element={<RequirePermission permission={Permissions.pageAccess.view}><PageAccessManagementPage /></RequirePermission>} />
      </Routes>,
      { route: '/page-access-management', user: makeUser({ permissions: [] }) },
    )

    expect(screen.getByText('no access')).toBeInTheDocument()
    expect(api.listRoles).not.toHaveBeenCalled()
  })

  it('loads paged authorization history with readable permission changes', async () => {
    renderAsUser(<PageAccessManagementPage />, { user: makeUser({ permissions: [Permissions.pageAccess.view] }) })
    await screen.findByText('/page-access-management')
    fireEvent.click(screen.getByRole('button', { name: 'History' }))
    expect(await screen.findByText('RolePermissionGranted')).toBeInTheDocument()
    expect(screen.getByText(Permissions.pageAccess.manage)).toBeInTheDocument()
    expect(api.history).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 25 }))
  })
})
