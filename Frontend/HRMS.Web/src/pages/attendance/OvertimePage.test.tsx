import { fireEvent, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { OvertimePage } from './OvertimePage.tsx'
import { Permissions } from '../../auth/permissions.ts'
import { makeUser } from '../../test/fixtures.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { installStubAdapter, ok } from '../../test/stubAdapter.ts'

const created = { id: 'ot1', employeeId: 'e1', workDate: '2026-09-15', requestedMinutes: 120, actualEligibleMinutes: 90, approvedMinutes: 0, category: 'NormalDay', status: 'Draft', policyId: 'p1', reason: 'Release', concurrencyVersion: 1 }

describe('Overtime page', () => {
  let restore: (() => void) | undefined
  afterEach(() => restore?.())

  it('renders the employee overtime request form', () => {
    restore = installStubAdapter().restore
    renderAsUser(<OvertimePage />, { user: makeUser({ permissions: [Permissions.attendance.overtimeRequest] }) })
    expect(screen.getByRole('heading', { name: 'My Overtime Requests' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create' })).toBeInTheDocument()
  })

  it('creates a request and displays server-derived minutes', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('post', '/api/attendance/overtime/requests', () => ({ data: ok(created) }))
    renderAsUser(<OvertimePage />, { user: makeUser({ permissions: [Permissions.attendance.overtimeRequest] }) })
    fireEvent.change(screen.getByLabelText('Employee ID'), { target: { value: 'e1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create' }))
    expect(await screen.findByText('90 minutes')).toBeInTheDocument()
    expect(screen.getByText('Actual eligible', { exact: true })).toBeInTheDocument()
  })

  it('submits only a draft and preserves the displayed approved value', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('post', '/api/attendance/overtime/requests', () => ({ data: ok(created) }))
    stub.on('post', '/api/attendance/overtime/requests/ot1/submit', () => ({ data: { ...ok(created), data: { ...created, status: 'Submitted' } } }))
    renderAsUser(<OvertimePage />, { user: makeUser({ permissions: [Permissions.attendance.overtimeRequest] }) })
    fireEvent.change(screen.getByLabelText('Employee ID'), { target: { value: 'e1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Submit' }))
    expect(await screen.findByText('Overtime request submitted.')).toBeInTheDocument()
    expect(screen.getByText('0 minutes')).toBeInTheDocument()
  })

  it('renders the manager team overtime queue', () => {
    restore = installStubAdapter().restore
    renderAsUser(<OvertimePage />, { user: makeUser({ permissions: [Permissions.attendance.overtimeViewTeam, Permissions.attendance.overtimeApprove] }) })
    expect(screen.getByRole('heading', { name: 'Team Overtime' })).toBeInTheDocument()
    expect(screen.getByText(/backend manager scope/)).toBeInTheDocument()
  })

  it('renders manager approve and reject actions', () => {
    restore = installStubAdapter().restore
    renderAsUser(<OvertimePage />, { user: makeUser({ permissions: [Permissions.attendance.overtimeApprove] }) })
    expect(screen.getByRole('button', { name: 'Approve selected' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reject selected' })).toBeInTheDocument()
  })

  it('renders time manager finalization controls', () => {
    restore = installStubAdapter().restore
    renderAsUser(<OvertimePage />, { user: makeUser({ permissions: [Permissions.attendance.overtimeFinalize] }) })
    expect(screen.getByRole('heading', { name: 'Monthly Overtime Finalization' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Finalize month' })).toBeInTheDocument()
  })

  it('renders reopen and version history for authorized users', () => {
    restore = installStubAdapter().restore
    renderAsUser(<OvertimePage />, { user: makeUser({ permissions: [Permissions.attendance.overtimeReopen] }) })
    expect(screen.getByRole('heading', { name: 'Reopen / Version History' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reopen' })).toBeInTheDocument()
  })

  it('renders payroll overtime traceability as read-only', () => {
    restore = installStubAdapter().restore
    renderAsUser(<OvertimePage />, { user: makeUser({ permissions: [Permissions.attendance.overtimeViewAll] }) })
    expect(screen.getByRole('heading', { name: 'Payroll OT Traceability' })).toBeInTheDocument()
    expect(screen.getByText(/finalized current snapshot/)).toBeInTheDocument()
  })

  it('does not render manager controls without manager permission', () => {
    restore = installStubAdapter().restore
    renderAsUser(<OvertimePage />, { user: makeUser({ permissions: [Permissions.attendance.overtimeRequest] }) })
    expect(screen.queryByRole('heading', { name: 'Team Overtime' })).not.toBeInTheDocument()
  })

  it('renders requested actual and approved labels together', () => {
    restore = installStubAdapter().restore
    renderAsUser(<OvertimePage />, { user: makeUser({ permissions: [Permissions.attendance.overtimeRequest] }) })
    expect(screen.getByLabelText('Requested minutes')).toBeInTheDocument()
    expect(screen.getByText(/Attendance derives eligible minutes/)).toBeInTheDocument()
  })

  it('renders period and reopen reason inputs', () => {
    restore = installStubAdapter().restore
    renderAsUser(<OvertimePage />, { user: makeUser({ permissions: [Permissions.attendance.overtimeFinalize, Permissions.attendance.overtimeReopen] }) })
    expect(screen.getByLabelText('Attendance period ID')).toBeInTheDocument()
    expect(screen.getByLabelText('Reopen reason')).toBeInTheDocument()
  })

  it('keeps payroll traceability read-only', () => {
    restore = installStubAdapter().restore
    renderAsUser(<OvertimePage />, { user: makeUser({ permissions: [Permissions.attendance.overtimeViewAll] }) })
    expect(screen.queryByRole('button', { name: /edit|save/i })).not.toBeInTheDocument()
  })
})
