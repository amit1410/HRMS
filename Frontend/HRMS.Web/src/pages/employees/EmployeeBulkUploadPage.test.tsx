import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { session } from '../../api/session.ts'
import { Permissions } from '../../auth/permissions.ts'
import { RequirePermission } from '../../auth/RequirePermission.tsx'
import { makeUser } from '../../test/fixtures.ts'
import { fail, installStubAdapter, ok, type StubAdapter } from '../../test/stubAdapter.ts'
import { renderAsUser } from '../../test/renderWith.tsx'
import { EmployeeBulkUploadPage, sanitizeBulkUploadText } from './EmployeeBulkUploadPage.tsx'

const fullPermissions = [Permissions.employee.import, Permissions.employee.create, Permissions.employeeSensitive.edit]

function renderGuarded(permissions: string[] = fullPermissions) {
  return renderAsUser(
    <Routes>
      <Route path="/employees/bulk-upload" element={
        <RequirePermission permission={Permissions.employee.import}>
          <RequirePermission permission={Permissions.employee.create}>
            <RequirePermission permission={Permissions.employeeSensitive.edit}>
              <EmployeeBulkUploadPage />
            </RequirePermission>
          </RequirePermission>
        </RequirePermission>
      } />
      <Route path="/employees/bulk-upload/:operationId" element={
        <RequirePermission permission={Permissions.employee.import}>
          <RequirePermission permission={Permissions.employee.create}>
            <RequirePermission permission={Permissions.employeeSensitive.edit}>
              <EmployeeBulkUploadPage />
            </RequirePermission>
          </RequirePermission>
        </RequirePermission>
      } />
      <Route path="/forbidden" element={<p>Access denied</p>} />
    </Routes>,
    { route: '/employees/bulk-upload', user: makeUser({ permissions }) },
  )
}

function preview(overrides: Partial<{
  totalRows: number
  validRows: number
  invalidRows: number
  warningRows: number
  rows: Array<{ rowNumber: number; status: string; issues: Array<{ fieldName: string; columnName: string; columnNumber: number; errorCode: string; message: string; guidance: string }> }>
}> = {}) {
  return {
    totalRows: 1,
    validRows: 1,
    invalidRows: 0,
    warningRows: 0,
    rows: [{ rowNumber: 2, status: 'Valid', issues: [] }],
    ...overrides,
  }
}

