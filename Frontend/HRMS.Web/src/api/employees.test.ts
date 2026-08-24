import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { exportEmployees, fileNameFromContentDisposition, saveFile } from './employees.ts'
import { session } from './session.ts'
import { fail, installStubAdapter, type StubAdapter } from '../test/stubAdapter.ts'

describe('content-disposition parsing', () => {
  it('prefers the RFC 6266 encoded form', () => {
    expect(
      fileNameFromContentDisposition(
        "attachment; filename=employees.csv; filename*=UTF-8''employees-2026-08-22.csv",
      ),
    ).toBe('employees-2026-08-22.csv')
  })

  it('decodes percent-encoding in the encoded form', () => {
    expect(
      fileNameFromContentDisposition("attachment; filename*=UTF-8''Nordwind%20Gmb%C3%9C.csv"),
    ).toBe('Nordwind GmbÜ.csv')
  })

  it('falls back to the quoted plain form', () => {
    expect(fileNameFromContentDisposition('attachment; filename="employees.csv"')).toBe(
      'employees.csv',
    )
  })

  it('returns null when the header is missing or not a string', () => {
    // The realistic case: the API forgot `WithExposedHeaders`, so CORS hides the header from us.
    expect(fileNameFromContentDisposition(undefined)).toBeNull()
    expect(fileNameFromContentDisposition(123)).toBeNull()
    expect(fileNameFromContentDisposition('attachment')).toBeNull()
  })
})

describe('employee export', () => {
  let stub: StubAdapter

  beforeEach(() => {
    session.save({ accessToken: 'access-1', refreshToken: 'refresh-1' })
    stub = installStubAdapter()
  })

  afterEach(() => {
    stub.restore()
    session.clear()
  })

  it('returns the file with the name the API chose', async () => {
    stub.on('get', '/api/employees/export', () => ({
      data: new Blob(['code,name\r\nEMP-001,Nadia Farrell\r\n'], { type: 'text/csv' }),
      headers: { 'content-disposition': 'attachment; filename="employees-2026-08-22.csv"' },
    }))

    const file = await exportEmployees({ status: 'Active' })

    expect(file.fileName).toBe('employees-2026-08-22.csv')
    expect(await file.blob.text()).toContain('EMP-001')
    expect(stub.calls[0]?.params).toEqual({ status: 'Active' })
  })

  it('names the file sensibly when the header is not exposed', async () => {
    stub.on('get', '/api/employees/export', () => ({ data: new Blob(['a,b'], { type: 'text/csv' }) }))

    expect((await exportEmployees()).fileName).toBe('employees.csv')
  })

  it('reads the error message out of a JSON body delivered as a blob', async () => {
    // With `responseType: 'blob'`, a refusal arrives as a Blob containing our envelope. Without
    // decoding it the user would get "the request failed" instead of what to do about it.
    stub.on('get', '/api/employees/export', () => ({
      status: 400,
      data: new Blob(
        [JSON.stringify(fail('The export is limited to 10,000 rows. Narrow the filters.'))],
        { type: 'application/json' },
      ),
    }))

    await expect(exportEmployees()).rejects.toMatchObject({
      status: 400,
      message: 'The export is limited to 10,000 rows. Narrow the filters.',
    })
  })

  it('still reports a plain failure when the body is not our envelope', async () => {
    stub.on('get', '/api/employees/export', () => ({
      status: 403,
      data: new Blob(['<html>Forbidden</html>'], { type: 'text/html' }),
    }))

    await expect(exportEmployees()).rejects.toMatchObject({
      status: 403,
      message: 'You do not have permission to do that.',
    })
  })
})

describe('saving a downloaded file', () => {
  it('revokes the object URL it created', () => {
    // jsdom implements neither `URL.createObjectURL` nor `revokeObjectURL`, so they are defined here
    // rather than spied on, and removed again afterwards.
    const createObjectURL = vi.fn(() => 'blob:hrms/1')
    const revokeObjectURL = vi.fn()
    const original = {
      create: Object.getOwnPropertyDescriptor(URL, 'createObjectURL'),
      revoke: Object.getOwnPropertyDescriptor(URL, 'revokeObjectURL'),
    }
    Object.defineProperty(URL, 'createObjectURL', { value: createObjectURL, configurable: true })
    Object.defineProperty(URL, 'revokeObjectURL', { value: revokeObjectURL, configurable: true })
    // A click on an anchor is navigation, which jsdom does not implement.
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined)

    try {
      const blob = new Blob(['a,b'], { type: 'text/csv' })
      saveFile({ blob, fileName: 'employees.csv' })

      expect(createObjectURL).toHaveBeenCalledWith(blob)
      expect(click).toHaveBeenCalledTimes(1)
      // Skipping this leaks the whole file for the lifetime of the tab.
      expect(revokeObjectURL).toHaveBeenCalledWith('blob:hrms/1')
      expect(document.querySelector('a[download]')).toBeNull()
    } finally {
      click.mockRestore()
      restore(URL, 'createObjectURL', original.create)
      restore(URL, 'revokeObjectURL', original.revoke)
    }
  })
})

function restore(target: object, property: string, descriptor: PropertyDescriptor | undefined): void {
  if (descriptor) {
    Object.defineProperty(target, property, descriptor)
  } else {
    delete (target as Record<string, unknown>)[property]
  }
}

