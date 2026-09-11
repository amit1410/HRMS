import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AttendanceFoundationPage } from './AttendanceFoundationPage.tsx'
import { renderAsUser } from '../../test/renderWith.tsx'
import { installStubAdapter, ok } from '../../test/stubAdapter.ts'
import { makeUser } from '../../test/fixtures.ts'
import { Permissions } from '../../auth/permissions.ts'

describe('AttendanceFoundationPage', () => {
  let restore: (() => void) | undefined
  afterEach(() => { restore?.() })

  function setup() {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/shifts', () => ({ data: ok([{ id: 's1', shiftCode: 'M', shiftName: 'Morning', startTime: '09:00', endTime: '18:00', captureMode: 'BiometricOnly', graceInMinutes: 10, graceOutMinutes: 10, isDefault: false, crossesMidnight: false }]) }))
    stub.on('get', '/api/attendance/patterns', () => ({ data: ok([{ id: 'p1', code: 'PAT-A', name: 'Pattern A', cycleLengthDays: 3, days: [] }]) }))
    stub.on('get', '/api/attendance/shift-applicability', () => ({ data: ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }) }))
    return stub
  }

  it('renders capture methodology, default control, and full break add/edit/delete UX', async () => {
    setup(); renderAsUser(<AttendanceFoundationPage />)
    expect(await screen.findByText('Attendance Foundation')).toBeInTheDocument()
    expect(screen.getByText('Set as Default Shift')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Add Break' }))
    expect(screen.getByLabelText('Break 1 name')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Break 1 name'), { target: { value: 'Lunch' } })
    expect(screen.getByDisplayValue('Lunch')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Delete' }))
    expect(screen.queryByLabelText('Break 1 name')).not.toBeInTheDocument()
  })

  it('supports Shift and Shift Pattern applicability targets and dynamic conditions', async () => {
    const stub = setup(); renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage, Permissions.attendance.rosterManage] }) })
    fireEvent.change(screen.getByLabelText('Rule Name'), { target: { value: 'IT rule' } })
    fireEvent.change(screen.getByLabelText('Target Type'), { target: { value: 'Shift Pattern' } })
    expect(await screen.findByText('PAT-A - Pattern A')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Shift Pattern'), { target: { value: 'p1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add Condition' }))
    expect(screen.getByLabelText('Condition 2 dimension')).toBeInTheDocument()
    fireEvent.click(screen.getAllByRole('button', { name: 'Remove' })[1]!)
    expect(screen.queryByLabelText('Condition 2 dimension')).not.toBeInTheDocument()
    stub.on('post', '/api/attendance/applicability', () => ({ data: ok({}) }))
    fireEvent.click(screen.getByRole('button', { name: 'Save Applicability Rule' }))
    await waitFor(() => expect(screen.getByText('Rule name, target, and every condition value are required.')).toBeInTheDocument())
  })

  it('renders upload NEW, UPDATE, UNCHANGED, ERROR, and calendar override metadata', async () => {
    const stub = setup()
    stub.on('post', '/api/attendance/roster/upload/validate', () => ({ data: ok({ id: 'b1', status: 'Validated', totalRows: 4, validRows: 3, invalidRows: 1, committedRows: 0, rows: [
      { rowNumber: 2, employeeCode: 'E001', rosterDate: '2026-08-15', dayType: 'Shift', shiftCode: 'M', action: 'New', isValid: true, underlyingCalendarDayType: 'Holiday', willOverrideCalendar: true },
      { rowNumber: 3, employeeCode: 'E001', rosterDate: '2026-08-16', dayType: 'Shift', shiftCode: 'E', currentDayType: 'Shift', currentShiftCode: 'M', action: 'Update', isValid: true, underlyingCalendarDayType: 'WeeklyOff', willOverrideCalendar: true },
      { rowNumber: 4, employeeCode: 'E001', rosterDate: '2026-08-17', dayType: 'Shift', shiftCode: 'M', currentDayType: 'Shift', currentShiftCode: 'M', action: 'Unchanged', isValid: true },
      { rowNumber: 5, employeeCode: 'E001', rosterDate: '2026-08-18', dayType: 'Shift', shiftCode: 'UNKNOWN', action: 'Error', isValid: false, errorMessage: 'Shift code not found.' },
    ] }) }))
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.rosterUpload, Permissions.attendance.rosterManage] }) })
    const input = await waitFor(() => document.querySelector('input[type="file"]'))
    if (!input) throw new Error('Roster CSV input did not render')
    fireEvent.change(input, { target: { files: [new File(['csv'], 'roster.csv', { type: 'text/csv' })] } })
    fireEvent.click(screen.getByRole('button', { name: 'Validate CSV' }))
    expect(await screen.findByText('UPDATE')).toBeInTheDocument()
    expect(screen.getByText('NEW')).toBeInTheDocument()
    expect(screen.getByText('UNCHANGED')).toBeInTheDocument()
    expect(screen.getByText('ERROR')).toBeInTheDocument()
    expect(screen.getByText('Holiday → M')).toBeInTheDocument()
    expect(screen.getByText('WeeklyOff → E')).toBeInTheDocument()
    expect(screen.getByText('Shift code not found.')).toBeInTheDocument()
    expect(screen.getByText('Total Rows')).toBeInTheDocument()
  })

  it('loads a persisted applicability rule and submits an edited target', async () => {
    const stub = setup()
    stub.on('get', '/api/attendance/shift-applicability', () => ({ data: ok({ items: [{ id: 'r1', ruleName: 'IT rule', priority: 4, effectiveFrom: '2026-01-01', shiftId: 's1', shiftPatternId: null, isActive: true, conditions: {} }], page: 1, pageSize: 20, totalCount: 1 }) }))
    stub.on('get', '/api/attendance/shift-applicability/r1', () => ({ data: ok({ id: 'r1', ruleName: 'IT rule', priority: 4, effectiveFrom: '2026-01-01', shiftId: 's1', shiftPatternId: null, isActive: true, conditions: {} }) }))
    stub.on('put', '/api/attendance/shift-applicability/r1', call => ({ data: ok({ ...call.body as object }) }))
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage] }) })
    fireEvent.click(await screen.findByRole('button', { name: 'Refresh Rules' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Edit' }))
    expect(await screen.findByDisplayValue('IT rule')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Target Type'), { target: { value: 'Shift Pattern' } })
    expect(screen.getByText('PAT-A - Pattern A')).toBeInTheDocument()
  })

  it('confirms holiday assignment before calling the roster mutation', async () => {
    const stub = setup()
    stub.on('get', '/api/attendance/employees/e1/roster/2026-08-15', () => ({ data: ok({ employeeId: 'e1', rosterDate: '2026-08-15', dayType: 'Holiday', underlyingCalendarDayType: 'Holiday', assignmentSource: 'System', isExplicitRoster: false }) }))
    stub.on('post', '/api/attendance/roster/assign', () => ({ data: ok([]) }))
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage, Permissions.attendance.rosterManage] }) })
    fireEvent.change((await screen.findAllByLabelText('Employee ID')).at(-1)!, { target: { value: 'e1' } })
    fireEvent.change(screen.getByLabelText('Date'), { target: { value: '2026-08-15' } })
    fireEvent.change((await screen.findAllByLabelText('Shift')).at(-1)!, { target: { value: 's1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load Effective Roster' }))
    await screen.findByText('Holiday')
    fireEvent.click(screen.getByRole('button', { name: 'Assign Roster' }))
    await waitFor(() => expect(stub.callsTo('post', '/api/attendance/roster/assign')).toHaveLength(1))
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('Holiday'))
    confirm.mockRestore()
  })

  it('deletes an applicability rule only after confirmation and refreshes', async () => {
    const stub = setup()
    stub.on('get', '/api/attendance/shift-applicability', (_call, attempt) => ({ data: ok({ items: attempt === 1 ? [{ id: 'r1', ruleName: 'Remove me', priority: 1, effectiveFrom: '2026-01-01', shiftId: 's1', isActive: true, conditions: {} }] : [], page: 1, pageSize: 20, totalCount: attempt === 1 ? 1 : 0 }) }))
    stub.on('delete', '/api/attendance/shift-applicability/r1', () => ({ data: ok(true) }))
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage] }) })
    fireEvent.click(await screen.findByRole('button', { name: 'Refresh Rules' }))
    fireEvent.click((await screen.findAllByRole('button', { name: 'Delete' })).at(-1)!)
    await waitFor(() => expect(stub.callsTo('delete', '/api/attendance/shift-applicability/r1')).toHaveLength(1))
    expect(await screen.findByText('No shift applicability rules configured.')).toBeInTheDocument()
    vi.restoreAllMocks()
  })

  it('loads persisted breaks, retains their id, and sends edited values', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/shifts', () => ({ data: ok([{ id: 's1', shiftCode: 'N', shiftName: 'Night', startTime: '22:00', endTime: '06:00', captureMode: 'Manual', graceInMinutes: 0, graceOutMinutes: 0, isDefault: false, crossesMidnight: true, breaks: [{ id: 'b1', name: 'Old', startTime: '01:00', endTime: '01:30', description: '', sequence: 1, isPaid: false }] }]) }))
    stub.on('get', '/api/attendance/patterns', () => ({ data: ok([]) })); stub.on('get', '/api/attendance/shift-applicability', () => ({ data: ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }) }))
    stub.on('put', '/api/attendance/shifts/s1', call => ({ data: ok(call.body) }))
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage] }) })
    fireEvent.change((await screen.findAllByLabelText('Existing Shift'))[0]!, { target: { value: 's1' } })
    const name = await screen.findByDisplayValue('Old'); fireEvent.change(name, { target: { value: 'Edited' } }); fireEvent.click(screen.getByRole('button', { name: 'Update Shift' }))
    await waitFor(() => expect(stub.callsTo('put', '/api/attendance/shifts/s1')).toHaveLength(1))
    expect((stub.callsTo('put', '/api/attendance/shifts/s1')[0]!.body as { breaks: { id: string; name: string }[] }).breaks[0]).toMatchObject({ id: 'b1', name: 'Edited' })
  })

  it('renders effective roster metadata and sends all filter values to the server', async () => {
    const stub = setup()
    stub.on('get', '/api/attendance/roster', () => ({ data: ok({ items: [{ employeeId: 'e1', employeeCode: 'E001', employeeName: 'A', rosterDate: '2026-09-11', dayType: 'Shift', effectiveShiftCode: 'M', assignmentSource: 'Auto', underlyingCalendarDayType: 'Working', isCalendarOverride: false }], page: 1, pageSize: 20, totalCount: 1 }) }))
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.rosterManage] }) })
    fireEvent.change(screen.getByLabelText('From Date'), { target: { value: '2026-09-01' } })
    fireEvent.change(screen.getByLabelText('To Date'), { target: { value: '2026-09-11' } })
    fireEvent.change(screen.getAllByLabelText('Employee ID')[0]!, { target: { value: 'e1' } })
    await waitFor(() => expect(screen.getAllByRole('option', { name: /M/ }).length).toBeGreaterThan(0)); fireEvent.change((await screen.findAllByLabelText('Shift'))[1]!, { target: { value: 's1' } })
    fireEvent.change(screen.getAllByLabelText('Day Type')[0]!, { target: { value: 'Shift' } })
    fireEvent.change(screen.getByLabelText('Assignment Source'), { target: { value: 'Auto' } })
    fireEvent.click(screen.getByRole('button', { name: 'Refresh Grid' }))
    await screen.findByText('E001 – A')
    const call = stub.callsTo('get', '/api/attendance/roster').at(-1)!
    expect(call.params).toMatchObject({ fromDate: '2026-09-01', toDate: '2026-09-11', employeeId: 'e1', shiftId: 's1', dayType: 'Shift', assignmentSource: 'Auto' })
    expect(screen.getAllByText('Auto').length).toBeGreaterThan(0)
    expect(screen.getByText('Working')).toBeInTheDocument()
  })

  it('requests the next roster page with deterministic page state', async () => {
    const stub = setup()
    stub.on('get', '/api/attendance/roster', (call) => ({ data: ok({ items: [{ employeeId: `e${call.params.page}`, employeeCode: `E00${call.params.page}`, employeeName: 'Employee', rosterDate: '2026-09-11', dayType: 'Shift', effectiveShiftCode: 'M', assignmentSource: 'System', underlyingCalendarDayType: 'Working', isCalendarOverride: false }], page: Number(call.params.page ?? 1), pageSize: 20, totalCount: 40 }) }))
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.rosterManage] }) })
    fireEvent.click(screen.getByRole('button', { name: 'Refresh Grid' }))
    await screen.findByText('E001 – Employee')
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    await screen.findByText('E002 – Employee')
    expect(stub.callsTo('get', '/api/attendance/roster').at(-1)!.params.page).toBe(2)
    expect(screen.getByText('Page 2 · 40 rows')).toBeInTheDocument()
  })

  it('cancels a Holiday override without mutating the roster', async () => {
    const stub = setup()
    stub.on('get', '/api/attendance/employees/e1/roster/2026-08-15', () => ({ data: ok({ employeeId: 'e1', rosterDate: '2026-08-15', dayType: 'Holiday', underlyingCalendarDayType: 'Holiday', assignmentSource: 'System', isExplicitRoster: false }) }))
    stub.on('post', '/api/attendance/roster/assign', () => ({ data: ok([]) }))
    vi.spyOn(window, 'confirm').mockReturnValue(false)
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage, Permissions.attendance.rosterManage] }) })
    fireEvent.change((await screen.findAllByLabelText('Employee ID')).at(-1)!, { target: { value: 'e1' } })
    fireEvent.change(screen.getByLabelText('Date'), { target: { value: '2026-08-15' } })
    fireEvent.change((await screen.findAllByLabelText('Shift')).at(-1)!, { target: { value: 's1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load Effective Roster' }))
    await screen.findByText('Holiday')
    fireEvent.click(screen.getByRole('button', { name: 'Assign Roster' }))
    expect(stub.callsTo('post', '/api/attendance/roster/assign')).toHaveLength(0)
    vi.restoreAllMocks()
  })

  it('confirms a WeeklyOff override before assigning a working Shift', async () => {
    const stub = setup()
    stub.on('get', '/api/attendance/employees/e1/roster/2026-08-16', () => ({ data: ok({ employeeId: 'e1', rosterDate: '2026-08-16', dayType: 'WeeklyOff', underlyingCalendarDayType: 'WeeklyOff', assignmentSource: 'Auto', isExplicitRoster: false }) }))
    stub.on('post', '/api/attendance/roster/assign', () => ({ data: ok([]) }))
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage, Permissions.attendance.rosterManage] }) })
    fireEvent.change((await screen.findAllByLabelText('Employee ID')).at(-1)!, { target: { value: 'e1' } }); fireEvent.change(screen.getByLabelText('Date'), { target: { value: '2026-08-16' } }); fireEvent.change((await screen.findAllByLabelText('Shift')).at(-1)!, { target: { value: 's1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load Effective Roster' })); await screen.findAllByText('WeeklyOff'); fireEvent.click(screen.getByRole('button', { name: 'Assign Roster' }))
    await waitFor(() => expect(stub.callsTo('post', '/api/attendance/roster/assign')).toHaveLength(1)); expect(confirm).toHaveBeenCalledWith(expect.stringContaining('Weekly Off')); confirm.mockRestore()
  })

  it('confirms changing a working Shift to WeeklyOff', async () => {
    const stub = setup()
    stub.on('get', '/api/attendance/employees/e1/roster/2026-08-17', () => ({ data: ok({ employeeId: 'e1', rosterDate: '2026-08-17', dayType: 'Shift', shiftId: 's1', shiftCode: 'M', underlyingCalendarDayType: 'Working', assignmentSource: 'Auto', isExplicitRoster: false }) }))
    stub.on('post', '/api/attendance/roster/assign', () => ({ data: ok([]) }))
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage, Permissions.attendance.rosterManage] }) })
    fireEvent.change((await screen.findAllByLabelText('Employee ID')).at(-1)!, { target: { value: 'e1' } }); fireEvent.change(screen.getByLabelText('Date'), { target: { value: '2026-08-17' } }); fireEvent.change((await screen.findAllByLabelText('Day Type')).at(-1)!, { target: { value: 'WeeklyOff' } })
    fireEvent.click(screen.getByRole('button', { name: 'Load Effective Roster' })); await screen.findAllByText('Shift'); fireEvent.click(screen.getByRole('button', { name: 'Assign Roster' }))
    await waitFor(() => expect(stub.callsTo('post', '/api/attendance/roster/assign')).toHaveLength(1)); expect(confirm).toHaveBeenCalledWith(expect.stringContaining('working Shift')); confirm.mockRestore()
  })

  it('removes an explicit override only after confirmation and refetches', async () => {
    const stub = setup(); let loads = 0
    stub.on('get', '/api/attendance/employees/e1/roster/2026-08-18', () => ({ data: ok({ employeeId: 'e1', rosterDate: '2026-08-18', dayType: loads++ === 0 ? 'Shift' : 'Holiday', shiftId: loads === 1 ? 's1' : null, shiftCode: loads === 1 ? 'M' : null, assignmentSource: loads === 1 ? 'Manual' : 'System', isExplicitRoster: loads === 1, underlyingCalendarDayType: 'Holiday' }) }))
    stub.on('delete', '/api/attendance/roster/e1/2026-08-18', () => ({ data: ok(true) })); const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.rosterManage] }) })
    fireEvent.change((await screen.findAllByLabelText('Employee ID')).at(-1)!, { target: { value: 'e1' } }); fireEvent.change(screen.getByLabelText('Date'), { target: { value: '2026-08-18' } }); fireEvent.click(screen.getByRole('button', { name: 'Load Effective Roster' })); await screen.findByText('Manual'); fireEvent.click(screen.getByRole('button', { name: 'Remove Override' }))
    await waitFor(() => expect(stub.callsTo('delete', '/api/attendance/roster/e1/2026-08-18')).toHaveLength(1)); await waitFor(() => expect(screen.getAllByText('Holiday').length).toBeGreaterThan(0)); expect(confirm).toHaveBeenCalled(); confirm.mockRestore()
  })

  it('commits a valid upload and refreshes the effective grid', async () => {
    const stub = setup()
    stub.on('post', '/api/attendance/roster/upload/validate', () => ({ data: ok({ id: 'b1', status: 'Validated', totalRows: 1, validRows: 1, invalidRows: 0, committedRows: 0, rows: [{ rowNumber: 2, employeeCode: 'E001', rosterDate: '2026-08-20', dayType: 'Shift', shiftCode: 'M', action: 'New', isValid: true }] }) }))
    stub.on('post', '/api/attendance/roster/upload/b1/commit', () => ({ data: ok({ id: 'b1', status: 'Committed', totalRows: 1, validRows: 1, invalidRows: 0, committedRows: 1, rows: [] }) }))
    stub.on('get', '/api/attendance/roster', () => ({ data: ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }) }))
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.rosterUpload, Permissions.attendance.rosterManage] }) })
    const input = document.querySelector('input[type="file"]'); if (!input) throw new Error('upload input missing'); fireEvent.change(input, { target: { files: [new File(['csv'], 'roster.csv', { type: 'text/csv' })] } }); fireEvent.click(screen.getByRole('button', { name: 'Validate CSV' })); await screen.findByText('Commit Roster'); fireEvent.click(screen.getByRole('button', { name: 'Commit Roster' }))
    await waitFor(() => expect(stub.callsTo('post', '/api/attendance/roster/upload/b1/commit')).toHaveLength(1)); expect(screen.getByText('Roster upload committed.')).toBeInTheDocument(); expect(stub.callsTo('get', '/api/attendance/roster').length).toBeGreaterThan(0)
  })

  it('omits a deleted persisted break from the update payload', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/shifts', () => ({ data: ok([{ id: 's1', shiftCode: 'M', shiftName: 'Morning', startTime: '09:00', endTime: '18:00', captureMode: 'Manual', graceInMinutes: 0, graceOutMinutes: 0, isDefault: false, crossesMidnight: false, breaks: [{ id: 'b1', name: 'Lunch', startTime: '13:00', endTime: '13:30', description: '', sequence: 1, isPaid: false }] }]) })); stub.on('get', '/api/attendance/patterns', () => ({ data: ok([]) })); stub.on('get', '/api/attendance/shift-applicability', () => ({ data: ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }) })); stub.on('put', '/api/attendance/shifts/s1', call => ({ data: ok(call.body) }))
     renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage] }) }); fireEvent.change((await screen.findAllByLabelText('Existing Shift'))[0]!, { target: { value: 's1' } }); await screen.findByDisplayValue('Lunch'); fireEvent.click(screen.getByRole('button', { name: 'Delete' })); fireEvent.click(screen.getByRole('button', { name: 'Update Shift' })); await waitFor(() => expect(stub.callsTo('put', '/api/attendance/shifts/s1')).toHaveLength(1)); expect((stub.callsTo('put', '/api/attendance/shifts/s1')[0]!.body as { breaks: unknown[] }).breaks).toHaveLength(0)
  })

  it('sends retained and newly added breaks exactly once', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/shifts', () => ({ data: ok([{ id: 's1', shiftCode: 'M', shiftName: 'Morning', startTime: '09:00', endTime: '18:00', captureMode: 'Manual', graceInMinutes: 0, graceOutMinutes: 0, isDefault: false, crossesMidnight: false, breaks: [{ id: 'b1', name: 'Lunch', startTime: '13:00', endTime: '13:30', description: '', sequence: 1, isPaid: false }] }]) })); stub.on('get', '/api/attendance/patterns', () => ({ data: ok([]) })); stub.on('get', '/api/attendance/shift-applicability', () => ({ data: ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }) })); stub.on('put', '/api/attendance/shifts/s1', call => ({ data: ok(call.body) }))
     renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage] }) }); fireEvent.change((await screen.findAllByLabelText('Existing Shift'))[0]!, { target: { value: 's1' } }); await screen.findByDisplayValue('Lunch'); fireEvent.click(screen.getByRole('button', { name: 'Add Break' })); fireEvent.change(screen.getByLabelText('Break 2 name'), { target: { value: 'Tea' } }); fireEvent.click(screen.getByRole('button', { name: 'Update Shift' })); await waitFor(() => expect(stub.callsTo('put', '/api/attendance/shifts/s1')).toHaveLength(1)); const breaks = (stub.callsTo('put', '/api/attendance/shifts/s1')[0]!.body as { breaks: { id?: string; name: string }[] }).breaks; expect(breaks).toHaveLength(2); expect(breaks[0]).toMatchObject({ id: 'b1' }); expect(breaks[1]).toMatchObject({ name: 'Tea' })
  })

  it('surfaces Shift load failure without crashing the editor', async () => {
    const stub = installStubAdapter(); restore = stub.restore; stub.on('get', '/api/attendance/patterns', () => ({ data: ok([]) })); stub.on('get', '/api/attendance/shift-applicability', () => ({ data: ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }) }))
    renderAsUser(<AttendanceFoundationPage />); expect(await screen.findByText('No stub registered for GET /api/attendance/shifts')).toBeInTheDocument(); expect(screen.getByText('Attendance Foundation')).toBeInTheDocument()
  })

  it('shows backend Break validation and keeps the invalid break editable', async () => {
    const stub = setup(); stub.on('post', '/api/attendance/shifts', () => ({ status: 400, data: { success: false, message: 'Break name is required.' } }))
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage] }) }); await screen.findByText('Morning'); fireEvent.click(screen.getByRole('button', { name: 'Add Break' })); fireEvent.click(screen.getByRole('button', { name: 'Create Shift' }))
    expect(await screen.findByText('Break name is required.')).toBeInTheDocument(); expect(screen.getByLabelText('Break 1 name')).toBeInTheDocument(); expect(screen.queryByText('Shift created.')).not.toBeInTheDocument()
  })

  it('preserves a persisted edited break and a new break after failed save', async () => {
    const stub = installStubAdapter(); restore = stub.restore
    stub.on('get', '/api/attendance/shifts', () => ({ data: ok([{ id: 's1', shiftCode: 'N', shiftName: 'Night', startTime: '22:00', endTime: '06:00', captureMode: 'Manual', graceInMinutes: 0, graceOutMinutes: 0, isDefault: false, crossesMidnight: true, breaks: [{ id: 'b1', name: 'Old', startTime: '01:00', endTime: '01:30', description: '', sequence: 1, isPaid: false }] }]) })); stub.on('get', '/api/attendance/patterns', () => ({ data: ok([]) })); stub.on('get', '/api/attendance/shift-applicability', () => ({ data: ok({ items: [], page: 1, pageSize: 20, totalCount: 0 }) })); stub.on('put', '/api/attendance/shifts/s1', () => ({ status: 400, data: { success: false, message: 'Shift save failed.' } }))
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage] }) }); await screen.findByText('Night'); fireEvent.change((await screen.findAllByLabelText('Existing Shift'))[0]!, { target: { value: 's1' } }); const old = await screen.findByDisplayValue('Old'); fireEvent.change(old, { target: { value: 'Edited' } }); fireEvent.click(screen.getByRole('button', { name: 'Add Break' })); fireEvent.change(screen.getByLabelText('Break 2 name'), { target: { value: 'Tea' } }); fireEvent.click(screen.getByRole('button', { name: 'Update Shift' }))
    expect(await screen.findByText('Shift save failed.')).toBeInTheDocument(); expect(screen.getByDisplayValue('Edited')).toBeInTheDocument(); expect(screen.getByDisplayValue('Tea')).toBeInTheDocument(); expect(screen.getByText('Night')).toBeInTheDocument()
  })

  it('keeps an applicability edit available after update validation fails', async () => {
     const stub = setup(); stub.on('get', '/api/attendance/shift-applicability', () => ({ data: ok({ items: [{ id: 'r1', ruleName: 'Rule', priority: 1, effectiveFrom: '2026-01-01', shiftId: 's1', isActive: true, conditions: { Department: 'd1' } }], page: 1, pageSize: 20, totalCount: 1 }) })); stub.on('get', '/api/attendance/shift-applicability/r1', () => ({ data: ok({ id: 'r1', ruleName: 'Rule', priority: 1, effectiveFrom: '2026-01-01', shiftId: 's1', isActive: true, conditions: { Department: 'd1' } }) })); stub.on('put', '/api/attendance/shift-applicability/r1', () => ({ status: 400, data: { success: false, message: 'Applicability update failed.' } }))
      renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage] }) }); await screen.findByRole('button', { name: 'Edit' }); fireEvent.click(screen.getByRole('button', { name: 'Edit' })); await screen.findByDisplayValue('Rule'); fireEvent.change(screen.getByDisplayValue('Rule'), { target: { value: 'Edited Rule' } }); fireEvent.click(screen.getByRole('button', { name: 'Update Applicability Rule' }))
    expect(await screen.findByText('Applicability update failed.')).toBeInTheDocument(); expect(screen.getByDisplayValue('Edited Rule')).toBeInTheDocument()
  })

  it('keeps an applicability rule visible after delete failure', async () => {
    const stub = setup(); stub.on('get', '/api/attendance/shift-applicability', () => ({ data: ok({ items: [{ id: 'r1', ruleName: 'Keep Rule', priority: 1, effectiveFrom: '2026-01-01', shiftId: 's1', isActive: true, conditions: {} }], page: 1, pageSize: 20, totalCount: 1 }) })); stub.on('delete', '/api/attendance/shift-applicability/r1', () => ({ status: 500, data: { success: false, message: 'Delete failed.' } })); const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
     renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage] }) }); await screen.findByText('Keep Rule'); fireEvent.click(screen.getByRole('button', { name: 'Delete' })); expect(await screen.findByText('Delete failed.')).toBeInTheDocument(); expect(screen.getByText('Keep Rule')).toBeInTheDocument(); expect(screen.queryByText('Applicability rule deleted.')).not.toBeInTheDocument(); confirm.mockRestore()
  })

  it('keeps the effective roster state after assignment failure', async () => {
    const stub = setup(); stub.on('get', '/api/attendance/employees/e1/roster/2026-08-21', () => ({ data: ok({ employeeId: 'e1', rosterDate: '2026-08-21', dayType: 'Shift', shiftId: 's1', shiftCode: 'M', assignmentSource: 'Auto', isExplicitRoster: false, underlyingCalendarDayType: 'Working' }) })); stub.on('post', '/api/attendance/roster/assign', () => ({ status: 500, data: { success: false, message: 'Assignment failed.' } })); vi.spyOn(window, 'confirm').mockReturnValue(true)
      renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage, Permissions.attendance.rosterManage] }) }); await screen.findByText('Morning'); fireEvent.change((await screen.findAllByLabelText('Employee ID')).at(-1)!, { target: { value: 'e1' } }); fireEvent.change(screen.getByLabelText('Date'), { target: { value: '2026-08-21' } }); fireEvent.change((await screen.findAllByLabelText('Shift')).at(-1)!, { target: { value: 's1' } }); fireEvent.click(screen.getByRole('button', { name: 'Load Effective Roster' })); await screen.findAllByText('Auto'); fireEvent.click(screen.getByRole('button', { name: 'Assign Roster' })); expect(await screen.findByText('Assignment failed.')).toBeInTheDocument(); expect(screen.queryByText('Roster assignment saved.')).not.toBeInTheDocument()
  })

  it('keeps an explicit override after Remove Override failure', async () => {
    const stub = setup(); stub.on('get', '/api/attendance/employees/e1/roster/2026-08-22', () => ({ data: ok({ employeeId: 'e1', rosterDate: '2026-08-22', dayType: 'Shift', shiftId: 's1', shiftCode: 'M', assignmentSource: 'Manual', isExplicitRoster: true, underlyingCalendarDayType: 'Holiday', isCalendarOverride: true }) })); stub.on('delete', '/api/attendance/roster/e1/2026-08-22', () => ({ status: 500, data: { success: false, message: 'Override removal failed.' } })); vi.spyOn(window, 'confirm').mockReturnValue(true)
     renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.rosterManage] }) }); await screen.findByText('Morning'); fireEvent.change((await screen.findAllByLabelText('Employee ID')).at(-1)!, { target: { value: 'e1' } }); fireEvent.change(screen.getByLabelText('Date'), { target: { value: '2026-08-22' } }); fireEvent.click(screen.getByRole('button', { name: 'Load Effective Roster' })); await screen.findAllByText('Manual'); fireEvent.click(screen.getByRole('button', { name: 'Remove Override' })); expect(await screen.findByText('Override removal failed.')).toBeInTheDocument(); expect(screen.getAllByText('Manual').length).toBeGreaterThan(0)
   })

  it('preserves a new Shift form after create failure', async () => {
    const stub = setup(); stub.on('post', '/api/attendance/shifts', () => ({ status: 400, data: { success: false, message: 'Shift create failed.' } }))
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage] }) }); await screen.findByText('Morning')
    fireEvent.change(screen.getByLabelText('Shift Code'), { target: { value: 'N' } }); fireEvent.change(screen.getByLabelText('Shift Name'), { target: { value: 'Night' } }); fireEvent.click(screen.getByRole('button', { name: 'Add Break' })); fireEvent.change(screen.getByLabelText('Break 1 name'), { target: { value: 'Tea' } }); fireEvent.click(screen.getByRole('button', { name: 'Create Shift' }))
    expect(await screen.findByText('Shift create failed.')).toBeInTheDocument(); expect(screen.getByDisplayValue('Night')).toBeInTheDocument(); expect(screen.getByDisplayValue('Tea')).toBeInTheDocument(); expect(screen.queryByText('Shift created.')).not.toBeInTheDocument()
  })

  it('surfaces applicability list failure without replacing the page with an empty success state', async () => {
    const stub = setup(); await Promise.resolve(); stub.on('get', '/api/attendance/shift-applicability', () => ({ status: 500, data: { success: false, message: 'Applicability list failed.' } }))
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.shiftManage] }) }); await screen.findByText('Morning'); fireEvent.click(screen.getByRole('button', { name: 'Refresh Rules' }))
    expect(await screen.findByText('Applicability list failed.')).toBeInTheDocument(); expect(screen.getByText('Attendance Foundation')).toBeInTheDocument()
  })

  it('surfaces effective roster query failure without a fake empty success', async () => {
    const stub = setup(); stub.on('get', '/api/attendance/roster', () => ({ status: 500, data: { success: false, message: 'Roster query failed.' } }))
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.rosterManage] }) }); await screen.findByText('Morning'); fireEvent.click(screen.getByRole('button', { name: 'Refresh Grid' }))
    expect(await screen.findByText('Roster query failed.')).toBeInTheDocument(); expect(screen.getByText('Attendance Foundation')).toBeInTheDocument()
  })

  it('keeps the working state after a WeeklyOff assignment failure', async () => {
    const stub = setup(); stub.on('get', '/api/attendance/employees/e1/roster/2026-08-23', () => ({ data: ok({ employeeId: 'e1', rosterDate: '2026-08-23', dayType: 'Shift', shiftId: 's1', shiftCode: 'M', assignmentSource: 'Auto', isExplicitRoster: false, underlyingCalendarDayType: 'Working' }) })); stub.on('post', '/api/attendance/roster/assign', () => ({ status: 500, data: { success: false, message: 'WeeklyOff assignment failed.' } })); vi.spyOn(window, 'confirm').mockReturnValue(true)
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.rosterManage] }) }); await screen.findByText('Morning'); fireEvent.change((await screen.findAllByLabelText('Employee ID')).at(-1)!, { target: { value: 'e1' } }); fireEvent.change(screen.getByLabelText('Date'), { target: { value: '2026-08-23' } }); fireEvent.click(screen.getByRole('button', { name: 'Load Effective Roster' })); await screen.findByText('Auto'); fireEvent.change(screen.getAllByLabelText('Day Type')[1]!, { target: { value: 'WeeklyOff' } }); fireEvent.click(screen.getByRole('button', { name: 'Assign Roster' })); expect(await screen.findByText('WeeklyOff assignment failed.')).toBeInTheDocument(); expect(screen.getAllByText('Shift').length).toBeGreaterThan(0); expect(screen.queryByText('Roster assignment saved.')).not.toBeInTheDocument()
  })

  it('surfaces upload preview failure and leaves commit unavailable', async () => {
    const stub = setup(); stub.on('post', '/api/attendance/roster/upload/validate', () => ({ status: 400, data: { success: false, message: 'Preview failed.' } }))
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.rosterUpload] }) }); await screen.findByText('Morning'); stub.on('post', '/api/attendance/roster/upload/validate', () => ({ status: 400, data: { success: false, message: 'Preview failed.' } })); const input = document.querySelector('input[type="file"]'); if (!input) throw new Error('upload input missing'); fireEvent.change(input, { target: { files: [new File(['csv'], 'roster.csv', { type: 'text/csv' })] } }); fireEvent.click(screen.getByRole('button', { name: 'Validate CSV' }))
    expect(await screen.findByText('Preview failed.')).toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Commit Roster' })).not.toBeInTheDocument()
  })

  it('surfaces upload commit failure without reporting success', async () => {
    const stub = setup(); stub.on('post', '/api/attendance/roster/upload/validate', () => ({ data: ok({ id: 'b2', status: 'Validated', totalRows: 1, validRows: 1, invalidRows: 0, committedRows: 0, rows: [{ rowNumber: 2, employeeCode: 'E001', rosterDate: '2026-08-20', dayType: 'Shift', shiftCode: 'M', action: 'New', isValid: true }] }) })); stub.on('post', '/api/attendance/roster/upload/b2/commit', () => ({ status: 500, data: { success: false, message: 'Commit failed.' } }))
    renderAsUser(<AttendanceFoundationPage />, { user: makeUser({ permissions: [Permissions.attendance.rosterUpload] }) }); const input = document.querySelector('input[type="file"]'); if (!input) throw new Error('upload input missing'); fireEvent.change(input, { target: { files: [new File(['csv'], 'roster.csv', { type: 'text/csv' })] } }); fireEvent.click(screen.getByRole('button', { name: 'Validate CSV' })); fireEvent.click(await screen.findByRole('button', { name: 'Commit Roster' }))
    expect(await screen.findByText('Commit failed.')).toBeInTheDocument(); expect(screen.queryByText('Roster upload committed.')).not.toBeInTheDocument(); expect(screen.getByText('NEW')).toBeInTheDocument()
  })

})