describe('EmployeeBulkUploadPage', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()
    stub.on('get', '/api/employees/bulk-upload/history', () => ({ data: ok({ items: [], page: 1, pageSize: 10, totalCount: 0, totalPages: 0 }) }))
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  it('requires Import, Create and Sensitive Edit permissions before rendering the workflow', () => {
    for (const permissions of [
      [Permissions.employee.import, Permissions.employee.create],
      [Permissions.employee.import, Permissions.employeeSensitive.edit],
      [Permissions.employee.create, Permissions.employeeSensitive.edit],
    ]) {
      const view = renderGuarded(permissions)
      expect(screen.getByText('Access denied')).toBeInTheDocument()
      view.unmount()
    }
  })

  it('shows the configured 10,000-row maximum by default', async () => {
    renderGuarded()
    expect(await screen.findByText(/Upload no more than 10,000 employee rows/)).toBeInTheDocument()
  })

  it('downloads the tenant template as a workbook blob', async () => {
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)
    const originalCreate = Object.getOwnPropertyDescriptor(URL, 'createObjectURL')
    const originalRevoke = Object.getOwnPropertyDescriptor(URL, 'revokeObjectURL')
    const createObjectURL = vi.fn(() => 'blob:employee-template')
    Object.defineProperty(URL, 'createObjectURL', { value: createObjectURL, configurable: true })
    Object.defineProperty(URL, 'revokeObjectURL', { value: vi.fn(), configurable: true })
    stub.on('get', '/api/employees/bulk-upload/template', () => ({ data: new Blob(['xlsx']) }))
    try {
      renderGuarded()
      await userEvent.click(screen.getByRole('button', { name: 'Download Excel Template' }))
      expect(stub.callsTo('get', '/api/employees/bulk-upload/template')[0]?.responseType).toBe('blob')
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

  it('rejects unsupported and oversized files before making an API request', async () => {
    renderGuarded()
    const input = screen.getByLabelText(/Choose or drop an Excel workbook/i)
    await userEvent.upload(input, new File(['text'], 'employees.csv', { type: 'text/csv' }), { applyAccept: false })
    expect(await screen.findByText(/\.xlsx extension/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Validate Workbook' })).toBeDisabled()

    const oversized = new File([new Uint8Array(10 * 1024 * 1024 + 1)], 'employees.xlsx')
    await userEvent.upload(input, oversized)
    expect(await screen.findByText(/exceeds the 10 MB limit/)).toBeInTheDocument()
    expect(stub.calls.filter(call => call.url !== '/api/employees/bulk-upload/history')).toHaveLength(0)
  })

  it('shows validation failures without exposing workbook values', async () => {
    stub.on('post', '/api/employees/bulk-upload/validate', () => ({ status: 400, data: fail('Workbook could not be validated.') }))
    renderGuarded()
    await userEvent.upload(screen.getByLabelText(/Choose or drop an Excel workbook/i), new File(['workbook'], 'employees.xlsx'))
    await userEvent.click(screen.getByRole('button', { name: 'Validate Workbook' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Workbook could not be validated.')
    expect(stub.callsTo('post', '/api/employees/bulk-upload/validate')).toHaveLength(1)
  })

  it('renders row, column and correction guidance for validation failures and disables preparation', async () => {
    stub.on('post', '/api/employees/bulk-upload/validate', () => ({ data: preview({
      totalRows: 2,
      validRows: 1,
      invalidRows: 1,
      rows: [
        { rowNumber: 2, status: 'Valid', issues: [] },
        { rowNumber: 3, status: 'Invalid', issues: [{
          fieldName: 'Department Code', columnName: 'Department Code *', columnNumber: 8,
          errorCode: 'master.invalid', message: 'Department code is unavailable.', guidance: 'Use an active code from the Departments reference sheet.',
        }] },
      ],
    }) }))
    renderGuarded()
    await userEvent.upload(screen.getByLabelText(/Choose or drop an Excel workbook/i), new File(['workbook'], 'employees.xlsx'))
    await userEvent.click(screen.getByRole('button', { name: 'Validate Workbook' }))
    expect(await screen.findByRole('table', { name: /validation errors/i })).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
    expect(screen.getByText('Department Code * (8)')).toBeInTheDocument()
    expect(screen.getByText('Use an active code from the Departments reference sheet.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Prepare Import' })).toBeDisabled()
    expect(stub.callsTo('post', '/api/employees/bulk-upload/prepare')).toHaveLength(0)
  })

  it('validates, prepares, keeps the token only in memory and makes no employee creation request', async () => {
    stub.on('post', '/api/employees/bulk-upload/validate', () => ({ data: preview() }))
    stub.on('post', '/api/employees/bulk-upload/prepare', () => ({ data: ok({
      operationId: 'ad42beef-0000-4000-8000-000000000001',
      confirmationToken: 'one-use-secret-token',
      totalRows: 1,
      summary: 'Prepared 1 valid employee row.',
    }) }))
    renderGuarded()
    await userEvent.upload(screen.getByLabelText(/Choose or drop an Excel workbook/i), new File(['workbook'], 'employees.xlsx'))
    await userEvent.click(screen.getByRole('button', { name: 'Validate Workbook' }))
    expect(await screen.findByText(/All 1 row passed validation/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Prepare Import' }))
    expect(await screen.findByRole('heading', { name: 'Import prepared' })).toBeInTheDocument()
    expect(screen.getByText(/No employee records have been created/)).toBeInTheDocument()
    expect(screen.getByText('Prepared — awaiting confirmation')).toBeInTheDocument()
    expect(screen.getByText(/held only in this page’s memory/)).toBeInTheDocument()
    expect(document.body).not.toHaveTextContent('one-use-secret-token')
    expect(sessionStorage.length).toBe(0)
    const storedValues = Object.keys(localStorage).map(key => localStorage.getItem(key) ?? '').join('\n')
    expect(storedValues).not.toContain('one-use-secret-token')
    expect(stub.callsTo('post', '/api/employees/bulk-upload/prepare')).toHaveLength(1)
    expect(stub.calls.some(call => call.url.startsWith('/api/employees/') && call.url !== '/api/employees/bulk-upload/prepare' && call.url !== '/api/employees/bulk-upload/validate' && call.url !== '/api/employees/bulk-upload/history')).toBe(false)
  })

  it('shows preparation failure without claiming employee creation', async () => {
    stub.on('post', '/api/employees/bulk-upload/validate', () => ({ data: preview() }))
    stub.on('post', '/api/employees/bulk-upload/prepare', () => ({ status: 409, data: fail('Workbook storage is unavailable.') }))
    renderGuarded()
    await userEvent.upload(screen.getByLabelText(/Choose or drop an Excel workbook/i), new File(['workbook'], 'employees.xlsx'))
    await userEvent.click(screen.getByRole('button', { name: 'Validate Workbook' }))
    await screen.findByText(/All 1 row passed validation/)
    await userEvent.click(screen.getByRole('button', { name: 'Prepare Import' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Workbook storage is unavailable.')
    expect(screen.queryByRole('heading', { name: 'Import prepared' })).not.toBeInTheDocument()
    expect(screen.queryByText(/employees were created/i)).not.toBeInTheDocument()
  })

  it('offers navigation to the existing queued operation when preparation detects a duplicate workbook', async () => {
    const operationId = 'b89d04aa-1419-4c85-a7e3-48ab1d92d111'
    stub.on('post', '/api/employees/bulk-upload/validate', () => ({ data: preview() }))
    stub.on('post', '/api/employees/bulk-upload/prepare', () => ({
      status: 409,
      data: fail('This workbook already has an active or accepted import operation.', [
        { field: 'existingOperationId', message: operationId },
      ]),
    }))
    stub.on('get', `/api/employees/bulk-upload/${operationId}`, () => ({ data: ok({
      operationId, state: 'Queued', totalRows: 10_000, succeededRows: 0, failedRows: 0, skippedRows: 0,
      startedAtUtc: null, completedAtUtc: null, lastUpdatedAtUtc: '2026-10-30T17:05:54Z', summary: null, rows: [],
    }) }))
    renderGuarded()
    await userEvent.upload(screen.getByLabelText(/Choose or drop an Excel workbook/i), new File(['workbook'], 'employees.xlsx'))
    await userEvent.click(screen.getByRole('button', { name: 'Validate Workbook' }))
    await screen.findByText(/All 1 row passed validation/)
    await userEvent.click(screen.getByRole('button', { name: 'Prepare Import' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('This workbook already has an import operation.')
    await userEvent.click(screen.getByRole('button', { name: 'Open existing import results' }))
    expect(await screen.findByRole('heading', { name: 'Import progress' })).toBeInTheDocument()
    expect(await screen.findByText('Queued')).toBeInTheDocument()
    expect(stub.callsTo('post', '/api/employees/bulk-upload/prepare')).toHaveLength(1)
    expect(stub.callsTo('get', `/api/employees/bulk-upload/${operationId}`)).toHaveLength(1)
  })

  it('reopens a queued operation from its URL after a page refresh', async () => {
    const operationId = 'b89d04aa-1419-4c85-a7e3-48ab1d92d111'
    stub.on('get', `/api/employees/bulk-upload/${operationId}`, () => ({ data: ok({
      operationId, state: 'Queued', totalRows: 10_000, succeededRows: 0, failedRows: 0, skippedRows: 0,
      startedAtUtc: null, completedAtUtc: null, lastUpdatedAtUtc: '2026-10-30T17:05:54Z', summary: null, rows: [],
    }) }))

    renderAsUser(<Routes><Route path="/employees/bulk-upload/:operationId" element={<EmployeeBulkUploadPage />} /></Routes>, {
      route: `/employees/bulk-upload/${operationId}`, user: makeUser({ permissions: fullPermissions }),
    })

    expect(await screen.findByRole('heading', { name: 'Import progress' })).toBeInTheDocument()
    expect(await screen.findByText('Queued')).toBeInTheDocument()
    expect(screen.getAllByText('10000')).toHaveLength(2)
    expect(stub.callsTo('get', `/api/employees/bulk-upload/${operationId}`)).toHaveLength(1)
  })

  it('allows an Employee.Import user to view results without upload permissions', async () => {
    const operationId = 'b89d04aa-1419-4c85-a7e3-48ab1d92d111'
    stub.on('get', `/api/employees/bulk-upload/${operationId}`, () => ({ data: ok({
      operationId, state: 'Queued', totalRows: 10_000, succeededRows: 0, failedRows: 0, skippedRows: 0,
      startedAtUtc: null, completedAtUtc: null, lastUpdatedAtUtc: '2026-10-30T17:05:54Z', summary: null, rows: [],
    }) }))

    renderAsUser(<Routes><Route path="/employees/bulk-upload/:operationId" element={
      <RequirePermission permission={Permissions.employee.import}><EmployeeBulkUploadPage readOnly /></RequirePermission>
    } /></Routes>, {
      route: `/employees/bulk-upload/${operationId}`, user: makeUser({ permissions: [Permissions.employee.import] }),
    })

    expect(await screen.findByText('Queued')).toBeInTheDocument()
    expect(screen.queryByLabelText(/Choose or drop an Excel workbook/i)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Confirm and enqueue' })).not.toBeInTheDocument()
  })

  it('shows an authorization failure when the operation status endpoint rejects access', async () => {
    const operationId = 'b89d04aa-1419-4c85-a7e3-48ab1d92d111'
    stub.on('get', `/api/employees/bulk-upload/${operationId}`, () => ({
      status: 403,
      data: fail('You do not have permission to perform this action.'),
    }))

    renderAsUser(<Routes><Route path="/employees/bulk-upload/:operationId" element={<EmployeeBulkUploadPage />} /></Routes>, {
      route: `/employees/bulk-upload/${operationId}`, user: makeUser({ permissions: fullPermissions }),
    })

    expect(await screen.findByRole('alert')).toHaveTextContent('You do not have permission to perform this action.')
    expect(stub.callsTo('get', `/api/employees/bulk-upload/${operationId}`)).toHaveLength(1)
  })

  it('requires an explicit confirmation and handles HTTP 202 without claiming completion', async () => {
    const operationId = 'ad42beef-0000-4000-8000-000000000001'
    stub.on('post', '/api/employees/bulk-upload/validate', () => ({ data: preview() }))
    stub.on('post', '/api/employees/bulk-upload/prepare', () => ({ data: ok({ operationId, confirmationToken: 'secret-once', totalRows: 1, summary: 'Prepared 1 employee row.' }) }))
    stub.on('post', '/api/employees/bulk-upload/confirm', call => {
      expect(call.body).toEqual({ operationId, confirmationToken: 'secret-once', confirm: true })
      return { status: 202, delay: true, data: ok({ operationId, state: 'Queued', statusUrl: `/api/employees/bulk-upload/${operationId}` }) }
    })
    stub.on('get', `/api/employees/bulk-upload/${operationId}`, () => ({ data: ok({
      operationId, state: 'Queued', totalRows: 1, succeededRows: 0, failedRows: 0, skippedRows: 0,
      startedAtUtc: null, completedAtUtc: null, lastUpdatedAtUtc: '2026-10-28T10:00:00Z', summary: null, rows: [],
    }) }))
    renderGuarded()
    await userEvent.upload(screen.getByLabelText(/Choose or drop an Excel workbook/i), new File(['workbook'], 'employees.xlsx'))
    await userEvent.click(screen.getByRole('button', { name: 'Validate Workbook' }))
    await screen.findByText(/All 1 row passed validation/)
    await userEvent.click(screen.getByRole('button', { name: 'Prepare Import' }))
    await screen.findByRole('heading', { name: 'Import prepared' })
    expect(stub.callsTo('post', '/api/employees/bulk-upload/confirm')).toHaveLength(0)
    await userEvent.click(screen.getByRole('button', { name: 'Review confirmation' }))
    expect(screen.getByRole('alertdialog')).toBeInTheDocument()
    const confirmButton = screen.getByRole('button', { name: 'Confirm and enqueue' })
    await userEvent.click(confirmButton)
    await userEvent.click(confirmButton)
    expect(await screen.findByRole('heading', { name: 'Import progress' })).toBeInTheDocument()
    expect(await screen.findByText('Queued')).toBeInTheDocument()
    expect(stub.callsTo('post', '/api/employees/bulk-upload/confirm')).toHaveLength(1)
    expect(document.body).not.toHaveTextContent('secret-once')
    expect(document.body).not.toHaveTextContent(/employees have been created/i)
    expect(sessionStorage.length).toBe(0)
  })

  it('shows terminal partial results and keeps successful rows distinct from failures', async () => {
    const operationId = 'bc42beef-0000-4000-8000-000000000002'
    stub.on('get', `/api/employees/bulk-upload/${operationId}`, () => ({ data: ok({
      operationId, state: 'PartiallyCompleted', totalRows: 3, succeededRows: 1, failedRows: 1, skippedRows: 1,
      startedAtUtc: '2026-10-28T10:00:00Z', completedAtUtc: '2026-10-28T10:02:00Z', lastUpdatedAtUtc: '2026-10-28T10:02:00Z', summary: 'Processing finished.',
      rows: [{ excelRowNumber: 4, status: 'Failed', employeeCode: null, errorCode: 'employee.create_failed', message: 'Aadhaar 123456789012 could not be accepted.' }],
    }) }))
    renderAsUser(<Routes><Route path="/employees/bulk-upload/:operationId" element={<EmployeeBulkUploadPage />} /></Routes>, {
      route: `/employees/bulk-upload/${operationId}`, user: makeUser({ permissions: fullPermissions }),
    })
    expect(await screen.findByText('PartiallyCompleted')).toBeInTheDocument()
    expect(screen.getByText('Successful rows remain created. Correct failed rows and prepare a new workbook; there is no blind retry for completed rows.')).toBeInTheDocument()
    expect(screen.getAllByText(/Aadhaar/).length).toBeGreaterThan(1)
    expect(document.body).not.toHaveTextContent('123456789012')
    expect(stub.callsTo('get', `/api/employees/bulk-upload/${operationId}`)).toHaveLength(1)
  })

  it('reopens accepted operations by operation ID and renders paginated tenant history', async () => {
    const operationId = 'cd42beef-0000-4000-8000-000000000003'
    stub.on('get', '/api/employees/bulk-upload/history', call => ({ data: ok({
      items: [{ operationId, state: 'Completed', totalRows: 2, succeededRows: 2, failedRows: 0, skippedRows: 0,
        preparedAtUtc: '2026-10-27T10:00:00Z', completedAtUtc: '2026-10-27T10:01:00Z', summary: 'Completed.' }],
      page: Number(call.params.page), pageSize: 10, totalCount: 11, totalPages: 2,
    }) }))
    stub.on('get', `/api/employees/bulk-upload/${operationId}`, () => ({ data: ok({
      operationId, state: 'Completed', totalRows: 2, succeededRows: 2, failedRows: 0, skippedRows: 0,
      startedAtUtc: '2026-10-27T10:00:00Z', completedAtUtc: '2026-10-27T10:01:00Z', lastUpdatedAtUtc: '2026-10-27T10:01:00Z', summary: null, rows: [],
    }) }))
    renderGuarded()
    expect(await screen.findByRole('heading', { name: 'Import history' })).toBeInTheDocument()
    await userEvent.click(await screen.findByRole('button', { name: 'Next' }))
    await waitFor(() => expect(stub.callsTo('get', '/api/employees/bulk-upload/history')).toHaveLength(2))
    await userEvent.click(await screen.findByRole('button', { name: 'Open results' }))
    expect(await screen.findByRole('heading', { name: 'Import progress' })).toBeInTheDocument()
    await waitFor(() => expect(stub.callsTo('get', `/api/employees/bulk-upload/${operationId}`)).toHaveLength(1))
    expect(stub.callsTo('get', `/api/employees/bulk-upload/${operationId}`)).toHaveLength(1)
  })

  it('stops polling after a failed terminal operation', async () => {
    const operationId = 'de42beef-0000-4000-8000-000000000004'
    stub.on('get', `/api/employees/bulk-upload/${operationId}`, () => ({ data: ok({
      operationId, state: 'Failed', totalRows: 2, succeededRows: 0, failedRows: 1, skippedRows: 1,
      startedAtUtc: '2026-10-28T10:00:00Z', completedAtUtc: '2026-10-28T10:01:00Z', lastUpdatedAtUtc: '2026-10-28T10:01:00Z',
      summary: 'Import failed.', rows: [{ excelRowNumber: 2, status: 'Failed', employeeCode: null,
        errorCode: 'employee.create_failed', message: 'Correct the row and prepare a new workbook.' }],
    }) }))
    renderAsUser(<Routes><Route path="/employees/bulk-upload/:operationId" element={<EmployeeBulkUploadPage />} /></Routes>, {
      route: `/employees/bulk-upload/${operationId}`, user: makeUser({ permissions: fullPermissions }),
    })
    expect((await screen.findAllByText('Failed')).length).toBeGreaterThan(1)
    await new Promise(resolve => window.setTimeout(resolve, 50))
    expect(stub.callsTo('get', `/api/employees/bulk-upload/${operationId}`)).toHaveLength(1)
  })

  it('cancels status polling when the page unmounts', async () => {
    const operationId = 'ef42beef-0000-4000-8000-000000000005'
    stub.on('get', `/api/employees/bulk-upload/${operationId}`, () => ({ delay: true, data: ok({
      operationId, state: 'Processing', totalRows: 1, succeededRows: 0, failedRows: 0, skippedRows: 0,
      startedAtUtc: '2026-10-28T10:00:00Z', completedAtUtc: null, lastUpdatedAtUtc: '2026-10-28T10:00:00Z',
      summary: null, rows: [],
    }) }))
    const view = renderAsUser(<Routes><Route path="/employees/bulk-upload/:operationId" element={<EmployeeBulkUploadPage />} /></Routes>, {
      route: `/employees/bulk-upload/${operationId}`, user: makeUser({ permissions: fullPermissions }),
    })
    await waitFor(() => expect(stub.callsTo('get', `/api/employees/bulk-upload/${operationId}`)).toHaveLength(1))
    view.unmount()
    await new Promise(resolve => window.setTimeout(resolve, 50))
    expect(stub.callsTo('get', `/api/employees/bulk-upload/${operationId}`)).toHaveLength(1)
  })

  it('shows token replay errors without retrying or exposing the token', async () => {
    stub.on('post', '/api/employees/bulk-upload/validate', () => ({ data: preview() }))
    stub.on('post', '/api/employees/bulk-upload/prepare', () => ({ data: ok({ operationId: 'ad42beef-0000-4000-8000-000000000001', confirmationToken: 'secret-once', totalRows: 1, summary: 'Prepared.' }) }))
    stub.on('post', '/api/employees/bulk-upload/confirm', () => ({ status: 409, data: fail('Already claimed.') }))
    renderGuarded()
    await userEvent.upload(screen.getByLabelText(/Choose or drop an Excel workbook/i), new File(['workbook'], 'employees.xlsx'))
    await userEvent.click(screen.getByRole('button', { name: 'Validate Workbook' }))
    await screen.findByText(/All 1 row passed validation/)
    await userEvent.click(screen.getByRole('button', { name: 'Prepare Import' }))
    await screen.findByRole('heading', { name: 'Import prepared' })
    await userEvent.click(screen.getByRole('button', { name: 'Review confirmation' }))
    await userEvent.click(screen.getByRole('button', { name: 'Confirm and enqueue' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/may already have been consumed/)
    expect(document.body).not.toHaveTextContent('secret-once')
    expect(stub.callsTo('post', '/api/employees/bulk-upload/confirm')).toHaveLength(1)
  })

  it('masks sensitive identifiers and contact details in server-provided messages', () => {
    const safe = sanitizeBulkUploadText('Aadhaar 999988887777 PAN ABCDE1234F phone +91 98765 43210 email a.person@example.test')
    expect(safe).not.toContain('999988887777')
    expect(safe).not.toContain('ABCDE1234F')
    expect(safe).not.toContain('98765 43210')
    expect(safe).not.toContain('a.person@example.test')
    expect(safe).toContain('[email hidden]')
  })
})
