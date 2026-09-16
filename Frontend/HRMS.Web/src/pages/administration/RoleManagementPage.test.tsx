import { fireEvent, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { renderAsUser } from '../../test/renderWith.tsx'
import { RoleManagementPage } from './RoleManagementPage.tsx'

const api = vi.hoisted(() => ({
  users: [{ userId: 'user-1', displayName: 'Rahul Mehta', employeeCode: 'EMP-004' }],
  roles: [
    { id: 1, name: 'Employee', description: 'System role' },
    { id: 2, name: 'Manager', description: 'System role' },
    { id: 3, name: 'HRBP', description: 'Human resources business partner' },
    { id: 4, name: 'IT', description: 'IT role' },
  ],
  assignments: [{ assignmentId: 'assignment-1', userId: 'user-1', employeeId: 'employee-1', employeeCode: 'EMP-004', employeeName: 'Rahul Mehta', roleId: 1, roleName: 'Employee', assignmentSource: 'System', effectiveFrom: '2026-01-01', effectiveTo: null, isCurrentlyEffective: true, reason: null, assignedByUserId: null, scopes: [], isSystemManaged: true, canRevoke: false, status: 'Active', isRevoked: false, scopeSummary: 'Tenant-wide' }],
  listAssignments: vi.fn(async (query: { search?: string } = {}) => ({ items: query.search ? [] : api.assignments, page: 1, pageSize: 20, totalCount: query.search ? 0 : 1, totalPages: query.search ? 0 : 1, hasPreviousPage: false, hasNextPage: false })),
  listRoleCandidates: vi.fn(async () => ({ items: api.users, page: 1, pageSize: 100, totalCount: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false })),
}))

vi.mock('../../api/roleAssignments.ts', () => ({
  listRoles: vi.fn(async () => api.roles),
  listAssignments: api.listAssignments,
  listRoleCandidates: api.listRoleCandidates,
  getUserRoleHistory: vi.fn(async () => []),
  assignRole: vi.fn(),
  revokeRole: vi.fn(),
}))
vi.mock('../../auth/useAuth.ts', () => ({ useAuth: () => ({ can: () => true, user: null, status: 'authenticated' }) }))

describe('RoleManagementPage', () => {
  beforeEach(() => vi.clearAllMocks())

  it('renders assignments and system-managed Employee behavior', async () => {
    renderAsUser(<RoleManagementPage />)
    expect(screen.getByRole('heading', { name: 'Role Management' })).toBeInTheDocument()
    expect(await screen.findByText('Rahul Mehta')).toBeInTheDocument()
    expect(screen.getByText('System managed')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Revoke' })).not.toBeInTheDocument()
  })

  it('opens assignment flow and does not offer system roles', async () => {
    renderAsUser(<RoleManagementPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Assign Role' }))
    expect(await screen.findByRole('heading', { name: 'Assign Role' })).toBeInTheDocument()
    const roleSelect = screen.getAllByRole('combobox').at(-1)!
    expect(roleSelect).toHaveTextContent('HRBP')
    expect(roleSelect).not.toHaveTextContent('Employee')
    expect(roleSelect).not.toHaveTextContent('Manager')
    expect(api.listRoleCandidates).toHaveBeenCalledTimes(1)
  })

  it('uses the aggregate assignment endpoint with server-side filters and no per-user requests', async () => {
    renderAsUser(<RoleManagementPage />)
    await screen.findByText('Rahul Mehta')
    expect(api.listAssignments).toHaveBeenCalledWith(expect.objectContaining({ page: 1, pageSize: 20 }))
    fireEvent.change(screen.getByPlaceholderText('Name, code, or user ID'), { target: { value: 'missing' } })
    await screen.findByText('No role assignments found.')
    expect(api.listAssignments).toHaveBeenLastCalledWith(expect.objectContaining({ search: 'missing', page: 1 }))
  })

  it('filters assignments and renders the roles tab', async () => {
    renderAsUser(<RoleManagementPage />)
    await screen.findByText('Rahul Mehta')
    fireEvent.change(screen.getByPlaceholderText('Name, code, or user ID'), { target: { value: 'missing' } })
    expect(await screen.findByText('No role assignments found.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('tab', { name: 'Roles' }))
    await waitFor(() => expect(screen.getByText('HRBP')).toBeInTheDocument())
    expect(screen.getByText('Canonical roles are managed by the platform.')).toBeInTheDocument()
  })
})
